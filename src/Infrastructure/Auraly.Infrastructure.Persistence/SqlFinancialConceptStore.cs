using System.Data;
using Auraly.Application.Catalog;
using Auraly.Contracts.Catalog;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlFinancialConceptStore(SqlServerConnectionFactory connections):IFinancialConceptStore
{
    private const string Columns="FinancialConceptId,Code,Name,AccountId,AccountCode,AccountName,FinancialFunction,SideOptionId,SideCode,SideLabel,UseForReceipts,UseForSupplierPayments,UseForVouchers,IsActive,SortOrder,RowVersion";
    private const string Filter="""
        c.TenantId=@Tenant AND (@Id IS NULL OR c.FinancialConceptId=@Id)
        AND (@Active IS NULL OR c.IsActive=@Active) AND (@Search IS NULL OR c.Code LIKE @Search OR c.Name LIKE @Search)
        AND (@Usage IS NULL OR @Usage=N'Filter' OR c.IsActive=1 AND
          ((@Usage=N'Receipts' AND c.UseForReceipts=1) OR (@Usage=N'SupplierPayments' AND c.UseForSupplierPayments=1)
            OR (@Usage=N'Vouchers' AND c.UseForVouchers=1)) AND EXISTS(
              SELECT 1 FROM reference.FinancialConceptDetails ready WHERE ready.FinancialConceptId=c.FinancialConceptId
                AND ready.TenantId=@Tenant AND ready.IsReady=1))
        """;
    private static string PageSql=>$"""
        SELECT c.FinancialConceptId,c.SortOrder,c.Code INTO #ConceptPage FROM reference.FinancialConcepts c WHERE {Filter}
        ORDER BY c.SortOrder,c.Code,c.FinancialConceptId OFFSET @Offset ROWS FETCH NEXT @Size ROWS ONLY;
        SELECT {string.Join(",",Columns.Split(',').Select(column=>"detail."+column))}
        FROM #ConceptPage page JOIN reference.FinancialConceptDetails detail ON detail.FinancialConceptId=page.FinancialConceptId
        ORDER BY page.SortOrder,page.Code,page.FinancialConceptId;
        SELECT COUNT(*) FROM reference.FinancialConcepts c WHERE {Filter};
        """;

    public async Task<FinancialConceptPage> ListAsync(CatalogUserIdentity user,FinancialConceptQuery query,CancellationToken token)
    {
        await using var connection=connections.Create();await connection.OpenAsync(token);
        await using var command=new SqlCommand(PageSql,connection);AddScope(command,user,query);
        await using var reader=await command.ExecuteReaderAsync(token);
        return await ReadPageAsync(reader,query,token);
    }

    public async Task<FinancialConceptSaveResult> SaveAsync(CatalogUserIdentity user,SaveFinancialConceptRequest request,
        FinancialConceptQuery query,CancellationToken token)
    {
        await using var connection=connections.Create();await connection.OpenAsync(token);
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,token);
        await using var command=new SqlCommand("""
            DECLARE @OldFunction nvarchar(64),@OldSide uniqueidentifier,@Function nvarchar(64),@AccountExists uniqueidentifier;
            SELECT @OldFunction=account.FinancialFunction,@OldSide=concept.SideOptionId
              FROM reference.FinancialConcepts concept WITH(UPDLOCK,HOLDLOCK)
              JOIN dbo.AccountingAccounts account ON account.AccountId=concept.AccountId AND account.TenantId=@Tenant
              WHERE concept.TenantId=@Tenant AND concept.FinancialConceptId=@Concept;
            SELECT @AccountExists=account.AccountId,@Function=account.FinancialFunction
              FROM dbo.AccountingAccounts account WITH(HOLDLOCK)
              CROSS APPLY accounting.FinancialConceptAccountReadiness(@Tenant,account.AccountId,@SideId,@Enabled) ready
              WHERE account.AccountId=@Account AND account.TenantId=@Tenant;
            IF @AccountExists IS NULL
              THROW 51230,N'El concepto requiere un auxiliar general o de anticipo, con naturaleza compatible. Tesorería se selecciona como forma de pago.',1;
            IF (ISNULL(@OldFunction,N'')<>ISNULL(@Function,N'') OR @OldSide<>@SideId) AND EXISTS(
              SELECT 1 FROM accounting.SourceConceptIndex WHERE FinancialConceptId=@Concept)
              THROW 51231,N'Un concepto utilizado conserva su función y naturaleza. Crea otro para cambiar ese efecto.',1;
            IF @Version IS NULL
              INSERT reference.FinancialConcepts(FinancialConceptId,TenantId,Code,Name,AccountId,SideOptionId,
                UseForReceipts,UseForSupplierPayments,UseForVouchers,IsActive,SortOrder,CreatedAt,UpdatedAt)
              VALUES(@Concept,@Tenant,@Code,@Name,@Account,@SideId,@Receipts,@Suppliers,@Vouchers,@Enabled,@Order,SYSUTCDATETIME(),SYSUTCDATETIME());
            ELSE BEGIN
              UPDATE reference.FinancialConcepts SET Name=@Name,AccountId=@Account,SideOptionId=@SideId,
                UseForReceipts=@Receipts,UseForSupplierPayments=@Suppliers,UseForVouchers=@Vouchers,IsActive=@Enabled,
                SortOrder=@Order,UpdatedAt=SYSUTCDATETIME()
              WHERE FinancialConceptId=@Concept AND TenantId=@Tenant AND Code=@Code AND RowVersion=@Version;
              IF @@ROWCOUNT<>1 THROW 51231,N'El concepto cambió o su código no coincide. Recarga antes de guardar.',1;
            END;
            """+$"SELECT {Columns} FROM reference.FinancialConceptDetails WHERE TenantId=@Tenant AND FinancialConceptId=@Concept;\n"+PageSql,connection,transaction);
        AddScope(command,user,query);
        command.Parameters.AddWithValue("@Concept",request.FinancialConceptId);
        command.Parameters.AddWithValue("@Code",request.Code);command.Parameters.AddWithValue("@Name",request.Name);
        command.Parameters.AddWithValue("@Account",request.AccountId);command.Parameters.AddWithValue("@SideId",request.SideOptionId);
        command.Parameters.AddWithValue("@Receipts",request.UseForReceipts);command.Parameters.AddWithValue("@Suppliers",request.UseForSupplierPayments);
        command.Parameters.AddWithValue("@Vouchers",request.UseForVouchers);command.Parameters.AddWithValue("@Enabled",request.IsActive);
        command.Parameters.AddWithValue("@Order",request.SortOrder);
        command.Parameters.Add("@Version",SqlDbType.Binary,8).Value=(object?)request.RowVersion??DBNull.Value;
        FinancialConceptSaveResult result;
        try
        {
            await using var reader=await command.ExecuteReaderAsync(token);
            if(!await reader.ReadAsync(token))throw new InvalidOperationException("The saved concept is missing.");
            var concept=Read(reader);await reader.NextResultAsync(token);
            result=new(concept,await ReadPageAsync(reader,query,token));
        }
        catch(SqlException error) when(error.Number is 51230 or 51231 or 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            if(error.Number==51230)throw new CatalogValidationException(error.Message);
            throw new CatalogConflictException(error.Number==51231?error.Message:"El código ya pertenece a otro concepto.");
        }
        await transaction.CommitAsync(token);return result;
    }

    private static void AddScope(SqlCommand command,CatalogUserIdentity user,FinancialConceptQuery query)
    {
        command.Parameters.AddWithValue("@Tenant",user.TenantId);
        command.Parameters.AddWithValue("@Id",(object?)query.FinancialConceptId??DBNull.Value);
        command.Parameters.AddWithValue("@Active",(object?)query.IsActive??DBNull.Value);
        command.Parameters.AddWithValue("@Search",string.IsNullOrEmpty(query.Search)?DBNull.Value:$"%{query.Search}%");
        command.Parameters.AddWithValue("@Usage",(object?)query.Usage??DBNull.Value);
        command.Parameters.AddWithValue("@Offset",checked((query.Page-1)*query.PageSize));command.Parameters.AddWithValue("@Size",query.PageSize);
    }
    private static FinancialConceptView Read(SqlDataReader row)=>new(row.GetGuid(0),row.GetString(1),row.GetString(2),row.GetGuid(3),
        row.GetString(4),row.GetString(5),row.IsDBNull(6)?null:row.GetString(6),row.GetGuid(7),row.GetString(8),row.GetString(9),
        row.GetBoolean(10),row.GetBoolean(11),row.GetBoolean(12),row.GetBoolean(13),row.GetInt32(14),(byte[])row[15]);
    private static async Task<FinancialConceptPage> ReadPageAsync(SqlDataReader reader,FinancialConceptQuery query,CancellationToken token)
    {
        var rows=new List<FinancialConceptView>();while(await reader.ReadAsync(token))rows.Add(Read(reader));
        await reader.NextResultAsync(token);await reader.ReadAsync(token);
        return new(rows,query.Page,query.PageSize,reader.GetInt32(0));
    }
}
