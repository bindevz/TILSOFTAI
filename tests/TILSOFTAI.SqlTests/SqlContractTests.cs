namespace TILSOFTAI.SqlTests;

public static class SqlContractTests
{
    public static void Run()
    {
        string root = FindRepositoryRoot();
        string tools = File.ReadAllText(Path.Combine(root, "database", "TILSOFTAI", "070_model_tools.sql"));
        string security = File.ReadAllText(Path.Combine(root, "database", "TILSOFTAI", "020_security_schema.sql"));

        TestAssert.Contains(tools, "@TenantId UNIQUEIDENTIFIER");
        TestAssert.Contains(tools, "@UserId UNIQUEIDENTIFIER");
        TestAssert.Contains(tools, "@CorrelationId NVARCHAR(100)");
        TestAssert.Contains(tools, "security.UserEffectivePermission(@TenantId, @UserId)");
        TestAssert.Contains(tools, "p.TenantId = @TenantId");
        TestAssert.DoesNotContain(tools, "EXEC(@");
        TestAssert.Contains(security, "CREATE OR ALTER FUNCTION security.UserEffectivePermission");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TILSOFTAI.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
