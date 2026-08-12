using Microsoft.Data.SqlClient;
namespace Auraly.Commerce.Accounting.Infrastructure;
public sealed class AccountingSqlConnectionFactory
{
    private readonly string _connectionString;
    public AccountingSqlConnectionFactory(string connectionString)
    {
        if(string.IsNullOrWhiteSpace(connectionString))throw new ArgumentException("A SQL Server connection string is required.",nameof(connectionString));
        _connectionString=connectionString;
    }
    public SqlConnection Create()=>new(_connectionString);
}
