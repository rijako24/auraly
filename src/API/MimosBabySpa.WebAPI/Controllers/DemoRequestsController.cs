using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.WebAPI.Configuration;

namespace MimosBabySpa.WebAPI.Controllers;

[ApiController]
[Route("api/demo-requests")]
public sealed class DemoRequestsController : ControllerBase
{
    private const string WhatsAppProvider = "whatsapp";
    private readonly DemoRequestOptions _options;
    private readonly IConversationInboundService _conversationInbound;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DemoRequestsController> _logger;

    public DemoRequestsController(
        IOptions<DemoRequestOptions> options,
        IConversationInboundService conversationInbound,
        IUnitOfWork unitOfWork,
        ILogger<DemoRequestsController> logger)
    {
        _options = options.Value;
        _conversationInbound = conversationInbound;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Create([FromBody] DemoRequest request, CancellationToken ct)
    {
        var businessId = await ResolveDemoBusinessIdAsync(ct);
        if (businessId == Guid.Empty)
            return Problem("DemoRequests:BusinessId o DemoRequests:BusinessName no esta configurado.", statusCode: StatusCodes.Status500InternalServerError);

        var phone = NormalizeWhatsAppNumber(request.Phone);
        if (phone is null)
        {
            ModelState.AddModelError(nameof(request.Phone), "El WhatsApp debe incluir indicativo de pais o un celular colombiano valido.");
            return ValidationProblem(ModelState);
        }

        var email = request.Email.Trim();
        var customerName = NormalizeCustomerName(request.Name);
        var messageText = BuildInitialMessage(request, phone, email, customerName);

        var result = await _conversationInbound.EnqueueAsync(
            new ConversationInboundRequest(
                businessId,
                WhatsAppProvider,
                phone,
                messageText,
                customerName,
                Facts: BuildDemoFacts(request, email, customerName)),
            ct);

        _logger.LogInformation(
            "Demo request enqueued as inbound conversation message. BusinessId: {BusinessId}, ProviderMessageId: {ProviderMessageId}, IsNew: {IsNew}",
            businessId,
            result.ProviderMessageId,
            result.IsNew);

        return Accepted(new
        {
            message = "Solicitud de demo recibida. El agente iniciara la conversacion por WhatsApp.",
            providerMessageId = result.ProviderMessageId,
            queued = result.IsNew
        });
    }

    private async Task<Guid> ResolveDemoBusinessIdAsync(CancellationToken ct)
    {
        if (_options.BusinessId != Guid.Empty)
            return _options.BusinessId;

        if (string.IsNullOrWhiteSpace(_options.BusinessName))
            return Guid.Empty;

        var business = await _unitOfWork.Businesses.GetByNameAsync(_options.BusinessName, ct);
        return business?.BusinessId ?? Guid.Empty;
    }

    private static string BuildInitialMessage(DemoRequest request, string normalizedPhone, string email, string customerName) =>
        string.Join(Environment.NewLine,
            "Solicitud de demo desde formulario web.",
            $"Nombre: {customerName}",
            $"Empresa: {ValueOrDash(request.Company)}",
            $"Correo: {email}",
            $"WhatsApp: {normalizedPhone}");
    private static IReadOnlyDictionary<string, string> BuildDemoFacts(DemoRequest request, string email, string customerName) =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["customer_name"] = customerName,
            ["company_name"] = request.Company.Trim(),
            ["customer_email"] = email
        };

    private static string ValueOrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private static string NormalizeCustomerName(string value)
    {
        var normalized = Regex.Replace(value.Trim(), "\\s+", " ");
        var culture = CultureInfo.GetCultureInfo("es-CO");
        return culture.TextInfo.ToTitleCase(normalized.ToLower(culture));
    }


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

public sealed class DemoRequest
{
    [Required, MaxLength(60)]
    public string Phone { get; init; } = string.Empty;

    [Required, EmailAddress, MaxLength(200)]
    public string Email { get; init; } = string.Empty;

    [Required, MaxLength(120)]
    public string Name { get; init; } = string.Empty;

    [Required, MaxLength(160)]
    public string Company { get; init; } = string.Empty;
}
