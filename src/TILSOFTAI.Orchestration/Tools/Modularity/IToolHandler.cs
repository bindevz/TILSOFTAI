using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Orchestration.Tools.Modularity;

/// <summary>
/// A single tool implementation (one tool name -> one handler).
/// This enables module-based development without a central switch/case dispatcher.
/// </summary>
public interface IToolHandler
{
    string ToolName { get; }

    Task<ToolDispatchResult> HandleAsync(object intent, TSExecutionContext context, CancellationToken cancellationToken);
}
