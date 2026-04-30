namespace TILSOFTAI.SqlTests;

public static class SqlContractTests
{
    public static void Run()
    {
        string root = FindRepositoryRoot();
        string tools = File.ReadAllText(Path.Combine(root, "database", "TILSOFTAI", "070_model_tools.sql"));
        string security = File.ReadAllText(Path.Combine(root, "database", "TILSOFTAI", "020_security_schema.sql"));
        string runtime = File.ReadAllText(Path.Combine(root, "database", "TILSOFTAI", "080_ai_runtime_procedures.sql"));
        string seed = File.ReadAllText(Path.Combine(root, "database", "TILSOFTAI", "060_model_seed_data.sql"));

        TestAssert.Contains(tools, "@TenantId UNIQUEIDENTIFIER");
        TestAssert.Contains(tools, "@UserId UNIQUEIDENTIFIER");
        TestAssert.Contains(tools, "@CorrelationId NVARCHAR(100)");
        TestAssert.Contains(tools, "security.UserEffectivePermission(@TenantId, @UserId)");
        TestAssert.Contains(tools, "p.TenantId = @TenantId");
        TestAssert.DoesNotContain(tools, "EXEC(@");
        TestAssert.Contains(security, "CREATE OR ALTER FUNCTION security.UserEffectivePermission");
        TestAssert.Contains(runtime, "CREATE OR ALTER PROCEDURE ai.usp_CreateRun");
        TestAssert.Contains(runtime, "CREATE OR ALTER PROCEDURE ai.usp_SearchModelCapabilities");
        TestAssert.Contains(runtime, "artifact.usp_GetArtifactMetadata");
        TestAssert.Contains(runtime, "WHERE a.TenantId = @TenantId");
        TestAssert.Contains(seed, "MODEL-002");
        TestAssert.Contains(seed, "Tenant B");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TILSOFTAI.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
