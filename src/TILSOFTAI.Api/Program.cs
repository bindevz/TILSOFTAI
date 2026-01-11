using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TILSOFTAI.Api.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();

builder.Services.Configure<RouteOptions>(options => options.LowercaseUrls = true);

builder.Services.AddDbContext<TILSOFTAI.Infrastructure.Data.SqlServerDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("SqlServer")
        ?? throw new InvalidOperationException("SQL Server connection string is missing.");
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
});

builder.Services.AddScoped<TILSOFTAI.Domain.Interfaces.IUnitOfWork>(sp =>
    sp.GetRequiredService<TILSOFTAI.Infrastructure.Data.SqlServerDbContext>());

builder.Services.AddSingleton<TILSOFTAI.Application.Permissions.RbacService>();
builder.Services.AddSingleton<TILSOFTAI.Infrastructure.Caching.AppMemoryCache>();
builder.Services.AddSingleton<TILSOFTAI.Domain.Interfaces.IAppCache>(sp =>
    sp.GetRequiredService<TILSOFTAI.Infrastructure.Caching.AppMemoryCache>());

// Atomic Data Engine datasets
builder.Services.AddSingleton<TILSOFTAI.Domain.Interfaces.IAnalyticsDatasetStore, TILSOFTAI.Infrastructure.Caching.InMemoryAnalyticsDatasetStore>();

// ------------------------
// Tool modularity
// ------------------------
// Catalog registries (providers are optional; empty catalog is valid)
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.FiltersCatalog.IFilterCatalogRegistry, TILSOFTAI.Orchestration.Tools.FiltersCatalog.FilterCatalogRegistry>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.Modularity.IFilterCanonicalizer, TILSOFTAI.Orchestration.Tools.Modularity.FilterCanonicalizer>();

// Catalog services used by catalog tool handlers
builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.FiltersCatalog.IFilterCatalogService, TILSOFTAI.Orchestration.Tools.FiltersCatalog.FilterCatalogService>();

builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.ActionsCatalog.IActionsCatalogRegistry, TILSOFTAI.Orchestration.Tools.ActionsCatalog.ActionCatalogRegistry>();

builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.ActionsCatalog.IActionsCatalogService, TILSOFTAI.Orchestration.Tools.ActionsCatalog.ActionsCatalogService>();

// Tool schemas contributed by modules
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.ToolSchemas.IToolInputSpecProvider, TILSOFTAI.Orchestration.Modules.Common.CommonToolInputSpecProvider>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.ToolSchemas.IToolInputSpecProvider, TILSOFTAI.Orchestration.Modules.Analytics.AnalyticsToolInputSpecProvider>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.ToolSchemas.ToolInputSpecCatalog>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.ToolSchemas.DynamicIntentValidator>();

// Tool whitelist definitions contributed by modules
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.IToolRegistrationProvider, TILSOFTAI.Orchestration.Modules.Common.CommonToolRegistrationProvider>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.IToolRegistrationProvider, TILSOFTAI.Orchestration.Modules.Analytics.AnalyticsToolRegistrationProvider>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.ToolRegistry>();

// Handlers (one handler per tool)
builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.Modularity.IToolHandler, TILSOFTAI.Orchestration.Modules.Common.Handlers.FiltersCatalogToolHandler>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.Modularity.IToolHandler, TILSOFTAI.Orchestration.Modules.Common.Handlers.ActionsCatalogToolHandler>();

// Analytics tools
builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.Modularity.IToolHandler, TILSOFTAI.Orchestration.Modules.Analytics.Handlers.AnalyticsRunToolHandler>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.Modularity.IToolHandler, TILSOFTAI.Orchestration.Modules.Analytics.Handlers.AtomicQueryExecuteToolHandler>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.Modularity.IToolHandler, TILSOFTAI.Orchestration.Modules.Analytics.Handlers.AtomicCatalogSearchToolHandler>();

builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.ToolDispatcher>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Llm.TokenBudget>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.Chat.ChatPipeline>();
builder.Services.AddSingleton<TILSOFTAI.Domain.Interfaces.IAuditLogger, TILSOFTAI.Infrastructure.Observability.AuditLogger>();
builder.Services.AddScoped<TILSOFTAI.Domain.Interfaces.IConfirmationPlanStore, TILSOFTAI.Infrastructure.Data.SqlConfirmationPlanStore>();

// Filters
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.Filters.IFilterPatchMerger, TILSOFTAI.Orchestration.Tools.Filters.FilterPatchMerger>();

// SK planning / governance
builder.Services.AddSingleton<TILSOFTAI.Orchestration.SK.Plugins.PluginCatalog>(sp =>
    new TILSOFTAI.Orchestration.SK.Plugins.PluginCatalog(typeof(TILSOFTAI.Orchestration.SK.Plugins.AnalyticsToolsPlugin).Assembly));
builder.Services.AddSingleton<TILSOFTAI.Orchestration.SK.Planning.ModuleRouter>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.SK.Planning.PlannerRouter>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.SK.Planning.StepwiseLoopRunner>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.SK.Planning.IPluginExposurePolicy, TILSOFTAI.Orchestration.SK.Planning.DefaultPluginExposurePolicy>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.SK.Governance.CommitGuardFilter>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.SK.Governance.AutoInvocationCircuitBreakerFilter>();

// SK plugins (must be registered so PluginCatalog can instantiate them)
builder.Services.AddScoped<TILSOFTAI.Orchestration.SK.Plugins.AnalyticsToolsPlugin>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.SK.Plugins.FiltersToolsPlugin>();

// Auto registrations (Application Services + Infrastructure Repositories)
builder.Services.AddTilsoftaiAutoRegistrations();

// Runtime response schema validation
builder.Services.Configure<TILSOFTAI.Orchestration.Contracts.Validation.ResponseSchemaValidationOptions>(
    builder.Configuration.GetSection("ContractValidation"));
builder.Services.AddSingleton(sp => sp
    .GetRequiredService<IOptions<TILSOFTAI.Orchestration.Contracts.Validation.ResponseSchemaValidationOptions>>()
    .Value);
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Contracts.Validation.IResponseSchemaValidator,
    TILSOFTAI.Orchestration.Contracts.Validation.ResponseSchemaValidator>();

// LLM options
var lmStudioOptions = new TILSOFTAI.Orchestration.Llm.LmStudioOptions();
builder.Configuration.GetSection("LmStudio").Bind(lmStudioOptions);
builder.Services.AddSingleton(lmStudioOptions);

// Chat tuning
builder.Services.Configure<TILSOFTAI.Orchestration.Chat.ChatTuningOptions>(builder.Configuration.GetSection("ChatTuning"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<TILSOFTAI.Orchestration.Chat.ChatTuningOptions>>().Value);

// Localization
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Chat.Localization.ILanguageResolver, TILSOFTAI.Orchestration.Chat.Localization.HeuristicLanguageResolver>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Chat.Localization.IChatTextLocalizer, TILSOFTAI.Orchestration.Chat.Localization.DefaultChatTextLocalizer>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Chat.Localization.ChatTextPatterns>();

// SK infra
builder.Services.AddSingleton<TILSOFTAI.Orchestration.SK.SkKernelFactory>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.SK.ExecutionContextAccessor>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.SK.ToolInvoker>();

// Conversation state (ver20)
builder.Services.Configure<TILSOFTAI.Orchestration.SK.Conversation.ConversationStateStoreOptions>(builder.Configuration.GetSection("ConversationStateStore"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<TILSOFTAI.Orchestration.SK.Conversation.ConversationStateStoreOptions>>().Value);

// Only register Redis client when explicitly enabled.
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var opt = sp.GetRequiredService<TILSOFTAI.Orchestration.SK.Conversation.ConversationStateStoreOptions>();
    if (!string.Equals(opt.Provider, "Redis", StringComparison.OrdinalIgnoreCase)
        || string.IsNullOrWhiteSpace(opt.Redis.ConnectionString))
        throw new InvalidOperationException("Redis is not enabled. Set ConversationStateStore:Provider=Redis and provide ConversationStateStore:Redis:ConnectionString.");

    return ConnectionMultiplexer.Connect(opt.Redis.ConnectionString);
});

builder.Services.AddSingleton<TILSOFTAI.Orchestration.SK.Conversation.IConversationStateStore>(sp =>
{
    var opt = sp.GetRequiredService<TILSOFTAI.Orchestration.SK.Conversation.ConversationStateStoreOptions>();

    if (string.Equals(opt.Provider, "Redis", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(opt.Redis.ConnectionString))
    {
        return new TILSOFTAI.Orchestration.SK.Conversation.RedisConversationStateStore(
            sp.GetRequiredService<IConnectionMultiplexer>(),
            opt); // <-- FIX: pass opt, not opt.Redis
    }

    return new TILSOFTAI.Orchestration.SK.Conversation.InMemoryConversationStateStore(opt);
});

var app = builder.Build();

app.UseRouting();
app.UseAuthorization();
app.MapControllers();

app.Run();
