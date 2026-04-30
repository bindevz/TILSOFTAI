using System.Data;
using Microsoft.Data.SqlClient;
using TILSOFTAI.Application.Security;

namespace TILSOFTAI.Persistence.Connection;

public sealed class SqlCommandExecutor(ISqlConnectionFactory connectionFactory)
{
    public async Task ExecuteAsync(string procedureName, IReadOnlyList<SqlParameter> parameters, int timeoutSeconds, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqlCommand command = CreateCommand(connection, procedureName, parameters, timeoutSeconds);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException ex) when (ex.Number == 51001)
        {
            throw new PermissionDeniedException("SQL permission denied.", ex);
        }
    }

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryRowsAsync(string procedureName, IReadOnlyList<SqlParameter> parameters, int timeoutSeconds, int maxRows, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqlCommand command = CreateCommand(connection, procedureName, parameters, timeoutSeconds);
        try
        {
            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            List<IReadOnlyDictionary<string, object?>> rows = [];
            while (await reader.ReadAsync(cancellationToken))
            {
                if (rows.Count >= maxRows)
                    break;

                Dictionary<string, object?> row = new(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = await reader.IsDBNullAsync(i, cancellationToken) ? null : reader.GetValue(i);
                rows.Add(row);
            }

            return rows;
        }
        catch (SqlException ex) when (ex.Number == 51001)
        {
            throw new PermissionDeniedException("SQL permission denied.", ex);
        }
    }

    private static SqlCommand CreateCommand(SqlConnection connection, string procedureName, IReadOnlyList<SqlParameter> parameters, int timeoutSeconds)
    {
        SqlCommand command = connection.CreateCommand();
        command.CommandText = procedureName;
        command.CommandType = CommandType.StoredProcedure;
        command.CommandTimeout = timeoutSeconds;
        foreach (SqlParameter parameter in parameters)
            command.Parameters.Add(parameter);
        return command;
    }
}

