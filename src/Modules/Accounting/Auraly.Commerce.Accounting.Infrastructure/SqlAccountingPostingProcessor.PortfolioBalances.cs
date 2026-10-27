namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingPostingProcessor
{
    private enum PortfolioBalanceChangeKind { Settlement, Adjustment, Cancellation }

    // Only the processor mutates financial balances. Callers provide a bounded,
    // aggregated @PortfolioChanges(PositionId,Delta) batch in their existing
    // command/transaction; they keep ownership of evidence and history.
    private static string PortfolioBalanceChangesSql(string positionTable, string positionId,
        PortfolioBalanceChangeKind kind) => $"""
        UPDATE balance SET OutstandingAmount=balance.OutstandingAmount+change.Delta,
          Status=CASE WHEN balance.OutstandingAmount+change.Delta=0 THEN
            {(kind == PortfolioBalanceChangeKind.Cancellation
                ? "CASE WHEN -change.Delta=balance.OriginalAmount THEN N'Cancelled' ELSE N'Paid' END" : "N'Paid'")}
            {(kind == PortfolioBalanceChangeKind.Adjustment
                ? "WHEN balance.OutstandingAmount+change.Delta>=balance.OriginalAmount THEN N'Open'" : "")}
            ELSE N'PartiallyPaid' END
        FROM dbo.[{positionTable}] balance WITH(UPDLOCK,HOLDLOCK)
        JOIN @PortfolioChanges change ON change.PositionId=balance.[{positionId}]
        WHERE balance.BusinessId=@BusinessId AND balance.Status<>N'Cancelled'
          AND balance.OutstandingAmount+change.Delta>=0
          {(kind == PortfolioBalanceChangeKind.Settlement ? "AND change.Delta<0" : "")}
          {(kind == PortfolioBalanceChangeKind.Cancellation ? "AND change.Delta<=0 AND balance.OutstandingAmount+change.Delta=0" : "")};
        IF @@ROWCOUNT<>(SELECT COUNT(*) FROM @PortfolioChanges)
          THROW 51607,'The portfolio balances were not updated atomically.',1;
        """;

    private const string DeclarePortfolioBalanceChangesSql = """
        DECLARE @PortfolioChanges TABLE(PositionId uniqueidentifier PRIMARY KEY,Delta decimal(19,4) NOT NULL);
        """;

    private static string PortfolioBalanceReductionSql(string positionTable, string positionId) =>
        DeclarePortfolioBalanceChangesSql + """
        INSERT @PortfolioChanges SELECT PositionId,-SUM(Amount) FROM @Ready GROUP BY PositionId;
        """ + PortfolioBalanceChangesSql(positionTable, positionId, PortfolioBalanceChangeKind.Settlement);

    // Applications and their reversals share the same bounded credit writer.
    // A positive delta restores an existing credit; it never opens a second one.
    private const string DeclareCreditBalanceChangesSql = """
        DECLARE @CreditChanges TABLE(CreditId uniqueidentifier PRIMARY KEY,Delta decimal(19,4) NOT NULL);
        """;

    private static string CreditBalanceChangesSql(string creditTable, string creditId) => $"""
        UPDATE credit SET AvailableAmount=credit.AvailableAmount+change.Delta,
          Status=CASE WHEN credit.AvailableAmount+change.Delta=0 THEN {(creditTable == "CustomerCredits" ? "N'Exhausted'" : "N'Applied'")}
            WHEN credit.AvailableAmount+change.Delta=credit.OriginalAmount THEN N'Open'
            ELSE {(creditTable == "CustomerCredits" ? "N'Open'" : "N'PartiallyApplied'")} END
        FROM dbo.[{creditTable}] credit WITH(UPDLOCK,HOLDLOCK)
        JOIN @CreditChanges change ON change.CreditId=credit.[{creditId}]
        WHERE credit.BusinessId=@BusinessId AND credit.Status<>N'Cancelled'
          AND credit.AvailableAmount+change.Delta BETWEEN 0 AND credit.OriginalAmount;
        IF @@ROWCOUNT<>(SELECT COUNT(*) FROM @CreditChanges)
          THROW 51850,'The credit balances were not updated atomically.',1;
        """;
}
