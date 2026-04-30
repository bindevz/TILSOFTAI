using TILSOFTAI.Contracts.Api;
using TILSOFTAI.Contracts.Tools;

namespace TILSOFTAI.Application.Runs;

public sealed class FinalAnswerProvenanceValidator
{
    public FinalAnswer ValidateAndAttachSystemProvenance(
        FinalAnswer modelAnswer,
        ToolExecutionResult toolResult,
        Guid sanitizedArtifactId)
    {
        AnswerProvenance systemProvenance = new(toolResult.ToolName, toolResult.Filters, sanitizedArtifactId);

        if (modelAnswer.Provenance.Any(p => p.ArtifactId != sanitizedArtifactId || !string.Equals(p.ToolName, toolResult.ToolName, StringComparison.Ordinal)))
        {
            return modelAnswer with { Provenance = [systemProvenance] };
        }

        if (modelAnswer.Provenance.Count == 0)
            return modelAnswer with { Provenance = [systemProvenance] };

        return modelAnswer with { Provenance = [systemProvenance] };
    }
}

