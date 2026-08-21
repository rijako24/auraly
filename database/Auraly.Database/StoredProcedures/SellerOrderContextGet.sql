CREATE PROCEDURE [dbo].[SellerOrderContextGet]
    @TenantId UNIQUEIDENTIFIER,
    @BusinessId UNIQUEIDENTIFIER,
    @CustomerId UNIQUEIDENTIFIER,
    @SiteId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName)),p.Identification,email.Value,phone.Value,
           COALESCE(site.AddressLine,N''),orders.WarehouseId
    FROM dbo.Customers customer INNER JOIN dbo.Parties p ON p.PartyId=customer.PartyId
    OUTER APPLY(SELECT TOP(1) contact.Value FROM dbo.PartyContacts contact WHERE contact.PartyId=p.PartyId AND contact.ContactType=N'Email' AND contact.IsActive=1 ORDER BY contact.IsPrimary DESC,contact.CreatedAt) email
    OUTER APPLY(SELECT TOP(1) contact.Value FROM dbo.PartyContacts contact WHERE contact.PartyId=p.PartyId AND contact.ContactType=N'Phone' AND contact.IsActive=1 ORDER BY contact.IsPrimary DESC,contact.CreatedAt) phone
    LEFT JOIN dbo.PartySites site ON site.PartySiteId=@SiteId AND site.PartyId=p.PartyId AND site.IsActive=1
    CROSS APPLY(SELECT TOP(1) WarehouseId FROM dbo.Warehouses WHERE BusinessId=@BusinessId AND Code=N'PED' AND IsActive=1 ORDER BY CreatedAt) orders
    INNER JOIN dbo.Businesses business ON business.BusinessId=customer.BusinessId AND business.TenantId=@TenantId
    WHERE customer.CustomerId=@CustomerId AND customer.BusinessId=@BusinessId AND customer.IsActive=1;
END
