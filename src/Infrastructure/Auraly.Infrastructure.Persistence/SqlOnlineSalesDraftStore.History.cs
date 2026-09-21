using System.Data;
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
              ON cp.CustomerId=c.CustomerId AND cp.BusinessId=c.BusinessId
            OUTER APPLY(SELECT SUM(r.OutstandingAmount) Outstanding
                        FROM dbo.Receivables r
                        WHERE r.CustomerId=c.CustomerId AND r.BusinessId=c.BusinessId
                          AND r.Status IN(N'Open',N'PartiallyPaid')) balance
            WHERE c.BusinessId=@BusinessId AND c.IsActive=1 AND p.IsActive=1
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
              ON t.TaxProfileId=p.TaxProfileId AND t.BusinessId=@BusinessId AND t.IsActive=1
            WHERE p.TenantId=@TenantId AND p.BusinessId=@BusinessId AND p.IsActive=1
              AND(@Search=N'' OR p.Name LIKE @Contains OR p.ProductCode LIKE @Prefix
                  OR p.Sku LIKE @Prefix OR p.Reference LIKE @Prefix
                  OR EXISTS(SELECT 1 FROM dbo.ProductBarcodes barcode
                    WHERE barcode.ProductId=p.ProductId AND barcode.BusinessId=@BusinessId
                      AND barcode.IsActive=1 AND barcode.Barcode LIKE @Prefix)
                  OR EXISTS(SELECT 1 FROM dbo.ProductIdentifiers identifier
                    WHERE identifier.ProductId=p.ProductId AND identifier.BusinessId=@BusinessId
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
        var businessId = await ResolveHistoryBusinessAsync(
            connection,
            transaction,
            user,
            request.Context,
            cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT d.DocumentId,d.DocumentType,d.DocumentNumber,d.FiscalNumber,d.IssuedAt,
                   d.PayableAmount,d.CustomerIdentification,d.FiscalStatus,
                   COALESCE(
                     NULLIF(JSON_VALUE(payload.PayloadJson,'$.commercialSnapshot.customerName'),N''),
                     NULLIF(JSON_VALUE(payload.PayloadJson,'$.ublSnapshot.customer.registrationName'),N''),
                     N'Consumidor final') CustomerName
            FROM dbo.SalesDocuments d
            JOIN dbo.DocumentProcessingPayloads payload
              ON payload.DocumentId=d.DocumentId
             AND payload.DocumentType=d.DocumentType
             AND payload.BusinessId=d.BusinessId
            WHERE d.BusinessId=@BusinessId
              AND ISNULL(JSON_VALUE(payload.PayloadJson,'$.fiscalHabilitationOnly'),N'false')<>N'true'
              AND (@CustomerId IS NULL OR
                   (d.CustomerId=@CustomerId AND d.CustomerPartySiteId=@PartySiteId))
              AND (@From IS NULL OR d.IssuedAt>=@From)
              AND (@ToExclusive IS NULL OR d.IssuedAt<@ToExclusive)
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
            """;
        var search = request.Search?.Trim() ?? string.Empty;
        command.Parameters.AddRange([
            P("@BusinessId", businessId),
            P("@CustomerId", request.CustomerId),
            P("@PartySiteId", request.PartySiteId),
            P("@From", request.From?.ToDateTime(TimeOnly.MinValue)),
            P("@ToExclusive", request.To?.AddDays(1).ToDateTime(TimeOnly.MinValue)),
            P("@ProductId", request.ProductId),
            P("@MinimumTotal", request.MinimumTotal),
            P("@MaximumTotal", request.MaximumTotal),
            P("@Search", search),
            P("@Contains", $"%{search}%"),
            P("@Skip", request.Skip),
            P("@Take", request.Take + 1)
        ]);
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
                    reader.IsDBNull(7) ? null : reader.GetString(7)));
            }
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

    private static async Task<Guid> ResolveHistoryBusinessAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        OnlineSalesUserIdentity user,
        OnlineSalesHistoryContext requested,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT BusinessId
            FROM dbo.Businesses
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId AND IsActive=1;
            """;
        command.Parameters.AddRange([
            P("@BusinessId", requested.BusinessId),
            P("@TenantId", user.TenantId)
        ]);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid businessId
            ? businessId
            : throw new OnlineSalesDraftForbiddenException(
                "La sede no pertenece al tenant autenticado.");
    }
}
