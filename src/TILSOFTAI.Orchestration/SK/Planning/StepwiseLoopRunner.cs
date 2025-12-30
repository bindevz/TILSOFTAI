using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace TILSOFTAI.Orchestration.SK.Planning;

public sealed class StepwiseLoopRunner
{
    private const string DoneToken = "__DONE__";

    public async Task<string> RunAsync(Kernel kernel, ChatHistory history, int maxIterations, CancellationToken ct)
    {
        var chat = kernel.GetRequiredService<IChatCompletionService>();

        for (var i = 0; i < maxIterations; i++)
        {
            var settings = new OpenAIPromptExecutionSettings
            {
                // Cho phép model tự gọi tools và SK tự invoke
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };

            var msg = await chat.GetChatMessageContentAsync(history, settings, kernel, ct);
            var content = msg.Content ?? string.Empty;

            // Nếu model báo đã xong -> return
            if (content.Contains(DoneToken, StringComparison.OrdinalIgnoreCase))
            {
                return content.Replace(DoneToken, "", StringComparison.OrdinalIgnoreCase).Trim();
            }

            // Nếu chưa xong -> add assistant content vào history rồi chạy vòng tiếp
            history.AddAssistantMessage(content);
        }

        // Fallback: hết vòng mà chưa DONE -> trả nội dung cuối
        return history.LastOrDefault()?.Content ?? string.Empty;
    }
}
