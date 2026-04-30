using Microsoft.Data.SqlClient;

namespace TILSOFTAI.Persistence.Connection;

public interface ISqlConnectionFactory
{
    Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken);
}

