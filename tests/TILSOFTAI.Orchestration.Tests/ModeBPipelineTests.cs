using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TILSOFTAI.Application.Permissions;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration;
using TILSOFTAI.Orchestration.Chat;
using TILSOFTAI.Orchestration.Chat.Localization;
using TILSOFTAI.Orchestration.Contracts;
using TILSOFTAI.Orchestration.Contracts.Validation;
using TILSOFTAI.Orchestration.Llm;
using TILSOFTAI.Orchestration.Llm.OpenAi;
using TILSOFTAI.Orchestration.SK;
using TILSOFTAI.Orchestration.SK.Conversation;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.Filters;
using TILSOFTAI.Orchestration.Tools.Modularity;
using TILSOFTAI.Orchestration.Tools.ToolSchemas;
using Xunit;

namespace TILSOFTAI.Orchestration.Tests;

public sealed class ModeBPipelineTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ModeB_ToolChain_CompactsToolMessages()
    {
        var responses = new[]
        {
            ToolCallResponse(
                ToolCall("atomic.query.execute", new { spName = "dbo.TILSOFTAI_sp_Test", @params = new { Season = "2024/2025" } })
            ),
            ToolCallResponse(
                ToolCall("analytics.run", new { datasetId = "ds_1", pipeline = Array.Empty<object>() })
            ),
            FinalResponse("final insight")
        };

        var handler = new StubOpenAiHandler(responses);
        var toolHandlers = new IToolHandler[]
        {
            new TestToolHandler("atomic.catalog.search", intent => BuildCatalogResult(intent)),
            new TestToolHandler("atomic.query.execute", intent => BuildAtomicQueryResult(intent, rowCount: 80)),
            new TestToolHandler("analytics.run", intent => BuildAnalyticsRunResult(intent, rowCount: 80))
        };

        var pipeline = BuildPipeline(handler, toolHandlers);
        var response = await pipeline.HandleAsync(BuildRequest("show me data"), BuildContext(), CancellationToken.None);

        AssertHasThreeBlocks(response.Choices.First().Message.Content);

        var toolMessages = GetToolMessages(handler.RequestBodies.Skip(1));
        Assert.NotEmpty(toolMessages);
        Assert.All(toolMessages, AssertToolMessageCompacted);
    }

    [Fact]
    public async Task ModeB_JoinGroupBySum_ProducesOrderedMarkdown()
    {
        var pipelineSpec = new object[]
        {
            new
            {
                op = "join",
                rightDatasetId = "ds_right",
                leftKeys = new[] { "id" },
                rightKeys = new[] { "id" },
                how = "inner",
                rightPrefix = "r_",
                selectRight = new[] { "amount" }
            },
            new
            {
                op = "groupBy",
                by = new[] { "r_amount" },
                aggregates = new[] { new { op = "sum", column = "r_amount", @as = "total" } }
            }
        };

        var responses = new[]
        {
            ToolCallResponse(
                ToolCall("analytics.run", new { datasetId = "ds_join", pipeline = pipelineSpec })
            ),
            FinalResponse("summary")
        };

        var handler = new StubOpenAiHandler(responses);
        var toolHandlers = new IToolHandler[]
        {
            new TestToolHandler("atomic.catalog.search", intent => BuildCatalogResult(intent)),
            new TestToolHandler("atomic.query.execute", intent => BuildAtomicQueryResult(intent, rowCount: 10)),
            new TestToolHandler("analytics.run", intent => BuildAnalyticsRunResult(intent, rowCount: 10))
        };

        var pipeline = BuildPipeline(handler, toolHandlers);
        var response = await pipeline.HandleAsync(BuildRequest("join and sum"), BuildContext(), CancellationToken.None);

        AssertHasThreeBlocks(response.Choices.First().Message.Content);

        var toolMessages = GetToolMessages(handler.RequestBodies.Skip(1));
        Assert.All(toolMessages, AssertToolMessageCompacted);
    }

    [Fact]
    public async Task ModeB_CompactsLargeToolOutputs_UnderBudget()
    {
        var responses = new[]
        {
            ToolCallResponse(
                ToolCall("atomic.query.execute", new { spName = "dbo.TILSOFTAI_sp_Big", @params = new { } })
            ),
            FinalResponse("done")
        };

        var handler = new StubOpenAiHandler(responses);
        var toolHandlers = new IToolHandler[]
        {
            new TestToolHandler("atomic.catalog.search", intent => BuildCatalogResult(intent)),
            new TestToolHandler("atomic.query.execute", intent => BuildAtomicQueryResult(intent, rowCount: 400)),
            new TestToolHandler("analytics.run", intent => BuildAnalyticsRunResult(intent, rowCount: 10))
        };

        var tuning = new ChatTuningOptions { MaxToolResultBytes = 2000 };
        var pipeline = BuildPipeline(handler, toolHandlers, tuning);
        var response = await pipeline.HandleAsync(BuildRequest("big output"), BuildContext(), CancellationToken.None);

        AssertHasThreeBlocks(response.Choices.First().Message.Content);

        var toolMessages = GetToolMessages(handler.RequestBodies.Skip(1));
        Assert.NotEmpty(toolMessages);
        foreach (var msg in toolMessages)
        {
            AssertToolMessageCompacted(msg);
            Assert.True(Encoding.UTF8.GetByteCount(msg.Content ?? string.Empty) <= tuning.MaxToolResultBytes);
            using var doc = JsonDocument.Parse(msg.Content ?? "{}");
            var root = doc.RootElement;
            if (root.TryGetProperty("compaction", out var compaction)
                && compaction.TryGetProperty("truncated", out var truncatedEl))
            {
                Assert.True(truncatedEl.ValueKind == JsonValueKind.True || truncatedEl.ValueKind == JsonValueKind.False);
            }
        }
    }

    private static ChatCompletionRequest BuildRequest(string content)
        => new()
        {
            Model = "test",
            Messages = new[]
            {
                new ChatCompletionMessage { Role = "user", Content = content }
            }
        };

    private static TSExecutionContext BuildContext()
        => new()
        {
            TenantId = "tenant",
            UserId = "user",
            Roles = new[] { "user" },
            RequestId = "req-1",
            TraceId = "trace-1",
            ConversationId = "conv-1"
        };

    private static ChatPipeline BuildPipeline(StubOpenAiHandler handler, IEnumerable<IToolHandler> toolHandlers, ChatTuningOptions? tuning = null)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/")
        };

        var lmOptions = new LmStudioOptions
        {
            BaseUrl = "http://localhost",
            Model = "test",
            TimeoutSeconds = 30,
            ModelMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["test"] = "test"
            }
        };

        var chatClient = new OpenAiChatClient(httpClient, lmOptions, NullLogger<OpenAiChatClient>.Instance);

        var provider = new TestToolRegistrationProvider();
        var registry = new ToolRegistry(new[] { provider });
        var specCatalog = new ToolInputSpecCatalog(Array.Empty<IToolInputSpecProvider>());
        var schemaFactory = new OpenAiToolSchemaFactory(registry, specCatalog);

        var dispatcher = new ToolDispatcher(toolHandlers);
        var rbac = new RbacService();
        var ctxAccessor = new ExecutionContextAccessor();
        var conversationStore = new InMemoryConversationStateStore();
        var filterPatchMerger = new FilterPatchMerger(new FilterCanonicalizer());
        var validationOptions = Options.Create(new ResponseSchemaValidationOptions { Enabled = false });
        var validator = new ResponseSchemaValidator(validationOptions, NullLogger<ResponseSchemaValidator>.Instance);
        var options = new OrchestrationOptions { EnableFilterPatching = false };

        var toolInvoker = new ToolInvoker(
            registry,
            dispatcher,
            rbac,
            ctxAccessor,
            options,
            conversationStore,
            filterPatchMerger,
            validator,
            NullLogger<ToolInvoker>.Instance);

        var auditLogger = new NullAuditLogger();
        var languageResolver = new HeuristicLanguageResolver();
        var localizer = new DefaultChatTextLocalizer();
        var patterns = new ChatTextPatterns();
        var tokenBudget = new TokenBudget();
        var chatTuning = tuning ?? new ChatTuningOptions();

        return new ChatPipeline(
            chatClient,
            schemaFactory,
            toolInvoker,
            ctxAccessor,
            tokenBudget,
            auditLogger,
            conversationStore,
            languageResolver,
            localizer,
            patterns,
            chatTuning,
            NullLogger<ChatPipeline>.Instance);
    }

    private static IReadOnlyList<OpenAiChatMessage> GetToolMessages(IEnumerable<string> requestBodies)
    {
        var list = new List<OpenAiChatMessage>();
        foreach (var body in requestBodies)
        {
            if (string.IsNullOrWhiteSpace(body))
                continue;

            var request = JsonSerializer.Deserialize<OpenAiChatCompletionRequest>(body, Json);
            if (request?.Messages is null)
                continue;

            list.AddRange(request.Messages.Where(m => string.Equals(m.Role, "tool", StringComparison.OrdinalIgnoreCase)));
        }
        return list;
    }

    private static void AssertHasThreeBlocks(string content)
    {
        Assert.False(string.IsNullOrWhiteSpace(content));

        var first = content.IndexOf("## ", StringComparison.Ordinal);
        var second = content.IndexOf("## ", first + 3, StringComparison.Ordinal);
        var third = content.IndexOf("## ", second + 3, StringComparison.Ordinal);

        Assert.True(first >= 0);
        Assert.True(second > first);
        Assert.True(third > second);
    }

    private static void AssertToolMessageCompacted(OpenAiChatMessage message)
    {
        var content = message.Content ?? string.Empty;
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        Assert.False(root.TryGetProperty("data", out _));

        if (root.TryGetProperty("evidence", out var evidence) && evidence.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in evidence.EnumerateArray())
            {
                if (!item.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
                    continue;

                if (payload.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
                    Assert.True(rows.GetArrayLength() <= 20);
            }
        }
    }

    private static OpenAiChatCompletionResponse ToolCallResponse(params OpenAiToolCall[] toolCalls)
        => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Object = "chat.completion",
            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Model = "test",
            Choices = new List<OpenAiChatChoice>
            {
                new()
                {
                    Index = 0,
                    FinishReason = "tool_calls",
                    Message = new OpenAiChatMessage
                    {
                        Role = "assistant",
                        Content = null,
                        ToolCalls = toolCalls.ToList()
                    }
                }
            }
        };

    private static OpenAiChatCompletionResponse FinalResponse(string content)
        => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Object = "chat.completion",
            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Model = "test",
            Choices = new List<OpenAiChatChoice>
            {
                new()
                {
                    Index = 0,
                    FinishReason = "stop",
                    Message = new OpenAiChatMessage
                    {
                        Role = "assistant",
                        Content = content,
                        ToolCalls = null
                    }
                }
            }
        };

    private static OpenAiToolCall ToolCall(string name, object args)
        => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Function = new OpenAiToolCallFunction
            {
                Name = name,
                Arguments = JsonSerializer.Serialize(args, Json)
            }
        };

    private static ToolDispatchResult BuildCatalogResult(object intent)
    {
        var payload = new { kind = "atomic.catalog.search.v1", schemaVersion = 1, data = new { results = Array.Empty<object>() } };
        var extras = new ToolDispatchExtras(new EnvelopeSourceV1 { System = "test", Name = "catalog" }, Array.Empty<EnvelopeEvidenceItemV1>());
        return ToolDispatchResultFactory.Create(intent, ToolExecutionResult.CreateSuccess("ok", payload), extras);
    }

    private static ToolDispatchResult BuildAtomicQueryResult(object intent, int rowCount)
    {
        var rows = BuildRows(rowCount, 3);
        var payload = new
        {
            kind = "atomic.query.execute.v1",
            schemaVersion = 2,
            data = new { rows }
        };

        var evidence = new[]
        {
            new EnvelopeEvidenceItemV1
            {
                Id = "rows",
                Type = "list",
                Title = "Rows",
                Payload = new { columns = new[] { "c1", "c2", "c3" }, rows }
            }
        };

        var extras = new ToolDispatchExtras(new EnvelopeSourceV1 { System = "test", Name = "atomic" }, evidence);
        return ToolDispatchResultFactory.Create(intent, ToolExecutionResult.CreateSuccess("ok", payload), extras);
    }

    private static ToolDispatchResult BuildAnalyticsRunResult(object intent, int rowCount)
    {
        var rows = BuildRows(rowCount, 2);
        var payload = new
        {
            kind = "analytics.run.v1",
            schemaVersion = 2,
            data = new { rows }
        };

        var evidence = new[]
        {
            new EnvelopeEvidenceItemV1
            {
                Id = "summary_rows_preview",
                Type = "list",
                Title = "Summary rows preview",
                Payload = new { columns = new[] { "a", "b" }, rows }
            }
        };

        var extras = new ToolDispatchExtras(new EnvelopeSourceV1 { System = "test", Name = "engine" }, evidence);
        return ToolDispatchResultFactory.Create(intent, ToolExecutionResult.CreateSuccess("ok", payload), extras);
    }

    private static object?[] MakeRow(int columns, int rowIndex)
    {
        var row = new object?[columns];
        for (var i = 0; i < columns; i++)
            row[i] = $"r{rowIndex}_c{i}";
        return row;
    }

    private static IReadOnlyList<object?[]> BuildRows(int count, int columns)
    {
        var rows = new List<object?[]>(count);
        for (var i = 0; i < count; i++)
            rows.Add(MakeRow(columns, i));
        return rows;
    }

    private sealed class StubOpenAiHandler : HttpMessageHandler
    {
        private readonly Queue<OpenAiChatCompletionResponse> _responses;

        public StubOpenAiHandler(IEnumerable<OpenAiChatCompletionResponse> responses)
        {
            _responses = new Queue<OpenAiChatCompletionResponse>(responses);
        }

        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            RequestBodies.Add(body);

            if (_responses.Count == 0)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("no response")
                };
            }

            var response = _responses.Dequeue();
            var json = JsonSerializer.Serialize(response, Json);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class TestToolHandler : IToolHandler
    {
        private readonly Func<object, ToolDispatchResult> _handler;

        public TestToolHandler(string toolName, Func<object, ToolDispatchResult> handler)
        {
            ToolName = toolName;
            _handler = handler;
        }

        public string ToolName { get; }

        public Task<ToolDispatchResult> HandleAsync(object intent, TSExecutionContext context, CancellationToken cancellationToken)
            => Task.FromResult(_handler(intent));
    }

    private sealed class TestToolRegistrationProvider : IToolRegistrationProvider
    {
        private static readonly Dictionary<string, HashSet<string>> AllowedArgs = new(StringComparer.OrdinalIgnoreCase)
        {
            ["atomic.catalog.search"] = new(StringComparer.OrdinalIgnoreCase) { "query", "topK" },
            ["atomic.query.execute"] = new(StringComparer.OrdinalIgnoreCase) { "spName", "params" },
            ["analytics.run"] = new(StringComparer.OrdinalIgnoreCase) { "datasetId", "pipeline", "topN", "maxGroups", "maxResultRows" }
        };

        public IEnumerable<ToolDefinition> GetToolDefinitions()
        {
            foreach (var kv in AllowedArgs)
            {
                yield return new ToolDefinition(
                    Name: kv.Key,
                    Validator: Validate,
                    RequiresWrite: false,
                    AllowedArguments: kv.Value);
            }
        }

        private static ValidationResult<object> Validate(JsonElement args)
        {
            if (args.ValueKind != JsonValueKind.Object)
                return ValidationResult<object>.Fail("args must be object");

            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in args.EnumerateObject())
                dict[prop.Name] = ConvertArg(prop.Value);

            var intent = new DynamicToolIntent(
                Filters: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
                Page: 1,
                PageSize: 20,
                Args: dict);

            return ValidationResult<object>.Success(intent);
        }

        private static object? ConvertArg(JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.TryGetInt32(out var i) ? i : value.TryGetInt64(out var l) ? l : value.TryGetDecimal(out var d) ? d : value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                JsonValueKind.Array => value.Clone(),
                JsonValueKind.Object => value.Clone(),
                _ => value.Clone()
            };
        }
    }

    private sealed class NullAuditLogger : IAuditLogger
    {
        public Task LogUserInputAsync(TSExecutionContext context, string input, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LogAiDecisionAsync(TSExecutionContext context, string aiOutput, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LogToolExecutionAsync(TSExecutionContext context, string toolName, object arguments, object result, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
