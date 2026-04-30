using TILSOFTAI.Contracts.Configuration;

namespace TILSOFTAI.Application.Security;

public static class ConfigurationValidator
{
    public static IReadOnlyList<string> Validate(TilsoftAiOptions options)
    {
        List<string> errors = [];

        if (string.IsNullOrWhiteSpace(options.ConnectionStrings.TilsoftAi))
            errors.Add("ConnectionStrings:TilsoftAi is required.");
        if (!Uri.TryCreate(options.Ai.OpenAICompatible.BaseUrl, UriKind.Absolute, out _))
            errors.Add("Ai:OpenAICompatible:BaseUrl must be an absolute URL.");
        if (string.IsNullOrWhiteSpace(options.Ai.OpenAICompatible.ChatModel))
            errors.Add("Ai:OpenAICompatible:ChatModel is required.");
        if (string.IsNullOrWhiteSpace(options.Ai.OpenAICompatible.EmbeddingModel))
            errors.Add("Ai:OpenAICompatible:EmbeddingModel is required.");
        if (options.Ai.OpenAICompatible.RequestTimeoutSeconds <= 0)
            errors.Add("Ai:OpenAICompatible:RequestTimeoutSeconds must be greater than zero.");
        if (options.Ai.OpenAICompatible.EmbeddingDimensions <= 0)
            errors.Add("Ai:OpenAICompatible:EmbeddingDimensions must be greater than zero.");
        if (string.IsNullOrWhiteSpace(options.Artifacts.RootPath))
            errors.Add("Artifacts:RootPath is required.");
        if (options.Agent.MaxToolCalls <= 0)
            errors.Add("Agent:MaxToolCalls must be greater than zero.");
        if (options.Agent.MaxRowsPerTool <= 0)
            errors.Add("Agent:MaxRowsPerTool must be greater than zero.");
        if (options.Agent.MaxExecutionSeconds <= 0)
            errors.Add("Agent:MaxExecutionSeconds must be greater than zero.");

        return errors;
    }

    public static string Redact(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Length <= 4 ? "****" : string.Concat(value.AsSpan(0, 2), "****", value.AsSpan(value.Length - 2));
    }
}

