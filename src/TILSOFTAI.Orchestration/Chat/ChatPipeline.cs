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
using ExecutionContext = TILSOFTAI.Domain.ValueObjects.ExecutionContext;

namespace TILSOFTAI.Orchestration.Chat;

public sealed class ChatPipeline
{
    private readonly SkKernelFactory _kernelFactory;
    private readonly ExecutionContextAccessor _ctxAccessor;
    private readonly ToolRegistry _toolRegistry;
    private readonly ToolDispatcher _toolDispatcher;
    private readonly CommitGuardFilter _commitGuard;
    private readonly TokenBudget _tokenBudget;
    private readonly IAuditLogger _auditLogger;
    private readonly PlannerRouter _plannerRouter;
    private readonly StepwiseLoopRunner _stepwiseLoopRunner;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceProvider _serviceProvider;
    private readonly PluginCatalog _pluginCatalog;
    private readonly ModuleRouter _moduleRouter;

    //Plugin

    public ChatPipeline(
        SkKernelFactory kernelFactory,
        ExecutionContextAccessor ctxAccessor,
        ToolRegistry toolRegistry,
        ToolDispatcher toolDispatcher,
        CommitGuardFilter commitGuard,
        TokenBudget tokenBudget,
        IAuditLogger auditLogger,
        PlannerRouter plannerRouter,
        StepwiseLoopRunner stepwiseLoopRunner,
        IServiceProvider serviceProvider,
        PluginCatalog pluginCatalog,
        ModuleRouter moduleRouter)
    {
        _kernelFactory = kernelFactory;
        _ctxAccessor = ctxAccessor;
        _toolRegistry = toolRegistry;
        _toolDispatcher = toolDispatcher;
        _commitGuard = commitGuard;
        _tokenBudget = tokenBudget;
        _auditLogger = auditLogger;
        _plannerRouter = plannerRouter;
        _stepwiseLoopRunner = stepwiseLoopRunner;
        _serviceProvider = serviceProvider;
        _pluginCatalog = pluginCatalog;
        _moduleRouter = moduleRouter;
    }

    public async Task<ChatCompletionResponse> HandleAsync(ChatCompletionRequest request, ExecutionContext context, CancellationToken cancellationToken)
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
        _ctxAccessor.Context = context;
        _ctxAccessor.ConfirmedConfirmationId = TryExtractConfirmationId(lastUserMessage);

        // 2) build kernel
        var kernel = _kernelFactory.CreateKernel(request.Model);

        // 3) register module plugins (bạn uncomment sau khi đã inject plugins theo module)
        var modules = _moduleRouter.SelectModules(lastUserMessage, context);

        foreach (var module in modules)
        {
            if (!_pluginCatalog.ByModule.TryGetValue(module, out var types)) continue;

            foreach (var t in types)
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
            return BuildResponse(request.Model ?? "TILSOFT-AI", fallback + " __DONE__", incomingMessages);
        }

        // 4) governance filter: chọn 1 trong 2 dòng dưới tùy interface bạn implement
        // Nếu CommitGuardFilter : IAutoFunctionInvocationFilter
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
                content = await _stepwiseLoopRunner.RunAsync(kernel, history, maxIterations: 8, ct: cancellationToken);
            }
            else
            {
                var chat = kernel.GetRequiredService<IChatCompletionService>();
                var settings = new OpenAIPromptExecutionSettings
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                };

                var msg = await chat.GetChatMessageContentAsync(history, settings, kernel, cancellationToken);
                content = (msg.Content ?? string.Empty).Replace("__DONE__", "", StringComparison.OrdinalIgnoreCase).Trim();
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
        - Nếu yêu cầu CẦN dữ liệu nội bộ (giá, tồn kho, đơn hàng, khách hàng, model, doanh số...) thì PHẢI gọi tools để lấy evidence.
        - Nếu câu hỏi là nghiệp vụ ERP và CẦN dữ liệu nội bộ nhưng hệ thống CHƯA có tool phù hợp, hãy trả lời đúng mẫu: "Hiện tại tôi chưa được cập nhật tính năng này!" (có thể kèm gợi ý liên hệ quản trị hệ thống).
        - Nếu câu hỏi KHÔNG cần dữ liệu nội bộ (chào hỏi, giải thích khái niệm, hướng dẫn chung...), bạn được trả lời tự nhiên như một chatbot thông thường.
        - Tuyệt đối không bịa dữ liệu nội bộ khi chưa có evidence từ tool.
        - Chỉ được gọi các tools có trong danh sách hệ thống cung cấp. Tuyệt đối không tự bịa tool (ví dụ: functions.prepare).
        - Thao tác ghi (create/update/commit) phải theo 2 bước: prepare -> yêu cầu xác nhận -> commit.
        - Người dùng xác nhận bằng: XÁC NHẬN <confirmation_id>.

        Kết thúc:
        - Nếu bạn đã hoàn thành câu trả lời cuối cùng, hãy kết thúc bằng token: __DONE__.
        - Nếu còn cần gọi tools hoặc cần hỏi thêm, KHÔNG dùng __DONE__.
        """;

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
