CREATE VIEW accounting.ClosurePaymentCorrectionChanges
AS
WITH Changes AS
(
    SELECT correction.TenantId,correction.BusinessId,correction.WorkSessionClosureId,
      shift.PaymentMethodCode,shift.Amount,CONVERT(decimal(19,4),0) AllocatedAmount
    FROM accounting.SalePaymentCorrections correction
    CROSS APPLY(VALUES(correction.OriginalPaymentMethodCode,
      -CASE WHEN correction.MovementType=N'CreditSale' THEN correction.Amount ELSE correction.OriginalAmount END),
      (correction.PaymentMethodCode,correction.Amount)) shift(PaymentMethodCode,Amount)
    WHERE correction.PostingStatus IN(N'Posted',N'CommercialEffectsApplied')
    UNION ALL
    SELECT correction.TenantId,correction.BusinessId,correction.WorkSessionClosureId,
      allocation.PaymentMethodCode,CONVERT(decimal(19,4),0),allocation.Amount
    FROM accounting.SalePaymentCorrections correction
    CROSS APPLY OPENJSON(correction.ClosureAllocationsJson) WITH(
      PaymentMethodCode nvarchar(32) '$.paymentMethodCode',Amount decimal(19,4) '$.amount') allocation
    WHERE correction.PostingStatus IN(N'Posted',N'CommercialEffectsApplied')
)
SELECT TenantId,BusinessId,WorkSessionClosureId,PaymentMethodCode,
  SUM(Amount) Amount,SUM(AllocatedAmount) AllocatedAmount
FROM Changes
GROUP BY TenantId,BusinessId,WorkSessionClosureId,PaymentMethodCode;
GO
