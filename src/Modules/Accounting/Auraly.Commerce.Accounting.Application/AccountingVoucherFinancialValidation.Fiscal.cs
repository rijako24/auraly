using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Taxation.Contracts;
using Auraly.Contracts.Fiscal;

namespace Auraly.Commerce.Accounting.Application;

public sealed record VoucherFiscalCalculatedAcquisition(
    VoucherFiscalAcquisition Value, string TaxName, string DianTaxCode,
    decimal TaxRate, decimal VatAmount);

public sealed record VoucherFiscalCalculation(
    Guid SupplierId, IReadOnlyList<VoucherFiscalCalculatedAcquisition> Acquisitions,
    WithholdingCalculationSnapshot Withholding, string CalculationHash,
    IReadOnlyList<string> Diagnostics, bool CanConfirm,
    VoucherFiscalCancellationSource? Cancellation = null);

// The original is resolved through the captured payable. There is no second
// editable original-document ID or editable copy of its journal/fiscal facts.
public sealed record VoucherFiscalCancellationSource(
    ConfirmManualAccountingVoucherRequest Original, Guid PayableId,
    Guid? PartySiteId, PurchaseSupportFiscalSnapshot FiscalSnapshot);

public static partial class AccountingVoucherFinancialValidation
{
    public static VoucherFiscalPreview ValidateFiscal(
        ConfirmManualAccountingVoucherRequest request, VoucherFiscalCalculation calculation,
        VoucherFinancialContext context)
    {
        var preview = PreviewFiscal(request, calculation, context);
        if (!preview.CanConfirm)
            throw new AccountingValidationException(string.Join(" ", preview.Diagnostics));
        return preview;
    }

    public static VoucherFiscalPreview PreviewFiscal(ConfirmManualAccountingVoucherRequest request,
        VoucherFiscalCalculation calculation, VoucherFinancialContext context) =>
        request.VoucherTypeCode == "SupportDocumentCancellation"
            ? PreviewFiscalCancellation(request, calculation, context)
            : PreviewFiscalAcquisition(request, calculation, context);

    // Prove that the fiscal acquisition and the journal describe the same
    // recognition. Tax amounts arrive from the tax owner; this never recalculates
    // tax, opens a debt, accepts money or writes a second journal.
    public static VoucherFiscalPreview PreviewFiscalAcquisition(
        ConfirmManualAccountingVoucherRequest request, VoucherFiscalCalculation calculation,
        VoucherFinancialContext context)
    {
        VoucherFiscalPreview Incomplete(string message) => new(
            FiscalAcquisitionViews(request, calculation, context), calculation.Withholding,
            calculation.CalculationHash, [.. calculation.Diagnostics, message], false);

        if (request.VoucherTypeCode != "SupportDocument" || request.PayloadVersion != 4 || request.CurrencyCode != "COP" ||
            request.FiscalCapture is not { } capture || request.MainPartyId is not { } principal ||
            calculation.Acquisitions.Count == 0 ||
            !context.Parties.Any(party => party.PartyId == principal && party.SupplierId == calculation.SupplierId))
            return Incomplete("El documento soporte requiere las bases fiscales y su proveedor principal activo.");
        if (request.Lines.Any(line => line.ExistingPosition is not null || line.FinancialGroupId is not null || line.PaymentEvidence is not null))
            return Incomplete("El documento soporte registra una adquisición; sus pagos y cruces se realizan desde el comprobante correspondiente.");
        var accounts = context.Accounts.ToDictionary(account => account.AccountId);
        var lines = request.Lines.ToDictionary(line => line.LineId!.Value);
        var gross = calculation.Acquisitions.Sum(item => item.Value.TaxExclusiveAmount + item.VatAmount);
        if (calculation.Withholding.GrossAmount != gross ||
            calculation.Withholding.WithholdingTotal != calculation.Withholding.Lines.Sum(line => line.Amount) ||
            calculation.Withholding.NetAmount != gross - calculation.Withholding.WithholdingTotal)
            return Incomplete("El cálculo tributario no coincide con las bases fiscales y su neto.");
        if (request.Lines.Any(line => !accounts.ContainsKey(line.AccountId)))
            return Incomplete("Una partida no tiene su auxiliar disponible para comprobar el soporte.");
        var obligations = request.Lines.Where(line => accounts[line.AccountId].Function == "Payable").ToArray();
        if (obligations.Length != 1 || obligations[0].PartyId != principal || obligations[0].Debit != 0 ||
            obligations[0].Credit != calculation.Withholding.NetAmount || obligations[0].Credit <= 0 ||
            obligations[0].DueDate is not { } due || due.Date < request.OccurredAt.Date)
            return Incomplete("El soporte requiere una sola obligación del proveedor principal, por su neto después de retenciones y con vencimiento válido.");
        var supplierSite = context.Parties.Single(party => party.PartyId == principal).ResolveSite(obligations[0].PartySiteId);
        if (supplierSite is null) return Incomplete("Selecciona la sede del proveedor del documento soporte.");

        var covered = new HashSet<Guid> { obligations[0].LineId!.Value };
        var inputVat = new Dictionary<Guid, decimal>();
        foreach (var acquisition in calculation.Acquisitions)
        {
            var value = acquisition.Value;
            if (!lines.TryGetValue(value.SourceLineId, out var line) || !covered.Add(value.SourceLineId))
                return Incomplete("Cada base fiscal debe identificar una partida de adquisición distinta.");
            var account = accounts[line.AccountId];
            if (account.AccountType is not ("Asset" or "Expense") || account.Function is not ("General" or "UnallocatedPurchaseCost") ||
                string.IsNullOrWhiteSpace(account.AccountCode) || line.Credit != 0 ||
                line.Debit != value.TaxExclusiveAmount + (value.TaxTreatment == "CapitalizedCost" ? acquisition.VatAmount : 0))
                return Incomplete("El débito de la adquisición debe coincidir con su base y el IVA que sea mayor valor del costo.");
            if (value.TaxTreatment == "DeductibleInputVat" && acquisition.VatAmount > 0)
            {
                if (value.InputVatSourceLineId is not { } vatLine)
                    return Incomplete("Vincula la partida del IVA descontable de cada base que tenga ese tratamiento.");
                inputVat[vatLine] = inputVat.GetValueOrDefault(vatLine) + acquisition.VatAmount;
            }
            else if (value.InputVatSourceLineId is not null)
                return Incomplete("Una base sin IVA descontable no debe vincular otra partida de IVA.");
        }
        foreach (var (lineId, amount) in inputVat)
        {
            if (!lines.TryGetValue(lineId, out var line) || !covered.Add(lineId) || line.Debit != amount || line.Credit != 0 ||
                accounts[line.AccountId].AccountType != "Asset" || accounts[line.AccountId].Function != "General")
                return Incomplete("El IVA descontable debe corresponder a sus bases y a un débito separado de activo, sin función de tesorería ni cartera.");
        }

        var taxes = calculation.Withholding.Lines.Where(line => line.Amount > 0)
            .ToDictionary(line => (line.ManualLineId ?? line.RuleId, Manual: line.ManualLineId.HasValue));
        var bindings = capture.WithholdingBindings ?? [];
        if (taxes.Count != bindings.Count)
            return Incomplete("Vincula cada retención calculada con su partida contable.");
        var taxLines = new Dictionary<Guid, decimal>();
        foreach (var binding in bindings)
        {
            var key = (binding.RuleId ?? binding.ManualLineId!.Value, binding.ManualLineId.HasValue);
            if (!taxes.Remove(key, out var tax) || !lines.TryGetValue(binding.SourceLineId, out var line) ||
                accounts[line.AccountId].AccountType != "Liability" || accounts[line.AccountId].Function != "General" ||
                tax.AccountId is { } configured && configured != line.AccountId)
                return Incomplete("La retención debe usar la cuenta de pasivo indicada por su cálculo y la partida vinculada.");
            taxLines[binding.SourceLineId] = taxLines.GetValueOrDefault(binding.SourceLineId) + tax.Amount;
        }
        foreach (var (lineId, amount) in taxLines)
        {
            var line = lines[lineId];
            if (!covered.Add(lineId) || line.Credit != amount || line.Debit != 0)
                return Incomplete("El crédito de retenciones debe coincidir con el total calculado para esa partida.");
        }
        if (covered.Count != request.Lines.Count)
            return Incomplete("Todas las partidas del soporte deben corresponder a adquisición, IVA, retenciones o su obligación.");
        return new(FiscalAcquisitionViews(request, calculation, context), calculation.Withholding, calculation.CalculationHash,
            calculation.Diagnostics, calculation.CanConfirm, obligations[0].LineId, supplierSite);
    }

    private static IReadOnlyList<VoucherFiscalAcquisitionView> FiscalAcquisitionViews(
        ConfirmManualAccountingVoucherRequest request, VoucherFiscalCalculation calculation,
        VoucherFinancialContext context)
    {
        var lines = request.Lines.ToDictionary(line => line.LineId!.Value);
        var accounts = context.Accounts.ToDictionary(account => account.AccountId);
        return calculation.Acquisitions.Select((acquisition, index) => {
            if (!lines.TryGetValue(acquisition.Value.SourceLineId, out var line) ||
                !accounts.TryGetValue(line.AccountId, out var account) || string.IsNullOrWhiteSpace(account.AccountCode))
                throw new AccountingValidationException("Una base fiscal perdió su partida o auxiliar. Revisa el comprobante.");
            return new VoucherFiscalAcquisitionView(acquisition.Value, index + 1, account.AccountCode,
                line.Description, acquisition.TaxName, acquisition.DianTaxCode, acquisition.TaxRate, acquisition.VatAmount);
        }).ToArray();
    }
}
