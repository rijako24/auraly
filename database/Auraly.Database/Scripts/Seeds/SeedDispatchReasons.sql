INSERT dispatch.DispatchReasons(DispatchReasonId,BusinessId,ReasonType,Code,Name,IsSystem,IsActive,DisplayOrder,CreatedAt)
SELECT reason.ReasonId,reason.BusinessId,reason.ReasonType,reason.Code,reason.Name,
       reason.IsSystem,reason.IsActive,reason.DisplayOrder,reason.CreatedAt
FROM dbo.BusinessReasons reason
WHERE reason.ReasonType IN(N'NotDelivered',N'DeliveryReturn')
  AND NOT EXISTS(
    SELECT 1 FROM dispatch.DispatchReasons currentReason
    WHERE currentReason.BusinessId=reason.BusinessId
      AND currentReason.ReasonType=reason.ReasonType
      AND currentReason.Code=reason.Code);

UPDATE legacy SET legacy.Name=reason.Name,legacy.IsActive=reason.IsActive,
  legacy.DisplayOrder=reason.DisplayOrder,legacy.UpdatedAt=SYSUTCDATETIME()
FROM dispatch.DispatchReasons legacy
INNER JOIN dbo.BusinessReasons reason
  ON reason.BusinessId=legacy.BusinessId
 AND reason.ReasonType=legacy.ReasonType
 AND reason.Code=legacy.Code
WHERE legacy.Name<>reason.Name OR legacy.IsActive<>reason.IsActive
   OR legacy.DisplayOrder<>reason.DisplayOrder;
