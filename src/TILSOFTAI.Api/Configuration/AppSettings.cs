using System.ComponentModel.DataAnnotations;

namespace TILSOFTAI.Configuration;

public sealed class AppSettings
{
    [Required]
    public LlmSettings Llm { get; init; } = new();

    [Required]
    public ChatSettings Chat { get; init; } = new();

    [Required]
    public SqlSettings Sql { get; init; } = new();

    [Required]
    public RedisSettings Redis { get; init; } = new();

    [Required]
    public OrchestrationSettings Orchestration { get; init; } = new();

    [Required]
    public AnalyticsEngineSettings AnalyticsEngine { get; init; } = new();

    [Required]
    public LocalizationSettings Localization { get; init; } = new();
}

public sealed class LlmSettings
{
    [Required]
    public string Endpoint { get; init; } = string.Empty;

    [Required]
    public string Model { get; init; } = string.Empty;

    [Range(1, 1800)]
    public int TimeoutSeconds { get; init; } = 300;
}

public sealed class ChatSettings
{
    [Range(1, 30)]
    public int MaxToolSteps { get; init; } = 8;

    [Range(256, 200000)]
    public int MaxPromptTokensEstimate { get; init; } = 8000;

    [Range(1000, 200000)]
    public int MaxToolResultBytes { get; init; } = 16000;

    [Required]
    public string TrimPolicy { get; init; } = "drop_tools_first";
}

public sealed class SqlSettings
{
    [Required]
    public string ConnectionStringName { get; init; } = "SqlServer";

    [Range(1, 1800)]
    public int CommandTimeoutSeconds { get; init; } = 60;
}

public sealed class RedisSettings
{
    public bool Enabled { get; init; } = false;

    public string? ConnectionString { get; init; }

    [Range(1, 1440)]
    public int DatasetTtlMinutes { get; init; } = 60;
}

public sealed class OrchestrationSettings
{
    [Required]
    [MinLength(1)]
    public string[] ToolAllowlist { get; init; } =
    [
        "atomic.catalog.search",
        "atomic.query.execute",
        "analytics.run"
    ];

    [Required]
    public StrictModeSettings StrictMode { get; init; } = new();
}

public sealed class StrictModeSettings
{
    public bool ResponseSchemaValidationEnabled { get; init; } = true;

    public bool ValidateAllKindsWithSchema { get; init; } = false;

    public bool FailOnMissingSchemaForEnforcedKinds { get; init; } = true;
}

public sealed class AnalyticsEngineSettings
{
    [Range(1, 200000)]
    public int MaxJoinRows { get; init; } = 100000;

    [Range(1, 5000)]
    public int MaxJoinMatchesPerLeft { get; init; } = 50;

    [Range(1, 5000)]
    public int MaxGroups { get; init; } = 200;

    [Range(1, 200)]
    public int PreviewRowLimit { get; init; } = 20;

    [Required]
    public JoinKeyOptions JoinKeyOptions { get; init; } = new();
}

public sealed class JoinKeyOptions
{
    public JoinKeyStringComparison StringComparison { get; init; } = JoinKeyStringComparison.Ordinal;

    public NumericStringCoercion NumericStringCoercion { get; init; } = NumericStringCoercion.None;

    public DateNormalization DateNormalization { get; init; } = DateNormalization.UtcTicks;
}

public enum JoinKeyStringComparison
{
    Ordinal,
    OrdinalIgnoreCase
}

public enum NumericStringCoercion
{
    None,
    Safe
}

public enum DateNormalization
{
    None,
    UtcTicks
}

public sealed class LocalizationSettings
{
    [Required]
    public string DefaultCulture { get; init; } = "en";

    [Required]
    [MinLength(1)]
    public string[] SupportedCultures { get; init; } = ["en", "vi"];
}
