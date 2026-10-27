using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Fiscal.Core;

namespace Auraly.Commerce.Accounting.Application;

public static partial class AccountingVoucherFinancialValidation
{
    public static IReadOnlyList<VoucherDraftLineView> PrepareFiscalCancellationLines(
        VoucherFiscalCancellationSource source, IReadOnlyList<VoucherDraftLineView> originalLines)
    {
        var opening = source.Original.FinancialEffects!.Positions.Single();
        return originalLines.Select(line => line with { Value = line.Value with {
            Description = "Anulación: " + (line.Value.Description.Length > 489 ? line.Value.Description[..489] : line.Value.Description),
            Debit = line.Value.Credit, Credit = line.Value.Debit,
            BaseAmount = line.Value.BaseAmount is { } basis ? -basis : null,
            ExistingPosition = line.Value.LineId == opening.SourceLineId ? new("Payable", source.PayableId) : null,
            PartySiteId = line.Value.LineId == opening.SourceLineId ? source.PartySiteId : line.Value.PartySiteId,
            FinancialGroupId = null, PaymentEvidence = null, DueDate = null } }).ToArray();
    }

    public static VoucherFiscalPreview PreviewFiscalCancellation(ConfirmManualAccountingVoucherRequest request,
        VoucherFiscalCalculation calculation, VoucherFinancialContext context)
    {
        VoucherFiscalPreview Incomplete(string message) => new([], calculation.Withholding,
            calculation.CalculationHash, [.. calculation.Diagnostics, message], false);
        if (request.PayloadVersion != 4 || request.VoucherTypeCode != "SupportDocumentCancellation" ||
            request.CurrencyCode != "COP" || request.FinancialEffects is not null ||
            request.FiscalCapture is not { Acquisitions.Count: 0 } capture ||
            capture.WithholdingBindings is { Count: > 0 } || capture.WithholdingAdjustments is { Count: > 0 } ||
            request.WorkSessionId is not null || calculation.Cancellation is not { } source)
            return Incomplete("La anulación requiere su soporte original y no admite otra adquisición, pagos o ajustes tributarios.");
        var original = source.Original;
        if (original.PayloadVersion != 4 || original.VoucherTypeCode != "SupportDocument" ||
            original.Lines is not { Count: >= 2 and <= 500 } || original.Lines.Any(line => line.LineId is null) ||
            original.Lines.Select(line => line.LineId).Distinct().Count() != original.Lines.Count ||
            original.VoucherId == request.VoucherId || original.BusinessId != request.BusinessId ||
            original.CurrencyCode != request.CurrencyCode || request.MainPartyId is not { } principal ||
            original.MainPartyId != principal || DianFiscalDateTime.DateInColombia(request.OccurredAt) <
                DianFiscalDateTime.DateInColombia(original.OccurredAt) ||
            !context.Parties.Any(party => party.PartyId == principal && party.SupplierId == calculation.SupplierId))
            return Incomplete("La anulación debe conservar proveedor, sede y moneda del soporte y una fecha permitida posterior a su emisión.");
        if (original.FinancialEffects is not { Positions.Count: 1 } accepted ||
            accepted.Positions[0] is not { EffectKind: "Open", SubledgerKind: "Payable" } opening ||
            opening.PositionId != source.PayableId || opening.PartyId != principal ||
            opening.CounterpartyRoleId != calculation.SupplierId || opening.PartySiteId != source.PartySiteId ||
            opening.Amount != calculation.Withholding.NetAmount || opening.Amount <= 0)
            return Incomplete("La obligación original del soporte requiere conciliar su apertura antes de anularla.");
        var originals = original.Lines.ToDictionary(line => line.LineId!.Value);
        var accounts = context.Accounts.ToDictionary(account => account.AccountId);
        if (request.Lines.Count != originals.Count || request.Lines.Any(line => line.LineId is null) ||
            request.Lines.Select(line => line.LineId).Distinct().Count() != request.Lines.Count)
            return Incomplete("La anulación total conserva cada partida del asiento original.");
        foreach (var line in request.Lines)
        {
            if (!accounts.TryGetValue(line.AccountId, out var account) ||
                line.LineId == opening.SourceLineId && account.Function != "Payable" ||
                line.LineId != opening.SourceLineId && account.Function is not ("General" or "UnallocatedPurchaseCost"))
                return Incomplete("Las partidas originales deben conservar sus auxiliares y su función financiera antes de anular.");
            if (!originals.TryGetValue(line.LineId!.Value, out var prior) || line.AccountId != prior.AccountId ||
                line.PartyId != prior.PartyId || line.CostCenterId != prior.CostCenterId ||
                line.Debit != prior.Credit || line.Credit != prior.Debit ||
                line.BaseAmount != (prior.BaseAmount is { } basis ? -basis : null) ||
                line.FinancialGroupId is not null || line.PaymentEvidence is not null || line.DueDate is not null ||
                line.PartySiteId != (line.LineId == opening.SourceLineId ? source.PartySiteId : prior.PartySiteId))
                return Incomplete("La anulación debe invertir exactamente las cuentas, dimensiones, débitos, créditos y bases originales.");
            if (line.LineId == opening.SourceLineId)
            {
                if (line.ExistingPosition is not { SubledgerKind: "Payable", ReversesTransactionId: null } target ||
                    target.SubledgerId != source.PayableId)
                    return Incomplete("Vincula la partida de proveedores con la obligación original que se anulará.");
            }
            else if (line.ExistingPosition is not null)
                return Incomplete("La anulación no cruza ni paga otras obligaciones.");
        }
        var position = context.Positions.SingleOrDefault(item => item.Kind == "Payable" && item.PositionId == source.PayableId);
        if (position is null || position.OpeningIssue is not null || position.AccountId != opening.AccountId ||
            position.PartyId != principal || position.RoleId != calculation.SupplierId ||
            position.PartySiteId != source.PartySiteId || position.CurrencyCode != request.CurrencyCode)
            return Incomplete("La cuenta, proveedor y sede de apertura deben conciliar con la obligación del soporte.");
        return new([], calculation.Withholding, calculation.CalculationHash, calculation.Diagnostics,
            calculation.CanConfirm, opening.SourceLineId, source.PartySiteId);
    }

    private static VoucherFinancialEffects NormalizeFiscalCancellation(AccountingUserIdentity user,
        ConfirmManualAccountingVoucherRequest request, VoucherFinancialContext context,
        IAuralyIdGenerator ids, VoucherFiscalPreview? proof)
    {
        if (proof is not { CanConfirm: true, PayableSourceLineId: { } lineId } || request.FiscalCapture is null)
            throw new AccountingValidationException("No se anuló el soporte. Revisa el original y sus dependencias antes de contabilizar.");
        var line = request.Lines.Single(item => item.LineId == lineId);
        var target = line.ExistingPosition
            ?? throw new AccountingValidationException("La anulación requiere la obligación original.");
        var position = context.Positions.Single(item => item.Kind == "Payable" && item.PositionId == target.SubledgerId);
        return new(user.UserId, [], [new(ids.NewId(), lineId, "Cancellation", "Payable", lineId,
            position.PositionId, line.AccountId, position.PartyId, position.RoleId, position.PartySiteId, line.Debit, null)], [], []);
    }
}
