namespace Auraly.Commerce.Taxation.Contracts;

public static class TaxationPermissionCodes
{
    public const string ViewWithholdingRules = "commerce.taxation.withholdings.view";
    public const string ManageWithholdingRules = "commerce.taxation.withholdings.manage";
}

public static class WithholdingKinds
{
    public const string IncomeTax = "IncomeTax";
    public const string Vat = "Vat";
    public const string IndustryCommerce = "IndustryCommerce";
}

public static class WithholdingDirections
{
    public const string Purchase = "Purchase";
    public const string Sale = "Sale";
}
public static class WithholdingRecognitionMoments
{
    public const string Accrual = "Accrual";
    public const string Payment = "Payment";
}


public static class WithholdingBaseKinds
{
    public const string TaxExclusiveAmount = "TaxExclusiveAmount";
    public const string VatAmount = "VatAmount";
}

public sealed record TaxationUserIdentity(
    Guid TenantId, Guid BusinessId, Guid UserId, IReadOnlySet<string> Permissions);

public sealed record WithholdingRuleView(
    Guid RuleId, Guid BusinessId, int Version, string Code, string Name,
    string Kind, string Direction, string Moment, string BaseKind, string? ConceptCode,
    string? JurisdictionCode, decimal Rate, decimal MinimumBase,
    IReadOnlyList<string> RequiredResponsibilities, DateOnly EffectiveFrom,
    DateOnly? EffectiveTo, bool IsActive);

public sealed record SaveWithholdingRuleRequest(
    Guid BusinessId, string Code, string Name, string Kind, string Direction,
    string Moment, string BaseKind, string? ConceptCode, string? JurisdictionCode,
    decimal Rate, decimal MinimumBase, IReadOnlyCollection<string> RequiredResponsibilities,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, bool IsActive);

public sealed record CounterpartyTaxProfileView(
    Guid BusinessId, Guid CounterpartyId,
    IReadOnlyList<string> Responsibilities, string? JurisdictionCode,
    DateTimeOffset UpdatedAt);

public sealed record SaveCounterpartyTaxProfileRequest(
    Guid BusinessId, Guid CounterpartyId,
    IReadOnlyCollection<string> Responsibilities, string? JurisdictionCode);

public sealed record WithholdingPreviewRequest(
    Guid BusinessId, string Direction, string Moment, Guid CounterpartyId,
    string? ConceptCode, string? JurisdictionCode, decimal TaxExclusiveAmount,
    decimal VatAmount, DateTimeOffset OccurredAt,
    IReadOnlyCollection<string>? CounterpartyResponsibilities = null,
    IReadOnlyCollection<Guid>? PreviouslyRecognizedRuleIds = null);

public sealed record WithholdingLineSnapshot(
    Guid RuleId, int RuleVersion, string RuleCode, string Name, string Kind,
    string BaseKind, decimal TaxableBase, decimal Rate, decimal Amount,
    string? JurisdictionCode);

public sealed record WithholdingCalculationSnapshot(
    decimal GrossAmount, decimal WithholdingTotal, decimal NetAmount,
    IReadOnlyList<WithholdingLineSnapshot> Lines);
