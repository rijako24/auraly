DECLARE @DispatchReasonDefaults TABLE(ReasonType nvarchar(32),Code nvarchar(40),Name nvarchar(160),DisplayOrder int);
INSERT @DispatchReasonDefaults VALUES
(N'NotDelivered',N'CUSTOMER_ABSENT',N'Cliente ausente',10),
(N'NotDelivered',N'BUSINESS_CLOSED',N'Local cerrado',20),
(N'NotDelivered',N'CUSTOMER_REJECTED',N'Cliente rechazó el pedido',30),
(N'NotDelivered',N'WRONG_ADDRESS',N'Dirección incorrecta',40),
(N'NotDelivered',N'NO_PAYMENT',N'Cliente sin medio de pago',50),
(N'NotDelivered',N'ACCESS_RESTRICTED',N'No fue posible acceder al lugar',60),
(N'NotDelivered',N'OTHER',N'Otro motivo',999);

INSERT dispatch.DispatchReasons(DispatchReasonId,BusinessId,ReasonType,Code,Name,IsSystem,IsActive,DisplayOrder,CreatedAt)
SELECT NEWID(),business.BusinessId,reason.ReasonType,reason.Code,reason.Name,1,1,reason.DisplayOrder,SYSUTCDATETIME()
FROM dbo.Businesses business CROSS JOIN @DispatchReasonDefaults reason
WHERE NOT EXISTS(SELECT 1 FROM dispatch.DispatchReasons currentReason WHERE currentReason.BusinessId=business.BusinessId AND currentReason.ReasonType=reason.ReasonType AND currentReason.Code=reason.Code);
