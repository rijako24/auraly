using Auraly.Commerce.Taxation.Contracts;
using Auraly.Commerce.Taxation.Domain;

namespace Auraly.Commerce.Taxation.Application;
public interface IWithholdingRuleStore
{
    Task<IReadOnlyList<WithholdingRule>> ListAsync(Guid tenantId, Guid businessId, bool includeInactive, CancellationToken ct);
    Task<WithholdingRule> SaveVersionAsync(Guid tenantId, Guid userId, Guid? ruleId, WithholdingRule proposed, CancellationToken ct);
    Task<CounterpartyTaxProfileView?> GetProfileAsync(
        Guid tenantId, Guid businessId, Guid counterpartyId, CancellationToken ct);
    Task<CounterpartyTaxProfileView> SaveProfileAsync(
        Guid tenantId, Guid userId, SaveCounterpartyTaxProfileRequest request, CancellationToken ct);
}

public sealed class WithholdingService(IWithholdingRuleStore store, WithholdingEngine engine)
{
    public async Task<IReadOnlyList<WithholdingRuleView>> ListAsync(
        TaxationUserIdentity user, bool includeInactive, CancellationToken ct = default)
    {
        Require(user, TaxationPermissionCodes.ViewWithholdingRules);
        return (await store.ListAsync(user.TenantId, user.BusinessId, includeInactive, ct))
            .Select(ToView).ToArray();
    }

    public async Task<WithholdingRuleView> SaveAsync(
        TaxationUserIdentity user, Guid? ruleId, SaveWithholdingRuleRequest request,
        CancellationToken ct = default)
    {
        Require(user, TaxationPermissionCodes.ManageWithholdingRules);
        if (request.BusinessId != user.BusinessId)
            throw new TaxationForbiddenException("The rule belongs to another business.");
        if (!string.Equals(request.Moment, WithholdingRecognitionMoments.Accrual, StringComparison.Ordinal))
            throw new TaxationValidationException(
                "Only accrual withholding is available in the automated purchasing flow.");
        if (!string.Equals(request.Direction, WithholdingDirections.Purchase, StringComparison.Ordinal))
            throw new TaxationValidationException(
                "Only purchase withholding is available in the automated purchasing flow.");
        var proposed = WithholdingRule.Create(
            ruleId ?? Guid.NewGuid(), user.BusinessId, 1, request.Code, request.Name,
            Parse<WithholdingKind>(request.Kind, nameof(request.Kind)),
            Parse<WithholdingDirection>(request.Direction, nameof(request.Direction)),
            Parse<WithholdingRecognitionMoment>(request.Moment, nameof(request.Moment)),
            Parse<WithholdingBaseKind>(request.BaseKind, nameof(request.BaseKind)),
            request.ConceptCode, request.JurisdictionCode, request.Rate, request.MinimumBase,
            request.RequiredResponsibilities, request.EffectiveFrom, request.EffectiveTo, request.IsActive);
        return ToView(await store.SaveVersionAsync(user.TenantId, user.UserId, ruleId, proposed, ct));
    }

    public async Task<CounterpartyTaxProfileView?> GetProfileAsync(
        TaxationUserIdentity user, Guid counterpartyId, CancellationToken ct = default)
    {
        Require(user, TaxationPermissionCodes.ViewWithholdingRules);
        if (counterpartyId == Guid.Empty)
            throw new TaxationValidationException("CounterpartyId is required.");
        return await store.GetProfileAsync(
            user.TenantId, user.BusinessId, counterpartyId, ct);
    }

    public async Task<CounterpartyTaxProfileView> SaveProfileAsync(
        TaxationUserIdentity user, SaveCounterpartyTaxProfileRequest request,
        CancellationToken ct = default)
    {
        Require(user, TaxationPermissionCodes.ManageWithholdingRules);
        if (request.BusinessId != user.BusinessId || request.CounterpartyId == Guid.Empty)
            throw new TaxationForbiddenException("The tax profile belongs to another business.");
        if (request.Responsibilities is null)
            throw new TaxationValidationException("Responsibilities are required.");
        if (request.Responsibilities.Count > 20)
            throw new TaxationValidationException("At most 20 tax responsibilities are supported.");
        if (request.Responsibilities.Any(value => value?.Trim().Length > 32))
            throw new TaxationValidationException("A tax responsibility cannot exceed 32 characters.");
        var normalized = request with
        {
            Responsibilities = request.Responsibilities
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(Normalize)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            JurisdictionCode = string.IsNullOrWhiteSpace(request.JurisdictionCode)
                ? null : request.JurisdictionCode.Trim().ToUpperInvariant()
        };
        if (normalized.JurisdictionCode?.Length > 16)
            throw new TaxationValidationException(
                "JurisdictionCode cannot exceed 16 characters.");
        return await store.SaveProfileAsync(user.TenantId, user.UserId, normalized, ct);
    }

    public async Task<WithholdingCalculationSnapshot> PreviewAsync(
        TaxationUserIdentity user, WithholdingPreviewRequest request, CancellationToken ct = default)
    {
        Require(user, TaxationPermissionCodes.ViewWithholdingRules);
        return await CalculateAsync(user.TenantId, user.BusinessId, request, ct);
    }

    public async Task<WithholdingCalculationSnapshot> CalculateAsync(
        Guid tenantId, Guid businessId, WithholdingPreviewRequest request, CancellationToken ct = default)
    {
        if (request.BusinessId != businessId)
            throw new TaxationForbiddenException("The calculation belongs to another business.");
        var profile = request.CounterpartyId == Guid.Empty
            ? null
            : await store.GetProfileAsync(tenantId, businessId, request.CounterpartyId, ct);
        var responsibilities = request.CounterpartyResponsibilities is { Count: > 0 }
            ? new HashSet<string>(request.CounterpartyResponsibilities
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(Normalize), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(profile?.Responsibilities ?? [], StringComparer.OrdinalIgnoreCase);
        var jurisdictionCode = string.IsNullOrWhiteSpace(request.JurisdictionCode)
            ? profile?.JurisdictionCode
            : request.JurisdictionCode.Trim().ToUpperInvariant();
        var context = new WithholdingCalculationContext(
            businessId, Parse<WithholdingDirection>(request.Direction, nameof(request.Direction)),
            Parse<WithholdingRecognitionMoment>(request.Moment, nameof(request.Moment)),
            request.CounterpartyId, request.ConceptCode, jurisdictionCode,
            request.TaxExclusiveAmount, request.VatAmount, request.OccurredAt,
            profile?.AppliesWithholding ?? false, responsibilities,
            new HashSet<Guid>(request.PreviouslyRecognizedRuleIds ?? []));
        var rules = await store.ListAsync(tenantId, businessId, false, ct);
        return ToSnapshot(engine.Calculate(context, rules));
    }

    private static WithholdingRuleView ToView(WithholdingRule rule) => new(
        rule.RuleId, rule.BusinessId, rule.Version, rule.Code, rule.Name, rule.Kind.ToString(),
        rule.Direction.ToString(), rule.Moment.ToString(), rule.BaseKind.ToString(),
        rule.ConceptCode, rule.JurisdictionCode,
        rule.Rate, rule.MinimumBase, rule.RequiredResponsibilities.Order().ToArray(),
        rule.EffectiveFrom, rule.EffectiveTo, rule.IsActive);

    private static WithholdingCalculationSnapshot ToSnapshot(WithholdingCalculation result) => new(
        result.GrossAmount, result.WithholdingTotal, result.NetAmount, result.Lines.Select(line =>
            new WithholdingLineSnapshot(line.RuleId, line.RuleVersion, line.RuleCode, line.Name,
                line.Kind.ToString(), line.BaseKind.ToString(), line.TaxableBase, line.Rate,
                line.Amount, line.JurisdictionCode)).ToArray());

    private static T Parse<T>(string value, string field) where T : struct, Enum =>
        Enum.TryParse<T>(value, false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed : throw new TaxationValidationException($"{field} has an unsupported value.");
    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
    private static void Require(TaxationUserIdentity user, string permission)
    {
        if (!user.Permissions.Contains(permission))
            throw new TaxationForbiddenException($"Permission '{permission}' is required.");
    }
}

public sealed class TaxationValidationException(string message) : Exception(message);
public sealed class TaxationForbiddenException(string message) : Exception(message);
public sealed class TaxationConflictException(string message) : Exception(message);
