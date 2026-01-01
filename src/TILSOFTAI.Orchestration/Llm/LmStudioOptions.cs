namespace TILSOFTAI.Orchestration.Llm;

public sealed class LmStudioOptions
{
    public string BaseUrl { get; set; } = "http://172.16.54.10:6688";
    public string Model { get; set; } = "TILSOFT-AI";
    public int TimeoutSeconds { get; set; } = 30;
    public Dictionary<string, string> ModelMap { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        { "TILSOFT-AI", "meta-llama-3.1-8b-instruct" }
    };
}
