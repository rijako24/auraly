namespace Auraly.Commerce.Taxation.Domain;

public enum WithholdingKind { IncomeTax, Vat, IndustryCommerce }
public enum WithholdingDirection { Purchase, Sale }
public enum WithholdingBaseKind { TaxExclusiveAmount, VatAmount }
public enum WithholdingRecognitionMoment { Accrual, Payment }

public static class TaxResponsibilityCodes
{
    public const string IncomeTaxSelfWithholder = "O-15";
}

public sealed record WithholdingRule(
    Guid RuleId,
    Guid BusinessId,
    int Version,
    string Code,
    string Name,
    WithholdingKind Kind,
    WithholdingDirection Direction,
    WithholdingRecognitionMoment Moment,
    WithholdingBaseKind BaseKind,
    string? ConceptCode,
    string? JurisdictionCode,
    decimal Rate,
    decimal MinimumBase,
    IReadOnlySet<string> RequiredResponsibilities,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive,
    bool AppliesAutomatically = true,
    Guid? DefaultAccountId = null)
{
    public static WithholdingRule Create(
        Guid ruleId, Guid businessId, int version, string code, string name,
        WithholdingKind kind, WithholdingDirection direction,
        WithholdingRecognitionMoment moment, WithholdingBaseKind baseKind,
        string? conceptCode, string? jurisdictionCode, decimal rate, decimal minimumBase,
        IEnumerable<string>? requiredResponsibilities, DateOnly effectiveFrom,
        DateOnly? effectiveTo, bool isActive, bool appliesAutomatically = true,
        Guid? defaultAccountId = null)
    {
        if (ruleId == Guid.Empty || businessId == Guid.Empty)
            throw new WithholdingRuleException("RuleId and BusinessId are required.");
        if (version < 1) throw new WithholdingRuleException("Version must be positive.");
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 32)
            throw new WithholdingRuleException("Code is required and cannot exceed 32 characters.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
            throw new WithholdingRuleException("Name is required and cannot exceed 120 characters.");
        if (rate is <= 0 or > 100)
            throw new WithholdingRuleException("Rate must be greater than zero and at most 100.");
        if (minimumBase < 0) throw new WithholdingRuleException("MinimumBase cannot be negative.");
        if (effectiveTo < effectiveFrom)
            throw new WithholdingRuleException("EffectiveTo cannot precede EffectiveFrom.");
        if (kind == WithholdingKind.Vat && baseKind != WithholdingBaseKind.VatAmount)
            throw new WithholdingRuleException("ReteIVA must use the VAT amount as its taxable base.");
        if (kind != WithholdingKind.Vat && baseKind == WithholdingBaseKind.VatAmount)
            throw new WithholdingRuleException("Only ReteIVA can use the VAT amount as its taxable base.");
        var normalizedConcept = Normalize(conceptCode);
        var normalizedJurisdiction = Normalize(jurisdictionCode);
        if (normalizedConcept?.Length > 32)
            throw new WithholdingRuleException("ConceptCode cannot exceed 32 characters.");
        if (normalizedJurisdiction?.Length > 16)
            throw new WithholdingRuleException("JurisdictionCode cannot exceed 16 characters.");
        var responsibilities = (requiredResponsibilities ?? [])
            .Select(Normalize).Where(value => value is not null).Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (responsibilities.Count > 20 || responsibilities.Any(value => value.Length > 32))
            throw new WithholdingRuleException("Tax responsibilities must contain at most 20 values of 32 characters.");
        if (kind == WithholdingKind.IndustryCommerce && string.IsNullOrWhiteSpace(jurisdictionCode))
            throw new WithholdingRuleException("ReteICA requires a jurisdiction.");
        if (!appliesAutomatically && defaultAccountId is null)
            throw new WithholdingRuleException("A manual withholding rule requires an accounting account.");
        if (appliesAutomatically && defaultAccountId is not null)
            throw new WithholdingRuleException("An automatic withholding rule uses its configured accounting mapping.");

        return new WithholdingRule(
            ruleId, businessId, version, code.Trim().ToUpperInvariant(), name.Trim(), kind, direction,
            moment, baseKind, normalizedConcept, normalizedJurisdiction, rate, minimumBase,
            responsibilities, effectiveFrom, effectiveTo, isActive, appliesAutomatically, defaultAccountId);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}

public sealed record WithholdingCalculationContext(
    Guid BusinessId,
    WithholdingDirection Direction,
    WithholdingRecognitionMoment Moment,
    Guid CounterpartyId,
    string? ConceptCode,
    string? JurisdictionCode,
    decimal TaxExclusiveAmount,
    decimal VatAmount,
    DateTimeOffset OccurredAt,
    bool AppliesWithholding,
    IReadOnlySet<string> CounterpartyResponsibilities,
    IReadOnlySet<Guid> PreviouslyRecognizedRuleIds);

public sealed record WithholdingCalculationLine(
    Guid RuleId, int RuleVersion, string RuleCode, string Name, WithholdingKind Kind,
    WithholdingBaseKind BaseKind, decimal TaxableBase, decimal Rate, decimal Amount,
    string? JurisdictionCode);

public sealed record WithholdingCalculation(
    decimal GrossAmount, decimal WithholdingTotal, decimal NetAmount,
    IReadOnlyList<WithholdingCalculationLine> Lines)
{
    public void EnsureBalanced()
    {
        if (GrossAmount - WithholdingTotal != NetAmount ||
            Lines.Sum(line => line.Amount) != WithholdingTotal)
            throw new InvalidOperationException("The withholding calculation does not reconcile.");
    }
}

public sealed class WithholdingEngine
{
    // A document threshold is evaluated once per rule over all matching bases,
    // independently of the number of accounts or cost centers used to distribute it.
    public (WithholdingCalculation Calculation, IReadOnlyList<string> Diagnostics) CalculateDocument(
        IReadOnlyList<WithholdingCalculationContext> contexts, IEnumerable<WithholdingRule> candidateRules)
    {
        if (contexts.Count is < 1 or > 100)
            throw new WithholdingRuleException("A document requires between 1 and 100 bases.");
        var first = contexts[0];
        if (contexts.Any(c => c.BusinessId != first.BusinessId || c.CounterpartyId != first.CounterpartyId ||
            c.Direction != first.Direction || c.Moment != first.Moment || c.OccurredAt != first.OccurredAt ||
            c.BusinessId == Guid.Empty || c.CounterpartyId == Guid.Empty ||
            c.TaxExclusiveAmount < 0 || c.VatAmount < 0))
            throw new WithholdingRuleException("The document tax bases are inconsistent.");
        var lines = new List<WithholdingCalculationLine>();
        var diagnostics = new List<string>();
        var date = DateOnly.FromDateTime(first.OccurredAt.UtcDateTime);
        foreach (var rule in candidateRules.OrderBy(r => r.Kind).ThenBy(r => r.Code, StringComparer.Ordinal))
        {
            var matching = contexts.Where(c => c.AppliesWithholding && Applies(rule, c, date)).ToArray();
            if (matching.Length == 0) continue;
            var basis = first with { TaxExclusiveAmount = matching.Sum(c => c.TaxExclusiveAmount),
                VatAmount = matching.Sum(c => c.VatAmount) };
            var line = CalculateLine(rule, basis);
            if (line.Amount > 0) lines.Add(line);
            else diagnostics.Add($"{rule.Name}: base {line.TaxableBase:0.####}; mínimo {rule.MinimumBase:0.####}. No genera retención.");
        }
        if (contexts.All(c => !c.AppliesWithholding))
            diagnostics.Add("El perfil tributario del proveedor indica que no aplica retención.");
        else if (lines.Count == 0 && diagnostics.Count == 0)
            diagnostics.Add("Ninguna regla vigente coincide con el concepto, jurisdicción y responsabilidades del proveedor. Revisa la configuración tributaria.");
        var gross = Money(contexts.Sum(c => c.TaxExclusiveAmount + c.VatAmount));
        var held = Money(lines.Sum(l => l.Amount));
        if (held > gross) throw new WithholdingRuleException("Withholdings cannot exceed the document gross amount.");
        var result = new WithholdingCalculation(gross, held, Money(gross - held), lines);
        result.EnsureBalanced();
        return (result, diagnostics);
    }

    public WithholdingCalculation Calculate(
        WithholdingCalculationContext context, IEnumerable<WithholdingRule> candidateRules)
    {
        if (context.BusinessId == Guid.Empty || context.CounterpartyId == Guid.Empty)
            throw new WithholdingRuleException("Business and counterparty are required.");
        if (context.TaxExclusiveAmount < 0 || context.VatAmount < 0)
            throw new WithholdingRuleException("Tax bases cannot be negative.");

        if (!context.AppliesWithholding)
            return new WithholdingCalculation(
                Money(context.TaxExclusiveAmount + context.VatAmount), 0,
                Money(context.TaxExclusiveAmount + context.VatAmount), []);

        var date = DateOnly.FromDateTime(context.OccurredAt.UtcDateTime);
        var lines = candidateRules
            .Where(rule => Applies(rule, context, date))
            .OrderBy(rule => rule.Kind).ThenBy(rule => rule.Code, StringComparer.Ordinal)
            .Select(rule => CalculateLine(rule, context))
            .Where(line => line.Amount > 0)
            .ToArray();
        var gross = Money(context.TaxExclusiveAmount + context.VatAmount);
        var withheld = Money(lines.Sum(line => line.Amount));
        if (withheld > gross)
            throw new WithholdingRuleException("Withholdings cannot exceed the document gross amount.");
        var result = new WithholdingCalculation(gross, withheld, Money(gross - withheld), lines);
        result.EnsureBalanced();
        return result;
    }

    private static bool Applies(WithholdingRule rule, WithholdingCalculationContext context, DateOnly date)
    {
        if (!rule.IsActive || !rule.AppliesAutomatically || rule.BusinessId != context.BusinessId ||
            rule.Direction != context.Direction || rule.Moment != context.Moment)
            return false;
        if (rule.Kind == WithholdingKind.IncomeTax &&
            context.Direction == WithholdingDirection.Purchase &&
            HasResponsibility(
                context.CounterpartyResponsibilities,
                TaxResponsibilityCodes.IncomeTaxSelfWithholder))
            return false;
        if (date < rule.EffectiveFrom || rule.EffectiveTo is not null && date > rule.EffectiveTo)
            return false;
        if (context.PreviouslyRecognizedRuleIds.Contains(rule.RuleId)) return false;
        if (rule.ConceptCode is not null && !Same(rule.ConceptCode, context.ConceptCode)) return false;
        if (rule.JurisdictionCode is not null && !Same(rule.JurisdictionCode, context.JurisdictionCode)) return false;
        return rule.RequiredResponsibilities.All(context.CounterpartyResponsibilities.Contains);
    }

    private static WithholdingCalculationLine CalculateLine(
        WithholdingRule rule, WithholdingCalculationContext context)
    {
        var basis = Money(rule.BaseKind == WithholdingBaseKind.VatAmount
            ? context.VatAmount : context.TaxExclusiveAmount);
        var amount = basis < rule.MinimumBase ? 0 : Money(basis * rule.Rate / 100m);
        return new(rule.RuleId, rule.Version, rule.Code, rule.Name, rule.Kind, rule.BaseKind,
            basis, rule.Rate, amount, rule.JurisdictionCode);
    }

    private static bool Same(string left, string? right) =>
        string.Equals(left, right?.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool HasResponsibility(IEnumerable<string> responsibilities, string code) =>
        responsibilities.Any(value => Same(code, value));
    private static decimal Money(decimal value) =>
        decimal.Round(value, 4, MidpointRounding.AwayFromZero);
}

public sealed class WithholdingRuleException(string message) : Exception(message);
