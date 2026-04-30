using System.Text.RegularExpressions;
using TILSOFTAI.Application.Abstractions;

namespace TILSOFTAI.Application.Runs;

public sealed partial class ModelParameterBinder : IModelParameterBinder
{
    public ParameterBindingResult BindProjectCode(string question)
    {
        Match invalidModelToken = InvalidModelTokenRegex().Match(question);
        if (invalidModelToken.Success)
            return new(false, null, "InvalidProjectCode", "Project code must match MODEL-000 format.");

        Match match = ProjectCodeRegex().Match(question);
        if (!match.Success)
            return new(false, null, "MissingProjectCode", "Please provide a valid Model project code.");

        return new(true, match.Value.ToUpperInvariant(), null, null);
    }

    [GeneratedRegex(@"\bMODEL-\d{3}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProjectCodeRegex();

    [GeneratedRegex(@"\bMODEL-(?!\d{3}\b)[A-Z0-9]+\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InvalidModelTokenRegex();
}
