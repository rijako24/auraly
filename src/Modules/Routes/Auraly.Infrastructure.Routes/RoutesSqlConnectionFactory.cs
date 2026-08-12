using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Routes;

public sealed class RoutesSqlConnectionFactory
{
    private readonly string connectionString;

    public RoutesSqlConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A SQL Server connection string is required.", nameof(connectionString));
        this.connectionString = connectionString;
    }

    public SqlConnection Create() => new(connectionString);
}
