namespace TILSOFTAI.Contracts.Configuration;

public sealed class TilsoftAiOptions
{
    public ConnectionStringOptions ConnectionStrings { get; init; } = new();
    public AiOptions Ai { get; init; } = new();
    public AgentOptions Agent { get; init; } = new();
    public ArtifactOptions Artifacts { get; init; } = new();
    public SecurityOptions Security { get; init; } = new();
    public ObservabilityOptions Observability { get; init; } = new();
}

public sealed class ConnectionStringOptions
{
    public string TilsoftAi { get; init; } = string.Empty;
}

public sealed class AiOptions
{
    public string Provider { get; init; } = "OpenAICompatible";
    public OpenAICompatibleOptions OpenAICompatible { get; init; } = new();
}

public sealed class OpenAICompatibleOptions
{
    public string BaseUrl { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string ChatModel { get; init; } = string.Empty;
    public string EmbeddingModel { get; init; } = string.Empty;
    public int RequestTimeoutSeconds { get; init; } = 120;
    public int EmbeddingDimensions { get; init; } = 1536;
}

public sealed class AgentOptions
{
    public int MaxToolCalls { get; init; } = 5;
    public int MaxRowsPerTool { get; init; } = 5000;
    public int MaxExecutionSeconds { get; init; } = 60;
    public bool RequireArtifactPersistence { get; init; } = true;
}

public sealed class ArtifactOptions
{
    public string Provider { get; init; } = "FileSystem";
    public string RootPath { get; init; } = string.Empty;
}

public sealed class SecurityOptions
{
    public bool RequireTenantHeader { get; init; } = true;
    public bool RequireUserHeader { get; init; } = true;
}

public sealed class ObservabilityOptions
{
    public string ServiceName { get; init; } = "TILSOFTAI";
    public bool EnableOpenTelemetry { get; init; } = true;
}

