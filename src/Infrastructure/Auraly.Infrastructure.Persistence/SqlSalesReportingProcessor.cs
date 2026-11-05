using System.Data;
using System.Security.Cryptography;
using System.Text;
using Auraly.Application.Sales;
using Auraly.Contracts.Returns;
using Auraly.Contracts.Purchasing;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlSalesReportingProcessor(
    SqlServerConnectionFactory connections,
    SqlSalesReportingProjectionWriter projectionWriter,
    TimeProvider timeProvider,
    Auraly.BuildingBlocks.Domain.Identifiers.IAuralyIdGenerator ids)
{
    public async Task ProcessAsync(
        Guid documentId,
        string documentType,
        Guid businessId,
        long sourceVersion,
        CancellationToken cancellationToken)
    {
        if (!SalesReportingProcessingPolicy.Supports(documentType)) return;

        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await ProcessSourceAsync(connection,documentId,documentType,businessId,sourceVersion,false,cancellationToken);
    }

    private async Task ProcessSourceAsync(SqlConnection connection,Guid documentId,string documentType,Guid businessId,
        long sourceVersion,bool rebuilding,CancellationToken cancellationToken)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var committed = false;
        try
        {
            var source = await LockSourceAsync(
                connection, transaction, documentId, documentType, businessId,
                sourceVersion,cancellationToken,rebuilding);
            if (source is null)
                throw new InvalidOperationException(
                    "The completed document has no immutable reporting source.");
            if (source.Status == "Projected")
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            var startedAt = timeProvider.GetUtcNow();
            await using (var start = new SqlCommand("""
                UPDATE reporting.SalesReportingJobs
                SET Status=N'Processing',AttemptCount=AttemptCount+1,
                    StartedAt=@StartedAt,CompletedAt=NULL,LastError=NULL
                WHERE SourceDocumentId=@DocumentId
                  AND SourceDocumentType=@DocumentType
                  AND BusinessId=@BusinessId
                  AND SourceVersion=@SourceVersion
                  AND Status IN(N'Pending',N'Failed');
                """, connection, transaction))
            {
                start.Parameters.AddWithValue("@DocumentId", documentId);
                start.Parameters.AddWithValue("@DocumentType", documentType);
                start.Parameters.AddWithValue("@BusinessId", businessId);
                start.Parameters.AddWithValue("@SourceVersion", sourceVersion);
                start.Parameters.AddWithValue("@StartedAt", startedAt);
                if (await start.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new DBConcurrencyException(
                        "The sales reporting job could not be acquired.");
            }
            transaction.Save("BeforeProjection");

            try
            {
                var session = new SalesReportingSqlSession(connection, transaction);
                if (documentType is "SalesInvoice" or "SalesReceipt")
                {
                    var value=PosSaleContractSerializer.Deserialize<SalesReportingSaleSource>(source.Payload);
                    ValidateSourceIdentity(value.TenantId,value.BusinessId,value.DocumentId,source.TenantId,businessId,documentId);
                    if(value.CommercialSnapshot.DocumentType!=documentType)throw new InvalidOperationException("El tipo de la fuente no corresponde al trabajo de reporting.");
                    await projectionWriter.ProjectSaleAsync(session,value,cancellationToken);
                }
                else if(documentType==ServiceInvoiceDocumentTypes.ServiceInvoice)
                {
                    var value=ServiceInvoiceSnapshotSerializer.Deserialize(source.Payload);
                    ValidateSourceIdentity(value.TenantId,value.BusinessId,value.DocumentId,source.TenantId,businessId,documentId);
                    await projectionWriter.ProjectServiceInvoiceAsync(session,value,cancellationToken);
                }
                else if(documentType=="SalesReturn")
                {
                    var value=SalesReturnContractSerializer.Deserialize(source.Payload);
                    ValidateSourceIdentity(value.TenantId,value.BusinessId,value.ReturnId,source.TenantId,businessId,documentId);
                    await projectionWriter.ProjectReturnAsync(session,value,cancellationToken);
                }
                else if(documentType=="GoodsReceipt")
                {
                    var value=GoodsReceiptContractSerializer.Deserialize(source.Payload);
                    ValidateSourceIdentity(value.TenantId,value.BusinessId,value.DocumentId,source.TenantId,businessId,documentId);
                    await projectionWriter.ProjectGoodsReceiptAsync(session,value,cancellationToken);
                }
                else if(documentType=="PurchaseReturn")
                {
                    var value=PurchaseReturnContractSerializer.Deserialize(source.Payload);
                    ValidateSourceIdentity(value.TenantId,value.BusinessId,value.ReturnId,source.TenantId,businessId,documentId);
                    await projectionWriter.ProjectPurchaseReturnAsync(session,value,cancellationToken);
                }
                else throw new InvalidOperationException($"Reporting no admite el tipo '{documentType}'.");

                var completedAt = timeProvider.GetUtcNow();
                await using var complete = new SqlCommand("""
                    UPDATE reporting.SalesReportingJobs
                    SET Status=N'Projected',CompletedAt=@CompletedAt,LastError=NULL
                    WHERE SourceDocumentId=@DocumentId
                      AND SourceDocumentType=@DocumentType
                      AND BusinessId=@BusinessId AND Status=N'Processing'
                      AND SourceVersion=@SourceVersion;
                    """, connection, transaction);
                complete.Parameters.AddWithValue("@DocumentId", documentId);
                complete.Parameters.AddWithValue("@DocumentType", documentType);
                complete.Parameters.AddWithValue("@BusinessId", businessId);
                complete.Parameters.AddWithValue("@SourceVersion", sourceVersion);
                complete.Parameters.AddWithValue("@CompletedAt", completedAt);
                if (await complete.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new DBConcurrencyException(
                        "The sales reporting job could not be completed.");
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                transaction.Rollback("BeforeProjection");
                var completedAt = timeProvider.GetUtcNow();
                await using var fail = new SqlCommand("""
                    UPDATE reporting.SalesReportingJobs
                    SET Status=N'Failed',CompletedAt=@CompletedAt,LastError=@Error
                    WHERE SourceDocumentId=@DocumentId
                      AND SourceDocumentType=@DocumentType
                      AND BusinessId=@BusinessId AND Status=N'Processing'
                      AND SourceVersion=@SourceVersion;
                    """, connection, transaction);
                fail.Parameters.AddWithValue("@DocumentId", documentId);
                fail.Parameters.AddWithValue("@DocumentType", documentType);
                fail.Parameters.AddWithValue("@BusinessId", businessId);
                fail.Parameters.AddWithValue("@SourceVersion", sourceVersion);
                fail.Parameters.AddWithValue("@CompletedAt", completedAt);
                fail.Parameters.AddWithValue(
                    "@Error", error.Message.Length <= 2000
                        ? error.Message : error.Message[..2000]);
                await fail.ExecuteNonQueryAsync(CancellationToken.None);
                await transaction.CommitAsync(CancellationToken.None);
                committed = true;
                throw;
            }

            await transaction.CommitAsync(cancellationToken);
            committed = true;
        }
        catch
        {
            if (!committed)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<ReportingSource?> LockSourceAsync(
        SqlConnection connection, SqlTransaction transaction,
        Guid documentId, string documentType, Guid businessId,
        long sourceVersion,
        CancellationToken cancellationToken,bool rebuilding)
    {
        var sql=(rebuilding?"":TransactionLockSql+"\n"+"""
            IF EXISTS(SELECT 1 FROM reporting.SalesReportingCheckpoints WHERE BusinessId=@BusinessId AND RebuildInProgress=1)
              THROW 51619,N'La reconstrucción explícita de reporting debe completarse.',1;
            """)+"\n"+"""
            SELECT COALESCE(r.SourcePayloadJson,p.PayloadJson),r.Status,r.SourcePayloadHash,b.TenantId
            FROM reporting.SalesReportingJobs r WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.Businesses b ON b.BusinessId=r.BusinessId
            LEFT JOIN dbo.DocumentProcessingPayloads p
              ON p.DocumentId=r.SourceDocumentId
             AND p.DocumentType=r.SourceDocumentType
             AND p.BusinessId=r.BusinessId
             AND p.PayloadHash=r.SourcePayloadHash
            LEFT JOIN dbo.DocumentProcessingJobs j
              ON j.JobId=r.SourceDocumentProcessingJobId
            WHERE r.SourceDocumentId=@DocumentId
              AND r.SourceDocumentType=@DocumentType
              AND r.BusinessId=@BusinessId AND r.SourceVersion=@SourceVersion
              AND ((r.SourceDocumentProcessingJobId IS NULL AND r.SourcePayloadJson IS NOT NULL)
                OR j.Status=N'Completed');
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@ReportingLockResource",ReportingLockResource(businessId));
        command.Parameters.AddWithValue("@ReportingLockMode","Shared");
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@DocumentType", documentType);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@SourceVersion", sourceVersion);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var payload=reader.GetString(0);var expected=(byte[])reader[2];
        var actual=SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        if (!CryptographicOperations.FixedTimeEquals(expected,actual))
            throw new InvalidOperationException("The immutable reporting source hash is invalid.");
        return new ReportingSource(payload,reader.GetString(1),reader.GetGuid(3));
    }

    private static void ValidateSourceIdentity(Guid tenant,Guid business,Guid document,Guid expectedTenant,Guid expectedBusiness,Guid expectedDocument)
    {
        if(tenant!=expectedTenant||business!=expectedBusiness||document!=expectedDocument)
            throw new InvalidOperationException("La identidad de la fuente inmutable no corresponde al trabajo de reporting.");
    }

    private sealed record ReportingSource(string Payload, string Status,Guid TenantId);
}

// Read only fields consumed by the projection. The uploaded unit-cost estimate
// is deliberately absent: historical sources may contain null, and recognized
// cost is owned by the ledger or the frozen generic-product line.
internal sealed record SalesReportingSaleSource(
    Guid TenantId, Guid BusinessId, Guid WarehouseId, Guid WorkSessionId,
    Guid SoldByUserId, Guid DocumentId,
    PosSaleCommercialSnapshotContract CommercialSnapshot,
    IReadOnlyList<SalesReportingSaleSourceLine> Lines,
    IReadOnlyList<PosSalePaymentContract> Payments,
    PosSaleUblSnapshotContract? UblSnapshot = null,
    Guid? CustomerId = null, Guid? SourceOrderId = null,
    PosSaleCreditContract? Credit = null, Guid? CustomerPartySiteId = null,
    IReadOnlyList<AppliedInvoiceCharge>? Charges = null,
    decimal RestaurantTipAmount = 0m);

internal sealed record SalesReportingSaleSourceLine(
    int LineNumber, Guid ProductId, string TaxCode, decimal Quantity,
    decimal DiscountAmount, decimal TaxAmount, decimal UntaxedAmount,
    decimal LineTotal, decimal TaxRate);
