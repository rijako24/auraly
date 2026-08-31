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
               CustomerId,UntaxedAmount,TaxAmount,PayableAmount,CreditAmount,CreditDueDate,
               CufeReceived,CufeCalculated,FiscalStatus,ProcessingStatus,ReceivedAt,ProcessedAt,
               SoldByUserId)
            VALUES(@DocumentId,@BusinessId,NULL,NULL,N'Online',@DocumentSeriesId,
               @DocumentNumber,@DocumentPrefix,@SeriesCode,@DocumentConsecutive,
               @FiscalSeriesId,@AuthorizationId,N'ServiceInvoice',@IdempotencyKey,@Hash,@RequestHash,
               @FiscalNumber,@FiscalPrefix,@FiscalConsecutive,@Now,@CustomerIdentification,
               @CustomerId,@Untaxed,@Tax,@Payable,@CreditAmount,@CreditDueDate,@Cufe,@Cufe,
               N'PendingGeneration',N'Processed',@Now,@Now,@SoldByUserId);

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

            INSERT dbo.AccountingSourceDocuments
              (SourceDocumentId,SourceDocumentType,TenantId,BusinessId,PayloadJson,
               PayloadHash,OccurredAt,AcceptedAt)
            VALUES(@DocumentId,N'ServiceInvoice',@TenantId,@BusinessId,@Payload,
               @Hash,@Now,@Now);
            INSERT dbo.AccountingPostingJobs
              (AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,
               SourceDocumentType,SourcePayloadHash,OccurredAt,Status,AttemptCount,CreatedAt)
            VALUES(@AccountingJobId,@TenantId,@BusinessId,@DocumentId,
               N'ServiceInvoice',@Hash,@Now,N'Pending',0,@Now);
            INSERT reporting.SalesReportingJobs
              (SalesReportingJobId,BusinessId,SourceDocumentId,SourceDocumentType,
               SourceVersion,SourcePayloadHash,SourcePayloadJson,Status,AttemptCount,CreatedAt)
            VALUES(@ReportingJobId,@BusinessId,@DocumentId,N'ServiceInvoice',1,
               @Hash,@Payload,N'Pending',0,@Now);
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
        Add(command, "@ReportingJobId", ids.NewId());
        await command.ExecuteNonQueryAsync(cancellationToken);

        foreach (var line in snapshot.Lines)
        {
            await using var detail = new SqlCommand("""
                INSERT sales.SalesDocumentServiceLines
                  (DocumentId,LineNumber,BillableServiceId,ServiceCode,Description,
                   UnitCode,TaxCode,TaxName,TaxRate,Quantity,UnitPrice,DiscountAmount,
                   UntaxedAmount,TaxAmount,LineTotal)
                VALUES(@DocumentId,@Line,@ServiceId,@Code,@Description,@UnitCode,
                   @TaxCode,@TaxName,@TaxRate,@Quantity,@UnitPrice,@Discount,
                   @Untaxed,@Tax,@Total);
                """, connection, transaction);
            Add(detail, "@DocumentId", snapshot.DocumentId);
            Add(detail, "@Line", line.LineNumber);
            Add(detail, "@ServiceId", line.BillableServiceId);
            Add(detail, "@Code", line.ServiceCode);
            Add(detail, "@Description", line.Description);
            Add(detail, "@UnitCode", line.UnitCode);
            Add(detail, "@TaxCode", line.TaxCode);
            Add(detail, "@TaxName", line.TaxName);
            Add(detail, "@TaxRate", line.TaxRate);
            Add(detail, "@Quantity", line.Quantity);
            Add(detail, "@UnitPrice", line.UnitPrice);
            Add(detail, "@Discount", line.DiscountAmount);
            Add(detail, "@Untaxed", line.UntaxedAmount);
            Add(detail, "@Tax", line.TaxAmount);
            Add(detail, "@Total", line.LineTotal);
            if (await detail.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException(
                    "No fue posible persistir el detalle de servicio.");
        }
    }

    private static void Add(SqlCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
}
