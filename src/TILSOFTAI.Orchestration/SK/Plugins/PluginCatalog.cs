using System.Reflection;

namespace TILSOFTAI.Orchestration.SK.Plugins;

public sealed class PluginCatalog
{
    public IReadOnlyDictionary<string, List<Type>> ByModule { get; }

    /// <summary>
    /// Builds a module -> plugin type index.
    /// Pass assemblies explicitly for deterministic behavior, or use the parameterless constructor to scan loaded assemblies.
    /// </summary>
    public PluginCatalog(params Assembly[] assemblies)
    {
        var dict = new Dictionary<string, List<Type>>(StringComparer.OrdinalIgnoreCase);

        var targetAssemblies = assemblies is { Length: > 0 }
            ? assemblies
            : AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic).ToArray();

        foreach (var asm in targetAssemblies)
        {
            if (asm.IsDynamic) continue;

            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).Cast<Type>().ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var t in types)
            {
                if (!t.IsClass || t.IsAbstract) continue;
                if (!t.Name.EndsWith("ToolsPlugin", StringComparison.Ordinal)) continue;

                var mods = t.GetCustomAttributes(typeof(SkModuleAttribute), inherit: true)
                    .Cast<SkModuleAttribute>()
                    .Select(x => x.Name)
                    .DefaultIfEmpty(ToDefaultModuleName(t.Name));

                foreach (var m in mods)
                {
                    if (!dict.TryGetValue(m, out var list))
                        dict[m] = list = new List<Type>();
                    list.Add(t);
                }
            }
        }

        ByModule = dict;
    }

    private static string ToDefaultModuleName(string typeName)
    {
        const string suffix = "ToolsPlugin";
        var name = typeName.EndsWith(suffix, StringComparison.Ordinal)
            ? typeName[..^suffix.Length]
            : typeName;

        if (string.IsNullOrWhiteSpace(name)) return "common";
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }
}
