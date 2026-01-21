namespace TILSOFTAI.Infrastructure.Options;

public sealed class SqlOptions
{
    public int CommandTimeoutSeconds { get; set; } = 60;
}
