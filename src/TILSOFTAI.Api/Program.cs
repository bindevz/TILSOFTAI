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

builder.Services.AddScoped<IOrdersRepository, OrdersRepository>();
builder.Services.AddScoped<ICustomersRepository, CustomersRepository>();
builder.Services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<SqlServerDbContext>());

builder.Services.AddSingleton<RbacService>();
builder.Services.AddSingleton<AppMemoryCache>();
builder.Services.AddSingleton<ToolRegistry>();
builder.Services.AddScoped<ToolDispatcher>();
builder.Services.AddSingleton<ResponseParser>();
builder.Services.AddSingleton<TokenBudget>();
builder.Services.AddSingleton<ContextManager>();
builder.Services.AddSingleton<SemanticResolver>();
builder.Services.AddScoped<OrdersService>();
builder.Services.AddScoped<CustomersService>();
builder.Services.AddScoped<ConfirmationPlanService>();
builder.Services.AddScoped<ModelsService>();
builder.Services.AddScoped<ChatPipeline>();
builder.Services.AddSingleton<IAuditLogger, AuditLogger>();
builder.Services.AddScoped<IConfirmationPlanStore, SqlConfirmationPlanStore>();
builder.Services.AddScoped<IModelRepository, ModelRepository>();

var lmStudioOptions = new LmStudioOptions();
builder.Configuration.GetSection("LmStudio").Bind(lmStudioOptions);
builder.Services.AddSingleton(lmStudioOptions);
builder.Services.AddHttpClient<LmStudioClient>();
builder.Services.AddHttpClient<SemanticKernelChatCompletionClient>();

//AI
// SK infra
builder.Services.AddSingleton<TILSOFTAI.Orchestration.SK.SkKernelFactory>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.SK.ExecutionContextAccessor>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.SK.ToolInvoker>();

// Tool invoker (bridge to existing ToolRegistry/ToolDispatcher)
builder.Services.AddScoped<ToolInvoker>();

// Plugins per module
builder.Services.AddSingleton<PluginCatalog>();

// Governance + Planner
builder.Services.AddScoped<TILSOFTAI.Orchestration.SK.Governance.CommitGuardFilter>();
builder.Services.AddSingleton<TILSOFTAI.Orchestration.SK.Planning.PlannerRouter>();
builder.Services.AddScoped<TILSOFTAI.Orchestration.SK.Planning.StepwiseLoopRunner>();

var useSemanticKernel = builder.Configuration.GetValue<bool>("Ai:UseSemanticKernel");
if (useSemanticKernel)
{
    builder.Services.AddScoped<IChatCompletionClient, SemanticKernelChatCompletionClient>();
}
else
{
    builder.Services.AddScoped<IChatCompletionClient, LmStudioChatCompletionClient>();
}

builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection("RateLimiting"));

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RateLimitMiddleware>();

app.MapControllers();

app.Run();
