CREATE PROCEDURE dbo.TenantBillingReminderRecipientGet
    @NotificationId UNIQUEIDENTIFIER,
    @MessageId UNIQUEIDENTIFIER,
    @TenantId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT app.Email,COALESCE(NULLIF(LTRIM(RTRIM(app.FirstName)),N''),N'Administrador'),
           tenantValue.Name,notificationValue.Title,notificationValue.Message,
           renewal.TenantSubscriptionRenewalOrderId,renewal.DueAt,renewal.PayableAmount
    FROM billing.TenantBillingNotifications notificationValue
    JOIN dbo.AppUsers app ON app.UserId=notificationValue.UserId AND app.TenantId=notificationValue.TenantId
    JOIN dbo.Tenants tenantValue ON tenantValue.TenantId=notificationValue.TenantId
    JOIN billing.TenantSubscriptionRenewalOrders renewal
      ON renewal.TenantSubscriptionRenewalOrderId=notificationValue.TenantSubscriptionRenewalOrderId
    WHERE notificationValue.TenantBillingNotificationId=@NotificationId
      AND notificationValue.EmailOutboxMessageId=@MessageId
      AND notificationValue.TenantId=@TenantId
      AND app.IsActive=1 AND NULLIF(LTRIM(RTRIM(app.Email)),N'') IS NOT NULL
      AND renewal.IsCurrent=1 AND renewal.Status IN(N'Draft',N'PendingPayment',N'PaymentFailed');
END;
GO
