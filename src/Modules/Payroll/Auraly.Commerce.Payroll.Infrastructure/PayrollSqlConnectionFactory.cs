using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Payroll.Infrastructure;

public sealed class PayrollSqlConnectionFactory
{
    private readonly string connectionString;

    public PayrollSqlConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A SQL Server connection string is required.", nameof(connectionString));
        this.connectionString = connectionString;
    }

    public SqlConnection Create() => new(connectionString);
}
