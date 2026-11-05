using System.Data;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

internal sealed record ServiceInvoiceDocumentWrite(
    Guid TenantId,
    Guid CustomerId,
    ServiceInvoiceSnapshot Snapshot,
    SqlOnlineSalesDraftStore.CheckoutConfiguration Configuration,
    string IdempotencyKey,
    byte[]? RequestHash,
    decimal CreditAmount,
    DateTimeOffset? CreditDueDate,
    string PaymentMethodCode,
    string? PaymentReference,
    DateTimeOffset PaymentRegisteredAt);

/// <summary>
/// Canonical persistence boundary for every online service invoice. The caller owns
/// the serializable transaction so subscription settlement can append its own link
/// and capacity changes atomically without duplicating the document writer.
/// </summary>
internal static class SqlServiceInvoiceDocumentWriter
{
    public static async Task PersistAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IAuralyIdGenerator ids,
        ServiceInvoiceDocumentWrite write,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var snapshot = write.Snapshot;
        var configuration = write.Configuration;
        var payload = ServiceInvoiceSnapshotSerializer.Serialize(snapshot);
        var hash = ServiceInvoiceSnapshotSerializer.Hash(snapshot);
        await using var command = new SqlCommand("""
            SET XACT_ABORT ON;
            INSERT dbo.SalesDocuments
              (DocumentId,BusinessId,WarehouseId,DeviceId,SourceMode,DocumentSeriesId,
               DocumentNumber,DocumentPrefix,DocumentSeriesCode,DocumentConsecutive,
               FiscalSeriesId,FiscalAuthorizationId,DocumentType,IdempotencyKey,PayloadHash,RequestHash,
               FiscalNumber,FiscalPrefix,FiscalConsecutive,IssuedAt,CustomerIdentification,
               CustomerId,CustomerPartySiteId,UntaxedAmount,TaxAmount,PayableAmount,CreditAmount,CreditDueDate,
               CufeReceived,CufeCalculated,FiscalStatus,ProcessingStatus,ReceivedAt,ProcessedAt,
               SoldByUserId,SoldByNameSnapshot,CustomerNameSnapshot,SellerIdSnapshot,SellerNameSnapshot,AttributionSnapshotVersion)
            VALUES(@DocumentId,@BusinessId,NULL,NULL,N'Online',@DocumentSeriesId,
               @DocumentNumber,@DocumentPrefix,@SeriesCode,@DocumentConsecutive,
               @FiscalSeriesId,@AuthorizationId,N'ServiceInvoice',@IdempotencyKey,@Hash,@RequestHash,
               @FiscalNumber,@FiscalPrefix,@FiscalConsecutive,@Now,@CustomerIdentification,
               @CustomerId,@CustomerPartySiteId,@Untaxed,@Tax,@Payable,@CreditAmount,@CreditDueDate,@Cufe,@Cufe,
               N'PendingGeneration',N'Processed',@Now,@Now,@SoldByUserId,
               (SELECT CONCAT(FirstName,N' ',LastName) FROM dbo.AppUsers WHERE UserId=@SoldByUserId AND TenantId=@TenantId),
               @CustomerName,
               (SELECT seller.SellerId FROM dbo.CommerceSellers seller JOIN dbo.AppUsers actor ON actor.PartyId=seller.PartyId
                  WHERE actor.UserId=@SoldByUserId AND actor.TenantId=@TenantId AND seller.TenantId=@TenantId),
               (SELECT COALESCE(NULLIF(p.DisplayName,N''),NULLIF(p.LegalName,N''),NULLIF(CONCAT(p.FirstName,N' ',p.LastName),N' '),seller.Code)
                  FROM dbo.CommerceSellers seller JOIN dbo.Parties p ON p.PartyId=seller.PartyId
                  JOIN dbo.AppUsers actor ON actor.PartyId=seller.PartyId
                  WHERE actor.UserId=@SoldByUserId AND actor.TenantId=@TenantId AND seller.TenantId=@TenantId),1);

            INSERT sales.SalesDocumentServiceLines
              (DocumentId,LineNumber,BillableServiceId,ServiceCode,Description,UnitCode,
               TaxCode,TaxName,TaxRate,Quantity,UnitPrice,DiscountAmount,UntaxedAmount,TaxAmount,LineTotal)
            SELECT @DocumentId,lineNumber,billableServiceId,serviceCode,description,unitCode,
              taxCode,taxName,taxRate,quantity,unitPrice,discountAmount,untaxedAmount,taxAmount,lineTotal
            FROM OPENJSON(@Payload,'$.lines') WITH(
              lineNumber int,billableServiceId uniqueidentifier,serviceCode nvarchar(80),
              description nvarchar(500),unitCode nvarchar(24),taxCode nvarchar(16),taxName nvarchar(160),
              taxRate decimal(9,6),quantity decimal(19,6),unitPrice decimal(19,6),
              discountAmount decimal(19,4),untaxedAmount decimal(19,4),taxAmount decimal(19,4),lineTotal decimal(19,4));
            IF @@ROWCOUNT<>@LineCount THROW 51610,N'No fue posible persistir el detalle de servicio.',1;

            IF @PaymentAmount>0
              INSERT dbo.SalesPayments
                (DocumentId,PaymentNumber,MethodCode,Amount,Reference,RegisteredAt)
              VALUES(@DocumentId,1,@PaymentMethod,@PaymentAmount,@PaymentReference,@PaidAt);

            INSERT dbo.FiscalDocuments
              (DocumentId,BusinessId,SourceDocumentType,FiscalDocumentType,
               AuralyDocumentNumber,FiscalNumber,UniqueCodeType,UniqueCode,IssuedAt,
               FiscalStatus,CreatedAt,UpdatedAt)
            VALUES(@DocumentId,@BusinessId,N'ServiceInvoice',N'Invoice',
               @DocumentNumber,@FiscalNumber,N'CUFE',@Cufe,@Now,
               N'PendingGeneration',@Now,@Now);
            INSERT dbo.FiscalDocumentProcesses
              (DocumentId,BusinessId,FiscalIssuerConfigurationId,Status,AttemptCount,
               CreatedAt,UpdatedAt)
            VALUES(@DocumentId,@BusinessId,@IssuerId,N'PendingGeneration',0,@Now,@Now);
            INSERT sales.SalesDocumentServiceFiscalSnapshots
              (DocumentId,SnapshotJson,PayloadHash,Environment,CreatedAt)
            VALUES(@DocumentId,@Payload,@Hash,@Environment,@Now);

            DECLARE @Sources nvarchar(max)=(SELECT @DocumentId DocumentId,N'ServiceInvoice' DocumentType,
              @BusinessId BusinessId,@Payload Payload,CONVERT(varchar(64),@Hash,2) Hash,
              @Now OccurredAt,@AccountingJobId JobId FOR JSON PATH);
            EXEC dbo.AccountingSourcesAccept @TenantId,@Sources,@Now,1;
            """, connection, transaction);
        Add(command, "@DocumentId", snapshot.DocumentId);
        Add(command, "@BusinessId", snapshot.BusinessId);
        Add(command, "@DocumentSeriesId", snapshot.DocumentNumber.SeriesId);
        Add(command, "@DocumentNumber", snapshot.DocumentNumber.FullNumber);
        Add(command, "@DocumentPrefix", snapshot.DocumentNumber.Prefix);
        Add(command, "@SeriesCode", snapshot.DocumentNumber.SeriesCode);
        Add(command, "@DocumentConsecutive", snapshot.DocumentNumber.Consecutive);
        Add(command, "@FiscalSeriesId", snapshot.FiscalSnapshot.SeriesId);
        Add(command, "@AuthorizationId", snapshot.FiscalSnapshot.FiscalAuthorizationId);
        Add(command, "@IdempotencyKey", write.IdempotencyKey);
        command.Parameters.Add("@Hash", SqlDbType.Binary, 32).Value = hash;
        command.Parameters.Add("@RequestHash", SqlDbType.Binary, 32).Value =
            write.RequestHash is null ? DBNull.Value : write.RequestHash;
        Add(command, "@FiscalNumber", snapshot.FiscalSnapshot.FiscalNumber);
        Add(command, "@FiscalPrefix", snapshot.FiscalSnapshot.Prefix);
        Add(command, "@FiscalConsecutive", snapshot.FiscalSnapshot.Consecutive);
        Add(command, "@Now", now);
        Add(command, "@CustomerIdentification", snapshot.CommercialSnapshot.CustomerIdentification);
        Add(command, "@CustomerId", write.CustomerId);
        Add(command, "@CustomerName", snapshot.UblSnapshot.Customer.RegistrationName);
        Add(command, "@LineCount", snapshot.Lines.Count);
        Add(command, "@CustomerPartySiteId", snapshot.CustomerPartySiteId);
        Add(command, "@Untaxed", snapshot.CommercialSnapshot.UntaxedAmount);
        Add(command, "@Tax", snapshot.CommercialSnapshot.TaxAmount);
        Add(command, "@Payable", snapshot.CommercialSnapshot.PayableAmount);
        Add(command, "@CreditAmount", write.CreditAmount);
        Add(command, "@CreditDueDate", write.CreditDueDate);
        Add(command, "@Cufe", snapshot.FiscalSnapshot.Cufe);
        Add(command, "@PaymentAmount", snapshot.Payment.Amount);
        Add(command, "@PaymentMethod", write.PaymentMethodCode);
        Add(command, "@PaymentReference", write.PaymentReference);
        Add(command, "@PaidAt", write.PaymentRegisteredAt);
        Add(command, "@SoldByUserId", snapshot.SoldByUserId);
        Add(command, "@IssuerId", configuration.FiscalIssuerConfigurationId);
        Add(command, "@Payload", payload);
        Add(command, "@Environment", (byte)configuration.Environment);
        Add(command, "@TenantId", write.TenantId);
        Add(command, "@AccountingJobId", ids.NewId());
        await command.ExecuteNonQueryAsync(cancellationToken);

        await SqlSalesReportingJobWriter.InsertServiceInvoiceAsync(connection, transaction,
            ids.NewId(), snapshot.BusinessId, snapshot.DocumentId, payload, hash, now, cancellationToken);
    }

    private static void Add(SqlCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
}
