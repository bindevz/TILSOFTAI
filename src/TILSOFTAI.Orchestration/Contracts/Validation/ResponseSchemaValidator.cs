using System.Text;
using System.Text.Json;
using Json.Schema;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TILSOFTAI.Orchestration.Llm;

namespace TILSOFTAI.Orchestration.Contracts.Validation;

/// <summary>
/// Validates tool response payloads against JSON Schemas stored under governance/contracts.
///
/// Design goals:
/// - Contract-first: the schema files are the single source of truth.
/// - Runtime guardrail: a handler cannot drift away from the schema without tripping validation.
/// - Low coupling: validation selection is driven by payload.kind + payload.schemaVersion.
/// </summary>
public sealed class ResponseSchemaValidator : IResponseSchemaValidator
{
    // IMPORTANT: Use Web defaults (camelCase) to match governance schemas.
    private static readonly JsonSerializerOptions InstanceJson = new(JsonSerializerDefaults.Web);

    private readonly ILogger<ResponseSchemaValidator> _log;
    private readonly ResponseSchemaValidationOptions _opt;

    // Build-time registry for $ref resolution.
    private readonly SchemaRegistry _schemaRegistry = new();
    private readonly BuildOptions _buildOptions;

    // Mapping of (schemaVersion, kind) -> compiled schema
    private readonly Dictionary<string, JsonSchema> _schemas = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _enforcedKinds;

    public ResponseSchemaValidator(
        IOptions<ResponseSchemaValidationOptions> options,
        ILogger<ResponseSchemaValidator> log)
    {
        _log = log;
        _opt = options?.Value ?? new ResponseSchemaValidationOptions();
        _enforcedKinds = new HashSet<string>(_opt.EnforcedKinds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        // Ensure draft 2020-12 as default dialect to match governance schemas.
        Dialect.Default = Dialect.Draft202012;

        _buildOptions = new BuildOptions
        {
            Dialect = Dialect.Draft202012,
            SchemaRegistry = _schemaRegistry
        };

        if (!_opt.Enabled)
        {
            _log.LogInformation("Response schema validation disabled.");
            return;
        }

        var root = TryLocateContractsRoot(_opt.ContractsRootPath);
        if (root is null)
        {
            var msg = "Could not locate governance/contracts for runtime schema validation. " +
                      "Ensure governance/contracts is deployed (copied to output) or set ContractValidation:ContractsRootPath.";
            // Fail fast only if we have enforced kinds; otherwise downgrade to warning.
            if (_enforcedKinds.Count > 0)
                throw new InvalidOperationException(msg);

            _log.LogWarning(msg);
            return;
        }

        LoadSchemas(root);
    }

    public void ValidateOrThrow(object? payload, string toolName)
    {
        if (!_opt.Enabled)
            return;

        if (payload is null)
            return;

        // We rely on (kind, schemaVersion) embedded in the payload.
        // NOTE: JsonElement/JsonDocument lifetimes are a common pitfall.
        // Tool handlers (or other layers) may accidentally return a JsonElement backed by a disposed JsonDocument.
        // Also, Json.Schema may keep references to the instance element while producing EvaluationResults.
        // Therefore we always materialize a fresh, self-owned JsonElement via Clone().
        // IMPORTANT: serialize using Web defaults to match contract casing (camelCase).
        // If we serialize using JsonSerializerOptions.Default, record/class properties will be PascalCase
        // (e.g., Columns/Rows/TotalCount) and will falsely violate the schema (columns/rows/totalCount).
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload, InstanceJson));
        var root = doc.RootElement.Clone();
        if (root.ValueKind != JsonValueKind.Object)
            return;

        if (!root.TryGetProperty("kind", out var kindEl) || kindEl.ValueKind != JsonValueKind.String)
            return;

        if (!root.TryGetProperty("schemaVersion", out var verEl) || verEl.ValueKind != JsonValueKind.Number)
            return;

        var kind = kindEl.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(kind))
            return;

        var schemaVersion = verEl.GetInt32();

        var isEnforced = _enforcedKinds.Contains(kind);
        if (!isEnforced && !_opt.ValidateAllKindsWithSchema)
            return;

        var key = MakeKey(schemaVersion, kind);
        if (!_schemas.TryGetValue(key, out var schema))
        {
            if (isEnforced && _opt.FailOnMissingSchemaForEnforcedKinds)
            {
                throw new ResponseContractException(
                    $"Missing response schema for kind '{kind}' (schemaVersion {schemaVersion}). Tool='{toolName}'.");
            }

            _log.LogWarning("No response schema found for kind '{Kind}' (schemaVersion {SchemaVersion}). Tool='{Tool}'. Skipping validation.",
                kind, schemaVersion, toolName);
            return;
        }

        // Evaluate instance against schema.
        var evalOptions = new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = true
        };

        var results = schema.Evaluate(root, evalOptions);
        if (results.IsValid)
            return;

        var errors = FlattenErrors(results);
        var preview = errors.Count == 0
            ? "(no details)"
            : string.Join(" | ", errors.Take(10));

        throw new ResponseContractException(
            $"Response payload violates schema '{kind}' (schemaVersion {schemaVersion}). Tool='{toolName}'. Errors: {preview}");
    }

    private void LoadSchemas(string contractsRoot)
    {
        // Contracts folder structure: governance/contracts/v{N}/*.schema.json
        var files = Directory.EnumerateFiles(contractsRoot, "*.schema.json", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _log.LogInformation("Loading {Count} JSON Schemas from {Root}.", files.Length, contractsRoot);

        foreach (var file in files)
        {
            var version = TryExtractSchemaVersion(file);
            if (version is null)
                continue;

            var kind = Path.GetFileName(file)
                .Replace(".schema.json", string.Empty, StringComparison.OrdinalIgnoreCase);

            try
            {
                // IMPORTANT: JsonSchema.Build() may keep references to the provided JsonElement.
                // If that element is backed by a disposed JsonDocument, schema evaluation can throw ObjectDisposedException.
                // We therefore Clone() the schema root so its underlying document remains alive independently.
                using var schemaDoc = JsonDocument.Parse(File.ReadAllText(file));
                var schemaRoot = schemaDoc.RootElement.Clone();
                var schema = JsonSchema.Build(schemaRoot, _buildOptions);

                // Register alias URI to support relative $ref that includes filename suffix.
                // Example: $id = tilsoftai://contracts/models.search.v2
                //          $ref = tabular.data.v1.schema.json  => resolves to tilsoftai://contracts/tabular.data.v1.schema.json
                var alias = new Uri($"tilsoftai://contracts/{Path.GetFileName(file)}");
                _schemaRegistry.Register(alias, schema);

                // Store by (schemaVersion, kind)
                _schemas[MakeKey(version.Value, kind)] = schema;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to load schema from {File}.", file);
                // Fail fast if this is a schema for an enforced kind.
                if (_enforcedKinds.Contains(kind))
                    throw;
            }
        }

        _log.LogInformation("Loaded {Count} compiled schemas.", _schemas.Count);
    }

    private static string MakeKey(int schemaVersion, string kind)
        => $"v{schemaVersion}:{kind}";

    private static int? TryExtractSchemaVersion(string schemaPath)
    {
        // Expect a directory segment named "v2" or "v1" ...
        var dir = new DirectoryInfo(Path.GetDirectoryName(schemaPath) ?? string.Empty);
        while (dir is not null)
        {
            var name = dir.Name;
            if (name.Length >= 2 && (name[0] == 'v' || name[0] == 'V') && int.TryParse(name[1..], out var v))
                return v;
            dir = dir.Parent;
        }
        return null;
    }

    private static string? TryLocateContractsRoot(string? configured)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(configured))
            candidates.Add(configured!);

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "governance", "contracts"));
        candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "governance", "contracts"));

        // Walk up a few levels (dev builds often run under src/.../bin/Debug/...)
        var cur = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (var i = 0; i < 8 && cur is not null; i++)
        {
            candidates.Add(Path.Combine(cur.FullName, "governance", "contracts"));
            cur = cur.Parent;
        }

        foreach (var c in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (Directory.Exists(c))
                    return c;
            }
            catch
            {
                // ignored
            }
        }

        return null;
    }

    private static List<string> FlattenErrors(EvaluationResults root)
    {
        var list = new List<string>();
        Collect(root, list);
        return list;

        static void Collect(EvaluationResults node, List<string> acc)
        {
            if (node.Errors is not null)
            {
                foreach (var kv in node.Errors)
                {
                    // JsonSchema.Net uses JsonPointer (a struct) for instance locations; it is never null.
                    // The default pointer string is empty, so we can safely call ToString().
                    var loc = node.InstanceLocation.ToString();
                    var msg = string.IsNullOrWhiteSpace(loc)
                        ? $"{kv.Key}: {kv.Value}"
                        : $"{loc}: {kv.Key}: {kv.Value}";
                    acc.Add(msg);
                }
            }

            if (node.Details is null) return;
            foreach (var d in node.Details)
                Collect(d, acc);
        }
    }
}
