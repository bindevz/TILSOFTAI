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
builder.Services.AddScoped<OrdersService>();
builder.Services.AddScoped<CustomersService>();
builder.Services.AddScoped<ChatPipeline>();
builder.Services.AddSingleton<IAuditLogger, AuditLogger>();

var lmStudioOptions = new LmStudioOptions();
builder.Configuration.GetSection("LmStudio").Bind(lmStudioOptions);
builder.Services.AddSingleton(lmStudioOptions);
builder.Services.AddHttpClient<LmStudioClient>();

builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection("RateLimiting"));

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RateLimitMiddleware>();

app.MapControllers();

app.Run();
