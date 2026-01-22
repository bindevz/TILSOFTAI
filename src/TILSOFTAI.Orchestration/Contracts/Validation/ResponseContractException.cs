namespace TILSOFTAI.Orchestration.Contracts.Validation;

/// <summary>
/// Indicates a server-side response payload violated its governance schema.
/// This is non-retryable and should surface as a deterministic failure.
/// </summary>
public sealed class ResponseContractException : Exception
{
    public ResponseContractException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
