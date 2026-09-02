CREATE PROCEDURE [purchasing].[PurchaseOrderDraftDelete]
    @TenantId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @PurchaseOrderId UNIQUEIDENTIFIER,
    @RowVersion VARBINARY(8)
AS
BEGIN
    SET NOCOUNT ON;

    DELETE draft
    FROM purchasing.PurchaseOrderDrafts draft
    INNER JOIN dbo.Businesses business ON business.BusinessId=draft.BusinessId
    WHERE draft.PurchaseOrderId=@PurchaseOrderId
      AND draft.BusinessId=@BusinessId
      AND business.TenantId=@TenantId
      AND draft.RowVersion=@RowVersion;

    IF @@ROWCOUNT=0
        THROW 51204,'The purchase-order draft was not found or changed in another session.',1;
END;
