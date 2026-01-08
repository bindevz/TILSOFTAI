using Microsoft.EntityFrameworkCore;
using TILSOFTAI.Api.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();

builder.Services.Configure<RouteOptions>(options => options.LowercaseUrls = true);

builder.Services.AddDbContext<TILSOFTAI.Infrastructure.Data.SqlServerDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("SqlServer") ?? throw new InvalidOperationException("SQL Server connection string is missing.");
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
});

builder.Services.AddScoped<TILSOFTAI.Domain.Interfaces.IUnitOfWork>(provider => provider.GetRequiredService<TILSOFTAI.Infrastructure.Data.SqlServerDbContext>());

builder.Services.AddSingleton<TILSOFTAI.Application.Permissions.RbacService>();
builder.Services.AddSingleton<TILSOFTAI.Infrastructure.Caching.AppMemoryCache>();
builder.Services.AddSingleton<TILSOFTAI.Domain.Interfaces.IAppCache>(sp => sp.GetRequiredService<TILSOFTAI.Infrastructure.Caching.AppMemoryCache>());

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
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.ToolRegistry>();

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

builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.ToolDispatcher>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Llm.TokenBudget>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.Chat.ChatPipeline>();
builder.Services.AddSingleton<TILSOFTAI.Domain.Interfaces.IAuditLogger, TILSOFTAI.Infrastructure.Observability.AuditLogger>();
builder.Services.AddScoped<TILSOFTAI.Domain.Interfaces.IConfirmationPlanStore, TILSOFTAI.Infrastructure.Data.SqlConfirmationPlanStore>();
builder.Services.AddTilsoftaiAutoRegistrations();


var lmStudioOptions = new TILSOFTAI.Orchestration.Llm.LmStudioOptions();
builder.Configuration.GetSection("LmStudio").Bind(lmStudioOptions);
builder.Services.AddSingleton(lmStudioOptions);

// Chat tuning (ver21)
builder.Services.Configure<TILSOFTAI.Orchestration.Chat.ChatTuningOptions>(builder.Configuration.GetSection("ChatTuning"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<TILSOFTAI.Orchestration.Chat.ChatTuningOptions>>().Value);

// Localization + multilingual heuristics (ver21)
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Chat.Localization.ILanguageResolver, TILSOFTAI.Orchestration.Chat.Localization.HeuristicLanguageResolver>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Chat.Localization.IChatTextLocalizer, TILSOFTAI.Orchestration.Chat.Localization.DefaultChatTextLocalizer>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Chat.Localization.ChatTextPatterns>();

//AI
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
        var mux = sp.GetRequiredService<IConnectionMultiplexer>();
        return new TILSOFTAI.Orchestration.SK.Conversation.RedisConversationStateStore(mux, opt);
    }

    return new TILSOFTAI.Orchestration.SK.Conversation.InMemoryConversationStateStore(opt);
});
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.Filters.IFilterPatchMerger,
                             TILSOFTAI.Orchestration.Tools.Filters.FilterPatchMerger>();

// Plugins per module
builder.Services.AddSingleton(sp => new TILSOFTAI.Orchestration.SK.Plugins.PluginCatalog(typeof(TILSOFTAI.Orchestration.Chat.ChatPipeline).Assembly));

// Module routing (Level 2: expose subset of tools per request)
builder.Services.AddSingleton<TILSOFTAI.Orchestration.SK.Planning.ModuleRouter>();

// Tool-pack exposure policy (Level 3: reduce tool overload within a module)
builder.Services.AddSingleton<TILSOFTAI.Orchestration.SK.Planning.IPluginExposurePolicy, TILSOFTAI.Orchestration.Modules.Models.ModelsPluginExposurePolicy>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.SK.Planning.IPluginExposurePolicy, TILSOFTAI.Orchestration.SK.Planning.DefaultPluginExposurePolicy>();
// Auto register all *ToolsPlugin classes (Level 2: scalable plugin growth)
builder.Services.Scan(scan => scan
    .FromAssembliesOf(typeof(TILSOFTAI.Orchestration.Chat.ChatPipeline))
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
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.ActionsCatalog.IActionsCatalogService, TILSOFTAI.Orchestration.Tools.ActionsCatalog.ActionsCatalogService>();


builder.Services.Configure<TILSOFTAI.Api.Middleware.RateLimitOptions>(builder.Configuration.GetSection("RateLimiting"));

var app = builder.Build();

app.UseMiddleware<TILSOFTAI.Api.Middleware.ExceptionHandlingMiddleware>();
app.UseMiddleware<TILSOFTAI.Api.Middleware.CorrelationIdMiddleware>();
app.UseMiddleware<TILSOFTAI.Api.Middleware.ConversationIdMiddleware>();
app.UseMiddleware<TILSOFTAI.Api.Middleware.RateLimitMiddleware>();

app.MapControllers();

app.Run();
