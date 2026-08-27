using Auraly.BuildingBlocks.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlServerConnectionFactory
{
    private readonly AuralySqlConnectionSource source;

    public SqlServerConnectionFactory(AuralySqlConnectionSource source) =>
        this.source = source;

    public SqlConnection Create() => new(source.ConnectionString);
}

