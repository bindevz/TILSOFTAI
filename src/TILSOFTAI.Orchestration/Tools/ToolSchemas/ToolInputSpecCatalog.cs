using System.Collections.ObjectModel;

namespace TILSOFTAI.Orchestration.Tools.ToolSchemas;

public interface IToolInputSpecProvider
{
    IEnumerable<ToolInputSpec> GetSpecs();
}

public sealed class ToolInputSpecCatalog
{
    private readonly IReadOnlyDictionary<string, ToolInputSpec> _specs;

    public ToolInputSpecCatalog(IEnumerable<IToolInputSpecProvider> providers)
    {
        var dict = new Dictionary<string, ToolInputSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in providers)
        {
            foreach (var s in p.GetSpecs())
            {
                if (string.IsNullOrWhiteSpace(s.ToolName))
                    throw new InvalidOperationException("ToolInputSpec has empty ToolName.");

                if (!dict.TryAdd(s.ToolName, s))
                    throw new InvalidOperationException($"Duplicate ToolInputSpec registered: {s.ToolName}");
            }
        }

        _specs = new ReadOnlyDictionary<string, ToolInputSpec>(dict);
    }

    public bool TryGet(string toolName, out ToolInputSpec spec)
        => _specs.TryGetValue(toolName, out spec!);
}
