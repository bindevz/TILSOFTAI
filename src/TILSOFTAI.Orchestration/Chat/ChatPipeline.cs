using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Orchestration.Chat.Localization;
using TILSOFTAI.Orchestration.Llm;
using TILSOFTAI.Orchestration.Llm.OpenAi;
using TILSOFTAI.Orchestration.Formatting;
using TILSOFTAI.Orchestration.SK;
using TILSOFTAI.Orchestration.SK.Conversation;
using TILSOFTAI.Orchestration.Tools;

namespace TILSOFTAI.Orchestration.Chat;

/// <summary>
/// Manual tool-calling loop (Mode B):
/// - Call LLM with OpenAI "tools" schema
/// - Execute tool calls via ToolInvoker
/// - Append tool results back to the conversation
/// - Compose final Markdown response server-side
/// </summary>
public sealed class ChatPipeline
{
    private static readonly HashSet<string> SupportedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "user", "assistant", "tool"
    };

    private readonly OpenAiChatClient _chat;
    private readonly OpenAiToolSchemaFactory _toolSchemaFactory;
    private readonly ToolInvoker _toolInvoker;
    private readonly ExecutionContextAccessor _ctxAccessor;
    private readonly TokenBudget _tokenBudget;
    private readonly IAuditLogger _auditLogger;
    private readonly IConversationStateStore _conversationState;
    private readonly ILanguageResolver _languageResolver;
    private readonly IChatTextLocalizer _localizer;
    private readonly ChatTextPatterns _patterns;
    private readonly ChatTuningOptions _tuning;
    private readonly ILogger<ChatPipeline> _logger;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    // Tool allowlist exposed to LLM for this Orchestrator (single source of truth).
    private static readonly IReadOnlySet<string> ExposedTools = ToolExposurePolicy.ModeBAllowedTools;

    public ChatPipeline(
        OpenAiChatClient chat,
        OpenAiToolSchemaFactory toolSchemaFactory,
        ToolInvoker toolInvoker,
        ExecutionContextAccessor ctxAccessor,
        TokenBudget tokenBudget,
        IAuditLogger auditLogger,
        IConversationStateStore conversationState,
        ILanguageResolver languageResolver,
        IChatTextLocalizer localizer,
        ChatTextPatterns patterns,
        ChatTuningOptions tuning,
        ILogger<ChatPipeline>? logger = null)
    {
        _chat = chat;
        _toolSchemaFactory = toolSchemaFactory;
        _toolInvoker = toolInvoker;
        _ctxAccessor = ctxAccessor;
        _tokenBudget = tokenBudget;
        _auditLogger = auditLogger;
        _conversationState = conversationState;
        _languageResolver = languageResolver;
        _localizer = localizer;
        _patterns = patterns;
        _tuning = tuning;
        _logger = logger ?? NullLogger<ChatPipeline>.Instance;
    }

    public async Task<ChatCompletionResponse> HandleAsync(ChatCompletionRequest request, TILSOFTAI.Domain.ValueObjects.TSExecutionContext context, CancellationToken cancellationToken)
    {
        var incomingMessages = request.Messages ?? Array.Empty<ChatCompletionMessage>();

        if (incomingMessages.Any(m => !SupportedRoles.Contains(m.Role)))
            return BuildFailureResponse("Unsupported message role.", request.Model, incomingMessages);

        var hasUser = incomingMessages.Any(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(m.Content));
        if (!hasUser)
            return BuildFailureResponse("User message required.", request.Model, incomingMessages);

        var lastUserMessage = incomingMessages.Last(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(m.Content)).Content;

        // Audit: user input (best-effort)
        try
        {
            await _auditLogger.LogUserInputAsync(context, lastUserMessage, cancellationToken);
        }
        catch { /* ignore */ }


        // Conversation state + reset filters
        var conversationState = await _conversationState.TryGetAsync(context, cancellationToken);
        if (_patterns.IsResetFiltersIntent(lastUserMessage))
        {
            await _conversationState.ClearAsync(context, cancellationToken);
            conversationState = null;
        }

        // Resolve response language and persist.
        var lang = _languageResolver.Resolve(incomingMessages, conversationState);
        conversationState ??= new ConversationState();
        if (!string.Equals(conversationState.PreferredLanguage, lang.ToIsoCode(), StringComparison.OrdinalIgnoreCase))
        {
            conversationState.PreferredLanguage = lang.ToIsoCode();
            await _conversationState.UpsertAsync(context, conversationState, cancellationToken);
        }

        // Set request context for ToolInvoker
        _ctxAccessor.Context = context;
        _ctxAccessor.ConfirmedConfirmationId = _patterns.TryExtractConfirmationId(lastUserMessage);
        _ctxAccessor.AutoInvokeCount = 0;
        _ctxAccessor.CircuitBreakerTripped = false;
        _ctxAccessor.CircuitBreakerReason = null;
        _ctxAccessor.AutoInvokeSignatureCounts.Clear();
        _ctxAccessor.LastTotalCount = null;
        _ctxAccessor.LastStoredProcedure = null;
        _ctxAccessor.LastFilters = null;
        _ctxAccessor.LastSeasonFilter = null;
        _ctxAccessor.LastCollectionFilter = null;
        _ctxAccessor.LastRangeNameFilter = null;
        _ctxAccessor.LastDisplayPreviewJson = null;
        _ctxAccessor.LastListPreviewMarkdown = null;
        _ctxAccessor.LastInsightPreviewMarkdown = null;
        _ctxAccessor.LastSchemaDigestJson = null;
        _ctxAccessor.LastEngineDatasetsDigestJson = null;
        _ctxAccessor.LastListPreviewTitle = null;
        _ctxAccessor.LastInsightPreviewTitle = null;

        _logger.LogInformation("ChatPipeline start requestId={RequestId} traceId={TraceId} convId={ConversationId} userId={UserId} lang={Lang} model={Model} msgs={MsgCount}",
            context.RequestId, context.TraceId, context.ConversationId, context.UserId, lang.ToIsoCode(), request.Model, incomingMessages.Count);

        // Build OpenAI messages
        var systemPrompt = BuildSystemPrompt(lang, conversationState);
        var messages = new List<OpenAiChatMessage>
        {
            new() { Role = "system", Content = systemPrompt }
        };

        foreach (var m in incomingMessages)
        {
            // Do not allow client to override system prompt.
            if (string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
                continue;

            messages.Add(new OpenAiChatMessage { Role = m.Role, Content = m.Content });
        }

        // Build tools
        var tools = _toolSchemaFactory.BuildTools(ExposedTools);

        // Planner loop
        var mappedModel = _chat.MapModel(request.Model);
        var maxSteps = Math.Clamp(_tuning.MaxToolSteps, 1, 20);

        OpenAiChatCompletionResponse? lastResp = null;

        for (var step = 0; step < maxSteps; step++)
        {
            TrimMessagesToBudget(messages, _tuning.MaxPromptTokens);

            var call = new OpenAiChatCompletionRequest
            {
                Model = mappedModel,
                Messages = messages,
                Tools = tools.ToList(),
                ToolChoice = "auto",
                Temperature = request.Temperature ?? _tuning.Temperature,
                MaxTokens = request.MaxTokens ?? _tuning.MaxTokens,
                Stream = false
            };

            try
            {
                lastResp = await _chat.CreateChatCompletionAsync(call, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                var timeoutInsight = lang == ChatLanguage.En
                    ? "The request timed out (504)."
                    : "Yeu cau bi qua thoi gian xu ly (504).";

                var timeoutFinal = ComposeFinalMarkdown(timeoutInsight, _ctxAccessor);
                return MapToApiResponse(lastResp, request.Model ?? "TILSOFT-AI", timeoutFinal, incomingMessages);
            }
            catch (TimeoutException)
            {
                var timeoutInsight = lang == ChatLanguage.En
                    ? "The request timed out (504)."
                    : "Yeu cau bi qua thoi gian xu ly (504).";

                var timeoutFinal = ComposeFinalMarkdown(timeoutInsight, _ctxAccessor);
                return MapToApiResponse(lastResp, request.Model ?? "TILSOFT-AI", timeoutFinal, incomingMessages);
            }

            var choice = lastResp.Choices.FirstOrDefault();
            if (choice?.Message is null)
                break;

            var assistantMsg = choice.Message;
            messages.Add(assistantMsg);

            // No tool calls -> final insight
            if (assistantMsg.ToolCalls is null || assistantMsg.ToolCalls.Count == 0)
            {
                var insight = SanitizeInsightText(assistantMsg.Content);
                if (string.IsNullOrWhiteSpace(insight))
                    insight = _localizer.Get(ChatTextKeys.FallbackNoContent, lang);

                var final = ComposeFinalMarkdown(insight, _ctxAccessor);

                // Audit: AI output (best-effort)
                try
                {
                    await _auditLogger.LogAiDecisionAsync(context, final, cancellationToken);
                }
                catch { /* ignore */ }

                return MapToApiResponse(lastResp, request.Model ?? "TILSOFT-AI", final, incomingMessages);
            }

            // Execute tools
            foreach (var tc in assistantMsg.ToolCalls)
            {
                var toolName = tc.Function.Name;
                var argsJson = tc.Function.Arguments ?? "{}";

                // Circuit breaker by signature
                var signature = ComputeSignature(toolName, argsJson);
                _ctxAccessor.AutoInvokeSignatureCounts.TryGetValue(signature, out var count);
                count++;
                _ctxAccessor.AutoInvokeSignatureCounts[signature] = count;
                if (count > 2)
                {
                    _ctxAccessor.CircuitBreakerTripped = true;
                    _ctxAccessor.CircuitBreakerReason = $"Repeated tool call signature: {toolName}";

                    var forced = ComposeFinalMarkdown(_localizer.Get(ChatTextKeys.FallbackNoContent, lang), _ctxAccessor);
                    return MapToApiResponse(lastResp, request.Model ?? "TILSOFT-AI", forced, incomingMessages);
                }

                JsonElement argsElement;
                try
                {
                    using var doc = JsonDocument.Parse(argsJson);
                    argsElement = doc.RootElement.Clone();
                }
                catch
                {
                    using var doc = JsonDocument.Parse("{}");
                    argsElement = doc.RootElement.Clone();
                }

                var toolResult = await _toolInvoker.ExecuteAsync(toolName, argsElement, ExposedTools, cancellationToken);
                var toolResultJson = JsonSerializer.Serialize(toolResult, _json);

                TryCaptureToolArtifacts(toolResultJson);

                var compactJson = ToolResultCompactor.CompactEnvelopeJson(toolResultJson, ResolveMaxToolResultBytes());
                messages.Add(new OpenAiChatMessage
                {
                    Role = "tool",
                    ToolCallId = tc.Id,
                    Content = compactJson
                });

                TrimMessagesToBudget(messages, _tuning.MaxPromptTokens);
            }
        }

        var fallbackFinal = ComposeFinalMarkdown(_localizer.Get(ChatTextKeys.FallbackNoContent, lang), _ctxAccessor);
        return MapToApiResponse(lastResp, request.Model ?? "TILSOFT-AI", fallbackFinal, incomingMessages);
    }
    private string BuildSystemPrompt(ChatLanguage lang, ConversationState? state)
    {
        var basePrompt = _localizer.Get(ChatTextKeys.SystemPrompt, lang);

        // Provide prior query hint to help short follow-ups.
        if (state?.LastQuery is not null)
        {
            var hint = _localizer.Get(ChatTextKeys.PreviousQueryHint, lang);
            basePrompt += "\n\n" + hint + JsonSerializer.Serialize(state.LastQuery, _json);
        }

        var outputRule = lang == ChatLanguage.En
            ? "OUTPUT: Return only plain insight text. Do NOT include Markdown headings, tables, lists, or JSON."
            : "OUTPUT: Chi tra loi phan insight ngan gon. KHONG dung tieu de Markdown, bang, danh sach hoac JSON.";

        var toolRule = lang == ChatLanguage.En
            ? "Tool rule: If you need to choose a stored procedure by domain/context, call atomic.catalog.search first, then atomic.query.execute. If you need deeper analysis (group/pivot/sum/avg/topN), use analytics.run with datasetId from atomic.query.execute."
            : "Tool rule: Neu can chon stored procedure theo domain/ngu canh, luon goi atomic.catalog.search truoc, sau do atomic.query.execute. Neu can phan tich sau (group/pivot/sum/avg/topN), dung analytics.run voi datasetId tu atomic.query.execute.";

        basePrompt += "\n\n" + outputRule;
        basePrompt += "\n\n" + toolRule;

        return basePrompt;
    }
    private static void TrimMessagesToBudget(List<OpenAiChatMessage> messages, int maxPromptTokens)
    {
        if (messages.Count == 0)
            return;

        var limit = Math.Max(maxPromptTokens, 1000);
        while (EstimatePromptTokens(messages) > limit && messages.Count > 1)
        {
            var toolIndex = messages.FindIndex(m => string.Equals(m.Role, "tool", StringComparison.OrdinalIgnoreCase));
            if (toolIndex > 0)
            {
                messages.RemoveAt(toolIndex);
                continue;
            }

            var keepFrom = Math.Max(1, messages.Count - 8);
            if (messages.Count > keepFrom)
            {
                messages.RemoveAt(1);
                continue;
            }

            if (messages.Count > 1)
                messages.RemoveAt(1);
            else
                break;
        }
    }

    private static int EstimatePromptTokens(IEnumerable<OpenAiChatMessage> messages)
    {
        var total = 0;
        foreach (var m in messages)
        {
            var len = m.Content?.Length ?? 0;
            total += len / 4;
        }
        return total;
    }
    private static string ComposeFinalMarkdown(string insightText, ExecutionContextAccessor ctx)
    {
        var emptyNote = "(kh\u00f4ng c\u00f3)";
        var insight = string.IsNullOrWhiteSpace(insightText) ? emptyNote : insightText.Trim();
        var insightPreview = string.IsNullOrWhiteSpace(ctx.LastInsightPreviewMarkdown) ? emptyNote : ctx.LastInsightPreviewMarkdown;
        var listPreview = string.IsNullOrWhiteSpace(ctx.LastListPreviewMarkdown) ? emptyNote : ctx.LastListPreviewMarkdown;

        var sb = new StringBuilder();
        sb.AppendLine("## K\u1ebft lu\u1eadn / Insight");
        sb.AppendLine(insight);
        sb.AppendLine();
        sb.AppendLine("## Preview d\u1eef li\u1ec7u c\u1ee7a K\u1ebft lu\u1eadn / Insight");
        if (!string.IsNullOrWhiteSpace(ctx.LastInsightPreviewTitle))
            sb.AppendLine($"_{ctx.LastInsightPreviewTitle}_");
        sb.AppendLine(insightPreview);
        sb.AppendLine();
        sb.AppendLine("## Preview danh s\u00e1ch");
        if (!string.IsNullOrWhiteSpace(ctx.LastListPreviewTitle))
            sb.AppendLine($"_{ctx.LastListPreviewTitle}_");
        sb.AppendLine(listPreview);
        return sb.ToString().TrimEnd();
    }

    private static string SanitizeInsightText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var result = new List<string>();
        var started = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                if (started) break;
                continue;
            }

            if (IsHeadingLine(trimmed) || IsTableLine(trimmed))
            {
                if (started) break;
                continue;
            }

            result.Add(line.TrimEnd());
            started = true;
        }

        return string.Join("\n", result).Trim();
    }

    private static bool IsHeadingLine(string trimmed)
        => trimmed.StartsWith("#", StringComparison.Ordinal);

    private static bool IsTableLine(string trimmed)
    {
        if (trimmed.StartsWith("|", StringComparison.Ordinal) || trimmed.EndsWith("|", StringComparison.Ordinal))
            return true;
        return trimmed.Contains("|", StringComparison.Ordinal) && trimmed.Contains("---", StringComparison.Ordinal);
    }

    private void TryCaptureToolArtifacts(string envelopeJson)
    {
        if (string.IsNullOrWhiteSpace(envelopeJson))
            return;

        try
        {
            using var doc = JsonDocument.Parse(envelopeJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("tool", out var toolEl) || !toolEl.TryGetProperty("name", out var nameEl))
                return;

            var toolName = nameEl.GetString() ?? string.Empty;

            if (string.Equals(toolName, "analytics.run", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(_ctxAccessor.LastInsightPreviewMarkdown))
            {
                if (TryReadEvidenceTable(root, "summary_rows_preview", out var cols, out var rows))
                {
                    _ctxAccessor.LastInsightPreviewMarkdown = MarkdownTableRenderer.Render(cols, rows);
                }
            }

            if (string.Equals(toolName, "atomic.query.execute", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(_ctxAccessor.LastListPreviewMarkdown))
            {
                if (TryReadDisplayTable(root, out var cols, out var rows, out var title))
                {
                    _ctxAccessor.LastListPreviewTitle = title;
                    _ctxAccessor.LastListPreviewMarkdown = MarkdownTableRenderer.Render(cols, rows);
                }
            }
        }
        catch
        {
            // best-effort only
        }
    }

    private static bool TryReadEvidenceTable(JsonElement root, string evidenceId, out List<string> columns, out List<object?[]> rows)
    {
        columns = new List<string>();
        rows = new List<object?[]>();

        if (!root.TryGetProperty("evidence", out var evidenceEl) || evidenceEl.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var item in evidenceEl.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.String)
                continue;
            if (!string.Equals(idEl.GetString(), evidenceId, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!item.TryGetProperty("payload", out var payloadEl) || payloadEl.ValueKind != JsonValueKind.Object)
                continue;

            return TryReadTablePayload(payloadEl, out columns, out rows);
        }

        return false;
    }

    private static bool TryReadDisplayTable(JsonElement root, out List<string> columns, out List<object?[]> rows, out string? title)
    {
        columns = new List<string>();
        rows = new List<object?[]>();
        title = null;

        if (!root.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Object)
            return false;

        if (!dataEl.TryGetProperty("displayTables", out var tablesEl) || tablesEl.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var tableEl in tablesEl.EnumerateArray())
        {
            if (tableEl.ValueKind != JsonValueKind.Object)
                continue;

            if (tableEl.TryGetProperty("tableName", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                title = nameEl.GetString();

            if (!tableEl.TryGetProperty("table", out var tabularEl) || tabularEl.ValueKind != JsonValueKind.Object)
                continue;

            return TryReadTabularData(tabularEl, out columns, out rows);
        }

        return false;
    }

    private static bool TryReadTablePayload(JsonElement payload, out List<string> columns, out List<object?[]> rows)
    {
        columns = new List<string>();
        rows = new List<object?[]>();

        if (payload.TryGetProperty("columns", out var columnsEl) && columnsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var col in columnsEl.EnumerateArray())
            {
                if (col.ValueKind == JsonValueKind.String)
                    columns.Add(col.GetString() ?? string.Empty);
            }
        }

        if (payload.TryGetProperty("rows", out var rowsEl) && rowsEl.ValueKind == JsonValueKind.Array)
        {
            var maxRows = 50;
            foreach (var rowEl in rowsEl.EnumerateArray().Take(maxRows))
            {
                if (rowEl.ValueKind != JsonValueKind.Array)
                    continue;
                rows.Add(ReadRow(rowEl, columns.Count));
            }
        }

        if (columns.Count == 0 && rows.Count > 0)
        {
            for (var i = 0; i < rows[0].Length; i++)
                columns.Add($"col{i + 1}");
        }

        return columns.Count > 0;
    }

    private static bool TryReadTabularData(JsonElement tabularEl, out List<string> columns, out List<object?[]> rows)
    {
        columns = new List<string>();
        rows = new List<object?[]>();

        if (tabularEl.TryGetProperty("columns", out var colsEl) && colsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var colEl in colsEl.EnumerateArray())
            {
                if (colEl.ValueKind == JsonValueKind.Object && colEl.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                    columns.Add(nameEl.GetString() ?? string.Empty);
                else if (colEl.ValueKind == JsonValueKind.String)
                    columns.Add(colEl.GetString() ?? string.Empty);
            }
        }

        if (tabularEl.TryGetProperty("rows", out var rowsEl) && rowsEl.ValueKind == JsonValueKind.Array)
        {
            var maxRows = 50;
            foreach (var rowEl in rowsEl.EnumerateArray().Take(maxRows))
            {
                if (rowEl.ValueKind != JsonValueKind.Array)
                    continue;
                rows.Add(ReadRow(rowEl, columns.Count));
            }
        }

        if (columns.Count == 0 && rows.Count > 0)
        {
            for (var i = 0; i < rows[0].Length; i++)
                columns.Add($"col{i + 1}");
        }

        return columns.Count > 0;
    }

    private static object?[] ReadRow(JsonElement rowEl, int maxCols)
    {
        var cols = maxCols > 0 ? maxCols : rowEl.GetArrayLength();
        var row = new object?[cols];
        var index = 0;
        foreach (var cell in rowEl.EnumerateArray())
        {
            if (index >= cols)
                break;
            row[index++] = ReadCell(cell);
        }
        return row;
    }

    private static object? ReadCell(JsonElement cell)
    {
        return cell.ValueKind switch
        {
            JsonValueKind.String => cell.GetString(),
            JsonValueKind.Number => cell.TryGetInt64(out var l) ? l : cell.TryGetDouble(out var d) ? d : cell.GetRawText(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => cell.GetRawText()
        };
    }

    private static string ComputeSignature(string toolName, string argsJson)
    {
        var input = Encoding.UTF8.GetBytes(toolName + "|" + argsJson);
        var hash = SHA256.HashData(input);
        return toolName + ":" + Convert.ToHexString(hash);
    }

    private int ResolveMaxToolResultBytes()
    {
        if (_tuning.MaxToolResultBytes > 0)
            return _tuning.MaxToolResultBytes;

        if (_tuning.MaxToolResultChars > 0)
        {
            var approxBytes = _tuning.MaxToolResultChars * 2;
            return Math.Clamp(approxBytes, 1000, 200000);
        }

        return 16000;
    }

    private ChatCompletionResponse MapToApiResponse(OpenAiChatCompletionResponse? raw, string model, string assistantContent, IReadOnlyCollection<ChatCompletionMessage> incoming)
    {
        var usage = raw?.Usage;
        var apiUsage = new ChatUsage
        {
            PromptTokens = usage?.PromptTokens ?? 0,
            CompletionTokens = usage?.CompletionTokens ?? 0,
            TotalTokens = usage?.TotalTokens ?? 0
        };

        return new ChatCompletionResponse
        {
            Id = raw?.Id ?? Guid.NewGuid().ToString("N"),
            Object = raw?.Object ?? "chat.completion",
            Created = raw?.Created ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Model = model,
            Usage = apiUsage,
            Choices = new[]
            {
                new ChatCompletionChoice
                {
                    Index = 0,
                    FinishReason = "stop",
                    Message = new ChatCompletionMessage { Role = "assistant", Content = assistantContent }
                }
            }
        };
    }

    private static ChatCompletionResponse BuildFailureResponse(string message, string? model, IReadOnlyCollection<ChatCompletionMessage> incoming)
    {
        return new ChatCompletionResponse
        {
            Id = Guid.NewGuid().ToString("N"),
            Object = "chat.completion",
            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Model = model ?? "TILSOFT-AI",
            Usage = new ChatUsage { PromptTokens = 0, CompletionTokens = 0, TotalTokens = 0 },
            Choices = new[]
            {
                new ChatCompletionChoice
                {
                    Index = 0,
                    FinishReason = "stop",
                    Message = new ChatCompletionMessage { Role = "assistant", Content = message }
                }
            }
        };
    }
}




