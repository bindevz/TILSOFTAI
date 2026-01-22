using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TILSOFTAI.Api.DependencyInjection;
using TILSOFTAI.Orchestration.Chat;
using TILSOFTAI.Orchestration.Chat.Localization;
using TILSOFTAI.Orchestration.Llm;
using TILSOFTAI.Orchestration.Llm.OpenAi;
using TILSOFTAI.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();

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

builder.Services.Configure<TILSOFTAI.Infrastructure.Caching.AnalyticsDatasetStoreOptions>(
    builder.Configuration.GetSection("AnalyticsDatasetStore"));
builder.Services.AddSingleton(sp => sp
    .GetRequiredService<IOptions<TILSOFTAI.Infrastructure.Caching.AnalyticsDatasetStoreOptions>>()
    .Value);

builder.Services.AddSingleton<TILSOFTAI.Infrastructure.Caching.InMemoryAnalyticsDatasetStore>();
builder.Services.AddSingleton<TILSOFTAI.Domain.Interfaces.IAnalyticsDatasetStore>(sp =>
{
    var opt = sp.GetRequiredService<TILSOFTAI.Infrastructure.Caching.AnalyticsDatasetStoreOptions>();
    var fallback = sp.GetRequiredService<TILSOFTAI.Infrastructure.Caching.InMemoryAnalyticsDatasetStore>();

    if (string.Equals(opt.Provider, "redis", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(opt.RedisConnection))
    {
        try
        {
            var mux = ConnectionMultiplexer.Connect(opt.RedisConnection);
            return new TILSOFTAI.Infrastructure.Caching.RedisAnalyticsDatasetStore(mux, fallback, opt);
        }
        catch
        {
            return fallback;
        }
    }

    return fallback;
});

builder.Services.Configure<TILSOFTAI.Application.Analytics.AnalyticsResultCacheOptions>(
    builder.Configuration.GetSection("AnalyticsResultCache"));
builder.Services.AddSingleton(sp => sp
    .GetRequiredService<IOptions<TILSOFTAI.Application.Analytics.AnalyticsResultCacheOptions>>()
    .Value);

builder.Services.AddSingleton<TILSOFTAI.Infrastructure.Caching.InMemoryAnalysisResultCache>();
builder.Services.AddSingleton<TILSOFTAI.Domain.Interfaces.IAnalyticsResultCache>(sp =>
{
    var opt = sp.GetRequiredService<TILSOFTAI.Application.Analytics.AnalyticsResultCacheOptions>();
    var fallback = sp.GetRequiredService<TILSOFTAI.Infrastructure.Caching.InMemoryAnalysisResultCache>();

    if (string.Equals(opt.Provider, "redis", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(opt.RedisConnection))
    {
        try
        {
            var mux = ConnectionMultiplexer.Connect(opt.RedisConnection);
            return new TILSOFTAI.Infrastructure.Caching.RedisAnalysisResultCache(mux, fallback);
        }
        catch
        {
            return fallback;
        }
    }

    return fallback;
});

// Tool schemas
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.ToolSchemas.IToolInputSpecProvider, TILSOFTAI.Orchestration.Modules.Analytics.AnalyticsToolInputSpecProvider>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.ToolSchemas.ToolInputSpecCatalog>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.ToolSchemas.DynamicIntentValidator>();

// Tool whitelist
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.IToolRegistrationProvider, TILSOFTAI.Orchestration.Modules.Analytics.AnalyticsToolRegistrationProvider>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Tools.ToolRegistry>();

// Handlers
builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.Modularity.IToolHandler, TILSOFTAI.Orchestration.Modules.Analytics.Handlers.AnalyticsRunToolHandler>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.Modularity.IToolHandler, TILSOFTAI.Orchestration.Modules.Analytics.Handlers.AtomicQueryExecuteToolHandler>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.Modularity.IToolHandler, TILSOFTAI.Orchestration.Modules.Analytics.Handlers.AtomicCatalogSearchToolHandler>();

builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.ToolDispatcher>();

// Filters patching
// Audit
builder.Services.AddSingleton<TILSOFTAI.Domain.Interfaces.IAuditLogger, TILSOFTAI.Infrastructure.Observability.AuditLogger>();

// Auto registrations (Application Services + Infrastructure Repositories)
builder.Services.AddTilsoftaiAutoRegistrations();

// SQL options
builder.Services.Configure<TILSOFTAI.Infrastructure.Options.SqlOptions>(
    builder.Configuration.GetSection("Sql"));
builder.Services.AddSingleton(sp => sp
    .GetRequiredService<IOptions<TILSOFTAI.Infrastructure.Options.SqlOptions>>()
    .Value);

// Runtime response schema validation
builder.Services.Configure<TILSOFTAI.Orchestration.Contracts.Validation.ResponseSchemaValidationOptions>(
    builder.Configuration.GetSection("ContractValidation"));
builder.Services.AddSingleton(sp => sp
    .GetRequiredService<IOptions<TILSOFTAI.Orchestration.Contracts.Validation.ResponseSchemaValidationOptions>>()
    .Value);
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Contracts.Validation.IResponseSchemaValidator,
    TILSOFTAI.Orchestration.Contracts.Validation.ResponseSchemaValidator>();

// LLM options
var lmStudioOptions = new LmStudioOptions();
builder.Configuration.GetSection("LmStudio").Bind(lmStudioOptions);
builder.Services.AddSingleton(lmStudioOptions);

builder.Services.AddHttpClient<OpenAiChatClient>((sp, http) =>
{
    var lm = sp.GetRequiredService<LmStudioOptions>();
    http.BaseAddress = new Uri(lm.BaseUrl.TrimEnd('/') + "/v1/");
    http.Timeout = TimeSpan.FromSeconds(Math.Clamp(lm.TimeoutSeconds, 5, 1800));
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "lm-studio");
});

builder.Services.AddSingleton<OpenAiToolSchemaFactory>();
builder.Services.AddSingleton<TokenBudget>();

// Chat tuning
builder.Services.Configure<ChatTuningOptions>(builder.Configuration.GetSection("ChatTuning"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<ChatTuningOptions>>().Value);

// Orchestration feature flags
// Localization
builder.Services.AddSingleton<ILanguageResolver, HeuristicLanguageResolver>();
builder.Services.AddSingleton<IChatTextLocalizer, DefaultChatTextLocalizer>();
builder.Services.AddSingleton<ChatTextPatterns>();

// ToolInvoker infra
builder.Services.AddScoped<TILSOFTAI.Orchestration.SK.ExecutionContextAccessor>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.SK.ToolInvoker>();

// Chat pipeline (Mode B)
builder.Services.AddScoped<ChatPipeline>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendCors", policy =>
    {
        policy.WithOrigins("http://tsl-app.auvietsoft.vn")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsProduction())
{
    var connectionStrings = app.Configuration.GetSection("ConnectionStrings").GetChildren();
    foreach (var cs in connectionStrings)
    {
        if (!ContainsInlinePassword(cs.Value))
            continue;

        var envOverride = Environment.GetEnvironmentVariable($"ConnectionStrings__{cs.Key}");
        if (!string.IsNullOrWhiteSpace(envOverride))
            continue;

        app.Logger.LogWarning("Connection string '{ConnectionName}' contains an inline password. Use environment variables or a secret manager.", cs.Key);
        throw new InvalidOperationException("Inline passwords are not allowed in production configuration.");
    }
}

// Middleware
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ConversationIdMiddleware>();
app.UseMiddleware<RateLimitMiddleware>();

app.UseRouting();
app.UseCors("FrontendCors");
app.UseAuthorization();
app.MapControllers();

app.Run();

static bool ContainsInlinePassword(string? connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString))
        return false;

    return connectionString.IndexOf("Password=", StringComparison.OrdinalIgnoreCase) >= 0;
}
