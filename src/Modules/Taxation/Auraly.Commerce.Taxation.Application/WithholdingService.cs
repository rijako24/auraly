using Auraly.Commerce.Taxation.Contracts;
using Auraly.Commerce.Taxation.Domain;
using Auraly.BuildingBlocks.Application.Synchronization;

namespace Auraly.Commerce.Taxation.Application;
public interface IWithholdingRuleStore
{
    Task<AppliedWithholdingReportView> ListAppliedAsync(
        Guid tenantId, Guid businessId, DateOnly from, DateOnly to,
        int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyList<WithholdingRule>> ListAsync(Guid tenantId, Guid businessId, bool includeInactive, CancellationToken ct);
    Task<WithholdingRule> SaveVersionAsync(Guid tenantId, Guid userId, Guid? ruleId, WithholdingRule proposed, CancellationToken ct);
    Task<CounterpartyTaxProfileView?> GetProfileAsync(
        Guid tenantId, Guid businessId, Guid counterpartyId, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, CounterpartyTaxProfileView>> GetProfilesAsync(
        Guid tenantId, Guid businessId, IReadOnlyCollection<Guid> counterpartyIds,
        CancellationToken ct);
    Task<CounterpartyTaxProfileView> SaveProfileAsync(
        Guid tenantId, Guid userId, SaveCounterpartyTaxProfileRequest request, CancellationToken ct);
    Task<IReadOnlySet<string>> GetActiveResponsibilityCodesAsync(CancellationToken ct);
    Task<IReadOnlySet<Guid>> GetValidManualAccountIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> accountIds, CancellationToken ct);
}

public sealed record WithholdingCalculationPlan(
    Guid BusinessId,
    IReadOnlyList<WithholdingRule> Rules,
    IReadOnlyDictionary<Guid, CounterpartyTaxProfileView> Profiles,
    IReadOnlySet<Guid> ValidManualAccountIds);

public sealed class WithholdingService(
    IWithholdingRuleStore store,
    WithholdingEngine engine,
    IPosSynchronizationOutboxDispatcher synchronization)
{
    public Task<AppliedWithholdingReportView> ListAppliedAsync(
        TaxationUserIdentity user, DateOnly from, DateOnly to,
        int page, int pageSize, CancellationToken ct = default)
    {
        Require(user, TaxationPermissionCodes.ViewWithholdingRules);
        if (to < from || to.DayNumber - from.DayNumber > 365 || to == DateOnly.MaxValue ||
            page is < 1 or > 100000 || pageSize is < 1 or > 100)
            throw new TaxationValidationException("Indica un rango de hasta un año, página y tamaño válidos.");
        return store.ListAppliedAsync(user.TenantId, user.BusinessId, from, to, page, pageSize, ct);
    }

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
                "Only accrual withholding is available in the automated document flows.");
        if (request.RequiredResponsibilities is null)
            throw new TaxationValidationException("RequiredResponsibilities is required.");
        var responsibilities = await ValidateResponsibilitiesAsync(request.RequiredResponsibilities, ct);
        var proposed = WithholdingRule.Create(
            ruleId ?? Guid.NewGuid(), user.BusinessId, 1, request.Code, request.Name,
            Parse<WithholdingKind>(request.Kind, nameof(request.Kind)),
            Parse<WithholdingDirection>(request.Direction, nameof(request.Direction)),
            Parse<WithholdingRecognitionMoment>(request.Moment, nameof(request.Moment)),
            Parse<WithholdingBaseKind>(request.BaseKind, nameof(request.BaseKind)),
            request.ConceptCode, request.JurisdictionCode, request.Rate, request.MinimumBase,
            responsibilities, request.EffectiveFrom, request.EffectiveTo, request.IsActive,
            request.AppliesAutomatically, request.DefaultAccountId);
        var saved = ToView(await store.SaveVersionAsync(
            user.TenantId, user.UserId, ruleId, proposed, ct));
        if (saved.AppliesAutomatically)
            await synchronization.DispatchPendingAsync(
                user.TenantId, user.BusinessId, CancellationToken.None);
        return saved;
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
            Responsibilities = await ValidateResponsibilitiesAsync(request.Responsibilities, ct),
            JurisdictionCode = string.IsNullOrWhiteSpace(request.JurisdictionCode)
                ? null : request.JurisdictionCode.Trim().ToUpperInvariant()
        };
        if (normalized.JurisdictionCode?.Length > 16)
            throw new TaxationValidationException(
                "JurisdictionCode cannot exceed 16 characters.");
        var saved = await store.SaveProfileAsync(
            user.TenantId, user.UserId, normalized, ct);
        await synchronization.DispatchTenantPendingAsync(
            user.TenantId, CancellationToken.None);
        return saved;
    }

    private async Task<IReadOnlyCollection<string>> ValidateResponsibilitiesAsync(
        IEnumerable<string> values, CancellationToken ct)
    {
        var normalized = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var active = await store.GetActiveResponsibilityCodesAsync(ct);
        var invalid = normalized.Where(value => !active.Contains(value)).ToArray();
        if (invalid.Length > 0)
            throw new TaxationValidationException(
                $"Las responsabilidades tributarias no existen o están inactivas: {string.Join(", ", invalid)}.");
        return normalized;
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
        var plan = await PrepareCalculationPlanAsync(
            tenantId,
            businessId,
            request.CounterpartyId == Guid.Empty ? [] : [request.CounterpartyId],
            ct);
        return Calculate(plan, request);
    }

    public async Task<WithholdingCalculationPlan> PrepareCalculationPlanAsync(
        Guid tenantId,
        Guid businessId,
        IReadOnlyCollection<Guid> counterpartyIds,
        CancellationToken ct = default,
        IReadOnlyCollection<Guid>? manualAccountIds = null)
    {
        var ids = counterpartyIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        var accounts = (manualAccountIds ?? []).Where(id => id != Guid.Empty).Distinct().ToArray();
        var profilesTask = store.GetProfilesAsync(tenantId, businessId, ids, ct);
        var rulesTask = store.ListAsync(tenantId, businessId, false, ct);
        var accountsTask = accounts.Length == 0
            ? Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>())
            : store.GetValidManualAccountIdsAsync(tenantId, accounts, ct);
        await Task.WhenAll(profilesTask, rulesTask, accountsTask);
        return new WithholdingCalculationPlan(
            businessId,
            await rulesTask,
            await profilesTask,
            await accountsTask);
    }

    public WithholdingCalculationSnapshot Calculate(
        WithholdingCalculationPlan plan,
        WithholdingPreviewRequest request)
        => ToSnapshot(engine.Calculate(CreateContext(plan, request), plan.Rules));

    public (WithholdingCalculationSnapshot Calculation, IReadOnlyList<string> Diagnostics) CalculateDocument(
        WithholdingCalculationPlan plan, IReadOnlyList<WithholdingPreviewRequest> bases)
    {
        var result = engine.CalculateDocument(bases.Select(basis => CreateContext(plan, basis)).ToArray(), plan.Rules);
        return (ToSnapshot(result.Calculation), result.Diagnostics);
    }

    public WithholdingCalculationSnapshot ApplyAdjustments(
        WithholdingCalculationPlan plan, WithholdingCalculationSnapshot automatic,
        IReadOnlyList<WithholdingAdjustmentRequest>? adjustments,
        Guid userId, DateTimeOffset occurredAt, decimal vatAmount)
    {
        if (adjustments is not { Count: > 0 }) return automatic;
        if (adjustments.Count > 20 || userId == Guid.Empty ||
            adjustments.Where(item => item.Action != "Manual").Select(item => item.RuleId).Distinct().Count() !=
                adjustments.Count(item => item.Action != "Manual") ||
            adjustments.Where(item => item.Action == "Manual").Select(item => item.ManualLineId).Distinct().Count() !=
                adjustments.Count(item => item.Action == "Manual"))
            throw new TaxationValidationException("Las retenciones manuales son inválidas o están duplicadas.");

        var date = DateOnly.FromDateTime(occurredAt.UtcDateTime);
        var lines = automatic.Lines.ToDictionary(line => line.RuleId);
        var manualLines = new List<WithholdingLineSnapshot>();
        var audit = new List<WithholdingAdjustmentSnapshot>(adjustments.Count);
        foreach (var adjustment in adjustments)
        {
            var reason = adjustment.Reason?.Trim();
            if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
                throw new TaxationValidationException("Indica el motivo del ajuste (máximo 500 caracteres).");
            if (adjustment.Action == "Manual")
            {
                if (adjustment.RuleId != Guid.Empty || adjustment.ManualLineId is not { } manualId ||
                    manualId == Guid.Empty || adjustment.AccountId is not { } accountId ||
                    !plan.ValidManualAccountIds.Contains(accountId) ||
                    !Enum.TryParse<WithholdingKind>(adjustment.Kind, false, out var kind) ||
                    !Enum.IsDefined(kind) || adjustment.Name is null ||
                    adjustment.Name.Trim().Length is < 1 or > 120 ||
                    adjustment.Rate is not > 0 or > 100 ||
                    decimal.Round(adjustment.Rate.Value, 6) != adjustment.Rate ||
                    adjustment.TaxableBase is not > 0 || adjustment.Amount is not > 0 ||
                    decimal.Round(adjustment.TaxableBase.Value, 4) != adjustment.TaxableBase ||
                    decimal.Round(adjustment.Amount.Value, 4) != adjustment.Amount ||
                    adjustment.TaxableBase > (kind == WithholdingKind.Vat
                        ? vatAmount : automatic.GrossAmount - vatAmount) ||
                    adjustment.Amount > adjustment.TaxableBase ||
                    (kind == WithholdingKind.IndustryCommerce &&
                        string.IsNullOrWhiteSpace(adjustment.JurisdictionCode)) ||
                    (kind != WithholdingKind.IndustryCommerce &&
                        !string.IsNullOrWhiteSpace(adjustment.JurisdictionCode)) ||
                    adjustment.JurisdictionCode?.Trim().Length > 16)
                    throw new TaxationValidationException("Completa la retención manual con base, tasa, valor y una cuenta de pasivo activa.");
                var jurisdiction = adjustment.JurisdictionCode?.Trim().ToUpperInvariant();
                if (lines.Values.Concat(manualLines).Any(line =>
                    line.Kind == kind.ToString() && line.TaxableBase == adjustment.TaxableBase &&
                    string.Equals(line.JurisdictionCode, jurisdiction, StringComparison.OrdinalIgnoreCase)))
                    throw new TaxationValidationException("Ya existe una retención de este tipo y base. Ajusta o excluye la existente para evitar duplicarla.");
                manualLines.Add(new WithholdingLineSnapshot(Guid.Empty, 0, "MANUAL",
                    adjustment.Name.Trim(), kind.ToString(),
                    kind == WithholdingKind.Vat ? WithholdingBaseKinds.VatAmount : WithholdingBaseKinds.TaxExclusiveAmount,
                    adjustment.TaxableBase.Value, adjustment.Rate.Value, adjustment.Amount.Value,
                    jurisdiction, accountId, manualId));
                audit.Add(new WithholdingAdjustmentSnapshot(Guid.Empty, 0, "Manual", null, null,
                    adjustment.TaxableBase, adjustment.Amount, reason, userId, manualId, accountId));
                continue;
            }
            var rule = plan.Rules.SingleOrDefault(candidate => candidate.RuleId == adjustment.RuleId);
            if (rule is null || !rule.IsActive || !rule.AppliesAutomatically || rule.BusinessId != plan.BusinessId ||
                rule.Direction != WithholdingDirection.Purchase ||
                rule.Moment != WithholdingRecognitionMoment.Accrual ||
                date < rule.EffectiveFrom || rule.EffectiveTo is not null && date > rule.EffectiveTo)
                throw new TaxationValidationException("La regla manual no está vigente para esta compra.");
            var exists = lines.TryGetValue(rule.RuleId, out var original);
            if (adjustment.Action == "Exclude")
            {
                if (!exists || adjustment.TaxableBase is not null || adjustment.Amount is not null)
                    throw new TaxationValidationException("Solo se puede excluir una retención calculada.");
                lines.Remove(rule.RuleId);
            }
            else if (adjustment.Action is "Add" or "Override")
            {
                if (exists != (adjustment.Action == "Override") ||
                    adjustment.TaxableBase is not > 0 || adjustment.Amount is not > 0 ||
                    decimal.Round(adjustment.TaxableBase.Value, 4) != adjustment.TaxableBase ||
                    decimal.Round(adjustment.Amount.Value, 4) != adjustment.Amount ||
                    adjustment.TaxableBase > automatic.GrossAmount ||
                    adjustment.Amount > adjustment.TaxableBase ||
                    rule.BaseKind == WithholdingBaseKind.VatAmount && adjustment.TaxableBase > vatAmount)
                    throw new TaxationValidationException("La base o el valor de la retención manual no es válido.");
                lines[rule.RuleId] = new WithholdingLineSnapshot(
                    rule.RuleId, rule.Version, rule.Code, rule.Name, rule.Kind.ToString(),
                    rule.BaseKind.ToString(), adjustment.TaxableBase.Value, rule.Rate,
                    adjustment.Amount.Value, rule.JurisdictionCode);
            }
            else throw new TaxationValidationException("La acción de retención manual no es válida.");

            audit.Add(new WithholdingAdjustmentSnapshot(rule.RuleId, rule.Version,
                adjustment.Action, original?.TaxableBase, original?.Amount,
                adjustment.TaxableBase, adjustment.Amount, reason, userId));
        }
        var total = lines.Values.Sum(line => line.Amount) + manualLines.Sum(line => line.Amount);
        if (total > automatic.GrossAmount)
            throw new TaxationValidationException("Las retenciones superan el total del documento.");
        return new WithholdingCalculationSnapshot(automatic.GrossAmount, total,
            automatic.GrossAmount - total,
            lines.Values.Concat(manualLines).OrderBy(line => line.Kind, StringComparer.Ordinal)
                .ThenBy(line => line.RuleCode, StringComparer.Ordinal).ToArray(), audit);
    }

    private static WithholdingCalculationContext CreateContext(
        WithholdingCalculationPlan plan, WithholdingPreviewRequest request)
    {
        if (request.BusinessId != plan.BusinessId)
            throw new TaxationForbiddenException("The calculation belongs to another business.");
        plan.Profiles.TryGetValue(request.CounterpartyId, out var profile);
        var responsibilities = request.CounterpartyResponsibilities is { Count: > 0 }
            ? new HashSet<string>(request.CounterpartyResponsibilities
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(Normalize), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(profile?.Responsibilities ?? [], StringComparer.OrdinalIgnoreCase);
        var jurisdictionCode = string.IsNullOrWhiteSpace(request.JurisdictionCode)
            ? profile?.JurisdictionCode
            : request.JurisdictionCode.Trim().ToUpperInvariant();
        return new WithholdingCalculationContext(
            plan.BusinessId, Parse<WithholdingDirection>(request.Direction, nameof(request.Direction)),
            Parse<WithholdingRecognitionMoment>(request.Moment, nameof(request.Moment)),
            request.CounterpartyId, request.ConceptCode, jurisdictionCode,
            request.TaxExclusiveAmount, request.VatAmount, request.OccurredAt,
            profile?.AppliesWithholding ?? false, responsibilities,
            new HashSet<Guid>(request.PreviouslyRecognizedRuleIds ?? []));
    }

    private static WithholdingRuleView ToView(WithholdingRule rule) => new(
        rule.RuleId, rule.BusinessId, rule.Version, rule.Code, rule.Name, rule.Kind.ToString(),
        rule.Direction.ToString(), rule.Moment.ToString(), rule.BaseKind.ToString(),
        rule.ConceptCode, rule.JurisdictionCode,
        rule.Rate, rule.MinimumBase, rule.RequiredResponsibilities.Order().ToArray(),
        rule.EffectiveFrom, rule.EffectiveTo, rule.IsActive, rule.AppliesAutomatically,
        rule.DefaultAccountId);

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
