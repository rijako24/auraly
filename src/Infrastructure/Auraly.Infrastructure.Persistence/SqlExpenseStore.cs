using System.Data;
using System.Text.Json;
using Auraly.Application.Expenses;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Commerce.Taxation.Contracts;
using Auraly.Contracts.Expenses;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Purchasing;
using Auraly.Contracts.Sales;
using Auraly.Application.Fiscal;
using Auraly.Domain.Expenses;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlExpenseStore(SqlServerConnectionFactory connections, IAuralyIdGenerator ids,
    TimeProvider timeProvider) : IExpenseStore
{
    public async Task<ExpenseWorkspaceOptions> GetOptionsAsync(ExpenseUserIdentity user, CancellationToken ct)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT c.ExpenseConceptId,c.BusinessId,c.Code,c.Name,c.ExpenseAccountId,a.Code,a.Name,
              c.DefaultCostCenterId,cc.Name,c.WithholdingConceptCode,c.IsActive
            FROM dbo.ExpenseConcepts c JOIN dbo.AccountingAccounts a ON a.AccountId=c.ExpenseAccountId
            LEFT JOIN dbo.AccountingCostCenters cc ON cc.CostCenterId=c.DefaultCostCenterId
            WHERE c.BusinessId=@BusinessId AND c.IsActive=1 ORDER BY c.Name,c.Code;
            SELECT SupplierId,Identification,Name FROM dbo.Suppliers
              WHERE BusinessId=@BusinessId AND IsActive=1 AND 1=0
              ORDER BY Name,Identification;
            SELECT a.AccountId,a.Code,a.Name FROM dbo.AccountingAccounts a
              JOIN dbo.Businesses b ON b.TenantId=a.TenantId
              WHERE b.BusinessId=@BusinessId AND a.IsActive=1 AND a.AllowsPosting=1 AND a.AccountType=N'Expense'
              ORDER BY a.Code;
            SELECT CostCenterId,Code,Name,IsDefault FROM dbo.AccountingCostCenters
              WHERE BusinessId=@BusinessId AND IsActive=1 ORDER BY IsDefault DESC,Code;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var concepts = new List<ExpenseConceptView>(); while (await reader.ReadAsync(ct)) concepts.Add(ReadConcept(reader));
        await reader.NextResultAsync(ct); var suppliers = new List<ExpenseSupplierOption>();
        while (await reader.ReadAsync(ct)) suppliers.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        await reader.NextResultAsync(ct); var accounts = new List<ExpenseAccountOption>();
        while (await reader.ReadAsync(ct)) accounts.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        await reader.NextResultAsync(ct); var centers = new List<ExpenseCostCenterOption>();
        while (await reader.ReadAsync(ct)) centers.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3)));
        return new(concepts, suppliers, accounts, centers,
        [
            new(PurchaseEvidenceTypes.SupplierElectronicInvoice, "Factura electrónica", "Factura electrónica emitida por el proveedor."),
            new(PurchaseEvidenceTypes.InternalReceiptVoucher, "Comprobante interno", "Comprobante interno para respaldar el gasto."),
            new(PurchaseEvidenceTypes.BuyerElectronicSupportDocument, "Documento soporte", "Documento soporte electrónico emitido y enviado a la DIAN.")
        ]);
    }

    public async Task<IReadOnlyList<ExpenseConceptView>> ListConceptsAsync(ExpenseUserIdentity user, bool includeInactive, CancellationToken ct)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT c.ExpenseConceptId,c.BusinessId,c.Code,c.Name,c.ExpenseAccountId,a.Code,a.Name,
              c.DefaultCostCenterId,cc.Name,c.WithholdingConceptCode,c.IsActive
            FROM dbo.ExpenseConcepts c JOIN dbo.AccountingAccounts a ON a.AccountId=c.ExpenseAccountId
            LEFT JOIN dbo.AccountingCostCenters cc ON cc.CostCenterId=c.DefaultCostCenterId
            WHERE c.BusinessId=@BusinessId AND (@All=1 OR c.IsActive=1) ORDER BY c.Name,c.Code;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId); command.Parameters.AddWithValue("@All", includeInactive);
        var values = new List<ExpenseConceptView>(); await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) values.Add(ReadConcept(reader)); return values;
    }

    public async Task<ExpenseConceptView> SaveConceptAsync(ExpenseUserIdentity user, SaveExpenseConceptRequest request, CancellationToken ct)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await using var command = new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
                  THROW 51600,'La empresa está fuera del tenant.',1;
                IF NOT EXISTS(SELECT 1 FROM dbo.AccountingAccounts WHERE AccountId=@AccountId AND TenantId=@TenantId
                  AND AccountType=N'Expense' AND AllowsPosting=1 AND IsActive=1)
                  THROW 51601,'La cuenta debe ser una cuenta de gasto activa que permita movimientos.',1;
                IF @CenterId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.AccountingCostCenters WHERE CostCenterId=@CenterId AND BusinessId=@BusinessId AND IsActive=1)
                  THROW 51602,'El centro de costo está fuera de la empresa.',1;
                UPDATE dbo.ExpenseConcepts WITH(UPDLOCK,HOLDLOCK) SET Name=@Name,ExpenseAccountId=@AccountId,
                  DefaultCostCenterId=@CenterId,WithholdingConceptCode=@WithholdingCode,IsActive=@Active,UpdatedAt=@Now
                  WHERE ExpenseConceptId=@Id AND BusinessId=@BusinessId;
                IF @@ROWCOUNT=0 INSERT dbo.ExpenseConcepts(ExpenseConceptId,BusinessId,Code,Name,ExpenseAccountId,
                  DefaultCostCenterId,WithholdingConceptCode,IsActive,CreatedAt,UpdatedAt)
                  VALUES(@Id,@BusinessId,CONCAT(N'EXP-',UPPER(LEFT(REPLACE(CONVERT(nvarchar(36),@Id),N'-',N''),12))),@Name,@AccountId,@CenterId,@WithholdingCode,@Active,@Now,@Now);
                """, connection, tx);
            command.Parameters.AddWithValue("@Id", request.ConceptId); command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            command.Parameters.AddWithValue("@TenantId", user.TenantId);
            command.Parameters.AddWithValue("@Name", request.Name); command.Parameters.AddWithValue("@AccountId", request.ExpenseAccountId);
            command.Parameters.AddWithValue("@CenterId", (object?)request.DefaultCostCenterId ?? DBNull.Value);
            command.Parameters.AddWithValue("@WithholdingCode", (object?)request.WithholdingConceptCode ?? DBNull.Value);
            command.Parameters.AddWithValue("@Active", request.IsActive); command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            await command.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct);
        }
        catch (SqlException error) when (error.Number is >= 51600 and <= 51602) { await tx.RollbackAsync(CancellationToken.None); throw new ExpenseValidationException(error.Message); }
        catch (SqlException error) when (error.Number is 2601 or 2627) { await tx.RollbackAsync(CancellationToken.None); throw new ExpenseConflictException("Ya existe un concepto con ese código."); }
        catch { await tx.RollbackAsync(CancellationToken.None); throw; }
        return await GetConceptAsync(user, request.ConceptId, ct)
            ?? throw new DBConcurrencyException("The saved expense concept was not found.");
    }

    public async Task<ExpensePage> ListAsync(ExpenseUserIdentity user, int page, int pageSize, string? search, Guid? conceptId,
        Guid? supplierId, DateOnly? from, DateOnly? to, string? status, string? payableStatus, CancellationToken ct)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        const string filter = """e.BusinessId=@BusinessId AND (@Search IS NULL OR e.DocumentNumber LIKE N'%'+@Search+N'%' OR e.SupplierDocumentNumber LIKE N'%'+@Search+N'%' OR s.Name LIKE N'%'+@Search+N'%' OR c.Name LIKE N'%'+@Search+N'%') AND (@ConceptId IS NULL OR e.ExpenseConceptId=@ConceptId) AND (@SupplierId IS NULL OR e.SupplierId=@SupplierId) AND (@From IS NULL OR CONVERT(date,e.IssuedAt)>=@From) AND (@To IS NULL OR CONVERT(date,e.IssuedAt)<=@To) AND (@Status IS NULL OR (@Status=N'Returned' AND e.Status=N'Processed' AND returned.AppliedChargeId IS NOT NULL) OR (@Status<>N'Returned' AND e.Status=@Status AND (@Status<>N'Processed' OR returned.AppliedChargeId IS NULL))) AND (@PayableStatus IS NULL OR (@PayableStatus=N'None' AND p.PayableId IS NULL) OR p.Status=@PayableStatus)""";
        await using var command = new SqlCommand($"""
            SELECT COUNT(*),COALESCE(SUM(e.GrossAmount),0),COALESCE(SUM(e.WithholdingAmount),0),COALESCE(SUM(e.NetPayable),0)
              FROM dbo.Expenses e JOIN dbo.Suppliers s ON s.SupplierId=e.SupplierId JOIN dbo.ExpenseConcepts c ON c.ExpenseConceptId=e.ExpenseConceptId
              LEFT JOIN dbo.Payables p ON p.SourceDocumentId=e.ExpenseId AND p.SourceDocumentType=N'Expense' AND p.BusinessId=e.BusinessId
              LEFT JOIN dbo.SalesReturnCharges returned ON returned.AppliedChargeId=e.ExpenseId WHERE {filter};
            SELECT e.ExpenseId,e.DocumentNumber,e.SupplierDocumentNumber,e.SupplierId,s.Name,e.ExpenseConceptId,c.Name,
              e.IssuedAt,e.DueDate,e.GrossAmount,e.WithholdingAmount,e.NetPayable,e.CurrencyCode,e.Status,e.EvidenceUrl,e.PurchaseEvidenceType,
              p.Status,p.OutstandingAmount,CAST(CASE WHEN returned.AppliedChargeId IS NULL THEN 0 ELSE 1 END AS bit)
              FROM dbo.Expenses e JOIN dbo.Suppliers s ON s.SupplierId=e.SupplierId JOIN dbo.ExpenseConcepts c ON c.ExpenseConceptId=e.ExpenseConceptId
              LEFT JOIN dbo.Payables p ON p.SourceDocumentId=e.ExpenseId AND p.SourceDocumentType=N'Expense' AND p.BusinessId=e.BusinessId
              LEFT JOIN dbo.SalesReturnCharges returned ON returned.AppliedChargeId=e.ExpenseId
              WHERE {filter} ORDER BY e.IssuedAt DESC,e.ExpenseId OFFSET @Offset ROWS FETCH NEXT @Size ROWS ONLY;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId); command.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
        command.Parameters.AddWithValue("@ConceptId", (object?)conceptId ?? DBNull.Value); command.Parameters.AddWithValue("@SupplierId", (object?)supplierId ?? DBNull.Value);
        command.Parameters.AddWithValue("@From", from is null ? DBNull.Value : from.Value.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@To", to is null ? DBNull.Value : to.Value.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);
        command.Parameters.AddWithValue("@PayableStatus", (object?)payableStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("@Offset", (page - 1) * pageSize); command.Parameters.AddWithValue("@Size", pageSize);
        await using var reader = await command.ExecuteReaderAsync(ct); await reader.ReadAsync(ct);
        var count = reader.GetInt32(0); var gross = reader.GetDecimal(1); var held = reader.GetDecimal(2); var net = reader.GetDecimal(3);
        await reader.NextResultAsync(ct); var items = new List<ExpenseListItem>();
        while (await reader.ReadAsync(ct)) items.Add(new(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetGuid(3), reader.GetString(4), reader.GetGuid(5), reader.GetString(6), reader.GetDateTimeOffset(7), reader.GetDateTimeOffset(8), reader.GetDecimal(9), reader.GetDecimal(10), reader.GetDecimal(11), reader.GetString(12), reader.GetString(13), reader.IsDBNull(14) ? null : reader.GetString(14), reader.GetString(15), reader.IsDBNull(16) ? null : reader.GetString(16), reader.IsDBNull(17) ? null : reader.GetDecimal(17), reader.GetBoolean(18)));
        return new(items, page, pageSize, count, gross, held, net);
    }

    public async Task<ExpenseDetail?> GetAsync(ExpenseUserIdentity user, Guid expenseId, CancellationToken ct)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT e.ExpenseId,e.DocumentNumber,e.SupplierDocumentNumber,e.SupplierId,s.Name,
              e.ExpenseConceptId,c.Name,e.IssuedAt,e.DueDate,e.CurrencyCode,e.Description,
              e.TaxExclusiveAmount,e.VatAmount,e.GrossAmount,e.WithholdingAmount,e.NetPayable,
              e.Status,e.PurchaseEvidenceType,e.EvidenceUrl,f.FiscalNumber,f.FiscalStatus,
              p.PayableId,p.Status,p.OriginalAmount,p.OutstandingAmount,
              e.CancellationId,e.CancellationReason,adjustment.FiscalNumber,adjustment.FiscalStatus,
              e.SourceInvoiceId,invoice.DocumentNumber,
              CAST(CASE WHEN returned.AppliedChargeId IS NULL THEN 0 ELSE 1 END AS bit)
            FROM dbo.Expenses e
            JOIN dbo.Businesses b ON b.BusinessId=e.BusinessId AND b.TenantId=@TenantId
            JOIN dbo.Suppliers s ON s.SupplierId=e.SupplierId AND s.BusinessId=e.BusinessId
            JOIN dbo.ExpenseConcepts c ON c.ExpenseConceptId=e.ExpenseConceptId AND c.BusinessId=e.BusinessId
            LEFT JOIN dbo.FiscalDocuments f ON f.DocumentId=e.ExpenseId AND f.BusinessId=e.BusinessId
            LEFT JOIN dbo.Payables p ON p.SourceDocumentId=e.ExpenseId AND p.SourceDocumentType=N'Expense'
              AND p.BusinessId=e.BusinessId
            LEFT JOIN dbo.FiscalDocuments adjustment ON adjustment.DocumentId=e.CancellationId
              AND adjustment.BusinessId=e.BusinessId AND adjustment.FiscalDocumentType=N'SupportDocumentAdjustment'
            LEFT JOIN dbo.SalesDocuments invoice ON invoice.DocumentId=e.SourceInvoiceId
              AND invoice.BusinessId=e.BusinessId
            LEFT JOIN dbo.SalesReturnCharges returned ON returned.AppliedChargeId=e.ExpenseId
            WHERE e.BusinessId=@BusinessId AND e.ExpenseId=@ExpenseId;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@ExpenseId", expenseId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        ExpensePayableView? payable = reader.IsDBNull(21) ? null :
            new(reader.GetGuid(21), reader.GetString(22), reader.GetDecimal(23), reader.GetDecimal(24));
        return new(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetGuid(3), reader.GetString(4), reader.GetGuid(5), reader.GetString(6),
            reader.GetDateTimeOffset(7), reader.GetDateTimeOffset(8), reader.GetString(9),
            reader.GetString(10), reader.GetDecimal(11), reader.GetDecimal(12), reader.GetDecimal(13),
            reader.GetDecimal(14), reader.GetDecimal(15), reader.GetString(16), reader.GetString(17),
            reader.IsDBNull(18) ? null : reader.GetString(18),
            reader.IsDBNull(19) ? null : reader.GetString(19),
            reader.IsDBNull(20) ? null : reader.GetString(20), payable,
            reader.IsDBNull(25) ? null : reader.GetGuid(25),
            reader.IsDBNull(26) ? null : reader.GetString(26),
            reader.IsDBNull(27) ? null : reader.GetString(27),
            reader.IsDBNull(28) ? null : reader.GetString(28),
            reader.IsDBNull(29) ? null : reader.GetGuid(29),
            reader.IsDBNull(30) ? null : reader.GetString(30), reader.GetBoolean(31));
    }

    public async Task<ExpenseAcceptance> AcceptAsync(ExpenseUserIdentity user, string idempotencyKey, ConfirmExpenseRequest request,
        ExpenseAmounts amounts, WithholdingCalculationSnapshot withholding, CancellationToken ct)
    {
        var requestHash = HashRequest(request);
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var replay = await FindReplayAsync(connection, tx, user, idempotencyKey, request, amounts, ct);
            if (replay is not null)
            {
                await tx.CommitAsync(ct);
                return replay;
            }
            Guid accountId; Guid? defaultCenter; string? purchaseEvidencePolicy;
            await using (var validate = new SqlCommand("""
                SELECT c.ExpenseAccountId,c.DefaultCostCenterId,s.PurchaseEvidencePolicy
                FROM dbo.ExpenseConcepts c
                JOIN dbo.Businesses b ON b.BusinessId=c.BusinessId
                JOIN dbo.Suppliers s ON s.SupplierId=@SupplierId
                  AND s.BusinessId=c.BusinessId AND s.IsActive=1
                WHERE c.ExpenseConceptId=@ConceptId AND c.BusinessId=@BusinessId AND b.TenantId=@TenantId AND c.IsActive=1
                  AND (@CenterId IS NULL OR EXISTS(SELECT 1 FROM dbo.AccountingCostCenters cc WHERE cc.CostCenterId=@CenterId AND cc.BusinessId=@BusinessId AND cc.IsActive=1));
                """, connection, tx))
            {
                validate.Parameters.AddWithValue("@ConceptId", request.ConceptId); validate.Parameters.AddWithValue("@BusinessId", user.BusinessId); validate.Parameters.AddWithValue("@TenantId", user.TenantId); validate.Parameters.AddWithValue("@SupplierId", request.SupplierId); validate.Parameters.AddWithValue("@CenterId", (object?)request.CostCenterId ?? DBNull.Value);
                await using var reader = await validate.ExecuteReaderAsync(ct); if (!await reader.ReadAsync(ct)) throw new ExpenseValidationException("Proveedor, concepto o centro de costo no pertenecen a la empresa."); accountId = reader.GetGuid(0); defaultCenter = reader.IsDBNull(1) ? null : reader.GetGuid(1); purchaseEvidencePolicy = reader.IsDBNull(2) ? null : reader.GetString(2);
            }
            if (!PurchaseEvidenceTypes.AllowedFor(purchaseEvidencePolicy).Contains(request.PurchaseEvidenceType))
                throw new ExpenseValidationException("El tipo de documento no está permitido por la política fiscal del proveedor.");
            var now = timeProvider.GetUtcNow();
            var requiresSupport = request.PurchaseEvidenceType == PurchaseEvidenceTypes.BuyerElectronicSupportDocument;
            if (requiresSupport && !await SqlDianDocumentQuota.TryReserveAsync(connection, tx,
                    user.BusinessId, request.ExpenseId, "SupportDocument", now, ct))
                throw new ExpenseValidationException(
                    "No hay cupo de documentos DIAN para generar el documento soporte del gasto.");
            SqlGoodsReceiptStore.SupportFiscalAllocation? support = null;
            if (requiresSupport)
            {
                try
                {
                    support = await SqlGoodsReceiptStore.AllocateSupportFiscalAsync(connection, tx,
                        user.BusinessId, request.SupplierId, request.IssuedAt, now, ct);
                }
                catch (Auraly.Application.Purchasing.PurchasingValidationException error)
                {
                    throw new ExpenseValidationException(error.Message);
                }
            }
            var number = await SqlOperationalDocumentAllocator.AllocateNumberAsync(connection, tx, user.BusinessId, ExpenseDocumentTypes.Expense, now, ct);
            var accountingJobId = ids.NewId(); var center = request.CostCenterId ?? defaultCenter;
            var payload = new ExpenseDocumentPayload(user.TenantId, user.BusinessId, request.ExpenseId, request.SupplierId, request.ConceptId, accountId, center, user.UserId, number.FullNumber, number.SeriesId, number.Prefix, number.SeriesCode, number.Consecutive, request.SupplierDocumentNumber, request.IssuedAt, request.DueDate, request.CurrencyCode, request.Description, amounts.TaxExclusiveAmount, amounts.VatAmount, amounts.GrossAmount, request.EvidenceUrl, withholding, PurchaseEvidenceType: request.PurchaseEvidenceType);
            await PersistAcceptedAsync(connection, tx,
                [new(payload, idempotencyKey, requestHash, accountingJobId)], now, ct);
            if (support is not null)
                await InsertSupportFiscalAsync(connection, tx, payload, support, now, ct);
            await tx.CommitAsync(ct); return new(request.ExpenseId, Guid.Empty, number.FullNumber, "Accepted", 0, false, accountingJobId, support is not null);
        }
        catch (ExpenseConflictException) { await tx.RollbackAsync(CancellationToken.None); throw; }
        catch (SqlException error) when (error.Number is 2601 or 2627) { await tx.RollbackAsync(CancellationToken.None); throw new ExpenseConflictException("El número de factura del proveedor ya fue registrado."); }
        catch { await tx.RollbackAsync(CancellationToken.None); throw; }
    }

    private static ExpenseConceptView ReadConcept(SqlDataReader r) => new(r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.GetString(3), r.GetGuid(4), r.GetString(5), r.GetString(6), r.IsDBNull(7) ? null : r.GetGuid(7), r.IsDBNull(8) ? null : r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9), r.GetBoolean(10));

    private static Task InsertSupportFiscalAsync(SqlConnection connection, SqlTransaction tx,
        ExpenseDocumentPayload expense, SqlGoodsReceiptStore.SupportFiscalAllocation support,
        DateTimeOffset now, CancellationToken ct) =>
        InsertSupportFiscalBatchAsync(connection, tx, [(expense, support)], now, ct);

    private static async Task InsertSupportFiscalBatchAsync(SqlConnection connection, SqlTransaction tx,
        IReadOnlyList<(ExpenseDocumentPayload Expense, SqlGoodsReceiptStore.SupportFiscalAllocation Support)> documents,
        DateTimeOffset now, CancellationToken ct)
    {
        if (documents.Count is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(documents));
        var rows = documents.Select(item => {
            var expense = item.Expense; var support = item.Support;
            var snapshot = new PurchaseSupportFiscalSnapshot(null,
                support.IssuerConfigurationId, support.FiscalNumber, support.Environment,
                support.QrValidationUrl, support.Seller, support.Authorization,
                [new PurchaseSupportLineMetadata(1, $"GASTO-{expense.ConceptId:N}", "999", "EA", "IVA", "01")],
                Expense: expense, SellerPostalZone: support.SellerPostalZone);
            return new { DocumentId = expense.ExpenseId, expense.BusinessId, expense.DocumentNumber,
                expense.IssuedAt, support.FiscalNumber, support.IssuerConfigurationId, support.Environment,
                SnapshotJson = PurchaseSupportFiscalSnapshotSerializer.Serialize(snapshot) };
        }).ToArray();
        await using var command = new SqlCommand("""
            SET NOCOUNT ON;
            DECLARE @Documents TABLE(DocumentId uniqueidentifier PRIMARY KEY,BusinessId uniqueidentifier,
              DocumentNumber nvarchar(64),IssuedAt datetimeoffset,FiscalNumber nvarchar(64),
              IssuerId uniqueidentifier,Environment tinyint,SnapshotJson nvarchar(max));
            INSERT @Documents SELECT DocumentId,BusinessId,DocumentNumber,IssuedAt,FiscalNumber,
              IssuerConfigurationId,Environment,SnapshotJson FROM OPENJSON(@DocumentsJson) WITH(
              DocumentId uniqueidentifier,BusinessId uniqueidentifier,DocumentNumber nvarchar(64),
              IssuedAt datetimeoffset,FiscalNumber nvarchar(64),IssuerConfigurationId uniqueidentifier,
              Environment tinyint,SnapshotJson nvarchar(max));
            INSERT dbo.FiscalDocuments(DocumentId,BusinessId,SourceDocumentType,FiscalDocumentType,
              AuralyDocumentNumber,FiscalNumber,UniqueCodeType,UniqueCode,IssuedAt,FiscalStatus,CreatedAt,UpdatedAt)
            SELECT DocumentId,BusinessId,N'Expense',N'SupportDocument',DocumentNumber,FiscalNumber,
              N'CUDS',NULL,IssuedAt,@Status,@Now,@Now FROM @Documents;
            INSERT fiscal.PurchaseSupportFiscalSnapshots(DocumentId,SnapshotJson,Environment,CreatedAt)
            SELECT DocumentId,SnapshotJson,Environment,@Now FROM @Documents;
            INSERT dbo.FiscalDocumentProcesses(DocumentId,BusinessId,FiscalIssuerConfigurationId,Status,
              AttemptCount,NextAttemptAt,CreatedAt,UpdatedAt)
            SELECT DocumentId,BusinessId,IssuerId,@Status,0,@Now,@Now,@Now FROM @Documents;
            """, connection, tx);
        command.Parameters.Add("@DocumentsJson", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(rows);
        command.Parameters.AddWithValue("@Status", FiscalDocumentStatusCodes.PendingGeneration);
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(ct);
    }

}
