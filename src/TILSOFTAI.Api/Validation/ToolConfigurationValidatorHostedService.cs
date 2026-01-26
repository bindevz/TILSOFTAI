using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TILSOFTAI.Application.Permissions;
using TILSOFTAI.Configuration;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.ToolSchemas;

namespace TILSOFTAI.Api.Validation;

/// <summary>
/// Validates orchestration tool configuration invariants at application startup.
///
/// This validator is designed to prevent silent misconfiguration where the model is allowed to call a tool,
/// but the runtime cannot execute it due to missing:
///   - Tool registry registration
///   - Tool input specification
///   - RBAC mapping
///
/// The most common symptom without this validator is repeated tool calls (loops) and inability to apply filters,
/// because the model cannot successfully discover/execute the correct tool.
/// </summary>
public sealed class ToolConfigurationValidatorHostedService : IHostedService
{
    private readonly ILogger<ToolConfigurationValidatorHostedService> _logger;
    private readonly AppSettings _settings;
    private readonly ToolRegistry _toolRegistry;
    private readonly ToolInputSpecCatalog _specCatalog;
    private readonly RbacService _rbac;

    public ToolConfigurationValidatorHostedService(
        ILogger<ToolConfigurationValidatorHostedService> logger,
        IOptions<AppSettings> settings,
        ToolRegistry toolRegistry,
        ToolInputSpecCatalog specCatalog,
        RbacService rbac)
    {
        _logger = logger;
        _settings = settings.Value;
        _toolRegistry = toolRegistry;
        _specCatalog = specCatalog;
        _rbac = rbac;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var cfg = _settings.Orchestration.ToolConfigurationValidation;
        if (!cfg.Enabled)
        {
            _logger.LogInformation("Tool configuration validation is disabled.");
            return Task.CompletedTask;
        }

        var allowlist = _settings.Orchestration.ToolAllowlist ?? Array.Empty<string>();
        var distinctAllowlist = new HashSet<string>(allowlist, StringComparer.OrdinalIgnoreCase);

        if (allowlist.Length != distinctAllowlist.Count)
        {
            var duplicates = allowlist
                .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => $"{g.Key} (x{g.Count()})")
                .ToArray();

            _logger.LogWarning("Orchestration.ToolAllowlist contains duplicates: {Duplicates}", string.Join(", ", duplicates));
        }

        var errors = new List<string>();
        var registeredTools = new HashSet<string>(_toolRegistry.GetToolNames(), StringComparer.OrdinalIgnoreCase);

        foreach (var toolName in distinctAllowlist.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (cfg.ValidateToolRegistry && !registeredTools.Contains(toolName))
            {
                errors.Add($"Tool '{toolName}' is in Orchestration.ToolAllowlist but is not registered in ToolRegistry. " +
                           "Ensure the corresponding IToolRegistrationProvider is registered and gated by ToolAllowlist correctly.");
            }

            if (cfg.ValidateToolInputSpecs && !_specCatalog.TryGet(toolName, out _))
            {
                errors.Add($"Tool '{toolName}' is in Orchestration.ToolAllowlist but has no ToolInputSpec registered. " +
                           "Ensure an IToolInputSpecProvider provides a spec for this tool.");
            }

            if (cfg.ValidateRbacMappings && !_rbac.IsToolConfigured(toolName))
            {
                errors.Add($"Tool '{toolName}' is in Orchestration.ToolAllowlist but has no RBAC mapping. " +
                           "Add the tool to RbacService.ToolRoles.");
            }
        }

        if (errors.Count == 0)
        {
            _logger.LogInformation(
                "Tool configuration validation passed. AllowlistCount={AllowlistCount}, RegisteredCount={RegisteredCount}",
                distinctAllowlist.Count,
                registeredTools.Count);

            return Task.CompletedTask;
        }

        // Provide actionable diagnostics.
        var diagnostics = string.Join(Environment.NewLine, errors.Select(e => "- " + e));

        _logger.LogError(
            "Tool configuration validation failed with {ErrorCount} error(s).\n{Diagnostics}\n\n" +
            "Allowlist: {Allowlist}\nRegisteredTools: {RegisteredTools}",
            errors.Count,
            diagnostics,
            string.Join(", ", distinctAllowlist.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)),
            string.Join(", ", registeredTools.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)));

        if (cfg.FailFast)
            throw new InvalidOperationException("Tool configuration validation failed. See logs for details.");

        _logger.LogWarning("Continuing startup because ToolConfigurationValidation.FailFast=false.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
