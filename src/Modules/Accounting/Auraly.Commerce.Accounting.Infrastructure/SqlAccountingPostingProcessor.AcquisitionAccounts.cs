using System.Text.Json;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingPostingProcessor
{
    private static decimal DeductiblePurchaseVat(IEnumerable<GoodsReceiptLineSnapshot> lines) =>
        lines.Where(line => line.TaxTreatment == PurchasingTaxTreatments.DeductibleInputVat)
            .Sum(line => line.FunctionalTaxAmount);

    // One acquisition-scoped read, irrespective of its number of lines. The
    // first recognition identifies the account; every subsequent correction
    // must reconcile to that same posted auxiliary. Current mappings are not
    // evidence of where an existing amount was recognized.
    private static async Task<(Guid? AccountId, string? Issue)> LoadAcquisitionVatAccountAsync(
        SqlConnection connection, SqlTransaction transaction, Guid tenantId, Guid businessId,
        Guid receiptId, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            WITH acquisition AS (
              SELECT @ReceiptId DocumentId,N'GoodsReceipt' DocumentType,0 RevisionNumber
              UNION ALL
              SELECT RevisionId,N'GoodsReceiptCostRevision',RevisionNumber
              FROM purchasing.GoodsReceiptCostRevisions
              WHERE GoodsReceiptId=@ReceiptId AND BusinessId=@BusinessId
            )
            SELECT acquisition.DocumentType,source.PayloadJson,job.Status,
              JSON_QUERY(job.ResolvedAccountMappingsJson) MappingJson,
              (SELECT line.AccountId,SUM(line.Debit-line.Credit) Amount,
                MIN(CASE WHEN account.IsActive=1 AND account.AllowsPosting=1
                  THEN 1 ELSE 0 END) Available
               FROM dbo.AccountingEntryLines line
               JOIN dbo.AccountingAccounts account WITH(UPDLOCK,HOLDLOCK,INDEX(PK_AccountingAccounts))
                 ON account.AccountId=line.AccountId AND account.TenantId=@TenantId
               WHERE line.EntryId=entry.EntryId GROUP BY line.AccountId FOR JSON PATH) LinesJson
            FROM acquisition
            LEFT JOIN dbo.AccountingSourceDocuments source
              ON source.SourceDocumentId=acquisition.DocumentId
                AND source.SourceDocumentType=acquisition.DocumentType
                AND source.TenantId=@TenantId AND source.BusinessId=@BusinessId
            LEFT JOIN dbo.AccountingPostingJobs job
              ON job.SourceDocumentId=source.SourceDocumentId AND job.SourceDocumentType=source.SourceDocumentType
                AND job.TenantId=source.TenantId AND job.BusinessId=source.BusinessId
                AND job.SourcePayloadHash=source.PayloadHash
            LEFT JOIN dbo.AccountingEntries entry
              ON entry.SourceDocumentId=source.SourceDocumentId AND entry.SourceDocumentType=source.SourceDocumentType
                AND entry.TenantId=source.TenantId AND entry.BusinessId=source.BusinessId
                AND entry.SourcePayloadHash=source.PayloadHash
            ORDER BY acquisition.RevisionNumber;
            """, connection, transaction);
        command.Parameters.AddWithValue("@ReceiptId", receiptId);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        Guid? originalAccount = null;
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            if (reader.IsDBNull(1)) return (null, "Falta la fuente contable de la adquisición.");
            decimal recognizedVat;
            if (reader.GetString(0) == PurchasingDocumentTypes.GoodsReceipt)
            {
                var receipt = GoodsReceiptContractSerializer.Deserialize(reader.GetString(1));
                if (receipt.DocumentId != receiptId || receipt.BusinessId != businessId || receipt.TenantId != tenantId)
                    throw new InvalidOperationException("The acquisition source does not match its accounting scope.");
                recognizedVat = DeductiblePurchaseVat(receipt.Lines);
            }
            else
            {
                var revision = GoodsReceiptContractSerializer.DeserializeCostRevision(reader.GetString(1));
                if (revision.GoodsReceiptId != receiptId || revision.BusinessId != businessId || revision.TenantId != tenantId)
                    throw new InvalidOperationException("The acquisition revision does not match its accounting scope.");
                recognizedVat = revision.CostEdit is { } edit
                    ? DeductiblePurchaseVat(edit.CurrentLines) - DeductiblePurchaseVat(edit.PreviousLines) : 0m;
            }
            if (recognizedVat == 0m) continue;
            if (reader.IsDBNull(2) || reader.GetString(2) != "Posted")
                return (null, "El IVA de la adquisición tiene una contabilización anterior pendiente.");
            if (originalAccount is null)
            {
                if (reader.IsDBNull(3)) return (null, "Falta identificar el auxiliar del primer IVA contabilizado.");
                using var mappings = JsonDocument.Parse(reader.GetString(3));
                if (mappings.RootElement.ValueKind != JsonValueKind.Array)
                    return (null, "El vínculo histórico del IVA no contiene una lista de auxiliares válida.");
                var candidates = mappings.RootElement.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Object &&
                        item.TryGetProperty("Category", out var category) &&
                        category.ValueKind == JsonValueKind.String && category.GetString() == AccountingCategories.InputVat)
                    .Select(item => item.TryGetProperty("AccountId", out var account) ? account : default).ToArray();
                if (candidates.Length != 1 || candidates[0].ValueKind != JsonValueKind.String ||
                    !candidates[0].TryGetGuid(out var accountId))
                    return (null, "El auxiliar del primer IVA contabilizado es ambiguo.");
                originalAccount = accountId;
            }
            using var lines = JsonDocument.Parse(reader.GetString(4));
            var originalLines = lines.RootElement.EnumerateArray()
                .Where(line => line.GetProperty("AccountId").GetGuid() == originalAccount).ToArray();
            if (originalLines.Length != 1 || originalLines[0].GetProperty("Available").GetInt32() != 1 ||
                originalLines[0].GetProperty("Amount").GetDecimal() != recognizedVat)
                return (null, "El IVA histórico no concilia en un único auxiliar activo. Requiere revisión del asiento original y sus correcciones.");
        }
        return (originalAccount, null);
    }
}
