using Auraly.BuildingBlocks.Infrastructure.Identifiers;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Fiscal;

namespace Auraly.Foundation.Tests;

public sealed partial class AccountingVoucherFiscalValidationTests
{
    [Fact]
    public void Support_cancellation_reverses_the_original_journal_and_closes_its_same_payable_without_money()
    {
        var (request, calculation, context, user) = CancellationScenario();
        var proof = AccountingVoucherFinancialValidation.PreviewFiscalCancellation(request, calculation, context);
        Assert.True(proof.CanConfirm, string.Join(" ", proof.Diagnostics));
        Assert.Empty(proof.Acquisitions);
        Assert.Same(calculation.Withholding, proof.Withholding);
        var effects = AccountingVoucherFinancialValidation.Normalize(user, request, context,
            new Uuid7AuralyIdGenerator(TimeProvider.System), proof);
        Assert.Empty(effects.Payments);
        Assert.Empty(effects.CreditApplications);
        Assert.Empty(effects.DirectMovements);
        Assert.Null(effects.Unapplications);
        var closing = Assert.Single(effects.Positions);
        Assert.Equal("Cancellation", closing.EffectKind);
        Assert.Equal(calculation.Cancellation!.PayableId, closing.PositionId);
        Assert.Equal(112_650m, closing.Amount);
        Assert.Equal(proof.PayableSourceLineId, closing.SourceLineId);
        Assert.Null(closing.DueDate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void Support_cancellation_uses_the_original_calendar_day_in_Colombia(int offset)
    {
        var (request, calculation, context, _) = CancellationScenario();
        request = request with { OccurredAt = calculation.Cancellation!.Original.OccurredAt.AddHours(offset).ToUniversalTime() };
        Assert.True(AccountingVoucherFinancialValidation.PreviewFiscalCancellation(request, calculation, context).CanConfirm);
    }

    [Theory]
    [InlineData("Amount")]
    [InlineData("Account")]
    [InlineData("Party")]
    [InlineData("Site")]
    [InlineData("Base")]
    [InlineData("ExtraPosition")]
    [InlineData("Target")]
    [InlineData("Type")]
    [InlineData("Capture")]
    [InlineData("TaxAdjustment")]
    [InlineData("Group")]
    [InlineData("Session")]
    [InlineData("DueDate")]
    [InlineData("Date")]
    [InlineData("Principal")]
    [InlineData("OriginalBusiness")]
    [InlineData("OriginalType")]
    [InlineData("OpeningIssue")]
    [InlineData("MissingAccount")]
    [InlineData("Treasury")]
    public void Support_cancellation_rejects_changes_to_the_original_or_a_different_operation(string fault)
    {
        var (request, calculation, context, _) = CancellationScenario();
        var first = request.Lines[0];
        var last = request.Lines[^1];
        if (fault is "Amount" or "Account" or "Party" or "Base" or "ExtraPosition" or "Group" or "DueDate")
        {
            first = fault switch {
                "Amount" => first with { Credit = first.Credit + 1 },
                "Account" => first with { AccountId = request.Lines[1].AccountId },
                "Party" => first with { PartyId = Guid.NewGuid() },
                "Base" => first with { BaseAmount = 100_000m },
                "ExtraPosition" => first with { ExistingPosition = new("Receivable", Guid.NewGuid()) },
                "Group" => first with { FinancialGroupId = Guid.NewGuid() },
                _ => first with { DueDate = request.OccurredAt }
            };
            request = request with { Lines = request.Lines.Select((line, index) => index == 0 ? first : line).ToArray() };
        }
        else if (fault is "Site" or "Target")
        {
            last = fault == "Site" ? last with { PartySiteId = Guid.NewGuid() }
                : last with { ExistingPosition = new("Payable", Guid.NewGuid()) };
            request = request with { Lines = request.Lines.Select((line, index) => index == request.Lines.Count - 1 ? last : line).ToArray() };
        }
        else if (fault == "Type") request = request with { VoucherTypeCode = "Accounting" };
        else if (fault == "Capture") request = request with { FiscalCapture = null };
        else if (fault == "TaxAdjustment") request = request with { FiscalCapture = request.FiscalCapture! with {
            WithholdingBindings = [new(first.LineId!.Value, Guid.NewGuid())] } };
        else if (fault == "Session") request = request with { WorkSessionId = Guid.NewGuid() };
        else if (fault == "Date") request = request with { OccurredAt = calculation.Cancellation!.Original.OccurredAt.AddDays(-1) };
        else if (fault == "Principal") request = request with { MainPartyId = Guid.NewGuid() };
        else if (fault is "OriginalBusiness" or "OriginalType")
        {
            var source = calculation.Cancellation!;
            calculation = calculation with { Cancellation = source with { Original = fault == "OriginalBusiness"
                ? source.Original with { BusinessId = Guid.NewGuid() } : source.Original with { VoucherTypeCode = "Accounting" } } };
        }
        else if (fault == "OpeningIssue") context = context with { Positions = [context.Positions[0] with { OpeningIssue = "Apertura no conciliada" }] };
        else context = context with { Accounts = fault == "MissingAccount" ? context.Accounts.Skip(1).ToArray()
            : context.Accounts.Select((account, index) => index == 0 ? account with { Function = "Bank" } : account).ToArray() };
        var proof = AccountingVoucherFinancialValidation.PreviewFiscalCancellation(request, calculation, context);
        Assert.False(proof.CanConfirm);
        Assert.NotEmpty(proof.Diagnostics);
    }

    [Fact]
    public void Support_cancellation_preserves_dependency_errors_and_never_normalizes_them_as_a_payment()
    {
        var (request, calculation, context, user) = CancellationScenario();
        calculation = calculation with { CanConfirm = false, Diagnostics = ["Desaplica el abono pendiente antes de anular."] };
        var proof = AccountingVoucherFinancialValidation.PreviewFiscalCancellation(request, calculation, context);
        Assert.False(proof.CanConfirm);
        Assert.Contains("Desaplica el abono pendiente antes de anular.", proof.Diagnostics);
        Assert.Throws<AccountingValidationException>(() => AccountingVoucherFinancialValidation.Normalize(user, request,
            context, new Uuid7AuralyIdGenerator(TimeProvider.System), proof));
    }

    private static (ConfirmManualAccountingVoucherRequest, VoucherFiscalCalculation, VoucherFinancialContext, AccountingUserIdentity) CancellationScenario()
    {
        var (original, calculation, context) = Scenario();
        var user = new AccountingUserIdentity(Guid.NewGuid(), Guid.NewGuid(), original.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend });
        var party = context.Parties.Single();
        var payableId = Guid.NewGuid();
        var openingLine = original.Lines[^1];
        var center = Guid.NewGuid();
        original = original with { Lines = original.Lines.Select((line, index) => line with {
            CostCenterId = center, BaseAmount = index == 0 ? 100_000m : null,
            PartySiteId = null }).ToArray(), FinancialEffects = new(Guid.NewGuid(), [], [new(Guid.NewGuid(),
                openingLine.LineId!.Value, "Open", "Payable", openingLine.LineId.Value, payableId, openingLine.AccountId,
                party.PartyId, party.SupplierId, party.DefaultPartySiteId, calculation.Withholding.NetAmount,
                openingLine.DueDate, original.Reference)], [], []) };
        var seller = new Auraly.Contracts.Sales.PosSaleUblPartyContract("222222222", "0", "13", "2",
            "Proveedor del soporte", "Proveedor del soporte", "R-99-PN", "ZZ", "No aplica",
            new("11001", "Bogotá", "Bogotá D.C.", "11", "CL 1 2 3"));
        var snapshot = new PurchaseSupportFiscalSnapshot(null, Guid.NewGuid(), "DS-1", 2, "https://example.test/qr", seller,
            new("18760000001", new(2026, 1, 1), new(2026, 12, 31), "DS", 1, 10000),
            [new(1, "SERVICIO", "999", "EA", "IVA", "01")],
            OriginalSupportNumber: "DS-1", OriginalSupportCuds: new string('a', 96),
            OriginalSupportIssuedOn: DateOnly.FromDateTime(original.OccurredAt.Date),
            ManualAcquisition: new(user.TenantId, original.BusinessId, original.VoucherId, party.SupplierId!.Value,
                original.OccurredAt, openingLine.DueDate!.Value, "COP",
                [new(1, original.Lines[0].LineId!.Value, "Servicio", 100_000m, 19_000m, 19m)], calculation.Withholding));
        calculation = calculation with { Cancellation = new(original, payableId, party.DefaultPartySiteId, snapshot) };
        context = context with { Positions = [new("Payable", payableId, openingLine.AccountId, party.PartyId,
            party.SupplierId, party.DefaultPartySiteId, "COP", original.OccurredAt, null)] };
        var request = original with { VoucherId = Guid.NewGuid(), VoucherTypeCode = "SupportDocumentCancellation",
            OccurredAt = original.OccurredAt.AddDays(1), Reference = null, FinancialEffects = null,
            FiscalCapture = new([], CalculationHash: calculation.CalculationHash),
            Lines = original.Lines.Select(line => line with { Debit = line.Credit, Credit = line.Debit,
                BaseAmount = line.BaseAmount is { } basis ? -basis : null, DueDate = null,
                PartySiteId = line.LineId == openingLine.LineId ? party.DefaultPartySiteId : line.PartySiteId,
                ExistingPosition = line.LineId == openingLine.LineId ? new("Payable", payableId) : null }).ToArray() };
        return (request, calculation, context, user);
    }
}
