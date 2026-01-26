using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TILSOFTAI.Api.Configuration;
using TILSOFTAI.Api.DependencyInjection;
using TILSOFTAI.Api.Localization;
using TILSOFTAI.Api.Middleware;
using TILSOFTAI.Configuration;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Orchestration.Chat;
using TILSOFTAI.Orchestration.Chat.Localization;
using TILSOFTAI.Orchestration.Llm.OpenAi;
using TILSOFTAI.Orchestration;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddAppSettings(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();

builder.Services.Configure<RouteOptions>(options => options.LowercaseUrls = true);

builder.Services.AddDbContext<TILSOFTAI.Infrastructure.Data.SqlServerDbContext>((sp, options) =>
{
    var settings = sp.GetRequiredService<IOptions<AppSettings>>().Value;
    var connectionString = builder.Configuration.GetConnectionString(settings.Sql.ConnectionStringName)
        ?? throw new InvalidOperationException("SQL Server connection string is missing.");
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
});

builder.Services.AddScoped<TILSOFTAI.Domain.Interfaces.IUnitOfWork>(sp =>
    sp.GetRequiredService<TILSOFTAI.Infrastructure.Data.SqlServerDbContext>());

builder.Services.AddSingleton<TILSOFTAI.Application.Permissions.RbacService>();
builder.Services.AddSingleton<TILSOFTAI.Infrastructure.Caching.AppMemoryCache>();
builder.Services.AddSingleton<TILSOFTAI.Domain.Interfaces.IAppCache>(sp =>
    sp.GetRequiredService<TILSOFTAI.Infrastructure.Caching.AppMemoryCache>());

builder.Services.AddSingleton<TILSOFTAI.Infrastructure.Caching.InMemoryAnalyticsDatasetStore>();
builder.Services.AddSingleton<TILSOFTAI.Domain.Interfaces.IAnalyticsDatasetStore>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<AppSettings>>().Value;
    var redis = settings.Redis;
    var fallback = sp.GetRequiredService<TILSOFTAI.Infrastructure.Caching.InMemoryAnalyticsDatasetStore>();

    if (redis.Enabled && !string.IsNullOrWhiteSpace(redis.ConnectionString))
    {
        try
        {
            var mux = ConnectionMultiplexer.Connect(redis.ConnectionString);
            return new TILSOFTAI.Infrastructure.Caching.RedisAnalyticsDatasetStore(mux, fallback, redis);
        }
        catch
        {
            return fallback;
        }
    }

    return fallback;
});

builder.Services.AddSingleton<TILSOFTAI.Infrastructure.Caching.InMemoryAnalysisResultCache>();
builder.Services.AddSingleton<TILSOFTAI.Domain.Interfaces.IAnalyticsResultCache>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<AppSettings>>().Value;
    var redis = settings.Redis;
    var fallback = sp.GetRequiredService<TILSOFTAI.Infrastructure.Caching.InMemoryAnalysisResultCache>();

    if (redis.Enabled && !string.IsNullOrWhiteSpace(redis.ConnectionString))
    {
        try
        {
            var mux = ConnectionMultiplexer.Connect(redis.ConnectionString);
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

// Startup validation: fail fast if a tool is allowlisted but missing RBAC, registry registration, or input spec.
builder.Services.AddHostedService<TILSOFTAI.Api.Validation.ToolConfigurationValidatorHostedService>();

// Handlers
builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.Modularity.IToolHandler, TILSOFTAI.Orchestration.Modules.Analytics.Handlers.AnalyticsRunToolHandler>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.Modularity.IToolHandler, TILSOFTAI.Orchestration.Modules.Analytics.Handlers.AtomicQueryExecuteToolHandler>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.Modularity.IToolHandler, TILSOFTAI.Orchestration.Modules.Analytics.Handlers.AtomicCatalogSearchToolHandler>();

// Entity Graph
builder.Services.AddEntityGraphOrchestration();
// Document Search (Vector RAG)
builder.Services.AddDocumentSearchOrchestration();

builder.Services.AddScoped<TILSOFTAI.Orchestration.Tools.ToolDispatcher>();

// Filters patching
// Audit
builder.Services.AddSingleton<TILSOFTAI.Domain.Interfaces.IAuditLogger, TILSOFTAI.Infrastructure.Observability.AuditLogger>();

// Auto registrations (Application Services + Infrastructure Repositories)
builder.Services.AddTilsoftaiAutoRegistrations();

// Runtime response schema validation
builder.Services.AddSingleton<TILSOFTAI.Orchestration.Contracts.Validation.IResponseSchemaValidator,
    TILSOFTAI.Orchestration.Contracts.Validation.ResponseSchemaValidator>();

builder.Services.AddHttpClient<OpenAiChatClient>((sp, http) =>
{
    var lm = sp.GetRequiredService<IOptions<AppSettings>>().Value.Llm;
    http.BaseAddress = new Uri(lm.Endpoint.TrimEnd('/') + "/v1/");
    http.Timeout = TimeSpan.FromSeconds(Math.Clamp(lm.TimeoutSeconds, 5, 1800));
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "lm-studio");
});

builder.Services.AddHttpClient<OpenAiEmbeddingsClient>((sp, http) =>
{
    var emb = sp.GetRequiredService<IOptions<AppSettings>>().Value.Embeddings;
    http.BaseAddress = new Uri(emb.Endpoint.TrimEnd('/') + "/v1/");
    http.Timeout = TimeSpan.FromSeconds(Math.Clamp(emb.TimeoutSeconds, 5, 3600));
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "lm-studio");
});

builder.Services.AddScoped<IEmbeddingClient>(sp => sp.GetRequiredService<OpenAiEmbeddingsClient>());


builder.Services.AddSingleton<OpenAiToolSchemaFactory>();

// Localization
builder.Services.AddSingleton<IApiTextLocalizer, ResxApiTextLocalizer>();
builder.Services.AddSingleton<ILanguageResolver, HeuristicLanguageResolver>();
builder.Services.AddSingleton<IChatTextLocalizer, ResxChatTextLocalizer>();
// ToolInvoker infra
builder.Services.AddScoped<TILSOFTAI.Orchestration.Execution.ExecutionContextAccessor>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.Execution.ToolInvoker>();

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
app.UseMiddleware<RequestCultureMiddleware>();
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





