CREATE VIEW dispatch.Closures AS
SELECT closure.WorkSessionClosureId DispatchSettlementId,session.WorkSessionId,session.BusinessId,session.DispatchId,
  closure.DispatchExpectedCash ExpectedCash,closure.CountedCash DeclaredCash,
  card.CountedAmount DeclaredCard,transfer.CountedAmount DeclaredTransfer,
  COALESCE(closure.ReceivedCash,closure.CountedCash)-closure.DispatchExpectedCash CashDifference,
  closure.DispatchTransferTotal DepositTotal,closure.DispatchCreditDocumentTotal CreditDocumentTotal,
  closure.DispatchCreditAdvanceTotal CreditAdvanceTotal,closure.DispatchReturnTotal ReturnTotal,
  closure.Note DifferenceReason,closure.ClosedByUserId TransporterClosedBy,closure.ClosedAt TransporterClosedAt,
  closure.ReceivedCash CashReceived,closure.ReceivedByUserId ReceivedBy,closure.ReceivedAt,
  closure.DispatchReviewNotes Notes,closure.DispatchStatus Status,closure.ReconciliationStatus,closure.IdempotencyKey
FROM dbo.WorkSessionClosures closure
JOIN dbo.WorkSessions session ON session.WorkSessionId=closure.WorkSessionId AND session.Kind=N'Dispatch'
LEFT JOIN dbo.WorkSessionClosurePaymentTotals card ON card.WorkSessionClosureId=closure.WorkSessionClosureId AND card.PaymentMethodCode=N'Card'
LEFT JOIN dbo.WorkSessionClosurePaymentTotals transfer ON transfer.WorkSessionClosureId=closure.WorkSessionClosureId AND transfer.PaymentMethodCode=N'Transfer';
