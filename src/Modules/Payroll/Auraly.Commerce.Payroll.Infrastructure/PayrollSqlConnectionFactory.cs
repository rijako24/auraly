using Auraly.BuildingBlocks.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Payroll.Infrastructure;

public sealed class PayrollSqlConnectionFactory
{
    private readonly AuralySqlConnectionSource source;

    public PayrollSqlConnectionFactory(AuralySqlConnectionSource source) =>
        this.source = source;

    public SqlConnection Create() => new(source.ConnectionString);
}
