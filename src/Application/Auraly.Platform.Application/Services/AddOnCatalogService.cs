using Auraly.Platform.Application.Configuration;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;
using System.Globalization;
using System.Text;

namespace Auraly.Platform.Application.Services;

public sealed class AddOnCatalogService : IAddOnCatalogService
{
    private static readonly CompareInfo Cmp = CultureInfo.InvariantCulture.CompareInfo;
    private const CompareOptions Opts = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "al", "con", "de", "del", "el", "en", "la", "las", "lo", "los",
        "para", "por", "un", "una", "y"
    };

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
                null, "service_not_found");
        }

        var compatible = await GetCompatibleAsync(businessId, service.ServiceName, ct);
        var resolvedNames = new List<string>();
        var invalidNames = new List<string>();
        var ambiguous = new List<(string Input, IReadOnlyList<string> Matches)>();

        foreach (var rawName in SplitNames(addOnsCsv))
        {
            var matches = ResolveCompatibleAddOnMatches(compatible, rawName);
            if (matches.Count == 1)
            {
                resolvedNames.Add(matches[0].AddOnName);
                continue;
            }

            if (matches.Count > 1)
            {
                ambiguous.Add((rawName, matches.Select(m => m.AddOnName).ToList()));
                continue;
            }

            invalidNames.Add(rawName);
        }

        if (ambiguous.Count > 0)
        {
            var details = string.Join("; ", ambiguous.Select(a =>
                $"'{a.Input}' puede ser: {string.Join(", ", a.Matches)}"));

            return AddOnValidationResult.Fail(
                $"Ambiguous add-on selection: {details}.",
                null,
                "ambiguous_add_ons");
        }

        if (invalidNames.Count > 0)
        {
            var remediation = compatible.Count > 0
                ? $"Compatible add-ons for '{service.ServiceName}': {string.Join(", ", compatible.Select(a => a.AddOnName))}. To indicate no add-ons, use 'ninguno'."
                : $"There are no compatible add-ons for '{service.ServiceName}'. To indicate no add-ons, use 'ninguno'.";

            return AddOnValidationResult.Fail(
                $"Invalid or incompatible add-on(s): {string.Join(", ", invalidNames)}.",
                remediation);
        }

        var duplicateGroup = FindDuplicateGroup(resolvedNames);
        if (duplicateGroup is not null)
        {
            return AddOnValidationResult.Fail(
                $"Multiple add-ons from the same group were selected: {string.Join(", ", duplicateGroup.Value.Names)}.",
                null,
                "duplicate_add_on_group");
        }

        return AddOnValidationResult.Ok(string.Join(", ", resolvedNames));
    }

    private static IReadOnlyList<AddOnRuleInfo> ResolveCompatibleAddOnMatches(
        IReadOnlyList<AddOnRuleInfo> compatible,
        string input)
    {
        var normalized = input.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return [];

        var exact = compatible
            .Where(a => Cmp.Compare(a.AddOnName, normalized, Opts) == 0)
            .ToList();
        if (exact.Count > 0)
            return exact;

        var contained = compatible
            .Where(a =>
                Cmp.IndexOf(a.AddOnName, normalized, Opts) >= 0
                || Cmp.IndexOf(normalized, a.AddOnName, Opts) >= 0)
            .ToList();
        if (contained.Count > 0)
            return contained;

        var inputTokens = Tokenize(normalized).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (inputTokens.Count == 0)
            return [];

        var scored = compatible
            .Select(a => new
            {
                AddOn = a,
                Score = ScoreAddOn(a, inputTokens)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.AddOn.AddOnName)
            .ToList();

        if (scored.Count == 0)
            return [];

        var bestScore = scored[0].Score;
        return scored
            .Where(x => x.Score == bestScore)
            .Select(x => x.AddOn)
            .ToList();
    }

    private static int ScoreAddOn(AddOnRuleInfo addOn, ISet<string> inputTokens)
    {
        var nameTokens = Tokenize(addOn.AddOnName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var descriptionTokens = Tokenize(addOn.AddOnDescription).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var score = 0;
        foreach (var token in inputTokens)
        {
            if (nameTokens.Contains(token))
                score += 3;
            else if (descriptionTokens.Contains(token))
                score += 1;
        }

        return score;
    }

    private static (string Group, IReadOnlyList<string> Names)? FindDuplicateGroup(IReadOnlyList<string> names)
    {
        var grouped = names
            .Select(name => new { Name = name, Group = ResolveGroupKey(name) })
            .Where(x => !string.IsNullOrWhiteSpace(x.Group))
            .GroupBy(x => x.Group, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        return grouped is null
            ? null
            : (grouped.Key, grouped.Select(x => x.Name).ToList());
    }

    private static string ResolveGroupKey(string name) =>
        Tokenize(name).FirstOrDefault() ?? string.Empty;

    private static IEnumerable<string> Tokenize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        var normalized = RemoveDiacritics(value).ToLowerInvariant();
        var token = new List<char>();
        foreach (var ch in normalized)
        {
            if (char.IsLetterOrDigit(ch))
            {
                token.Add(ch);
                continue;
            }

            foreach (var emitted in FlushToken(token))
                yield return emitted;
        }

        foreach (var emitted in FlushToken(token))
            yield return emitted;
    }

    private static IEnumerable<string> FlushToken(List<char> token)
    {
        if (token.Count == 0)
            yield break;

        var text = new string(token.ToArray());
        token.Clear();

        if ((text.Length > 1 || text.All(char.IsDigit)) && !StopWords.Contains(text))
            yield return text;
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        return string.Concat(normalized.Where(ch =>
            CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark));
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
        IncludeInCheckoutTotal = rule.AddOnService.IncludeInCheckoutTotal,
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
