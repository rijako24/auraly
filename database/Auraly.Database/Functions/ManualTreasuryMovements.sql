-- Accepted facts, including pending posting. The sent draft is only the indexed
-- session lookup; amounts and identities always come from the immutable source.
CREATE FUNCTION worksessions.ManualTreasuryMovements
(@TenantId uniqueidentifier,@BusinessId uniqueidentifier,@SessionId uniqueidentifier)
RETURNS @Movements TABLE
(
    SourceDocumentId uniqueidentifier,BusinessId uniqueidentifier,MovementId uniqueidentifier,
    SourceLineId uniqueidentifier,MethodCode nvarchar(32),Amount decimal(19,4),Reference nvarchar(160),
    OccurredAt datetimeoffset(7),AcceptedByUserId uniqueidentifier,DocumentNumber nvarchar(100),Description nvarchar(500),
    AccountId uniqueidentifier,EntryId uniqueidentifier
)
AS
BEGIN
    -- Scope before expanding JSON. Inlining the projection lets the optimizer
    -- expand other sessions' accepted sources before applying the draft filter.
    -- These transient rows hold no balances and never escape this read.
    DECLARE @Sources TABLE(SourceDocumentId uniqueidentifier PRIMARY KEY,BusinessId uniqueidentifier,
      PayloadJson nvarchar(max),OccurredAt datetimeoffset(7),EntryNumber nvarchar(100),EntryId uniqueidentifier);
    INSERT @Sources
    SELECT source.SourceDocumentId,source.BusinessId,source.PayloadJson,source.OccurredAt,entry.EntryNumber,entry.EntryId
    FROM accounting.VoucherDrafts draft
    JOIN dbo.AccountingSourceDocuments source ON source.SourceDocumentId=draft.DocumentId
      AND source.SourceDocumentType=N'ManualAccountingVoucher'
      AND source.TenantId=draft.TenantId AND source.BusinessId=draft.BusinessId
    LEFT JOIN dbo.AccountingEntries entry ON entry.SourceDocumentId=source.SourceDocumentId
      AND entry.SourceDocumentType=source.SourceDocumentType AND entry.TenantId=source.TenantId
      AND entry.BusinessId=source.BusinessId
    WHERE draft.TenantId=@TenantId AND draft.BusinessId=@BusinessId AND draft.WorkSessionId=@SessionId
      AND draft.SentAt IS NOT NULL;

    INSERT @Movements
    SELECT source.SourceDocumentId,source.BusinessId,movement.MovementId,movement.SourceLineId,
      movement.MethodCode,movement.Amount,movement.Reference,source.OccurredAt,effects.AcceptedByUserId,
      CONVERT(nvarchar(100),COALESCE(source.EntryNumber,header.Reference,
        CONVERT(nvarchar(36),source.SourceDocumentId))) DocumentNumber,header.Description,
      movement.AccountId,source.EntryId
    FROM @Sources source
    CROSS APPLY OPENJSON(source.PayloadJson) WITH(WorkSessionId uniqueidentifier,PayloadVersion int,
      Reference nvarchar(100),Description nvarchar(500),FinancialEffects nvarchar(max) AS JSON) header
    CROSS APPLY OPENJSON(header.FinancialEffects) WITH(AcceptedByUserId uniqueidentifier,
      DirectMovements nvarchar(max) AS JSON) effects
    CROSS APPLY OPENJSON(effects.DirectMovements) WITH(MovementId uniqueidentifier,SourceLineId uniqueidentifier,
      MethodCode nvarchar(32),Amount decimal(19,4),Reference nvarchar(160),AccountId uniqueidentifier) movement
    WHERE header.WorkSessionId=@SessionId AND header.PayloadVersion=4;
    RETURN;
END;
GO
