using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MimosBabySpa.Application.Common.Exceptions;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.WhatsAppTemplates.DTOs;
using MimosBabySpa.Application.WhatsAppTemplates.Interfaces;
using MimosBabySpa.Infrastructure.Configuration;

namespace MimosBabySpa.Infrastructure.Services;

public sealed partial class WhatsAppTemplateService : IWhatsAppTemplateService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IWhatsAppCredentialResolver _credentialResolver;

    public WhatsAppTemplateService(
        HttpClient httpClient,
        IWhatsAppCredentialResolver credentialResolver,
        IOptions<WhatsAppWebhookOptions> webhookOptions)
    {
        _httpClient = httpClient;
        _credentialResolver = credentialResolver;

        var options = webhookOptions.Value;
        if (string.IsNullOrWhiteSpace(options.ApiBaseUrl))
            throw new InvalidOperationException("WhatsApp:Webhook:ApiBaseUrl es obligatorio.");

        _httpClient.BaseAddress = new Uri(options.ApiBaseUrl.TrimEnd('/') + "/");
    }

    public async Task<IReadOnlyList<WhatsAppTemplateDto>> GetByBusinessIdAsync(
        Guid businessId,
        bool approvedOnly = true,
        CancellationToken ct = default)
    {
        var credentials = await _credentialResolver.ResolveAsync(businessId, ct);
        if (credentials is null || string.IsNullOrWhiteSpace(credentials.BusinessAccountId))
            throw new DomainValidationException("WhatsApp", "El negocio no tiene WABA configurado para consultar plantillas.");

        var fields = Uri.EscapeDataString("id,name,status,category,language,components");
        var url = $"{credentials.BusinessAccountId}/message_templates?fields={fields}&limit=100";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", $"Bearer {credentials.AccessToken}");

        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new DomainValidationException("WhatsApp", "No se pudieron consultar las plantillas de WhatsApp en Meta.");

        var payload = JsonSerializer.Deserialize<MetaTemplateResponse>(body, JsonOptions);
        var templates = payload?.Data ?? [];

        return templates
            .Where(t => !approvedOnly || string.Equals(t.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
            .Select(t => new WhatsAppTemplateDto(
                t.Id ?? string.Empty,
                t.Name ?? string.Empty,
                t.Status ?? string.Empty,
                NormalizeCategory(t.Category),
                t.Language ?? "es_CO",
                CountParameters(t.Components, "HEADER"),
                CountParameters(t.Components, "BODY")))
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .OrderBy(t => t.Name)
            .ThenBy(t => t.Language)
            .ToList();
    }

    private static string NormalizeCategory(string? category) =>
        string.Equals(category, "UTILITY", StringComparison.OrdinalIgnoreCase)
            ? "Utility"
            : "Marketing";

    private static int CountParameters(IReadOnlyList<MetaTemplateComponent>? components, string type)
    {
        var text = components?
            .FirstOrDefault(c => string.Equals(c.Type, type, StringComparison.OrdinalIgnoreCase))
            ?.Text;

        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return PlaceholderRegex().Matches(text).Select(m => m.Value).Distinct().Count();
    }

    [GeneratedRegex(@"\{\{\s*\d+\s*\}\}")]
    private static partial Regex PlaceholderRegex();

    private sealed record MetaTemplateResponse(IReadOnlyList<MetaTemplateItem>? Data);

    private sealed record MetaTemplateItem(
        string? Id,
        string? Name,
        string? Status,
        string? Category,
        string? Language,
        IReadOnlyList<MetaTemplateComponent>? Components);

    private sealed record MetaTemplateComponent(string? Type, string? Text);
}

