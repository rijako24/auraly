using Auraly.Application.Returns;
using Auraly.Contracts.Returns;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlSalesReturnQueryStore(SqlServerConnectionFactory connections)
    : ISalesReturnQueryStore
{
    public async Task<ReturnableSalePage> ListReturnableSalesAsync(
        SalesReturnUserIdentity user, ReturnableSalesQuery query,
        CancellationToken cancellationToken)
    {
        const string sql = """
            WITH Returned AS
            (
              SELECT l.OriginalDocumentId,l.OriginalLineNumber,
                     SUM(l.Quantity) ReturnedQuantity,SUM(l.LineTotal) ReturnedTotal
              FROM dbo.SalesReturnLines l
              INNER JOIN dbo.SalesReturns r ON r.ReturnId=l.ReturnId
              WHERE r.BusinessId=@BusinessId
              GROUP BY l.OriginalDocumentId,l.OriginalLineNumber
            ), Sales AS
            (
              SELECT d.DocumentId,d.DocumentNumber,
                     COALESCE(d.FiscalNumber,N'') FiscalNumber,
                     COALESCE(d.CufeReceived,N'') CufeReceived,d.IssuedAt,
                     d.CustomerId,COALESCE(NULLIF(p.DisplayName,N''),NULLIF(p.LegalName,N''),
                       NULLIF(d.CustomerIdentification,N''),N'Consumidor final') CustomerName,
                     d.CustomerIdentification,d.WarehouseId,w.Name WarehouseName,d.PayableAmount,
                     COALESCE(SUM(x.ReturnedTotal),0) ReturnedTotal,
                     CAST(CASE WHEN SUM(CASE WHEN l.Quantity>COALESCE(x.ReturnedQuantity,0)
                                              THEN 1 ELSE 0 END)>0 THEN 1 ELSE 0 END AS BIT) HasAvailable,
                     COALESCE(d.FiscalStatus,N'No aplica') FiscalStatus
              FROM dbo.SalesDocuments d
              INNER JOIN dbo.Businesses b ON b.BusinessId=d.BusinessId AND b.TenantId=@TenantId
              INNER JOIN dbo.Warehouses w ON w.WarehouseId=d.WarehouseId
              INNER JOIN dbo.SalesDocumentLines l ON l.DocumentId=d.DocumentId
              LEFT JOIN Returned x ON x.OriginalDocumentId=l.DocumentId
                                  AND x.OriginalLineNumber=l.LineNumber
              LEFT JOIN dbo.Customers c ON c.CustomerId=d.CustomerId
              LEFT JOIN dbo.Parties p ON p.PartyId=c.PartyId
              WHERE d.BusinessId=@BusinessId
                AND d.DocumentType IN(N'SalesInvoice',N'SalesReceipt')
                AND d.ProcessingStatus=N'Completed'
                AND (@From IS NULL OR d.IssuedAt>=@From)
                AND (@To IS NULL OR d.IssuedAt<DATEADD(DAY,1,@To))
                AND (@Search IS NULL OR d.DocumentNumber LIKE N'%'+@Search+N'%'
                  OR d.FiscalNumber LIKE N'%'+@Search+N'%'
                  OR d.CufeReceived LIKE N'%'+@Search+N'%'
                  OR d.CustomerIdentification LIKE N'%'+@Search+N'%'
                  OR p.DisplayName LIKE N'%'+@Search+N'%'
                  OR p.LegalName LIKE N'%'+@Search+N'%'
                  OR EXISTS(SELECT 1 FROM dbo.Products product
                    WHERE product.ProductId=l.ProductId AND
                      (product.ProductCode LIKE N'%'+@Search+N'%' OR
                       product.Reference LIKE N'%'+@Search+N'%' OR
                       product.Name LIKE N'%'+@Search+N'%')))
                AND (@Customer IS NULL OR d.CustomerIdentification LIKE N'%'+@Customer+N'%'
                  OR p.DisplayName LIKE N'%'+@Customer+N'%'
                  OR p.LegalName LIKE N'%'+@Customer+N'%')
              GROUP BY d.DocumentId,d.DocumentNumber,d.FiscalNumber,d.CufeReceived,d.IssuedAt,
                       d.CustomerId,p.DisplayName,p.LegalName,d.CustomerIdentification,
                       d.WarehouseId,w.Name,d.PayableAmount,d.FiscalStatus
            )
            SELECT DocumentId,DocumentNumber,FiscalNumber,CufeReceived,IssuedAt,CustomerId,
                   CustomerName,CustomerIdentification,WarehouseId,WarehouseName,PayableAmount,
                   ReturnedTotal,HasAvailable,FiscalStatus,COUNT(*) OVER()
            FROM Sales
            WHERE @Available IS NULL OR HasAvailable=@Available
            ORDER BY IssuedAt DESC,DocumentId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        Scope(command, user);
        command.Parameters.AddWithValue("@Search", (object?)query.Search ?? DBNull.Value);
        command.Parameters.AddWithValue("@Customer", (object?)query.Customer ?? DBNull.Value);
        command.Parameters.AddWithValue("@From", (object?)query.From?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        command.Parameters.AddWithValue("@To", (object?)query.To?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        command.Parameters.AddWithValue("@Available", (object?)query.WithAvailableQuantity ?? DBNull.Value);
        command.Parameters.AddWithValue("@Offset", (query.Page - 1) * query.PageSize);
        command.Parameters.AddWithValue("@PageSize", query.PageSize);
        var items = new List<ReturnableSaleListItem>();
        var total = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            total = reader.GetInt32(14);
            items.Add(new ReturnableSaleListItem(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetDateTimeOffset(4), reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.GetString(6), reader.GetString(7), reader.GetGuid(8), reader.GetString(9),
                reader.GetDecimal(10), reader.GetDecimal(11), reader.GetBoolean(12), reader.GetString(13)));
        }
        return new ReturnableSalePage(items, query.Page, query.PageSize, total);
    }

    public async Task<ReturnableSale?> GetReturnableSaleAsync(
        SalesReturnUserIdentity user, Guid documentId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        const string headerSql = """
            SELECT d.DocumentNumber,COALESCE(d.FiscalNumber,N''),COALESCE(d.CufeReceived,N''),d.IssuedAt,d.CustomerId,
                   COALESCE(NULLIF(p.DisplayName,N''),NULLIF(p.LegalName,N''),
                     NULLIF(d.CustomerIdentification,N''),N'Consumidor final'),
                   d.CustomerIdentification,d.WarehouseId,w.Name,d.PayableAmount,
                   COALESCE((SELECT SUM(r.TotalAmount) FROM dbo.SalesReturns r
                             WHERE r.OriginalDocumentId=d.DocumentId),0),
                   COALESCE((SELECT SUM(r.OutstandingAmount) FROM dbo.Receivables r
                             WHERE r.SourceDocumentId=d.DocumentId
                               AND r.SourceDocumentType=N'SalesInvoice'
                               AND r.Status IN(N'Open',N'PartiallyPaid')),0),
                   COALESCE(d.FiscalStatus,N'No aplica')
            FROM dbo.SalesDocuments d
            INNER JOIN dbo.Businesses b ON b.BusinessId=d.BusinessId AND b.TenantId=@TenantId
            INNER JOIN dbo.Warehouses w ON w.WarehouseId=d.WarehouseId
            LEFT JOIN dbo.Customers c ON c.CustomerId=d.CustomerId
            LEFT JOIN dbo.Parties p ON p.PartyId=c.PartyId
            WHERE d.DocumentId=@Id AND d.BusinessId=@BusinessId
              AND d.DocumentType IN(N'SalesInvoice',N'SalesReceipt')
              AND d.ProcessingStatus=N'Completed';
            """;
        string number; string fiscal; string cufe; DateTimeOffset issued; Guid? customerId;
        string customerName; string identification; Guid warehouseId; string warehouseName;
        decimal total; decimal returned; decimal receivable; string fiscalStatus;
        await using (var command = new SqlCommand(headerSql, connection))
        {
            Scope(command, user); command.Parameters.AddWithValue("@Id", documentId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            number=reader.GetString(0); fiscal=reader.GetString(1); cufe=reader.GetString(2);
            issued=reader.GetDateTimeOffset(3); customerId=reader.IsDBNull(4)?null:reader.GetGuid(4);
            customerName=reader.GetString(5); identification=reader.GetString(6);
            warehouseId=reader.GetGuid(7); warehouseName=reader.GetString(8);
            total=reader.GetDecimal(9); returned=reader.GetDecimal(10);
            receivable=reader.GetDecimal(11); fiscalStatus=reader.GetString(12);
        }
        var payments = await LoadPaymentsAsync(connection, documentId, cancellationToken);
        var lines = await LoadLinesAsync(connection, documentId, cancellationToken);
        return new ReturnableSale(documentId, number, fiscal, cufe, issued, customerId,
            customerName, identification, warehouseId, warehouseName, total, returned,
            receivable, fiscalStatus, payments, lines);
    }

    public async Task<SalesReturnPage> ListReturnsAsync(
        SalesReturnUserIdentity user, SalesReturnQuery query, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT r.ReturnId,r.DocumentNumber,r.OriginalDocumentId,d.DocumentNumber,
                   COALESCE(NULLIF(p.DisplayName,N''),NULLIF(p.LegalName,N''),
                     NULLIF(r.CustomerIdentification,N''),N'Consumidor final'),
                   r.ReturnedAt,r.EconomicResolution,r.TotalAmount,r.Status,r.FiscalStatus,
                   r.ReasonCode,COUNT(*) OVER()
            FROM dbo.SalesReturns r
            INNER JOIN dbo.Businesses b ON b.BusinessId=r.BusinessId AND b.TenantId=@TenantId
            INNER JOIN dbo.SalesDocuments d ON d.DocumentId=r.OriginalDocumentId
            LEFT JOIN dbo.Customers c ON c.CustomerId=r.CustomerId
            LEFT JOIN dbo.Parties p ON p.PartyId=c.PartyId
            WHERE r.BusinessId=@BusinessId
              AND (@Status IS NULL OR r.Status=@Status)
              AND (@From IS NULL OR r.ReturnedAt>=@From)
              AND (@To IS NULL OR r.ReturnedAt<DATEADD(DAY,1,@To))
              AND (@Search IS NULL OR r.DocumentNumber LIKE N'%'+@Search+N'%'
                OR d.DocumentNumber LIKE N'%'+@Search+N'%'
                OR r.CustomerIdentification LIKE N'%'+@Search+N'%'
                OR p.DisplayName LIKE N'%'+@Search+N'%' OR p.LegalName LIKE N'%'+@Search+N'%')
            ORDER BY r.ReturnedAt DESC,r.ReturnId
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        await using var connection=connections.Create(); await connection.OpenAsync(cancellationToken);
        await using var command=new SqlCommand(sql,connection); Scope(command,user);
        command.Parameters.AddWithValue("@Search",(object?)query.Search??DBNull.Value);
        command.Parameters.AddWithValue("@Status",(object?)query.Status??DBNull.Value);
        command.Parameters.AddWithValue("@From",(object?)query.From?.ToDateTime(TimeOnly.MinValue)??DBNull.Value);
        command.Parameters.AddWithValue("@To",(object?)query.To?.ToDateTime(TimeOnly.MinValue)??DBNull.Value);
        command.Parameters.AddWithValue("@Offset",(query.Page-1)*query.PageSize);
        command.Parameters.AddWithValue("@PageSize",query.PageSize);
        var items=new List<SalesReturnListItem>();var total=0;
        await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken))
        {
            total=reader.GetInt32(11);
            items.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetGuid(2),reader.GetString(3),
                reader.GetString(4),reader.GetDateTimeOffset(5),reader.GetString(6),reader.GetDecimal(7),
                reader.GetString(8),reader.IsDBNull(9)?null:reader.GetString(9),reader.GetString(10)));
        }
        return new(items,query.Page,query.PageSize,total);
    }

    public async Task<SalesReturnDetail?> GetReturnAsync(
        SalesReturnUserIdentity user, Guid returnId, CancellationToken cancellationToken)
    {
        await using var connection=connections.Create();await connection.OpenAsync(cancellationToken);
        const string headerSql="""
            SELECT r.DocumentNumber,r.OriginalDocumentId,d.DocumentNumber,
                   COALESCE(NULLIF(p.DisplayName,N''),NULLIF(p.LegalName,N''),
                     NULLIF(r.CustomerIdentification,N''),N'Consumidor final'),
                   r.CustomerIdentification,r.WarehouseId,w.Name,r.ReturnedAt,r.EconomicResolution,
                   r.RefundMethodCode,r.UntaxedAmount,r.TaxAmount,r.TotalAmount,r.Status,r.FiscalStatus,
                   r.ReasonCode,r.ReasonDescription,r.Notes
            FROM dbo.SalesReturns r
            INNER JOIN dbo.Businesses b ON b.BusinessId=r.BusinessId AND b.TenantId=@TenantId
            INNER JOIN dbo.SalesDocuments d ON d.DocumentId=r.OriginalDocumentId
            INNER JOIN dbo.Warehouses w ON w.WarehouseId=r.WarehouseId
            LEFT JOIN dbo.Customers c ON c.CustomerId=r.CustomerId
            LEFT JOIN dbo.Parties p ON p.PartyId=c.PartyId
            WHERE r.ReturnId=@Id AND r.BusinessId=@BusinessId;
            """;
        string number;Guid originalId;string originalNumber;string customer;string identification;
        Guid warehouseId;string warehouse;DateTimeOffset returnedAt;string resolution;string? method;
        decimal untaxed;decimal tax;decimal total;string status;string? fiscalStatus;string reason;
        string description;string? notes;
        await using(var command=new SqlCommand(headerSql,connection))
        {
            Scope(command,user);command.Parameters.AddWithValue("@Id",returnId);
            await using var reader=await command.ExecuteReaderAsync(cancellationToken);
            if(!await reader.ReadAsync(cancellationToken))return null;
            number=reader.GetString(0);originalId=reader.GetGuid(1);originalNumber=reader.GetString(2);
            customer=reader.GetString(3);identification=reader.GetString(4);warehouseId=reader.GetGuid(5);
            warehouse=reader.GetString(6);returnedAt=reader.GetDateTimeOffset(7);resolution=reader.GetString(8);
            method=reader.IsDBNull(9)?null:reader.GetString(9);untaxed=reader.GetDecimal(10);
            tax=reader.GetDecimal(11);total=reader.GetDecimal(12);status=reader.GetString(13);
            fiscalStatus=reader.IsDBNull(14)?null:reader.GetString(14);reason=reader.GetString(15);
            description=reader.GetString(16);notes=reader.IsDBNull(17)?null:reader.GetString(17);
        }
        var lines=new List<SalesReturnLineSnapshot>();
        await using(var command=new SqlCommand("""
            SELECT LineNumber,OriginalLineNumber,ProductId,DescriptionSnapshot,Quantity,UnitPrice,
                   DiscountAmount,TaxCode,TaxRate,UntaxedAmount,TaxAmount,LineTotal,
                   RecognizedUnitCost,InventoryDisposition
            FROM dbo.SalesReturnLines WHERE ReturnId=@Id ORDER BY LineNumber;
            """,connection))
        {
            command.Parameters.AddWithValue("@Id",returnId);
            await using var reader=await command.ExecuteReaderAsync(cancellationToken);
            while(await reader.ReadAsync(cancellationToken))lines.Add(new(reader.GetInt32(0),reader.GetInt32(1),
                reader.GetGuid(2),reader.GetString(3),reader.GetDecimal(4),reader.GetDecimal(5),
                reader.GetDecimal(6),reader.GetString(7),reader.GetDecimal(8),reader.GetDecimal(9),
                reader.GetDecimal(10),reader.GetDecimal(11),reader.GetDecimal(12),reader.GetString(13)));
        }
        return new(returnId,number,originalId,originalNumber,customer,identification,warehouseId,
            warehouse,returnedAt,resolution,method,untaxed,tax,total,status,fiscalStatus,reason,
            description,notes,lines);
    }

    private static async Task<IReadOnlyList<ReturnableSalePayment>> LoadPaymentsAsync(
        SqlConnection connection,Guid documentId,CancellationToken cancellationToken)
    {
        await using var command=new SqlCommand("""
            SELECT p.PaymentNumber,p.MethodCode,p.Amount,COALESCE(SUM(s.Amount),0)
            FROM dbo.SalesPayments p
            LEFT JOIN dbo.SalesReturnSettlements s ON s.OriginalDocumentId=p.DocumentId
              AND s.OriginalPaymentNumber=p.PaymentNumber AND s.SettlementType=N'Refund'
            WHERE p.DocumentId=@Id
            GROUP BY p.PaymentNumber,p.MethodCode,p.Amount ORDER BY p.PaymentNumber;
            """,connection);
        command.Parameters.AddWithValue("@Id",documentId);var values=new List<ReturnableSalePayment>();
        await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken))
        {var amount=reader.GetDecimal(2);var refunded=reader.GetDecimal(3);values.Add(new(reader.GetInt32(0),reader.GetString(1),amount,refunded,decimal.Max(0,amount-refunded)));}
        return values;
    }

    private static async Task<IReadOnlyList<ReturnableSaleLine>> LoadLinesAsync(
        SqlConnection connection,Guid documentId,CancellationToken cancellationToken)
    {
        await using var command=new SqlCommand("""
            SELECT l.LineNumber,l.ProductId,COALESCE(p.ProductCode,N''),p.Reference,l.Description,
                   l.Quantity,COALESCE(SUM(r.Quantity),0),l.UnitPrice,l.DiscountAmount,l.TaxCode,
                   l.TaxRate,l.UntaxedAmount,l.TaxAmount,l.LineTotal,
                   COALESCE((SELECT STRING_AGG(pb.Barcode,N' ')
                             FROM dbo.ProductBarcodes pb
                             WHERE pb.ProductId=l.ProductId AND pb.IsActive=1),N'')
            FROM dbo.SalesDocumentLines l
            INNER JOIN dbo.Products p ON p.ProductId=l.ProductId
            LEFT JOIN dbo.SalesReturnLines r ON r.OriginalDocumentId=l.DocumentId
              AND r.OriginalLineNumber=l.LineNumber
            WHERE l.DocumentId=@Id
            GROUP BY l.LineNumber,l.ProductId,p.ProductCode,p.Reference,l.Description,l.Quantity,
                     l.UnitPrice,l.DiscountAmount,l.TaxCode,l.TaxRate,l.UntaxedAmount,l.TaxAmount,l.LineTotal
            ORDER BY l.LineNumber;
            """,connection);
        command.Parameters.AddWithValue("@Id",documentId);var values=new List<ReturnableSaleLine>();
        await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken))
        {var sold=reader.GetDecimal(5);var returned=reader.GetDecimal(6);values.Add(new(reader.GetInt32(0),reader.GetGuid(1),reader.GetString(2),reader.IsDBNull(3)?null:reader.GetString(3),reader.GetString(4),sold,returned,decimal.Max(0,sold-returned),reader.GetDecimal(7),reader.GetDecimal(8),reader.GetString(9),reader.GetDecimal(10),reader.GetDecimal(11),reader.GetDecimal(12),reader.GetDecimal(13),reader.GetString(14)));}
        return values;
    }

    private static void Scope(SqlCommand command,SalesReturnUserIdentity user)
    {
        command.Parameters.AddWithValue("@TenantId",user.TenantId);
        command.Parameters.AddWithValue("@BusinessId",user.BusinessId);
    }
}
