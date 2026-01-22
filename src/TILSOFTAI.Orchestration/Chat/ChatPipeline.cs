using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TILSOFTAI.Configuration;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Orchestration.Chat.Localization;
using TILSOFTAI.Orchestration.Llm;
using TILSOFTAI.Orchestration.Llm.OpenAi;
using TILSOFTAI.Orchestration.Formatting;
using TILSOFTAI.Orchestration.Execution;
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
    private const double DefaultTemperature = 0.2;
    private const int DefaultMaxTokens = 800;

    private readonly OpenAiChatClient _chat;
    private readonly OpenAiToolSchemaFactory _toolSchemaFactory;
    private readonly ToolInvoker _toolInvoker;
    private readonly ExecutionContextAccessor _ctxAccessor;
    private readonly IAuditLogger _auditLogger;
    private readonly ILanguageResolver _languageResolver;
    private readonly IChatTextLocalizer _localizer;
    private readonly AppSettings _settings;
    private readonly ILogger<ChatPipeline> _logger;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public ChatPipeline(
        OpenAiChatClient chat,
        OpenAiToolSchemaFactory toolSchemaFactory,
        ToolInvoker toolInvoker,
        ExecutionContextAccessor ctxAccessor,
        IAuditLogger auditLogger,
        ILanguageResolver languageResolver,
        IChatTextLocalizer localizer,
        IOptions<AppSettings> settings,
        ILogger<ChatPipeline>? logger = null)
    {
        _chat = chat;
        _toolSchemaFactory = toolSchemaFactory;
        _toolInvoker = toolInvoker;
        _ctxAccessor = ctxAccessor;
        _auditLogger = auditLogger;
        _languageResolver = languageResolver;
        _localizer = localizer;
        _settings = settings.Value;
        _logger = logger ?? NullLogger<ChatPipeline>.Instance;
    }

    public async Task<ChatCompletionResponse> HandleAsync(ChatCompletionRequest request, TILSOFTAI.Domain.ValueObjects.TSExecutionContext context, CancellationToken cancellationToken)
    {
        var incomingMessages = request.Messages ?? Array.Empty<ChatCompletionMessage>();

        var cultureName = _languageResolver.Resolve(incomingMessages);
        ApplyCulture(cultureName);

        if (incomingMessages.Any(m => !SupportedRoles.Contains(m.Role)))
            return BuildFailureResponse(BuildFallbackInsight(), request.Model, incomingMessages);

        var hasUser = incomingMessages.Any(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(m.Content));
        if (!hasUser)
            return BuildFailureResponse(BuildFallbackInsight(), request.Model, incomingMessages);

        var lastUserMessage = incomingMessages.Last(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(m.Content)).Content;

        // Audit: user input (best-effort)
        try
        {
            await _auditLogger.LogUserInputAsync(context, lastUserMessage, cancellationToken);
        }
        catch { /* ignore */ }


        // Set request context for ToolInvoker
        _ctxAccessor.Context = context;
        _ctxAccessor.LastListPreviewMarkdown = null;
        _ctxAccessor.LastInsightPreviewMarkdown = null;

        _logger.LogInformation("ChatPipeline start requestId={RequestId} traceId={TraceId} convId={ConversationId} userId={UserId} lang={Lang} model={Model} msgs={MsgCount}",
            context.RequestId, context.TraceId, context.ConversationId, context.UserId, cultureName, request.Model, incomingMessages.Count);

        // Build OpenAI messages
        var systemPrompt = BuildSystemPrompt();
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
        var toolAllowlist = ResolveToolAllowlist();
        var tools = _toolSchemaFactory.BuildTools(toolAllowlist);

        // Planner loop
        var mappedModel = _chat.MapModel(request.Model);
        var maxSteps = Math.Clamp(_settings.Chat.MaxToolSteps, 1, 20);
        var previewRowLimit = ResolvePreviewRowLimit();
        var signatureCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        OpenAiChatCompletionResponse? lastResp = null;

        for (var step = 0; step < maxSteps; step++)
        {
            TrimMessagesToBudget(messages);

            var call = new OpenAiChatCompletionRequest
            {
                Model = mappedModel,
                Messages = messages,
                Tools = tools.ToList(),
                ToolChoice = "auto",
                Temperature = request.Temperature ?? DefaultTemperature,
                MaxTokens = request.MaxTokens ?? DefaultMaxTokens,
                Stream = false
            };

            try
            {
                lastResp = await _chat.CreateChatCompletionAsync(call, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                var timeoutFinal = ComposeFinalMarkdown(BuildFallbackInsight(), _ctxAccessor);
                return MapToApiResponse(lastResp, request.Model ?? "TILSOFT-AI", timeoutFinal, incomingMessages);
            }catch (TimeoutException)
            {
                var timeoutFinal = ComposeFinalMarkdown(BuildFallbackInsight(), _ctxAccessor);
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
                    insight = BuildFallbackInsight();

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
                if (!signatureCounts.TryGetValue(signature, out var count))
                    count = 0;
                count++;
                signatureCounts[signature] = count;
                if (count > 2)
                {
                    var forced = ComposeFinalMarkdown(BuildFallbackInsight(), _ctxAccessor);
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

                var toolResult = await _toolInvoker.ExecuteAsync(toolName, argsElement, toolAllowlist, cancellationToken);
                var toolResultJson = JsonSerializer.Serialize(toolResult, _json);

                TryCaptureToolArtifacts(toolResultJson, previewRowLimit);

                var compactJson = ToolResultCompactor.CompactEnvelopeJson(
                    toolResultJson,
                    ResolveMaxToolResultBytes(),
                    _settings.Chat.CompactionLimits);
                messages.Add(new OpenAiChatMessage
                {
                    Role = "tool",
                    ToolCallId = tc.Id,
                    Content = compactJson
                });

                TrimMessagesToBudget(messages);
            }
        }

        var fallbackFinal = ComposeFinalMarkdown(BuildFallbackInsight(), _ctxAccessor);
        return MapToApiResponse(lastResp, request.Model ?? "TILSOFT-AI", fallbackFinal, incomingMessages);
    }
    private void ApplyCulture(string cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
            return;

        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            _localizer.SetCulture(cultureName);
        }
        catch (CultureNotFoundException)
        {
            // Ignore invalid culture names.
        }
    }

    private string BuildFallbackInsight()
    {
        var fallback = _localizer.Get(ChatTextKeys.FallbackNoContent);
        var hint = _localizer.Get(ChatTextKeys.PreviousQueryHint);
        if (string.IsNullOrWhiteSpace(hint))
            return fallback;
        return string.Join("\n\n", new[] { fallback, hint }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private IReadOnlySet<string> ResolveToolAllowlist()
    {
        var allowlist = _settings.Orchestration.ToolAllowlist ?? Array.Empty<string>();
        var trimmed = allowlist
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (trimmed.Length == 0)
        {
            trimmed =
            [
                "atomic.catalog.search",
                "atomic.query.execute",
                "analytics.run"
            ];
        }

        return new HashSet<string>(trimmed, StringComparer.OrdinalIgnoreCase);
    }
    private string BuildSystemPrompt()
    {
        return _localizer.Get(ChatTextKeys.SystemPrompt);
    }
    private void TrimMessagesToBudget(List<OpenAiChatMessage> messages)
    {
        if (messages.Count == 0)
            return;

        var limit = Math.Clamp(_settings.Chat.MaxPromptTokensEstimate, 256, 200000);
        var policy = (_settings.Chat.TrimPolicy ?? string.Empty).Trim().ToLowerInvariant();
        while (EstimatePromptTokens(messages) > limit && messages.Count > 1)
        {
            if (policy != "drop_oldest_first")
            {
                var toolIndex = messages.FindIndex(m => string.Equals(m.Role, "tool", StringComparison.OrdinalIgnoreCase));
                if (toolIndex > 0)
                {
                    messages.RemoveAt(toolIndex);
                    continue;
                }
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
    private string ComposeFinalMarkdown(string insightText, ExecutionContextAccessor ctx)
    {
        var insight = string.IsNullOrWhiteSpace(insightText) ? string.Empty : insightText.Trim();
        var insightPreview = string.IsNullOrWhiteSpace(ctx.LastInsightPreviewMarkdown) ? string.Empty : ctx.LastInsightPreviewMarkdown;
        var listPreview = string.IsNullOrWhiteSpace(ctx.LastListPreviewMarkdown) ? string.Empty : ctx.LastListPreviewMarkdown;

        var insightTitle = _localizer.Get(ChatTextKeys.BlockTitleInsight);
        var insightPreviewTitle = _localizer.Get(ChatTextKeys.BlockTitleInsightPreview);
        var listPreviewTitle = _localizer.Get(ChatTextKeys.BlockTitleListPreview);

        var sb = new StringBuilder();
        sb.AppendLine($"## {insightTitle}");
        if (!string.IsNullOrWhiteSpace(insight))
            sb.AppendLine(insight);
        sb.AppendLine();
        sb.AppendLine($"## {insightPreviewTitle}");
        if (!string.IsNullOrWhiteSpace(insightPreview))
            sb.AppendLine(insightPreview);
        sb.AppendLine();
        sb.AppendLine($"## {listPreviewTitle}");
        if (!string.IsNullOrWhiteSpace(listPreview))
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

    private void TryCaptureToolArtifacts(string envelopeJson, int previewRowLimit)
    {
        if (string.IsNullOrWhiteSpace(envelopeJson))
            return;

        try
        {
            var renderOptions = new MarkdownTableRenderOptions { MaxRows = previewRowLimit };
            using var doc = JsonDocument.Parse(envelopeJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("tool", out var toolEl) || !toolEl.TryGetProperty("name", out var nameEl))
                return;

            var toolName = nameEl.GetString() ?? string.Empty;

            if (string.Equals(toolName, "analytics.run", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(_ctxAccessor.LastInsightPreviewMarkdown))
            {
                if (TryReadEvidenceTable(root, "summary_rows_preview", previewRowLimit, out var cols, out var rows))
                {
                    _ctxAccessor.LastInsightPreviewMarkdown = MarkdownTableRenderer.Render(cols, rows, renderOptions);
                }
            }

            if (string.Equals(toolName, "atomic.query.execute", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(_ctxAccessor.LastListPreviewMarkdown))
            {
                if (TryReadDisplayTable(root, previewRowLimit, out var cols, out var rows, out _))
                    _ctxAccessor.LastListPreviewMarkdown = MarkdownTableRenderer.Render(cols, rows, renderOptions);
            }
        }
        catch
        {
            // best-effort only
        }
    }

    private static bool TryReadEvidenceTable(JsonElement root, string evidenceId, int maxRows, out List<string> columns, out List<object?[]> rows)
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

            return TryReadTablePayload(payloadEl, maxRows, out columns, out rows);
        }

        return false;
    }

    private static bool TryReadDisplayTable(JsonElement root, int maxRows, out List<string> columns, out List<object?[]> rows, out string? title)
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

            return TryReadTabularData(tabularEl, maxRows, out columns, out rows);
        }

        return false;
    }

    private static bool TryReadTablePayload(JsonElement payload, int maxRows, out List<string> columns, out List<object?[]> rows)
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
            var limit = Math.Clamp(maxRows, 1, 200);
            foreach (var rowEl in rowsEl.EnumerateArray().Take(limit))
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

    private static bool TryReadTabularData(JsonElement tabularEl, int maxRows, out List<string> columns, out List<object?[]> rows)
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
            var limit = Math.Clamp(maxRows, 1, 200);
            foreach (var rowEl in rowsEl.EnumerateArray().Take(limit))
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
        var maxBytes = _settings.Chat.MaxToolResultBytes;
        if (maxBytes <= 0)
            maxBytes = 16000;
        return Math.Clamp(maxBytes, 1000, 200000);
    }

    private int ResolvePreviewRowLimit()
    {
        var limit = _settings.AnalyticsEngine.PreviewRowLimit;
        if (limit <= 0)
            limit = 20;
        return Math.Clamp(limit, 1, 200);
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








