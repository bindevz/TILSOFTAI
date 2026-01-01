using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace TILSOFTAI.Orchestration.SK.Planning
{
    public sealed class StepwiseLoopRunner
    {
        // Circuit breaker thresholds (assistant-level, tool-level guards are handled by AutoInvocationCircuitBreakerFilter)
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
                            return "Tôi đã thực hiện các bước cần thiết nhưng chưa thể tổng hợp câu trả lời. Vui lòng thử lại hoặc cung cấp thêm tiêu chí.";
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
                            return "Tôi đang bị lặp trong quá trình xử lý. Vui lòng nêu rõ hơn yêu cầu hoặc cung cấp thêm tiêu chí.";
                        }
                    }
                    else
                    {
                        lastAssistant = content;
                        repeatSameAssistant = 0;
                    }

                    history.AddAssistantMessage(content);

                    // New stop condition: first non-empty assistant output.
                    // Tool calling (if needed) is handled internally by SK when FunctionChoiceBehavior.Auto is enabled.
                    // Multi-turn business workflows (prepare -> confirm -> commit) are handled across user turns.
                    return content;
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
                        return "Tạm thời tôi không thể xử lý yêu cầu do lỗi hệ thống. Vui lòng thử lại.";
                    }

                    // Khuyến nghị: không add error vào history như assistant message,
                    // vì sẽ làm model “ăn” lỗi và có thể lặp sai hành vi.
                    // Nếu cần trace, hãy log ra logger thay vì history.
                }
            }

            return "Hệ thống đã thực thi các bước cần thiết nhưng chưa thể tổng hợp câu trả lời. Vui lòng thử lại hoặc cung cấp thêm tiêu chí.";
        }
    }
}
