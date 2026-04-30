using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TILSOFTAI.Application.Abstractions;
using TILSOFTAI.Contracts.Api;
using TILSOFTAI.Contracts.Configuration;

namespace TILSOFTAI.Infrastructure.LocalAi;

public sealed class OpenAICompatibleLocalAiClient(HttpClient httpClient, TilsoftAiOptions options) : ILocalAiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken cancellationToken)
    {
        OpenAICompatibleOptions aiOptions = options.Ai.OpenAICompatible;
        using HttpRequestMessage httpRequest = new(HttpMethod.Post, new Uri(new Uri(aiOptions.BaseUrl), "chat/completions"));
        AddAuthorization(httpRequest, aiOptions.ApiKey);

        var payload = new OpenAIChatCompletionRequest(
            aiOptions.ChatModel,
            [
                new OpenAIChatMessage("system", request.SystemPrompt),
                new OpenAIChatMessage("user", BuildUserPrompt(request))
            ],
            0.1,
            new OpenAIResponseFormat("json_object"));

        httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(aiOptions.RequestTimeoutSeconds));

        using HttpResponseMessage response = await httpClient.SendAsync(httpRequest, timeoutCts.Token);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Local AI chat call failed with HTTP {(int)response.StatusCode}.");

        string body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
        OpenAIChatCompletionResponse parsed = JsonSerializer.Deserialize<OpenAIChatCompletionResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("Local AI chat response was empty or invalid.");
        string content = parsed.Choices.FirstOrDefault()?.Message.Content
            ?? throw new InvalidOperationException("Local AI chat response did not include message content.");

        FinalAnswer answer;
        try
        {
            answer = JsonSerializer.Deserialize<FinalAnswer>(content, JsonOptions)
                ?? throw new InvalidOperationException("Local AI final answer JSON was invalid.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Local AI final answer JSON was invalid.", ex);
        }
        ValidateAnswer(answer);
        return new AiChatResponse(answer);
    }

    public async Task<AiEmbeddingResponse> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken)
    {
        OpenAICompatibleOptions aiOptions = options.Ai.OpenAICompatible;
        using HttpRequestMessage httpRequest = new(HttpMethod.Post, new Uri(new Uri(aiOptions.BaseUrl), "embeddings"));
        AddAuthorization(httpRequest, aiOptions.ApiKey);
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(new OpenAIEmbeddingRequest(aiOptions.EmbeddingModel, request.Input), JsonOptions), Encoding.UTF8, "application/json");

        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(aiOptions.RequestTimeoutSeconds));

        using HttpResponseMessage response = await httpClient.SendAsync(httpRequest, timeoutCts.Token);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Local AI embedding call failed with HTTP {(int)response.StatusCode}.");

        string body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
        OpenAIEmbeddingResponse parsed = JsonSerializer.Deserialize<OpenAIEmbeddingResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("Local AI embedding response was empty or invalid.");
        float[] vector = parsed.Data.FirstOrDefault()?.Embedding ?? [];
        return new AiEmbeddingResponse(vector, parsed.Model);
    }

    private static string BuildUserPrompt(AiChatRequest request) =>
        "Return strict JSON matching the final answer contract. Use only this sanitized context:\n" + request.ContextPackage.ToJsonString();

    private static void AddAuthorization(HttpRequestMessage request, string apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    private static void ValidateAnswer(FinalAnswer answer)
    {
        if (string.IsNullOrWhiteSpace(answer.Summary))
            throw new InvalidOperationException("Final answer summary is required.");
        if (answer.Provenance.Count == 0)
            throw new InvalidOperationException("Final answer provenance is required.");
    }
}

public sealed record OpenAIChatCompletionRequest(string Model, IReadOnlyList<OpenAIChatMessage> Messages, double Temperature, OpenAIResponseFormat ResponseFormat);
public sealed record OpenAIChatMessage(string Role, string Content);
public sealed record OpenAIResponseFormat(string Type);
public sealed record OpenAIChatCompletionResponse(IReadOnlyList<OpenAIChoice> Choices);
public sealed record OpenAIChoice(OpenAIChatMessage Message);
public sealed record OpenAIEmbeddingRequest(string Model, string Input);
public sealed record OpenAIEmbeddingResponse(string Model, IReadOnlyList<OpenAIEmbeddingData> Data);
public sealed record OpenAIEmbeddingData(float[] Embedding);
