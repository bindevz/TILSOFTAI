namespace TILSOFTAI.Orchestration.Llm;

public sealed class LmStudioOptions
{
    public string BaseUrl { get; set; } = "http://192.168.8.247:6688";
    public string Model { get; set; } = "TILSOFT-AI";
    public int TimeoutSeconds { get; set; } = 30;
    public Dictionary<string, string> ModelMap { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        { "TILSOFT-AI", "qwen/qwen3-vl-30b" }
    };
}
