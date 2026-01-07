using Microsoft.EntityFrameworkCore;
using TILSOFTAI.Api.DependencyInjection;
using TILSOFTAI.Api.Middleware;
using TILSOFTAI.Application.Permissions;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Infrastructure.Caching;
using TILSOFTAI.Infrastructure.Data;
using TILSOFTAI.Infrastructure.Observability;
using TILSOFTAI.Orchestration.Chat;
using TILSOFTAI.Orchestration.Llm;
using TILSOFTAI.Orchestration.SK;
using TILSOFTAI.Orchestration.SK.Plugins;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.ActionsCatalog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();

builder.Services.Configure<RouteOptions>(options => options.LowercaseUrls = true);

builder.Services.AddDbContext<SqlServerDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("SqlServer") ?? throw new InvalidOperationException("SQL Server connection string is missing.");
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
});

builder.Services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<SqlServerDbContext>());

builder.Services.AddSingleton<RbacService>();
builder.Services.AddSingleton<AppMemoryCache>();
builder.Services.AddSingleton<IAppCache>(sp => sp.GetRequiredService<AppMemoryCache>());

// ------------------------
// Tool modularity (ver13)
// ------------------------
// Catalogs contributed by modules
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.FiltersCatalog.IFilterCatalogProvider, TILSOFTAI.Orchestration.Modules.Models.ModelsFilterCatalogProvider>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.FiltersCatalog.IFilterCatalogRegistry, TILSOFTAI.Orchestration.Tools.FiltersCatalog.FilterCatalogRegistry>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.Modularity.IFilterCanonicalizer, TILSOFTAI.Orchestration.Tools.Modularity.FilterCanonicalizer>();

builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.ActionsCatalog.IActionsCatalogProvider, TILSOFTAI.Orchestration.Modules.Models.ModelsActionsCatalogProvider>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.ActionsCatalog.IActionsCatalogRegistry, TILSOFTAI.Orchestration.Tools.ActionsCatalog.ActionCatalogRegistry>();

// Tool schemas contributed by modules
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.ToolSchemas.IToolInputSpecProvider, TILSOFTAI.Orchestration.Modules.Common.CommonToolInputSpecProvider>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.ToolSchemas.IToolInputSpecProvider, TILSOFTAI.Orchestration.Modules.Models.ModelsToolInputSpecProvider>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.ToolSchemas.ToolInputSpecCatalog>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.ToolSchemas.DynamicIntentValidator>();

// Tool whitelist definitions contributed by modules
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.IToolRegistrationProvider, TILSOFTAI.Orchestration.Modules.Common.CommonToolRegistrationProvider>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.IToolRegistrationProvider, TILSOFTAI.Orchestration.Modules.Models.ModelsToolRegistrationProvider>();
builder.Services.AddSingleton<ToolRegistry>();

// Handlers (one handler per tool)
builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.Modularity.IToolHandler, TILSOFTAI.Orchestration.Modules.Common.Handlers.FiltersCatalogToolHandler>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.Modularity.IToolHandler, TILSOFTAI.Orchestration.Modules.Common.Handlers.ActionsCatalogToolHandler>();

builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.Modularity.IToolHandler, TILSOFTAI.Orchestration.Modules.Models.Handlers.ModelsSearchToolHandler>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.Modularity.IToolHandler, TILSOFTAI.Orchestration.Modules.Models.Handlers.ModelsCountToolHandler>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.Modularity.IToolHandler, TILSOFTAI.Orchestration.Modules.Models.Handlers.ModelsStatsToolHandler>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.Modularity.IToolHandler, TILSOFTAI.Orchestration.Modules.Models.Handlers.ModelsOptionsToolHandler>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.Modularity.IToolHandler, TILSOFTAI.Orchestration.Modules.Models.Handlers.ModelsGetToolHandler>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.Modularity.IToolHandler, TILSOFTAI.Orchestration.Modules.Models.Handlers.ModelsAttributesListToolHandler>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.Modularity.IToolHandler, TILSOFTAI.Orchestration.Modules.Models.Handlers.ModelsPriceAnalyzeToolHandler>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.Modularity.IToolHandler, TILSOFTAI.Orchestration.Modules.Models.Handlers.ModelsCreatePrepareToolHandler>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.Modularity.IToolHandler, TILSOFTAI.Orchestration.Modules.Models.Handlers.ModelsCreateCommitToolHandler>();

builder.Services.AddScoped<ToolDispatcher>();
builder.Services.AddSingleton<TokenBudget>();
builder.Services.AddScoped<ChatPipeline>();
builder.Services.AddSingleton<IAuditLogger, AuditLogger>();
builder.Services.AddScoped<IConfirmationPlanStore, SqlConfirmationPlanStore>();
builder.Services.AddTilsoftaiAutoRegistrations();


var lmStudioOptions = new LmStudioOptions();
builder.Configuration.GetSection("LmStudio").Bind(lmStudioOptions);
builder.Services.AddSingleton(lmStudioOptions);

//AI
// SK infra
builder.Services.AddSingleton<TILSOFTAI.Orchestration.SK.SkKernelFactory>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.SK.ExecutionContextAccessor>();
builder.Services.AddScoped<ToolInvoker>();

// Plugins per module
builder.Services.AddSingleton(sp => new PluginCatalog(typeof(ChatPipeline).Assembly));

// Module routing (Level 2: expose subset of tools per request)
builder.Services.AddSingleton<TILSOFTAI.Orchestration.SK.Planning.ModuleRouter>();

// Tool-pack exposure policy (Level 3: reduce tool overload within a module)
builder.Services.AddSingleton<TILSOFTAI.Orchestration.SK.Planning.IPluginExposurePolicy, TILSOFTAI.Orchestration.Modules.Models.ModelsPluginExposurePolicy>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.SK.Planning.IPluginExposurePolicy, TILSOFTAI.Orchestration.SK.Planning.DefaultPluginExposurePolicy>();
// Auto register all *ToolsPlugin classes (Level 2: scalable plugin growth)
builder.Services.Scan(scan => scan
    .FromAssembliesOf(typeof(ChatPipeline))
    .AddClasses(classes => classes.Where(t => t.Name.EndsWith("ToolsPlugin")))
    .AsSelf()
    .WithScopedLifetime());


// Governance + Planner
builder.Services.AddScoped<TILSOFTAI.Orchestration.SK.Governance.CommitGuardFilter>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.SK.Governance.AutoInvocationCircuitBreakerFilter>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.SK.Planning.PlannerRouter>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.SK.Planning.StepwiseLoopRunner>();

// Filter Catalog (Phase 2: includeValues reads DB, so keep it scoped)
builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.FiltersCatalog.IFilterCatalogService,
                          TILSOFTAI.Orchestration.Tools.FiltersCatalog.FilterCatalogService>();
builder.Services.AddSingleton<IActionsCatalogService, ActionsCatalogService>();


builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection("RateLimiting"));

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RateLimitMiddleware>();

app.MapControllers();

app.Run();
