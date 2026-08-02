using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Pricing;

public sealed class PricingSqlConnectionFactory
{
    private readonly string connectionString;

    public PricingSqlConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A SQL Server connection string is required.", nameof(connectionString));
        this.connectionString = connectionString;
    }

    public SqlConnection Create() => new(connectionString);
}
