using System.Text.Json;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingStore
{
    private static async Task ValidateJournalCancellationAsync(SqlConnection connection,
        SqlTransaction transaction, AccountingUserIdentity user,
        ConfirmManualAccountingVoucherRequest request, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            SELECT source.PayloadJson,entry.EntryId,job.Status,source.OccurredAt
            FROM dbo.AccountingSourceDocuments source
            JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=source.SourceDocumentId
              AND job.SourceDocumentType=source.SourceDocumentType AND job.TenantId=source.TenantId
              AND job.BusinessId=source.BusinessId
            JOIN dbo.AccountingEntries entry ON entry.SourceDocumentId=source.SourceDocumentId
              AND entry.SourceDocumentType=source.SourceDocumentType AND entry.TenantId=source.TenantId
              AND entry.BusinessId=source.BusinessId
            WHERE source.SourceDocumentId=@OriginalId AND source.SourceDocumentType=N'ManualAccountingVoucher'
              AND source.TenantId=@TenantId AND source.BusinessId=@BusinessId
              AND source.ReversesDocumentId IS NULL;
            SELECT line.AccountId,line.PartyId,line.CostCenterId,line.Description,line.Debit,line.Credit
            FROM dbo.AccountingEntryLines line
            JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId
              AND entry.SourceDocumentId=@OriginalId AND entry.SourceDocumentType=N'ManualAccountingVoucher'
              AND entry.TenantId=@TenantId AND entry.BusinessId=@BusinessId
            ORDER BY line.LineNumber;
            """, connection, transaction);
        command.Parameters.AddWithValue("@OriginalId", request.ReversesDocumentId!.Value);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token))
            throw new AccountingConflictException("El comprobante original no está contabilizado en esta sede o ya es una anulación.");
        var original = JsonSerializer.Deserialize<ConfirmManualAccountingVoucherRequest>(reader.GetString(0));
        var effects = original?.FinancialEffects;
        if (original is not { PayloadVersion: 4, VoucherTypeCode: "Accounting", FiscalCapture: null } ||
            original.BusinessId != user.BusinessId || original.VoucherId != request.ReversesDocumentId ||
            original.MainPartyId != request.MainPartyId || reader.GetString(2) != "Posted" ||
            request.OccurredAt < reader.GetDateTimeOffset(3) || effects is null ||
            effects.Positions.Count != 0 || effects.Payments.Count != 0 ||
            effects.CreditApplications.Count != 0 || effects.DirectMovements.Count != 0 ||
            effects.Unapplications?.Count > 0)
            throw new AccountingConflictException("El original tiene efectos financieros o fiscales, o su asiento no permite anulación contable simple.");
        await reader.NextResultAsync(token);
        var number = 0;
        while (await reader.ReadAsync(token))
        {
            if (number >= request.Lines.Count)
                throw new AccountingConflictException("Las partidas de la anulación no coinciden con el asiento original.");
            var inverse = request.Lines[number++];
            if (inverse.AccountId != reader.GetGuid(0) ||
                inverse.PartyId != (reader.IsDBNull(1) ? null : reader.GetGuid(1)) ||
                inverse.CostCenterId != (reader.IsDBNull(2) ? null : reader.GetGuid(2)) ||
                inverse.Description != reader.GetString(3) ||
                inverse.Debit != reader.GetDecimal(5) || inverse.Credit != reader.GetDecimal(4) ||
                inverse.ExistingPosition is not null || inverse.FinancialGroupId is not null)
                throw new AccountingConflictException("Las partidas de la anulación deben invertir exactamente el asiento original.");
        }
        if (number != request.Lines.Count || number < 2)
            throw new AccountingConflictException("La anulación requiere todas las partidas del asiento original.");
    }
}
