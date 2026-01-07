using System.Collections.ObjectModel;

namespace TILSOFTAI.Orchestration.Tools.ActionsCatalog;

public interface IActionsCatalogRegistry
{
    IReadOnlyCollection<ActionDescriptor> List();
    bool TryGet(string action, out ActionDescriptor descriptor);
}

/// <summary>
/// Module extension point. Each module can contribute its own write action descriptors.
/// </summary>
public interface IActionsCatalogProvider
{
    IEnumerable<ActionDescriptor> GetActions();
}

/// <summary>
/// Aggregates write action descriptors from all loaded modules.
/// </summary>
public sealed class ActionCatalogRegistry : IActionsCatalogRegistry
{
    private readonly IReadOnlyDictionary<string, ActionDescriptor> _actions;

    public ActionCatalogRegistry(IEnumerable<IActionsCatalogProvider> providers)
    {
        var dict = new Dictionary<string, ActionDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in providers)
        {
            foreach (var a in p.GetActions())
            {
                if (string.IsNullOrWhiteSpace(a.Action))
                    throw new InvalidOperationException("Action catalog has empty action key.");

                if (!dict.TryAdd(a.Action, a))
                    throw new InvalidOperationException($"Duplicate actions.catalog action registered: {a.Action}");
            }
        }

        _actions = new ReadOnlyDictionary<string, ActionDescriptor>(dict);
    }

    public IReadOnlyCollection<ActionDescriptor> List() => _actions.Values.ToList();

    public bool TryGet(string action, out ActionDescriptor descriptor)
        => _actions.TryGetValue(action, out descriptor!);
}
