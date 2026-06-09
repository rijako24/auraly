using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public sealed class AddOnCatalogService : IAddOnCatalogService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ServiceNameResolver _nameResolver;

    public AddOnCatalogService(IUnitOfWork unitOfWork, ServiceNameResolver nameResolver)
    {
        _unitOfWork = unitOfWork;
        _nameResolver = nameResolver;
    }

    public async Task<IReadOnlyList<AddOnRuleInfo>> GetCompatibleAsync(
        Guid businessId,
        string serviceName,
        CancellationToken ct = default)
    {
        var service = await ResolveServiceAsync(businessId, serviceName, ct);
        if (service is null)
            return [];

        var rules = await _unitOfWork.ServiceAddOnRules.GetByBusinessIdAsync(businessId);
        return rules
            .Where(r => IsCompatible(r, service))
            .Select(MapRule)
            .OrderBy(r => r.DisplayOrder)
            .ThenBy(r => r.AddOnName)
            .ToList();
    }

    public async Task<AddOnValidationResult> ValidateAsync(
        Guid businessId,
        string serviceName,
        string? addOnsCsv,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(addOnsCsv) || IsNone(addOnsCsv))
            return AddOnValidationResult.Ok(null);

        var service = await ResolveServiceAsync(businessId, serviceName, ct);
        if (service is null)
        {
            return AddOnValidationResult.Fail(
                $"Service '{serviceName}' was not found in the catalog.",
                "Call get_service_catalog to get the current list of services.");
        }

        var compatible = await GetCompatibleAsync(businessId, service.ServiceName, ct);
        var compatibleNames = compatible
            .Select(a => a.AddOnName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var resolvedNames = new List<string>();
        var invalidNames = new List<string>();

        foreach (var rawName in SplitNames(addOnsCsv))
        {
            var canonical = await _nameResolver.ResolveAsync(businessId, rawName, ct);
            if (canonical is null || !compatibleNames.Contains(canonical))
            {
                invalidNames.Add(rawName);
                continue;
            }

            resolvedNames.Add(canonical);
        }

        if (invalidNames.Count > 0)
        {
            var hint = compatible.Count > 0
                ? $"Compatible add-ons for '{service.ServiceName}': {string.Join(", ", compatible.Select(a => a.AddOnName))}. To indicate no add-ons, use 'ninguno'."
                : $"There are no compatible add-ons for '{service.ServiceName}'. To indicate no add-ons, use 'ninguno'.";

            return AddOnValidationResult.Fail(
                $"Invalid or incompatible add-on(s): {string.Join(", ", invalidNames)}.",
                hint);
        }

        return AddOnValidationResult.Ok(string.Join(", ", resolvedNames));
    }

    private async Task<Service?> ResolveServiceAsync(
        Guid businessId, string serviceName, CancellationToken ct)
    {
        var canonical = await _nameResolver.ResolveAsync(businessId, serviceName, ct);
        if (canonical is null)
            return null;

        return await _unitOfWork.Services.GetByBusinessIdAndNameAsync(businessId, canonical);
    }

    private static bool IsCompatible(ServiceAddOnRule rule, Service selectedService)
    {
        if (!rule.AddOnService.IsActive)
            return false;

        if (rule.AddOnService.ServiceType != ServiceType.AddOn)
            return false;

        if (rule.CompatibleServiceId.HasValue)
            return rule.CompatibleServiceId.Value == selectedService.ServiceId;

        return true;
    }

    private static AddOnRuleInfo MapRule(ServiceAddOnRule rule) => new()
    {
        AddOnName = rule.AddOnService.ServiceName,
        AddOnDescription = rule.AddOnService.Description,
        AddOnPrice = rule.AddOnService.Price,
        DisplayOrder = rule.DisplayOrder,
        CompatibleWithServiceName = rule.CompatibleService?.ServiceName,
        CompatibleCategoryId = rule.CompatibleService?.CategoryId,
        CompatibleCategoryName = rule.CompatibleService?.ServiceCategory?.Name
    };

    private static bool IsNone(string value) =>
        string.Equals(value.Trim(), "ninguno", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> SplitNames(string raw)
    {
        foreach (var part in raw.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!IsNone(part))
                yield return part;
        }
    }
}
