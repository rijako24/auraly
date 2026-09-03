using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Returns;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Returns;
using Auraly.Domain.Returns;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlSalesReturnStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider) : ISalesReturnStore
{
    public async Task<SalesReturnAcceptance> AcceptAsync(
        SalesReturnUserIdentity user,
        string idempotencyKey,
        ConfirmSalesReturnRequest request,
        CancellationToken cancellationToken)
    {
        var requestHash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request));
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await AcceptAttemptAsync(
                    user, idempotencyKey, request, requestHash, cancellationToken);
            }
            catch (SqlException exception) when (exception.Number == 1205 && attempt < 4)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(25 * attempt), timeProvider, cancellationToken);
            }
        }
    }

    private async Task<SalesReturnAcceptance> AcceptAttemptAsync(
        SalesReturnUserIdentity user,
        string idempotencyKey,
        ConfirmSalesReturnRequest request,
        byte[] requestHash,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var replay = await TryReplayAsync(connection, transaction, user.BusinessId,
                request.ReturnId, idempotencyKey, requestHash, cancellationToken);
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }

            await using (var reason = new SqlCommand("""
                SELECT COUNT_BIG(*) FROM dbo.BusinessReasons WITH(UPDLOCK,HOLDLOCK)
                WHERE BusinessId=@BusinessId AND ReasonType=N'SalesReturn'
                  AND Code=@Code AND IsActive=1;
                """, connection, transaction))
            {
                reason.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                reason.Parameters.AddWithValue("@Code", request.ReasonCode);
                if (Convert.ToInt64(await reason.ExecuteScalarAsync(cancellationToken)) != 1)
                    throw new SalesReturnValidationException("The return reason is not active for this business.");
            }

            var original = await LoadOriginalAsync(
                connection, transaction, user, request, cancellationToken);
            var lines = new List<SalesReturnLineSnapshot>(request.Lines.Count);
            var lineNumber = 0;
            var coversEveryRequestedBalance = true;
            foreach (var requested in request.Lines.OrderBy(line => line.OriginalLineNumber))
            {
                var source = await LoadOriginalLineAsync(connection, transaction,
                    request.OriginalDocumentId, requested.OriginalLineNumber, cancellationToken);
                SalesReturnAmounts amounts;
                try
                {
                    amounts = SalesReturnAmountCalculator.Calculate(
                        source.Quantity, source.DiscountAmount, source.UntaxedAmount,
                        source.TaxAmount, source.LineTotal, source.ReturnedQuantity,
                        source.ReturnedDiscount, source.ReturnedUntaxed, source.ReturnedTax,
                        source.ReturnedTotal, requested.Quantity);
                }
                catch (ArgumentException exception)
                {
                    throw new SalesReturnConflictException(exception.Message);
                }
                coversEveryRequestedBalance &= requested.Quantity == source.Quantity - source.ReturnedQuantity;
                lines.Add(new SalesReturnLineSnapshot(
                    ++lineNumber, requested.OriginalLineNumber, source.ProductId,
                    source.Description, requested.Quantity, source.UnitPrice,
                    amounts.DiscountAmount, source.TaxCode, source.TaxRate,
                    amounts.UntaxedAmount, amounts.TaxAmount, amounts.LineTotal, source.RecognizedUnitCost,
                    requested.InventoryDisposition));
            }
            if (request.ReturnScopeCode == SalesReturnScopes.FullCancellation)
            {
                var availableLineCount = await CountAvailableLinesAsync(
                    connection, transaction, request.OriginalDocumentId, cancellationToken);
                if (!coversEveryRequestedBalance || lines.Count != availableLineCount)
                    throw new SalesReturnConflictException(
                        "La anulación total debe devolver el saldo completo de todas las líneas disponibles.");
            }
            var untaxed = lines.Sum(line => line.UntaxedAmount);
            var tax = lines.Sum(line => line.TaxAmount);
            var total = lines.Sum(line => line.LineTotal);
            if (total <= 0) throw new SalesReturnValidationException(
                "The return must have a positive economic value.");
            if (request.EconomicResolution == ReturnEconomicResolutions.CustomerCredit &&
                original.CustomerId is null)
                throw new SalesReturnValidationException(
                    "Customer credit requires an identified customer on the original sale.");
            if (request.EconomicResolution == ReturnEconomicResolutions.CustomerCredit &&
                (original.ReceivableOutstanding <= 0 || total > original.ReceivableOutstanding))
                throw new SalesReturnValidationException(
                    "El abono a cartera requiere saldo pendiente suficiente en la cuenta por cobrar de la venta.");

            var settlement = new RefundSettlementContext(null, null, null, null);
            if (request.EconomicResolution == ReturnEconomicResolutions.Refund)
            {
                settlement = await ValidateRefundAsync(
                    connection, transaction, user, request, total, cancellationToken);
                request = request with
                {
                    OriginalPaymentNumber = settlement.OriginalPaymentNumber,
                    BankAccountId = settlement.BankAccountId
                };
            }
            var number = await AllocateNumberAsync(
                connection, transaction, user.BusinessId, cancellationToken);
            var now = timeProvider.GetUtcNow();
            var sequence = await AllocateSequenceAsync(
                connection, transaction, user.BusinessId, now, cancellationToken);
            var payload = new SalesReturnDocumentPayload(
                user.TenantId, user.BusinessId, request.ReturnId, request.WarehouseId,
                request.OriginalDocumentId, user.UserId, number.FullNumber, number.SeriesId,
                number.Prefix, number.SeriesCode, number.Consecutive, request.ReturnedAt,
                request.EconomicResolution, request.RefundMethodCode, "1",
                request.ReasonDescription, original.CustomerId, original.CustomerIdentification,
                untaxed, tax, total, lines, request.WorkSessionId,
                request.OriginalPaymentNumber, request.ReasonCode, request.Notes,
                request.ReturnScopeCode, settlement.CardFranchiseCode,
                settlement.ApprovalNumber, request.BankAccountId,
                request.SettlementReference, request.SettlementNotes);
            var payloadJson = SalesReturnContractSerializer.Serialize(payload);
            var payloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson));
            var movementId = ids.NewId();
            await InsertReturnAsync(connection, transaction, request, user, number,
                original, settlement, requestHash, idempotencyKey, untaxed, tax, total, now,
                cancellationToken);
            await InsertLinesAsync(connection, transaction, request.ReturnId,
                request.OriginalDocumentId, lines, cancellationToken);
            await InsertJobAsync(connection, transaction, request.ReturnId,
                user.BusinessId, movementId, sequence, payloadJson, payloadHash, now,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new SalesReturnAcceptance(request.ReturnId, movementId,
                number.FullNumber, "Accepted", sequence, false);
        }
        catch (SalesReturnConflictException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            if (transaction.Connection is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            throw;
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new SalesReturnConflictException(
                "The return number, DocumentId or idempotency key is already in use.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<SalesReturnAcceptance?> TryReplayAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        Guid returnId, string idempotencyKey, byte[] requestHash,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT r.ReturnId,r.DocumentNumber,r.Status,r.PayloadHash,
                   j.ProcessingSequence,j.JobId
            FROM dbo.SalesReturns r WITH (UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.DocumentProcessingJobs j
              ON j.DocumentId=r.ReturnId AND j.DocumentType=N'SalesReturn'
            WHERE r.BusinessId=@BusinessId
              AND (r.ReturnId=@ReturnId OR r.IdempotencyKey=@IdempotencyKey);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@ReturnId", returnId);
        command.Parameters.AddWithValue("@IdempotencyKey", idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        if (!reader.GetFieldValue<byte[]>(3).AsSpan().SequenceEqual(requestHash))
            throw new SalesReturnConflictException(
                "The idempotency key or ReturnId was reused with another payload.");
        return new SalesReturnAcceptance(reader.GetGuid(0), reader.GetGuid(5),
            reader.GetString(1), reader.GetString(2), reader.GetInt64(4), true);
    }

    private static async Task<OriginalSale> LoadOriginalAsync(
        SqlConnection connection, SqlTransaction transaction,
        SalesReturnUserIdentity user, ConfirmSalesReturnRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WITH (HOLDLOCK)
                           WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
              THROW 51200,'The business is outside the authenticated tenant.',1;
            IF NOT EXISTS (SELECT 1 FROM dbo.Warehouses WITH (HOLDLOCK)
                           WHERE WarehouseId=@WarehouseId AND BusinessId=@BusinessId AND IsActive=1 AND UseForSales=1)
              THROW 51201,'Selecciona una bodega de venta válida para la devolución.',1;
            SELECT d.CustomerId,d.CustomerIdentification,
                   COALESCE((SELECT SUM(r.OutstandingAmount) FROM dbo.Receivables r
                     WHERE r.BusinessId=d.BusinessId AND r.SourceDocumentId=d.DocumentId
                       AND r.SourceDocumentType=N'SalesInvoice' AND r.Status IN(N'Open',N'PartiallyPaid')),0)
            FROM dbo.SalesDocuments d WITH (UPDLOCK,HOLDLOCK)
            WHERE d.DocumentId=@OriginalDocumentId AND d.BusinessId=@BusinessId
              AND d.DocumentType IN(N'SalesInvoice',N'SalesReceipt')
              AND d.ProcessingStatus=N'Completed';
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@WarehouseId", request.WarehouseId);
        command.Parameters.AddWithValue("@OriginalDocumentId", request.OriginalDocumentId);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new SalesReturnValidationException(
                    "The original completed invoice was not found in this business.");
            return new OriginalSale(
                reader.IsDBNull(0) ? null : reader.GetGuid(0), reader.GetString(1), reader.GetDecimal(2));
        }
        catch (SqlException exception) when (exception.Number is 51200 or 51201)
        {
            throw new SalesReturnValidationException(exception.Message);
        }
    }

    private static async Task<OriginalLine> LoadOriginalLineAsync(
        SqlConnection connection, SqlTransaction transaction,
        Guid originalDocumentId, int originalLineNumber,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT l.ProductId,l.Description,l.Quantity,l.UnitPrice,l.DiscountAmount,
                   l.TaxCode,l.TaxRate,l.UntaxedAmount,l.TaxAmount,l.LineTotal,
                   COALESCE(returned.Quantity,0),COALESCE(returned.DiscountAmount,0),
                   COALESCE(returned.UntaxedAmount,0),COALESCE(returned.TaxAmount,0),
                   COALESCE(returned.LineTotal,0),
                   COALESCE(l.UnitCostSnapshot,(SELECT TOP(1) m.RecognizedUnitCost
                     FROM dbo.InventoryMovements m
                     WHERE m.DocumentId=l.DocumentId AND m.LineNumber=l.LineNumber
                       AND m.DocumentType IN(N'SalesInvoice',N'SalesReceipt')
                       AND m.MovementType=N'Sale'),0)
            FROM dbo.SalesDocumentLines l WITH (UPDLOCK,HOLDLOCK)
            OUTER APPLY (SELECT SUM(r.Quantity) Quantity,
              SUM(r.DiscountAmount) DiscountAmount,SUM(r.UntaxedAmount) UntaxedAmount,
              SUM(r.TaxAmount) TaxAmount,SUM(r.LineTotal) LineTotal
              FROM dbo.SalesReturnLines r WITH (UPDLOCK,HOLDLOCK)
              WHERE r.OriginalDocumentId=l.DocumentId
                AND r.OriginalLineNumber=l.LineNumber) returned
            WHERE l.DocumentId=@DocumentId AND l.LineNumber=@LineNumber
            ;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", originalDocumentId);
        command.Parameters.AddWithValue("@LineNumber", originalLineNumber);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new SalesReturnValidationException(
                $"Original sale line {originalLineNumber} was not found.");
        return new OriginalLine(reader.GetGuid(0), reader.GetString(1), reader.GetDecimal(2),
            reader.GetDecimal(3), reader.GetDecimal(4), reader.GetString(5), reader.GetDecimal(6),
            reader.GetDecimal(7), reader.GetDecimal(8), reader.GetDecimal(9),
            reader.GetDecimal(10), reader.GetDecimal(11), reader.GetDecimal(12),
            reader.GetDecimal(13), reader.GetDecimal(14), reader.GetDecimal(15));
    }

    private static async Task<RefundSettlementContext> ValidateRefundAsync(
        SqlConnection connection, SqlTransaction transaction, SalesReturnUserIdentity user,
        ConfirmSalesReturnRequest request, decimal requestedAmount,
        CancellationToken cancellationToken)
    {
        if (request.RefundMethodCode == SalesReturnRefundMethods.Cash)
        {
            // Administrative cash refunds are accounted without belonging to a
            // cashier shift. Only the POS supplies a WorkSessionId and affects
            // that drawer's closure.
            if (request.WorkSessionId is null)
                return new(null, null, null, null);
            await using var session = new SqlCommand("""
                SELECT COUNT_BIG(*) FROM dbo.WorkSessions WITH(UPDLOCK,HOLDLOCK)
                WHERE WorkSessionId=@Id AND BusinessId=@BusinessId AND WarehouseId=@WarehouseId
                  AND TenantId=@TenantId AND UserId=@UserId AND Status=N'Open';
                """, connection, transaction);
            session.Parameters.AddWithValue("@Id", request.WorkSessionId.Value);
            session.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            session.Parameters.AddWithValue("@WarehouseId", request.WarehouseId);
            session.Parameters.AddWithValue("@UserId", user.UserId);
            session.Parameters.AddWithValue("@TenantId", user.TenantId);
            if (Convert.ToInt64(await session.ExecuteScalarAsync(cancellationToken)) != 1)
                throw new SalesReturnValidationException("La devolución en efectivo requiere la sesión de caja abierta del usuario.");
            return new(null, null, null, null);
        }

        if (request.RefundMethodCode == SalesReturnRefundMethods.Transfer)
        {
            await using var bank = new SqlCommand("""
                DECLARE @AccountingEnabled bit=CASE WHEN EXISTS(
                  SELECT 1 FROM dbo.AccountingTenantSettings settings
                  INNER JOIN dbo.Businesses business ON business.TenantId=settings.TenantId
                  WHERE business.BusinessId=@BusinessId AND settings.Status=N'Ready') THEN 1 ELSE 0 END;
                SELECT CASE
                  WHEN @AccountingEnabled=0 AND @BankAccountId IS NULL THEN 1
                  WHEN @AccountingEnabled=1 AND EXISTS(
                    SELECT 1 FROM accounting.BankAccounts b WITH(UPDLOCK,HOLDLOCK)
                    INNER JOIN dbo.Businesses business ON business.TenantId=b.TenantId
                    INNER JOIN dbo.AccountingAccounts account
                      ON account.AccountId=b.AccountingAccountId AND account.TenantId=b.TenantId
                    WHERE b.BankAccountId=@BankAccountId AND business.BusinessId=@BusinessId
                      AND b.IsActive=1 AND account.IsActive=1 AND account.AllowsPosting=1) THEN 1
                  ELSE 0 END;
                """, connection, transaction);
            bank.Parameters.AddWithValue("@BankAccountId", (object?)request.BankAccountId ?? DBNull.Value);
            bank.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            if (Convert.ToInt64(await bank.ExecuteScalarAsync(cancellationToken)) != 1)
                throw new SalesReturnValidationException("La contabilidad activa requiere una cuenta bancaria de salida válida.");
            return new(null, request.BankAccountId, null, null);
        }

        await using var card = new SqlCommand("""
            SELECT p.MethodCode,p.Amount-COALESCE(reversed.Amount,0),
                   p.CardFranchiseCode,p.ApprovalNumber
            FROM dbo.SalesPayments p WITH(UPDLOCK,HOLDLOCK)
            OUTER APPLY
            (
                SELECT SUM(s.Amount) Amount
                FROM dbo.SalesReturnSettlements s WITH(UPDLOCK,HOLDLOCK)
                WHERE s.OriginalDocumentId=p.DocumentId
                  AND s.OriginalPaymentNumber=p.PaymentNumber
            ) reversed
            WHERE p.DocumentId=@DocumentId AND p.PaymentNumber=@PaymentNumber;
            """, connection, transaction);
        card.Parameters.AddWithValue("@DocumentId", request.OriginalDocumentId);
        card.Parameters.AddWithValue("@PaymentNumber", request.OriginalPaymentNumber!.Value);
        await using var reader = await card.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) ||
            reader.GetString(0) != request.RefundMethodCode)
            throw new SalesReturnValidationException(
                "La reversión debe corresponder a un pago original de la misma tarjeta.");
        if (reader.GetDecimal(1) < requestedAmount)
            throw new SalesReturnValidationException(
                "El valor supera el saldo disponible del pago original para reversar.");
        return new(request.OriginalPaymentNumber, null,
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private static async Task<AuralyDocumentNumberAssignment> AllocateNumberAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP(1) ds.DocumentSeriesId,ds.Prefix,ds.SeriesCode,ds.Padding,
                   ds.RangeEnd,COALESCE(c.NextConsecutive,ds.RangeStart)
            FROM dbo.DocumentSeries ds WITH (UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.DocumentSeriesCursors c WITH (UPDLOCK,HOLDLOCK)
              ON c.DocumentSeriesId=ds.DocumentSeriesId
            WHERE ds.BusinessId=@BusinessId AND ds.DocumentType=N'SalesReturn'
              AND ds.DeviceId IS NULL AND ds.IsActive=1
            ORDER BY ds.DocumentSeriesId;
            """;
        Guid id; string prefix; string code; byte padding; long end; long consecutive;
        await using (var command = new SqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("@BusinessId", businessId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new SalesReturnValidationException(
                    "No active SalesReturn document series is configured for the business.");
            id=reader.GetGuid(0); prefix=reader.GetString(1); code=reader.GetString(2);
            padding=reader.GetByte(3); end=reader.GetInt64(4); consecutive=reader.GetInt64(5);
        }
        if (consecutive > end) throw new SalesReturnValidationException(
            "The SalesReturn document series is exhausted.");
        await using var update = new SqlCommand("""
            IF EXISTS (SELECT 1 FROM dbo.DocumentSeriesCursors WHERE DocumentSeriesId=@Id)
              UPDATE dbo.DocumentSeriesCursors SET NextConsecutive=@Next,UpdatedAt=@Now WHERE DocumentSeriesId=@Id;
            ELSE INSERT dbo.DocumentSeriesCursors(DocumentSeriesId,NextConsecutive,UpdatedAt)
              VALUES(@Id,@Next,@Now);
            """, connection, transaction);
        update.Parameters.AddWithValue("@Id", id);
        update.Parameters.AddWithValue("@Next", consecutive + 1);
        update.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
        await update.ExecuteNonQueryAsync(cancellationToken);
        return AuralyDocumentNumberAssignment.Create(id, AuralyDocumentTypes.SalesReturn,
            prefix, code, consecutive, padding);
    }

    private static async Task<long> AllocateSequenceAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            IF NOT EXISTS (SELECT 1 FROM dbo.BusinessProcessingCursors WITH (UPDLOCK,HOLDLOCK) WHERE BusinessId=@BusinessId)
              INSERT dbo.BusinessProcessingCursors(BusinessId,LastAssignedSequence,LastCompletedSequence,UpdatedAt)
                VALUES(@BusinessId,0,0,@Now);
            UPDATE dbo.BusinessProcessingCursors WITH (UPDLOCK,HOLDLOCK)
            SET LastAssignedSequence=LastAssignedSequence+1,UpdatedAt=@Now
            OUTPUT inserted.LastAssignedSequence WHERE BusinessId=@BusinessId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@Now", now);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task InsertReturnAsync(
        SqlConnection connection, SqlTransaction transaction, ConfirmSalesReturnRequest request,
        SalesReturnUserIdentity user, AuralyDocumentNumberAssignment number, OriginalSale original,
        RefundSettlementContext settlement, byte[] requestHash, string idempotencyKey,
        decimal untaxed, decimal tax, decimal total,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.SalesReturns
              (ReturnId,BusinessId,WarehouseId,WorkSessionId,OriginalDocumentId,DocumentSeriesId,DocumentNumber,
               DocumentPrefix,DocumentSeriesCode,DocumentConsecutive,IdempotencyKey,PayloadHash,
               ReturnedAt,ReturnScopeCode,EconomicResolution,RefundMethodCode,OriginalPaymentNumber,
               CardFranchiseCode,ApprovalNumber,BankAccountId,RefundReference,RefundNotes,CorrectionCode,
               ReasonCode,ReasonDescription,Notes,
               CustomerId,CustomerIdentification,UntaxedAmount,TaxAmount,TotalAmount,Status,
               CreatedByUserId,AcceptedAt)
            VALUES(@Id,@BusinessId,@WarehouseId,@WorkSessionId,@OriginalId,@SeriesId,@Number,@Prefix,@SeriesCode,
               @Consecutive,@Key,@Hash,@ReturnedAt,@ReturnScopeCode,@Resolution,@Method,@PaymentNumber,
               @CardFranchiseCode,@ApprovalNumber,@BankAccountId,@RefundReference,@RefundNotes,N'1',@ReasonCode,@Reason,@Notes,@CustomerId,
               @CustomerIdentification,@Untaxed,@Tax,@Total,N'Accepted',@UserId,@Now);
            """, connection, transaction);
        command.Parameters.AddWithValue("@Id", request.ReturnId);
        command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", request.WarehouseId);
        command.Parameters.AddWithValue("@WorkSessionId", (object?)request.WorkSessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("@OriginalId", request.OriginalDocumentId);
        command.Parameters.AddWithValue("@SeriesId", number.SeriesId);
        command.Parameters.AddWithValue("@Number", number.FullNumber);
        command.Parameters.AddWithValue("@Prefix", number.Prefix);
        command.Parameters.AddWithValue("@SeriesCode", number.SeriesCode);
        command.Parameters.AddWithValue("@Consecutive", number.Consecutive);
        command.Parameters.AddWithValue("@Key", idempotencyKey);
        command.Parameters.Add("@Hash", SqlDbType.Binary, 32).Value=requestHash;
        command.Parameters.AddWithValue("@ReturnedAt", request.ReturnedAt);
        command.Parameters.AddWithValue("@ReturnScopeCode", request.ReturnScopeCode);
        command.Parameters.AddWithValue("@Resolution", request.EconomicResolution);
        command.Parameters.AddWithValue("@Method", (object?)request.RefundMethodCode ?? DBNull.Value);
        command.Parameters.AddWithValue("@PaymentNumber", (object?)request.OriginalPaymentNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("@CardFranchiseCode", (object?)settlement.CardFranchiseCode ?? DBNull.Value);
        command.Parameters.AddWithValue("@ApprovalNumber", (object?)settlement.ApprovalNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("@BankAccountId", (object?)request.BankAccountId ?? DBNull.Value);
        command.Parameters.AddWithValue("@RefundReference", (object?)request.SettlementReference ?? DBNull.Value);
        command.Parameters.AddWithValue("@RefundNotes", (object?)request.SettlementNotes ?? DBNull.Value);
        command.Parameters.AddWithValue("@ReasonCode", request.ReasonCode);
        command.Parameters.AddWithValue("@Notes", (object?)request.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("@Reason", request.ReasonDescription);
        command.Parameters.AddWithValue("@CustomerId", (object?)original.CustomerId ?? DBNull.Value);
        command.Parameters.AddWithValue("@CustomerIdentification", original.CustomerIdentification);
        AddDecimal(command,"@Untaxed",untaxed,19,4); AddDecimal(command,"@Tax",tax,19,4);
        AddDecimal(command,"@Total",total,19,4);
        command.Parameters.AddWithValue("@UserId", user.UserId);
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> CountAvailableLinesAsync(
        SqlConnection connection, SqlTransaction transaction, Guid originalDocumentId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT COUNT(*)
            FROM dbo.SalesDocumentLines l WITH(UPDLOCK,HOLDLOCK)
            OUTER APPLY
            (
                SELECT COALESCE(SUM(rl.Quantity),0) AS ReturnedQuantity
                FROM dbo.SalesReturnLines rl WITH(UPDLOCK,HOLDLOCK)
                JOIN dbo.SalesReturns r WITH(UPDLOCK,HOLDLOCK) ON r.ReturnId=rl.ReturnId
                WHERE rl.OriginalDocumentId=l.DocumentId
                  AND rl.OriginalLineNumber=l.LineNumber
                  AND r.Status IN(N'Accepted',N'Processed')
            ) returned
            WHERE l.DocumentId=@DocumentId
              AND l.Quantity-returned.ReturnedQuantity>0;
            """, connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", originalDocumentId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task InsertLinesAsync(
        SqlConnection connection, SqlTransaction transaction, Guid returnId,
        Guid originalId, IEnumerable<SalesReturnLineSnapshot> lines,
        CancellationToken cancellationToken)
    {
        foreach (var line in lines)
        {
            await using var command = new SqlCommand("""
                INSERT dbo.SalesReturnLines
                  (ReturnId,OriginalDocumentId,LineNumber,OriginalLineNumber,ProductId,
                   DescriptionSnapshot,Quantity,UnitPrice,DiscountAmount,TaxCode,TaxRate,
                   UntaxedAmount,TaxAmount,LineTotal,RecognizedUnitCost,InventoryDisposition)
                VALUES(@ReturnId,@OriginalId,@Line,@OriginalLine,@ProductId,@Description,
                   @Quantity,@UnitPrice,@Discount,@TaxCode,@TaxRate,@Untaxed,@Tax,@Total,@Cost,@Disposition);
                """, connection, transaction);
            command.Parameters.AddWithValue("@ReturnId", returnId);
            command.Parameters.AddWithValue("@OriginalId", originalId);
            command.Parameters.AddWithValue("@Line", line.LineNumber);
            command.Parameters.AddWithValue("@OriginalLine", line.OriginalLineNumber);
            command.Parameters.AddWithValue("@ProductId", line.ProductId);
            command.Parameters.AddWithValue("@Description", line.Description);
            AddDecimal(command,"@Quantity",line.Quantity,19,6);
            AddDecimal(command,"@UnitPrice",line.UnitPrice,19,4);
            AddDecimal(command,"@Discount",line.DiscountAmount,19,4);
            command.Parameters.AddWithValue("@TaxCode", line.TaxCode);
            AddDecimal(command,"@TaxRate",line.TaxRate,9,6);
            AddDecimal(command,"@Untaxed",line.UntaxedAmount,19,4);
            AddDecimal(command,"@Tax",line.TaxAmount,19,4);
            AddDecimal(command,"@Total",line.LineTotal,19,4);
            AddDecimal(command,"@Cost",line.RecognizedUnitCost,19,6);
            command.Parameters.AddWithValue("@Disposition", line.InventoryDisposition);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertJobAsync(
        SqlConnection connection, SqlTransaction transaction, Guid returnId, Guid businessId,
        Guid movementId, long sequence, string payload, byte[] payloadHash, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.DocumentProcessingJobs
              (JobId,BusinessId,ProcessingSequence,DocumentId,DocumentType,Status,AvailableAt,CreatedAt)
            VALUES(@JobId,@BusinessId,@Sequence,@DocumentId,N'SalesReturn',N'Pending',@Now,@Now);
            INSERT dbo.DocumentProcessingPayloads
              (DocumentId,DocumentType,BusinessId,ContractVersion,PayloadJson,PayloadHash,AcceptedAt)
            VALUES(@DocumentId,N'SalesReturn',@BusinessId,1,@Payload,@PayloadHash,@Now);
            """, connection, transaction);
        command.Parameters.AddWithValue("@JobId", movementId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@Sequence", sequence);
        command.Parameters.AddWithValue("@DocumentId", returnId);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.AddWithValue("@Payload", payload);
        command.Parameters.Add("@PayloadHash",SqlDbType.Binary,32).Value=payloadHash;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddDecimal(SqlCommand command,string name,decimal value,byte precision,byte scale)
    {
        var parameter=command.Parameters.Add(name,SqlDbType.Decimal);
        parameter.Precision=precision; parameter.Scale=scale; parameter.Value=value;
    }

    private sealed record OriginalSale(Guid? CustomerId,string CustomerIdentification,decimal ReceivableOutstanding);
    private sealed record RefundSettlementContext(
        int? OriginalPaymentNumber,
        Guid? BankAccountId,
        string? CardFranchiseCode,
        string? ApprovalNumber);
    private sealed record OriginalLine(Guid ProductId,string Description,decimal Quantity,
        decimal UnitPrice,decimal DiscountAmount,string TaxCode,decimal TaxRate,
        decimal UntaxedAmount,decimal TaxAmount,decimal LineTotal,decimal ReturnedQuantity,
        decimal ReturnedDiscount,decimal ReturnedUntaxed,decimal ReturnedTax,decimal ReturnedTotal,
        decimal RecognizedUnitCost);
}
