CREATE PROCEDURE [dbo].[SellerOrderCreate]
    @OrderId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @CustomerId UNIQUEIDENTIFIER,
    @Status INT,
    @CustomerName NVARCHAR(200),
    @Email NVARCHAR(254) = NULL,
    @Phone NVARCHAR(50) = NULL,
    @Identification NVARCHAR(80) = NULL,
    @Address NVARCHAR(500),
    @Notes NVARCHAR(MAX) = NULL,
    @Total DECIMAL(19,4),
    @Number NVARCHAR(300),
    @ExternalStatus NVARCHAR(100),
    @IdempotencyKey NVARCHAR(200),
    @Attributes NVARCHAR(MAX),
    @LinesJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    INSERT dbo.Orders(OrderId,BusinessId,CustomerId,Source,FulfillmentMode,Status,CustomerNameSnapshot,
        CustomerEmailSnapshot,CustomerPhoneSnapshot,CustomerDocumentSnapshot,DeliveryAddressSnapshot,Notes,
        Currency,Subtotal,DiscountTotal,TaxTotal,Total,CustomerConfirmed,ExternalDocumentNumber,ExternalStatus,
        IdempotencyKey,CustomAttributesJson,CreatedAt,UpdatedAt)
    VALUES(@OrderId,@BusinessId,@CustomerId,1,0,@Status,@CustomerName,@Email,@Phone,@Identification,@Address,@Notes,
        N'COP',@Total,0,0,@Total,1,@Number,@ExternalStatus,@IdempotencyKey,@Attributes,SYSUTCDATETIME(),SYSUTCDATETIME());

    INSERT dbo.OrderItems(OrderItemId,OrderId,BusinessId,ProductId,Sku,ProductCodeSnapshot,ProductNameSnapshot,
        DescriptionSnapshot,UnitCodeSnapshot,Quantity,UnitPrice,DiscountAmount,TaxAmount,LineTotal,RawPayloadJson,CreatedAt)
    SELECT NEWID(),@OrderId,@BusinessId,j.ProductId,j.Code,j.Code,j.Name,j.Name,j.UnitCode,j.Quantity,j.UnitPrice,0,j.TaxAmount,j.LineTotal,j.RawPayloadJson,SYSUTCDATETIME()
    FROM OPENJSON(@LinesJson) WITH(
        ProductId UNIQUEIDENTIFIER '$.productId',
        Code NVARCHAR(100) '$.code',
        Name NVARCHAR(250) '$.name',
        UnitCode NVARCHAR(24) '$.unitCode',
        Quantity DECIMAL(19,6) '$.quantity',
        UnitPrice DECIMAL(19,4) '$.unitPrice',
        TaxAmount DECIMAL(19,4) '$.taxAmount',
        LineTotal DECIMAL(19,4) '$.lineTotal',
        RawPayloadJson NVARCHAR(MAX) '$.rawPayloadJson') j;

    IF @@ROWCOUNT = 0 THROW 51301, 'El pedido requiere al menos un producto.', 1;
END
