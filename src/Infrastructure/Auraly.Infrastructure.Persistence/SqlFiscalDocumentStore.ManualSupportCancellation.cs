using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Fiscal;
using Auraly.Fiscal.Core;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlFiscalDocumentStore
{
    private static async Task<VoucherFiscalCalculation> ReviewManualSupportCancellationAsync(
        SqlConnection connection, SqlTransaction? transaction, AccountingUserIdentity user,
        ConfirmManualAccountingVoucherRequest request, CancellationToken token)
    {
        var targets = request.Lines.Where(line => line.ExistingPosition is not null).ToArray();
        if (targets.Length != 1 || targets[0].ExistingPosition is not
            { SubledgerKind: "Payable", ReversesTransactionId: null } target)
            throw new AccountingValidationException("Selecciona la obligación del documento soporte que deseas anular.");
        var facts = await LoadManualSupportCancellationAsync(connection, transaction, user, target.SubledgerId, token);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            Capture = request with { FinancialEffects = null,
                FiscalCapture = request.FiscalCapture! with { CalculationHash = null } },
            Original = facts.Source.Original, Fiscal = facts.Source.FiscalSnapshot, issues = facts.Diagnostics }, ManualReviewHashJson))));
        return new(facts.SupplierId, [], facts.Source.FiscalSnapshot.ManualAcquisition!.Withholding,
            hash, facts.Diagnostics, facts.Diagnostics.Count == 0, facts.Source);
    }

    public async Task<VoucherDraftPreparation> PrepareCancellationCaptureAsync(SqlConnection connection,
        SqlTransaction transaction, AccountingUserIdentity user, Guid payableId, CancellationToken token)
    {
        var facts = await LoadManualSupportCancellationAsync(connection, transaction, user, payableId, token);
        if (facts.Diagnostics.Count > 0)
            throw new AccountingValidationException(string.Join(" ", facts.Diagnostics));
        var reversed = AccountingVoucherFinancialValidation.PrepareFiscalCancellationLines(facts.Source, facts.Lines);
        return new($"Anulación del soporte {facts.Source.FiscalSnapshot.OriginalSupportNumber}",
            facts.Source.FiscalSnapshot.OriginalSupportNumber, facts.Source.Original.MainPartyId!.Value,
            facts.PartyName, facts.PartyIdentification, reversed);
    }

    private sealed record ManualSupportCancellationFacts(VoucherFiscalCancellationSource Source,
        Guid SupplierId, IReadOnlyList<string> Diagnostics, IReadOnlyList<VoucherDraftLineView> Lines,
        string PartyName, string? PartyIdentification);

    private static async Task<ManualSupportCancellationFacts> LoadManualSupportCancellationAsync(
        SqlConnection connection, SqlTransaction? transaction, AccountingUserIdentity user,
        Guid payableId, CancellationToken token)
    {
        // The existing payable lock serializes payments; the immutable source
        // lock is the same lock used by Purchasing for cost associations.
        await using var command = new SqlCommand("""
            DECLARE @OriginalId uniqueidentifier,@EntryId uniqueidentifier;
            SELECT @OriginalId=payable.SourceDocumentId,@EntryId=payable.OpeningEntryId
            FROM dbo.Payables payable WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.Businesses business ON business.BusinessId=payable.BusinessId AND business.TenantId=@TenantId
            WHERE payable.PayableId=@PayableId AND payable.BusinessId=@BusinessId
              AND payable.SourceDocumentType=N'ManualAccountingVoucher';
            SELECT source.PayloadJson,snapshot.SnapshotJson,fiscal.FiscalStatus,fiscal.FiscalNumber,
              fiscal.UniqueCode,fiscal.IssuedAt,payable.OriginalAmount,payable.OutstandingAmount,
              payable.Status,payable.SupplierId,payable.PartySiteId,available.AvailableAmount,
              available.PendingAdjustmentCount,
              CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM purchasing.GoodsReceiptCostDocuments
                WHERE SourceEntryId=@EntryId) THEN 1 ELSE 0 END),
              CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.PayableTransactions history
                WHERE history.PayableId=@PayableId AND history.TransactionType NOT IN
                  (N'Opening',N'Payment',N'Unapplication',N'CreditApplication',N'CreditUnapplication'))
                THEN 1 ELSE 0 END),party.DisplayName,party.Identification,party.PartyId
            FROM dbo.AccountingSourceDocuments source WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.Payables payable ON payable.PayableId=@PayableId AND payable.SourceDocumentId=source.SourceDocumentId
              AND payable.SourceDocumentType=source.SourceDocumentType AND payable.BusinessId=source.BusinessId
            JOIN dbo.AccountingEntries entry ON entry.EntryId=@EntryId AND entry.SourceDocumentId=source.SourceDocumentId
              AND entry.SourceDocumentType=source.SourceDocumentType AND entry.TenantId=@TenantId AND entry.BusinessId=@BusinessId
            JOIN dbo.Suppliers supplier ON supplier.SupplierId=payable.SupplierId AND supplier.TenantId=@TenantId
            JOIN dbo.Parties party ON party.PartyId=supplier.PartyId AND party.TenantId=@TenantId
            JOIN dbo.FiscalDocuments fiscal ON fiscal.DocumentId=source.SourceDocumentId
              AND fiscal.BusinessId=@BusinessId AND fiscal.SourceDocumentType=source.SourceDocumentType
              AND fiscal.FiscalDocumentType=N'SupportDocument'
            JOIN fiscal.PurchaseSupportFiscalSnapshots snapshot ON snapshot.DocumentId=fiscal.DocumentId
            CROSS APPLY accounting.ObligationAvailability(@BusinessId,N'Payable',@PayableId,payable.OutstandingAmount,NULL) available
            WHERE source.SourceDocumentId=@OriginalId AND source.SourceDocumentType=N'ManualAccountingVoucher'
              AND source.TenantId=@TenantId AND source.BusinessId=@BusinessId;
            SELECT line.SourceLineId,line.AccountId,line.PartyId,line.CostCenterId,line.Debit,line.Credit,line.BaseAmount,
              account.Code,account.Name,party.DisplayName,party.Identification,center.Name
            FROM dbo.AccountingEntryLines line
            JOIN dbo.AccountingAccounts account ON account.AccountId=line.AccountId AND account.TenantId=@TenantId
            LEFT JOIN dbo.Parties party ON party.PartyId=line.PartyId AND party.TenantId=@TenantId
            LEFT JOIN dbo.AccountingCostCenters center ON center.CostCenterId=line.CostCenterId AND center.BusinessId=@BusinessId
            WHERE line.EntryId=@EntryId ORDER BY line.LineNumber;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@PayableId", payableId);
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token))
            throw new AccountingValidationException("La obligación no corresponde a un soporte manual contabilizado de esta sede. Los gastos y compras conservan su anulación o devolución de origen.");
        var original = JsonSerializer.Deserialize<ConfirmManualAccountingVoucherRequest>(reader.GetString(0))
            ?? throw new AccountingValidationException("Falta la captura inmutable del soporte original; concilia su origen.");
        var fiscal = PurchaseSupportFiscalSnapshotSerializer.Deserialize(reader.GetString(1));
        var supplierId = reader.GetGuid(9);
        Guid? siteId = reader.IsDBNull(10) ? null : reader.GetGuid(10);
        var partyName = reader.GetString(15);
        var partyIdentification = reader.IsDBNull(16) ? null : reader.GetString(16);
        var issues = new List<string>();
        if (reader.GetString(2) != FiscalDocumentStatusCodes.DianAccepted || reader.IsDBNull(4))
            issues.Add("El documento soporte original debe estar aceptado por la DIAN y conservar su CUDS antes de anularlo.");
        if (reader.GetDecimal(6) != reader.GetDecimal(7) || reader.GetString(8) != "Open" ||
            reader.GetDecimal(11) != reader.GetDecimal(6) || reader.GetInt64(12) != 0 || reader.GetBoolean(14))
            issues.Add("El soporte tiene pagos, cruces o ajustes aplicados o pendientes. Resuelve esas dependencias antes de anularlo.");
        if (reader.GetBoolean(13))
            issues.Add("El soporte tiene cargos asociados a compras. Resuelve esas asociaciones antes de anularlo.");
        fiscal = fiscal with { OriginalSupportNumber = reader.GetString(3),
            OriginalSupportCuds = reader.IsDBNull(4) ? null : reader.GetString(4),
            OriginalSupportIssuedOn = DianFiscalDateTime.DateInColombia(reader.GetDateTimeOffset(5)) };
        if (original.PayloadVersion != 4 || original.VoucherTypeCode != "SupportDocument" ||
            original.MainPartyId != reader.GetGuid(17) ||
            fiscal.ManualAcquisition is not { } acquisition || acquisition.DocumentId != original.VoucherId ||
            acquisition.TenantId != user.TenantId || acquisition.BusinessId != user.BusinessId ||
            acquisition.SupplierId != supplierId || original.BusinessId != user.BusinessId ||
            original.FinancialEffects is not { Positions.Count: 1 } ||
            original.FinancialEffects.Positions[0].PositionId != payableId ||
            acquisition.Withholding.NetAmount != reader.GetDecimal(6))
            throw new AccountingValidationException("La apertura contable y el snapshot fiscal original no concilian. No se anuló el soporte.");
        if (original.Lines is not { Count: >= 2 and <= 500 } || original.Lines.Any(line => line.LineId is null) ||
            original.Lines.Select(line => line.LineId).Distinct().Count() != original.Lines.Count)
            throw new AccountingValidationException("El soporte original no conserva sus partidas identificadas; concilia su origen.");
        var prior = original.Lines.ToDictionary(line => line.LineId!.Value);
        var lines = new List<ManualVoucherLineRequest>();
        var views = new List<VoucherDraftLineView>();
        await reader.NextResultAsync(token);
        while (await reader.ReadAsync(token))
        {
            if (reader.IsDBNull(0) || !prior.TryGetValue(reader.GetGuid(0), out var line))
                throw new AccountingValidationException("El asiento original no conserva sus partidas identificadas; concilia su origen.");
            var resolved = line with { AccountId = reader.GetGuid(1), PartyId = reader.IsDBNull(2) ? null : reader.GetGuid(2),
                CostCenterId = reader.IsDBNull(3) ? null : reader.GetGuid(3), Debit = reader.GetDecimal(4),
                Credit = reader.GetDecimal(5), BaseAmount = reader.IsDBNull(6) ? null : reader.GetDecimal(6) };
            lines.Add(resolved);
            views.Add(new(new(resolved.AccountId, resolved.PartyId, resolved.CostCenterId, resolved.Description,
                resolved.Debit, resolved.Credit, resolved.Reference, resolved.LineId, resolved.BaseAmount,
                PartySiteId: resolved.PartySiteId), reader.GetString(7), reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11)));
        }
        if (lines.Count != prior.Count)
            throw new AccountingValidationException("El asiento original está incompleto; concilia su origen antes de anular.");
        original = original with { Lines = lines };
        return new(new(original, payableId, siteId, fiscal), supplierId, issues, views, partyName, partyIdentification);
    }
}
