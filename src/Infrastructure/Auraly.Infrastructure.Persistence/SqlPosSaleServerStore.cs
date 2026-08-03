using System.Data;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlPosSaleServerStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator idGenerator)
    : IPosSaleServerStore
{
    public async Task<PosSaleContextValidation> ValidateContextAsync(
        PosSaleUploadRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT_BIG(1)
            FROM dbo.FiscalSeries s
            INNER JOIN dbo.FiscalAuthorizations a
                ON a.FiscalAuthorizationId=s.FiscalAuthorizationId
            INNER JOIN dbo.Businesses b
                ON b.BusinessId=s.BusinessId
            INNER JOIN dbo.Warehouses w
                ON w.WarehouseId=@WarehouseId
               AND w.BusinessId=b.BusinessId
            INNER JOIN dbo.AppUsers u
                ON u.UserId=@SoldByUserId
               AND u.TenantId=b.TenantId
            INNER JOIN dbo.DocumentSeries ds
                ON ds.DocumentSeriesId=@DocumentSeriesId
               AND ds.BusinessId=b.BusinessId
            INNER JOIN dbo.EnrolledDevices d
                ON d.DeviceId=@DeviceId
               AND d.TenantId=b.TenantId
            WHERE s.SeriesId=@SeriesId
              AND s.BusinessId=@BusinessId
              AND s.DeviceId=@DeviceId
              AND s.EmitterKind=N'Device'
              AND a.FiscalAuthorizationId=@FiscalAuthorizationId
              AND b.TenantId=@TenantId
              AND b.IsActive=1
              AND w.IsActive=1
              AND u.IsActive=1
              AND d.IsActive=1
              AND ds.DeviceId=@DeviceId
              AND ds.DocumentType=@DocumentType
              AND ds.Prefix=@DocumentPrefix
              AND ds.SeriesCode=@DocumentSeriesCode
              AND @DocumentConsecutive BETWEEN ds.RangeStart AND ds.RangeEnd
              AND ds.IsOfflineCapable=1
              AND ds.IsActive=1
              AND s.DocumentType=@DocumentType
              AND s.Prefix=@Prefix
              AND @Consecutive BETWEEN s.RangeStart AND s.RangeEnd
              AND a.AuthorizationNumber=@AuthorizationNumber
              AND a.SupplierTaxId=@SupplierTaxId
              AND a.Environment=@Environment
              AND CONVERT(date,@IssuedAt) BETWEEN a.ValidFrom AND a.ValidUntil
              AND s.IsActive=1
              AND a.IsActive=1;
            """;

        var snapshot = request.FiscalSnapshot;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@SeriesId", snapshot.SeriesId);
        command.Parameters.AddWithValue("@DocumentSeriesId", request.DocumentNumber.SeriesId);
        command.Parameters.AddWithValue("@DocumentPrefix", request.DocumentNumber.Prefix);
        command.Parameters.AddWithValue("@DocumentSeriesCode", request.DocumentNumber.SeriesCode);
        command.Parameters.AddWithValue("@DocumentConsecutive", request.DocumentNumber.Consecutive);
        command.Parameters.AddWithValue("@FiscalAuthorizationId", snapshot.FiscalAuthorizationId);
        command.Parameters.AddWithValue("@TenantId", request.TenantId);
        command.Parameters.AddWithValue("@SoldByUserId", request.SoldByUserId);
        command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", request.WarehouseId);
        command.Parameters.AddWithValue("@DeviceId", request.DeviceId);
        command.Parameters.AddWithValue("@DocumentType", snapshot.DocumentType);
        command.Parameters.AddWithValue("@Prefix", snapshot.Prefix);
        command.Parameters.AddWithValue("@Consecutive", snapshot.Consecutive);
        command.Parameters.AddWithValue("@AuthorizationNumber", snapshot.AuthorizationNumber);
        command.Parameters.AddWithValue("@SupplierTaxId", snapshot.SupplierTaxId);
        command.Parameters.AddWithValue("@Environment", snapshot.Environment);
        command.Parameters.AddWithValue("@IssuedAt", snapshot.IssuedAt);
        var count = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
        return count == 1
            ? PosSaleContextValidation.Valid()
            : PosSaleContextValidation.Invalid(
                "The fiscal series, authorization, business, warehouse or device assignment is invalid.");
    }
    public async Task<StoredPosSale?> FindAsync(
        Guid businessId,
        Guid documentId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var connection = connections.Create();
                await connection.OpenAsync(cancellationToken);
                return await FindInternalAsync(
                    connection,
                    transaction: null,
                    businessId,
                    documentId,
                    idempotencyKey,
                    cancellationToken);
            }
            catch (SqlException exception)
                when (exception.Number == 1205 && attempt < 4)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(25 * attempt),
                    cancellationToken);
            }
        }
    }

    public Task<StoredPosSale> StoreReceptionAsync(
        StorePosSaleReceptionCommand command,
        CancellationToken cancellationToken) =>
        StoreReceptionCoreAsync(command, 0, cancellationToken);

    private async Task<StoredPosSale> StoreReceptionCoreAsync(
        StorePosSaleReceptionCommand command,
        int deadlockAttempt,
        CancellationToken cancellationToken)
    {
        var request = command.Request;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            // Reception and processing acquire locks in the same order:
            // business cursor first, then document and session rows.
            await EnsureAndLockBusinessCursorAsync(
                connection, transaction, request.BusinessId,
                command.ReceivedAt, cancellationToken);

            var existing = await FindInternalAsync(
                connection,
                transaction,
                request.BusinessId,
                request.DocumentId,
                command.IdempotencyKey,
                cancellationToken);
            if (existing is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return existing;
            }

            var processingStatus = command.Verification.IsVerified ? "Received" : "Blocked";
            var fiscalStatus = command.Verification.IsVerified
                ? PosSaleRemoteStatuses.FiscalVerified
                : PosSaleRemoteStatuses.FiscalIntegrityConflict;
            await InsertDocumentAsync(
                connection,
                transaction,
                command,
                fiscalStatus,
                processingStatus,
                cancellationToken);
            await InsertFiscalDocumentAsync(
                connection,
                transaction,
                command,
                fiscalStatus,
                cancellationToken);
            await InsertSnapshotAsync(
                connection,
                transaction,
                command,
                fiscalStatus,
                cancellationToken);
            await InsertFiscalProcessAsync(
                connection,
                transaction,
                command,
                command.Verification.IsVerified
                    ? FiscalDocumentStatusCodes.PendingGeneration
                    : FiscalDocumentStatusCodes.FiscalIntegrityConflict,
                cancellationToken);

            if (command.Verification.IsVerified)
            {
                await EnqueueDocumentAsync(
                    connection,
                    transaction,
                    command,
                    cancellationToken);
            }


            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            if (transaction.Connection is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            if (deadlockAttempt >= 4) throw;
            await Task.Delay(
                TimeSpan.FromMilliseconds(25 * (deadlockAttempt + 1)),
                cancellationToken);
            return await StoreReceptionCoreAsync(
                command,
                deadlockAttempt + 1,
                cancellationToken);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            if (transaction.Connection is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
        }

        return await FindAfterContentionAsync(
            request.BusinessId,
            request.DocumentId,
            command.IdempotencyKey,
            cancellationToken);
    }

    private async Task<StoredPosSale> FindAfterContentionAsync(
        Guid businessId, Guid documentId, string idempotencyKey, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                var stored = await FindAsync(businessId, documentId, idempotencyKey, cancellationToken);
                if (stored is not null) return stored;
            }
            catch (SqlException exception) when (exception.Number == 1205 && attempt < 4)
            {
                // The competing idempotent request still owns the winning transaction.
            }
            if (attempt < 4)
                await Task.Delay(TimeSpan.FromMilliseconds(25 * (attempt + 1)), cancellationToken);
        }
        throw new InvalidOperationException("The received sale was not persisted after bounded contention retries.");
    }

    private static async Task InsertFiscalDocumentAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        StorePosSaleReceptionCommand command,
        string fiscalStatus,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT dbo.FiscalDocuments
              (DocumentId,BusinessId,SourceDocumentType,FiscalDocumentType,
               AuralyDocumentNumber,FiscalNumber,UniqueCodeType,UniqueCode,
               IssuedAt,FiscalStatus,CreatedAt,UpdatedAt)
            VALUES
              (@DocumentId,@BusinessId,N'SalesInvoice',N'Invoice',
               @AuralyNumber,@FiscalNumber,N'CUFE',@UniqueCode,
               @IssuedAt,@FiscalStatus,@CreatedAt,@CreatedAt);
            """;
        var request = command.Request;
        await using var sqlCommand = new SqlCommand(sql, connection, transaction);
        sqlCommand.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        sqlCommand.Parameters.AddWithValue("@BusinessId", request.BusinessId);
        sqlCommand.Parameters.AddWithValue("@AuralyNumber", request.DocumentNumber.FullNumber);
        sqlCommand.Parameters.AddWithValue("@FiscalNumber", request.FiscalSnapshot.FiscalNumber);
        sqlCommand.Parameters.AddWithValue("@UniqueCode", request.FiscalSnapshot.Cufe);
        sqlCommand.Parameters.AddWithValue("@IssuedAt", request.FiscalSnapshot.IssuedAt);
        sqlCommand.Parameters.AddWithValue("@FiscalStatus", fiscalStatus);
        sqlCommand.Parameters.AddWithValue("@CreatedAt", command.ReceivedAt);
        await sqlCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertDocumentAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        StorePosSaleReceptionCommand command,
        string fiscalStatus,
        string processingStatus,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.SalesDocuments
            (
                DocumentId, BusinessId, WarehouseId,
                DeviceId, WorkSessionId, SourceMode, DocumentSeriesId, DocumentNumber,
                DocumentPrefix, DocumentSeriesCode, DocumentConsecutive,
                FiscalSeriesId, FiscalAuthorizationId,
                DocumentType, IdempotencyKey, PayloadHash, FiscalNumber,
                FiscalPrefix, FiscalConsecutive, IssuedAt, CustomerIdentification, CustomerId,
                UntaxedAmount, TaxAmount, PayableAmount, CreditAmount, CreditDueDate, CufeReceived,
                CufeCalculated, FiscalStatus, ProcessingStatus, ReceivedAt,
                CreatedByDeviceId, SoldByUserId
            )
            VALUES
            (
                @DocumentId, @BusinessId, @WarehouseId,
                @DeviceId, @WorkSessionId, @SourceMode, @DocumentSeriesId, @DocumentNumber,
                @DocumentPrefix, @DocumentSeriesCode, @DocumentConsecutive,
                @FiscalSeriesId, @FiscalAuthorizationId,
                @DocumentType, @IdempotencyKey, @PayloadHash, @FiscalNumber,
                @FiscalPrefix, @FiscalConsecutive, @IssuedAt, @CustomerIdentification, @CustomerId,
                @UntaxedAmount, @TaxAmount, @PayableAmount, @CreditAmount, @CreditDueDate, @CufeReceived,
                @CufeCalculated, @FiscalStatus, @ProcessingStatus, @ReceivedAt,
                @DeviceId, @SoldByUserId
            );
            """;
        var request = command.Request;
        var snapshot = request.FiscalSnapshot;
        await using var sqlCommand = new SqlCommand(sql, connection, transaction);
        sqlCommand.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        sqlCommand.Parameters.AddWithValue("@BusinessId", request.BusinessId);
        sqlCommand.Parameters.AddWithValue("@WarehouseId", request.WarehouseId);
        sqlCommand.Parameters.AddWithValue("@WorkSessionId", request.WorkSessionId);
        sqlCommand.Parameters.AddWithValue("@DeviceId", request.DeviceId == Guid.Empty ? DBNull.Value : request.DeviceId);
        sqlCommand.Parameters.AddWithValue("@SourceMode", request.SourceMode);
        sqlCommand.Parameters.AddWithValue("@DocumentSeriesId", request.DocumentNumber.SeriesId);
        sqlCommand.Parameters.AddWithValue("@SoldByUserId", request.SoldByUserId);
        sqlCommand.Parameters.AddWithValue("@DocumentNumber", request.DocumentNumber.FullNumber);
        sqlCommand.Parameters.AddWithValue("@DocumentPrefix", request.DocumentNumber.Prefix);
        sqlCommand.Parameters.AddWithValue("@DocumentSeriesCode", request.DocumentNumber.SeriesCode);
        sqlCommand.Parameters.AddWithValue("@DocumentConsecutive", request.DocumentNumber.Consecutive);
        sqlCommand.Parameters.AddWithValue("@FiscalSeriesId", snapshot.SeriesId);
        sqlCommand.Parameters.AddWithValue("@FiscalAuthorizationId", snapshot.FiscalAuthorizationId);
        sqlCommand.Parameters.AddWithValue("@DocumentType", snapshot.DocumentType);
        sqlCommand.Parameters.AddWithValue("@IdempotencyKey", command.IdempotencyKey);
        sqlCommand.Parameters.Add("@PayloadHash", SqlDbType.Binary, 32).Value = command.PayloadHash;
        sqlCommand.Parameters.AddWithValue("@FiscalNumber", snapshot.FiscalNumber);
        sqlCommand.Parameters.AddWithValue("@FiscalPrefix", snapshot.Prefix);
        sqlCommand.Parameters.AddWithValue("@FiscalConsecutive", snapshot.Consecutive);
        sqlCommand.Parameters.AddWithValue("@IssuedAt", snapshot.IssuedAt);
        sqlCommand.Parameters.AddWithValue("@CustomerIdentification", snapshot.CustomerIdentification);
        AddDecimal(sqlCommand, "@UntaxedAmount", snapshot.UntaxedAmount, 19, 4);
        sqlCommand.Parameters.AddWithValue("@CustomerId", (object?)request.CustomerId ?? DBNull.Value);
        AddDecimal(sqlCommand, "@TaxAmount", snapshot.TaxAmount, 19, 4);
        AddDecimal(sqlCommand, "@PayableAmount", snapshot.PayableAmount, 19, 4);
        AddDecimal(sqlCommand, "@CreditAmount", request.Credit?.Amount ?? 0m, 19, 4);
        sqlCommand.Parameters.AddWithValue("@CreditDueDate", (object?)request.Credit?.DueDate ?? DBNull.Value);
        sqlCommand.Parameters.AddWithValue("@CufeReceived", snapshot.Cufe);
        sqlCommand.Parameters.AddWithValue(
            "@CufeCalculated",
            (object?)command.Verification.CufeCalculated ?? DBNull.Value);
        sqlCommand.Parameters.AddWithValue("@FiscalStatus", fiscalStatus);
        sqlCommand.Parameters.AddWithValue("@ProcessingStatus", processingStatus);
        sqlCommand.Parameters.AddWithValue("@ReceivedAt", command.ReceivedAt);
        await sqlCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertSnapshotAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        StorePosSaleReceptionCommand command,
        string integrityStatus,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.FiscalSnapshots
            (
                DocumentId, SnapshotJson, PayloadHash, TechnicalKeyVersion,
                Environment, CufeReceived, CufeCalculated, QrPayload,
                IntegrityStatus, VerifiedAt, ConflictReason, CreatedAt
            )
            VALUES
            (
                @DocumentId, @SnapshotJson, @PayloadHash, @TechnicalKeyVersion,
                @Environment, @CufeReceived, @CufeCalculated, @QrPayload,
                @IntegrityStatus, @VerifiedAt, @ConflictReason, @CreatedAt
            );
            """;
        var snapshot = command.Request.FiscalSnapshot;
        await using var sqlCommand = new SqlCommand(sql, connection, transaction);
        sqlCommand.Parameters.AddWithValue("@DocumentId", command.Request.DocumentId);
        sqlCommand.Parameters.AddWithValue("@SnapshotJson", command.SnapshotJson);
        sqlCommand.Parameters.Add("@PayloadHash", SqlDbType.Binary, 32).Value = command.PayloadHash;
        sqlCommand.Parameters.AddWithValue("@TechnicalKeyVersion", snapshot.TechnicalKeyVersion);
        sqlCommand.Parameters.AddWithValue("@Environment", snapshot.Environment);
        sqlCommand.Parameters.AddWithValue("@CufeReceived", snapshot.Cufe);
        sqlCommand.Parameters.AddWithValue(
            "@CufeCalculated",
            (object?)command.Verification.CufeCalculated ?? DBNull.Value);
        sqlCommand.Parameters.AddWithValue("@QrPayload", snapshot.QrPayload);
        sqlCommand.Parameters.AddWithValue("@IntegrityStatus", integrityStatus);
        sqlCommand.Parameters.AddWithValue(
            "@VerifiedAt",
            command.Verification.IsVerified ? command.ReceivedAt : DBNull.Value);
        sqlCommand.Parameters.AddWithValue(
            "@ConflictReason",
            (object?)command.Verification.ConflictReason ?? DBNull.Value);
        sqlCommand.Parameters.AddWithValue("@CreatedAt", command.ReceivedAt);
        await sqlCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertFiscalProcessAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        StorePosSaleReceptionCommand command,
        string status,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.FiscalDocumentProcesses
            (
                DocumentId, BusinessId, FiscalIssuerConfigurationId, Status,
                AttemptCount, CreatedAt, UpdatedAt
            )
            VALUES
            (
                @DocumentId, @BusinessId, @FiscalIssuerConfigurationId, @Status,
                0, @CreatedAt, @CreatedAt
            );
            """;
        await using var sqlCommand = new SqlCommand(sql, connection, transaction);
        sqlCommand.Parameters.AddWithValue("@DocumentId", command.Request.DocumentId);
        sqlCommand.Parameters.AddWithValue("@BusinessId", command.Request.BusinessId);
        sqlCommand.Parameters.AddWithValue(
            "@FiscalIssuerConfigurationId",
            (object?)command.Request.UblSnapshot?.FiscalIssuerConfigurationId ?? DBNull.Value);
        sqlCommand.Parameters.AddWithValue("@Status", status);
        sqlCommand.Parameters.AddWithValue("@CreatedAt", command.ReceivedAt);
        await sqlCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnqueueDocumentAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        StorePosSaleReceptionCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            DECLARE @Assigned TABLE (ProcessingSequence BIGINT NOT NULL);

            UPDATE dbo.BusinessProcessingCursors WITH (UPDLOCK, HOLDLOCK)
            SET LastAssignedSequence = LastAssignedSequence + 1,
                UpdatedAt = @CreatedAt
            OUTPUT INSERTED.LastAssignedSequence INTO @Assigned (ProcessingSequence)
            WHERE BusinessId = @BusinessId;

            INSERT INTO dbo.DocumentProcessingJobs
            (
                JobId, BusinessId, ProcessingSequence, DocumentId, DocumentType,
                Status, AttemptCount, AvailableAt, CreatedAt
            )
            SELECT
                @JobId, @BusinessId, ProcessingSequence, @DocumentId, @DocumentType,
                N'Pending', 0, @CreatedAt, @CreatedAt
            FROM @Assigned;

            INSERT INTO dbo.DocumentProcessingPayloads
            (
                DocumentId, DocumentType, BusinessId, ContractVersion,
                PayloadJson, PayloadHash, AcceptedAt
            )
            VALUES
            (
                @DocumentId, @DocumentType, @BusinessId, 1,
                @PayloadJson, @PayloadHash, @CreatedAt
            );
            """;

        await using var sqlCommand = new SqlCommand(sql, connection, transaction);
        sqlCommand.Parameters.AddWithValue("@JobId", idGenerator.NewId());
        sqlCommand.Parameters.AddWithValue("@BusinessId", command.Request.BusinessId);
        sqlCommand.Parameters.AddWithValue("@DocumentId", command.Request.DocumentId);
        sqlCommand.Parameters.AddWithValue(
            "@DocumentType",
            command.Request.FiscalSnapshot.DocumentType);
        sqlCommand.Parameters.AddWithValue("@PayloadJson", command.SnapshotJson);
        sqlCommand.Parameters.Add("@PayloadHash", SqlDbType.Binary, 32).Value = command.PayloadHash;
        sqlCommand.Parameters.AddWithValue("@CreatedAt", command.ReceivedAt);
        if (await sqlCommand.ExecuteNonQueryAsync(cancellationToken) < 3)
        {
            throw new DBConcurrencyException(
                "The business sequence, durable job and immutable payload were not created atomically.");
        }
    }

    private static async Task EnsureAndLockBusinessCursorAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            IF NOT EXISTS
            (
                SELECT 1
                FROM dbo.BusinessProcessingCursors WITH (UPDLOCK,HOLDLOCK)
                WHERE BusinessId=@BusinessId
            )
            BEGIN
                INSERT dbo.BusinessProcessingCursors
                  (BusinessId,LastAssignedSequence,LastCompletedSequence,UpdatedAt)
                VALUES
                  (@BusinessId,0,0,@Now);
            END;

            SELECT LastAssignedSequence
            FROM dbo.BusinessProcessingCursors WITH (UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@Now", now);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null || value is DBNull)
            throw new DBConcurrencyException(
                "The business processing cursor could not be locked.");
    }

    private static async Task<StoredPosSale?> FindInternalAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        Guid businessId,
        Guid documentId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT d.DocumentId,
                   j.JobId,
                   b.TenantId,
                   d.IdempotencyKey,
                   d.PayloadHash,
                   d.FiscalStatus,
                   d.ProcessingStatus,
                   d.CufeReceived,
                   d.CufeCalculated,
                   COALESCE(j.JobId,d.DocumentId),
                   d.ReceivedAt,
                   d.ProcessedAt,
                   COALESCE(s.ConflictReason,j.LastError)
            FROM dbo.SalesDocuments d
            INNER JOIN dbo.Businesses b ON b.BusinessId = d.BusinessId
            INNER JOIN dbo.FiscalSnapshots s ON s.DocumentId = d.DocumentId
            LEFT JOIN dbo.DocumentProcessingJobs j
                ON j.DocumentId=d.DocumentId
               AND j.DocumentType=d.DocumentType
            WHERE d.BusinessId = @BusinessId
              AND (d.DocumentId = @DocumentId OR d.IdempotencyKey = @IdempotencyKey)
            ORDER BY CASE WHEN d.DocumentId = @DocumentId THEN 0 ELSE 1 END;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@IdempotencyKey", idempotencyKey.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var result = new StoredPosSale(
            reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetString(3),
            (byte[])reader[4],
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetGuid(9),
            reader.GetDateTimeOffset(10),
            reader.IsDBNull(11) ? null : reader.GetDateTimeOffset(11),
            reader.IsDBNull(12) ? null : reader.GetString(12));
        if (await reader.ReadAsync(cancellationToken))
        {
            throw new PosSaleIdempotencyConflictException(
                "The document ID and idempotency key refer to different sales.");
        }

        return result;
    }

    private static void AddDecimal(
        SqlCommand command,
        string name,
        decimal value,
        byte precision,
        byte scale)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = precision;
        parameter.Scale = scale;
        parameter.Value = value;
    }
}

