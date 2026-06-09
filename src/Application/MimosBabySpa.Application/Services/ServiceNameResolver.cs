using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Resuelve un nombre de servicio extraído por el LLM al nombre canónico en base de datos.
///
/// Estrategia de resolución (orden de prioridad):
///   1. Coincidencia exacta (case + accent insensitive).
///   2. El nombre extraído contiene el nombre canónico ("Plan Marineritos" ⊇ "Marineritos").
///   3. El nombre canónico contiene el nombre extraído ("Marineritos" ⊆ "Plan Marineritos").
///   4. Si no hay match → null (el caller decide qué hacer).
///
/// Usa CompareOptions.IgnoreNonSpace para tolerar acentos omitidos por el LLM
/// (e.g. "decoracion" matchea "Decoración").
/// </summary>
public class ServiceNameResolver
{
    private static readonly CompareInfo Cmp = CultureInfo.InvariantCulture.CompareInfo;
    private const CompareOptions Opts = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "al", "con", "de", "del", "el", "en", "la", "las", "lo", "los",
        "para", "por", "un", "una", "y"
    };

    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ServiceNameResolver> _logger;

    public ServiceNameResolver(IUnitOfWork unitOfWork, ILogger<ServiceNameResolver> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// Devuelve el ServiceName canónico de la base de datos que mejor coincide con <paramref name="input"/>.
    /// Retorna null si no encuentra coincidencia.
    /// </summary>
    public async Task<string?> ResolveAsync(Guid businessId, string input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var services = await _unitOfWork.Services.GetActiveByBusinessIdAsync(businessId);
        var serviceList = services.ToList();

        var normalized = input.Trim();

        var match = serviceList.FirstOrDefault(s =>
            Cmp.Compare(s.ServiceName, normalized, Opts) == 0);
        if (match != null) return match.ServiceName;

        match = serviceList.FirstOrDefault(s =>
            Cmp.IndexOf(normalized, s.ServiceName, Opts) >= 0);
        if (match != null)
        {
            _logger.LogInformation(
                "ServiceNameResolver: '{Input}' → '{Canonical}' (canonical contained in input)",
                input, match.ServiceName);
            return match.ServiceName;
        }

        match = serviceList.FirstOrDefault(s =>
            Cmp.IndexOf(s.ServiceName, normalized, Opts) >= 0);
        if (match != null)
        {
            _logger.LogInformation(
                "ServiceNameResolver: '{Input}' → '{Canonical}' (input contained in canonical)",
                input, match.ServiceName);
            return match.ServiceName;
        }

        var compactInput = CompactName(normalized);
        if (!string.IsNullOrWhiteSpace(compactInput))
        {
            match = serviceList.FirstOrDefault(s =>
                Cmp.Compare(CompactName(s.ServiceName), compactInput, Opts) == 0);
            if (match != null)
            {
                _logger.LogInformation(
                    "ServiceNameResolver: '{Input}' → '{Canonical}' (compact exact match)",
                    input, match.ServiceName);
                return match.ServiceName;
            }

            match = serviceList.FirstOrDefault(s =>
                Cmp.IndexOf(CompactName(s.ServiceName), compactInput, Opts) >= 0
                || Cmp.IndexOf(compactInput, CompactName(s.ServiceName), Opts) >= 0);
            if (match != null)
            {
                _logger.LogInformation(
                    "ServiceNameResolver: '{Input}' → '{Canonical}' (compact contained match)",
                    input, match.ServiceName);
                return match.ServiceName;
            }
        }

        match = ResolveByTokenScore(serviceList, normalized);
        if (match != null)
        {
            _logger.LogInformation(
                "ServiceNameResolver: '{Input}' → '{Canonical}' (token score match)",
                input, match.ServiceName);
            return match.ServiceName;
        }

        _logger.LogWarning(
            "ServiceNameResolver: no match found for '{Input}' in business {BusinessId}",
            input, businessId);
        return null;
    }

    public async Task<IReadOnlyList<string>> GetCandidateNamesAsync(
        Guid businessId,
        string input,
        int max = 5,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input) || max <= 0)
            return [];

        var services = await _unitOfWork.Services.GetActiveByBusinessIdAsync(businessId);
        var inputTokens = Tokenize(input).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (inputTokens.Count == 0)
            return services.Select(s => s.ServiceName).OrderBy(n => n).Take(max).ToList();

        return services
            .Select(s => new
            {
                s.ServiceName,
                Score = ScoreService(s, inputTokens)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.ServiceName)
            .Take(max)
            .Select(x => x.ServiceName)
            .ToList();
    }

    private static string CompactName(string value)
    {
        return string.Concat(value.Where(char.IsLetterOrDigit));
    }

    private static Service? ResolveByTokenScore(IReadOnlyList<Service> services, string input)
    {
        var inputTokens = Tokenize(input).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (inputTokens.Count == 0)
            return null;

        var scored = services
            .Select(s => new { Service = s, Score = ScoreService(s, inputTokens) })
            .Where(x => x.Score >= 4)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Service.ServiceName)
            .ToList();

        if (scored.Count == 0)
            return null;

        if (scored.Count == 1 || scored[0].Score >= scored[1].Score + 2)
            return scored[0].Service;

        return null;
    }

    private static int ScoreService(Service service, ISet<string> inputTokens)
    {
        var nameTokens = Tokenize(service.ServiceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var descriptionTokens = Tokenize(service.Description).ToHashSet(StringComparer.OrdinalIgnoreCase);

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
}
