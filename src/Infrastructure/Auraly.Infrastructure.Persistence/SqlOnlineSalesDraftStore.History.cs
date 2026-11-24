using System.Data;
using Auraly.BuildingBlocks.Infrastructure.Persistence;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlOnlineSalesDraftStore
{
    public async Task<OnlineSalesCustomerPage> SearchCustomersAsync(
        OnlineSalesUserIdentity user,
        SearchOnlineSalesHistoryOptionsRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, cancellationToken);
        var businessId = await ResolveHistoryBusinessAsync(
            connection, transaction, user, request.Context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT c.CustomerId,COALESCE(p.Identification,N''),
                   COALESCE(p.DisplayName,p.LegalName,
                            NULLIF(LTRIM(RTRIM(CONCAT(p.FirstName,N' ',p.LastName))),N''),
                            N'Sin nombre'),
                   s.PriceChannelId,c.RequiresElectronicInvoice,
                   CAST(COALESCE(cp.IsCreditEnabled,0) AS bit),
                   CASE WHEN cp.CreditLimit IS NULL THEN NULL
                        ELSE CASE WHEN cp.CreditLimit-COALESCE(balance.Outstanding,0)<0 THEN 0
                                  ELSE cp.CreditLimit-COALESCE(balance.Outstanding,0) END END,
                   site.PartySiteId,site.Name,site.AddressLine
            FROM dbo.Customers c
            JOIN dbo.Parties p ON p.PartyId=c.PartyId
            JOIN dbo.PartySites site ON site.PartyId=p.PartyId AND site.IsActive=1
            LEFT JOIN dbo.CustomerPricingSettings s ON s.CustomerId=c.CustomerId
              AND s.ValidFrom<=SYSDATETIMEOFFSET()
              AND(s.ValidUntil IS NULL OR s.ValidUntil>SYSDATETIMEOFFSET())
            LEFT JOIN dbo.CustomerCreditProfiles cp
              ON cp.CustomerId=c.CustomerId AND cp.BusinessId=@BusinessId
            OUTER APPLY(SELECT SUM(r.OutstandingAmount) Outstanding
                        FROM dbo.Receivables r
                        WHERE r.CustomerId=c.CustomerId AND r.BusinessId=@BusinessId
                          AND r.Status IN(N'Open',N'PartiallyPaid')) balance
            WHERE c.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId) AND c.IsActive=1 AND p.IsActive=1
              AND (@Search=N'' OR NOT EXISTS(
                   SELECT 1 FROM STRING_SPLIT(@Search,N' ') term
                   WHERE NULLIF(LTRIM(RTRIM(term.value)),N'') IS NOT NULL
                     AND NOT (COALESCE(p.Identification,N'') LIKE N'%'+LTRIM(RTRIM(term.value))+N'%'
                              OR COALESCE(p.DisplayName,N'') LIKE N'%'+LTRIM(RTRIM(term.value))+N'%'
                              OR COALESCE(p.LegalName,N'') LIKE N'%'+LTRIM(RTRIM(term.value))+N'%'
                              OR COALESCE(p.FirstName,N'') LIKE N'%'+LTRIM(RTRIM(term.value))+N'%'
                              OR COALESCE(p.LastName,N'') LIKE N'%'+LTRIM(RTRIM(term.value))+N'%'
                              OR site.Name LIKE N'%'+LTRIM(RTRIM(term.value))+N'%'
                              OR site.Code LIKE N'%'+LTRIM(RTRIM(term.value))+N'%'
                              OR site.AddressLine LIKE N'%'+LTRIM(RTRIM(term.value))+N'%'
                              OR COALESCE(site.Phone,N'') LIKE N'%'+LTRIM(RTRIM(term.value))+N'%')))
            ORDER BY CASE WHEN p.Identification=@Search THEN 0 ELSE 1 END,
                     COALESCE(p.DisplayName,p.LegalName,p.FirstName),site.Name,site.PartySiteId
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            """;
        var search = request.Search?.Trim() ?? string.Empty;
        command.Parameters.AddRange([
            P("@BusinessId", businessId), P("@Search", search),
            P("@Skip", request.Skip), P("@Take", request.Take + 1)
        ]);
        var items = new List<OnlineSalesCustomer>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                items.Add(new(
                    reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3),
                    reader.GetBoolean(4), reader.GetBoolean(5),
                    reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                    reader.GetGuid(7), reader.GetString(8), reader.GetString(9)));
        var hasMore = items.Count > request.Take;
        if (hasMore) items.RemoveAt(items.Count - 1);
        await transaction.CommitAsync(cancellationToken);
        return new(items, hasMore, hasMore ? request.Skip + items.Count : null);
    }

    public async Task<OnlineSalesProductPage> SearchProductsAsync(
        OnlineSalesUserIdentity user,
        SearchOnlineSalesHistoryOptionsRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, cancellationToken);
        var businessId = await ResolveHistoryBusinessAsync(
            connection, transaction, user, request.Context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT p.ProductId,COALESCE(NULLIF(p.ProductCode,N''),NULLIF(p.Sku,N''),N''),
                   p.Reference,p.Name,COALESCE(NULLIF(p.BaseUnitCode,N''),N'EA'),
                   COALESCE(t.DianTaxCode,N'01'),COALESCE(t.Rate,0),
                   p.IsActive,p.IsWeighable,p.AllowsFractionalSale
            FROM dbo.Products p
            LEFT JOIN dbo.TaxProfiles t
              ON t.TaxProfileId=p.TaxProfileId AND t.TenantId=p.TenantId AND t.IsActive=1
            WHERE p.TenantId=@TenantId AND p.IsActive=1
              AND(@Search=N'' OR p.Name LIKE @Contains OR p.ProductCode LIKE @Prefix
                  OR p.Sku LIKE @Prefix OR p.Reference LIKE @Prefix
                  OR EXISTS(SELECT 1 FROM dbo.ProductBarcodes barcode
                    WHERE barcode.ProductId=p.ProductId AND barcode.TenantId=p.TenantId
                      AND barcode.IsActive=1 AND barcode.Barcode LIKE @Prefix)
                  OR EXISTS(SELECT 1 FROM dbo.ProductIdentifiers identifier
                    WHERE identifier.ProductId=p.ProductId AND identifier.TenantId=p.TenantId
                      AND identifier.IsActive=1 AND identifier.Value LIKE @Prefix))
            ORDER BY CASE WHEN p.ProductCode=@Search OR p.Sku=@Search OR p.Reference=@Search
                          THEN 0 ELSE 1 END,p.Name,p.ProductId
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            """;
        var search = request.Search?.Trim() ?? string.Empty;
        command.Parameters.AddRange([
            P("@TenantId", user.TenantId), P("@BusinessId", businessId),
            P("@Search", search), P("@Contains", $"%{search}%"),
            P("@Prefix", $"{search}%"), P("@Skip", request.Skip),
            P("@Take", request.Take + 1)
        ]);
        var items = new List<OnlineSalesProduct>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                items.Add(new(
                    reader.GetGuid(0), reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.GetDecimal(6),
                    0m, "COP", reader.GetBoolean(7), reader.GetBoolean(8),
                    reader.GetBoolean(9), "History"));
        var hasMore = items.Count > request.Take;
        if (hasMore) items.RemoveAt(items.Count - 1);
        await transaction.CommitAsync(cancellationToken);
        return new(items, hasMore, hasMore ? request.Skip + items.Count : null);
    }

    public async Task<OnlineSalesCustomer?> GetCustomerAsync(
        OnlineSalesUserIdentity user,
        GetOnlineSalesCustomerRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
        var scope = await ResolveOnlineContextAsync(
            connection,
            transaction,
            user,
            request.Context,
            cancellationToken);
        var customer = await ReadCustomerAsync(
            connection,
            transaction,
            scope.BusinessId,
            request.CustomerId,
            request.PartySiteId,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return customer;
    }

    public async Task<OnlineSalesIssuedSalePage> SearchAsync(
        OnlineSalesUserIdentity user,
        SearchOnlineSalesIssuedSalesRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
        var historyScope = await ResolveHistoryScopeAsync(
            connection,
            transaction,
            user,
            request.Context,
            cancellationToken);
        var businessId = historyScope.BusinessId;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT d.DocumentId,d.DocumentType,d.DocumentNumber,d.FiscalNumber,d.IssuedAt,
                   d.PayableAmount,d.CustomerIdentification,d.FiscalStatus,
                   COALESCE(
                     NULLIF(JSON_VALUE(payload.PayloadJson,'$.commercialSnapshot.customerName'),N''),
                     NULLIF(JSON_VALUE(payload.PayloadJson,'$.ublSnapshot.customer.registrationName'),N''),
                     N'Consumidor final') CustomerName, d.CollectionMode, d.WorkSessionId,d.SellerNameSnapshot
            INTO #HistoryPage
            FROM dbo.SalesDocuments d
            JOIN dbo.DocumentProcessingPayloads payload
              ON payload.DocumentId=d.DocumentId
             AND payload.DocumentType=d.DocumentType
             AND payload.BusinessId=d.BusinessId
            WHERE d.BusinessId=@BusinessId
              AND ISNULL(JSON_VALUE(payload.PayloadJson,'$.fiscalHabilitationOnly'),N'false')<>N'true'
              AND (@SellerId IS NULL OR d.SellerIdSnapshot=@SellerId)
              AND (@BilledBy IS NULL OR d.SoldByUserId=@BilledBy)
              AND (@CustomerId IS NULL OR
                   (d.CustomerId=@CustomerId AND d.CustomerPartySiteId=@PartySiteId))
              AND (@From IS NULL OR d.IssuedAt>=@From)
              AND (@To IS NULL OR d.IssuedAt<@To)
              AND (@MinimumTotal IS NULL OR d.PayableAmount>=@MinimumTotal)
              AND (@MaximumTotal IS NULL OR d.PayableAmount<=@MaximumTotal)
              AND (@ProductId IS NULL OR EXISTS(
                    SELECT 1 FROM dbo.SalesDocumentLines line
                    WHERE line.DocumentId=d.DocumentId AND line.ProductId=@ProductId))
              AND (@Search=N'' OR d.DocumentNumber LIKE @Contains
                   OR d.FiscalNumber LIKE @Contains
                   OR d.CufeReceived LIKE @Contains
                   OR d.CustomerIdentification LIKE @Contains)
            ORDER BY d.IssuedAt DESC,d.DocumentId DESC
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            SELECT page.*,closure.WorkSessionClosureId,assigned.DispatchId,assigned.DispatchCount
            INTO #HistoryContext
            FROM #HistoryPage page
            LEFT JOIN dbo.WorkSessionClosures closure ON closure.WorkSessionId=page.WorkSessionId AND page.CollectionMode=N'Session'
            OUTER APPLY(SELECT COUNT_BIG(*) DispatchCount,TRY_CONVERT(uniqueidentifier,MIN(CONVERT(nvarchar(36),source.DispatchId))) DispatchId FROM dbo.DispatchSourceDocuments source
              JOIN dbo.Dispatches dispatch ON dispatch.DispatchId=source.DispatchId AND dispatch.BusinessId=@BusinessId
              WHERE source.SourceDocumentId=page.DocumentId AND dispatch.Status<>N'Cancelled') assigned
            ;
            IF EXISTS(SELECT 1 FROM #HistoryContext WHERE DispatchCount>1)
              THROW 51000,N'Una factura pertenece a varios despachos activos. Revisa su asignación.',1;
            SELECT * FROM #HistoryContext ORDER BY IssuedAt DESC,DocumentId DESC;
            SELECT DISTINCT page.DispatchId INTO #ConfirmedHistoryDispatch FROM #HistoryContext page
            JOIN dbo.WorkSessions session ON session.DispatchId=page.DispatchId AND session.Kind=N'Dispatch'
            JOIN dbo.WorkSessionClosures closure ON closure.WorkSessionId=session.WorkSessionId
            JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=closure.WorkSessionClosureId
              AND job.SourceDocumentType=N'DispatchCashDifference' AND job.TenantId=@TenantId AND job.BusinessId=@BusinessId
              AND job.Status IN(N'Posted',N'CommercialEffectsApplied');
            DECLARE @CreditSalesJson nvarchar(max)=(SELECT DocumentId FROM #HistoryPage FOR JSON PATH);
            SELECT credit.DocumentId,credit.PaymentId,credit.MethodCode,credit.Amount,credit.FormName
            INTO #HistoryCreditCollections
            FROM accounting.SaleCreditCollectionFacts(@TenantId,@BusinessId,@CreditSalesJson) credit;
            SELECT payment.DocumentId,payment.PaymentNumber,payment.EffectivePaymentReady,
              payment.EffectiveMethodCode,payment.EffectiveAmount,payment.EffectiveFormName,
              payment.Amount,payment.CollectedOutsideDispatch INTO #HistoryPaymentFacts
            FROM #HistoryPage page
            CROSS APPLY accounting.SalePaymentPostingFacts(@TenantId,@BusinessId,page.DocumentId) payment;
            SELECT page.DocumentId,payment.PaymentNumber,CASE WHEN payment.EffectivePaymentReady=1 THEN payment.EffectiveMethodCode ELSE original.MethodCode END MethodCode,
              CASE WHEN payment.EffectivePaymentReady=1 THEN payment.EffectiveAmount ELSE payment.Amount END Amount,
              CASE WHEN payment.EffectivePaymentReady=1 THEN payment.EffectiveFormName ELSE original.PaymentMethodName END FormName
            FROM #HistoryPage page JOIN #HistoryPaymentFacts payment ON payment.DocumentId=page.DocumentId
            JOIN dbo.SalesPayments original ON original.DocumentId=page.DocumentId AND original.PaymentNumber=payment.PaymentNumber
            WHERE payment.CollectedOutsideDispatch=1 AND payment.EffectiveAmount<>0
            UNION ALL
            SELECT page.DocumentId,payment.PaymentNumber,payment.MethodCode,payment.Amount+payment.RoundingAdjustment,payment.PaymentMethodName
            FROM #HistoryPage page JOIN dbo.SalesPayments payment ON payment.DocumentId=page.DocumentId
            WHERE page.CollectionMode=N'Dispatch' AND payment.Amount+payment.RoundingAdjustment<>0
              AND NOT EXISTS(SELECT 1 FROM #HistoryPaymentFacts kept WHERE kept.DocumentId=page.DocumentId AND kept.CollectedOutsideDispatch=1)
              AND NOT EXISTS(SELECT 1 FROM #HistoryContext context JOIN #ConfirmedHistoryDispatch confirmed ON confirmed.DispatchId=context.DispatchId WHERE context.DocumentId=page.DocumentId)
            UNION ALL
            SELECT page.DocumentId,CAST(ROW_NUMBER() OVER(PARTITION BY page.DocumentId ORDER BY payment.PaymentId) AS int),payment.MethodCode,payment.Amount,payment.FormName
            FROM #ConfirmedHistoryDispatch confirmed CROSS APPLY dispatch.EffectivePayments(confirmed.DispatchId) payment
            JOIN dbo.DispatchSourceDocuments source ON source.DispatchSourceDocumentId=payment.DispatchSourceDocumentId
            JOIN #HistoryPage page ON page.DocumentId=source.SourceDocumentId
            WHERE page.CollectionMode=N'Dispatch' AND payment.ApplicationType=N'InvoicePayment' AND payment.Amount<>0
            UNION ALL
            SELECT credit.DocumentId,-CONVERT(int,ROW_NUMBER() OVER(PARTITION BY credit.DocumentId ORDER BY credit.PaymentId)),
              credit.MethodCode,credit.Amount,credit.FormName FROM #HistoryCreditCollections credit
            UNION ALL
            SELECT page.DocumentId,0,N'Credit',sale.CreditAmount-COALESCE(collected.Amount,0),sale.CreditPaymentMethodName
            FROM #HistoryPage page JOIN dbo.SalesDocuments sale ON sale.DocumentId=page.DocumentId
            OUTER APPLY(SELECT SUM(Amount) Amount FROM #HistoryCreditCollections WHERE DocumentId=page.DocumentId) collected
            WHERE sale.CreditAmount>COALESCE(collected.Amount,0)
            ORDER BY DocumentId,PaymentNumber;
            """;
        var search = request.Search?.Trim() ?? string.Empty;
        command.Parameters.AddRange([
            P("@BusinessId", businessId),
            P("@CustomerId", request.CustomerId),
            P("@PartySiteId", request.PartySiteId),
            P("@SellerId", request.SellerId), P("@BilledBy", request.BilledByUserId),
            P("@TenantId", user.TenantId),
            P("@ProductId", request.ProductId),
            P("@MinimumTotal", request.MinimumTotal),
            P("@MaximumTotal", request.MaximumTotal),
            P("@Search", search),
            P("@Contains", $"%{search}%"),
            P("@Skip", request.Skip),
            P("@Take", request.Take + 1)
        ]);
        SqlBusinessLocalDates.BindDateRange(command,request.From,request.To,historyScope.Zone,request.DateRangeVersion);
        var items = new List<OnlineSalesIssuedSale>();
        await using (var reader =
                     await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetDateTimeOffset(4),
                    reader.GetDecimal(5),
                    reader.GetString(6),
                    reader.GetString(8),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    ClosureId:reader.IsDBNull(12)?null:reader.GetGuid(12),
                    DispatchId:reader.GetString(9)=="Dispatch"&&!reader.IsDBNull(13)?reader.GetGuid(13):null,
                    SellerName:reader.IsDBNull(11)?null:reader.GetString(11)));
            }
            await reader.NextResultAsync(cancellationToken);
            var payments=new Dictionary<Guid,List<OnlineSalesHistoryPayment>>();
            while(await reader.ReadAsync(cancellationToken))
            {
                var id=reader.GetGuid(0);
                if(!payments.TryGetValue(id,out var values)) payments[id]=values=[];
                values.Add(new(reader.GetInt32(1),reader.GetString(2),reader.GetDecimal(3),reader.IsDBNull(4)?null:reader.GetString(4)));
            }
            for(var index=0;index<items.Count;index++)
                items[index]=items[index] with { Payments=payments.GetValueOrDefault(items[index].DocumentId)??[] };
        }
        var hasMore = items.Count > request.Take;
        if (hasMore)
            items.RemoveAt(items.Count - 1);
        await transaction.CommitAsync(cancellationToken);
        return new(
            items,
            hasMore,
            hasMore ? request.Skip + items.Count : null);
    }

    public async Task<StoredOnlineSalesReceipt?> GetReceiptAsync(
        OnlineSalesUserIdentity user,
        OnlineSalesHistoryContext context,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
        var businessId = await ResolveHistoryBusinessAsync(
            connection,
            transaction,
            user,
            context,
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT payload.PayloadJson,document.FiscalStatus
            FROM dbo.SalesDocuments document
            JOIN dbo.DocumentProcessingPayloads payload
              ON payload.DocumentId=document.DocumentId
             AND payload.DocumentType=document.DocumentType
             AND payload.BusinessId=document.BusinessId
            WHERE document.DocumentId=@DocumentId
              AND document.BusinessId=@BusinessId
              AND ISNULL(JSON_VALUE(payload.PayloadJson,'$.fiscalHabilitationOnly'),N'false')<>N'true';
            """;
        command.Parameters.AddRange([
            P("@DocumentId", documentId),
            P("@BusinessId", businessId)
        ]);
        StoredOnlineSalesReceipt? result = null;
        await using (var reader =
                     await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
                result = new(
                    PosSaleContractSerializer.Deserialize(reader.GetString(0)),
                    reader.IsDBNull(1) ? null : reader.GetString(1));
        }
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<bool> RecordReprintAsync(
        OnlineSalesUserIdentity user,
        OnlineSalesHistoryContext context,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
        var businessId = await ResolveHistoryBusinessAsync(
            connection, transaction, user, context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT dbo.AuditLogs(
              AuditLogId,UserId,TenantId,BusinessId,Action,EntityType,EntityId,Timestamp)
            SELECT NEWID(),@UserId,@TenantId,@BusinessId,N'Sales.Reprinted',
                   N'SalesDocument',CONVERT(nvarchar(100),document.DocumentId),SYSUTCDATETIME()
            FROM dbo.SalesDocuments document
            JOIN dbo.DocumentProcessingPayloads payload
              ON payload.DocumentId=document.DocumentId
             AND payload.DocumentType=document.DocumentType
             AND payload.BusinessId=document.BusinessId
            WHERE document.DocumentId=@DocumentId
              AND document.BusinessId=@BusinessId
              AND ISNULL(JSON_VALUE(payload.PayloadJson,'$.fiscalHabilitationOnly'),N'false')<>N'true';
            """;
        command.Parameters.AddRange([
            P("@UserId", user.UserId),
            P("@TenantId", user.TenantId),
            P("@BusinessId", businessId),
            P("@DocumentId", documentId)
        ]);
        var recorded = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        await transaction.CommitAsync(cancellationToken);
        return recorded;
    }

    private static async Task<Guid> ResolveHistoryBusinessAsync(SqlConnection connection,SqlTransaction transaction,
        OnlineSalesUserIdentity user,OnlineSalesHistoryContext requested,CancellationToken ct) =>
        (await ResolveHistoryScopeAsync(connection,transaction,user,requested,ct)).BusinessId;

    private static async Task<(Guid BusinessId,TimeZoneInfo Zone)> ResolveHistoryScopeAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        OnlineSalesUserIdentity user,
        OnlineSalesHistoryContext requested,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT BusinessId,TimeZone
            FROM dbo.Businesses
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId AND IsActive=1;
            """;
        command.Parameters.AddRange([
            P("@BusinessId", requested.BusinessId),
            P("@TenantId", user.TenantId)
        ]);
        await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        if(!await reader.ReadAsync(cancellationToken))
            throw new OnlineSalesDraftForbiddenException("La sede no pertenece al tenant autenticado.");
        return(reader.GetGuid(0),TimeZoneInfo.FindSystemTimeZoneById(reader.GetString(1)));
    }
}
