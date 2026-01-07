using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Orchestration.Llm;
using TILSOFTAI.Orchestration.SK;
using TILSOFTAI.Orchestration.SK.Governance;
using TILSOFTAI.Orchestration.SK.Planning;
using TILSOFTAI.Orchestration.SK.Plugins;
using TILSOFTAI.Orchestration.Tools;
using TSExecutionContext = TILSOFTAI.Domain.ValueObjects.TSExecutionContext;

namespace TILSOFTAI.Orchestration.Chat;

public sealed class ChatPipeline
{
    private readonly SkKernelFactory _kernelFactory;
    private readonly ExecutionContextAccessor _ctxAccessor;
    private readonly ToolRegistry _toolRegistry;
    private readonly ToolDispatcher _toolDispatcher;
    private readonly CommitGuardFilter _commitGuard;
    private readonly AutoInvocationCircuitBreakerFilter _circuitBreaker;
    private readonly TokenBudget _tokenBudget;
    private readonly IAuditLogger _auditLogger;
    private readonly PlannerRouter _plannerRouter;
    private readonly StepwiseLoopRunner _stepwiseLoopRunner;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceProvider _serviceProvider;
    private readonly PluginCatalog _pluginCatalog;
    private readonly ModuleRouter _moduleRouter;
    private readonly IEnumerable<IPluginExposurePolicy> _exposurePolicies;

    //Plugin

    public ChatPipeline(
        SkKernelFactory kernelFactory,
        ExecutionContextAccessor ctxAccessor,
        ToolRegistry toolRegistry,
        ToolDispatcher toolDispatcher,
        CommitGuardFilter commitGuard,
        AutoInvocationCircuitBreakerFilter circuitBreaker,
        TokenBudget tokenBudget,
        IAuditLogger auditLogger,
        PlannerRouter plannerRouter,
        StepwiseLoopRunner stepwiseLoopRunner,
        IServiceProvider serviceProvider,
        PluginCatalog pluginCatalog,
        ModuleRouter moduleRouter,
        IEnumerable<IPluginExposurePolicy> exposurePolicies)
    {
        _kernelFactory = kernelFactory;
        _ctxAccessor = ctxAccessor;
        _toolRegistry = toolRegistry;
        _toolDispatcher = toolDispatcher;
        _commitGuard = commitGuard;
        _circuitBreaker = circuitBreaker;
        _tokenBudget = tokenBudget;
        _auditLogger = auditLogger;
        _plannerRouter = plannerRouter;
        _stepwiseLoopRunner = stepwiseLoopRunner;
        _serviceProvider = serviceProvider;
        _pluginCatalog = pluginCatalog;
        _moduleRouter = moduleRouter;
        _exposurePolicies = exposurePolicies;
    }

    public async Task<ChatCompletionResponse> HandleAsync(ChatCompletionRequest request, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var serializedRequest = JsonSerializer.Serialize(request, _serializerOptions);

        var incomingMessages = request.Messages ?? Array.Empty<ChatCompletionMessage>();

        if (incomingMessages.Any(m => !IsSupportedRole(m.Role)))
        {
            return BuildFailureResponse("Unsupported message role.", request.Model, Array.Empty<ChatCompletionMessage>());
        }

        var hasUser = incomingMessages.Any(m =>
            string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(m.Content));

        if (!hasUser)
        {
            return BuildFailureResponse("User message required.", request.Model, Array.Empty<ChatCompletionMessage>());
        }

        var lastUserMessage = incomingMessages.Last(m =>
            string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(m.Content)).Content;

        // 1) set context for plugins/filters
        _ctxAccessor.CircuitBreakerTripped = false;
        _ctxAccessor.CircuitBreakerReason = null;
        _ctxAccessor.Context = context;
        _ctxAccessor.ConfirmedConfirmationId = TryExtractConfirmationId(lastUserMessage);
        _ctxAccessor.AutoInvokeCount = 0;
        _ctxAccessor.AutoInvokeSignatureCounts.Clear();

        // 2) build kernel
        var kernel = _kernelFactory.CreateKernel(request.Model);

        // 3) register module plugins (bạn uncomment sau khi đã inject plugins theo module)
        var modules = _moduleRouter.SelectModules(lastUserMessage, context);

        foreach (var module in modules)
        {
            if (!_pluginCatalog.ByModule.TryGetValue(module, out var types)) continue;

            var policy = _exposurePolicies.First(p => p.CanHandle(module));
            var selectedTypes = policy.Select(module, types, lastUserMessage);
            if (selectedTypes.Count == 0) selectedTypes = types;

            foreach (var t in selectedTypes)
            {
                var plugin = _serviceProvider.GetRequiredService(t);
                var pluginName = ToPluginName(t.Name);
                kernel.Plugins.AddFromObject(plugin, pluginName);
            }
        }

        // If this looks like an ERP/business question (modules selected) but no tools are
        // actually exposed, do not let the model hallucinate tool calls. Return a clear
        // feature-not-available response instead.
        var exposedFunctionCount = kernel.Plugins.SelectMany(p => p).Count();
        if (modules.Count > 0 && exposedFunctionCount == 0)
        {
            var fallback = "Hiện tại tôi chưa được cập nhật tính năng này! Vui lòng liên hệ quản trị hệ thống để kích hoạt hoặc bổ sung tool phù hợp.";
            return BuildResponse(request.Model ?? "TILSOFT-AI", fallback, incomingMessages);
        }

        // 4) governance filter: chọn 1 trong 2 dòng dưới tùy interface bạn implement
        // Nếu CommitGuardFilter : IAutoFunctionInvocationFilter
        kernel.AutoFunctionInvocationFilters.Add(_circuitBreaker);
        kernel.AutoFunctionInvocationFilters.Add(_commitGuard);
        // Nếu CommitGuardFilter : IFunctionInvocationFilter thì dùng:
        // kernel.FunctionInvocationFilters.Add(_commitGuard);

        // 5) build chat history
        var history = new ChatHistory();
        history.AddSystemMessage(BuildSystemPrompt());

        foreach (var m in incomingMessages)
        {
            if (string.IsNullOrWhiteSpace(m.Content)) continue;
            if (string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase)) continue;

            history.AddMessage(ToAuthorRole(m.Role), m.Content);
        }

        // 6) 01 model - internal routing
        var useLoop = _plannerRouter.ShouldUseLoop(lastUserMessage);

        string content;
        try
        {
            if (useLoop)
            {
                content = await _stepwiseLoopRunner.RunAsync(kernel, history, maxIterations: 8, cancellationToken);
            }
            else
            {
                content = await GetAssistantAnswerAsync(request.Model, kernel, history, cancellationToken);
            }

            // Defensive: if some path returns empty content, force a no-tools synthesis pass
            if (string.IsNullOrWhiteSpace(content))
            {
                content = await SynthesizeWithoutToolsAsync(request.Model, history, cancellationToken);
            }

            // thấy marker / flag thì xóa content để ép synthesis
            if (_ctxAccessor.CircuitBreakerTripped ||
                content.StartsWith("CIRCUIT_BREAKER", StringComparison.OrdinalIgnoreCase) ||
                content.Contains("\"circuit_breaker\"", StringComparison.OrdinalIgnoreCase))
            {
                content = string.Empty;
            }
        }
        catch
        {
            return BuildFailureResponse("AI response unavailable.", request.Model, incomingMessages);
        }

        await _auditLogger.LogUserInputAsync(context, serializedRequest, cancellationToken);

        var response = BuildResponse(request.Model ?? "TILSOFT-AI", content, incomingMessages);
        _tokenBudget.EnsureWithinBudget(response.Choices.First().Message.Content);
        return response;
    }

    private static string BuildSystemPrompt() => """
        Bạn là trợ lý nghiệp vụ ERP.

        Quy tắc công cụ (tools/functions):
        - Kết quả tool luôn bọc theo envelope JSON: kind="tilsoft.envelope.v1" (schemaVersion>=2).
          + Nếu ok=true: đọc field data (payload tool) và có thể dùng field evidence (registry) để tóm tắt nhanh.
          + Nếu ok=false: đọc field error.code/error.message và field policy.decision/reasonCode; KHÔNG lặp lại cùng tool-call.
          + Field source/telemetry giúp bạn giải thích nguồn số liệu (SQL/SP) và thời gian xử lý.
        - Nếu không chắc filters hợp lệ cho nghiệp vụ, hãy gọi filters-catalog (resource tương ứng) trước khi gọi tool dữ liệu.
        - Nếu không chắc thao tác ghi (create/update) cần những tham số nào, hãy gọi actions-catalog trước khi gọi tool prepare.
        - Nếu yêu cầu CẦN dữ liệu nội bộ (giá, tồn kho, đơn hàng, khách hàng, model, doanh số...) thì PHẢI gọi tools để lấy evidence.
        - Nếu câu hỏi là nghiệp vụ ERP và CẦN dữ liệu nội bộ nhưng hệ thống CHƯA có tool phù hợp, hãy trả lời đúng mẫu: "Hiện tại tôi chưa được cập nhật tính năng này!" (có thể kèm gợi ý liên hệ quản trị hệ thống).
        - Nếu câu hỏi KHÔNG cần dữ liệu nội bộ (chào hỏi, giải thích khái niệm, hướng dẫn chung...), bạn được trả lời tự nhiên như một chatbot thông thường.
        - Tuyệt đối không bịa dữ liệu nội bộ khi chưa có evidence từ tool.
        - Chỉ được gọi các tools có trong danh sách hệ thống cung cấp. Tuyệt đối không tự bịa tool (ví dụ: functions.prepare).
        - Thao tác ghi (create/update/commit) phải theo 2 bước: prepare -> yêu cầu xác nhận -> commit.
        - Người dùng xác nhận bằng: XÁC NHẬN <confirmation_id>.
        """;

    private async Task<string> GetAssistantAnswerAsync(
        string? requestedModel,
        Kernel kernel,
        ChatHistory history,
        CancellationToken cancellationToken)
    {
        var chat = kernel.GetRequiredService<IChatCompletionService>();
        var settings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        };

        var msg = await chat.GetChatMessageContentAsync(history, settings, kernel, cancellationToken);
        var content = (msg.Content ?? string.Empty).Trim();

        // If the model executed tools but returned empty content, do a forced synthesis
        // pass with no tools exposed. This prevents empty assistant outputs from reaching
        // the client and avoids relying on any sentinel tokens.
        if (string.IsNullOrWhiteSpace(content))
        {
            content = await SynthesizeWithoutToolsAsync(requestedModel, history, cancellationToken);
        }

        return content;
    }

    private async Task<string> SynthesizeWithoutToolsAsync(
        string? requestedModel,
        ChatHistory history,
        CancellationToken cancellationToken)
    {
        // Add a short instruction to produce a final answer based on evidence already in history.
        history.AddSystemMessage("\"Bạn đã có đủ kết quả từ tools. Hãy trả lời trực tiếp, ngắn gọn, dựa trên evidence trong hội thoại. \" +\r\n  \"KHÔNG gọi tool. Không nhắc tới thông báo kỹ thuật nội bộ (circuit breaker / guardrails).\"");

        // New kernel without plugins => no tools can be called.
        var kernel2 = _kernelFactory.CreateKernel(requestedModel);
        var chat2 = kernel2.GetRequiredService<IChatCompletionService>();
        var msg2 = await chat2.GetChatMessageContentAsync(history, new OpenAIPromptExecutionSettings(), kernel2, cancellationToken);
        return (msg2.Content ?? string.Empty).Trim();
    }

    private static string? TryExtractConfirmationId(string text)
    {
        var m = System.Text.RegularExpressions.Regex.Match(text, @"(?i)\b(xác\s*nhận|confirm)\b[^a-f0-9]*([a-f0-9]{32})\b");
        return m.Success ? m.Groups[2].Value : null;
    }

    private ChatCompletionResponse BuildFailureResponse(string error, string? model, IReadOnlyCollection<ChatCompletionMessage> promptMessages)
    {
        var payload = JsonSerializer.Serialize(new { error }, _serializerOptions);
        return BuildResponse(model, payload, promptMessages);
    }

    private ChatCompletionResponse BuildResponse(string? model, string content, IReadOnlyCollection<ChatCompletionMessage> promptMessages)
    {
        var completionTokens = _tokenBudget.EstimateTokens(content);
        var promptTokens = _tokenBudget.EstimateMessageTokens(promptMessages);

        return new ChatCompletionResponse
        {
            Id = $"chatcmpl-{Guid.NewGuid():N}",
            Object = "chat.completion",
            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Model = model ?? "lmstudio",
            Choices = new[]
            {
                new ChatCompletionChoice
                {
                    Index = 0,
                    FinishReason = "stop",
                    Message = new ChatCompletionMessage
                    {
                        Role = "assistant",
                        Content = content
                    }
                }
            },
            Usage = new ChatUsage
            {
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                TotalTokens = promptTokens + completionTokens
            }
        };
    }

    //private static bool IsSupportedRole(string? role)
    //{
    //    return string.Equals(role, "user", StringComparison.OrdinalIgnoreCase) ||
    //           string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase) ||
    //           string.Equals(role, "system", StringComparison.OrdinalIgnoreCase);
    //}
    private static AuthorRole ToAuthorRole(string? role)
    {
        if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase)) return AuthorRole.User;
        if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)) return AuthorRole.Assistant;
        if (string.Equals(role, "system", StringComparison.OrdinalIgnoreCase)) return AuthorRole.System;

        // fallback an toàn: coi như user
        return AuthorRole.User;
    }

    private static bool IsSupportedRole(string? role)
    {
        return string.Equals(role, "user", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "system", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToPluginName(string typeName)
    {
        const string suffix = "ToolsPlugin";
        var name = typeName.EndsWith(suffix, StringComparison.Ordinal)
            ? typeName[..^suffix.Length]
            : typeName;

        if (string.IsNullOrWhiteSpace(name)) return "common";
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

}
