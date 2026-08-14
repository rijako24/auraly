using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Auraly.Platform.Application.Common.Exceptions;
using Auraly.Platform.Application.Common.Interfaces;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Infrastructure.Configuration;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Services;

public sealed class WhatsAppChannelAdminService : IWhatsAppChannelAdminService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _auditService;
    private readonly HttpClient _httpClient;
    private readonly string? _apiBaseUrl;

    public WhatsAppChannelAdminService(ApplicationDbContext db, IAuditService auditService,
        HttpClient httpClient, IOptions<WhatsAppWebhookOptions> options)
    {
        _db = db;
        _auditService = auditService;
        _httpClient = httpClient;
        _apiBaseUrl = string.IsNullOrWhiteSpace(options.Value.ApiBaseUrl)
            ? null
            : options.Value.ApiBaseUrl.TrimEnd('/') + "/";
        if (_apiBaseUrl is not null)
            _httpClient.BaseAddress = new Uri(_apiBaseUrl, UriKind.Absolute);
    }

    public async Task<IReadOnlyList<WhatsAppChannelDto>> GetByBusinessAsync(Guid tenantId, bool allTenants, Guid businessId, CancellationToken ct = default)
    {
        await EnsureBusinessScopeAsync(tenantId, allTenants, businessId, ct);
        var channels = await _db.BusinessWhatsAppNumbers.AsNoTracking().Include(x => x.Agent)
            .Where(x => x.BusinessId == businessId).OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.PhoneNumber).ToListAsync(ct);
        return channels.Select(Map).ToList();
    }

    public async Task<WhatsAppChannelDto> CreateAsync(Guid tenantId, bool allTenants, Guid businessId, CreateWhatsAppChannelRequest request, CancellationToken ct = default)
    {
        await EnsureBusinessScopeAsync(tenantId, allTenants, businessId, ct);
        var agent = await GetAgentAsync(businessId, request.AgentId, ct);
        ValidateRequired(request.PhoneNumber, request.WhatsAppPhoneNumberId, request.WhatsAppBusinessAccountId, request.AccessToken);
        var phoneId = request.WhatsAppPhoneNumberId.Trim();
        if (await _db.BusinessWhatsAppNumbers.AnyAsync(x => x.WhatsAppPhoneNumberId == phoneId, ct))
            throw new DomainValidationException("WhatsAppPhoneNumberId", "Este Phone Number ID ya esta configurado.");
        var channel = new BusinessWhatsAppNumber {
            BusinessWhatsAppNumberId = Guid.NewGuid(), BusinessId = businessId, AgentId = agent.AgentId, Agent = agent,
            PhoneNumber = request.PhoneNumber.Trim(), WhatsAppPhoneNumberId = phoneId,
            WhatsAppBusinessAccountId = request.WhatsAppBusinessAccountId.Trim(), WhatsAppAccessToken = request.AccessToken.Trim(),
            IsActive = request.IsActive, CreatedAt = DateTime.UtcNow };
        _db.BusinessWhatsAppNumbers.Add(channel);
        await _db.SaveChangesAsync(ct);
        var result = Map(channel);
        await _auditService.LogAsync("Create", "WhatsAppChannel", channel.BusinessWhatsAppNumberId.ToString(), null, result, ct);
        return result;
    }

    public async Task<WhatsAppChannelDto> UpdateAsync(Guid tenantId, bool allTenants, Guid businessId, Guid channelId, UpdateWhatsAppChannelRequest request, CancellationToken ct = default)
    {
        await EnsureBusinessScopeAsync(tenantId, allTenants, businessId, ct);
        var channel = await GetChannelAsync(businessId, channelId, ct);
        var oldState = Map(channel);
        var agent = await GetAgentAsync(businessId, request.AgentId, ct);
        ValidateRequired(request.PhoneNumber, request.WhatsAppPhoneNumberId, request.WhatsAppBusinessAccountId);
        var phoneId = request.WhatsAppPhoneNumberId.Trim();
        if (await _db.BusinessWhatsAppNumbers.AnyAsync(x => x.WhatsAppPhoneNumberId == phoneId && x.BusinessWhatsAppNumberId != channelId, ct))
            throw new DomainValidationException("WhatsAppPhoneNumberId", "Este Phone Number ID ya esta configurado.");
        channel.AgentId = agent.AgentId; channel.Agent = agent; channel.PhoneNumber = request.PhoneNumber.Trim();
        channel.WhatsAppPhoneNumberId = phoneId; channel.WhatsAppBusinessAccountId = request.WhatsAppBusinessAccountId.Trim();
        if (!string.IsNullOrWhiteSpace(request.AccessToken)) channel.WhatsAppAccessToken = request.AccessToken.Trim();
        channel.IsActive = request.IsActive;
        await _db.SaveChangesAsync(ct);
        var result = Map(channel);
        await _auditService.LogAsync("Update", "WhatsAppChannel", channelId.ToString(), oldState, result, ct);
        return result;
    }

    public async Task DeactivateAsync(Guid tenantId, bool allTenants, Guid businessId, Guid channelId, CancellationToken ct = default)
    {
        await EnsureBusinessScopeAsync(tenantId, allTenants, businessId, ct);
        var channel = await GetChannelAsync(businessId, channelId, ct);
        channel.IsActive = false;
        await _db.SaveChangesAsync(ct);
        await _auditService.LogAsync("Deactivate", "WhatsAppChannel", channelId.ToString(), null, null, ct);
    }

    public async Task<WhatsAppChannelConnectionStatusDto> ValidateAsync(Guid tenantId, bool allTenants, Guid businessId, Guid channelId, CancellationToken ct = default)
    {
        if (_apiBaseUrl is null)
            throw new DomainValidationException("WhatsApp",
                "Configura la URL oficial de Meta antes de validar el canal.");
        await EnsureBusinessScopeAsync(tenantId, allTenants, businessId, ct);
        var channel = await GetChannelAsync(businessId, channelId, ct);
        try
        {
            using var phoneRequest = CreateMetaRequest($"{channel.WhatsAppPhoneNumberId}?fields=id,display_phone_number,verified_name,quality_rating", channel.WhatsAppAccessToken);
            using var phoneResponse = await _httpClient.SendAsync(phoneRequest, ct);
            var phoneBody = await phoneResponse.Content.ReadAsStringAsync(ct);
            if (!phoneResponse.IsSuccessStatusCode) return Failure(ReadMetaError(phoneBody));
            var phone = JsonSerializer.Deserialize<MetaPhoneResponse>(phoneBody, JsonOptions);

            using var businessRequest = CreateMetaRequest($"{channel.WhatsAppBusinessAccountId}?fields=id,name", channel.WhatsAppAccessToken);
            using var businessResponse = await _httpClient.SendAsync(businessRequest, ct);
            var businessBody = await businessResponse.Content.ReadAsStringAsync(ct);
            if (!businessResponse.IsSuccessStatusCode) return Failure(ReadMetaError(businessBody));
            var business = JsonSerializer.Deserialize<MetaBusinessResponse>(businessBody, JsonOptions);
            return new(true, "connected", "Meta confirmo el numero y la cuenta de WhatsApp Business.",
                phone?.VerifiedName, phone?.DisplayPhoneNumber, phone?.QualityRating, business?.Name, DateTime.UtcNow);
        }
        catch (HttpRequestException)
        {
            return Failure("No fue posible comunicarse con Meta. Intenta nuevamente.");
        }
    }

    private HttpRequestMessage CreateMetaRequest(string url, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static WhatsAppChannelConnectionStatusDto Failure(string message) =>
        new(false, "error", message, null, null, null, null, DateTime.UtcNow);

    private static string ReadMetaError(string body)
    {
        try { return JsonSerializer.Deserialize<MetaErrorEnvelope>(body, JsonOptions)?.Error?.Message ?? "Meta rechazo las credenciales configuradas."; }
        catch (JsonException) { return "Meta rechazo las credenciales configuradas."; }
    }

    private async Task EnsureBusinessScopeAsync(Guid tenantId, bool allTenants, Guid businessId, CancellationToken ct)
    {
        var business = await _db.Businesses.AsNoTracking().FirstOrDefaultAsync(x => x.BusinessId == businessId, ct)
            ?? throw new NotFoundException(nameof(Business), businessId);
        if (!allTenants && business.TenantId != tenantId) throw new NotFoundException(nameof(Business), businessId);
    }

    private async Task<Agent> GetAgentAsync(Guid businessId, Guid agentId, CancellationToken ct)
    {
        var agent = await _db.Agents.FirstOrDefaultAsync(x => x.AgentId == agentId, ct) ?? throw new NotFoundException(nameof(Agent), agentId);
        if (agent.BusinessId != businessId) throw new DomainValidationException("AgentId", "El agente no pertenece al negocio seleccionado.");
        return agent;
    }

    private async Task<BusinessWhatsAppNumber> GetChannelAsync(Guid businessId, Guid channelId, CancellationToken ct) =>
        await _db.BusinessWhatsAppNumbers.Include(x => x.Agent)
            .FirstOrDefaultAsync(x => x.BusinessWhatsAppNumberId == channelId && x.BusinessId == businessId, ct)
        ?? throw new NotFoundException(nameof(BusinessWhatsAppNumber), channelId);

    private static void ValidateRequired(params string?[] values)
    {
        if (values.Any(string.IsNullOrWhiteSpace)) throw new DomainValidationException("WhatsApp", "Numero, Phone Number ID, WABA ID y token son obligatorios.");
    }

    private static WhatsAppChannelDto Map(BusinessWhatsAppNumber x) => new(x.BusinessWhatsAppNumberId, x.BusinessId,
        x.AgentId ?? Guid.Empty, x.Agent?.Name ?? "Sin agente", x.PhoneNumber, x.WhatsAppPhoneNumberId,
        x.WhatsAppBusinessAccountId ?? string.Empty, !string.IsNullOrWhiteSpace(x.WhatsAppAccessToken), x.IsActive, x.CreatedAt);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private sealed record MetaPhoneResponse(
        string? Id,
        [property: System.Text.Json.Serialization.JsonPropertyName("display_phone_number")] string? DisplayPhoneNumber,
        [property: System.Text.Json.Serialization.JsonPropertyName("verified_name")] string? VerifiedName,
        [property: System.Text.Json.Serialization.JsonPropertyName("quality_rating")] string? QualityRating);
    private sealed record MetaBusinessResponse(string? Id, string? Name);
    private sealed record MetaErrorEnvelope(MetaError? Error);
    private sealed record MetaError(string? Message);
}
