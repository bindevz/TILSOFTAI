namespace TILSOFTAI.Orchestration.Tools;

public sealed record ValidationResult<T>(bool IsValid, string? Error, T? Value)
{
    public static ValidationResult<T> Success(T value) => new(true, null, value);
    public static ValidationResult<T> Fail(string error) => new(false, error, default);
}
