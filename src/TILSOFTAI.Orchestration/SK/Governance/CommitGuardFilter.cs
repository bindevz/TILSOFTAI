using Microsoft.SemanticKernel;
using TILSOFTAI.Orchestration.SK;

namespace TILSOFTAI.Orchestration.SK.Governance;

public sealed class CommitGuardFilter : IAutoFunctionInvocationFilter
{
    private readonly ExecutionContextAccessor _ctx;

    public CommitGuardFilter(ExecutionContextAccessor ctx) => _ctx = ctx;

    public async Task OnAutoFunctionInvocationAsync(
        AutoFunctionInvocationContext context,
        Func<AutoFunctionInvocationContext, Task> next)
    {
        var fn = context.Function.Name;
        var isCommit = fn.EndsWith("commit", StringComparison.OrdinalIgnoreCase);

        if (!isCommit)
        {
            await next(context);
            return;
        }

        if (string.IsNullOrWhiteSpace(_ctx.ConfirmedConfirmationId))
        {
            // Chặn luôn commit
            context.Result = new FunctionResult(
                context.Function,
                "BLOCKED: Cần xác nhận từ người dùng. Vui lòng trả lời: XÁC NHẬN <confirmation_id>.");
            context.Terminate = true; // dừng chuỗi auto function calling
            return;
        }

        if (!context.Arguments.TryGetValue("confirmationId", out var arg))
        {
            context.Result = new FunctionResult(context.Function, "BLOCKED: confirmationId missing.");
            context.Terminate = true;
            return;
        }

        var provided = arg?.ToString();
        if (!string.Equals(provided, _ctx.ConfirmedConfirmationId, StringComparison.OrdinalIgnoreCase))
        {
            context.Result = new FunctionResult(context.Function, "BLOCKED: confirmationId chưa được xác nhận.");
            context.Terminate = true;
            return;
        }

        await next(context);
    }
}
