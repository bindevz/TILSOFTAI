namespace TILSOFTAI.Orchestration.Contracts.Validation;

/// <summary>
/// Runtime response contract validation (governance/contracts).
/// </summary>
public sealed class ResponseSchemaValidationOptions
{
    /// <summary>
    /// Enables response payload validation.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// If true, any payload that carries (kind, schemaVersion) and has a schema present will be validated.
    /// If false, only <see cref="EnforcedKinds"/> are validated.
    /// </summary>
    public bool ValidateAllKindsWithSchema { get; set; } = false;

    /// <summary>
    /// If true, enforced kinds must have a schema present; otherwise a contract error is raised.
    /// </summary>
    public bool FailOnMissingSchemaForEnforcedKinds { get; set; } = true;

    /// <summary>
    /// Optional override path to governance/contracts.
    /// If not set, the validator will attempt to locate it automatically.
    /// </summary>
    public string? ContractsRootPath { get; set; }

    /// <summary>
    /// Kinds that must be validated at runtime.
    /// </summary>
    public string[] EnforcedKinds { get; set; } =
    [
        "models.search.v2",
        "models.get.v2",
        "models.attributes.list.v2",
        "models.price.analyze.v2"
    ];
}
