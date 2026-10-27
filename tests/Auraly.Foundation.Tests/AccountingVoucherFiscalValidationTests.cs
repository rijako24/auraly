using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Taxation.Contracts;

namespace Auraly.Foundation.Tests;

public sealed partial class AccountingVoucherFiscalValidationTests
{
    [Fact]
    public void Fiscal_proof_covers_the_same_acquisition_vat_retention_and_single_payable()
    {
        var (request, calculation, context) = Scenario();
        var preview = AccountingVoucherFinancialValidation.ValidateFiscal(request, calculation, context);
        Assert.True(preview.CanConfirm);
        Assert.Equal("5195", Assert.Single(preview.Acquisitions).AccountCode);
        Assert.Equal(19_000m, preview.Acquisitions[0].VatAmount);
        Assert.Same(calculation.Withholding, preview.Withholding);
        Assert.Equal(112_650m, preview.Withholding.NetAmount);
    }

    [Theory]
    [InlineData("Acquisition")]
    [InlineData("Vat")]
    [InlineData("Payable")]
    [InlineData("Retention")]
    [InlineData("Extra")]
    [InlineData("Treasury")]
    [InlineData("Principal")]
    [InlineData("FiscalTotal")]
    public void Fiscal_proof_rejects_a_journal_or_snapshot_that_no_longer_explains_the_same_acquisition(string invalid)
    {
        var (request, calculation, context) = Scenario();
        if (invalid is "Acquisition" or "Vat" or "Payable" or "Retention")
        {
            var index = invalid switch { "Acquisition" => 0, "Vat" => 1, "Retention" => 2, _ => 3 };
            request = request with { Lines = request.Lines.Select((line, current) => current != index ? line :
                line.Debit > 0 ? line with { Debit = line.Debit + 1 } : line with { Credit = line.Credit + 1 }).ToArray() };
        }
        else if (invalid == "Extra")
            request = request with { Lines = request.Lines.Append(request.Lines[0] with { LineId = Guid.NewGuid() }).ToArray() };
        else if (invalid == "Treasury")
            context = context with { Accounts = context.Accounts.Select((account, index) => index == 0
                ? account with { Function = "Bank", AccountType = "Asset" } : account).ToArray() };
        else if (invalid == "Principal")
            request = request with { MainPartyId = Guid.NewGuid() };
        else
            calculation = calculation with { Withholding = calculation.Withholding with { GrossAmount = 1m } };
        Assert.Throws<AccountingValidationException>(() =>
            AccountingVoucherFinancialValidation.ValidateFiscal(request, calculation, context));
    }

    [Fact]
    public void Preview_exposes_calculated_retentions_while_the_payable_is_still_being_completed()
    {
        var (request, calculation, context) = Scenario();
        request = request with { Lines = request.Lines.Select((line, index) => index == 3
            ? line with { Credit = 119_000m } : line).ToArray() };
        var preview = AccountingVoucherFinancialValidation.PreviewFiscalAcquisition(request, calculation, context);
        Assert.False(preview.CanConfirm);
        Assert.Same(calculation.Withholding, preview.Withholding);
        Assert.Equal(6_350m, preview.Withholding.WithholdingTotal);
        Assert.NotEmpty(preview.Diagnostics);
        Assert.Null(preview.PayableSourceLineId);
    }

    private static (ConfirmManualAccountingVoucherRequest, VoucherFiscalCalculation, VoucherFinancialContext) Scenario()
    {
        var principal = Guid.NewGuid(); var supplier = Guid.NewGuid(); var site = Guid.NewGuid();
        var accountIds = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var lineIds = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var rules = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        var at = new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.FromHours(-5));
        var basis = new VoucherFiscalAcquisition(lineIds[0], 100_000m, Guid.NewGuid(), "DeductibleInputVat", lineIds[1]);
        var capture = new VoucherFiscalCapture([basis], rules.Select(id => new VoucherFiscalWithholdingBinding(lineIds[2], id)).ToArray());
        var request = new ConfirmManualAccountingVoucherRequest(Guid.NewGuid(), Guid.NewGuid(), at,
            "MANUAL_VOUCHER", "Adquisición", [
                new(accountIds[0], principal, null, "Servicio", 100_000m, 0, lineIds[0]),
                new(accountIds[1], principal, null, "IVA descontable", 19_000m, 0, lineIds[1]),
                new(accountIds[2], principal, null, "Retenciones", 0, 6_350m, lineIds[2]),
                new(accountIds[3], principal, null, "Obligación", 0, 112_650m, lineIds[3], DueDate: at.AddDays(30), PartySiteId: site)
            ], principal, 4, "DS-1", "SupportDocument", CurrencyCode: "COP", FiscalCapture: capture);
        var withholding = new WithholdingCalculationSnapshot(119_000m, 6_350m, 112_650m, [
            new(rules[0], 1, "RF", "Retefuente", "IncomeTax", "TaxExclusiveAmount", 100_000m, 2.5m, 2_500m, null, accountIds[2]),
            new(rules[1], 1, "RV", "ReteIVA", "Vat", "VatAmount", 19_000m, 15m, 2_850m, null, accountIds[2]),
            new(rules[2], 1, "RI", "ReteICA", "IndustryCommerce", "TaxExclusiveAmount", 100_000m, 1m, 1_000m, "11001", accountIds[2])]);
        var calculation = new VoucherFiscalCalculation(supplier, [new(basis, "IVA", "01", 19m, 19_000m)],
            withholding, new string('A', 64), [], true);
        var context = new VoucherFinancialContext([
            new(accountIds[0], "General", null, null, "Expense", "5195"),
            new(accountIds[1], "General", null, null, "Asset", "1355"),
            new(accountIds[2], "General", null, null, "Liability", "2365"),
            new(accountIds[3], "Payable", null, null, "Liability", "2205")
        ], [], [new(principal, supplier, site, null, null)]);
        return (request, calculation, context);
    }
}
