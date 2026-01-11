namespace TILSOFTAI.Orchestration.Contracts.Validation;

/// <summary>
/// Runtime response contract validation (governance/contracts).
/// </summary>
public sealed class ResponseSchemaValidationOptions
{
    public bool Enabled { get; set; } = true;

    public bool ValidateAllKindsWithSchema { get; set; } = false;

    public bool FailOnMissingSchemaForEnforcedKinds { get; set; } = true;

    public string? ContractsRootPath { get; set; }

    /// <summary>
    /// Kinds that must be validated at runtime.
    /// Keep this list small and stable (fail-closed when removing schemas).
    /// </summary>
    public string[] EnforcedKinds { get; set; } =
    [
        "atomic.query.execute.v1",
        "atomic.catalog.search.v1"
    ];
}
