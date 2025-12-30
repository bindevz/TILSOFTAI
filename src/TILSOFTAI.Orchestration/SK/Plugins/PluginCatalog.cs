namespace TILSOFTAI.Orchestration.SK.Plugins
{
    public sealed class PluginCatalog
    {
        public IReadOnlyDictionary<string, List<Type>> ByModule { get; }

        public PluginCatalog()
        {
            var dict = new Dictionary<string, List<Type>>(StringComparer.OrdinalIgnoreCase);

            var all = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("ToolsPlugin"));

            foreach (var t in all)
            {
                var mods = t.GetCustomAttributes(typeof(SkModuleAttribute), inherit: true)
                            .Cast<SkModuleAttribute>()
                            .Select(x => x.Name)
                            .DefaultIfEmpty(ToPluginName(t.Name));

                foreach (var m in mods)
                {
                    if (!dict.TryGetValue(m, out var list))
                        dict[m] = list = new List<Type>();
                    list.Add(t);
                }
            }

            ByModule = dict;
        }

        private static string ToPluginName(string typeName)
            => char.ToLowerInvariant(typeName[0]) + typeName.Replace("ToolsPlugin", "").Substring(1);
    }
}
