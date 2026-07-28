using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlServerConnectionFactory
{
    private readonly string _connectionString;

    public SqlServerConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A SQL Server connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public SqlConnection Create() => new(_connectionString);
}

