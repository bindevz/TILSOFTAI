using System.Data;
using Microsoft.Data.SqlClient;

namespace TILSOFTAI.Persistence.Connection;

public static class SqlParameterFactory
{
    public static SqlParameter UniqueIdentifier(string name, Guid value) => new(name, SqlDbType.UniqueIdentifier) { Value = value };
    public static SqlParameter NVarChar(string name, string? value, int size = -1) => new(name, SqlDbType.NVarChar, size) { Value = (object?)value ?? DBNull.Value };
    public static SqlParameter Int(string name, int value) => new(name, SqlDbType.Int) { Value = value };
    public static SqlParameter BigInt(string name, long value) => new(name, SqlDbType.BigInt) { Value = value };
}

