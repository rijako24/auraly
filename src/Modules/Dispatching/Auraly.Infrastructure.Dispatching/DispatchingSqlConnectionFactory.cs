using Microsoft.Data.SqlClient;
namespace Auraly.Infrastructure.Dispatching;
public sealed class DispatchingSqlConnectionFactory(string connectionString)
{
    public SqlConnection Create() => new(connectionString);
}
