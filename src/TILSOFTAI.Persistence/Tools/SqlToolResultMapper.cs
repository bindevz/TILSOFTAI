namespace TILSOFTAI.Persistence.Tools;

public static class SqlToolResultMapper
{
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> Normalize(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        return rows.Select(row => row.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase) as IReadOnlyDictionary<string, object?>).ToList();
    }
}

