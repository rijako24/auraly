CREATE FUNCTION accounting.UnapplicationAvailability(
    @BusinessId uniqueidentifier,@Kind nvarchar(16),@TransactionId uniqueidentifier,@OriginalAmount decimal(19,4))
RETURNS TABLE
AS RETURN
(
    SELECT reserved.Amount ReservedAmount,CONVERT(decimal(19,4),@OriginalAmount-reserved.Amount) AvailableAmount
    FROM(SELECT CONVERT(decimal(19,4),COALESCE(SUM(target.Amount),0)) Amount
      FROM accounting.SourcePositionIndex target
      WHERE target.BusinessId=@BusinessId AND target.SubledgerKind=@Kind
        AND target.ReversesTransactionId=@TransactionId) reserved
);
GO
