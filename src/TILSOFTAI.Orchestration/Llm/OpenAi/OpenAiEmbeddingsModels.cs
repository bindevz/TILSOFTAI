using System.Text.Json.Serialization;

namespace TILSOFTAI.Orchestration.Llm.OpenAi;

public sealed class OpenAiEmbeddingRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("input")]
    public string Input { get; set; } = string.Empty;

    // Some OpenAI-compatible servers support this.
    [JsonPropertyName("dimensions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Dimensions { get; set; }
}

public sealed class OpenAiEmbeddingResponse
{
    [JsonPropertyName("data")]
    public List<OpenAiEmbeddingData>? Data { get; set; }
}

public sealed class OpenAiEmbeddingData
{
    [JsonPropertyName("embedding")]
    public float[]? Embedding { get; set; }
}
