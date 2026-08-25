using System.Data;
using System.Security.Cryptography;
using Auraly.Application.Fiscal;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlFiscalGenerationWorkStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids) : IFiscalGenerationWorkStore
{
    public async Task<FiscalGenerationWorkItem?> AcquireAsync(
        Guid businessId, Guid requestedDocumentId, string workerId,
        DateTimeOffset acquiredAt, TimeSpan lease,
        CancellationToken cancellationToken)
    {
        const string acquireSql = """
            DECLARE @Document TABLE(DocumentId uniqueidentifier NOT NULL);
            ;WITH candidate AS
            (
                SELECT p.DocumentId
                FROM dbo.FiscalDocumentProcesses p WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE p.DocumentId=@DocumentId AND p.BusinessId=@BusinessId
                  AND p.Status = @PendingGeneration
                  AND p.FiscalIssuerConfigurationId IS NOT NULL
                  AND (p.NextAttemptAt IS NULL OR p.NextAttemptAt <= @AcquiredAt)
                  AND (p.LockedAt IS NULL OR p.LockedAt < @LeaseExpiredAt)
            )
            UPDATE p
            SET LockedAt=@AcquiredAt, LockedBy=@WorkerId,
                AttemptCount=AttemptCount+1, UpdatedAt=@AcquiredAt
            OUTPUT inserted.DocumentId INTO @Document
            FROM dbo.FiscalDocumentProcesses p
            INNER JOIN candidate c ON c.DocumentId=p.DocumentId;
            SELECT DocumentId FROM @Document;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        Guid? documentId;
        await using (var command = new SqlCommand(acquireSql, connection, transaction))
        {
            command.Parameters.AddWithValue("@DocumentId", requestedDocumentId);
            command.Parameters.AddWithValue("@BusinessId", businessId);
            command.Parameters.AddWithValue("@PendingGeneration", FiscalDocumentStatusCodes.PendingGeneration);
            command.Parameters.AddWithValue("@AcquiredAt", acquiredAt);
            command.Parameters.AddWithValue("@LeaseExpiredAt", acquiredAt - lease);
            command.Parameters.AddWithValue("@WorkerId", workerId);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            documentId = value is null or DBNull ? null : (Guid)value;
        }
        if (documentId is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var work = await LoadAsync(connection, transaction, documentId.Value, workerId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return work;
    }

    public async Task<DateTimeOffset?> GetResumeAtAsync(
        Guid businessId,
        Guid documentId,
        DateTimeOffset checkedAt,
        TimeSpan lease,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT p.NextAttemptAt,p.LockedAt
            FROM dbo.FiscalDocumentProcesses p
            WHERE p.DocumentId=@DocumentId AND p.BusinessId=@BusinessId
              AND p.Status=@PendingGeneration
              AND p.FiscalIssuerConfigurationId IS NOT NULL;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue(
            "@PendingGeneration",
            FiscalDocumentStatusCodes.PendingGeneration);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var resumeAt = checkedAt.AddSeconds(1);
        if (!reader.IsDBNull(0))
        {
            var nextAttemptAt = reader.GetFieldValue<DateTimeOffset>(0);
            if (nextAttemptAt > resumeAt) resumeAt = nextAttemptAt;
        }
        if (!reader.IsDBNull(1))
        {
            var leaseExpiresAt = reader.GetFieldValue<DateTimeOffset>(1).Add(lease);
            if (leaseExpiresAt > resumeAt) resumeAt = leaseExpiresAt.AddSeconds(1);
        }
        return resumeAt;
    }

    public async Task CompleteAsync(FiscalGenerationWorkItem work,
        FiscalGeneratedArtifacts artifacts, CancellationToken cancellationToken)
    {
        ValidateHash(artifacts.UnsignedXml, artifacts.UnsignedSha256Hex);
        ValidateHash(artifacts.SignedXml, artifacts.SignedSha256Hex);
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await InsertArtifactAsync(connection, transaction, work.DocumentId,
            FiscalArtifactTypeCodes.UnsignedXml, artifacts.UnsignedXml,
            artifacts.UnsignedSha256Hex, $"{work.FiscalNumber}.xml",
            artifacts, artifacts.GeneratedAt, cancellationToken);
        await InsertArtifactAsync(connection, transaction, work.DocumentId,
            FiscalArtifactTypeCodes.SignedXml, artifacts.SignedXml,
            artifacts.SignedSha256Hex, $"{work.FiscalNumber}-signed.xml",
            artifacts, artifacts.SignedAt, cancellationToken);
        const string sql = """
            UPDATE dbo.FiscalDocumentProcesses
            SET Status=@Status, GeneratedAt=@GeneratedAt, SignedAt=@SignedAt,
                NextAttemptAt=@SignedAt, LockedAt=NULL, LockedBy=NULL,
                LastErrorCode=NULL, LastErrorMessage=NULL, UpdatedAt=@SignedAt
            WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId
              AND LockedBy=@WorkerId AND Status=@PendingGeneration;
            UPDATE dbo.FiscalDocuments
            SET UniqueCode=@UniqueCode,FiscalStatus=@Status,UpdatedAt=@SignedAt
            WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId;
            UPDATE dbo.SalesReturnFiscalSnapshots
            SET UniqueCode=@UniqueCode,QrPayload=@QrPayload
            WHERE DocumentId=@DocumentId AND @FiscalDocumentType=N'CreditNote';
            UPDATE dbo.SalesDebitNoteFiscalSnapshots
            SET UniqueCode=@UniqueCode,QrPayload=@QrPayload
            WHERE DocumentId=@DocumentId AND @FiscalDocumentType=N'DebitNote';
            UPDATE dbo.PurchaseSupportFiscalSnapshots
            SET UniqueCode=@UniqueCode,QrPayload=@QrPayload
            WHERE DocumentId=@DocumentId AND @FiscalDocumentType=N'SupportDocument';
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@Status", FiscalDocumentStatusCodes.PendingSubmission);
        command.Parameters.AddWithValue("@PendingGeneration", FiscalDocumentStatusCodes.PendingGeneration);
        command.Parameters.AddWithValue("@GeneratedAt", artifacts.GeneratedAt);
        command.Parameters.AddWithValue("@SignedAt", artifacts.SignedAt);
        command.Parameters.AddWithValue("@DocumentId", work.DocumentId);
        command.Parameters.AddWithValue("@BusinessId", work.BusinessId);
        command.Parameters.AddWithValue("@WorkerId", work.WorkerId);
        command.Parameters.AddWithValue("@UniqueCode", artifacts.UniqueCode);
        command.Parameters.AddWithValue("@QrPayload", artifacts.QrPayload);
        command.Parameters.AddWithValue("@FiscalDocumentType", work.FiscalDocumentType);
        var expectedRows = work.FiscalDocumentType is
            FiscalDocumentTypeCodes.CreditNote or FiscalDocumentTypeCodes.DebitNote or
            FiscalDocumentTypeCodes.SupportDocument ? 3 : 2;
        if (await command.ExecuteNonQueryAsync(cancellationToken) != expectedRows)
            throw new InvalidOperationException(
                "The fiscal generation lease is no longer owned by this worker.");
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task FailAsync(FiscalGenerationWorkItem work, string status,
        string errorCode, string errorMessage, DateTimeOffset failedAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.FiscalDocumentProcesses
            SET Status=@Status, LastErrorCode=@ErrorCode, LastErrorMessage=@ErrorMessage,
                LockedAt=NULL, LockedBy=NULL, NextAttemptAt=NULL, UpdatedAt=@FailedAt
            WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId AND LockedBy=@WorkerId;
            UPDATE dbo.FiscalDocuments
            SET FiscalStatus=@Status,UpdatedAt=@FailedAt
            WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId;
            UPDATE dbo.SalesDocuments
            SET FiscalStatus=@Status
            WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId
              AND @FiscalDocumentType=N'Invoice';
            UPDATE dbo.SalesReturns
            SET FiscalStatus=@Status
            WHERE ReturnId=@DocumentId AND BusinessId=@BusinessId
              AND @FiscalDocumentType=N'CreditNote';
            UPDATE dbo.SalesDebitNotes
            SET FiscalStatus=@Status
            WHERE DebitNoteId=@DocumentId AND BusinessId=@BusinessId
              AND @FiscalDocumentType=N'DebitNote';
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@Status", status);
        command.Parameters.AddWithValue("@ErrorCode", Limit(errorCode, 128));
        command.Parameters.AddWithValue("@ErrorMessage", Limit(errorMessage, 2000));
        command.Parameters.AddWithValue("@FailedAt", failedAt);
        command.Parameters.AddWithValue("@DocumentId", work.DocumentId);
        command.Parameters.AddWithValue("@BusinessId", work.BusinessId);
        command.Parameters.AddWithValue("@WorkerId", work.WorkerId);
        command.Parameters.AddWithValue("@FiscalDocumentType", work.FiscalDocumentType);
        var expectedRows = work.FiscalDocumentType == FiscalDocumentTypeCodes.SupportDocument ? 2 : 3;
        if (await command.ExecuteNonQueryAsync(cancellationToken) != expectedRows)
            throw new InvalidOperationException("The fiscal generation failure could not release its lease.");
        await SqlFiscalStatusSynchronizationOutbox.InsertAsync(
            connection, transaction, ids, work.BusinessId, failedAt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<FiscalGenerationWorkItem> LoadAsync(
        SqlConnection connection, SqlTransaction transaction, Guid documentId,
        string workerId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT p.BusinessId,fd.FiscalDocumentType,fd.FiscalNumber,
                   s.SnapshotJson,credit.SnapshotJson,
                   c.FiscalIssuerConfigurationId, c.SupplierTaxId, c.SupplierCheckDigit,
                   c.LegalName, COALESCE(c.TradeName,c.LegalName), c.TaxLevelCode,
                   c.TaxSchemeId, c.TaxSchemeName, c.IdentificationTypeCode,
                   c.CityCode, c.CityName, c.DepartmentName, c.DepartmentCode,
                   c.AddressLine, c.CountryCode, c.CountryName,
                   c.SoftwareIdentificationCode, c.SoftwarePinSecretReference,
                   c.Environment, c.CertificateProvider, c.CertificateKeyReference,
                   c.CertificateThumbprint, c.TechnicalAnnexVersion, c.GeneratorVersion,
                   a.AuthorizationNumber, a.ValidFrom, a.ValidUntil,
                   fs.Prefix, fs.RangeStart, fs.RangeEnd,debit.SnapshotJson,support.SnapshotJson
            FROM dbo.FiscalDocumentProcesses p
            INNER JOIN dbo.FiscalDocuments fd ON fd.DocumentId=p.DocumentId
            LEFT JOIN dbo.FiscalSnapshots s ON s.DocumentId=p.DocumentId
            LEFT JOIN dbo.SalesReturnFiscalSnapshots credit ON credit.DocumentId=p.DocumentId
            LEFT JOIN dbo.SalesDebitNoteFiscalSnapshots debit ON debit.DocumentId=p.DocumentId
            LEFT JOIN dbo.PurchaseSupportFiscalSnapshots support ON support.DocumentId=p.DocumentId
            LEFT JOIN dbo.SalesDocuments d ON d.DocumentId=p.DocumentId
            INNER JOIN dbo.FiscalIssuerConfigurations c
                ON c.FiscalIssuerConfigurationId=p.FiscalIssuerConfigurationId
               AND c.BusinessId=p.BusinessId
            LEFT JOIN dbo.FiscalAuthorizations a
                ON a.FiscalAuthorizationId=d.FiscalAuthorizationId
               AND a.BusinessId=p.BusinessId
            LEFT JOIN dbo.FiscalSeries fs
                ON fs.SeriesId=d.FiscalSeriesId
               AND fs.FiscalAuthorizationId=a.FiscalAuthorizationId
            WHERE p.DocumentId=@DocumentId AND p.LockedBy=@WorkerId;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@WorkerId", workerId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The acquired fiscal document could not be loaded.");
        var businessId = reader.GetGuid(0);
        var documentType = reader.GetString(1);
        var fiscalNumber = reader.GetString(2);
        var sale = reader.IsDBNull(3)
            ? null
            : PosSaleContractSerializer.Deserialize(reader.GetString(3));
        var creditNote = reader.IsDBNull(4)
            ? null
            : SalesReturnCreditNoteSnapshotSerializer.Deserialize(reader.GetString(4));
        var debitNote = reader.IsDBNull(35)
            ? null
            : SalesDebitNoteFiscalSnapshotSerializer.Deserialize(reader.GetString(35));
        var supportDocument = reader.IsDBNull(36)
            ? null
            : PurchaseSupportFiscalSnapshotSerializer.Deserialize(reader.GetString(36));
        var issuer = new FiscalIssuerWorkConfiguration(
            reader.GetGuid(5), businessId, reader.GetString(6), reader.GetString(7),
            reader.GetString(8), reader.GetString(9), reader.GetString(10),
            reader.GetString(11), reader.GetString(12), reader.GetString(13),
            new PosSaleUblAddressContract(reader.GetString(14), reader.GetString(15),
                reader.GetString(16), reader.GetString(17), reader.GetString(18),
                reader.GetString(19), reader.GetString(20)),
            reader.GetString(21), reader.GetString(22), reader.GetByte(23),
            reader.GetString(24), reader.GetString(25), reader.GetString(26),
            reader.GetString(27), reader.GetString(28));
        FiscalAuthorizationWorkConfiguration? authorization = null;
        if (!reader.IsDBNull(29))
            authorization = new FiscalAuthorizationWorkConfiguration(
                reader.GetString(29), DateOnly.FromDateTime(reader.GetDateTime(30)),
                DateOnly.FromDateTime(reader.GetDateTime(31)), reader.GetString(32),
                reader.GetInt64(33), reader.GetInt64(34));
        return new FiscalGenerationWorkItem(
            documentId, businessId, workerId, documentType, fiscalNumber,
            sale, creditNote, debitNote, issuer,
            authorization ?? (supportDocument is null ? null : new FiscalAuthorizationWorkConfiguration(
                supportDocument.Authorization.Number, supportDocument.Authorization.ValidFrom,
                supportDocument.Authorization.ValidUntil, supportDocument.Authorization.Prefix,
                supportDocument.Authorization.RangeStart, supportDocument.Authorization.RangeEnd)),
            supportDocument);
    }

    private async Task InsertArtifactAsync(SqlConnection connection, SqlTransaction transaction,
        Guid documentId, string type, byte[] content, string hashHex, string fileName,
        FiscalGeneratedArtifacts metadata, DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.FiscalArtifacts
            (FiscalArtifactId,DocumentId,ArtifactType,ArtifactVersion,Content,ContentHash,
             ContentType,FileName,TechnicalAnnexVersion,GeneratorVersion,CreatedAt)
            VALUES
            (@Id,@DocumentId,@Type,1,@Content,@Hash,'application/xml',@FileName,
             @Annex,@Generator,@CreatedAt);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@Id", ids.NewId());
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@Type", type);
        command.Parameters.Add("@Content", SqlDbType.VarBinary, -1).Value=content;
        command.Parameters.Add("@Hash", SqlDbType.Binary, 32).Value=Convert.FromHexString(hashHex);
        command.Parameters.AddWithValue("@FileName", fileName);
        command.Parameters.AddWithValue("@Annex", metadata.TechnicalAnnexVersion);
        command.Parameters.AddWithValue("@Generator", metadata.GeneratorVersion);
        command.Parameters.AddWithValue("@CreatedAt", createdAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void ValidateHash(byte[] content, string expected)
    {
        var actual=Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actual), Convert.FromHexString(expected)))
            throw new InvalidOperationException("A fiscal artifact hash does not match its content.");
    }

    private static string Limit(string value, int length) =>
        value.Length <= length ? value : value[..length];
}
