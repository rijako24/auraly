using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Returns;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Returns;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlSalesDebitNoteStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider) : ISalesDebitNoteStore
{
    public async Task<SalesDebitNoteAcceptance> AcceptAsync(
        SalesReturnUserIdentity user,
        string idempotencyKey,
        ConfirmSalesDebitNoteRequest request,
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
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), timeProvider, cancellationToken);
            }
        }
    }

    private async Task<SalesDebitNoteAcceptance> AcceptAttemptAsync(
        SalesReturnUserIdentity user,
        string idempotencyKey,
        ConfirmSalesDebitNoteRequest request,
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
                request.DebitNoteId, idempotencyKey, requestHash, cancellationToken);
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }

            var original = await LoadOriginalAsync(
                connection, transaction, user, request.OriginalDocumentId, cancellationToken);
            var lines = request.Lines.Select((line, index) =>
            {
                var untaxed = decimal.Round(line.Quantity * line.UnitPrice, 4, MidpointRounding.AwayFromZero);
                var tax = decimal.Round(untaxed * line.TaxRate / 100m, 4, MidpointRounding.AwayFromZero);
                return new SalesDebitNoteLineSnapshot(
                    index + 1, line.Description, line.Quantity, line.UnitPrice,
                    line.TaxCode, line.TaxRate, untaxed, tax, untaxed + tax);
            }).ToArray();
            var untaxedAmount = lines.Sum(line => line.UntaxedAmount);
            var taxAmount = lines.Sum(line => line.TaxAmount);
            var totalAmount = lines.Sum(line => line.LineTotal);
            if (totalAmount <= 0)
                throw new SalesReturnValidationException("The debit note must have a positive total.");

            var number = await AllocateNumberAsync(connection, transaction, user.BusinessId, cancellationToken);
            var now = timeProvider.GetUtcNow();
            var sequence = await AllocateSequenceAsync(connection, transaction, user.BusinessId, now, cancellationToken);
            var payload = new SalesDebitNoteDocumentPayload(
                user.TenantId, user.BusinessId, request.DebitNoteId,
                request.OriginalDocumentId, user.UserId, number.FullNumber, number.SeriesId,
                number.Prefix, number.SeriesCode, number.Consecutive, request.IssuedAt,
                request.DueAt, request.ConceptCode, request.ReasonDescription,
                original.CustomerId, original.CustomerIdentification,
                untaxedAmount, taxAmount, totalAmount, lines, request.Notes);
            var payloadJson = SalesDebitNoteContractSerializer.Serialize(payload);
            var payloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson));
            var jobId = ids.NewId();

            await InsertHeaderAsync(connection, transaction, user, request, idempotencyKey,
                requestHash, number, original, untaxedAmount, taxAmount, totalAmount, now,
                cancellationToken);
            await InsertLinesAsync(connection, transaction, request.DebitNoteId, lines, cancellationToken);
            await InsertJobAsync(connection, transaction, request.DebitNoteId, user.BusinessId,
                jobId, sequence, payloadJson, payloadHash, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new SalesDebitNoteAcceptance(request.DebitNoteId, jobId,
                number.FullNumber, "Accepted", sequence, false);
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            if (transaction.Connection is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new SalesReturnConflictException(
                "The debit-note number, identifier or idempotency key is already in use.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<SalesDebitNotePage> ListAsync(
        SalesReturnUserIdentity user,
        SalesDebitNoteQuery query,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            SELECT n.DebitNoteId,n.OriginalDocumentId,n.DocumentNumber,d.DocumentNumber,
                   COALESCE(NULLIF(p.DisplayName,N''),NULLIF(p.LegalName,N''),
                            NULLIF(LTRIM(RTRIM(CONCAT(p.FirstName,N' ',p.LastName))),N''),
                            n.CustomerIdentification),
                   n.CustomerIdentification,n.IssuedAt,n.ConceptCode,n.ReasonDescription,
                   n.TotalAmount,n.Status,COALESCE(n.FiscalStatus,N'PendingGeneration'),fd.UniqueCode,
                   COUNT_BIG(*) OVER()
            FROM dbo.SalesDebitNotes n
            INNER JOIN dbo.Businesses b ON b.BusinessId=n.BusinessId AND b.TenantId=@TenantId
            INNER JOIN dbo.SalesDocuments d ON d.DocumentId=n.OriginalDocumentId
            INNER JOIN dbo.Customers c ON c.CustomerId=n.CustomerId
            INNER JOIN dbo.Parties p ON p.PartyId=c.PartyId AND p.TenantId=@TenantId
            LEFT JOIN dbo.FiscalDocuments fd ON fd.DocumentId=n.DebitNoteId
            WHERE n.BusinessId=@BusinessId
              AND (@Search IS NULL OR n.DocumentNumber LIKE N'%'+@Search+N'%'
                   OR d.DocumentNumber LIKE N'%'+@Search+N'%'
                   OR n.CustomerIdentification LIKE N'%'+@Search+N'%'
                   OR p.DisplayName LIKE N'%'+@Search+N'%' OR p.LegalName LIKE N'%'+@Search+N'%'
                   OR p.FirstName LIKE N'%'+@Search+N'%' OR p.LastName LIKE N'%'+@Search+N'%')
              AND (@From IS NULL OR n.IssuedAt>=@From)
              AND (@ToExclusive IS NULL OR n.IssuedAt<@ToExclusive)
            ORDER BY n.IssuedAt DESC,n.DebitNoteId DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        await using var command = new SqlCommand(sql, connection);
        Scope(command, user);
        command.Parameters.AddWithValue("@Search", Db(string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim()));
        command.Parameters.AddWithValue("@From", Db(query.From?.ToDateTime(TimeOnly.MinValue)));
        command.Parameters.AddWithValue("@ToExclusive", Db(query.To?.AddDays(1).ToDateTime(TimeOnly.MinValue)));
        command.Parameters.AddWithValue("@Offset", (query.Page - 1) * query.PageSize);
        command.Parameters.AddWithValue("@PageSize", query.PageSize);
        var items = new List<SalesDebitNoteListItem>();
        long total = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            total = reader.GetInt64(13);
            items.Add(ReadHeader(reader));
        }
        return new SalesDebitNotePage(items, query.Page, query.PageSize, total,
            total == 0 ? 0 : (int)Math.Ceiling(total / (double)query.PageSize));
    }

    public async Task<SalesDebitNoteDetail?> GetAsync(
        SalesReturnUserIdentity user,
        Guid debitNoteId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        const string headerSql = """
            SELECT n.DebitNoteId,n.OriginalDocumentId,n.DocumentNumber,d.DocumentNumber,
                   COALESCE(NULLIF(p.DisplayName,N''),NULLIF(p.LegalName,N''),
                            NULLIF(LTRIM(RTRIM(CONCAT(p.FirstName,N' ',p.LastName))),N''),
                            n.CustomerIdentification),
                   n.CustomerIdentification,n.IssuedAt,n.ConceptCode,n.ReasonDescription,
                   n.TotalAmount,n.Status,COALESCE(n.FiscalStatus,N'PendingGeneration'),fd.UniqueCode,
                   n.DueAt,n.UntaxedAmount,n.TaxAmount,n.Notes
            FROM dbo.SalesDebitNotes n
            INNER JOIN dbo.Businesses b ON b.BusinessId=n.BusinessId AND b.TenantId=@TenantId
            INNER JOIN dbo.SalesDocuments d ON d.DocumentId=n.OriginalDocumentId
            INNER JOIN dbo.Customers c ON c.CustomerId=n.CustomerId
            INNER JOIN dbo.Parties p ON p.PartyId=c.PartyId AND p.TenantId=@TenantId
            LEFT JOIN dbo.FiscalDocuments fd ON fd.DocumentId=n.DebitNoteId
            WHERE n.BusinessId=@BusinessId AND n.DebitNoteId=@Id;
            """;
        SalesDebitNoteListItem header;
        DateTimeOffset dueAt;
        decimal untaxed;
        decimal tax;
        string? notes;
        await using (var command = new SqlCommand(headerSql, connection))
        {
            Scope(command, user);
            command.Parameters.AddWithValue("@Id", debitNoteId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            header = ReadHeader(reader);
            dueAt = reader.GetDateTimeOffset(13);
            untaxed = reader.GetDecimal(14);
            tax = reader.GetDecimal(15);
            notes = reader.IsDBNull(16) ? null : reader.GetString(16);
        }
        var lines = new List<SalesDebitNoteLineSnapshot>();
        await using (var command = new SqlCommand("""
            SELECT LineNumber,DescriptionSnapshot,Quantity,UnitPrice,TaxCode,TaxRate,
                   UntaxedAmount,TaxAmount,LineTotal
            FROM dbo.SalesDebitNoteLines WHERE DebitNoteId=@Id ORDER BY LineNumber;
            """, connection))
        {
            command.Parameters.AddWithValue("@Id", debitNoteId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                lines.Add(new SalesDebitNoteLineSnapshot(
                    reader.GetInt32(0), reader.GetString(1), reader.GetDecimal(2),
                    reader.GetDecimal(3), reader.GetString(4), reader.GetDecimal(5),
                    reader.GetDecimal(6), reader.GetDecimal(7), reader.GetDecimal(8)));
        }
        return new SalesDebitNoteDetail(header, dueAt, untaxed, tax, notes, lines);
    }

    private static async Task<SalesDebitNoteAcceptance?> TryReplayAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        Guid debitNoteId, string idempotencyKey, byte[] requestHash,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT n.DebitNoteId,n.DocumentNumber,n.Status,n.PayloadHash,j.ProcessingSequence,j.JobId
            FROM dbo.SalesDebitNotes n WITH(UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.DocumentProcessingJobs j
              ON j.DocumentId=n.DebitNoteId AND j.DocumentType=N'SalesDebitNote'
            WHERE n.BusinessId=@BusinessId
              AND (n.DebitNoteId=@Id OR n.IdempotencyKey=@Key);
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@Id", debitNoteId);
        command.Parameters.AddWithValue("@Key", idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        if (!reader.GetFieldValue<byte[]>(3).AsSpan().SequenceEqual(requestHash))
            throw new SalesReturnConflictException(
                "The idempotency key or DebitNoteId was reused with another payload.");
        return new SalesDebitNoteAcceptance(reader.GetGuid(0), reader.GetGuid(5),
            reader.GetString(1), reader.GetString(2), reader.GetInt64(4), true);
    }

    private static async Task<OriginalInvoice> LoadOriginalAsync(
        SqlConnection connection, SqlTransaction transaction, SalesReturnUserIdentity user,
        Guid originalDocumentId, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT d.CustomerId,d.CustomerIdentification,d.DocumentNumber
            FROM dbo.SalesDocuments d WITH(UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.Businesses b ON b.BusinessId=d.BusinessId AND b.TenantId=@TenantId
            INNER JOIN dbo.FiscalSnapshots f ON f.DocumentId=d.DocumentId
            INNER JOIN dbo.FiscalDocuments fd ON fd.DocumentId=d.DocumentId
              AND fd.FiscalDocumentType=N'Invoice' AND fd.UniqueCode IS NOT NULL
            WHERE d.DocumentId=@Id AND d.BusinessId=@BusinessId
              AND d.DocumentType=N'SalesInvoice' AND d.ProcessingStatus=N'Completed'
              AND d.CustomerId IS NOT NULL;
            """, connection, transaction);
        Scope(command, user);
        command.Parameters.AddWithValue("@Id", originalDocumentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new SalesReturnValidationException(
                "The debit note requires a completed electronic invoice with an identified customer.");
        return new OriginalInvoice(reader.GetGuid(0), reader.GetString(1), reader.GetString(2));
    }

    private static async Task<AuralyDocumentNumberAssignment> AllocateNumberAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT TOP(1) ds.DocumentSeriesId,ds.Prefix,ds.SeriesCode,ds.Padding,
                   ds.RangeEnd,COALESCE(c.NextConsecutive,ds.RangeStart)
            FROM dbo.DocumentSeries ds WITH(UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.DocumentSeriesCursors c WITH(UPDLOCK,HOLDLOCK)
              ON c.DocumentSeriesId=ds.DocumentSeriesId
            WHERE ds.BusinessId=@BusinessId AND ds.DocumentType=N'SalesDebitNote'
              AND ds.DeviceId IS NULL AND ds.IsActive=1
            ORDER BY ds.DocumentSeriesId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        Guid id;
        string prefix;
        string code;
        byte padding;
        long end;
        long consecutive;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
                throw new SalesReturnValidationException(
                    "No active SalesDebitNote document series is configured for the business.");
            id = reader.GetGuid(0); prefix = reader.GetString(1); code = reader.GetString(2);
            padding = reader.GetByte(3); end = reader.GetInt64(4); consecutive = reader.GetInt64(5);
        }
        if (consecutive > end)
            throw new SalesReturnValidationException("The SalesDebitNote document series is exhausted.");
        await using var update = new SqlCommand("""
            IF EXISTS(SELECT 1 FROM dbo.DocumentSeriesCursors WHERE DocumentSeriesId=@Id)
              UPDATE dbo.DocumentSeriesCursors SET NextConsecutive=@Next,UpdatedAt=@Now WHERE DocumentSeriesId=@Id;
            ELSE INSERT dbo.DocumentSeriesCursors(DocumentSeriesId,NextConsecutive,UpdatedAt)
              VALUES(@Id,@Next,@Now);
            """, connection, transaction);
        update.Parameters.AddWithValue("@Id", id);
        update.Parameters.AddWithValue("@Next", consecutive + 1);
        update.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
        await update.ExecuteNonQueryAsync(cancellationToken);
        return AuralyDocumentNumberAssignment.Create(id, AuralyDocumentTypes.SalesDebitNote,
            prefix, code, consecutive, padding);
    }

    private static async Task<long> AllocateSequenceAsync(
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

    private static async Task InsertHeaderAsync(
        SqlConnection connection, SqlTransaction transaction, SalesReturnUserIdentity user,
        ConfirmSalesDebitNoteRequest request, string idempotencyKey, byte[] requestHash,
        AuralyDocumentNumberAssignment number, OriginalInvoice original,
        decimal untaxed, decimal tax, decimal total, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.SalesDebitNotes
              (DebitNoteId,BusinessId,OriginalDocumentId,DocumentSeriesId,DocumentNumber,
               DocumentPrefix,DocumentSeriesCode,DocumentConsecutive,IdempotencyKey,PayloadHash,
               IssuedAt,DueAt,ConceptCode,ReasonDescription,Notes,CustomerId,
               CustomerIdentification,UntaxedAmount,TaxAmount,TotalAmount,Status,
               CreatedByUserId,AcceptedAt)
            VALUES(@Id,@BusinessId,@OriginalId,@SeriesId,@Number,@Prefix,@SeriesCode,
               @Consecutive,@Key,@Hash,@IssuedAt,@DueAt,@Concept,@Reason,@Notes,@CustomerId,
               @Identification,@Untaxed,@Tax,@Total,N'Accepted',@UserId,@Now);
            """, connection, transaction);
        command.Parameters.AddWithValue("@Id", request.DebitNoteId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@OriginalId", request.OriginalDocumentId);
        command.Parameters.AddWithValue("@SeriesId", number.SeriesId);
        command.Parameters.AddWithValue("@Number", number.FullNumber);
        command.Parameters.AddWithValue("@Prefix", number.Prefix);
        command.Parameters.AddWithValue("@SeriesCode", number.SeriesCode);
        command.Parameters.AddWithValue("@Consecutive", number.Consecutive);
        command.Parameters.AddWithValue("@Key", idempotencyKey);
        command.Parameters.Add("@Hash", SqlDbType.Binary, 32).Value = requestHash;
        command.Parameters.AddWithValue("@IssuedAt", request.IssuedAt);
        command.Parameters.AddWithValue("@DueAt", request.DueAt);
        command.Parameters.AddWithValue("@Concept", request.ConceptCode);
        command.Parameters.AddWithValue("@Reason", request.ReasonDescription);
        command.Parameters.AddWithValue("@Notes", Db(request.Notes));
        command.Parameters.AddWithValue("@CustomerId", original.CustomerId);
        command.Parameters.AddWithValue("@Identification", original.CustomerIdentification);
        Decimal(command, "@Untaxed", untaxed, 19, 4);
        Decimal(command, "@Tax", tax, 19, 4);
        Decimal(command, "@Total", total, 19, 4);
        command.Parameters.AddWithValue("@UserId", user.UserId);
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertLinesAsync(
        SqlConnection connection, SqlTransaction transaction, Guid id,
        IEnumerable<SalesDebitNoteLineSnapshot> lines, CancellationToken cancellationToken)
    {
        foreach (var line in lines)
        {
            await using var command = new SqlCommand("""
                INSERT dbo.SalesDebitNoteLines
                  (DebitNoteId,LineNumber,DescriptionSnapshot,Quantity,UnitPrice,TaxCode,
                   TaxRate,UntaxedAmount,TaxAmount,LineTotal)
                VALUES(@Id,@Line,@Description,@Quantity,@UnitPrice,@TaxCode,@TaxRate,
                   @Untaxed,@Tax,@Total);
                """, connection, transaction);
            command.Parameters.AddWithValue("@Id", id);
            command.Parameters.AddWithValue("@Line", line.LineNumber);
            command.Parameters.AddWithValue("@Description", line.Description);
            Decimal(command, "@Quantity", line.Quantity, 19, 6);
            Decimal(command, "@UnitPrice", line.UnitPrice, 19, 4);
            command.Parameters.AddWithValue("@TaxCode", line.TaxCode);
            Decimal(command, "@TaxRate", line.TaxRate, 9, 6);
            Decimal(command, "@Untaxed", line.UntaxedAmount, 19, 4);
            Decimal(command, "@Tax", line.TaxAmount, 19, 4);
            Decimal(command, "@Total", line.LineTotal, 19, 4);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertJobAsync(
        SqlConnection connection, SqlTransaction transaction, Guid id, Guid businessId,
        Guid jobId, long sequence, string payload, byte[] payloadHash, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.DocumentProcessingJobs
              (JobId,BusinessId,ProcessingSequence,DocumentId,DocumentType,Status,AvailableAt,CreatedAt)
            VALUES(@JobId,@BusinessId,@Sequence,@Id,N'SalesDebitNote',N'Pending',@Now,@Now);
            INSERT dbo.DocumentProcessingPayloads
              (DocumentId,DocumentType,BusinessId,ContractVersion,PayloadJson,PayloadHash,AcceptedAt)
            VALUES(@Id,N'SalesDebitNote',@BusinessId,1,@Payload,@PayloadHash,@Now);
            """, connection, transaction);
        command.Parameters.AddWithValue("@JobId", jobId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@Sequence", sequence);
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.AddWithValue("@Payload", payload);
        command.Parameters.Add("@PayloadHash", SqlDbType.Binary, 32).Value = payloadHash;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SalesDebitNoteListItem ReadHeader(SqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
        reader.GetString(4), reader.GetString(5), reader.GetDateTimeOffset(6),
        reader.GetString(7), reader.GetString(8), reader.GetDecimal(9),
        reader.GetString(10), reader.GetString(11), reader.IsDBNull(12) ? null : reader.GetString(12));

    private static void Scope(SqlCommand command, SalesReturnUserIdentity user)
    {
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
    }

    private static void Decimal(SqlCommand command, string name, decimal value, byte precision, byte scale)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = precision;
        parameter.Scale = scale;
        parameter.Value = value;
    }

    private static object Db(object? value) => value ?? DBNull.Value;

    private sealed record OriginalInvoice(
        Guid CustomerId, string CustomerIdentification, string DocumentNumber);
}
