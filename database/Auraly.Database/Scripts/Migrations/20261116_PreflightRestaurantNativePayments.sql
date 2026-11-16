-- Finish or reverse already captured legacy table payments through the old
-- application's normal commands before the native-catalog cutover. Never
-- infer a form or rewrite payment evidence on an open restaurant account.
SET NOCOUNT ON;
IF OBJECT_ID(N'sales.RestaurantAccountCollections',N'U') IS NOT NULL
  AND OBJECT_ID(N'sales.RestaurantAccounts',N'U') IS NOT NULL
EXEC(N'
    IF EXISTS(SELECT 1 FROM sales.RestaurantAccounts account
      JOIN sales.RestaurantAccountCollections collection
        ON collection.AccountId=account.AccountId AND collection.TenantId=account.TenantId
          AND collection.BusinessId=account.BusinessId
      CROSS APPLY OPENJSON(collection.PaymentsJson) WITH(PaymentMethodId uniqueidentifier) payment
      WHERE account.Status IN(N''Open'',N''Issuing'') AND collection.Status=N''Captured''
        AND payment.PaymentMethodId IS NULL)
      THROW 51982,N''Hay mesas abiertas con cobros anteriores al catálogo. Finaliza o revierte sus cobros desde la versión anterior antes de actualizar.'',1;
');
