namespace TILSOFTAI.Orchestration.Contracts.Validation;

public interface IResponseSchemaValidator
{
    /// <summary>
    /// Validates the given payload (typically ToolExecutionResult.Data) against the corresponding JSON Schema
    /// in governance/contracts, if enabled.
    ///
    /// Must throw <see cref="ResponseContractException"/> on validation failure.
    /// </summary>
    void ValidateOrThrow(object? payload, string toolName);
}
