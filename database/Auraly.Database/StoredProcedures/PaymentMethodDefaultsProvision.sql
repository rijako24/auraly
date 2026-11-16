-- Catalog bootstrap only. Every caller uses these same profile templates;
-- no financial document, bank identity or existing configuration is rewritten.
CREATE PROCEDURE dbo.PaymentMethodDefaultsProvision
    @TenantId uniqueidentifier=NULL,
    @Now datetimeoffset(7)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    -- The provisioning command or deployment script owns the transaction.
    DECLARE @Created TABLE(TenantId uniqueidentifier,PaymentMethodId uniqueidentifier,
        PRIMARY KEY(TenantId,PaymentMethodId));

    INSERT reference.PaymentMethods(TenantId,PaymentMethodId,Code,Name,PaymentTypeId,AccountId,
        UseForSales,UseForReceipts,UseForSupplierPayments,UseForPayroll,UseForVouchers,
        IsActive,SortOrder,Version,CreatedAt,UpdatedAt)
    OUTPUT inserted.TenantId,inserted.PaymentMethodId INTO @Created
    SELECT tenant.TenantId,NEWID(),type.Code,type.Label,type.OptionId,account.AccountId,
        template.UseForSales,template.UseForReceipts,template.UseForSupplierPayments,
        template.UseForPayroll,template.UseForVouchers,0,template.SortOrder,1,@Now,@Now
    FROM dbo.Tenants tenant
    JOIN dbo.AccountingConfigurationProfiles profile ON profile.IsDefault=1 AND profile.IsActive=1
    JOIN dbo.AccountingConfigurationProfilePaymentMethods template ON template.ProfileCode=profile.ProfileCode
    JOIN reference.Options type ON type.OptionId=template.PaymentTypeId
        AND type.CatalogCode=N'payment-method' AND type.IsActive=1
    CROSS APPLY accounting.DefaultAccountDefinitions(profile.ProfileCode) definition
    JOIN dbo.AccountingAccounts account ON account.TenantId=tenant.TenantId AND account.Code=definition.AccountCode
        AND account.Name=definition.AccountName AND account.AccountType=definition.AccountType
    WHERE tenant.IsActive=1 AND (@TenantId IS NULL OR tenant.TenantId=@TenantId)
        AND definition.Category=template.AccountCategory
        AND NOT EXISTS(SELECT 1 FROM reference.PaymentMethods existing WITH(UPDLOCK,HOLDLOCK)
            WHERE existing.TenantId=tenant.TenantId AND existing.PaymentTypeId=type.OptionId);

    -- A generic bank account has no real bank identity yet. Keep that form
    -- inactive until the administrator completes it; never bypass readiness.
    UPDATE form SET IsActive=1
    FROM reference.PaymentMethods form
    JOIN @Created created ON created.TenantId=form.TenantId AND created.PaymentMethodId=form.PaymentMethodId
    JOIN reference.PaymentMethodDetails ready ON ready.TenantId=form.TenantId
        AND ready.PaymentMethodId=form.PaymentMethodId AND ready.IsReady=1;

    -- Provisioning uses the same Configuration stream as administrative
    -- saves. Reprovisioning without new forms produces no extra notice.
    INSERT dbo.PosSynchronizationOutboxMessages(NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
    SELECT NEWID(),business.BusinessId,N'Configuration',COALESCE(latest.CursorValue,0)+1,@Now
    FROM dbo.Businesses business
    JOIN (SELECT DISTINCT TenantId FROM @Created) changed ON changed.TenantId=business.TenantId
    OUTER APPLY(
        SELECT MAX(AvailableThroughCursor) CursorValue
        FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
        WHERE BusinessId=business.BusinessId AND Stream=N'Configuration') latest;
END;
