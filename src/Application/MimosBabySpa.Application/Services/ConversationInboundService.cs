using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.DTOs;

namespace MimosBabySpa.Application.Services;

public sealed record ConversationInboundRequest(
    Guid BusinessId,
    string Provider,
    string UserNumber,
    string MessageText,
    string? CustomerName = null,
    string? ProviderMessageId = null,
    TimeSpan? DebounceDelay = null,
    IReadOnlyDictionary<string, string>? Facts = null);

public sealed record ConversationInboundResult(
    string ProviderMessageId,
    bool IsNew);

public interface IConversationInboundService
{
    Task<ConversationInboundResult> EnqueueAsync(
        ConversationInboundRequest request,
        CancellationToken ct = default);
}

public sealed class ConversationInboundService : IConversationInboundService
{
    private static readonly TimeSpan DefaultDebounceDelay = TimeSpan.FromSeconds(1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IInboundMessageDeduplicationService _deduplicationService;
    private readonly IWhatsAppInboundQueueService _queueService;
    private readonly ILogger<ConversationInboundService> _logger;

    public ConversationInboundService(
        IInboundMessageDeduplicationService deduplicationService,
        IWhatsAppInboundQueueService queueService,
        ILogger<ConversationInboundService> logger)
    {
        _deduplicationService = deduplicationService;
        _queueService = queueService;
        _logger = logger;
    }

    public async Task<ConversationInboundResult> EnqueueAsync(
        ConversationInboundRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.BusinessId == Guid.Empty)
            throw new ArgumentException("BusinessId is required.", nameof(request));

        var provider = NormalizeRequired(request.Provider, nameof(request.Provider));
        var userNumber = NormalizeRequired(request.UserNumber, nameof(request.UserNumber));
        var messageText = NormalizeRequired(request.MessageText, nameof(request.MessageText));
        var providerMessageId = NormalizeProviderMessageId(request.ProviderMessageId, provider);
        var customerName = string.IsNullOrWhiteSpace(request.CustomerName)
            ? null
            : request.CustomerName.Trim();

        var now = DateTime.UtcNow;
        var dueAtUtc = now.Add(request.DebounceDelay ?? DefaultDebounceDelay);
        var facts = NormalizeFacts(request.Facts);
        var rawEntryJson = JsonSerializer.Serialize(
            BuildCompatibleEntry(providerMessageId, userNumber, messageText, customerName, facts),
            JsonOptions);

        var isNew = await _deduplicationService.TryRecordReceivedAsync(
            request.BusinessId,
            provider,
            providerMessageId,
            userNumber,
            customerName,
            rawEntryJson,
            now,
            dueAtUtc,
            ct);

        if (!isNew)
        {
            _logger.LogInformation(
                "Inbound message duplicate ignored. BusinessId: {BusinessId}, Provider: {Provider}, ProviderMessageId: {ProviderMessageId}",
                request.BusinessId,
                provider,
                providerMessageId);

            return new ConversationInboundResult(providerMessageId, false);
        }

        await _queueService.ScheduleDebounceAsync(
            request.BusinessId,
            provider,
            userNumber,
            providerMessageId,
            dueAtUtc,
            ct);

        await _deduplicationService.MarkQueuedAsync(
            request.BusinessId,
            provider,
            providerMessageId,
            dueAtUtc,
            ct);

        return new ConversationInboundResult(providerMessageId, true);
    }

    private static Entry BuildCompatibleEntry(
        string providerMessageId,
        string userNumber,
        string messageText,
        string? customerName,
        IReadOnlyDictionary<string, string> facts) =>
        new()
        {
            Id = $"synthetic:{providerMessageId}",
            Changes =
            [
                new Change
                {
                    Field = "messages",
                    Value = new Value
                    {
                        Contacts = string.IsNullOrWhiteSpace(customerName)
                            ? []
                            :
                            [
                                new Contact
                                {
                                    Profile = new Profile { Name = customerName }
                                }
                            ],
                        Messages =
                        [
                            new Message
                            {
                                Id = providerMessageId,
                                From = userNumber,
                                Type = "text",
                                Text = new TextMessage { Body = messageText },
                                Facts = facts.Count == 0 ? null : new Dictionary<string, string>(facts, StringComparer.OrdinalIgnoreCase)
                            }
                        ]
                    }
                }
            ]
        };

    private static IReadOnlyDictionary<string, string> NormalizeFacts(IReadOnlyDictionary<string, string>? facts)
    {
        if (facts is null || facts.Count == 0)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        return facts
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(
                pair => pair.Key.Trim(),
                pair => pair.Value.Trim(),
                StringComparer.OrdinalIgnoreCase);
    }
    private static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{parameterName} is required.", parameterName);

        return value.Trim();
    }

    private static string NormalizeProviderMessageId(string? providerMessageId, string provider)
    {
        var normalized = string.IsNullOrWhiteSpace(providerMessageId)
            ? $"{provider}:{Guid.NewGuid():N}"
            : providerMessageId.Trim();

        if (normalized.Length > 128)
            throw new ArgumentException("ProviderMessageId cannot exceed 128 characters.", nameof(providerMessageId));

        return normalized;
    }
}
