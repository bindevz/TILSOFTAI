using Microsoft.Data.SqlClient;
using TILSOFTAI.Contracts.Configuration;

namespace TILSOFTAI.Persistence.Connection;

public sealed class SqlConnectionFactory(TilsoftAiOptions options) : ISqlConnectionFactory
{
    public async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionStrings.TilsoftAi))
            throw new InvalidOperationException("ConnectionStrings:TilsoftAi is required.");

        SqlConnection connection = new(options.ConnectionStrings.TilsoftAi);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}

