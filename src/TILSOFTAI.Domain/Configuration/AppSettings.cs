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
    public ApiSettings Api { get; init; } = new();

    [Required]
    public OrchestrationSettings Orchestration { get; init; } = new();

    [Required]
    public AnalyticsEngineSettings AnalyticsEngine { get; init; } = new();

    [Required]
    public LocalizationSettings Localization { get; init; } = new();

    [Required]
    public EntityGraphSettings EntityGraph { get; init; } = new();

    [Required]
    public EmbeddingsSettings Embeddings { get; init; } = new();

    [Required]
    public DocumentSearchSettings DocumentSearch { get; init; } = new();
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

    [Required]
    public CompactionLimitsSettings CompactionLimits { get; init; } = new();
}

public sealed class SqlSettings
{
    [Required]
    public string ConnectionStringName { get; init; } = "SqlServer";

    [Range(1, 1800)]
    public int CommandTimeoutSeconds { get; init; } = 60;
}

public sealed class CompactionLimitsSettings
{
    [Range(1, 20)]
    public int MaxDepth { get; init; } = 6;

    [Range(1, 200)]
    public int MaxArrayElements { get; init; } = 20;

    [Range(50, 5000)]
    public int MaxStringLength { get; init; } = 500;
}

public sealed class RedisSettings
{
    public bool Enabled { get; init; } = false;

    public string? ConnectionString { get; init; }

    [Range(1, 1440)]
    public int DatasetTtlMinutes { get; init; } = 60;
}

public sealed class ApiSettings
{
    [Required]
    public RateLimitSettings RateLimit { get; init; } = new();
}

public sealed class RateLimitSettings
{
    [Range(1, 10000)]
    public int RequestsPerMinute { get; init; } = 120;

    [Range(1, 3600)]
    public int BlockDurationSeconds { get; init; } = 30;
}

public sealed class OrchestrationSettings
{
    [Required]
    [MinLength(1)]
    public string[] ToolAllowlist { get; init; } =
    [
        "atomic.catalog.search",
        "atomic.query.execute",
        "atomic.graph.search",
        "atomic.graph.get",
        "atomic.doc.search",
        "analytics.run"
    ];

    [Required]
    public StrictModeSettings StrictMode { get; init; } = new();

    [Required]
    public ToolConfigurationValidationSettings ToolConfigurationValidation { get; init; } = new();

    [Required]
    public AtomicQueryLimitsSettings AtomicQueryLimits { get; init; } = new();
}

public sealed class ToolConfigurationValidationSettings
{
    /// <summary>
    /// Enables the startup tool configuration validator.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// If true, the application will fail startup when a validation error is detected.
    /// If false, errors are logged and the application continues.
    /// </summary>
    public bool FailFast { get; init; } = true;

    /// <summary>
    /// Validates that every tool in Orchestration.ToolAllowlist has an RBAC mapping.
    /// </summary>
    public bool ValidateRbacMappings { get; init; } = true;

    /// <summary>
    /// Validates that every tool in Orchestration.ToolAllowlist is registered in ToolRegistry.
    /// </summary>
    public bool ValidateToolRegistry { get; init; } = true;

    /// <summary>
    /// Validates that every tool in Orchestration.ToolAllowlist has an input spec in ToolInputSpecCatalog.
    /// </summary>
    public bool ValidateToolInputSpecs { get; init; } = true;
}

public sealed class StrictModeSettings
{
    public bool ResponseSchemaValidationEnabled { get; init; } = true;

    public bool ValidateAllKindsWithSchema { get; init; } = false;

    public bool FailOnMissingSchemaForEnforcedKinds { get; init; } = true;
}

public sealed class AtomicQueryLimitsSettings
{
    [Range(1, 200000)]
    public int MaxRowsPerTable { get; init; } = 20000;

    [Range(0, 50000)]
    public int MaxRowsSummary { get; init; } = 500;

    [Range(1, 500000)]
    public int MaxSchemaRows { get; init; } = 50000;

    [Range(1, 100)]
    public int MaxTables { get; init; } = 20;

    [Range(1, 500)]
    public int MaxColumns { get; init; } = 100;

    [Range(1, 20000)]
    public int MaxDisplayRows { get; init; } = 2000;

    [Range(0, 200)]
    public int PreviewRows { get; init; } = 100;
}

public sealed class AnalyticsEngineSettings
{
    [Range(1, 200000)]
    public int MaxJoinRows { get; init; } = 100000;

    [Range(1, 5000)]
    public int MaxJoinMatchesPerLeft { get; init; } = 50;

    [Range(1, 5000)]
    public int MaxGroups { get; init; } = 200;

    [Range(1, 5000)]
    public int MaxResultRows { get; init; } = 500;

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

public sealed class EntityGraphSettings
{
    public bool Enabled { get; init; } = true;

    [Required]
    public string SearchSpName { get; init; } = "dbo.TILSOFTAI_sp_EntityGraph_Search";

    [Required]
    public string GetSpName { get; init; } = "dbo.TILSOFTAI_sp_EntityGraph_Get";
}


public sealed class EmbeddingsSettings
{
    public bool Enabled { get; init; } = true;

    [Required]
    public string Endpoint { get; init; } = string.Empty;

    [Required]
    public string Model { get; init; } = "text-embedding-3-small";

    [Range(1, 3600)]
    public int TimeoutSeconds { get; init; } = 120;

    [Range(1, 8192)]
    public int Dimensions { get; init; } = 1536;
}

public sealed class DocumentSearchSettings
{
    public bool Enabled { get; init; } = true;

    [Required]
    public string SearchSpName { get; init; } = "dbo.TILSOFTAI_sp_DocChunk_Search";

    [Range(1, 50)]
    public int MaxTopK { get; init; } = 8;
}
