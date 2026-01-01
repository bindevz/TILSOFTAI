using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace TILSOFTAI.Orchestration.SK.Planning
{
    public sealed class StepwiseLoopRunner
    {
        // Circuit breaker thresholds
        private const int MaxConsecutiveFailures = 3;
        private const int MaxConsecutiveEmptyAssistant = 2;
        private const int MaxRepeatSameAssistant = 2;

        public async Task<string> RunAsync(
            Kernel kernel,
            ChatHistory history,
            int maxIterations,
            CancellationToken cancellationToken)
        {
            // Resolve from Kernel (NOT from DI)
            var chat = kernel.GetRequiredService<IChatCompletionService>();

            var settings = new OpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };

            int consecutiveFailures = 0;
            int consecutiveEmptyAssistant = 0;

            string? lastAssistant = null;
            int repeatSameAssistant = 0;

            for (int i = 0; i < maxIterations; i++)
            {
                try
                {
                    var result = await chat.GetChatMessageContentAsync(
                        history,
                        settings,
                        kernel,
                        cancellationToken);

                    var content = result.Content?.Trim();

                    // 2) Không add assistant message rỗng
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        consecutiveEmptyAssistant++;
                        if (consecutiveEmptyAssistant >= MaxConsecutiveEmptyAssistant)
                        {
                            return "Planner stopped: assistant returned empty content repeatedly (no progress).";
                        }

                        // Không coi là exception, nhưng là 1 dạng “no progress”
                        continue;
                    }

                    consecutiveEmptyAssistant = 0;

                    // Track lặp nội dung để circuit-break “kẹt”
                    if (string.Equals(lastAssistant, content, StringComparison.Ordinal))
                    {
                        repeatSameAssistant++;
                        if (repeatSameAssistant >= MaxRepeatSameAssistant)
                        {
                            return "Planner stopped: assistant repeated the same output (no progress).";
                        }
                    }
                    else
                    {
                        lastAssistant = content;
                        repeatSameAssistant = 0;
                    }

                    history.AddAssistantMessage(content);

                    // DONE condition (giữ logic hiện có của bạn)
                    if (content.Contains("__DONE__", StringComparison.OrdinalIgnoreCase))
                    {
                        // Tùy bạn: có thể parse phần answer sau token DONE
                        return content;
                    }

                    // Nếu qua được 1 vòng bình thường thì reset failure counter
                    consecutiveFailures = 0;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    consecutiveFailures++;
                    if (consecutiveFailures >= MaxConsecutiveFailures)
                    {
                        return $"Planner stopped: too many consecutive failures ({MaxConsecutiveFailures}). Last error: {ex.Message}";
                    }

                    // Khuyến nghị: không add error vào history như assistant message,
                    // vì sẽ làm model “ăn” lỗi và có thể lặp sai hành vi.
                    // Nếu cần trace, hãy log ra logger thay vì history.
                }
            }

            return "Planner stopped: reached max iterations.";
        }
    }
}
