using Auraly.Contracts.Purchasing;
using System.Data;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlGoodsReceiptWorkspaceStore
{
    public async Task<GoodsReceiptCostVoucherCandidatePage> ListCostVouchersAsync(PurchasingUserIdentity user,
        Guid partyId, string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            CREATE TABLE #EntryKeys(EntryId uniqueidentifier NOT NULL PRIMARY KEY);
            INSERT #EntryKeys(EntryId)
            SELECT entry.EntryId FROM dbo.AccountingEntryLines line
            JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId
              AND entry.TenantId=@TenantId AND entry.BusinessId=@BusinessId
            WHERE line.PartyId=@PartyId AND line.Debit>0
              AND entry.SourceDocumentType IN(N'Expense',N'ManualAccountingVoucher',N'GoodsReceiptCostDocument')
            UNION
            SELECT entry.EntryId FROM accounting.VoucherDrafts draft
            JOIN dbo.AccountingEntries entry ON entry.SourceDocumentId=draft.DocumentId
              AND entry.SourceDocumentType=draft.DocumentType AND entry.TenantId=@TenantId AND entry.BusinessId=@BusinessId
            WHERE draft.TenantId=@TenantId AND draft.BusinessId=@BusinessId AND draft.MainPartyId=@PartyId
              AND draft.DocumentType=N'ManualAccountingVoucher'
            UNION
            SELECT entry.EntryId FROM dbo.Suppliers supplier
            JOIN dbo.Expenses expense ON expense.SupplierId=supplier.SupplierId AND expense.BusinessId=@BusinessId
            JOIN dbo.AccountingEntries entry ON entry.SourceDocumentId=expense.ExpenseId
              AND entry.SourceDocumentType=N'Expense' AND entry.TenantId=@TenantId AND entry.BusinessId=@BusinessId
            WHERE supplier.PartyId=@PartyId AND supplier.TenantId=@TenantId
            UNION
            SELECT entry.EntryId FROM dbo.Suppliers supplier
            JOIN purchasing.GoodsReceiptCostDocuments document ON document.SupplierId=supplier.SupplierId
              AND document.SourceEntryId IS NULL
            JOIN dbo.AccountingEntries entry ON entry.SourceDocumentId=document.CostDocumentId
              AND entry.SourceDocumentType=N'GoodsReceiptCostDocument'
              AND entry.TenantId=@TenantId AND entry.BusinessId=@BusinessId
            WHERE supplier.PartyId=@PartyId AND supplier.TenantId=@TenantId;

            SELECT line.EntryId,line.LineNumber,line.AccountCode,line.AccountName,
              line.Description,line.PartyId,line.EligibleFunctionalAmount,line.AssignedFunctionalAmount
            INTO #CostLines FROM #EntryKeys keyset
            CROSS APPLY purchasing.ReceiptCostVoucherLines(keyset.EntryId) line
            WHERE line.EligibleFunctionalAmount>0;

            SELECT entry.EntryId,entry.EntryNumber,entry.OccurredAt,entry.Description,
              COALESCE(expense.DocumentNumber,costDocument.DocumentNumber,draft.Reference,entry.EntryNumber) DocumentNumber,
              line.LineNumber,line.AccountCode,line.AccountName,line.Description Concept,
              line.EligibleFunctionalAmount,line.AssignedFunctionalAmount
            INTO #Eligible
            FROM #CostLines line
            JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId
              AND entry.TenantId=@TenantId AND entry.BusinessId=@BusinessId
            JOIN dbo.AccountingSourceDocuments source ON source.SourceDocumentId=entry.SourceDocumentId
              AND source.SourceDocumentType=entry.SourceDocumentType
              AND source.TenantId=entry.TenantId AND source.BusinessId=entry.BusinessId
            LEFT JOIN dbo.Expenses expense ON expense.ExpenseId=entry.SourceDocumentId
              AND entry.SourceDocumentType=N'Expense' AND expense.BusinessId=@BusinessId
            LEFT JOIN dbo.Suppliers expenseSupplier ON expenseSupplier.SupplierId=expense.SupplierId
              AND expenseSupplier.TenantId=@TenantId
            LEFT JOIN accounting.VoucherDrafts draft ON draft.DocumentId=entry.SourceDocumentId
              AND draft.DocumentType=N'ManualAccountingVoucher' AND draft.TenantId=@TenantId AND draft.BusinessId=@BusinessId
            LEFT JOIN purchasing.GoodsReceiptCostDocuments costDocument ON costDocument.CostDocumentId=entry.SourceDocumentId
              AND entry.SourceDocumentType=N'GoodsReceiptCostDocument' AND costDocument.SourceEntryId IS NULL
            LEFT JOIN dbo.Suppliers costSupplier ON costSupplier.SupplierId=costDocument.SupplierId
              AND costSupplier.TenantId=@TenantId
            WHERE (line.PartyId=@PartyId OR
                COALESCE(draft.MainPartyId,expenseSupplier.PartyId,costSupplier.PartyId)=@PartyId);

            SELECT candidate.EntryId,candidate.EntryNumber,candidate.OccurredAt,candidate.Description,
              candidate.DocumentNumber,MIN(candidate.LineNumber) FirstLineNumber,
              COUNT(*) EligibleLineCount,SUM(candidate.EligibleFunctionalAmount) EligibleFunctionalAmount,
              SUM(candidate.AssignedFunctionalAmount) AssignedFunctionalAmount
            INTO #Candidates FROM #Eligible candidate
            GROUP BY candidate.EntryId,candidate.EntryNumber,candidate.OccurredAt,candidate.Description,candidate.DocumentNumber
            HAVING @Search IS NULL OR MAX(CASE WHEN candidate.EntryNumber LIKE @Search
              OR candidate.DocumentNumber LIKE @Search
              OR candidate.Description COLLATE Latin1_General_100_CI_AI LIKE @Search
              OR candidate.Concept COLLATE Latin1_General_100_CI_AI LIKE @Search
              OR candidate.AccountCode LIKE @Search
              OR candidate.AccountName COLLATE Latin1_General_100_CI_AI LIKE @Search
              THEN 1 ELSE 0 END)=1;

            SELECT COUNT(*) FROM #Candidates;
            SELECT candidate.EntryId,candidate.EntryNumber,candidate.DocumentNumber,candidate.OccurredAt,
              party.DisplayName,candidate.Description,firstLine.AccountCode,firstLine.AccountName,
              firstLine.Concept,candidate.EligibleLineCount,candidate.EligibleFunctionalAmount,
              candidate.AssignedFunctionalAmount
            FROM #Candidates candidate
            JOIN #Eligible firstLine ON firstLine.EntryId=candidate.EntryId AND firstLine.LineNumber=candidate.FirstLineNumber
            JOIN dbo.Parties party ON party.PartyId=@PartyId AND party.TenantId=@TenantId
            ORDER BY candidate.OccurredAt DESC,candidate.EntryId DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@PartyId", partyId);
        command.Parameters.Add("@Search", SqlDbType.NVarChar, 162).Value =
            search is null ? DBNull.Value : $"%{search}%";
        command.Parameters.AddWithValue("@Offset", ((long)page - 1) * pageSize);
        command.Parameters.AddWithValue("@PageSize", pageSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new DataException("No se recibió el total de comprobantes disponibles.");
        var total = reader.GetInt32(0);
        await reader.NextResultAsync(cancellationToken);
        var items = new List<GoodsReceiptCostVoucherCandidate>();
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetDateTimeOffset(3),
                reader.GetString(4),reader.GetString(5),reader.GetString(6),reader.GetString(7),
                reader.GetString(8),reader.GetInt32(9),reader.GetDecimal(10),reader.GetDecimal(11)));
        return new(items,page,pageSize,total);
    }

    public async Task<GoodsReceiptCostVoucher?> GetCostVoucherAsync(PurchasingUserIdentity user, Guid entryId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            DECLARE @ScopedEntryId uniqueidentifier=(SELECT entry.EntryId FROM dbo.AccountingEntries entry
              JOIN dbo.AccountingSourceDocuments source ON source.SourceDocumentId=entry.SourceDocumentId
                AND source.SourceDocumentType=entry.SourceDocumentType AND source.TenantId=entry.TenantId AND source.BusinessId=entry.BusinessId
              WHERE entry.EntryId=@EntryId AND entry.TenantId=@TenantId AND entry.BusinessId=@BusinessId);
            SELECT EntryId,EntryNumber,SourceDocumentId,SourceDocumentType FROM dbo.AccountingEntries WHERE EntryId=@ScopedEntryId;
            SELECT line.LineNumber,line.AccountId,line.AccountCode,line.AccountName,line.PartyId,
              original.PartyNameSnapshot,line.Description,line.Debit,line.Credit,
              line.EligibleFunctionalAmount,line.AssignedFunctionalAmount
            FROM purchasing.ReceiptCostVoucherLines(@ScopedEntryId) line
            JOIN dbo.AccountingEntryLines original ON original.EntryId=line.EntryId AND original.LineNumber=line.LineNumber
            ORDER BY line.LineNumber;
            """, connection);
        command.Parameters.AddWithValue("@EntryId", entryId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var number = reader.GetString(1);
        var documentId = reader.GetGuid(2);
        var type = reader.GetString(3);
        var lines = new List<GoodsReceiptCostVoucherLine>();
        await reader.NextResultAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            lines.Add(new(reader.GetInt32(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6), reader.GetDecimal(7), reader.GetDecimal(8), reader.GetDecimal(9), reader.GetDecimal(10)));
        return new(entryId, number, documentId, type, lines);
    }
}
