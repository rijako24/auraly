using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;
using MimosBabySpa.WebAPI.Configuration;

namespace MimosBabySpa.WebAPI.Controllers;

[ApiController]
[Route("api/demo-requests")]
public sealed class DemoRequestsController : ControllerBase
{
    private const string DemoProvider = "web-demo";
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly DemoRequestOptions _options;
    private readonly IInboundMessageDeduplicationService _deduplicationService;
    private readonly IWhatsAppInboundQueueService _inboundQueueService;
    private readonly ILogger<DemoRequestsController> _logger;

    public DemoRequestsController(
        IOptions<DemoRequestOptions> options,
        IInboundMessageDeduplicationService deduplicationService,
        IWhatsAppInboundQueueService inboundQueueService,
        ILogger<DemoRequestsController> logger)
    {
        _options = options.Value;
        _deduplicationService = deduplicationService;
        _inboundQueueService = inboundQueueService;
        _logger = logger;
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Create([FromBody] DemoRequest request, CancellationToken ct)
    {
        if (_options.BusinessId == Guid.Empty)
            return Problem("DemoRequests:BusinessId no esta configurado.", statusCode: StatusCodes.Status500InternalServerError);

        var phone = NormalizeWhatsAppNumber(request.Phone);
        if (phone is null)
        {
            ModelState.AddModelError(nameof(request.Phone), "El WhatsApp debe incluir indicativo de pais o un celular colombiano valido.");
            return ValidationProblem(ModelState);
        }

        var now = DateTime.UtcNow;
        var dueAtUtc = now.Add(DebounceDelay);
        var providerMessageId = $"demo-request:{_options.BusinessId:N}:{Guid.NewGuid():N}";
        var requestSummary = BuildRequestSummary(request, phone);
        var rawEntryJson = JsonSerializer.Serialize(
            BuildSyntheticEntry(providerMessageId, phone, request.Name, requestSummary),
            JsonOptions);

        var isNew = await _deduplicationService.TryRecordReceivedAsync(
            _options.BusinessId,
            DemoProvider,
            providerMessageId,
            phone,
            request.Name,
            rawEntryJson,
            now,
            dueAtUtc,
            ct);

        await _inboundQueueService.ScheduleDebounceAsync(
            _options.BusinessId,
            DemoProvider,
            phone,
            providerMessageId,
            dueAtUtc,
            ct);

        if (isNew)
        {
            await _deduplicationService.MarkQueuedAsync(
                _options.BusinessId,
                DemoProvider,
                providerMessageId,
                dueAtUtc,
                ct);
        }

        _logger.LogInformation(
            "Demo request queued as inbound message {ProviderMessageId} for {Phone}",
            providerMessageId,
            phone);

        return Accepted(new
        {
            message = "Solicitud de demo recibida. Aly procesara la conversacion por WhatsApp.",
            requestId = providerMessageId
        });
    }

    private static Entry BuildSyntheticEntry(
        string providerMessageId,
        string phone,
        string? customerName,
        string messageText)
    {
        return new Entry
        {
            Id = providerMessageId,
            Changes =
            [
                new Change
                {
                    Field = "messages",
                    Value = new Value
                    {
                        Contacts =
                        [
                            new Contact
                            {
                                Profile = new Profile
                                {
                                    Name = ValueOrDefault(customerName, string.Empty)
                                }
                            }
                        ],
                        Messages =
                        [
                            new Message
                            {
                                Id = providerMessageId,
                                From = phone,
                                Type = "text",
                                Text = new TextMessage
                                {
                                    Body = messageText
                                }
                            }
                        ]
                    }
                }
            ]
        };
    }

    private static string BuildRequestSummary(DemoRequest request, string normalizedPhone)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Solicitud de demo desde la landing de AURALY");
        builder.AppendLine();
        builder.AppendLine($"WhatsApp: {normalizedPhone}");
        builder.AppendLine($"Nombre: {ValueOrDash(request.Name)}");
        builder.AppendLine($"Empresa: {ValueOrDash(request.Company)}");
        builder.AppendLine($"Email: {ValueOrDash(request.Email)}");
        builder.AppendLine();
        builder.AppendLine("Mensaje:");
        builder.AppendLine(ValueOrDash(request.Message));
        return builder.ToString();
    }

    private static string ValueOrDefault(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string ValueOrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private static string? NormalizeWhatsAppNumber(string value)
    {
        var digits = Regex.Replace(value.Trim(), "[^0-9]", string.Empty);
        if (digits.StartsWith("00", StringComparison.Ordinal))
            digits = digits[2..];

        if (digits.Length == 10 && digits.StartsWith("3", StringComparison.Ordinal))
            digits = "57" + digits;

        return digits.Length is >= 8 and <= 15 ? digits : null;
    }
}

public sealed record DemoRequest(
    [property: Required, MaxLength(60)] string Phone,
    [property: EmailAddress, MaxLength(200)] string? Email,
    [property: MaxLength(120)] string? Name,
    [property: MaxLength(160)] string? Company,
    [property: MaxLength(1000)] string? Message);
