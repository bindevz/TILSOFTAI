using TILSOFTAI.Application.Abstractions;
using TILSOFTAI.Contracts.Api;

namespace TILSOFTAI.Application.Testing;

public sealed class TestingLocalAiClient : ILocalAiClient
{
    public Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken cancellationToken)
    {
        Guid artifactId = Guid.Parse(request.ContextPackage["provenance"]![0]!["artifactId"]!.GetValue<string>());
        string toolName = request.ContextPackage["provenance"]![0]!["toolName"]!.GetValue<string>();

        FinalAnswer answer = new(
            "The Model run answer is grounded in sanitized artifact evidence.",
            [new AnswerTable("Run Evidence", ["Metric", "Value"], [["Run Status", "Passed"], ["Overall Score", "96.5"], ["Failed Checks", "0"], ["Warning Checks", "1"]])],
            ["The project passed all mandatory checks. Warning details should be reviewed."],
            ["The conclusion is based on the latest Model-domain run evidence available to this request."],
            [new AnswerProvenance(toolName, ["ProjectCode = MODEL-001"], artifactId)],
            ["Review warning check details.", "Compare against the previous run.", "Export verification evidence."]);

        return Task.FromResult(new AiChatResponse(answer));
    }

    public Task<AiEmbeddingResponse> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken)
    {
        float[] vector = Enumerable.Repeat(0.01f, 8).ToArray();
        return Task.FromResult(new AiEmbeddingResponse(vector, "testing-embedding-model"));
    }
}

