using Auraly.BuildingBlocks.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Pricing;

public sealed class PricingSqlConnectionFactory
{
    private readonly AuralySqlConnectionSource source;

    public PricingSqlConnectionFactory(AuralySqlConnectionSource source) =>
        this.source = source;

    public SqlConnection Create() => new(source.ConnectionString);
}
