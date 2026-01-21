using System.Text.Json;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Application.Services;

/// <summary>
/// Application service for governed stored procedure discovery and validation.
/// </summary>
public sealed class AtomicCatalogService
{
    private readonly IAtomicCatalogRepository _repo;

    public AtomicCatalogService(IAtomicCatalogRepository repo)
    {
        _repo = repo;
    }

    public Task<IReadOnlyList<AtomicCatalogSearchHit>> SearchAsync(
        string query,
        int topK,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("query is required.");

        topK = Math.Clamp(topK, 1, 20);
        return _repo.SearchAsync(query.Trim(), topK, cancellationToken);
    }

    public async Task<AtomicCatalogEntry> GetRequiredAllowedAsync(
        string storedProcedure,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storedProcedure))
            throw new ArgumentException("storedProcedure is required.");

        var entry = await _repo.GetByNameAsync(storedProcedure.Trim(), cancellationToken);
        if (entry is null)
            throw new ArgumentException("spName is not registered in catalog. Use atomic.catalog.search first.");

        if (!entry.IsEnabled)
            throw new ArgumentException("spName is disabled in catalog.");

        if (!entry.IsReadOnly)
            throw new ArgumentException("spName is not marked read-only in catalog.");

        if (!entry.IsAtomicCompatible)
            throw new ArgumentException("spName is not marked AtomicQuery-compatible in catalog.");

        return entry;
    }

    public static IReadOnlyList<AtomicCatalogParamSpec> ParseParamSpecs(string? paramsJson)
    {
        if (string.IsNullOrWhiteSpace(paramsJson))
            return Array.Empty<AtomicCatalogParamSpec>();

        try
        {
            using var doc = JsonDocument.Parse(paramsJson);
            var root = doc.RootElement;

            // Preferred shape: [ { name, sqlType, required, description_vi, description_en, default, example } ]
            if (root.ValueKind == JsonValueKind.Array)
            {
                return root.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.Object)
                    .Select(ParseParamObject)
                    .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                    .ToArray();
            }

            // Alternative shape: { params: [ ... ] } or { allowedParams: [ ... ] }
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (TryGetArray(root, "params", out var arr) || TryGetArray(root, "allowedParams", out arr))
                {
                    return arr.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.Object)
                        .Select(ParseParamObject)
                        .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                        .ToArray();
                }

                // allow: { names: ["@A","@B"] }
                if (TryGetArray(root, "names", out arr))
                {
                    return arr.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => new AtomicCatalogParamSpec(NormalizeParamName(e.GetString()), null, false, null, null, null, null))
                        .ToArray();
                }
            }
        }
        catch
        {
            // Fail-closed: treat as no params.
        }

        return Array.Empty<AtomicCatalogParamSpec>();
    }

    public static IReadOnlySet<string> GetAllowedParamNames(string? paramsJson)
    {
        var specs = ParseParamSpecs(paramsJson);
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in specs)
        {
            var n = NormalizeParamName(p.Name);
            if (!string.IsNullOrWhiteSpace(n))
                set.Add(n);
        }
        return set;
    }

    public static IReadOnlyDictionary<string, object?> GetDefaultParamValues(string? paramsJson)
    {
        var specs = ParseParamSpecs(paramsJson);
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in specs)
        {
            if (!p.HasDefault)
                continue;

            var name = NormalizeParamName(p.Name);
            if (!string.IsNullOrWhiteSpace(name))
                dict[name] = p.DefaultValue;
        }
        return dict;
    }

    private static AtomicCatalogParamSpec ParseParamObject(JsonElement o)
    {
        var name = NormalizeParamName(GetString(o, "name") ?? GetString(o, "param") ?? GetString(o, "key"));
        var sqlType = GetString(o, "sqlType") ?? GetString(o, "type");
        var required = GetBool(o, "required") ?? false;
        var descVi = GetString(o, "description_vi") ?? GetString(o, "desc_vi") ?? GetString(o, "descriptionVi");
        var descEn = GetString(o, "description_en") ?? GetString(o, "desc_en") ?? GetString(o, "descriptionEn");
        var example = GetString(o, "example");

        object? def = null;
        var hasDefault = o.TryGetProperty("default", out var d);
        if (hasDefault)
        {
            def = d.ValueKind switch
            {
                JsonValueKind.String => d.GetString(),
                JsonValueKind.Number => d.TryGetInt64(out var l) ? l : (d.TryGetDecimal(out var dec) ? dec : d.GetDouble()),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => d.GetRawText()
            };
        }

        return new AtomicCatalogParamSpec(name, sqlType, required, descVi, descEn, def, example, hasDefault);
    }

    private static bool TryGetArray(JsonElement root, string name, out JsonElement arr)
    {
        if (root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Array)
        {
            arr = e;
            return true;
        }

        arr = default;
        return false;
    }

    private static string? GetString(JsonElement o, string name)
        => o.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    private static bool? GetBool(JsonElement o, string name)
        => o.TryGetProperty(name, out var e) && (e.ValueKind == JsonValueKind.True || e.ValueKind == JsonValueKind.False) ? e.GetBoolean() : null;

    public static string NormalizeParamName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var n = name.Trim();
        if (!n.StartsWith("@", StringComparison.Ordinal)) n = "@" + n;
        return n;
    }
}

public sealed record AtomicCatalogParamSpec(
    string Name,
    string? SqlType,
    bool Required,
    string? DescriptionVi,
    string? DescriptionEn,
    object? DefaultValue,
    string? Example,
    bool HasDefault = false);
