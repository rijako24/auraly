using Auraly.BuildingBlocks.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
namespace Auraly.Commerce.Accounting.Infrastructure;
public sealed class AccountingSqlConnectionFactory
{
    private readonly AuralySqlConnectionSource source;
    public AccountingSqlConnectionFactory(AuralySqlConnectionSource source) => this.source = source;
    public SqlConnection Create()=>new(source.ConnectionString);
}
