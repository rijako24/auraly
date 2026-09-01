using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.WorkSessions;
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
            INNER JOIN dbo.Warehouses warehouse ON warehouse.WarehouseId=session.WarehouseId
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
                ORDER BY job.CreatedAt DESC),N'AccountingDisabled')
            """ + filter + """
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
                reader.GetGuid(4), reader.GetString(5), reader.GetGuid(6), reader.GetString(7),
                reader.GetDateTimeOffset(8), reader.GetDateTimeOffset(9), reader.GetInt64(10),
                reader.GetInt32(11), reader.GetInt64(12), reader.GetDecimal(13), reader.GetDecimal(14),
                reader.GetDecimal(15), reader.GetString(16), reader.GetString(17), []));
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

    public async Task<WorkSessionClosureReconciliationView> ReconcileClosureAsync(
        WorkSessionIdentity identity, Guid closureId, string idempotencyKey,
        ReconcileWorkSessionClosureRequest request, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            Guid businessId;
            await using (var scope = new SqlCommand("""
                SELECT session.BusinessId
                FROM dbo.WorkSessionClosures closure WITH(UPDLOCK,HOLDLOCK)
                INNER JOIN dbo.WorkSessions session ON session.WorkSessionId=closure.WorkSessionId
                INNER JOIN dbo.Businesses business ON business.BusinessId=session.BusinessId
                WHERE closure.WorkSessionClosureId=@ClosureId AND business.TenantId=@TenantId;
                """, connection, transaction))
            {
                scope.Parameters.AddWithValue("@ClosureId", closureId);
                scope.Parameters.AddWithValue("@TenantId", identity.TenantId);
                businessId = (Guid?)await scope.ExecuteScalarAsync(cancellationToken)
                    ?? throw new WorkSessionNotFoundException("El cierre no existe en la empresa autenticada.");
            }
            var expected = await ReadClosurePaymentTotalsAsync(connection, closureId, cancellationToken, transaction);
            var countable = expected.Where(value => value.RequiresCount).ToArray();
            var lines = request.Lines.ToDictionary(value => value.PaymentMethodCode.Trim(), StringComparer.OrdinalIgnoreCase);
            if (lines.Count != request.Lines.Count || countable.Any(value => !lines.ContainsKey(value.PaymentMethodCode)) ||
                lines.Keys.Any(code => countable.All(value => !value.PaymentMethodCode.Equals(code, StringComparison.OrdinalIgnoreCase))))
                throw new WorkSessionValidationException("La conciliación debe incluir cada medio contado exactamente una vez.");
            var expectedVerifications = await ReadPaymentVerificationsAsync(
                connection, transaction, closureId, cancellationToken);
            var individuallyVerifiable = expectedVerifications.Where(item =>
                !item.PaymentMethodCode.Equals("Cash", StringComparison.OrdinalIgnoreCase)).ToArray();
            var requestedVerifications = request.PaymentVerifications ?? [];
            var verificationDecisions = requestedVerifications
                .GroupBy(value => value.VerificationKey.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
            if (verificationDecisions.Any(group => group.Value.Length != 1) ||
                verificationDecisions.Count != individuallyVerifiable.Length ||
                individuallyVerifiable.Any(item => !verificationDecisions.ContainsKey(item.VerificationKey)) ||
                verificationDecisions.Keys.Any(key => individuallyVerifiable.All(item =>
                    !item.VerificationKey.Equals(key, StringComparison.OrdinalIgnoreCase))))
                throw new WorkSessionValidationException(
                    "Debe verificar cada comprobante de tarjeta y transferencia exactamente una vez.");
            var verifiedAmounts = individuallyVerifiable
                .Where(item => verificationDecisions[item.VerificationKey][0].Status == "Verified")
                .GroupBy(item => item.PaymentMethodCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount),
                    StringComparer.OrdinalIgnoreCase);
            foreach (var method in individuallyVerifiable.Select(item => item.PaymentMethodCode)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var verified = verifiedAmounts.GetValueOrDefault(method);
                if (!lines.TryGetValue(method, out var line) ||
                    decimal.Round(line.VerifiedAmount, 4) != decimal.Round(verified, 4))
                    throw new WorkSessionValidationException(
                        "El valor verificado debe corresponder a los comprobantes confirmados.");
            }
            var differences = countable.ToDictionary(value => value.PaymentMethodCode,
                value => decimal.Round(lines[value.PaymentMethodCode].VerifiedAmount-value.NetAmount,4),
                StringComparer.OrdinalIgnoreCase);
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
            var status = differences.Values.All(value => value == 0)
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
            foreach (var reason in normalizedLines.Select(line => line.ReasonCode).Where(value => value is not null).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await using var validateReason = new SqlCommand("""
                    SELECT COUNT(*) FROM reference.Options
                    WHERE CatalogCode=N'cash-reconciliation-reason' AND Code=@Code AND IsActive=1;
                    """, connection, transaction);
                validateReason.Parameters.AddWithValue("@Code", reason!);
                if (Convert.ToInt32(await validateReason.ExecuteScalarAsync(cancellationToken)) != 1)
                    throw new WorkSessionValidationException("El motivo de conciliación no pertenece al catálogo vigente.");
            }
            var snapshotObject = new
            {
                reconciliationId, closureId, status, reconciledAt, lines=normalizedLines,
                paymentVerifications=requestedVerifications,
                reclassifications=request.Reclassifications, note=request.Note
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
            foreach (var line in normalizedLines)
            {
                var total = countable.Single(value => value.PaymentMethodCode.Equals(line.PaymentMethodCode,StringComparison.OrdinalIgnoreCase));
                await using var insert = new SqlCommand("""
                    INSERT dbo.WorkSessionClosureReconciliationLines
                      (ReconciliationId,PaymentMethodCode,ExpectedAmount,CountedAmount,VerifiedAmount,Difference,IsConfirmed,ReasonCode)
                    VALUES(@Id,@Method,@Expected,@Counted,@Verified,@Difference,@Confirmed,@Reason);
                    """, connection, transaction);
                insert.Parameters.AddWithValue("@Id",reconciliationId); insert.Parameters.AddWithValue("@Method",line.PaymentMethodCode);
                AddMoney(insert,"@Expected",total.NetAmount); insert.Parameters.AddWithValue("@Counted",(object?)total.CountedAmount ?? DBNull.Value);
                AddMoney(insert,"@Verified",line.VerifiedAmount); AddMoney(insert,"@Difference",line.VerifiedAmount-total.NetAmount);
                insert.Parameters.AddWithValue("@Confirmed",line.IsConfirmed); insert.Parameters.AddWithValue("@Reason",(object?)line.ReasonCode ?? DBNull.Value);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
            for (var index=0; index<request.Reclassifications.Count; index++)
            {
                var correction=request.Reclassifications[index];
                await using var insert=new SqlCommand("""
                    INSERT dbo.WorkSessionClosureReclassifications
                      (ReclassificationId,ReconciliationId,LineNumber,FromPaymentMethodCode,ToPaymentMethodCode,Amount)
                    VALUES(@Id,@ReconciliationId,@Line,@From,@To,@Amount);
                    """,connection,transaction);
                insert.Parameters.AddWithValue("@Id",ids.NewId()); insert.Parameters.AddWithValue("@ReconciliationId",reconciliationId);
                insert.Parameters.AddWithValue("@Line",index+1); insert.Parameters.AddWithValue("@From",correction.FromPaymentMethodCode);
                insert.Parameters.AddWithValue("@To",correction.ToPaymentMethodCode); AddMoney(insert,"@Amount",correction.Amount);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
            var accountingLines = new List<WorkSessionClosureReconciliationAccountingLine>();
            foreach (var line in normalizedLines)
            {
                var total=countable.Single(value=>value.PaymentMethodCode.Equals(line.PaymentMethodCode,StringComparison.OrdinalIgnoreCase));
                await using var category=new SqlCommand("""
                    SELECT mapping.Category FROM dbo.AccountingConfigurationProfiles profile
                    INNER JOIN dbo.AccountingSourceCategoryMappings mapping ON mapping.ProfileCode=profile.ProfileCode
                      AND mapping.SourceType=N'ClosurePaymentMethod' AND mapping.SourceCode=@Method
                    WHERE profile.IsDefault=1 AND profile.IsActive=1;
                    """,connection,transaction);
                category.Parameters.AddWithValue("@Method",line.PaymentMethodCode);
                accountingLines.Add(new(line.PaymentMethodCode,(string)(await category.ExecuteScalarAsync(cancellationToken)
                    ?? throw new WorkSessionValidationException("Falta la configuración contable del medio de pago.")),
                    total.NetAmount,total.CountedAmount ?? total.NetAmount,line.VerifiedAmount,
                    line.VerifiedAmount-total.NetAmount,line.ReasonCode));
            }
            var payload = new WorkSessionClosureReconciliationPayload(reconciliationId,closureId,identity.TenantId,businessId,
                identity.UserId,reconciledAt,accountingLines,request.Reclassifications);
            var accountingRequired = accountingLines.Any(line => line.VerifiedAmount != line.CountedAmount)
                || differences.Values.Any(value => value != 0);
            if (accountingRequired)
                await InsertReconciliationAccountingJobAsync(connection,transaction,payload,cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(reconciliationId,closureId,businessId,status,reconciledAt,identity.UserId,normalizedLines,
                request.Reclassifications,request.Note,accountingRequired ? "Pending" : "NotRequired");
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new WorkSessionConflictException("El cierre ya fue conciliado o la operación ya fue recibida.");
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    private async Task InsertReconciliationAccountingJobAsync(SqlConnection connection, SqlTransaction transaction,
        WorkSessionClosureReconciliationPayload payload, CancellationToken cancellationToken)
    {
        var json=JsonSerializer.Serialize(payload,Json); var hash=SHA256.HashData(Encoding.UTF8.GetBytes(json));
        await using var command=new SqlCommand("""
            IF EXISTS(SELECT 1 FROM dbo.AccountingTenantSettings WHERE TenantId=@TenantId AND Status=N'Ready')
            BEGIN
              INSERT dbo.AccountingSourceDocuments(SourceDocumentId,SourceDocumentType,TenantId,BusinessId,PayloadJson,PayloadHash,OccurredAt,AcceptedAt)
              VALUES(@DocumentId,N'WorkSessionClosureReconciliation',@TenantId,@BusinessId,@Payload,@Hash,@At,@At);
              INSERT dbo.AccountingPostingJobs(AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,SourcePayloadHash,OccurredAt,Status,AttemptCount,CreatedAt)
              VALUES(@JobId,@TenantId,@BusinessId,@DocumentId,N'WorkSessionClosureReconciliation',@Hash,@At,N'Pending',0,@At);
            END;
            """,connection,transaction);
        command.Parameters.AddWithValue("@DocumentId",payload.ReconciliationId); command.Parameters.AddWithValue("@JobId",ids.NewId());
        command.Parameters.AddWithValue("@TenantId",payload.TenantId); command.Parameters.AddWithValue("@BusinessId",payload.BusinessId);
        command.Parameters.AddWithValue("@Payload",json); command.Parameters.Add("@Hash",SqlDbType.Binary,32).Value=hash;
        command.Parameters.AddWithValue("@At",payload.ReconciledAt); await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<WorkSessionPaymentTotal>> ReadClosurePaymentTotalsAsync(
        SqlConnection connection, Guid closureId, CancellationToken cancellationToken, SqlTransaction? transaction=null)
    {
        var result=new List<WorkSessionPaymentTotal>();
        await using var command=new SqlCommand("""
            SELECT total.PaymentMethodCode,total.SalesAmount,total.RefundAmount,total.OtherAmount,total.NetAmount,
              total.CountedAmount,total.Difference,CAST(CASE WHEN closureOption.OptionId IS NULL THEN 0 ELSE 1 END AS bit)
            FROM dbo.WorkSessionClosurePaymentTotals total
            LEFT JOIN reference.Options closureOption ON closureOption.CatalogCode=N'cash-closure-method' AND closureOption.Code=total.PaymentMethodCode AND closureOption.IsActive=1
            WHERE total.WorkSessionClosureId=@ClosureId ORDER BY COALESCE(closureOption.SortOrder,1000),total.PaymentMethodCode;
            """,connection,transaction);
        command.Parameters.AddWithValue("@ClosureId",closureId); await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken)) result.Add(new(reader.GetString(0),reader.GetDecimal(1),reader.GetDecimal(2),reader.GetDecimal(3),reader.GetDecimal(4),
            reader.IsDBNull(5)?null:reader.GetDecimal(5),reader.IsDBNull(6)?null:reader.GetDecimal(6),reader.GetBoolean(7)));
        return result;
    }

    private static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<WorkSessionPaymentTotal>>> ReadClosurePaymentTotalsAsync(
        SqlConnection connection, IReadOnlyList<Guid> closureIds, CancellationToken cancellationToken)
    {
        if (closureIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<WorkSessionPaymentTotal>>();
        var parameterNames = closureIds.Select((_, index) => $"@ClosureId{index}").ToArray();
        await using var command = new SqlCommand($"""
            SELECT total.WorkSessionClosureId,total.PaymentMethodCode,total.SalesAmount,total.RefundAmount,
              total.OtherAmount,total.NetAmount,total.CountedAmount,total.Difference,
              CAST(CASE WHEN closureOption.OptionId IS NULL THEN 0 ELSE 1 END AS bit),
              COALESCE(closureOption.SortOrder,1000)
            FROM dbo.WorkSessionClosurePaymentTotals total
            LEFT JOIN reference.Options closureOption ON closureOption.CatalogCode=N'cash-closure-method'
              AND closureOption.Code=total.PaymentMethodCode AND closureOption.IsActive=1
            WHERE total.WorkSessionClosureId IN ({string.Join(',', parameterNames)})
            ORDER BY total.WorkSessionClosureId,COALESCE(closureOption.SortOrder,1000),total.PaymentMethodCode;
            """, connection);
        for (var index = 0; index < closureIds.Count; index++)
            command.Parameters.AddWithValue(parameterNames[index], closureIds[index]);
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

    private static async Task<IReadOnlyList<WorkSessionPaymentVerificationItem>> ReadPaymentVerificationsAsync(
        SqlConnection connection, SqlTransaction? transaction, Guid closureId,
        CancellationToken cancellationToken)
    {
        var result = new List<WorkSessionPaymentVerificationItem>();
        await using var command = new SqlCommand("""
            WITH ClosureContext AS
            (
                SELECT session.WorkSessionId,session.UserId,session.OpenedAt,closure.ClosedAt
                FROM dbo.WorkSessionClosures closure
                INNER JOIN dbo.WorkSessions session ON session.WorkSessionId=closure.WorkSessionId
                WHERE closure.WorkSessionClosureId=@ClosureId
            ),
            VerificationMovements AS
            (
                SELECT CONCAT(N'Sale:',CONVERT(nvarchar(36),payment.DocumentId),N':',payment.PaymentNumber) VerificationKey,
                  COALESCE(mapping.ClosureMethodCode,payment.MethodCode) PaymentMethodCode,N'Sale' MovementType,
                  payment.DocumentId SourceId,document.DocumentNumber,payment.PaymentNumber SourceNumber,
                  payment.Amount,payment.Reference,payment.CardFranchiseCode,payment.ApprovalNumber,payment.RegisteredAt OccurredAt,
                  document.DocumentType SourceDocumentType
                FROM dbo.SalesPayments payment
                INNER JOIN dbo.SalesDocuments document ON document.DocumentId=payment.DocumentId
                INNER JOIN ClosureContext context ON context.WorkSessionId=document.WorkSessionId
                LEFT JOIN worksessions.CashClosurePaymentMethodMappings mapping ON mapping.PaymentMethodCode=payment.MethodCode
                UNION ALL
                SELECT CONCAT(N'Refund:',CONVERT(nvarchar(36),settlement.ReturnId),N':',settlement.SettlementNumber),
                  COALESCE(mapping.ClosureMethodCode,settlement.MethodCode),N'Refund',settlement.ReturnId,
                  saleReturn.DocumentNumber,settlement.SettlementNumber,-settlement.Amount,settlement.Reference,
                  settlement.CardFranchiseCode,settlement.ApprovalNumber,settlement.OccurredAt,
                  N'SalesReturn'
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
                  NULL,NULL,movement.OccurredAt,N'CashMovement'
                FROM dbo.WorkSessionMovements movement
                INNER JOIN ClosureContext context ON context.WorkSessionId=movement.WorkSessionId
                LEFT JOIN worksessions.CashClosurePaymentMethodMappings mapping ON mapping.PaymentMethodCode=movement.PaymentMethodCode
                WHERE movement.MovementType NOT IN(N'SalePayment',N'Refund')
            )
            SELECT movement.VerificationKey,movement.PaymentMethodCode,movement.MovementType,movement.SourceId,
              movement.DocumentNumber,movement.SourceNumber,movement.Amount,movement.Reference,
              movement.CardFranchiseCode,movement.ApprovalNumber,movement.OccurredAt,
              movement.SourceDocumentType,decision.Status
            FROM VerificationMovements movement
            INNER JOIN reference.Options closureOption ON closureOption.CatalogCode=N'cash-closure-method'
              AND closureOption.Code=movement.PaymentMethodCode AND closureOption.IsActive=1
            OUTER APPLY
            (
                SELECT TOP(1) JSON_VALUE(value.value,N'$.status') Status
                FROM dbo.WorkSessionClosureReconciliations reconciliation
                CROSS APPLY OPENJSON(reconciliation.SnapshotJson,N'$.paymentVerifications') value
                WHERE reconciliation.WorkSessionClosureId=@ClosureId
                  AND JSON_VALUE(value.value,N'$.verificationKey')=movement.VerificationKey
                ORDER BY reconciliation.ReconciledAt DESC
            ) decision
            ORDER BY closureOption.SortOrder,movement.OccurredAt,movement.VerificationKey;
            """, connection, transaction);
        command.Parameters.AddWithValue("@ClosureId", closureId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new WorkSessionPaymentVerificationItem(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetGuid(3),
                reader.GetString(4), reader.GetInt32(5), reader.GetDecimal(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9), reader.GetDateTimeOffset(10),
                reader.GetString(11), reader.IsDBNull(12) ? null : reader.GetString(12)));
        return result;
    }

    private static void AddClosureSearchParameters(SqlCommand command, WorkSessionIdentity identity,
        DateOnly from, DateOnly to, string? status)
    {
        command.Parameters.AddWithValue("@TenantId",identity.TenantId);
        command.Parameters.AddWithValue("@From",new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue),TimeSpan.Zero));
        command.Parameters.AddWithValue("@Until",new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue),TimeSpan.Zero));
        command.Parameters.AddWithValue("@Status",(object?)status ?? DBNull.Value);
    }
}
