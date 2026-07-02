using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public enum ServiceSelectionStatus
{
    Resolved,
    Ambiguous,
    NotFound
}

public sealed record ServiceSelectionResolution(
    ServiceSelectionStatus Status,
    string? ServiceName,
    IReadOnlyList<string> Candidates);

/// <summary>
/// Resolves a customer's raw service wording against the active catalog.
/// It only resolves when the wording points to a single service with enough evidence.
/// </summary>
public sealed class ServiceSelectionResolver
{
    private static readonly CompareInfo Cmp = CultureInfo.InvariantCulture.CompareInfo;
    private const CompareOptions Opts = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "al", "con", "de", "del", "el", "en", "la", "las", "lo", "los",
        "me", "mi", "para", "por", "un", "una", "y",
        "agendar", "agenda", "cita", "cotizar", "hacer", "necesito", "quiero", "quisiera",
        "reservar", "sacar", "servicio"
    };

    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ServiceSelectionResolver> _logger;

    public ServiceSelectionResolver(
        IUnitOfWork unitOfWork,
        ILogger<ServiceSelectionResolver> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<ServiceSelectionResolution> ResolveAsync(
        Guid businessId,
        string text,
        int maxCandidates = 5,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return NotFound([]);

        var services = (await _unitOfWork.Services.GetActiveByBusinessIdAsync(businessId)).ToList();
        if (services.Count == 0)
            return NotFound([]);

        var normalized = text.Trim();
        var exact = services
            .Where(s => ServiceNameEquals(s.ServiceName, normalized)
                        || CompactName(s.ServiceName).Equals(CompactName(normalized), StringComparison.OrdinalIgnoreCase)
                        || KeywordEquals(s.Keywords, normalized))
            .ToList();

        if (exact.Count == 1)
            return Resolved(exact[0]);

        if (exact.Count > 1)
            return Ambiguous(exact, maxCandidates);

        var inputTokens = Tokenize(normalized).ToList();
        if (inputTokens.Count == 0)
            return NotFound([]);

        var scored = services
            .Select(s => Score(s, normalized, inputTokens))
            .Where(c => c.Score > 0)
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.MatchedNameTokens)
            .ThenBy(c => c.Service.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (scored.Count == 0)
            return NotFound([]);

        if (scored.Count == 1)
            return Resolved(scored[0].Service);

        if (IsClearWinner(scored[0], scored[1], inputTokens.Count))
            return Resolved(scored[0].Service);

        _logger.LogInformation(
            "ServiceSelectionResolver: ambiguous service text '{Text}' for business {BusinessId}: {Candidates}",
            text,
            businessId,
            string.Join(", ", scored.Take(maxCandidates).Select(s => s.Service.ServiceName)));

        return Ambiguous(scored.Select(s => s.Service), maxCandidates);
    }

    private static bool IsClearWinner(ServiceCandidate top, ServiceCandidate second, int inputTokenCount)
    {
        var requiredNameTokens = Math.Min(2, inputTokenCount);
        return top.Score >= second.Score + 3
               && top.Score >= 6
               && top.MatchedNameTokens >= requiredNameTokens;
    }

    private static ServiceCandidate Score(Service service, string input, IReadOnlyList<string> inputTokens)
    {
        var nameTokens = Tokenize(service.ServiceName).ToList();
        var descriptionTokens = Tokenize(service.Description).ToList();
        var keywordTokens = Tokenize(service.Keywords).ToList();
        var score = 0;
        var matchedNameTokens = 0;

        if (Cmp.IndexOf(input, service.ServiceName, Opts) >= 0)
            score += 10;

        if (Cmp.IndexOf(service.ServiceName, input, Opts) >= 0)
            score += 4;

        var compactInput = CompactName(input);
        var compactName = CompactName(service.ServiceName);
        if (compactInput.Length >= 3
            && (compactName.Contains(compactInput, StringComparison.OrdinalIgnoreCase)
                || compactInput.Contains(compactName, StringComparison.OrdinalIgnoreCase)))
        {
            score += 2;
        }

        foreach (var token in inputTokens)
        {
            if (MatchesAny(nameTokens, token))
            {
                score += 3;
                matchedNameTokens++;
                continue;
            }

            if (MatchesAny(keywordTokens, token))
            {
                score += 4;
                matchedNameTokens++;
                continue;
            }

            if (MatchesAny(descriptionTokens, token))
                score += 1;
        }

        return new ServiceCandidate(service, score, matchedNameTokens);
    }

    private static bool MatchesAny(IEnumerable<string> candidates, string token) =>
        candidates.Any(candidate =>
            candidate.Equals(token, StringComparison.OrdinalIgnoreCase)
            || (token.Length >= 4 && candidate.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            || (candidate.Length >= 4 && token.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)));

    private static bool ServiceNameEquals(string serviceName, string input) =>
        Cmp.Compare(serviceName, input, Opts) == 0;

    private static bool KeywordEquals(string? keywords, string input) =>
        SplitKeywords(keywords).Any(keyword =>
            Cmp.Compare(keyword, input, Opts) == 0
            || CompactName(keyword).Equals(CompactName(input), StringComparison.OrdinalIgnoreCase));

    private static string CompactName(string value) =>
        string.Concat(RemoveDiacritics(value).Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private static IEnumerable<string> SplitKeywords(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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

    private static ServiceSelectionResolution Resolved(Service service) =>
        new(ServiceSelectionStatus.Resolved, service.ServiceName, [service.ServiceName]);

    private static ServiceSelectionResolution Ambiguous(IEnumerable<Service> services, int maxCandidates) =>
        new(ServiceSelectionStatus.Ambiguous, null, services
            .Select(s => s.ServiceName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxCandidates)
            .ToList());

    private static ServiceSelectionResolution NotFound(IReadOnlyList<string> candidates) =>
        new(ServiceSelectionStatus.NotFound, null, candidates);

    private sealed record ServiceCandidate(Service Service, int Score, int MatchedNameTokens);
}
