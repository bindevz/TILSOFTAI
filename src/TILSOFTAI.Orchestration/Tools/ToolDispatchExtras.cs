using TILSOFTAI.Orchestration.Contracts;

namespace TILSOFTAI.Orchestration.Tools;

/// <summary>
/// Optional enterprise stage-2 metadata that the dispatcher can attach to a tool result,
/// surfaced at the envelope level (source/evidence) so the LLM can reliably consume it.
/// </summary>
public sealed record ToolDispatchExtras(
    EnvelopeSourceV1? Source,
    IReadOnlyList<EnvelopeEvidenceItemV1> Evidence)
{
    public static ToolDispatchExtras Empty { get; } = new(null, Array.Empty<EnvelopeEvidenceItemV1>());
}
