using Auraly.BuildingBlocks.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Routes;

public sealed class RoutesSqlConnectionFactory
{
    private readonly AuralySqlConnectionSource source;

    public RoutesSqlConnectionFactory(AuralySqlConnectionSource source) =>
        this.source = source;

    public SqlConnection Create() => new(source.ConnectionString);
}
