using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Payables;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Payables;
using Auraly.Domain.Payables;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlPayablesStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider) : IPayablesStore
{
    public async Task<PayablePage> ListAsync(
        PayablesUserIdentity user,
        PayableQuery query,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        const string filters = """
            p.BusinessId=@BusinessId
            AND b.TenantId=@TenantId
            AND (@SupplierId IS NULL OR p.SupplierId=@SupplierId)
            AND (@Status IS NULL OR p.Status=@Status)
            AND (@Overdue IS NULL OR
                 (@Overdue=1 AND p.OutstandingAmount>0 AND p.DueDate<@Now) OR
                 (@Overdue=0 AND (p.OutstandingAmount=0 OR p.DueDate>=@Now)))
            AND (@Search IS NULL OR p.DocumentNumber LIKE N'%' + @Search + N'%'
                 OR s.Name LIKE N'%' + @Search + N'%'
                 OR s.Identification LIKE N'%' + @Search + N'%')
            """;
        var countSql = $"""
            SELECT COUNT(*),COALESCE(SUM(p.OutstandingAmount),0),
                   COALESCE(SUM(CASE WHEN p.OutstandingAmount>0 AND p.DueDate<@Now
                                     THEN p.OutstandingAmount ELSE 0 END),0)
            FROM dbo.Payables p
            INNER JOIN dbo.Businesses b ON b.BusinessId=p.BusinessId
            INNER JOIN dbo.Suppliers s ON s.SupplierId=p.SupplierId
            WHERE {filters};
            """;
        int totalCount;
        decimal totalOutstanding;
        decimal totalOverdue;
        await using (var command = new SqlCommand(countSql, connection))
        {
            AddQueryParameters(command, user, query, timeProvider.GetUtcNow());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            totalCount = reader.GetInt32(0);
            totalOutstanding = reader.GetDecimal(1);
            totalOverdue = reader.GetDecimal(2);
        }

        var dataSql = $"""
            SELECT p.PayableId,p.SupplierId,s.Name,p.DocumentNumber,p.CurrencyCode,
                   p.OriginalAmount,p.OutstandingAmount,p.DueDate,p.Status,p.CreatedAt,
                   CASE WHEN p.OutstandingAmount>0 AND p.DueDate<@Now THEN CAST(1 AS BIT)
                        ELSE CAST(0 AS BIT) END
            FROM dbo.Payables p
            INNER JOIN dbo.Businesses b ON b.BusinessId=p.BusinessId
            INNER JOIN dbo.Suppliers s ON s.SupplierId=p.SupplierId
            WHERE {filters}
            ORDER BY CASE WHEN p.OutstandingAmount>0 AND p.DueDate<@Now THEN 0 ELSE 1 END,
                     p.DueDate,p.PayableId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        var items = new List<PayableListItem>();
        await using (var command = new SqlCommand(dataSql, connection))
        {
            AddQueryParameters(command, user, query, timeProvider.GetUtcNow());
            command.Parameters.AddWithValue("@Offset", (query.Page - 1) * query.PageSize);
            command.Parameters.AddWithValue("@PageSize", query.PageSize);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                items.Add(new PayableListItem(
                    reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4), reader.GetDecimal(5),
                    reader.GetDecimal(6), reader.GetDateTimeOffset(7), reader.GetString(8),
                    reader.GetBoolean(10), reader.GetDateTimeOffset(9)));
        }
        return new PayablePage(
            items, query.Page, query.PageSize, totalCount, totalOutstanding, totalOverdue);
    }

    public async Task<PayableDetail?> GetAsync(
        PayablesUserIdentity user,
        Guid payableId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        const string headerSql = """
            SELECT p.PayableId,p.SupplierId,s.Name,s.Identification,p.SourceDocumentId,
                   p.SourceDocumentType,p.DocumentNumber,p.CurrencyCode,p.OriginalAmount,
                   p.OutstandingAmount,p.DueDate,p.Status
            FROM dbo.Payables p
            INNER JOIN dbo.Suppliers s ON s.SupplierId=p.SupplierId
            INNER JOIN dbo.Businesses b ON b.BusinessId=p.BusinessId
            WHERE p.PayableId=@PayableId AND p.BusinessId=@BusinessId AND b.TenantId=@TenantId;
            """;
        Guid supplierId;
        string supplierName;
        string supplierIdentification;
        Guid sourceDocumentId;
        string sourceDocumentType;
        string documentNumber;
        string currency;
        decimal original;
        decimal outstanding;
        DateTimeOffset dueDate;
        string status;
        await using (var command = new SqlCommand(headerSql, connection))
        {
            command.Parameters.AddWithValue("@PayableId", payableId);
            command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            command.Parameters.AddWithValue("@TenantId", user.TenantId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            supplierId = reader.GetGuid(1);
            supplierName = reader.GetString(2);
            supplierIdentification = reader.GetString(3);
            sourceDocumentId = reader.GetGuid(4);
            sourceDocumentType = reader.GetString(5);
            documentNumber = reader.GetString(6);
            currency = reader.GetString(7);
            original = reader.GetDecimal(8);
            outstanding = reader.GetDecimal(9);
            dueDate = reader.GetDateTimeOffset(10);
            status = reader.GetString(11);
        }
        var transactions = new List<PayableTransactionView>();
        await using (var command = new SqlCommand("""
            SELECT PayableTransactionId,TransactionType,Amount,SourceDocumentId,OccurredAt
            FROM dbo.PayableTransactions
            WHERE PayableId=@PayableId ORDER BY OccurredAt,PayableTransactionId;
            """, connection))
        {
            command.Parameters.AddWithValue("@PayableId", payableId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                transactions.Add(new PayableTransactionView(
                    reader.GetGuid(0), reader.GetString(1), reader.GetDecimal(2),
                    reader.GetGuid(3), reader.GetDateTimeOffset(4)));
        }
        return new PayableDetail(
            payableId, supplierId, supplierName, supplierIdentification,
            sourceDocumentId, sourceDocumentType, documentNumber, currency,
            original, outstanding, dueDate, status, transactions);
    }

    public async Task<SupplierPaymentAcceptance> AcceptPaymentAsync(
        PayablesUserIdentity user,
        string idempotencyKey,
        ConfirmSupplierPaymentRequest request,
        PayableSettlement settlement,
        CancellationToken cancellationToken)
    {
        var requestHash = HashRequest(request, settlement);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await AcceptPaymentAttemptAsync(
                    user, idempotencyKey, request, settlement, requestHash,
                    cancellationToken);
            }
            catch (SqlException exception) when (exception.Number == 1205 && attempt < 3)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(25 * attempt), timeProvider, cancellationToken);
            }
        }
    }

    private async Task<SupplierPaymentAcceptance> AcceptPaymentAttemptAsync(
        PayablesUserIdentity user,
        string idempotencyKey,
        ConfirmSupplierPaymentRequest request,
        PayableSettlement settlement,
        byte[] requestHash,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var replay = await FindReplayAsync(
                connection, transaction, user.BusinessId, request.PaymentId,
                idempotencyKey, requestHash, cancellationToken);
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }
            await ValidateScopeAndAvailabilityAsync(
                connection, transaction, user, request, settlement, cancellationToken);
            var number = await AllocateNumberAsync(
                connection, transaction, user.BusinessId, cancellationToken);
            var now = timeProvider.GetUtcNow();
            var sequence = await AllocateProcessingSequenceAsync(
                connection, transaction, user.BusinessId, now, cancellationToken);
            var movementId = ids.NewId();
            var payload = new SupplierPaymentDocumentPayload(
                user.TenantId, user.BusinessId, request.PaymentId, request.SupplierId,
                user.UserId, number.FullNumber, number.SeriesId, number.Prefix,
                number.SeriesCode, number.Consecutive, request.PaidAt, request.CurrencyCode,
                request.PaymentMethod, request.Reference, request.Notes,
                settlement.TotalAmount,
                settlement.Allocations.Select((item, index) =>
                    new SupplierPaymentAllocationSnapshot(index + 1, item.PayableId, item.Amount))
                    .ToArray());
            var payloadJson = SupplierPaymentContractSerializer.Serialize(payload);
            await InsertPaymentAsync(
                connection, transaction, user, request, settlement, number,
                idempotencyKey, requestHash, now, cancellationToken);
            await InsertApplicationsAsync(
                connection, transaction, request.PaymentId, settlement, cancellationToken);
            await InsertJobAsync(
                connection, transaction, user.BusinessId, request.PaymentId, movementId,
                sequence, payloadJson, SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)),
                now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new SupplierPaymentAcceptance(
                request.PaymentId, movementId, number.FullNumber, "Accepted", sequence, false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static void AddQueryParameters(
        SqlCommand command, PayablesUserIdentity user, PayableQuery query, DateTimeOffset now)
    {
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@SupplierId", (object?)query.SupplierId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Status", (object?)query.Status ?? DBNull.Value);
        command.Parameters.AddWithValue("@Overdue", (object?)query.Overdue ?? DBNull.Value);
        command.Parameters.AddWithValue("@Search", (object?)query.Search ?? DBNull.Value);
        command.Parameters.AddWithValue("@Now", now);
    }

    private static async Task<SupplierPaymentAcceptance?> FindReplayAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        Guid paymentId, string idempotencyKey, byte[] requestHash,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT p.PaymentId,p.DocumentNumber,p.Status,j.ProcessingSequence,j.JobId,p.PayloadHash
            FROM dbo.SupplierPayments p
            INNER JOIN dbo.DocumentProcessingJobs j
              ON j.DocumentId=p.PaymentId AND j.DocumentType=N'PayablePayment'
            WHERE p.BusinessId=@BusinessId
              AND (p.PaymentId=@PaymentId OR p.IdempotencyKey=@IdempotencyKey);
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@PaymentId", paymentId);
        command.Parameters.AddWithValue("@IdempotencyKey", idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        if (!reader.GetFieldValue<byte[]>(5).AsSpan().SequenceEqual(requestHash))
            throw new PayablesConflictException(
                "The idempotency key or PaymentId was reused with another payload.");
        return new SupplierPaymentAcceptance(
            reader.GetGuid(0), reader.GetGuid(4), reader.GetString(1), reader.GetString(2),
            reader.GetInt64(3), true);
    }

    private static async Task ValidateScopeAndAvailabilityAsync(
        SqlConnection connection, SqlTransaction transaction, PayablesUserIdentity user,
        ConfirmSupplierPaymentRequest request, PayableSettlement settlement,
        CancellationToken cancellationToken)
    {
        await using (var scope = new SqlCommand("""
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
              THROW 51200,'The business is outside the authenticated tenant.',1;
            IF NOT EXISTS(SELECT 1 FROM dbo.Suppliers WHERE SupplierId=@SupplierId AND BusinessId=@BusinessId AND IsActive=1)
              THROW 51201,'The supplier is outside the authenticated business.',1;
            """, connection, transaction))
        {
            scope.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            scope.Parameters.AddWithValue("@TenantId", user.TenantId);
            scope.Parameters.AddWithValue("@SupplierId", request.SupplierId);
            try { await scope.ExecuteNonQueryAsync(cancellationToken); }
            catch (SqlException exception) when (exception.Number is 51200 or 51201)
            { throw new PayablesValidationException(exception.Message); }
        }
        foreach (var allocation in settlement.Allocations.OrderBy(item => item.PayableId))
        {
            await using var command = new SqlCommand("""
                SELECT p.SupplierId,p.CurrencyCode,p.OutstandingAmount,p.Status,
                       COALESCE(SUM(CASE WHEN sp.Status=N'Accepted' THEN a.Amount ELSE 0 END),0)
                FROM dbo.Payables p WITH(UPDLOCK,HOLDLOCK)
                LEFT JOIN dbo.SupplierPaymentApplications a WITH(UPDLOCK,HOLDLOCK)
                  ON a.PayableId=p.PayableId AND a.AppliedAt IS NULL
                LEFT JOIN dbo.SupplierPayments sp WITH(UPDLOCK,HOLDLOCK)
                  ON sp.PaymentId=a.PaymentId
                WHERE p.PayableId=@PayableId AND p.BusinessId=@BusinessId
                GROUP BY p.SupplierId,p.CurrencyCode,p.OutstandingAmount,p.Status;
                """, connection, transaction);
            command.Parameters.AddWithValue("@PayableId", allocation.PayableId);
            command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new PayablesValidationException("An allocation references an unknown payable.");
            if (reader.GetGuid(0) != request.SupplierId)
                throw new PayablesValidationException("All obligations must belong to the selected supplier.");
            if (!string.Equals(reader.GetString(1), request.CurrencyCode, StringComparison.Ordinal))
                throw new PayablesValidationException("All obligations must use the payment currency.");
            if (reader.GetString(3) is "Paid" or "Cancelled")
                throw new PayablesValidationException("A paid or cancelled obligation cannot receive a payment.");
            var available = reader.GetDecimal(2) - reader.GetDecimal(4);
            if (allocation.Amount > available)
                throw new PayablesConflictException(
                    "The allocation exceeds the unreserved outstanding balance.");
        }
    }

    private static async Task<AuralyDocumentNumberAssignment> AllocateNumberAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        CancellationToken cancellationToken)
    {
        await using var select = new SqlCommand("""
            SELECT TOP(1) ds.DocumentSeriesId,ds.Prefix,ds.SeriesCode,ds.Padding,
                   ds.RangeEnd,COALESCE(c.NextConsecutive,ds.RangeStart)
            FROM dbo.DocumentSeries ds WITH(UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.DocumentSeriesCursors c WITH(UPDLOCK,HOLDLOCK)
              ON c.DocumentSeriesId=ds.DocumentSeriesId
            WHERE ds.BusinessId=@BusinessId AND ds.DocumentType=N'PayablePayment'
              AND ds.DeviceId IS NULL AND ds.IsActive=1
            ORDER BY ds.DocumentSeriesId;
            """, connection, transaction);
        select.Parameters.AddWithValue("@BusinessId", businessId);
        Guid seriesId; string prefix; string seriesCode; byte padding; long rangeEnd; long consecutive;
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
                throw new PayablesValidationException(
                    "No active PayablePayment document series is configured for the business.");
            seriesId = reader.GetGuid(0); prefix = reader.GetString(1);
            seriesCode = reader.GetString(2); padding = reader.GetByte(3);
            rangeEnd = reader.GetInt64(4); consecutive = reader.GetInt64(5);
        }
        if (consecutive > rangeEnd)
            throw new PayablesValidationException("The PayablePayment document series is exhausted.");
        await using var update = new SqlCommand("""
            IF EXISTS(SELECT 1 FROM dbo.DocumentSeriesCursors WHERE DocumentSeriesId=@SeriesId)
              UPDATE dbo.DocumentSeriesCursors SET NextConsecutive=@Next,UpdatedAt=@Now WHERE DocumentSeriesId=@SeriesId;
            ELSE INSERT dbo.DocumentSeriesCursors(DocumentSeriesId,NextConsecutive,UpdatedAt)
              VALUES(@SeriesId,@Next,@Now);
            """, connection, transaction);
        update.Parameters.AddWithValue("@SeriesId", seriesId);
        update.Parameters.AddWithValue("@Next", consecutive + 1);
        update.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
        await update.ExecuteNonQueryAsync(cancellationToken);
        return AuralyDocumentNumberAssignment.Create(
            seriesId, AuralyDocumentTypes.PayablePayment, prefix, seriesCode, consecutive, padding);
    }

    private static async Task<long> AllocateProcessingSequenceAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            IF NOT EXISTS(SELECT 1 FROM dbo.BusinessProcessingCursors WITH(UPDLOCK,HOLDLOCK) WHERE BusinessId=@BusinessId)
              INSERT dbo.BusinessProcessingCursors(BusinessId,LastAssignedSequence,LastCompletedSequence,UpdatedAt)
              VALUES(@BusinessId,0,0,@Now);
            UPDATE dbo.BusinessProcessingCursors WITH(UPDLOCK,HOLDLOCK)
            SET LastAssignedSequence=LastAssignedSequence+1,UpdatedAt=@Now
            OUTPUT inserted.LastAssignedSequence WHERE BusinessId=@BusinessId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@Now", now);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task InsertPaymentAsync(
        SqlConnection connection, SqlTransaction transaction, PayablesUserIdentity user,
        ConfirmSupplierPaymentRequest request, PayableSettlement settlement,
        AuralyDocumentNumberAssignment number, string idempotencyKey, byte[] requestHash,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.SupplierPayments
              (PaymentId,BusinessId,SupplierId,DocumentSeriesId,DocumentNumber,
               DocumentPrefix,DocumentSeriesCode,DocumentConsecutive,IdempotencyKey,
               PayloadHash,PaidAt,CurrencyCode,PaymentMethod,Reference,Notes,TotalAmount,
               Status,ConfirmedByUserId,AcceptedAt)
            VALUES(@Id,@BusinessId,@SupplierId,@SeriesId,@Number,@Prefix,@SeriesCode,
               @Consecutive,@Key,@Hash,@PaidAt,@Currency,@Method,@Reference,@Notes,@Total,
               N'Accepted',@UserId,@Now);
            """, connection, transaction);
        command.Parameters.AddWithValue("@Id", request.PaymentId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@SupplierId", request.SupplierId);
        command.Parameters.AddWithValue("@SeriesId", number.SeriesId);
        command.Parameters.AddWithValue("@Number", number.FullNumber);
        command.Parameters.AddWithValue("@Prefix", number.Prefix);
        command.Parameters.AddWithValue("@SeriesCode", number.SeriesCode);
        command.Parameters.AddWithValue("@Consecutive", number.Consecutive);
        command.Parameters.AddWithValue("@Key", idempotencyKey);
        command.Parameters.Add("@Hash", SqlDbType.Binary, 32).Value = requestHash;
        command.Parameters.AddWithValue("@PaidAt", request.PaidAt);
        command.Parameters.AddWithValue("@Currency", request.CurrencyCode);
        command.Parameters.AddWithValue("@Method", request.PaymentMethod);
        command.Parameters.AddWithValue("@Reference", (object?)request.Reference ?? DBNull.Value);
        command.Parameters.AddWithValue("@Notes", (object?)request.Notes ?? DBNull.Value);
        AddMoney(command, "@Total", settlement.TotalAmount);
        command.Parameters.AddWithValue("@UserId", user.UserId);
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertApplicationsAsync(
        SqlConnection connection, SqlTransaction transaction, Guid paymentId,
        PayableSettlement settlement, CancellationToken cancellationToken)
    {
        for (var index = 0; index < settlement.Allocations.Count; index++)
        {
            await using var command = new SqlCommand("""
                INSERT dbo.SupplierPaymentApplications(PaymentId,LineNumber,PayableId,Amount)
                VALUES(@PaymentId,@Line,@PayableId,@Amount);
                """, connection, transaction);
            command.Parameters.AddWithValue("@PaymentId", paymentId);
            command.Parameters.AddWithValue("@Line", index + 1);
            command.Parameters.AddWithValue("@PayableId", settlement.Allocations[index].PayableId);
            AddMoney(command, "@Amount", settlement.Allocations[index].Amount);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertJobAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        Guid paymentId, Guid movementId, long sequence, string payload, byte[] payloadHash,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.DocumentProcessingJobs
              (JobId,BusinessId,ProcessingSequence,DocumentId,DocumentType,Status,AvailableAt,CreatedAt)
            VALUES(@JobId,@BusinessId,@Sequence,@DocumentId,N'PayablePayment',N'Pending',@Now,@Now);
            INSERT dbo.DocumentProcessingPayloads
              (DocumentId,DocumentType,BusinessId,ContractVersion,PayloadJson,PayloadHash,AcceptedAt)
            VALUES(@DocumentId,N'PayablePayment',@BusinessId,1,@Payload,@Hash,@Now);
            """, connection, transaction);
        command.Parameters.AddWithValue("@JobId", movementId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@Sequence", sequence);
        command.Parameters.AddWithValue("@DocumentId", paymentId);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.AddWithValue("@Payload", payload);
        command.Parameters.Add("@Hash", SqlDbType.Binary, 32).Value = payloadHash;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static byte[] HashRequest(
        ConfirmSupplierPaymentRequest request, PayableSettlement settlement) =>
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            request.PaymentId, request.BusinessId, request.SupplierId, request.PaidAt,
            Currency = request.CurrencyCode, request.PaymentMethod, request.Reference,
            request.Notes, settlement.TotalAmount, settlement.Allocations
        }));

    private static void AddMoney(SqlCommand command, string name, decimal value)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = 19; parameter.Scale = 4; parameter.Value = value;
    }
}
