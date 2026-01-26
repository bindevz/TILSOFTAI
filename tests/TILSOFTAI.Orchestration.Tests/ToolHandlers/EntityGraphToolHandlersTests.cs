using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TILSOFTAI.Application.Services;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Execution;
using TILSOFTAI.Orchestration.Modules.EntityGraph.Handlers;
using TILSOFTAI.Orchestration.Tools;
using Xunit;

namespace TILSOFTAI.Orchestration.Tests.ToolHandlers;

public class EntityGraphToolHandlersTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task EntityGraphSearch_ReturnsHits_WhenFound()
    {
        var repo = new FakeEntityGraphRepository();
        var service = new EntityGraphService(repo);
        var handler = new EntityGraphSearchToolHandler(service, NullLogger<EntityGraphSearchToolHandler>.Instance);

        var intent = new DynamicToolIntent(
            Filters: new Dictionary<string, string?>(),
            Page: 1,
            PageSize: 20,
            Args: new Dictionary<string, object?>
            {
                ["query"] = "sales",
                ["topK"] = 5
            });

        var context = BuildContext();
        var result = await handler.HandleAsync(intent, context, CancellationToken.None);

        Assert.True(result.Result.Success);
        var payload = JsonSerializer.Serialize(result.Result.Data, Json);
        Assert.Contains("sales", payload);
        Assert.Contains("G1", payload);
        
        Assert.NotNull(result.Extras);
        Assert.Single(result.Extras.Evidence);
        Assert.Equal("graph_search_hits", result.Extras.Evidence.First().Id);
    }

    [Fact]
    public async Task EntityGraphGet_ReturnsGraph_WhenFound()
    {
        var repo = new FakeEntityGraphRepository();
        var service = new EntityGraphService(repo);
        var handler = new EntityGraphGetToolHandler(service, NullLogger<EntityGraphGetToolHandler>.Instance);

        var intent = new DynamicToolIntent(
            Filters: new Dictionary<string, string?>(),
            Page: 1,
            PageSize: 20,
            Args: new Dictionary<string, object?> { ["graphCode"] = "G1" });

        var context = BuildContext();
        var result = await handler.HandleAsync(intent, context, CancellationToken.None);

        Assert.True(result.Result.Success);
        var payload = JsonSerializer.Serialize(result.Result.Data, Json);
        Assert.Contains("G1", payload);
        Assert.Contains("sales", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stats", payload, StringComparison.OrdinalIgnoreCase);

        Assert.NotNull(result.Extras);
        Assert.Contains(result.Extras.Evidence, e => e.Id == "graph_definition");
    }

    [Fact]
    public async Task EntityGraphGet_ReturnsFailure_WhenNotFound()
    {
        var repo = new FakeEntityGraphRepository();
        var service = new EntityGraphService(repo);
        var handler = new EntityGraphGetToolHandler(service, NullLogger<EntityGraphGetToolHandler>.Instance);

        var intent = new DynamicToolIntent(
            Filters: new Dictionary<string, string?>(),
            Page: 1,
            PageSize: 20,
            Args: new Dictionary<string, object?> { ["graphCode"] = "missing" });

        var context = BuildContext();
        var result = await handler.HandleAsync(intent, context, CancellationToken.None);

        Assert.False(result.Result.Success);
        Assert.Contains("failed", result.Result.Message ?? "", StringComparison.OrdinalIgnoreCase);

        var payload = JsonSerializer.Serialize(result.Result.Data, Json);
        Assert.Contains("not found", payload, StringComparison.OrdinalIgnoreCase);
    }

    private static TSExecutionContext BuildContext() => new()
    {
        TenantId = "test",
        UserId = "test",
        RequestId = "req",
        TraceId = "trace"
    };

    private sealed class FakeEntityGraphRepository : IEntityGraphRepository
    {
        public Task<IReadOnlyList<EntityGraphSearchHit>> SearchAsync(string query, int topK, CancellationToken cancellationToken)
        {
            if (query == "empty") return Task.FromResult<IReadOnlyList<EntityGraphSearchHit>>(Array.Empty<EntityGraphSearchHit>());
            
            return Task.FromResult<IReadOnlyList<EntityGraphSearchHit>>([
                new EntityGraphSearchHit(1, "G1", "Sales", "Stats", "tag", "sp_root", "Mo ta", "Description", 100, DateTimeOffset.UtcNow, Array.Empty<EntityGraphPackHint>())
            ]);
        }

        public Task<EntityGraphDefinition?> GetByCodeAsync(string graphCode, CancellationToken cancellationToken)
        {
            if (graphCode == "missing") return Task.FromResult<EntityGraphDefinition?>(null);

            return Task.FromResult<EntityGraphDefinition?>(new EntityGraphDefinition(
                new EntityGraphSummary(1, "G1", "Sales", "Stats", "tag", "sp_root", "Mo ta", "Description", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
                new List<EntityGraphPackSummary>(),
                new List<EntityGraphNode>(),
                new List<EntityGraphEdge>(),
                new List<EntityGraphGlossaryEntry>()
            ));
        }
    }
}
