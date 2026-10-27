using Auraly.Commerce.Taxation.Contracts;

namespace Auraly.Commerce.Accounting.Contracts;

// Fiscal acquisition bases identify the journal lines they explain. They are
// document evidence, not additional postings or another editable payable.
public sealed record VoucherFiscalAcquisition(
    Guid SourceLineId, decimal TaxExclusiveAmount, Guid? TaxProfileId,
    string TaxTreatment, Guid? InputVatSourceLineId = null,
    string? WithholdingConceptCode = null);

public sealed record VoucherFiscalWithholdingBinding(
    Guid SourceLineId, Guid? RuleId = null, Guid? ManualLineId = null);

public sealed record VoucherFiscalCapture(
    IReadOnlyList<VoucherFiscalAcquisition> Acquisitions,
    IReadOnlyList<VoucherFiscalWithholdingBinding>? WithholdingBindings = null,
    IReadOnlyList<WithholdingAdjustmentRequest>? WithholdingAdjustments = null,
    string? CalculationHash = null);

public sealed record VoucherFiscalAcquisitionView(
    VoucherFiscalAcquisition Value, int LineNumber, string AccountCode,
    string Description, string TaxName, string DianTaxCode, decimal TaxRate,
    decimal VatAmount);

public sealed record VoucherFiscalPreview(
    IReadOnlyList<VoucherFiscalAcquisitionView> Acquisitions,
    WithholdingCalculationSnapshot Withholding, string CalculationHash,
    IReadOnlyList<string> Diagnostics, bool CanConfirm,
    Guid? PayableSourceLineId = null, Guid? SupplierPartySiteId = null);
