using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Expenses;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Commerce.Taxation.Contracts;
using Auraly.Contracts.Expenses;
using Auraly.Domain.Expenses;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlExpenseStore(SqlServerConnectionFactory connections, IAuralyIdGenerator ids,
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
        return new(concepts, suppliers, accounts, centers);
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
                UPDATE dbo.ExpenseConcepts WITH(UPDLOCK,HOLDLOCK) SET Code=@Code,Name=@Name,ExpenseAccountId=@AccountId,
                  DefaultCostCenterId=@CenterId,WithholdingConceptCode=@WithholdingCode,IsActive=@Active,UpdatedAt=@Now
                  WHERE ExpenseConceptId=@Id AND BusinessId=@BusinessId;
                IF @@ROWCOUNT=0 INSERT dbo.ExpenseConcepts(ExpenseConceptId,BusinessId,Code,Name,ExpenseAccountId,
                  DefaultCostCenterId,WithholdingConceptCode,IsActive,CreatedAt,UpdatedAt)
                  VALUES(@Id,@BusinessId,@Code,@Name,@AccountId,@CenterId,@WithholdingCode,@Active,@Now,@Now);
                """, connection, tx);
            command.Parameters.AddWithValue("@Id", request.ConceptId); command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            command.Parameters.AddWithValue("@TenantId", user.TenantId); command.Parameters.AddWithValue("@Code", request.Code);
            command.Parameters.AddWithValue("@Name", request.Name); command.Parameters.AddWithValue("@AccountId", request.ExpenseAccountId);
            command.Parameters.AddWithValue("@CenterId", (object?)request.DefaultCostCenterId ?? DBNull.Value);
            command.Parameters.AddWithValue("@WithholdingCode", (object?)request.WithholdingConceptCode ?? DBNull.Value);
            command.Parameters.AddWithValue("@Active", request.IsActive); command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
            await command.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct);
        }
        catch (SqlException error) when (error.Number is >= 51600 and <= 51602) { await tx.RollbackAsync(CancellationToken.None); throw new ExpenseValidationException(error.Message); }
        catch (SqlException error) when (error.Number is 2601 or 2627) { await tx.RollbackAsync(CancellationToken.None); throw new ExpenseConflictException("Ya existe un concepto con ese código."); }
        catch { await tx.RollbackAsync(CancellationToken.None); throw; }
        return (await ListConceptsAsync(user, true, ct)).Single(x => x.ConceptId == request.ConceptId);
    }

    public async Task<ExpensePage> ListAsync(ExpenseUserIdentity user, int page, int pageSize, string? search, Guid? conceptId,
        Guid? supplierId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        const string filter = """e.BusinessId=@BusinessId AND (@Search IS NULL OR e.DocumentNumber LIKE N'%'+@Search+N'%' OR e.SupplierDocumentNumber LIKE N'%'+@Search+N'%' OR s.Name LIKE N'%'+@Search+N'%') AND (@ConceptId IS NULL OR e.ExpenseConceptId=@ConceptId) AND (@SupplierId IS NULL OR e.SupplierId=@SupplierId) AND (@From IS NULL OR CONVERT(date,e.IssuedAt)>=@From) AND (@To IS NULL OR CONVERT(date,e.IssuedAt)<=@To)""";
        await using var command = new SqlCommand($"""
            SELECT COUNT(*),COALESCE(SUM(e.GrossAmount),0),COALESCE(SUM(e.WithholdingAmount),0),COALESCE(SUM(e.NetPayable),0)
              FROM dbo.Expenses e JOIN dbo.Suppliers s ON s.SupplierId=e.SupplierId WHERE {filter};
            SELECT e.ExpenseId,e.DocumentNumber,e.SupplierDocumentNumber,e.SupplierId,s.Name,e.ExpenseConceptId,c.Name,
              e.IssuedAt,e.DueDate,e.GrossAmount,e.WithholdingAmount,e.NetPayable,e.CurrencyCode,e.Status,e.EvidenceUrl
              FROM dbo.Expenses e JOIN dbo.Suppliers s ON s.SupplierId=e.SupplierId JOIN dbo.ExpenseConcepts c ON c.ExpenseConceptId=e.ExpenseConceptId
              WHERE {filter} ORDER BY e.IssuedAt DESC,e.ExpenseId OFFSET @Offset ROWS FETCH NEXT @Size ROWS ONLY;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId); command.Parameters.AddWithValue("@Search", (object?)search ?? DBNull.Value);
        command.Parameters.AddWithValue("@ConceptId", (object?)conceptId ?? DBNull.Value); command.Parameters.AddWithValue("@SupplierId", (object?)supplierId ?? DBNull.Value);
        command.Parameters.AddWithValue("@From", from is null ? DBNull.Value : from.Value.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@To", to is null ? DBNull.Value : to.Value.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@Offset", (page - 1) * pageSize); command.Parameters.AddWithValue("@Size", pageSize);
        await using var reader = await command.ExecuteReaderAsync(ct); await reader.ReadAsync(ct);
        var count = reader.GetInt32(0); var gross = reader.GetDecimal(1); var held = reader.GetDecimal(2); var net = reader.GetDecimal(3);
        await reader.NextResultAsync(ct); var items = new List<ExpenseListItem>();
        while (await reader.ReadAsync(ct)) items.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetGuid(3), reader.GetString(4), reader.GetGuid(5), reader.GetString(6), reader.GetDateTimeOffset(7), reader.GetDateTimeOffset(8), reader.GetDecimal(9), reader.GetDecimal(10), reader.GetDecimal(11), reader.GetString(12), reader.GetString(13), reader.IsDBNull(14) ? null : reader.GetString(14)));
        return new(items, page, pageSize, count, gross, held, net);
    }

    public async Task<ExpenseAcceptance> AcceptAsync(ExpenseUserIdentity user, string idempotencyKey, ConfirmExpenseRequest request,
        ExpenseAmounts amounts, WithholdingCalculationSnapshot withholding, CancellationToken ct)
    {
        var requestHash = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { request, amounts, withholding })));
        await using var connection = connections.Create(); await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await using (var replay = new SqlCommand("""
                SELECT e.ExpenseId,e.DocumentNumber,e.Status,e.RequestHash,j.ProcessingSequence,j.JobId
                FROM dbo.Expenses e WITH(UPDLOCK,HOLDLOCK) JOIN dbo.DocumentProcessingJobs j ON j.DocumentId=e.ExpenseId AND j.DocumentType=N'Expense'
                WHERE e.BusinessId=@BusinessId AND (e.ExpenseId=@Id OR e.IdempotencyKey=@Key);
                """, connection, tx))
            {
                replay.Parameters.AddWithValue("@BusinessId", user.BusinessId); replay.Parameters.AddWithValue("@Id", request.ExpenseId); replay.Parameters.AddWithValue("@Key", idempotencyKey);
                await using var reader = await replay.ExecuteReaderAsync(ct); if (await reader.ReadAsync(ct))
                {
                    if (!reader.GetFieldValue<byte[]>(3).AsSpan().SequenceEqual(requestHash)) throw new ExpenseConflictException("La clave de idempotencia se reutilizó con otros datos.");
                    var value = new ExpenseAcceptance(reader.GetGuid(0), reader.GetGuid(5), reader.GetString(1), reader.GetString(2), reader.GetInt64(4), true); await reader.DisposeAsync(); await tx.CommitAsync(ct); return value;
                }
            }
            Guid accountId; Guid? defaultCenter;
            await using (var validate = new SqlCommand("""
                SELECT c.ExpenseAccountId,c.DefaultCostCenterId FROM dbo.ExpenseConcepts c
                JOIN dbo.Businesses b ON b.BusinessId=c.BusinessId
                WHERE c.ExpenseConceptId=@ConceptId AND c.BusinessId=@BusinessId AND b.TenantId=@TenantId AND c.IsActive=1
                  AND EXISTS(SELECT 1 FROM dbo.Suppliers s WHERE s.SupplierId=@SupplierId AND s.BusinessId=@BusinessId AND s.IsActive=1)
                  AND (@CenterId IS NULL OR EXISTS(SELECT 1 FROM dbo.AccountingCostCenters cc WHERE cc.CostCenterId=@CenterId AND cc.BusinessId=@BusinessId AND cc.IsActive=1));
                """, connection, tx))
            {
                validate.Parameters.AddWithValue("@ConceptId", request.ConceptId); validate.Parameters.AddWithValue("@BusinessId", user.BusinessId); validate.Parameters.AddWithValue("@TenantId", user.TenantId); validate.Parameters.AddWithValue("@SupplierId", request.SupplierId); validate.Parameters.AddWithValue("@CenterId", (object?)request.CostCenterId ?? DBNull.Value);
                await using var reader = await validate.ExecuteReaderAsync(ct); if (!await reader.ReadAsync(ct)) throw new ExpenseValidationException("Proveedor, concepto o centro de costo no pertenecen a la empresa."); accountId = reader.GetGuid(0); defaultCenter = reader.IsDBNull(1) ? null : reader.GetGuid(1);
            }
            var now = timeProvider.GetUtcNow(); var number = await SqlOperationalDocumentAllocator.AllocateNumberAsync(connection, tx, user.BusinessId, ExpenseDocumentTypes.Expense, now, ct);
            var sequence = await SqlOperationalDocumentAllocator.AllocateSequenceAsync(connection, tx, user.BusinessId, now, ct); var movementId = ids.NewId(); var center = request.CostCenterId ?? defaultCenter;
            var payload = new ExpenseDocumentPayload(user.TenantId, user.BusinessId, request.ExpenseId, request.SupplierId, request.ConceptId, accountId, center, user.UserId, number.FullNumber, number.SeriesId, number.Prefix, number.SeriesCode, number.Consecutive, request.SupplierDocumentNumber, request.IssuedAt, request.DueDate, request.CurrencyCode, request.Description, amounts.TaxExclusiveAmount, amounts.VatAmount, amounts.GrossAmount, request.EvidenceUrl, withholding);
            var json = ExpenseContractSerializer.Serialize(payload); var payloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
            await using var insert = new SqlCommand("""
                INSERT dbo.Expenses(ExpenseId,BusinessId,SupplierId,ExpenseConceptId,CostCenterId,DocumentSeriesId,DocumentNumber,DocumentPrefix,DocumentSeriesCode,DocumentConsecutive,SupplierDocumentNumber,IssuedAt,DueDate,CurrencyCode,Description,TaxExclusiveAmount,VatAmount,GrossAmount,WithholdingAmount,NetPayable,EvidenceUrl,Status,ConfirmedByUserId,IdempotencyKey,RequestHash,AcceptedAt)
                VALUES(@Id,@BusinessId,@SupplierId,@ConceptId,@CenterId,@SeriesId,@Number,@Prefix,@SeriesCode,@Consecutive,@SupplierNumber,@IssuedAt,@DueDate,@Currency,@Description,@Net,@Vat,@Gross,@Held,@Payable,@Evidence,N'Accepted',@UserId,@Key,@RequestHash,@Now);
                INSERT dbo.DocumentProcessingJobs(JobId,BusinessId,ProcessingSequence,DocumentId,DocumentType,Status,AvailableAt,CreatedAt)
                VALUES(@JobId,@BusinessId,@Sequence,@Id,N'Expense',N'Pending',@Now,@Now);
                INSERT dbo.DocumentProcessingPayloads(DocumentId,DocumentType,BusinessId,ContractVersion,PayloadJson,PayloadHash,AcceptedAt)
                VALUES(@Id,N'Expense',@BusinessId,1,@Payload,@PayloadHash,@Now);
                """, connection, tx);
            insert.Parameters.AddWithValue("@Id", request.ExpenseId); insert.Parameters.AddWithValue("@BusinessId", user.BusinessId); insert.Parameters.AddWithValue("@SupplierId", request.SupplierId); insert.Parameters.AddWithValue("@ConceptId", request.ConceptId); insert.Parameters.AddWithValue("@CenterId", (object?)center ?? DBNull.Value); insert.Parameters.AddWithValue("@SeriesId", number.SeriesId); insert.Parameters.AddWithValue("@Number", number.FullNumber); insert.Parameters.AddWithValue("@Prefix", number.Prefix); insert.Parameters.AddWithValue("@SeriesCode", number.SeriesCode); insert.Parameters.AddWithValue("@Consecutive", number.Consecutive); insert.Parameters.AddWithValue("@SupplierNumber", request.SupplierDocumentNumber); insert.Parameters.AddWithValue("@IssuedAt", request.IssuedAt); insert.Parameters.AddWithValue("@DueDate", request.DueDate); insert.Parameters.AddWithValue("@Currency", request.CurrencyCode); insert.Parameters.AddWithValue("@Description", request.Description); Money(insert, "@Net", amounts.TaxExclusiveAmount); Money(insert, "@Vat", amounts.VatAmount); Money(insert, "@Gross", amounts.GrossAmount); Money(insert, "@Held", withholding.WithholdingTotal); Money(insert, "@Payable", withholding.NetAmount); insert.Parameters.AddWithValue("@Evidence", (object?)request.EvidenceUrl ?? DBNull.Value); insert.Parameters.AddWithValue("@UserId", user.UserId); insert.Parameters.AddWithValue("@Key", idempotencyKey); insert.Parameters.Add("@RequestHash", SqlDbType.Binary, 32).Value = requestHash; insert.Parameters.AddWithValue("@Now", now); insert.Parameters.AddWithValue("@JobId", movementId); insert.Parameters.AddWithValue("@Sequence", sequence); insert.Parameters.AddWithValue("@Payload", json); insert.Parameters.Add("@PayloadHash", SqlDbType.Binary, 32).Value = payloadHash;
            await insert.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct); return new(request.ExpenseId, movementId, number.FullNumber, "Accepted", sequence, false);
        }
        catch (ExpenseConflictException) { await tx.RollbackAsync(CancellationToken.None); throw; }
        catch (SqlException error) when (error.Number is 2601 or 2627) { await tx.RollbackAsync(CancellationToken.None); throw new ExpenseConflictException("El número de factura del proveedor ya fue registrado."); }
        catch { await tx.RollbackAsync(CancellationToken.None); throw; }
    }

    private static ExpenseConceptView ReadConcept(SqlDataReader r) => new(r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.GetString(3), r.GetGuid(4), r.GetString(5), r.GetString(6), r.IsDBNull(7) ? null : r.GetGuid(7), r.IsDBNull(8) ? null : r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9), r.GetBoolean(10));
    private static void Money(SqlCommand c, string name, decimal value) { var p = c.Parameters.Add(name, SqlDbType.Decimal); p.Precision = 19; p.Scale = 4; p.Value = value; }
}
