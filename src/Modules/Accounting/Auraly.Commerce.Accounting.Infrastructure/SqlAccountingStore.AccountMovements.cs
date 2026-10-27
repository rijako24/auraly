using System.Data;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingStore
{
    // The trial balance and its drill-down must expand the same account graph.
    // Callers seed #ReportAccounts with the scoped account keys they need.
    private const string ReportAccountHierarchySql = """
        WITH Ancestors AS (
          SELECT AccountId LeafAccountId,Code,ParentAccountId FROM #ReportAccounts
          UNION ALL
          SELECT child.LeafAccountId,parent.Code,parent.ParentAccountId
          FROM Ancestors child
          JOIN dbo.AccountingAccounts parent
            ON parent.AccountId=child.ParentAccountId AND parent.TenantId=@TenantId
        ), Hierarchy AS (
          SELECT LeafAccountId,Code FROM Ancestors
          UNION
          SELECT leaf.AccountId,LEFT(leaf.Code,levelValue.Length)
          FROM #ReportAccounts leaf CROSS JOIN (VALUES(1),(2),(4),(6)) levelValue(Length)
          WHERE levelValue.Length<=LEN(leaf.Code)
        )
        """;

    // Use the same scoped journal predicate for the separate aggregates and
    // page keys. The page is limited before loading descriptions and parties.
    private const string AccountMovementScopeSql = """
        FROM dbo.AccountingEntries e
        JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
        JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId AND a.TenantId=@TenantId
        WHERE e.TenantId=@TenantId AND e.BusinessId=@BusinessId
          AND e.RowVersion<=@ReportCutoff
          AND (@AccountFrom IS NULL OR a.Code>=@AccountFrom)
          AND (@AccountTo IS NULL OR a.Code<=@AccountTo)
          AND (@AccountRootCode IS NULL OR EXISTS(
            SELECT 1 FROM #AccountMovementRoot root WHERE root.AccountId=a.AccountId))
          AND (@PartyId IS NULL OR l.PartyId=@PartyId)
          AND (@CostCenterId IS NULL OR l.CostCenterId=@CostCenterId)
          AND CAST(e.OccurredAt AS date)<=@To
        """;

    public async Task<AccountMovementPage> GetAccountMovementsPageAsync(
        AccountingUserIdentity user, AccountMovementQuery query, CancellationToken cancellationToken, int? maximumRows = null)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            CREATE TABLE #AccountMovementRoot(AccountId uniqueidentifier PRIMARY KEY);
            IF @AccountRootCode IS NOT NULL
            BEGIN
              SELECT AccountId,Code,ParentAccountId INTO #ReportAccounts
              FROM dbo.AccountingAccounts WHERE TenantId=@TenantId;
            """ + "\n" + ReportAccountHierarchySql + "\n" + """
              INSERT #AccountMovementRoot(AccountId)
              SELECT DISTINCT LeafAccountId FROM Hierarchy WHERE Code=@AccountRootCode
              OPTION(MAXRECURSION 32);
              DROP TABLE #ReportAccounts;
            END;
            -- Entries are immutable; this database rowversion cutoff keeps the
            -- separate totals and page reads on the same posted set.
            DECLARE @ReportCutoff binary(8)=@@DBTS;
            SELECT AccountId,Code,Name,
              SUM(CASE WHEN CAST(OccurredAt AS date)<@From THEN Debit-Credit ELSE 0 END) OpeningBalance,
              SUM(CASE WHEN CAST(OccurredAt AS date)>=@From THEN Debit ELSE 0 END) Debit,
              SUM(CASE WHEN CAST(OccurredAt AS date)>=@From THEN Credit ELSE 0 END) Credit,
              SUM(CASE WHEN CAST(OccurredAt AS date)>=@From THEN 1 ELSE 0 END) MovementCount,
              CASE WHEN SUM(CASE WHEN CAST(OccurredAt AS date)>=@From AND BaseAmount IS NULL THEN 1 ELSE 0 END)=0
                THEN SUM(CASE WHEN CAST(OccurredAt AS date)>=@From THEN BaseAmount END) END BaseAmount
            INTO #AccountMovementTotals
            FROM (
              SELECT a.AccountId,a.Code,a.Name,e.OccurredAt,l.Debit,l.Credit,l.BaseAmount
            """ + "\n" + AccountMovementScopeSql + "\n" + """
            ) scope
            GROUP BY AccountId,Code,Name OPTION(RECOMPILE);
            CREATE UNIQUE CLUSTERED INDEX IX_AccountMovementTotals ON #AccountMovementTotals(AccountId);
            IF @MaximumRows IS NOT NULL AND (SELECT COALESCE(SUM(CONVERT(bigint,MovementCount)
              +CASE WHEN OpeningBalance<>0 OR MovementCount>0 THEN 1 ELSE 0 END),0) FROM #AccountMovementTotals)>@MaximumRows
              THROW 51412,N'AccountMovementReportLimitExceeded',1;
            WITH PeriodKeys AS (
              SELECT e.EntryId,e.EntryNumber,e.OccurredAt,l.LineNumber,a.Code,a.AccountId,l.BaseAmount,
                SUM(l.Debit-l.Credit) OVER(PARTITION BY a.AccountId
                  ORDER BY e.OccurredAt,e.EntryNumber,e.EntryId,l.LineNumber ROWS UNBOUNDED PRECEDING) PeriodBalance
            """ + "\n" + AccountMovementScopeSql + "\n" + """
                AND CAST(e.OccurredAt AS date)>=@From
            )
            SELECT EntryId,LineNumber,AccountId,Code,OccurredAt,EntryNumber,PeriodBalance,BaseAmount
            INTO #AccountMovementPage FROM PeriodKeys
            ORDER BY Code,OccurredAt,EntryNumber,EntryId,LineNumber
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY OPTION(RECOMPILE);

            SELECT e.EntryId,e.EntryNumber,e.SourceDocumentId,e.SourceDocumentType,e.OccurredAt,
              l.Description,l.Debit,l.Credit,total.OpeningBalance+page.PeriodBalance Balance,l.LineNumber,
              l.CostCenterId,l.CostCenterCodeSnapshot,l.CostCenterNameSnapshot,
              total.Code,total.Name,l.PartyId,l.PartyIdentificationSnapshot,l.PartyNameSnapshot,
              sourceNumber.DocumentNumber,
              COALESCE(JSON_VALUE(source.PayloadJson,'$.Reference'),JSON_VALUE(source.PayloadJson,'$.reference')) Reference,
              page.BaseAmount
            FROM #AccountMovementPage page
            JOIN dbo.AccountingEntries e ON e.EntryId=page.EntryId
            JOIN dbo.AccountingEntryLines l ON l.EntryId=page.EntryId AND l.LineNumber=page.LineNumber
            JOIN #AccountMovementTotals total ON total.AccountId=page.AccountId
            LEFT JOIN dbo.AccountingSourceDocuments source ON source.SourceDocumentId=e.SourceDocumentId
              AND source.SourceDocumentType=e.SourceDocumentType AND source.TenantId=@TenantId AND source.BusinessId=@BusinessId
            LEFT JOIN (
            """ + "\n" + TraceabilitySourceMetadataSql + "\n" + """
            ) sourceNumber ON sourceNumber.SourceDocumentId=e.SourceDocumentId AND sourceNumber.SourceDocumentType=e.SourceDocumentType
            ORDER BY page.Code,page.OccurredAt,page.EntryNumber,page.EntryId,page.LineNumber;
            SELECT Code,Name,OpeningBalance,Debit,Credit,OpeningBalance+Debit-Credit ClosingBalance,MovementCount,BaseAmount
            FROM #AccountMovementTotals WHERE OpeningBalance<>0 OR MovementCount>0 ORDER BY Code;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.Add("@From", SqlDbType.Date).Value = query.From.ToDateTime(TimeOnly.MinValue);
        command.Parameters.Add("@To", SqlDbType.Date).Value = query.To.ToDateTime(TimeOnly.MinValue);
        command.Parameters.Add("@AccountFrom", SqlDbType.NVarChar, 32).Value = (object?)query.AccountFrom ?? DBNull.Value;
        command.Parameters.Add("@AccountTo", SqlDbType.NVarChar, 32).Value = (object?)query.AccountTo ?? DBNull.Value;
        command.Parameters.Add("@AccountRootCode", SqlDbType.NVarChar, 32).Value = (object?)query.AccountRootCode ?? DBNull.Value;
        command.Parameters.Add("@PartyId", SqlDbType.UniqueIdentifier).Value = (object?)query.PartyId ?? DBNull.Value;
        command.Parameters.Add("@CostCenterId", SqlDbType.UniqueIdentifier).Value = (object?)query.CostCenterId ?? DBNull.Value;
        command.Parameters.AddWithValue("@Offset", (query.Page - 1) * query.PageSize);
        command.Parameters.AddWithValue("@PageSize", query.PageSize);
        command.Parameters.Add("@MaximumRows", SqlDbType.Int).Value = (object?)maximumRows ?? DBNull.Value;
        var items = new List<AccountMovementRow>();
        var totals = new List<AccountMovementTotal>();
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                items.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetGuid(2),reader.GetString(3),
                    reader.GetDateTimeOffset(4),reader.GetString(5),reader.GetDecimal(6),reader.GetDecimal(7),reader.GetDecimal(8),
                    reader.GetInt32(9),reader.IsDBNull(10)?null:reader.GetGuid(10),
                    reader.IsDBNull(11)?null:reader.GetString(11),reader.IsDBNull(12)?null:reader.GetString(12),
                    reader.GetString(13),reader.GetString(14),reader.IsDBNull(15)?null:reader.GetGuid(15),
                    reader.IsDBNull(16)?null:reader.GetString(16),reader.IsDBNull(17)?null:reader.GetString(17),
                    reader.IsDBNull(18)?null:reader.GetString(18),reader.IsDBNull(19)?null:reader.GetString(19),
                    reader.IsDBNull(20)?null:reader.GetDecimal(20)));
            await reader.NextResultAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                totals.Add(new(reader.GetString(0),reader.GetString(1),reader.GetDecimal(2),reader.GetDecimal(3),
                    reader.GetDecimal(4),reader.GetDecimal(5),reader.GetInt32(6),reader.IsDBNull(7)?null:reader.GetDecimal(7)));
            return new(items,totals,query.Page,query.PageSize,totals.Sum(total=>total.MovementCount));
        }
        catch (SqlException error) when (error.Number == 51412 && maximumRows.HasValue)
        {
            throw new AccountingValidationException($"El libro auxiliar supera {maximumRows.Value:N0} registros entre movimientos y saldos por cuenta. Reduce el período, el rango de cuentas o los filtros para imprimirlo o exportarlo completo.", error);
        }
    }
}
