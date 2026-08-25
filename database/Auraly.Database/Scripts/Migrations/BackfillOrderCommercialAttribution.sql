UPDATE orders
SET WarehouseId=COALESCE(orders.WarehouseId,TRY_CONVERT(uniqueidentifier,JSON_VALUE(orders.CustomAttributesJson,'$.WarehouseId'))),
    OrdersWarehouseId=COALESCE(orders.OrdersWarehouseId,TRY_CONVERT(uniqueidentifier,JSON_VALUE(orders.CustomAttributesJson,'$.ordersWarehouseId'))),
    ReservationTransferId=COALESCE(orders.ReservationTransferId,TRY_CONVERT(uniqueidentifier,JSON_VALUE(orders.CustomAttributesJson,'$.reservationTransferId'))),
    RouteId=COALESCE(orders.RouteId,TRY_CONVERT(uniqueidentifier,JSON_VALUE(orders.CustomAttributesJson,'$.RouteId'))),
    RouteStopId=COALESCE(orders.RouteStopId,TRY_CONVERT(uniqueidentifier,JSON_VALUE(orders.CustomAttributesJson,'$.RouteStopId'))),
    PartySiteId=COALESCE(orders.PartySiteId,TRY_CONVERT(uniqueidentifier,JSON_VALUE(orders.CustomAttributesJson,'$.PartySiteId'))),
    CapturedByUserId=COALESCE(orders.CapturedByUserId,TRY_CONVERT(uniqueidentifier,JSON_VALUE(orders.CustomAttributesJson,'$.createdBy'))),
    CapturedOffline=COALESCE(TRY_CONVERT(bit,JSON_VALUE(orders.CustomAttributesJson,'$.CapturedOffline')),orders.CapturedOffline),
    RequiresStockReview=COALESCE(TRY_CONVERT(bit,JSON_VALUE(orders.CustomAttributesJson,'$.requiresStockReview')),orders.RequiresStockReview)
FROM dbo.Orders orders
WHERE ISJSON(orders.CustomAttributesJson)=1;
GO

UPDATE orders
SET SellerId=seller.SellerId
FROM dbo.Orders orders
INNER JOIN dbo.AppUsers appUser ON appUser.UserId=orders.CapturedByUserId
INNER JOIN dbo.CommerceSellers seller
  ON seller.PartyId=appUser.PartyId AND seller.BusinessId=orders.BusinessId
WHERE orders.SellerId IS NULL AND seller.IsActive=1;
GO
