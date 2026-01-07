namespace TILSOFTAI.Orchestration.SK.Planning;

public interface IPluginExposurePolicy
{
    bool CanHandle(string module);
    IReadOnlyCollection<Type> Select(string module, IReadOnlyCollection<Type> candidates, string lastUserMessage);
}

public sealed class DefaultPluginExposurePolicy : IPluginExposurePolicy
{
    public bool CanHandle(string module) => true;

    public IReadOnlyCollection<Type> Select(string module, IReadOnlyCollection<Type> candidates, string lastUserMessage)
        => candidates;
}
