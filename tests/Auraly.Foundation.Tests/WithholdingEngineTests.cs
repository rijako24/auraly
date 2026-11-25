using Auraly.Commerce.Taxation.Domain;
using Auraly.Commerce.Taxation.Application;
using Auraly.Commerce.Taxation.Contracts;

namespace Auraly.Foundation.Tests;

public sealed class WithholdingEngineTests
{
    [Fact]
    public void Existing_adjustment_serialization_does_not_gain_a_null_review_field()
    {
        var adjustment = new WithholdingAdjustmentRequest(Guid.Empty, "Manual", 100m, 1m, "Soporte revisado");
        Assert.DoesNotContain("ReviewBasisHash", System.Text.Json.JsonSerializer.Serialize(adjustment));
        var snapshot = new WithholdingCalculationSnapshot(100m, 0m, 100m, []);
        Assert.DoesNotContain("ReviewBasisHash", System.Text.Json.JsonSerializer.Serialize(snapshot));
    }

    [Fact]
    public void Missing_supplier_profile_requires_an_explicit_audited_document_review()
    {
        var service = new WithholdingService(null!, new WithholdingEngine(), null!);
        var plan = new WithholdingCalculationPlan(BusinessId, [], new Dictionary<Guid, CounterpartyTaxProfileView>(), new HashSet<Guid>());
        var user = Guid.NewGuid();
        var date = new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
        WithholdingPreviewRequest[] bases = [new(BusinessId, "Purchase", "Accrual", CounterpartyId, null, null, 100m, 0m, date)];
        var initial = service.ReviewPurchaseDocument(plan, CounterpartyId, bases, [], user, date);
        Assert.False(initial.CanConfirm);
        var review = service.ReviewPurchaseDocument(plan, CounterpartyId, bases,
            [new(Guid.Empty, "Review", null, null, "Soporte revisado para esta operación", ReviewBasisHash: initial.Calculation.ReviewBasisHash)], user, date);
        Assert.True(review.CanConfirm);
        Assert.False(review.HasProfile);
        Assert.Empty(plan.Profiles);
        Assert.Empty(review.Calculation.Lines);
        Assert.Equal(100m, review.Calculation.NetAmount);
        var audit = Assert.Single(review.Calculation.Adjustments!);
        Assert.Equal("Review", audit.Action);
        Assert.Equal(user, audit.AdjustedByUserId);
        Assert.Null(audit.Amount);
        Assert.Contains(review.Diagnostics, text => text.Contains("revisión", StringComparison.OrdinalIgnoreCase));
        Assert.Throws<TaxationValidationException>(() => service.ReviewPurchaseDocument(plan, CounterpartyId,
            [bases[0] with { TaxExclusiveAmount = 200m }],
            [new(Guid.Empty, "Review", null, null, "Importe anterior", ReviewBasisHash: initial.Calculation.ReviewBasisHash)], user, date));
        Assert.Throws<TaxationValidationException>(() => service.ReviewPurchaseDocument(plan, CounterpartyId, bases,
            [new(Guid.Empty, "Review", null, 1m, "No debe cambiar importes")], user, date));
        Assert.Throws<TaxationValidationException>(() => service.ReviewPurchaseDocument(plan, CounterpartyId, bases,
            [new(Guid.Empty, "Review", null, null, " ")], user, date));
    }

    [Fact]
    public void Document_review_keeps_automatic_withholding_and_rejects_a_changed_rule_or_duplicate_review()
    {
        var service = new WithholdingService(null!, new WithholdingEngine(), null!);
        var date = new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
        var rule = Rule(WithholdingKind.IncomeTax, WithholdingBaseKind.TaxExclusiveAmount, 2.5m);
        var plan = new WithholdingCalculationPlan(BusinessId, [rule], new Dictionary<Guid, CounterpartyTaxProfileView>
            { [CounterpartyId] = new(BusinessId, CounterpartyId, true, [], null, date) }, new HashSet<Guid>());
        var user = Guid.NewGuid();
        WithholdingPreviewRequest[] bases = [new(BusinessId, "Purchase", "Accrual", CounterpartyId, null, null, 100_000m, 19_000m, date)];
        var initial = service.ReviewPurchaseDocument(plan, CounterpartyId, bases, [], user, date);
        var adjustment = new WithholdingAdjustmentRequest(Guid.Empty, "Review", null, null, "Retenciones verificadas",
            ReviewBasisHash: initial.Calculation.ReviewBasisHash);
        var reviewed = service.ReviewPurchaseDocument(plan, CounterpartyId, bases, [adjustment], user, date);
        Assert.Equal(2_500m, reviewed.Calculation.WithholdingTotal);
        Assert.Equal(initial.Calculation.Lines, reviewed.Calculation.Lines);
        Assert.Throws<TaxationValidationException>(() => service.ReviewPurchaseDocument(
            plan with { Rules = [rule with { Rate = 3m }] }, CounterpartyId, bases, [adjustment], user, date));
        Assert.Throws<TaxationValidationException>(() => service.ReviewPurchaseDocument(
            plan, CounterpartyId, bases, [adjustment, adjustment], user, date));
    }

    [Fact]
    public void Manual_only_rule_is_a_template_and_never_runs_in_automatic_calculation()
    {
        var manual = Rule(WithholdingKind.IncomeTax,
            WithholdingBaseKind.TaxExclusiveAmount, 2.5m) with
        { AppliesAutomatically = false, DefaultAccountId = Guid.NewGuid() };
        var context = Context(100_000m, 0m, "11001");
        Assert.Empty(new WithholdingEngine().Calculate(context, [manual]).Lines);
        Assert.Empty(new WithholdingEngine().CalculateDocument([context], [manual]).Calculation.Lines);
    }

    [Fact]
    public void Splitting_expense_accounts_does_not_split_the_minimum_base_or_tax_other_concepts()
    {
        var rule = Rule(WithholdingKind.IncomeTax, WithholdingBaseKind.TaxExclusiveAmount, 2.5m,
            minimum: 100_000m) with { ConceptCode = "SERVICIOS" };
        var engine = new WithholdingEngine();
        var combined = engine.CalculateDocument([Context(120_000m, 0m, "11001") with { ConceptCode = "SERVICIOS" }], [rule]);
        var distributed = engine.CalculateDocument([
            Context(60_000m, 0m, "11001") with { ConceptCode = "SERVICIOS" },
            Context(60_000m, 0m, "11001") with { ConceptCode = "SERVICIOS" },
            Context(80_000m, 0m, "11001") with { ConceptCode = "OTRO" }], [rule]);
        Assert.Equal(3_000m, distributed.Calculation.WithholdingTotal);
        Assert.Equal(combined.Calculation.Lines, distributed.Calculation.Lines);
        Assert.Equal(197_000m, distributed.Calculation.NetAmount);
        var below = engine.CalculateDocument([Context(50_000m, 0m, "11001") with { ConceptCode = "SERVICIOS" }], [rule]);
        Assert.Empty(below.Calculation.Lines);
        Assert.Contains(below.Diagnostics, message => message.Contains("mínimo"));
    }

    private static readonly Guid BusinessId = Guid.NewGuid();
    private static readonly Guid CounterpartyId = Guid.NewGuid();

    [Fact]
    public void Calculates_income_vat_and_ica_on_their_legal_bases()
    {
        var rules = new[]
        {
            Rule(WithholdingKind.IncomeTax, WithholdingBaseKind.TaxExclusiveAmount, 2.5m),
            Rule(WithholdingKind.Vat, WithholdingBaseKind.VatAmount, 15m),
            Rule(WithholdingKind.IndustryCommerce, WithholdingBaseKind.TaxExclusiveAmount, 0.966m, "11001")
        };
        var result = new WithholdingEngine().Calculate(Context(100_000m, 19_000m, "11001"), rules);

        Assert.Equal(119_000m, result.GrossAmount);
        Assert.Equal(6_316m, result.WithholdingTotal);
        Assert.Equal(112_684m, result.NetAmount);
        Assert.Equal(2_850m, result.Lines.Single(line => line.Kind == WithholdingKind.Vat).Amount);
        result.EnsureBalanced();
    }

    [Fact]
    public void Does_not_apply_below_minimum_without_responsibility_or_wrong_jurisdiction()
    {
        var minimum = Rule(WithholdingKind.IncomeTax, WithholdingBaseKind.TaxExclusiveAmount, 2m,
            minimum: 200_000m, responsibilities: ["O-13"]);
        var ica = Rule(WithholdingKind.IndustryCommerce, WithholdingBaseKind.TaxExclusiveAmount, 1m, "05001");

        var result = new WithholdingEngine().Calculate(Context(100_000m, 19_000m, "11001"), [minimum, ica]);

        Assert.Empty(result.Lines);
        Assert.Equal(result.GrossAmount, result.NetAmount);
    }

    [Fact]
    public void Does_not_recognize_the_same_rule_at_payment_after_accrual()
    {
        var rule = Rule(WithholdingKind.IncomeTax, WithholdingBaseKind.TaxExclusiveAmount, 2.5m);
        var context = Context(100_000m, 19_000m, "11001") with
        {
            Moment = WithholdingRecognitionMoment.Payment,
            PreviouslyRecognizedRuleIds = new HashSet<Guid> { rule.RuleId }
        };

        var result = new WithholdingEngine().Calculate(context, [rule]);

        Assert.Empty(result.Lines);
        Assert.Equal(119_000m, result.NetAmount);
    }

    [Fact]
    public void Applies_only_rules_configured_for_the_recognition_moment()
    {
        var paymentRule = Rule(
            WithholdingKind.IncomeTax, WithholdingBaseKind.TaxExclusiveAmount, 2.5m,
            moment: WithholdingRecognitionMoment.Payment);

        Assert.Empty(new WithholdingEngine().Calculate(
            Context(100_000m, 19_000m, "11001"), [paymentRule]).Lines);
        Assert.Single(new WithholdingEngine().Calculate(
            Context(100_000m, 19_000m, "11001") with { Moment = WithholdingRecognitionMoment.Payment },
            [paymentRule]).Lines);
    }

    [Fact]
    public void Multiple_responsibilities_classify_the_party_but_do_not_define_rates()
    {
        var matching = Rule(
            WithholdingKind.IncomeTax, WithholdingBaseKind.TaxExclusiveAmount, 2.5m,
            responsibilities: ["O-13", "O-23"]);
        var missingOne = Context(100_000m, 19_000m, "11001") with
        {
            CounterpartyResponsibilities = new HashSet<string>(["O-13"], StringComparer.OrdinalIgnoreCase)
        };
        var hasAll = missingOne with
        {
            CounterpartyResponsibilities = new HashSet<string>(["O-13", "O-23"], StringComparer.OrdinalIgnoreCase)
        };

        Assert.Empty(new WithholdingEngine().Calculate(missingOne, [matching]).Lines);
        var applied = Assert.Single(new WithholdingEngine().Calculate(hasAll, [matching]).Lines);
        Assert.Equal(2.5m, applied.Rate);
        Assert.Equal(2_500m, applied.Amount);
    }

    [Theory]
    [InlineData(100_000, 0)]
    [InlineData(10_000_000, 200_000)]
    public void Income_tax_is_not_withheld_from_a_self_withholding_supplier(
        decimal subtotal, decimal minimum)
    {
        var rule = Rule(
            WithholdingKind.IncomeTax, WithholdingBaseKind.TaxExclusiveAmount, 2.5m,
            minimum: minimum);
        var context = Context(subtotal, 0m, "11001") with
        {
            CounterpartyResponsibilities = new HashSet<string>(
                [TaxResponsibilityCodes.IncomeTaxSelfWithholder],
                StringComparer.OrdinalIgnoreCase)
        };

        var result = new WithholdingEngine().Calculate(context, [rule]);

        Assert.Empty(result.Lines);
        Assert.Equal(subtotal, result.NetAmount);
        result.EnsureBalanced();
    }

    [Fact]
    public void Self_withholding_supplier_can_still_have_vat_and_ica_withholdings()
    {
        var rules = new[]
        {
            Rule(WithholdingKind.IncomeTax, WithholdingBaseKind.TaxExclusiveAmount, 2.5m),
            Rule(WithholdingKind.Vat, WithholdingBaseKind.VatAmount, 15m),
            Rule(WithholdingKind.IndustryCommerce, WithholdingBaseKind.TaxExclusiveAmount, 1m, "11001")
        };
        var context = Context(100_000m, 19_000m, "11001") with
        {
            CounterpartyResponsibilities = new HashSet<string>(["o-15"])
        };

        var result = new WithholdingEngine().Calculate(context, rules);

        Assert.DoesNotContain(result.Lines, line => line.Kind == WithholdingKind.IncomeTax);
        Assert.Contains(result.Lines, line => line.Kind == WithholdingKind.Vat);
        Assert.Contains(result.Lines, line => line.Kind == WithholdingKind.IndustryCommerce);
        Assert.Equal(3_850m, result.WithholdingTotal);
        result.EnsureBalanced();
    }

    [Fact]
    public void Self_withholder_status_does_not_suppress_income_tax_withheld_by_a_customer()
    {
        var rule = Rule(
            WithholdingKind.IncomeTax, WithholdingBaseKind.TaxExclusiveAmount, 2.5m,
            direction: WithholdingDirection.Sale);
        var context = Context(100_000m, 0m, "11001") with
        {
            Direction = WithholdingDirection.Sale,
            CounterpartyResponsibilities = new HashSet<string>(
                [TaxResponsibilityCodes.IncomeTaxSelfWithholder])
        };

        var result = new WithholdingEngine().Calculate(context, [rule]);

        Assert.Equal(2_500m, Assert.Single(result.Lines).Amount);
    }

    [Fact]
    public void Rejects_reteiva_configured_on_invoice_subtotal()
    {
        Assert.Throws<WithholdingRuleException>(() => Rule(
            WithholdingKind.Vat, WithholdingBaseKind.TaxExclusiveAmount, 15m));
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(500, 12_500)]
    public void Document_review_keeps_a_single_accumulated_base_up_to_five_hundred_lines(int count, decimal expected)
    {
        var rule = Rule(WithholdingKind.IncomeTax, WithholdingBaseKind.TaxExclusiveAmount, 2.5m, minimum: 250_000m);
        var contexts = Enumerable.Repeat(Context(1_000m, 190m, "11001"), count).ToArray();
        var (calculation, _) = new WithholdingEngine().CalculateDocument(contexts, [rule]);
        Assert.Equal(expected, calculation.WithholdingTotal);
        Assert.Equal(count == 500 ? 595_000m : 119_000m, calculation.GrossAmount);
        if (count == 500) Assert.Equal(500_000m, Assert.Single(calculation.Lines).TaxableBase);
        else Assert.Empty(calculation.Lines);
        calculation.EnsureBalanced();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public void Document_review_rejects_an_empty_or_unbounded_capture(int count) =>
        Assert.Throws<WithholdingRuleException>(() => new WithholdingEngine().CalculateDocument(
            Enumerable.Repeat(Context(1m, 0, "11001"), count).ToArray(), []));

    private static WithholdingRule Rule(
        WithholdingKind kind, WithholdingBaseKind basis, decimal rate,
        string? jurisdiction = null, decimal minimum = 0, string[]? responsibilities = null,
        WithholdingRecognitionMoment moment = WithholdingRecognitionMoment.Accrual,
        WithholdingDirection direction = WithholdingDirection.Purchase) =>
        WithholdingRule.Create(Guid.NewGuid(), BusinessId, 1, Guid.NewGuid().ToString("N")[..12],
            kind.ToString(), kind, direction, moment, basis, null, jurisdiction,
            rate, minimum, responsibilities, new DateOnly(2026, 1, 1), null, true);

    private static WithholdingCalculationContext Context(
        decimal subtotal, decimal vat, string jurisdiction) => new(
            BusinessId, WithholdingDirection.Purchase, WithholdingRecognitionMoment.Accrual,
            CounterpartyId, null, jurisdiction, subtotal, vat,
            new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero),
            true,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase), new HashSet<Guid>());
}
