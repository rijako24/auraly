CREATE FUNCTION accounting.ManualVoucherCreditTargets(@PayloadJson nvarchar(max))
RETURNS TABLE
AS RETURN
(
    SELECT application.EffectId,target.SubledgerKind,target.SubledgerId,target.SourceLineId,application.Amount,
      CONVERT(nvarchar(24),N'CreditApplication') EffectKind,CONVERT(uniqueidentifier,NULL) ReversesTransactionId
    FROM OPENJSON(CASE WHEN JSON_VALUE(@PayloadJson,'$.PayloadVersion')=N'4'
      THEN @PayloadJson ELSE N'{}' END,'$.FinancialEffects.CreditApplications')
    WITH(EffectId uniqueidentifier,SubledgerKind nvarchar(16),PositionId uniqueidentifier,CreditId uniqueidentifier,
      PositionSourceLineId uniqueidentifier,CreditSourceLineId uniqueidentifier,Amount decimal(19,4)) application
    CROSS APPLY(VALUES
      (application.SubledgerKind,application.PositionId,application.PositionSourceLineId),
      (CASE application.SubledgerKind WHEN N'Payable' THEN N'SupplierCredit' WHEN N'Receivable' THEN N'CustomerCredit' END,
        application.CreditId,application.CreditSourceLineId)
    ) target(SubledgerKind,SubledgerId,SourceLineId)
    UNION ALL
    SELECT original.EffectId,target.Kind,target.PositionId,target.SourceLineId,original.Amount,
      CASE WHEN original.ReversedTransactionType=N'CreditApplication' THEN N'CreditUnapplication' ELSE N'Unapplication' END,
      original.ReversesTransactionId
    FROM OPENJSON(CASE WHEN JSON_VALUE(@PayloadJson,'$.PayloadVersion')=N'4'
      THEN @PayloadJson ELSE N'{}' END,'$.FinancialEffects.Unapplications')
    WITH(EffectId uniqueidentifier,SubledgerKind nvarchar(16),PositionId uniqueidentifier,
      PositionSourceLineId uniqueidentifier,Amount decimal(19,4),ReversesTransactionId uniqueidentifier,
      ReversedTransactionType nvarchar(24),AdvancePositionId uniqueidentifier,AdvanceSourceLineId uniqueidentifier) original
    CROSS APPLY(VALUES
      (original.SubledgerKind,original.PositionId,original.PositionSourceLineId),
      (CASE WHEN original.ReversedTransactionType=N'CreditApplication' THEN
         CASE original.SubledgerKind WHEN N'Payable' THEN N'SupplierCredit' WHEN N'Receivable' THEN N'CustomerCredit' END END,
       original.AdvancePositionId,original.AdvanceSourceLineId)
    ) target(Kind,PositionId,SourceLineId)
    WHERE target.Kind IS NOT NULL
    UNION ALL
    SELECT cancellation.EffectId,cancellation.SubledgerKind,cancellation.PositionId,
      cancellation.SourceLineId,cancellation.Amount,CONVERT(nvarchar(24),N'Cancellation'),CONVERT(uniqueidentifier,NULL)
    FROM OPENJSON(CASE WHEN JSON_VALUE(@PayloadJson,'$.PayloadVersion')=N'4'
      AND JSON_VALUE(@PayloadJson,'$.VoucherTypeCode')=N'SupportDocumentCancellation'
      THEN @PayloadJson ELSE N'{}' END,'$.FinancialEffects.Positions')
    WITH(EffectId uniqueidentifier,EffectKind nvarchar(24),SubledgerKind nvarchar(16),PositionId uniqueidentifier,
      SourceLineId uniqueidentifier,Amount decimal(19,4)) cancellation
    WHERE cancellation.EffectKind=N'Cancellation' AND cancellation.SubledgerKind=N'Payable'
);
GO
