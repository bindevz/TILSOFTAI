using Microsoft.EntityFrameworkCore;
using TILSOFTAI.Api.Middleware;
using TILSOFTAI.Application.Permissions;
using TILSOFTAI.Application.Services;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Infrastructure.Caching;
using TILSOFTAI.Infrastructure.Data;
using TILSOFTAI.Infrastructure.Observability;
using TILSOFTAI.Infrastructure.Repositories;
using TILSOFTAI.Orchestration.Chat;
using TILSOFTAI.Orchestration.Llm;
using TILSOFTAI.Orchestration.SK;
using TILSOFTAI.Orchestration.SK.Plugins;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Api.DependencyInjection;

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
builder.Services.AddSingleton<ToolRegistry>();
builder.Services.AddScoped<ToolDispatcher>();
builder.Services.AddSingleton<TokenBudget>();
builder.Services.AddSingleton<SemanticResolver>();
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


builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection("RateLimiting"));

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RateLimitMiddleware>();

app.MapControllers();

app.Run();
