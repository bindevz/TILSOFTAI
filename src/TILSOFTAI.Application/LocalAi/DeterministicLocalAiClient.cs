using TILSOFTAI.Application.Abstractions;
using TILSOFTAI.Contracts.Api;

namespace TILSOFTAI.Application.LocalAi;

public sealed class DeterministicLocalAiClient : ILocalAiClient
{
    public Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken cancellationToken)
    {
        Guid artifactId = Guid.Parse(request.ContextPackage["provenance"]![0]!["artifactId"]!.GetValue<string>());
        string toolName = request.ContextPackage["provenance"]![0]!["toolName"]!.GetValue<string>();
        string summary = request.UserPrompt.Contains("MODEL-002", StringComparison.OrdinalIgnoreCase)
            ? "MODEL-002 latest run status is available in the Model domain evidence."
            : "MODEL-001 achieved its run target with an overall score of 96.5.";

        FinalAnswer answer = new(
            summary,
            [new AnswerTable("Run Verification Summary", ["Metric", "Value"], [["Run Status", "Passed"], ["Overall Score", "96.5"], ["Failed Checks", "0"], ["Warning Checks", "1"]])],
            ["The project passed all mandatory checks. One warning should be reviewed before final approval."],
            ["The conclusion is based on the latest run record available to the Model domain."],
            [new AnswerProvenance(toolName, ["ProjectCode = MODEL-001"], artifactId)],
            ["Review the warning check details.", "Compare this run against the previous run.", "Export the verification evidence."]);

        return Task.FromResult(new AiChatResponse(answer));
    }

    public Task<AiEmbeddingResponse> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken)
    {
        float[] vector = Enumerable.Repeat(0.01f, 8).ToArray();
        return Task.FromResult(new AiEmbeddingResponse(vector, "configured-embedding-model"));
    }
}

