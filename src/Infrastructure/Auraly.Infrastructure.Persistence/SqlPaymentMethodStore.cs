using System.Data;
using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.Application.Catalog;
using Auraly.Contracts.Catalog;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlPaymentMethodStore(SqlServerConnectionFactory connections,
    IPosSynchronizationOutboxDispatcher synchronization) : IPaymentMethodStore
{
    private const string Columns = "PaymentMethodId,Code,Name,PaymentTypeId,MethodCode,TypeLabel,AccountId,AccountCode,AccountName,BankAccountId,UseForSales,UseForReceipts,UseForSupplierPayments,UseForPayroll,IsActive,SortOrder,Version,RowVersion,UseForVouchers";

    public async Task<PaymentMethodCatalogSnapshot> GetSnapshotAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand($"""
            SELECT TenantId FROM dbo.Tenants WHERE TenantId=@Tenant;
            SELECT TOP(1001) {Columns} FROM reference.PaymentMethodDetails
            WHERE TenantId=@Tenant AND IsActive=1 AND IsReady=1
            ORDER BY SortOrder,Code,PaymentMethodId;
            """, connection);
        command.Parameters.AddWithValue("@Tenant", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new CatalogForbiddenException("El tenant no está disponible.");
        await reader.NextResultAsync(cancellationToken);
        var forms = new List<PaymentMethodView>();
        while (await reader.ReadAsync(cancellationToken)) forms.Add(Read(reader));
        if (forms.Count > 1000) throw new CatalogValidationException("La configuración de caja admite hasta 1.000 formas activas.");
        return new(PaymentMethodCatalogSnapshot.CurrentContractVersion, forms);
    }

    public async Task<PaymentMethodPage> ListAsync(CatalogUserIdentity user, PaymentMethodQuery query,
        CancellationToken cancellationToken)
    {
        const string filter = """
            TenantId=@Tenant AND (@Id IS NULL OR PaymentMethodId=@Id)
            AND (@Active IS NULL OR IsActive=@Active)
            AND (@Search IS NULL OR Code LIKE @Search OR Name LIKE @Search)
            AND (@Usage IS NULL OR IsActive=1 AND IsReady=1 AND
              ((@Usage=N'Sales' AND UseForSales=1) OR (@Usage=N'Receipts' AND UseForReceipts=1)
               OR (@Usage=N'SupplierPayments' AND UseForSupplierPayments=1) OR (@Usage=N'Payroll' AND UseForPayroll=1)
               OR (@Usage IN(N'VoucherReceipts',N'VoucherPayments') AND UseForVouchers=1 AND EXISTS(
                 SELECT 1 FROM accounting.PaymentMethodAccountReadiness(@Tenant,MethodCode,AccountId) readiness
                 WHERE readiness.SourceType=CASE WHEN @Usage=N'VoucherReceipts' THEN N'CustomerPaymentMethod' ELSE N'SupplierPaymentMethod' END))))
            AND (@Usage IS NULL OR EXISTS(SELECT 1 FROM dbo.AccountingAccounts account
              WHERE account.TenantId=@Tenant AND account.AccountId=reference.PaymentMethodDetails.AccountId))
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand($"""
            SELECT {Columns} FROM reference.PaymentMethodDetails WHERE {filter}
            ORDER BY SortOrder,Code,PaymentMethodId OFFSET @Offset ROWS FETCH NEXT @Size ROWS ONLY;
            SELECT COUNT(*) FROM reference.PaymentMethodDetails WHERE {filter};
            """, connection);
        command.Parameters.AddWithValue("@Tenant", user.TenantId);
        command.Parameters.AddWithValue("@Id", (object?)query.PaymentMethodId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Active", (object?)query.IsActive ?? DBNull.Value);
        command.Parameters.AddWithValue("@Search", string.IsNullOrEmpty(query.Search) ? DBNull.Value : $"%{query.Search}%");
        command.Parameters.AddWithValue("@Usage", (object?)query.Usage ?? DBNull.Value);
        command.Parameters.AddWithValue("@Offset", checked((query.Page - 1) * query.PageSize));
        command.Parameters.AddWithValue("@Size", query.PageSize);
        var items = new List<PaymentMethodView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) items.Add(Read(reader));
        await reader.NextResultAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new(items, query.Page, query.PageSize, reader.GetInt32(0));
    }

    public async Task<PaymentMethodView> SaveAsync(CatalogUserIdentity user, SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new SqlCommand($"""
            DECLARE @Type nvarchar(64),@ExistingType uniqueidentifier,@VoucherUsage bit;
            -- Serialize semantic edits with acceptance, whose catalog read holds
            -- the same head until the source and its references are committed.
            SELECT @ExistingType=PaymentTypeId,@VoucherUsage=COALESCE(@Vouchers,UseForVouchers) FROM reference.PaymentMethods WITH(UPDLOCK,HOLDLOCK)
              WHERE TenantId=@Tenant AND PaymentMethodId=@Id;
            SET @VoucherUsage=COALESCE(@VoucherUsage,@Vouchers,0);
            IF @ExistingType<>@TypeId AND (
              EXISTS(SELECT 1 FROM dbo.SalesPayments WHERE PaymentMethodId=@Id)
              OR EXISTS(SELECT 1 FROM dbo.SalesDocuments WHERE CreditPaymentMethodId=@Id)
              OR EXISTS(SELECT 1 FROM dbo.CustomerPaymentTenders WHERE PaymentMethodId=@Id)
              OR EXISTS(SELECT 1 FROM dbo.SupplierPaymentTenders WHERE PaymentMethodId=@Id)
              OR EXISTS(SELECT 1 FROM payroll.PaymentBatches WHERE TenantId=@Tenant AND PaymentMethodId=@Id)
              OR EXISTS(SELECT 1 FROM accounting.VoucherDraftLines line
                JOIN accounting.VoucherDrafts draft ON draft.DocumentId=line.DocumentId AND draft.TenantId=@Tenant
                WHERE line.PaymentMethodId=@Id AND draft.SentAt IS NOT NULL))
              THROW 51221,N'Una forma utilizada conserva su tipo de pago. Crea otra forma para un tipo distinto.',1;
            SELECT @Type=Code FROM reference.Options WITH(HOLDLOCK)
              WHERE OptionId=@TypeId AND CatalogCode=N'payment-method' AND (@Active=0 OR IsActive=1);
            IF @Type IS NULL OR NOT EXISTS(SELECT 1 FROM dbo.AccountingAccounts WITH(HOLDLOCK)
              WHERE TenantId=@Tenant AND AccountId=@Account
                AND (@Active=0 OR IsActive=1 AND AllowsPosting=1 AND FinancialFunction IS NOT NULL))
              THROW 51220,N'El tipo o auxiliar de la forma de pago no está disponible.',1;
            IF (@Type=N'Credit' AND (@Receipts=1 OR @Suppliers=1 OR @Payroll=1 OR @VoucherUsage=1))
              OR (@Payroll=1 AND @Type NOT IN(N'Cash',N'Transfer'))
              THROW 51220,N'El tipo no admite los usos seleccionados.',1;
            IF @Active=1 AND EXISTS(SELECT 1 FROM (VALUES
                (N'PosPaymentMethod',@Sales),(N'CustomerPaymentMethod',@Receipts),(N'SupplierPaymentMethod',@Suppliers),
                (N'CustomerPaymentMethod',@Payroll)) usage(SourceType,Enabled)
              WHERE usage.Enabled=1 AND NOT EXISTS(
                SELECT 1 FROM accounting.PaymentMethodAccountReadiness(@Tenant,@Type,@Account) readiness
                WHERE readiness.SourceType=usage.SourceType))
              THROW 51220,N'El auxiliar no es compatible con todos los usos seleccionados de la forma.',1;
            IF @Active=1 AND @VoucherUsage=1 AND NOT EXISTS(
              SELECT 1 FROM accounting.PaymentMethodAccountReadiness(@Tenant,@Type,@Account) readiness
              WHERE readiness.SourceType IN(N'CustomerPaymentMethod',N'SupplierPaymentMethod'))
              THROW 51220,N'El auxiliar no admite entradas ni salidas de dinero en comprobantes.',1;
            IF @RowVersion IS NULL
              INSERT reference.PaymentMethods(TenantId,PaymentMethodId,Code,Name,PaymentTypeId,AccountId,
                UseForSales,UseForReceipts,UseForSupplierPayments,UseForPayroll,UseForVouchers,IsActive,SortOrder,Version,CreatedAt,UpdatedAt)
              VALUES(@Tenant,@Id,@Code,@Name,@TypeId,@Account,@Sales,@Receipts,@Suppliers,@Payroll,@VoucherUsage,@Active,@Sort,1,SYSUTCDATETIME(),SYSUTCDATETIME());
            ELSE BEGIN
              UPDATE reference.PaymentMethods SET Name=@Name,PaymentTypeId=@TypeId,AccountId=@Account,
                UseForSales=@Sales,UseForReceipts=@Receipts,UseForSupplierPayments=@Suppliers,
                UseForPayroll=@Payroll,UseForVouchers=@VoucherUsage,IsActive=@Active,SortOrder=@Sort,Version=Version+1,UpdatedAt=SYSUTCDATETIME()
              WHERE TenantId=@Tenant AND PaymentMethodId=@Id AND Code=@Code AND RowVersion=@RowVersion;
              IF @@ROWCOUNT<>1 THROW 51221,N'La forma cambió o su código no coincide. Recarga antes de guardar.',1;
            END;
            DECLARE @Businesses TABLE(BusinessId uniqueidentifier PRIMARY KEY);
            INSERT @Businesses SELECT BusinessId FROM dbo.Businesses WHERE TenantId=@Tenant;
            INSERT dbo.PosSynchronizationOutboxMessages(NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
            SELECT NEWID(),business.BusinessId,N'Configuration',COALESCE(latest.CursorValue,0)+1,SYSUTCDATETIME()
            FROM @Businesses business OUTER APPLY(
              SELECT MAX(AvailableThroughCursor) CursorValue
              FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
              WHERE BusinessId=business.BusinessId AND Stream=N'Configuration') latest;
            SELECT {Columns} FROM reference.PaymentMethodDetails WHERE TenantId=@Tenant AND PaymentMethodId=@Id;
            SELECT BusinessId FROM @Businesses ORDER BY BusinessId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@Tenant", user.TenantId);
        command.Parameters.AddWithValue("@Id", request.PaymentMethodId);
        command.Parameters.AddWithValue("@Code", request.Code);
        command.Parameters.AddWithValue("@Name", request.Name);
        command.Parameters.AddWithValue("@TypeId", request.PaymentTypeId);
        command.Parameters.AddWithValue("@Account", request.AccountId);
        command.Parameters.AddWithValue("@Sales", request.UseForSales);
        command.Parameters.AddWithValue("@Receipts", request.UseForReceipts);
        command.Parameters.AddWithValue("@Suppliers", request.UseForSupplierPayments);
        command.Parameters.AddWithValue("@Payroll", request.UseForPayroll);
        command.Parameters.Add("@Vouchers", SqlDbType.Bit).Value = (object?)request.UseForVouchers ?? DBNull.Value;
        command.Parameters.AddWithValue("@Active", request.IsActive);
        command.Parameters.AddWithValue("@Sort", request.SortOrder);
        command.Parameters.Add("@RowVersion", SqlDbType.Binary, 8).Value = (object?)request.RowVersion ?? DBNull.Value;
        try
        {
            PaymentMethodView result;
            var businesses = new List<Guid>();
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                await reader.ReadAsync(cancellationToken);
                result = Read(reader);
                await reader.NextResultAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken)) businesses.Add(reader.GetGuid(0));
            }
            await transaction.CommitAsync(cancellationToken);
            // The same committed batch returns its affected scopes. Signalling
            // the existing dispatcher performs no read to rediscover them.
            foreach (var business in businesses)
                await synchronization.DispatchPendingAsync(user.TenantId, business, CancellationToken.None);
            return result;
        }
        catch (SqlException error) when (error.Number == 51220)
        { throw new CatalogValidationException(error.Message); }
        catch (SqlException error) when (error.Number is 51221 or 2601 or 2627)
        { throw new CatalogConflictException(error.Number == 51221 ? error.Message : "Ya existe una forma con ese código o identidad."); }
    }

    private static PaymentMethodView Read(SqlDataReader reader) => new(reader.GetGuid(0),reader.GetString(1),
        reader.GetString(2),reader.GetGuid(3),reader.GetString(4),reader.GetString(5),reader.GetGuid(6),
        reader.GetString(7),reader.GetString(8),reader.IsDBNull(9)?null:reader.GetGuid(9),
        reader.GetBoolean(10),reader.GetBoolean(11),reader.GetBoolean(12),reader.GetBoolean(13),
        reader.GetBoolean(14),reader.GetInt32(15),reader.GetInt32(16),(byte[])reader[17],reader.GetBoolean(18));
}
