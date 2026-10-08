using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.WorkSessions;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlWorkSessionStore
{
    public async Task<WorkSessionClosurePage> ListClosuresAsync(
        WorkSessionIdentity identity, DateOnly from, DateOnly to, string? status,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        const string filter = """
            FROM dbo.WorkSessionClosures closure
            INNER JOIN dbo.WorkSessions session ON session.WorkSessionId=closure.WorkSessionId
            INNER JOIN dbo.Businesses business ON business.BusinessId=session.BusinessId
            LEFT JOIN dbo.Warehouses warehouse ON warehouse.WarehouseId=session.WarehouseId
            INNER JOIN dbo.AppUsers cashier ON cashier.UserId=session.UserId
            WHERE business.TenantId=@TenantId AND closure.ClosedAt>=@From AND closure.ClosedAt<@Until
              AND (@Status IS NULL OR closure.ReconciliationStatus=@Status)
            """;
        int total;
        await using (var count = new SqlCommand("SELECT COUNT(*) " + filter, connection))
        {
            AddClosureSearchParameters(count, identity, from, to, status);
            total = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken));
        }
        await using var command = new SqlCommand("""
            SELECT closure.WorkSessionClosureId,closure.WorkSessionId,session.BusinessId,business.Name,
              session.WarehouseId,warehouse.Name,session.UserId,
              LTRIM(RTRIM(CONCAT(cashier.FirstName,N' ',cashier.LastName))),session.OpenedAt,closure.ClosedAt,
              closure.SalesCount,closure.CreditSalesCount,closure.ReturnCount,closure.TotalSales,
              closure.TotalRefunds,closure.NetAmount,closure.ReconciliationStatus,
              COALESCE((SELECT TOP(1) job.Status FROM dbo.AccountingPostingJobs job
                WHERE job.SourceDocumentId IN(closure.WorkSessionClosureId,
                  (SELECT reconciliation.ReconciliationId FROM dbo.WorkSessionClosureReconciliations reconciliation
                   WHERE reconciliation.WorkSessionClosureId=closure.WorkSessionClosureId))
                ORDER BY job.CreatedAt DESC),N'AccountingDisabled'),closure.ExpectedCash
            """ + "\n" + filter + """
            ORDER BY closure.ClosedAt DESC,closure.WorkSessionClosureId DESC
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            """, connection);
        AddClosureSearchParameters(command, identity, from, to, status);
        command.Parameters.AddWithValue("@Skip", (page - 1) * pageSize);
        command.Parameters.AddWithValue("@Take", pageSize);
        var items = new List<WorkSessionClosureListItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new WorkSessionClosureListItem(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetGuid(6), reader.GetString(7),
                reader.GetDateTimeOffset(8), reader.GetDateTimeOffset(9), reader.GetInt64(10),
                reader.GetInt32(11), reader.GetInt64(12), reader.GetDecimal(13), reader.GetDecimal(14),
                reader.GetDecimal(15), reader.GetString(16), reader.GetString(17), [],
                reader.GetDecimal(18)));
        await reader.CloseAsync();
        var totalsByClosure = await ReadClosurePaymentTotalsAsync(
            connection, items.Select(item => item.WorkSessionClosureId).ToArray(), cancellationToken);
        for (var index = 0; index < items.Count; index++)
            items[index] = items[index] with
            {
                PaymentTotals = totalsByClosure.GetValueOrDefault(items[index].WorkSessionClosureId) ?? []
            };
        return new(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<WorkSessionPaymentVerificationItem>> ListClosurePaymentVerificationsAsync(
        WorkSessionIdentity identity, Guid closureId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await EnsureClosureScopeAsync(connection, null, identity.TenantId, closureId, cancellationToken);
        return await ReadPaymentVerificationsAsync(connection, null, closureId, cancellationToken);
    }

    public async Task<WorkSessionPaymentVerificationPage> ListClosurePaymentVerificationPageAsync(
        WorkSessionIdentity identity, Guid closureId, string? paymentMethodCode,
        string? movementType, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await EnsureClosureScopeAsync(connection, null, identity.TenantId, closureId, cancellationToken);
        await using var command = new SqlCommand(PaymentVerificationRowsSql + """
            SELECT PaymentMethodCode,MovementType,COUNT(1),SUM(Amount),
              SUM(CASE WHEN Status IN(N'Verified',N'Missing') THEN 1 ELSE 0 END),
              SUM(CASE WHEN Status=N'Verified' AND
                (CorrectedPaymentMethodCode IS NULL OR CorrectedPaymentMethodCode=PaymentMethodCode)
                THEN ABS(COALESCE(CorrectedAmount,Amount)) ELSE CONVERT(decimal(19,4),0) END)
            FROM #PaymentVerifications GROUP BY PaymentMethodCode,MovementType;
            SELECT COUNT(1) FROM #PaymentVerifications;
            SELECT VerificationKey,PaymentMethodCode,MovementType,SourceId,DocumentNumber,
              SourceNumber,Amount,Reference,CardFranchiseCode,ApprovalNumber,OccurredAt,
              SourceDocumentType,CustomerName,Status,ReasonName,Notes,
              CorrectedPaymentMethodCode,CorrectedAmount,CorrectionReason,
              TenderMethodCode,CorrectedTenderMethodCode,CorrectedCardFranchiseCode,
              CorrectedApprovalNumber,CorrectedReference,CounterpartyName
            FROM #PaymentVerifications
            ORDER BY SortOrder,OccurredAt,VerificationKey
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            """, connection);
        command.Parameters.AddWithValue("@ClosureId", closureId);
        command.Parameters.AddWithValue("@Method", (object?)paymentMethodCode ?? DBNull.Value);
        command.Parameters.AddWithValue("@Movement", (object?)movementType ?? DBNull.Value);
        command.Parameters.AddWithValue("@Skip", (page - 1) * pageSize);
        command.Parameters.AddWithValue("@Take", pageSize);
        var groups = new List<WorkSessionPaymentVerificationGroup>();
        var items = new List<WorkSessionPaymentVerificationItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            groups.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2),
                reader.GetDecimal(3), reader.GetInt32(4), reader.GetDecimal(5)));
        await reader.NextResultAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var total = reader.GetInt32(0);
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(ReadPaymentVerificationItem(reader));
        return new(items, groups, page, pageSize, total);
    }

    public async Task<WorkSessionClosureReconciliationView> ReconcileClosureAsync(
        WorkSessionIdentity identity, Guid closureId, string idempotencyKey,
        ReconcileWorkSessionClosureRequest request, CancellationToken cancellationToken)
    {
        var requestHash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, Json))));
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            Guid businessId;
            decimal expectedCash;
            await using (var scope = new SqlCommand("""
                SELECT session.BusinessId,closure.ExpectedCash
                FROM dbo.WorkSessionClosures closure WITH(UPDLOCK,HOLDLOCK)
                INNER JOIN dbo.WorkSessions session ON session.WorkSessionId=closure.WorkSessionId
                INNER JOIN dbo.Businesses business ON business.BusinessId=session.BusinessId
                WHERE closure.WorkSessionClosureId=@ClosureId AND business.TenantId=@TenantId;
                """, connection, transaction))
            {
                scope.Parameters.AddWithValue("@ClosureId", closureId);
                scope.Parameters.AddWithValue("@TenantId", identity.TenantId);
                await using var reader = await scope.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    throw new WorkSessionNotFoundException("El cierre no existe en la empresa autenticada.");
                businessId = reader.GetGuid(0);
                expectedCash = reader.GetDecimal(1);
            }
            var existing = await ReadAcceptedReconciliationAsync(connection, transaction,
                identity, closureId, businessId, idempotencyKey, requestHash, cancellationToken);
            if (existing is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return existing;
            }
            var expected = await ReadClosurePaymentTotalsAsync(connection, closureId, cancellationToken, transaction);
            var countable = expected.Where(value => value.RequiresCount).ToArray();
            var lines = request.Lines.ToDictionary(value => value.PaymentMethodCode.Trim(), StringComparer.OrdinalIgnoreCase);
            if (lines.Count != request.Lines.Count || countable.Any(value => !lines.ContainsKey(value.PaymentMethodCode)) ||
                lines.Keys.Any(code => countable.All(value => !value.PaymentMethodCode.Equals(code, StringComparison.OrdinalIgnoreCase))))
                throw new WorkSessionValidationException("La conciliación debe incluir cada medio contado exactamente una vez.");
            var requestedVerifications = request.PaymentVerifications ?? [];
            var verification = await ValidatePaymentVerificationsAsync(
                connection, transaction, closureId, businessId, requestedVerifications,
                request.PaymentCorrections ?? [], cancellationToken);
            if (verification.Corrections.Any(item =>
                countable.All(method => !method.PaymentMethodCode.Equals(item.PaymentMethodCode, StringComparison.OrdinalIgnoreCase))) ||
                verification.Corrections.Count > 0 && request.Reclassifications.Count > 0)
                throw new WorkSessionValidationException("Corrige un comprobante por medio; no combines correcciones con cruces generales.");
            foreach (var method in countable.Select(item => item.PaymentMethodCode))
            {
                var verified = verification.VerifiedAmounts.GetValueOrDefault(method);
                if (!lines.TryGetValue(method, out var line) ||
                    (!method.Equals("Cash", StringComparison.OrdinalIgnoreCase) &&
                     decimal.Round(line.VerifiedAmount, 4) != decimal.Round(verified, 4)))
                    throw new WorkSessionValidationException(
                        "El valor verificado debe corresponder a los comprobantes y movimientos confirmados.");
            }
            var differences = countable.ToDictionary(value => value.PaymentMethodCode,
                value => decimal.Round(lines[value.PaymentMethodCode].VerifiedAmount-
                    (value.PaymentMethodCode.Equals("Cash", StringComparison.OrdinalIgnoreCase)
                        ? expectedCash : value.NetAmount),4),
                StringComparer.OrdinalIgnoreCase);
            foreach (var correction in verification.Corrections)
            {
                differences[correction.OriginalPaymentMethodCode] += correction.OriginalAmount;
                differences[correction.PaymentMethodCode] -= correction.Amount;
            }
            foreach (var correction in request.Reclassifications)
            {
                if (correction.Amount <= 0 || correction.FromPaymentMethodCode.Equals(correction.ToPaymentMethodCode, StringComparison.OrdinalIgnoreCase) ||
                    !differences.TryGetValue(correction.FromPaymentMethodCode, out var sourceDifference) ||
                    !differences.TryGetValue(correction.ToPaymentMethodCode, out var targetDifference) ||
                    sourceDifference >= 0 || targetDifference <= 0 || correction.Amount > -sourceDifference || correction.Amount > targetDifference)
                    throw new WorkSessionValidationException("La reclasificación debe mover un faltante real hacia un sobrante real sin excederlos.");
                differences[correction.FromPaymentMethodCode] += correction.Amount;
                differences[correction.ToPaymentMethodCode] -= correction.Amount;
            }
            var status = differences.Values.All(value => value == 0) && !verification.HasMissingCreditSale
                ? "Reconciled" : "ReconciledWithDifferences";
            var reconciliationId = ids.NewId();
            var reconciledAt = timeProvider.GetUtcNow();
            var normalizedLines = countable.Select(value =>
            {
                var input = lines[value.PaymentMethodCode];
                if (!input.IsConfirmed)
                    throw new WorkSessionValidationException("Debe confirmar cada medio de pago antes de conciliar el cierre.");
                var reason = string.IsNullOrWhiteSpace(input.ReasonCode) ? null : input.ReasonCode.Trim();
                if (differences[value.PaymentMethodCode] != 0 && reason is null)
                    throw new WorkSessionValidationException("Toda diferencia residual necesita un motivo.");
                return input with { PaymentMethodCode=value.PaymentMethodCode, ReasonCode=reason };
            }).ToArray();
            var categories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await using (var lookup = new SqlCommand("""
                SELECT requested.PaymentMethodCode,mapping.Category,
                  CAST(CASE WHEN requested.ReasonCode IS NULL OR reason.OptionId IS NOT NULL
                    THEN 1 ELSE 0 END AS bit)
                FROM OPENJSON(@Lines) WITH(
                  PaymentMethodCode nvarchar(32),ReasonCode nvarchar(40)) requested
                LEFT JOIN reference.Options reason ON reason.CatalogCode=N'cash-reconciliation-reason'
                  AND reason.Code=requested.ReasonCode AND reason.IsActive=1
                LEFT JOIN dbo.AccountingConfigurationProfiles profile
                  ON profile.IsDefault=1 AND profile.IsActive=1
                LEFT JOIN dbo.AccountingSourceCategoryMappings mapping
                  ON mapping.ProfileCode=profile.ProfileCode
                  AND mapping.SourceType=N'ClosurePaymentMethod'
                  AND mapping.SourceCode=requested.PaymentMethodCode;
                """, connection, transaction))
            {
                lookup.Parameters.AddWithValue("@Lines", JsonSerializer.Serialize(normalizedLines));
                await using var reader = await lookup.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (!reader.GetBoolean(2))
                        throw new WorkSessionValidationException("El motivo de conciliación no pertenece al catálogo vigente.");
                    if (reader.IsDBNull(1) || !categories.TryAdd(reader.GetString(0), reader.GetString(1)))
                        throw new WorkSessionValidationException("Falta la configuración contable del medio de pago.");
                }
            }
            if (categories.Count != normalizedLines.Length)
                throw new WorkSessionValidationException("Falta la configuración contable del medio de pago.");
            var snapshotObject = new
            {
                reconciliationId, closureId, status, reconciledAt, lines=normalizedLines,
                paymentVerifications=requestedVerifications,
                paymentCorrections=verification.Corrections,
                reclassifications=request.Reclassifications, note=request.Note, requestHash
            };
            var snapshot = JsonSerializer.Serialize(snapshotObject, Json);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(snapshot));
            await using (var insert = new SqlCommand("""
                INSERT dbo.WorkSessionClosureReconciliations
                  (ReconciliationId,WorkSessionClosureId,ReconciledByUserId,IdempotencyKey,Status,Note,SnapshotJson,SnapshotHash,ReconciledAt)
                VALUES(@Id,@ClosureId,@UserId,@Key,@Status,@Note,@Snapshot,@Hash,@At);
                UPDATE dbo.WorkSessionClosures SET ReconciliationStatus=@Status WHERE WorkSessionClosureId=@ClosureId;
                """, connection, transaction))
            {
                insert.Parameters.AddWithValue("@Id", reconciliationId); insert.Parameters.AddWithValue("@ClosureId", closureId);
                insert.Parameters.AddWithValue("@UserId", identity.UserId); insert.Parameters.AddWithValue("@Key", idempotencyKey);
                insert.Parameters.AddWithValue("@Status", status); insert.Parameters.AddWithValue("@Note", (object?)request.Note ?? DBNull.Value);
                insert.Parameters.AddWithValue("@Snapshot", snapshot); insert.Parameters.Add("@Hash",SqlDbType.Binary,32).Value=hash;
                insert.Parameters.AddWithValue("@At", reconciledAt); await insert.ExecuteNonQueryAsync(cancellationToken);
            }
            var lineRows = normalizedLines.Select(line =>
            {
                var total = countable.Single(value => value.PaymentMethodCode.Equals(
                    line.PaymentMethodCode,StringComparison.OrdinalIgnoreCase));
                return new
                {
                    line.PaymentMethodCode, ExpectedAmount=line.PaymentMethodCode.Equals("Cash", StringComparison.OrdinalIgnoreCase)
                        ? expectedCash : total.NetAmount,
                    CountedAmount=total.CountedAmount, line.VerifiedAmount,
                    Difference=line.VerifiedAmount-(line.PaymentMethodCode.Equals("Cash", StringComparison.OrdinalIgnoreCase)
                        ? expectedCash : total.NetAmount),
                    line.IsConfirmed, line.ReasonCode
                };
            }).ToArray();
            var reclassificationRows = request.Reclassifications.Select((item,index) => new
            {
                ReclassificationId=ids.NewId(), LineNumber=index+1,
                item.FromPaymentMethodCode,item.ToPaymentMethodCode,item.Amount
            }).ToArray();
            await using (var insertDetails = new SqlCommand("""
                INSERT dbo.WorkSessionClosureReconciliationLines
                  (ReconciliationId,PaymentMethodCode,ExpectedAmount,CountedAmount,
                   VerifiedAmount,Difference,IsConfirmed,ReasonCode)
                SELECT @Id,line.PaymentMethodCode,line.ExpectedAmount,line.CountedAmount,
                  line.VerifiedAmount,line.Difference,line.IsConfirmed,line.ReasonCode
                FROM OPENJSON(@Lines) WITH(
                  PaymentMethodCode nvarchar(32),ExpectedAmount decimal(19,4),
                  CountedAmount decimal(19,4),VerifiedAmount decimal(19,4),
                  Difference decimal(19,4),IsConfirmed bit,ReasonCode nvarchar(40)) line;
                INSERT dbo.WorkSessionClosureReclassifications
                  (ReclassificationId,ReconciliationId,LineNumber,FromPaymentMethodCode,
                   ToPaymentMethodCode,Amount)
                SELECT correction.ReclassificationId,@Id,correction.LineNumber,
                  correction.FromPaymentMethodCode,correction.ToPaymentMethodCode,correction.Amount
                FROM OPENJSON(@Reclassifications) WITH(
                  ReclassificationId uniqueidentifier,LineNumber int,
                  FromPaymentMethodCode nvarchar(32),ToPaymentMethodCode nvarchar(32),
                  Amount decimal(19,4)) correction;
                """, connection, transaction))
            {
                insertDetails.Parameters.AddWithValue("@Id", reconciliationId);
                insertDetails.Parameters.AddWithValue("@Lines", JsonSerializer.Serialize(lineRows));
                insertDetails.Parameters.AddWithValue("@Reclassifications",
                    JsonSerializer.Serialize(reclassificationRows));
                await insertDetails.ExecuteNonQueryAsync(cancellationToken);
            }
            var accountingLines = normalizedLines.Select(line =>
            {
                var total=countable.Single(value=>value.PaymentMethodCode.Equals(line.PaymentMethodCode,StringComparison.OrdinalIgnoreCase));
                return new WorkSessionClosureReconciliationAccountingLine(line.PaymentMethodCode,
                    categories[line.PaymentMethodCode],
                    line.PaymentMethodCode.Equals("Cash", StringComparison.OrdinalIgnoreCase)
                        ? expectedCash : total.NetAmount,
                    total.CountedAmount ?? (line.PaymentMethodCode.Equals("Cash", StringComparison.OrdinalIgnoreCase)
                        ? expectedCash : total.NetAmount),line.VerifiedAmount,
                    line.VerifiedAmount-(line.PaymentMethodCode.Equals("Cash", StringComparison.OrdinalIgnoreCase)
                        ? expectedCash : total.NetAmount),line.ReasonCode);
            }).ToArray();
            var payload = new WorkSessionClosureReconciliationPayload(reconciliationId,closureId,identity.TenantId,businessId,
                identity.UserId,reconciledAt,accountingLines,request.Reclassifications,verification.Corrections);
            var accountingRequired = accountingLines.Any(line => line.VerifiedAmount != line.CountedAmount)
                || differences.Values.Any(value => value != 0) ||
                   verification.Corrections.Any(item => item.Amount != item.OriginalAmount ||
                       item.OriginalTenderCategory != item.TenderCategory);
            var accountingQueued = accountingRequired && await InsertReconciliationAccountingJobAsync(
                connection, transaction, payload, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(reconciliationId,closureId,businessId,status,reconciledAt,identity.UserId,normalizedLines,
                request.Reclassifications,request.Note,accountingQueued ? "Pending" : "NotRequired");
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new WorkSessionConflictException("El cierre ya fue conciliado o la operación ya fue recibida.");
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    private async Task<bool> InsertReconciliationAccountingJobAsync(SqlConnection connection, SqlTransaction transaction,
        WorkSessionClosureReconciliationPayload payload, CancellationToken cancellationToken)
    {
        var json=JsonSerializer.Serialize(payload,Json); var hash=SHA256.HashData(Encoding.UTF8.GetBytes(json));
        await using var command=new SqlCommand("""
            IF EXISTS(SELECT 1 FROM dbo.AccountingTenantSettings WHERE TenantId=@TenantId AND Status=N'Ready')
              OR @HasFinancialEffects=1
            BEGIN
              INSERT dbo.AccountingSourceDocuments(SourceDocumentId,SourceDocumentType,TenantId,BusinessId,PayloadJson,PayloadHash,OccurredAt,AcceptedAt,AccountingEntryRequired)
              VALUES(@DocumentId,N'WorkSessionClosureReconciliation',@TenantId,@BusinessId,@Payload,@Hash,@At,@At,
                CASE WHEN EXISTS(SELECT 1 FROM dbo.AccountingTenantSettings
                  WHERE TenantId=@TenantId AND Status=N'Ready') THEN 1 ELSE 0 END);
              INSERT dbo.AccountingPostingJobs(AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,SourcePayloadHash,OccurredAt,AccountingEntryRequired,Status,AttemptCount,CreatedAt)
              VALUES(@JobId,@TenantId,@BusinessId,@DocumentId,N'WorkSessionClosureReconciliation',@Hash,@At,
                CASE WHEN EXISTS(SELECT 1 FROM dbo.AccountingTenantSettings
                  WHERE TenantId=@TenantId AND Status=N'Ready') THEN 1 ELSE 0 END,N'Pending',0,@At);
              SELECT CAST(1 AS bit);
            END
            ELSE SELECT CAST(0 AS bit);
            """,connection,transaction);
        command.Parameters.AddWithValue("@DocumentId",payload.ReconciliationId); command.Parameters.AddWithValue("@JobId",ids.NewId());
        command.Parameters.AddWithValue("@TenantId",payload.TenantId); command.Parameters.AddWithValue("@BusinessId",payload.BusinessId);
        command.Parameters.AddWithValue("@Payload",json); command.Parameters.Add("@Hash",SqlDbType.Binary,32).Value=hash;
        command.Parameters.AddWithValue("@At",payload.ReconciledAt);
        command.Parameters.AddWithValue("@HasFinancialEffects",payload.PaymentCorrections?.Any(item =>
            item.Amount != item.OriginalAmount || item.OriginalTenderCategory != item.TenderCategory) == true);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new DBConcurrencyException("The reconciliation accounting state is unavailable."));
    }

    private static async Task<IReadOnlyList<WorkSessionPaymentTotal>> ReadClosurePaymentTotalsAsync(
        SqlConnection connection, Guid closureId, CancellationToken cancellationToken, SqlTransaction? transaction=null)
    {
        var totals = await ReadClosurePaymentTotalsAsync(connection, [closureId], cancellationToken, transaction);
        return totals.GetValueOrDefault(closureId) ?? [];
    }

    private static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<WorkSessionPaymentTotal>>> ReadClosurePaymentTotalsAsync(
        SqlConnection connection, IReadOnlyList<Guid> closureIds, CancellationToken cancellationToken,
        SqlTransaction? transaction=null)
    {
        if (closureIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<WorkSessionPaymentTotal>>();
        await using var command = new SqlCommand("""
            SELECT total.WorkSessionClosureId,total.PaymentMethodCode,
              total.SalesAmount,total.RefundAmount,total.OtherAmount,total.NetAmount,
              total.CountedAmount,total.Difference,
              CAST(CASE WHEN closureOption.OptionId IS NULL THEN 0 ELSE 1 END AS bit),
              COALESCE(closureOption.SortOrder,1000)
            FROM OPENJSON(@ClosureIds) WITH(ClosureId uniqueidentifier '$') ids
            JOIN dbo.WorkSessionClosurePaymentTotals total
              ON total.WorkSessionClosureId=ids.ClosureId
            LEFT JOIN reference.Options closureOption ON closureOption.CatalogCode=N'cash-closure-method'
              AND closureOption.Code=total.PaymentMethodCode AND closureOption.IsActive=1
            ORDER BY total.WorkSessionClosureId,COALESCE(closureOption.SortOrder,1000),
              total.PaymentMethodCode;
            """, connection, transaction);
        command.Parameters.AddWithValue("@ClosureIds", JsonSerializer.Serialize(closureIds));
        var grouped = new Dictionary<Guid, List<WorkSessionPaymentTotal>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var closureId = reader.GetGuid(0);
            if (!grouped.TryGetValue(closureId, out var totals))
                grouped[closureId] = totals = [];
            totals.Add(new WorkSessionPaymentTotal(
                reader.GetString(1), reader.GetDecimal(2), reader.GetDecimal(3), reader.GetDecimal(4),
                reader.GetDecimal(5), reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                reader.IsDBNull(7) ? null : reader.GetDecimal(7), reader.GetBoolean(8)));
        }
        return grouped.ToDictionary(pair => pair.Key,
            pair => (IReadOnlyList<WorkSessionPaymentTotal>)pair.Value);
    }

    private static async Task EnsureClosureScopeAsync(
        SqlConnection connection, SqlTransaction? transaction, Guid tenantId, Guid closureId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT COUNT(*)
            FROM dbo.WorkSessionClosures closure
            INNER JOIN dbo.WorkSessions session ON session.WorkSessionId=closure.WorkSessionId
            INNER JOIN dbo.Businesses business ON business.BusinessId=session.BusinessId
            WHERE closure.WorkSessionClosureId=@ClosureId AND business.TenantId=@TenantId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@ClosureId", closureId);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
            throw new WorkSessionNotFoundException("El cierre no existe en la empresa autenticada.");
    }

    private const string PaymentVerificationRowsSql = """
            WITH ClosureContext AS
            (
                SELECT session.WorkSessionId,session.BusinessId,session.UserId,session.OpenedAt,closure.ClosedAt,closure.ReceiptSnapshotJson SnapshotJson
                FROM dbo.WorkSessionClosures closure
                INNER JOIN dbo.WorkSessions session ON session.WorkSessionId=closure.WorkSessionId
                WHERE closure.WorkSessionClosureId=@ClosureId
            ),
            ClosureCashMovements AS
            (
                SELECT detail.DocumentId,detail.ReasonName,detail.Notes,detail.Direction,detail.DocumentNumber,
                    detail.Amount,detail.Reference,detail.OccurredAt,
                    detail.WorkSessionMovementId,detail.MovementType,raw.[key] SnapshotIndex,
                    COALESCE(TRY_CONVERT(int,JSON_VALUE(context.SnapshotJson,'$.receiptTemplateVersion')),1) TemplateVersion
                FROM ClosureContext context CROSS APPLY OPENJSON(context.SnapshotJson,N'$.cashMovements') raw
                CROSS APPLY OPENJSON(raw.value)
                WITH(DocumentId uniqueidentifier '$.documentId',ReasonName nvarchar(200) '$.reasonName',
                    Notes nvarchar(500) '$.notes',Direction nvarchar(8) '$.direction',
                    DocumentNumber nvarchar(100) '$.documentNumber',Amount decimal(19,4) '$.amount',
                    Reference nvarchar(500) '$.reference',OccurredAt datetimeoffset '$.occurredAt',
                    WorkSessionMovementId uniqueidentifier '$.workSessionMovementId',
                    MovementType nvarchar(32) '$.movementType') detail
            ),
            CashMovementDescriptions AS
            (
                SELECT DocumentId,WorkSessionMovementId,MAX(ReasonName) ReasonName,
                  MAX(Notes) Notes,MAX(TemplateVersion) TemplateVersion
                FROM ClosureCashMovements GROUP BY DocumentId,WorkSessionMovementId
            ),
            VerificationMovements AS
            (
                SELECT CONCAT(N'Sale:',CONVERT(nvarchar(36),payment.DocumentId),N':',payment.PaymentNumber) VerificationKey,
                  COALESCE(mapping.ClosureMethodCode,payment.MethodCode) PaymentMethodCode,N'Sale' MovementType,
                  payment.DocumentId SourceId,document.DocumentNumber,payment.PaymentNumber SourceNumber,
                  payment.Amount+payment.RoundingAdjustment Amount,payment.Reference,payment.CardFranchiseCode,payment.ApprovalNumber,payment.RegisteredAt OccurredAt,
                   document.DocumentType SourceDocumentType,CAST(NULL AS nvarchar(300)) CustomerName,
                   payment.MethodCode TenderMethodCode
                FROM dbo.SalesPayments payment
                INNER JOIN dbo.SalesDocuments document ON document.DocumentId=payment.DocumentId
                INNER JOIN ClosureContext context ON context.WorkSessionId=document.WorkSessionId
                LEFT JOIN worksessions.CashClosurePaymentMethodMappings mapping ON mapping.PaymentMethodCode=payment.MethodCode
                UNION ALL
                SELECT CONCAT(N'CreditSale:',CONVERT(nvarchar(36),document.DocumentId)),
                  N'Credit',N'CreditSale',document.DocumentId,document.DocumentNumber,0,
                  document.CreditAmount,NULL,NULL,NULL,document.IssuedAt,document.DocumentType,
                  COALESCE(NULLIF(party.DisplayName,N''),NULLIF(party.LegalName,N''),
                     NULLIF(party.Identification,N''),NULLIF(document.CustomerIdentification,N''),N'Cliente'),
                   N'Credit'
                FROM dbo.SalesDocuments document
                INNER JOIN ClosureContext context ON context.WorkSessionId=document.WorkSessionId
                LEFT JOIN dbo.Customers customer ON customer.CustomerId=document.CustomerId
                LEFT JOIN dbo.Parties party ON party.PartyId=customer.PartyId
                WHERE document.CreditAmount>0
                UNION ALL
                SELECT CONCAT(N'Refund:',CONVERT(nvarchar(36),settlement.ReturnId),N':',settlement.SettlementNumber),
                  COALESCE(mapping.ClosureMethodCode,settlement.MethodCode),N'Refund',settlement.ReturnId,
                  saleReturn.DocumentNumber,settlement.SettlementNumber,-settlement.Amount,settlement.Reference,
                  settlement.CardFranchiseCode,settlement.ApprovalNumber,settlement.OccurredAt,
                   N'SalesReturn',CAST(NULL AS nvarchar(300)),settlement.MethodCode
                FROM dbo.SalesReturnSettlements settlement
                INNER JOIN dbo.SalesReturns saleReturn ON saleReturn.ReturnId=settlement.ReturnId
                INNER JOIN ClosureContext context ON saleReturn.CreatedByUserId=context.UserId
                  AND saleReturn.ReturnedAt>=context.OpenedAt AND saleReturn.ReturnedAt<=context.ClosedAt
                LEFT JOIN worksessions.CashClosurePaymentMethodMappings mapping ON mapping.PaymentMethodCode=settlement.MethodCode
                WHERE settlement.SettlementType=N'Refund'
                UNION ALL
                SELECT CONCAT(N'Movement:',CONVERT(nvarchar(36),movement.WorkSessionMovementId)),
                  COALESCE(mapping.ClosureMethodCode,movement.PaymentMethodCode),movement.MovementType,movement.WorkSessionMovementId,
                  COALESCE(NULLIF(movement.Reference,N''),movement.SourceKey),0,movement.Amount,movement.Reference,
                   NULL,NULL,movement.OccurredAt,N'CashMovement',CAST(NULL AS nvarchar(300)),movement.PaymentMethodCode
                FROM dbo.WorkSessionMovements movement
                INNER JOIN ClosureContext context ON context.WorkSessionId=movement.WorkSessionId
                LEFT JOIN worksessions.CashClosurePaymentMethodMappings mapping ON mapping.PaymentMethodCode=movement.PaymentMethodCode
                WHERE movement.MovementType NOT IN(N'SalePayment',N'Refund')
                  AND (COALESCE(mapping.ClosureMethodCode,movement.PaymentMethodCode)<>N'Cash' OR
                    movement.MovementType NOT IN(N'CashIn',N'CashOut',N'PayablePayment',N'ReceivablePayment') OR
                    COALESCE(TRY_CONVERT(int,JSON_VALUE(context.SnapshotJson,'$.receiptTemplateVersion')),1)<4)
                UNION ALL
                SELECT CONCAT(N'CashMovement:',CONVERT(nvarchar(36),COALESCE(detail.WorkSessionMovementId,detail.DocumentId)),
                  CASE WHEN detail.WorkSessionMovementId IS NULL AND
                    (customerPayment.PaymentId IS NOT NULL OR supplierPayment.PaymentId IS NOT NULL)
                    THEN CONCAT(N':',detail.SnapshotIndex) ELSE N'' END),
                  N'Cash',COALESCE(detail.MovementType,
                    CASE WHEN customerPayment.PaymentId IS NOT NULL THEN N'ReceivablePayment'
                         WHEN supplierPayment.PaymentId IS NOT NULL THEN N'PayablePayment'
                         WHEN detail.Direction=N'In' THEN N'CashIn' ELSE N'CashOut' END),
                  COALESCE(detail.WorkSessionMovementId,detail.DocumentId),detail.DocumentNumber,0,
                  CASE WHEN detail.Direction=N'In' THEN detail.Amount ELSE -detail.Amount END,
                   detail.Reference,NULL,NULL,detail.OccurredAt,N'CashMovement',CAST(NULL AS nvarchar(300)),N'Cash'
                FROM ClosureCashMovements detail
                LEFT JOIN dbo.CustomerPayments customerPayment ON customerPayment.PaymentId=detail.DocumentId
                LEFT JOIN dbo.SupplierPayments supplierPayment ON supplierPayment.PaymentId=detail.DocumentId
                WHERE detail.TemplateVersion>=4
            ), LatestReconciliation AS
            (
                SELECT TOP(1) reconciliation.SnapshotJson
                FROM dbo.WorkSessionClosureReconciliations reconciliation
                WHERE reconciliation.WorkSessionClosureId=@ClosureId
                ORDER BY reconciliation.ReconciledAt DESC,reconciliation.ReconciliationId DESC
            ), DecisionStatuses AS
            (
                SELECT JSON_VALUE(value.value,N'$.verificationKey') VerificationKey,
                  JSON_VALUE(value.value,N'$.status') Status
                FROM LatestReconciliation reconciliation
                CROSS APPLY OPENJSON(reconciliation.SnapshotJson,N'$.paymentVerifications') value
            ), CorrectionStatuses AS
            (
                SELECT JSON_VALUE(value.value,N'$.verificationKey') VerificationKey,
                  JSON_VALUE(value.value,N'$.paymentMethodCode') PaymentMethodCode,
                  TRY_CONVERT(decimal(19,4),JSON_VALUE(value.value,N'$.amount')) Amount,
                   JSON_VALUE(value.value,N'$.reason') Reason,
                   JSON_VALUE(value.value,N'$.tenderMethodCode') TenderMethodCode,
                   JSON_VALUE(value.value,N'$.cardFranchiseCode') CardFranchiseCode,
                   JSON_VALUE(value.value,N'$.approvalNumber') ApprovalNumber,
                   JSON_VALUE(value.value,N'$.reference') Reference
                FROM LatestReconciliation reconciliation
                CROSS APPLY OPENJSON(reconciliation.SnapshotJson,N'$.paymentCorrections') value
            )
            SELECT movement.VerificationKey,movement.PaymentMethodCode,movement.MovementType,movement.SourceId,
              CASE WHEN movement.MovementType IN(N'ReceivablePayment',N'PayablePayment')
                THEN COALESCE(customerPayment.DocumentNumber,supplierPayment.DocumentNumber,N'')
                ELSE movement.DocumentNumber END DocumentNumber,
              movement.SourceNumber,movement.Amount,movement.Reference,
              movement.CardFranchiseCode,movement.ApprovalNumber,movement.OccurredAt,
              movement.SourceDocumentType,movement.CustomerName,decision.Status,detail.ReasonName,detail.Notes,
              correction.PaymentMethodCode CorrectedPaymentMethodCode,
              correction.Amount CorrectedAmount,correction.Reason CorrectionReason,
              movement.TenderMethodCode,correction.TenderMethodCode CorrectedTenderMethodCode,
              correction.CardFranchiseCode CorrectedCardFranchiseCode,
              correction.ApprovalNumber CorrectedApprovalNumber,
              correction.Reference CorrectedReference,
              COALESCE(NULLIF(customerParty.DisplayName,N''),NULLIF(customerParty.LegalName,N''),
                NULLIF(customerParty.Identification,N''),NULLIF(supplierParty.DisplayName,N''),
                NULLIF(supplierParty.LegalName,N''),NULLIF(supplierParty.Identification,N'')) CounterpartyName,
              COALESCE(closureOption.SortOrder,15) SortOrder
            INTO #PaymentVerifications
            FROM VerificationMovements movement
            CROSS JOIN ClosureContext context
            LEFT JOIN dbo.WorkSessionMovements cashMovement ON cashMovement.WorkSessionMovementId=movement.SourceId
              AND movement.MovementType IN(N'CashIn',N'CashOut',N'ReceivablePayment',N'PayablePayment')
            LEFT JOIN dbo.CustomerPayments customerPayment ON movement.MovementType=N'ReceivablePayment'
              AND customerPayment.PaymentId=COALESCE(cashMovement.DocumentId,movement.SourceId)
              AND customerPayment.BusinessId=context.BusinessId
            LEFT JOIN dbo.Customers paymentCustomer ON paymentCustomer.CustomerId=customerPayment.CustomerId
            LEFT JOIN dbo.Parties customerParty ON customerParty.PartyId=paymentCustomer.PartyId
            LEFT JOIN dbo.SupplierPayments supplierPayment ON movement.MovementType=N'PayablePayment'
              AND supplierPayment.PaymentId=COALESCE(cashMovement.DocumentId,movement.SourceId)
              AND supplierPayment.BusinessId=context.BusinessId
            LEFT JOIN dbo.Suppliers paymentSupplier ON paymentSupplier.SupplierId=supplierPayment.SupplierId
            LEFT JOIN dbo.Parties supplierParty ON supplierParty.PartyId=paymentSupplier.PartyId
            LEFT JOIN CashMovementDescriptions detail
              ON detail.DocumentId=CASE WHEN detail.TemplateVersion>=4 THEN COALESCE(cashMovement.DocumentId,movement.SourceId)
                   ELSE COALESCE(cashMovement.DocumentId,cashMovement.WorkSessionMovementId) END
                AND (detail.WorkSessionMovementId IS NULL OR detail.WorkSessionMovementId=movement.SourceId)
                AND movement.MovementType IN(N'CashIn',N'CashOut',N'ReceivablePayment',N'PayablePayment')
            LEFT JOIN reference.Options closureOption ON closureOption.CatalogCode=N'cash-closure-method'
              AND closureOption.Code=movement.PaymentMethodCode AND closureOption.IsActive=1
            LEFT JOIN DecisionStatuses decision ON decision.VerificationKey=movement.VerificationKey
            LEFT JOIN CorrectionStatuses correction ON correction.VerificationKey=movement.VerificationKey
            WHERE (closureOption.OptionId IS NOT NULL OR movement.PaymentMethodCode=N'Credit')
              AND (@Method IS NULL OR movement.PaymentMethodCode=@Method)
              AND (@Movement IS NULL OR movement.MovementType=@Movement);
            """;

    private static async Task<IReadOnlyList<WorkSessionPaymentVerificationItem>> ReadPaymentVerificationsAsync(
        SqlConnection connection, SqlTransaction? transaction, Guid closureId,
        CancellationToken cancellationToken)
    {
        var result = new List<WorkSessionPaymentVerificationItem>();
        await using var command = new SqlCommand(PaymentVerificationRowsSql + """
            SELECT VerificationKey,PaymentMethodCode,MovementType,SourceId,DocumentNumber,
              SourceNumber,Amount,Reference,CardFranchiseCode,ApprovalNumber,OccurredAt,
              SourceDocumentType,CustomerName,Status,ReasonName,Notes,
              CorrectedPaymentMethodCode,CorrectedAmount,CorrectionReason,
              TenderMethodCode,CorrectedTenderMethodCode,CorrectedCardFranchiseCode,
              CorrectedApprovalNumber,CorrectedReference,CounterpartyName
            FROM #PaymentVerifications ORDER BY SortOrder,OccurredAt,VerificationKey;
            """, connection, transaction);
        command.Parameters.AddWithValue("@ClosureId", closureId);
        command.Parameters.AddWithValue("@Method", DBNull.Value);
        command.Parameters.AddWithValue("@Movement", DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(ReadPaymentVerificationItem(reader));
        return result;
    }

    private static async Task<(Dictionary<string, decimal> VerifiedAmounts, bool HasMissingCreditSale,
        IReadOnlyList<WorkSessionAppliedPaymentCorrection> Corrections)>
        ValidatePaymentVerificationsAsync(SqlConnection connection, SqlTransaction transaction,
            Guid closureId, Guid businessId,
            IReadOnlyList<WorkSessionPaymentVerificationDecision> decisions,
            IReadOnlyList<WorkSessionPaymentCorrection> corrections,
            CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(PaymentVerificationRowsSql + """
            CREATE TABLE #Decisions(VerificationKey nvarchar(200) COLLATE Latin1_General_100_CI_AS,
              Status nvarchar(12));
            INSERT #Decisions(VerificationKey,Status)
            SELECT LTRIM(RTRIM(VerificationKey)),Status
            FROM OPENJSON(@DecisionsJson) WITH(VerificationKey nvarchar(200),Status nvarchar(12));
            CREATE INDEX IX_Decisions_VerificationKey ON #Decisions(VerificationKey);
            CREATE TABLE #Corrections(VerificationKey nvarchar(200) COLLATE Latin1_General_100_CI_AS,
              PaymentMethodCode nvarchar(32) COLLATE DATABASE_DEFAULT,Amount decimal(19,4),Reason nvarchar(500),
              TenderMethodCode nvarchar(32) COLLATE DATABASE_DEFAULT,CardFranchiseCode nvarchar(64) COLLATE DATABASE_DEFAULT,
              ApprovalNumber nvarchar(100),Reference nvarchar(160));
            INSERT #Corrections(VerificationKey,PaymentMethodCode,Amount,Reason,
              TenderMethodCode,CardFranchiseCode,ApprovalNumber,Reference)
            SELECT LTRIM(RTRIM(VerificationKey)),PaymentMethodCode,Amount,LTRIM(RTRIM(Reason)),
              TenderMethodCode,NULLIF(LTRIM(RTRIM(CardFranchiseCode)),N''),
              NULLIF(LTRIM(RTRIM(ApprovalNumber)),N''),NULLIF(LTRIM(RTRIM(Reference)),N'')
            FROM OPENJSON(@CorrectionsJson) WITH(VerificationKey nvarchar(200),
              PaymentMethodCode nvarchar(32),Amount decimal(19,4),Reason nvarchar(500),
              TenderMethodCode nvarchar(32),CardFranchiseCode nvarchar(64),
              ApprovalNumber nvarchar(100),Reference nvarchar(160));
            CREATE INDEX IX_Corrections_VerificationKey ON #Corrections(VerificationKey);

            WITH Required AS
            (
                SELECT VerificationKey COLLATE Latin1_General_100_CI_AS VerificationKey
                FROM #PaymentVerifications
                WHERE (PaymentMethodCode<>N'Cash' OR MovementType IN
                  (N'CashIn',N'CashOut',N'ReceivablePayment',N'PayablePayment',N'CreditSale'))
                  AND MovementType NOT IN(N'Sale',N'Refund')
            )
            SELECT CAST(CASE WHEN
                EXISTS(SELECT 1 FROM #Decisions GROUP BY VerificationKey HAVING COUNT(*)<>1)
                OR EXISTS(SELECT 1 FROM #Corrections GROUP BY VerificationKey HAVING COUNT(*)<>1)
                OR EXISTS(SELECT 1 FROM Required GROUP BY VerificationKey HAVING COUNT(*)<>1)
                OR EXISTS(
                    SELECT 1 FROM Required source FULL OUTER JOIN #Decisions decision
                      ON source.VerificationKey=decision.VerificationKey
                    WHERE source.VerificationKey IS NULL OR decision.VerificationKey IS NULL)
                THEN 1 ELSE 0 END AS bit);

            SELECT source.PaymentMethodCode,
              SUM(CASE WHEN (source.PaymentMethodCode=N'Cash' AND source.MovementType NOT IN
                    (N'CashIn',N'CashOut',N'ReceivablePayment',N'PayablePayment',N'CreditSale'))
                  OR source.MovementType IN(N'Sale',N'Refund')
                  OR decision.Status=N'Verified' THEN source.Amount ELSE 0 END),
              MAX(CASE WHEN source.MovementType=N'CreditSale' AND decision.Status=N'Missing'
                THEN 1 ELSE 0 END)
            FROM #PaymentVerifications source
            LEFT JOIN #Decisions decision ON source.VerificationKey COLLATE Latin1_General_100_CI_AS =
              decision.VerificationKey
            GROUP BY source.PaymentMethodCode;

            SELECT correction.VerificationKey,source.MovementType,source.SourceId,
              source.SourceNumber,source.PaymentMethodCode,source.Amount,
              correction.PaymentMethodCode,correction.Amount,correction.Reason,
              decision.Status,source.SourceDocumentType,
              COALESCE(saleParty.PartyId,customerParty.PartyId,supplierParty.PartyId,
                returnParty.PartyId) PartyId,
              COALESCE(sale.CustomerId,customerPayment.CustomerId,saleReturn.CustomerId) CustomerId,
              COALESCE(saleReceivable.ReceivableId,customerApplication.ReceivableId,
                supplierApplication.PayableId) SubledgerId,
              COALESCE(saleReceivable.OutstandingAmount,customerBalance.OutstandingAmount,
                supplierBalance.OutstandingAmount) OutstandingAmount,
              COALESCE(NULLIF(customerApplication.ApplicationCount,0),supplierApplication.ApplicationCount,1) ApplicationCount,
              COALESCE(NULLIF(customerTender.TenderCount,0),supplierTender.TenderCount,1) TenderCount,
              cashReason.CounterpartAccountingCategory,
              CAST(CASE WHEN
                (source.MovementType=N'Sale' AND sale.ProcessingStatus=N'Processed') OR
                (source.MovementType=N'Refund' AND saleReturn.Status=N'Processed') OR
                (source.MovementType=N'ReceivablePayment' AND customerPayment.Status=N'Processed') OR
                (source.MovementType=N'PayablePayment' AND supplierPayment.Status=N'Processed') OR
                (source.MovementType IN(N'CashIn',N'CashOut') AND cashDocument.Status=N'Processed')
                THEN 1 ELSE 0 END AS bit) SourceProcessed,
              supplierPayment.CurrencyCode, sale.CustomerPartySiteId,
              sale.DocumentNumber,sale.IssuedAt,
              saleReturn.TotalAmount,
              customerCredit.CustomerCreditId,sale.CreditAmount,
              source.TenderMethodCode,correction.TenderMethodCode,
              correction.CardFranchiseCode,correction.ApprovalNumber,correction.Reference,
              correctedMapping.ClosureMethodCode,
              franchise.OptionId,source.CardFranchiseCode,source.ApprovalNumber,
              source.Reference,
              COALESCE(NULLIF(CONCAT(N'BankAccount:',CONVERT(nvarchar(36),
                CASE WHEN source.MovementType=N'Sale' THEN saleTender.BankAccountId
                     WHEN source.MovementType=N'Refund' THEN refundTender.BankAccountId
                     WHEN source.MovementType=N'ReceivablePayment' AND customerTender.TenderCount=1
                       THEN customerTender.BankAccountId
                     WHEN source.MovementType=N'PayablePayment' AND supplierTender.TenderCount=1
                       THEN supplierTender.BankAccountId END)),N'BankAccount:'),
                originalTenderCategory.Category,originalClosureCategory.Category),
              correctedTenderCategory.Category
            FROM #Corrections correction
            LEFT JOIN #PaymentVerifications source ON source.VerificationKey COLLATE Latin1_General_100_CI_AS = correction.VerificationKey
            LEFT JOIN #Decisions decision ON decision.VerificationKey=correction.VerificationKey
            LEFT JOIN worksessions.CashClosurePaymentMethodMappings correctedMapping
              ON correctedMapping.PaymentMethodCode=COALESCE(correction.TenderMethodCode,
                  CASE WHEN correction.PaymentMethodCode=source.PaymentMethodCode
                    THEN source.TenderMethodCode ELSE correction.PaymentMethodCode END)
                AND correctedMapping.RequiresCount=1
            LEFT JOIN reference.Options franchise ON franchise.CatalogCode=N'card-franchise'
              AND franchise.Code=correction.CardFranchiseCode AND franchise.IsActive=1
            LEFT JOIN dbo.AccountingConfigurationProfiles profile
              ON profile.IsDefault=1 AND profile.IsActive=1
            LEFT JOIN dbo.AccountingSourceCategoryMappings originalTenderCategory
              ON originalTenderCategory.ProfileCode=profile.ProfileCode
                AND originalTenderCategory.SourceType=CASE
                  WHEN source.MovementType=N'ReceivablePayment' THEN N'CustomerPaymentMethod'
                  WHEN source.MovementType=N'PayablePayment' THEN N'SupplierPaymentMethod'
                  ELSE N'PosPaymentMethod' END
                AND originalTenderCategory.SourceCode=source.TenderMethodCode
            LEFT JOIN dbo.AccountingSourceCategoryMappings originalClosureCategory
              ON originalClosureCategory.ProfileCode=profile.ProfileCode
                AND originalClosureCategory.SourceType=N'ClosurePaymentMethod'
                AND originalClosureCategory.SourceCode=source.PaymentMethodCode
            LEFT JOIN dbo.AccountingSourceCategoryMappings correctedTenderCategory
              ON correctedTenderCategory.ProfileCode=profile.ProfileCode
                AND correctedTenderCategory.SourceType=CASE
                  WHEN source.MovementType=N'ReceivablePayment' THEN N'CustomerPaymentMethod'
                  WHEN source.MovementType=N'PayablePayment' THEN N'SupplierPaymentMethod'
                  ELSE N'PosPaymentMethod' END
                AND correctedTenderCategory.SourceCode=COALESCE(correction.TenderMethodCode,
                  CASE WHEN correction.PaymentMethodCode=source.PaymentMethodCode
                    THEN source.TenderMethodCode ELSE correction.PaymentMethodCode END)
            LEFT JOIN dbo.SalesDocuments sale WITH(UPDLOCK,HOLDLOCK)
              ON source.MovementType=N'Sale' AND sale.DocumentId=source.SourceId AND sale.BusinessId=@BusinessId
            LEFT JOIN dbo.SalesPayments saleTender ON source.MovementType=N'Sale'
              AND saleTender.DocumentId=sale.DocumentId AND saleTender.PaymentNumber=source.SourceNumber
            LEFT JOIN dbo.Customers saleCustomer ON saleCustomer.CustomerId=sale.CustomerId
            LEFT JOIN dbo.Parties saleParty ON saleParty.PartyId=saleCustomer.PartyId
            LEFT JOIN dbo.Receivables saleReceivable WITH(UPDLOCK,HOLDLOCK)
              ON saleReceivable.SourceDocumentId=sale.DocumentId
                AND saleReceivable.SourceDocumentType=sale.DocumentType AND saleReceivable.BusinessId=@BusinessId
            LEFT JOIN dbo.WorkSessionMovements paymentMovement WITH(UPDLOCK,HOLDLOCK)
              ON paymentMovement.WorkSessionMovementId=source.SourceId
                AND source.MovementType IN(N'ReceivablePayment',N'PayablePayment')
            LEFT JOIN dbo.CustomerPayments customerPayment WITH(UPDLOCK,HOLDLOCK)
              ON source.MovementType=N'ReceivablePayment' AND customerPayment.PaymentId=COALESCE(paymentMovement.DocumentId,source.SourceId)
                AND customerPayment.BusinessId=@BusinessId
            LEFT JOIN dbo.Customers paymentCustomer ON paymentCustomer.CustomerId=customerPayment.CustomerId
            LEFT JOIN dbo.Parties customerParty ON customerParty.PartyId=paymentCustomer.PartyId
            OUTER APPLY (SELECT COUNT(*) ApplicationCount,MAX(ReceivableId) ReceivableId
              FROM dbo.CustomerPaymentApplications WHERE PaymentId=customerPayment.PaymentId) customerApplication
            OUTER APPLY (SELECT COUNT(*) TenderCount,MAX(BankAccountId) BankAccountId FROM dbo.CustomerPaymentTenders
              WHERE PaymentId=customerPayment.PaymentId) customerTender
            LEFT JOIN dbo.Receivables customerBalance WITH(UPDLOCK,HOLDLOCK)
              ON customerBalance.ReceivableId=customerApplication.ReceivableId
                AND customerBalance.BusinessId=@BusinessId
            LEFT JOIN dbo.SupplierPayments supplierPayment WITH(UPDLOCK,HOLDLOCK)
              ON source.MovementType=N'PayablePayment' AND supplierPayment.PaymentId=COALESCE(paymentMovement.DocumentId,source.SourceId)
                AND supplierPayment.BusinessId=@BusinessId
            LEFT JOIN dbo.Suppliers paymentSupplier ON paymentSupplier.SupplierId=supplierPayment.SupplierId
            LEFT JOIN dbo.Parties supplierParty ON supplierParty.PartyId=paymentSupplier.PartyId
            OUTER APPLY (SELECT COUNT(*) ApplicationCount,MAX(PayableId) PayableId
              FROM dbo.SupplierPaymentApplications WHERE PaymentId=supplierPayment.PaymentId) supplierApplication
            OUTER APPLY (SELECT COUNT(*) TenderCount,MAX(BankAccountId) BankAccountId FROM dbo.SupplierPaymentTenders
              WHERE PaymentId=supplierPayment.PaymentId) supplierTender
            LEFT JOIN dbo.Payables supplierBalance WITH(UPDLOCK,HOLDLOCK)
              ON supplierBalance.PayableId=supplierApplication.PayableId
                AND supplierBalance.BusinessId=@BusinessId
            LEFT JOIN dbo.SalesReturns saleReturn WITH(UPDLOCK,HOLDLOCK)
              ON source.MovementType=N'Refund' AND saleReturn.ReturnId=source.SourceId
                AND saleReturn.BusinessId=@BusinessId
            LEFT JOIN dbo.SalesReturnSettlements refundTender ON source.MovementType=N'Refund'
              AND refundTender.ReturnId=saleReturn.ReturnId
                AND refundTender.SettlementNumber=source.SourceNumber
            LEFT JOIN dbo.Customers returnCustomer ON returnCustomer.CustomerId=saleReturn.CustomerId
            LEFT JOIN dbo.Parties returnParty ON returnParty.PartyId=returnCustomer.PartyId
            LEFT JOIN dbo.CustomerCredits customerCredit WITH(UPDLOCK,HOLDLOCK)
              ON customerCredit.SourceReturnId=saleReturn.ReturnId AND customerCredit.BusinessId=@BusinessId
            LEFT JOIN dbo.WorkSessionMovements cashMovement WITH(UPDLOCK,HOLDLOCK)
              ON source.MovementType IN(N'CashIn',N'CashOut')
                AND cashMovement.WorkSessionMovementId=source.SourceId
            LEFT JOIN dbo.CashMovementDocuments cashDocument WITH(UPDLOCK,HOLDLOCK)
              ON source.MovementType IN(N'CashIn',N'CashOut')
                AND cashDocument.DocumentId=COALESCE(cashMovement.DocumentId,source.SourceId)
                AND cashDocument.BusinessId=@BusinessId
            LEFT JOIN dbo.CashMovementReasons cashReason
              ON cashReason.ReasonId=cashDocument.ReasonId AND cashReason.BusinessId=@BusinessId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@ClosureId", closureId);
        command.Parameters.AddWithValue("@Method", DBNull.Value);
        command.Parameters.AddWithValue("@Movement", DBNull.Value);
        command.Parameters.Add("@DecisionsJson", SqlDbType.NVarChar, -1).Value =
            JsonSerializer.Serialize(decisions.Select(value => new
            {
                VerificationKey = value.VerificationKey.Trim(), value.Status
            }));
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.Add("@CorrectionsJson", SqlDbType.NVarChar, -1).Value =
            JsonSerializer.Serialize(corrections);
        var verifiedAmounts = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var hasMissingCreditSale = false;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.GetBoolean(0))
            throw new WorkSessionValidationException(
                "Debe verificar cada comprobante y movimiento conciliable exactamente una vez.");
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            verifiedAmounts.Add(reader.GetString(0), reader.GetDecimal(1));
            hasMissingCreditSale |= reader.GetInt32(2) != 0;
        }
        await reader.NextResultAsync(cancellationToken);
        var applied = new List<WorkSessionAppliedPaymentCorrection>(corrections.Count);
        var balances = new Dictionary<(string Kind, Guid Id), (decimal Outstanding, decimal Adjustment)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(1) || reader.IsDBNull(9) && reader.GetString(1) is not ("Sale" or "Refund"))
                throw new WorkSessionValidationException("El comprobante corregido no pertenece al cierre o no fue verificado.");
            var movementType = reader.GetString(1);
            if (movementType == "CreditSale" || !reader.GetBoolean(18))
                throw new WorkSessionValidationException("Solo se puede corregir un movimiento de dinero ya procesado.");
            var originalMethod = reader.GetString(4);
            var originalAmount = reader.GetDecimal(5);
            var method = reader.GetString(6);
            var amount = reader.GetDecimal(7);
            var originalTender = reader.GetString(26);
            var tender = reader.IsDBNull(27)
                ? (method.Equals(originalMethod, StringComparison.OrdinalIgnoreCase) ? originalTender : method)
                : reader.GetString(27);
            var cardFranchise = reader.IsDBNull(28) ? null : reader.GetString(28);
            var approvalNumber = reader.IsDBNull(29) ? null : reader.GetString(29);
            var reference = reader.IsDBNull(30) ? null : reader.GetString(30);
            var originalTenderCategory = reader.IsDBNull(36) ? null : reader.GetString(36);
            var tenderCategory = reader.IsDBNull(37) ? null : reader.GetString(37);
            if (method.Equals(originalMethod, StringComparison.OrdinalIgnoreCase) &&
                tender.Equals(originalTender, StringComparison.OrdinalIgnoreCase))
                tenderCategory = originalTenderCategory;
            if (reader.IsDBNull(31) || !reader.GetString(31).Equals(method, StringComparison.OrdinalIgnoreCase))
                throw new WorkSessionValidationException("El medio elegido no pertenece al grupo de conciliación.");
            if (!reader.IsDBNull(27))
            {
                if (tender is not ("Cash" or "Transfer" or "DebitCard" or "CreditCard"))
                    throw new WorkSessionValidationException("Selecciona efectivo, tarjeta débito, tarjeta crédito o transferencia.");
                if (tender is "DebitCard" or "CreditCard")
                {
                    if (cardFranchise is null || approvalNumber is null || reader.IsDBNull(32))
                        throw new WorkSessionValidationException("La tarjeta requiere franquicia vigente y número de aprobación.");
                }
                else if (cardFranchise is not null || approvalNumber is not null)
                    throw new WorkSessionValidationException("La franquicia y aprobación solo aplican a tarjetas.");
                if (tender == "Transfer" && reference is null)
                    throw new WorkSessionValidationException("La transferencia requiere su referencia.");
            }
            if (reader.IsDBNull(9) == false && reader.GetString(9) != "Verified")
                throw new WorkSessionValidationException("Verifica el comprobante antes de corregirlo.");
            var tenderDetailsChanged = !reader.IsDBNull(27) &&
                (!tender.Equals(originalTender, StringComparison.OrdinalIgnoreCase) ||
                 cardFranchise != (reader.IsDBNull(33) ? null : reader.GetString(33)) ||
                 approvalNumber != (reader.IsDBNull(34) ? null : reader.GetString(34)) ||
                 reference != (reader.IsDBNull(35) ? null : reader.GetString(35)));
            if (Math.Sign(amount) != Math.Sign(originalAmount) ||
                (method.Equals(originalMethod, StringComparison.OrdinalIgnoreCase) &&
                 amount == originalAmount && !tenderDetailsChanged))
                throw new WorkSessionValidationException("La corrección debe conservar el sentido del pago y cambiar el medio o valor.");
            if (movementType is "CashIn" or "CashOut" && method != "Cash")
                throw new WorkSessionValidationException("Las entradas y salidas de caja conservan el medio efectivo.");
            if (!reader.IsDBNull(27) &&
                (!originalMethod.Equals(method, StringComparison.OrdinalIgnoreCase) ||
                 !tender.Equals(originalTender, StringComparison.OrdinalIgnoreCase)) &&
                (originalTenderCategory is null || tenderCategory is null))
                throw new WorkSessionValidationException("Falta la configuración contable del medio de pago corregido.");
            var delta = amount - originalAmount;
            if (movementType == "Sale" && delta != 0 &&
                reader.GetString(10) is ("SalesInvoice" or "ServiceInvoice") &&
                (reader.IsDBNull(25) || reader.GetDecimal(25) == 0))
                throw new WorkSessionValidationException(
                    "La factura electrónica se emitió de contado. Corrige el medio desde el cierre, pero resuelve una diferencia de valor mediante el proceso fiscal o de cartera correspondiente.");
            var partyId = reader.IsDBNull(11) ? (Guid?)null : reader.GetGuid(11);
            var customerId = reader.IsDBNull(12) ? (Guid?)null : reader.GetGuid(12);
            var subledgerId = reader.IsDBNull(13) ? (Guid?)null : reader.GetGuid(13);
            var counterpartCategory = movementType switch
            {
                "Sale" or "ReceivablePayment" => AccountingCategories.AccountsReceivable,
                "PayablePayment" => AccountingCategories.AccountsPayable,
                "Refund" => AccountingCategories.CustomerCreditsPayable,
                "CashIn" or "CashOut" => reader.IsDBNull(17) ? null : reader.GetString(17),
                _ => null
            };
            if (delta != 0)
            {
                if (counterpartCategory is null)
                    throw new WorkSessionValidationException("Falta la cuenta contraparte para corregir el valor.");
                if (movementType is "ReceivablePayment" or "PayablePayment" &&
                    (reader.GetInt32(15) != 1 || reader.GetInt32(16) != 1 || subledgerId is null))
                    throw new WorkSessionValidationException("El pago tiene varias aplicaciones o medios; corrígelo desde cartera.");
                if (movementType == "PayablePayment" && reader.GetString(19) != "COP")
                    throw new WorkSessionValidationException("La corrección de este pago en otra moneda requiere el proceso de cartera.");
                if (movementType == "Sale" && (customerId is null || reader.IsDBNull(20)))
                    throw new WorkSessionValidationException("La venta necesita un cliente y sede identificados para ajustar cartera.");
                if (movementType == "Refund" && (delta < 0 || customerId is null || !reader.IsDBNull(24)))
                    throw new WorkSessionValidationException("La devolución no admite un reembolso mayor ni un crédito ya aplicado.");
                if (movementType is "Sale" or "ReceivablePayment" or "PayablePayment")
                {
                    if (subledgerId is null && movementType != "Sale")
                        throw new WorkSessionValidationException("No se encontró la obligación aplicada por el pago.");
                    if (subledgerId is Guid ledgerId)
                    {
                        var kind = movementType == "PayablePayment" ? "Payable" : "Receivable";
                        var key = (kind, ledgerId);
                        var adjustment = movementType == "PayablePayment" ? delta : -delta;
                        var current = balances.GetValueOrDefault(key,
                            (Outstanding: reader.GetDecimal(14), Adjustment: 0m));
                        balances[key] = (current.Outstanding, current.Adjustment + adjustment);
                    }
                }
            }
            applied.Add(new WorkSessionAppliedPaymentCorrection(reader.GetString(0), movementType,
                reader.GetGuid(2), reader.GetInt32(3), originalMethod, originalAmount,
                method, amount, reader.GetString(8), partyId, customerId,
                subledgerId, counterpartCategory, originalTender, tender,
                cardFranchise, approvalNumber, reference, originalTenderCategory, tenderCategory));
        }
        if (applied.Count != corrections.Count || balances.Values.Any(value => value.Outstanding + value.Adjustment < 0))
            throw new WorkSessionValidationException("La corrección excede el saldo disponible de la factura.");
        foreach (var correction in applied)
        {
            if (!correction.OriginalPaymentMethodCode.Equals("Cash", StringComparison.OrdinalIgnoreCase))
                verifiedAmounts[correction.OriginalPaymentMethodCode] =
                    verifiedAmounts.GetValueOrDefault(correction.OriginalPaymentMethodCode) - correction.OriginalAmount;
            if (!correction.PaymentMethodCode.Equals("Cash", StringComparison.OrdinalIgnoreCase))
                verifiedAmounts[correction.PaymentMethodCode] =
                    verifiedAmounts.GetValueOrDefault(correction.PaymentMethodCode) + correction.Amount;
        }
        return (verifiedAmounts, hasMissingCreditSale, applied);
    }

    private static async Task<WorkSessionClosureReconciliationView?> ReadAcceptedReconciliationAsync(
        SqlConnection connection, SqlTransaction transaction, WorkSessionIdentity identity,
        Guid closureId, Guid businessId, string idempotencyKey, string requestHash,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT reconciliation.ReconciliationId,reconciliation.ReconciledByUserId,
              reconciliation.IdempotencyKey,reconciliation.Status,reconciliation.Note,
              reconciliation.SnapshotJson,reconciliation.ReconciledAt,
              COALESCE(job.Status,N'NotRequired') AccountingStatus
            FROM dbo.WorkSessionClosureReconciliations reconciliation
            OUTER APPLY (SELECT TOP(1) posting.Status
              FROM dbo.AccountingPostingJobs posting
              WHERE posting.SourceDocumentId=reconciliation.ReconciliationId
                AND posting.SourceDocumentType=N'WorkSessionClosureReconciliation'
              ORDER BY posting.CreatedAt DESC) job
            WHERE reconciliation.WorkSessionClosureId=@ClosureId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@ClosureId", closureId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        if (reader.GetGuid(1) != identity.UserId ||
            !string.Equals(reader.GetString(2), idempotencyKey, StringComparison.Ordinal))
            throw new WorkSessionConflictException("El cierre ya fue conciliado con otra operación.");
        using var snapshot = JsonDocument.Parse(reader.GetString(5));
        if (!snapshot.RootElement.TryGetProperty("requestHash", out var storedHash) ||
            !string.Equals(storedHash.GetString(), requestHash, StringComparison.Ordinal))
            throw new WorkSessionConflictException("La clave idempotente corresponde a otros valores de conciliación.");
        var lines = snapshot.RootElement.GetProperty("lines")
            .Deserialize<ReconcileWorkSessionClosureLine[]>(Json) ?? [];
        var reclassifications = snapshot.RootElement.GetProperty("reclassifications")
            .Deserialize<WorkSessionPaymentReclassification[]>(Json) ?? [];
        return new(reader.GetGuid(0), closureId, businessId, reader.GetString(3),
            reader.GetDateTimeOffset(6), reader.GetGuid(1), lines, reclassifications,
            reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(7), true);
    }

    private static WorkSessionPaymentVerificationItem ReadPaymentVerificationItem(SqlDataReader reader) =>
            new(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetGuid(3),
                reader.GetString(4), reader.GetInt32(5), reader.GetDecimal(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9), reader.GetDateTimeOffset(10),
                reader.GetString(11), reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetString(16),
                reader.IsDBNull(17) ? null : reader.GetDecimal(17),
                reader.IsDBNull(18) ? null : reader.GetString(18),
                reader.IsDBNull(19) ? null : reader.GetString(19),
                reader.IsDBNull(20) ? null : reader.GetString(20),
                reader.IsDBNull(21) ? null : reader.GetString(21),
                reader.IsDBNull(22) ? null : reader.GetString(22),
                reader.IsDBNull(23) ? null : reader.GetString(23),
                reader.IsDBNull(24) ? null : reader.GetString(24));

    private static void AddClosureSearchParameters(SqlCommand command, WorkSessionIdentity identity,
        DateOnly from, DateOnly to, string? status)
    {
        command.Parameters.AddWithValue("@TenantId",identity.TenantId);
        command.Parameters.AddWithValue("@From",new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue),TimeSpan.Zero));
        command.Parameters.AddWithValue("@Until",new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue),TimeSpan.Zero));
        command.Parameters.AddWithValue("@Status",(object?)status ?? DBNull.Value);
    }
}
