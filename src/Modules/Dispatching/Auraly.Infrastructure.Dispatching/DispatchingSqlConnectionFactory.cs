using Auraly.BuildingBlocks.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
namespace Auraly.Infrastructure.Dispatching;
public sealed class DispatchingSqlConnectionFactory(AuralySqlConnectionSource source)
{
    public SqlConnection Create() => new(source.ConnectionString);
}
