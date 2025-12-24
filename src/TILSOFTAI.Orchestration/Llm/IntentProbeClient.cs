using System.Text.Json;
using System.Text.Json.Serialization;
using TILSOFTAI.Orchestration.Chat;

namespace TILSOFTAI.Orchestration.Llm;

public sealed class IntentProbeClient
{
    private readonly LmStudioClient _client;
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    private const string ProbePrompt = """
You are an intent probe. Return ONLY JSON.
Allowed keys: mode, confidence.
Mode must be one of: conversation, erp_intent.
Example: {"mode":"erp_intent","confidence":0.9}
""";

    public IntentProbeClient(LmStudioClient client)
    {
        _client = client;
    }

    public async Task<ProbeResult> ProbeAsync(string? model, string userContent, CancellationToken cancellationToken)
    {
        var messages = new[]
        {
            new ChatCompletionMessage { Role = "system", Content = ProbePrompt },
            new ChatCompletionMessage { Role = "user", Content = userContent }
        };

        var content = await _client.GetConversationAsync(model, messages, cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ResponseContractException("Probe returned empty content.");
        }

        try
        {
            var result = JsonSerializer.Deserialize<ProbeResult>(content, _options);
            if (result is null || string.IsNullOrWhiteSpace(result.Mode))
            {
                throw new ResponseContractException("Probe invalid.");
            }

            return result;
        }
        catch (JsonException)
        {
            throw new ResponseContractException("Probe invalid.");
        }
    }
}

public sealed class ProbeResult
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }
}
