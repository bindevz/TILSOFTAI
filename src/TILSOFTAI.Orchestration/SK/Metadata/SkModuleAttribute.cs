[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class SkModuleAttribute : Attribute
{
    public string Name { get; }
    public SkModuleAttribute(string name) => Name = name;
}
