namespace TILSOFTAI.Orchestration.Execution;

public sealed class ExecutionContextAccessor
{
    public TILSOFTAI.Domain.ValueObjects.TSExecutionContext Context { get; set; } = default!;

    public string? LastListPreviewMarkdown { get; set; }
    public string? LastInsightPreviewMarkdown { get; set; }
    public string? LastSchemaDigest { get; set; }
    public string? LastDatasetDigest { get; set; }

    public List<string> Diagnostics { get; } = new();
}
