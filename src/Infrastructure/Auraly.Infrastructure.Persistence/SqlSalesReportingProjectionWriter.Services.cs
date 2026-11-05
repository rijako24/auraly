using System.Data;
using System.Text.Json;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlSalesReportingProjectionWriter
{
    public async Task ProjectServiceInvoiceAsync(
        SalesReportingSqlSession session, ServiceInvoiceSnapshot value, CancellationToken cancellationToken)
    {
        var local = await ResolveLocalDateAsync(session, value.BusinessId,
            value.CommercialSnapshot.IssuedAt, cancellationToken);
        var now = timeProvider.GetUtcNow();
        await using var command = new SqlCommand("""
            INSERT reporting.SalesReportDocuments
              (DocumentId,TenantId,BusinessId,DocumentType,DocumentNumber,FiscalNumber,IssuedAt,
               BusinessLocalDate,TimeZoneId,SellerId,SellerName,CustomerId,PartySiteId,
               CustomerIdentification,CustomerName,SourceMode,FiscalStatus,CurrencyCode,
               GrossAmount,DiscountAmount,UntaxedAmount,TaxAmount,TotalAmount,RoundingAdjustmentAmount,
               CreditAmount,CollectedAmount,RecognizedCostAmount,ProjectionVersion,SourcePayloadHash,
               ProjectedAt,BilledByUserId,BilledByName,AttributionSnapshotVersion,MissingCostLineCount,BusinessLocalHour)
            SELECT d.DocumentId,@Tenant,d.BusinessId,d.DocumentType,d.DocumentNumber,d.FiscalNumber,d.IssuedAt,
               @Date,@Zone,d.SellerIdSnapshot,COALESCE(d.SellerNameSnapshot,N'Sin vendedor'),d.CustomerId,d.CustomerPartySiteId,
               d.CustomerIdentification,COALESCE(d.CustomerNameSnapshot,@CustomerName),d.SourceMode,d.FiscalStatus,@Currency,
               @Gross,@Discount,d.UntaxedAmount,d.TaxAmount,d.PayableAmount,@Rounding,
               d.CreditAmount,@Collected,NULL,@Version,d.PayloadHash,@Now,d.SoldByUserId,d.SoldByNameSnapshot,d.AttributionSnapshotVersion,@LineCount,@LocalHour
            FROM dbo.SalesDocuments d JOIN dbo.Businesses b ON b.BusinessId=d.BusinessId AND b.TenantId=@Tenant
            WHERE d.DocumentId=@Document AND d.BusinessId=@Business AND d.DocumentType=N'ServiceInvoice';
            IF @@ROWCOUNT<>1 THROW 51611,N'No existe la fuente autorizada de la factura de servicio.',1;

            INSERT reporting.ServiceSalesReportLineFacts
              (FactId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,SourceLineNumber,
               OriginalSaleDocumentId,MovementType,OccurredAt,BusinessLocalDate,BusinessLocalHour,SellerId,CustomerId,
               BillableServiceId,ServiceCode,ServiceName,UnitCode,Quantity,GrossAmount,DiscountAmount,
               UntaxedAmount,TaxCode,TaxRate,TaxAmount,TotalAmount,ProjectionVersion,ProjectedAt)
            SELECT l.FactId,@Tenant,@Business,@Document,N'ServiceInvoice',l.LineNumber,
               @Document,N'Sale',@At,@Date,@LocalHour,d.SellerId,d.CustomerId,
               l.BillableServiceId,l.ServiceCode,l.Description,l.UnitCode,l.Quantity,l.UntaxedAmount+l.DiscountAmount,
               l.DiscountAmount,l.UntaxedAmount,l.TaxCode,l.TaxRate,l.TaxAmount,l.LineTotal,@Version,@Now
            FROM OPENJSON(@Lines) WITH(FactId uniqueidentifier,LineNumber int,BillableServiceId uniqueidentifier,
              ServiceCode nvarchar(80),Description nvarchar(500),UnitCode nvarchar(24),Quantity decimal(19,6),
              DiscountAmount decimal(19,4),UntaxedAmount decimal(19,4),TaxCode nvarchar(16),TaxRate decimal(9,6),
              TaxAmount decimal(19,4),LineTotal decimal(19,4)) l
            JOIN reporting.SalesReportDocuments d ON d.DocumentId=@Document AND d.BusinessId=@Business;
            IF @@ROWCOUNT<>@LineCount THROW 51612,N'El detalle proyectado del servicio está incompleto.',1;

            INSERT reporting.SalesReportPaymentFacts
              (SourceDocumentId,SourceDocumentType,PaymentNumber,TenantId,BusinessId,BusinessLocalDate,
               MovementType,MethodCode,Amount,Reference,ProjectedAt)
            SELECT @Document,N'ServiceInvoice',1,@Tenant,@Business,@Date,N'Payment',@Method,@Collected,@Reference,@Now
            WHERE @Collected>0
            UNION ALL
            SELECT @Document,N'ServiceInvoice',0,@Tenant,@Business,@Date,N'Credit',N'Credit',@Credit,NULL,@Now
            WHERE @Credit>0;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@Tenant", value.TenantId);
        command.Parameters.AddWithValue("@Business", value.BusinessId);
        command.Parameters.AddWithValue("@Document", value.DocumentId);
        command.Parameters.AddWithValue("@Date", local.Date.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@Zone", local.TimeZoneId);
        command.Parameters.AddWithValue("@LocalHour",local.Hour);
        command.Parameters.AddWithValue("@At", value.CommercialSnapshot.IssuedAt);
        command.Parameters.AddWithValue("@CustomerName", value.UblSnapshot.Customer.RegistrationName);
        command.Parameters.AddWithValue("@Currency", value.UblSnapshot.CurrencyCode);
        command.Parameters.AddWithValue("@Version", ProjectionVersion);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.AddWithValue("@Method", value.Payment.MethodCode);
        command.Parameters.AddWithValue("@Reference", (object?)value.Payment.Reference ?? DBNull.Value);
        var discount = value.Lines.Sum(line => line.DiscountAmount);
        AddDecimal(command,"@Gross",value.CommercialSnapshot.UntaxedAmount+discount,19,4);
        AddDecimal(command,"@Discount",discount,19,4);
        AddDecimal(command,"@Rounding",value.CommercialSnapshot.PayableRoundingAmount,19,4);
        AddDecimal(command,"@Collected",value.Payment.CollectedAmount,19,4);
        AddDecimal(command,"@Credit",value.Credit?.Amount ?? 0m,19,4);
        var lines = value.Lines.Select(line => new { FactId=ids.NewId(),line.LineNumber,line.BillableServiceId,
            line.ServiceCode,line.Description,line.UnitCode,line.Quantity,line.DiscountAmount,line.UntaxedAmount,
            line.TaxCode,line.TaxRate,line.TaxAmount,line.LineTotal }).ToArray();
        command.Parameters.Add("@Lines",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(lines);
        command.Parameters.AddWithValue("@LineCount",lines.Length);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await InsertTaxFactsAsync(session,value.DocumentId,ServiceInvoiceDocumentTypes.ServiceInvoice,
            value.TenantId,value.BusinessId,local.Date,value.Lines.Select(line =>
                new TaxFact(line.TaxCode,line.TaxRate,line.UntaxedAmount,line.TaxAmount,line.LineTotal)),
            1m,now,cancellationToken,occurredAt:value.CommercialSnapshot.IssuedAt,localHour:local.Hour);
        await ApplyDimensionDeltasAsync(session,value.BusinessId,value.DocumentId,ServiceInvoiceDocumentTypes.ServiceInvoice,1,now,cancellationToken,
            value.TenantId,local.Date,value.CommercialSnapshot.PayableRoundingAmount);
        await ApplyDailyDeltaAsync(session,value.BusinessId,local.Date,1,0,0,
            value.CommercialSnapshot.UntaxedAmount+discount,discount,0,
            value.CommercialSnapshot.UntaxedAmount,value.CommercialSnapshot.TaxAmount,
            value.CommercialSnapshot.PayableAmount,0,0,value.Credit?.Amount ?? 0m,
            value.Payment.CollectedAmount,0,now,cancellationToken,unknownCostDocumentCount:1);
        await UpdateCheckpointAsync(session,value.BusinessId,value.DocumentId,
            ServiceInvoiceDocumentTypes.ServiceInvoice,now,cancellationToken);
    }
}
