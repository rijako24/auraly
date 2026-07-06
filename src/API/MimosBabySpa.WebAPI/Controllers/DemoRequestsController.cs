using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.WebAPI.Configuration;

namespace MimosBabySpa.WebAPI.Controllers;

[ApiController]
[Route("api/demo-requests")]
public sealed class DemoRequestsController : ControllerBase
{
    private const string DefaultTemplateSequenceName = "web_demo_follow_up";

    private readonly DemoRequestOptions _options;
    private readonly IActiveAgentConfigResolver _activeAgentConfigResolver;
    private readonly IMessageSequenceResolver _messageSequenceResolver;
    private readonly IOutboundMessageDispatcher _outboundDispatcher;
    private readonly IConversationService _conversationService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DemoRequestsController> _logger;

    public DemoRequestsController(
        IOptions<DemoRequestOptions> options,
        IActiveAgentConfigResolver activeAgentConfigResolver,
        IMessageSequenceResolver messageSequenceResolver,
        IOutboundMessageDispatcher outboundDispatcher,
        IConversationService conversationService,
        IUnitOfWork unitOfWork,
        ILogger<DemoRequestsController> logger)
    {
        _options = options.Value;
        _activeAgentConfigResolver = activeAgentConfigResolver;
        _messageSequenceResolver = messageSequenceResolver;
        _outboundDispatcher = outboundDispatcher;
        _conversationService = conversationService;
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

        var config = await _activeAgentConfigResolver.GetActiveConfigAsync(businessId, ct);
        if (config is null)
            return Problem("No hay agente activo configurado para solicitudes de demo.", statusCode: StatusCodes.Status500InternalServerError);

        var sequenceName = string.IsNullOrWhiteSpace(_options.TemplateSequenceName)
            ? DefaultTemplateSequenceName
            : _options.TemplateSequenceName.Trim();

        var messages = await _messageSequenceResolver.ResolveAsync(
            businessId,
            sequenceName,
            config.MessageSequences,
            BuildSequenceContext(request, phone),
            ct);

        if (messages.Count == 0)
            return Problem($"La secuencia de demo '{sequenceName}' no esta configurada o no genero mensajes.", statusCode: StatusCodes.Status500InternalServerError);

        var conversation = await _conversationService.GetOrCreateConversationAsync(
            businessId,
            phone,
            request.Name);

        await _outboundDispatcher.SendAllAsync(
            businessId,
            phone,
            messages,
            conversation.ConversationId,
            throwOnFailure: true,
            ct: ct);

        _logger.LogInformation(
            "Demo request sent through WhatsApp template sequence {Sequence} for {Phone}",
            sequenceName,
            phone);

        return Accepted(new
        {
            message = "Solicitud de demo recibida. El agente iniciara la conversacion por WhatsApp.",
            sequence = sequenceName
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

    private static MessageSequenceContext BuildSequenceContext(DemoRequest request, string normalizedPhone) =>
        new()
        {
            Custom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["CustomerName"] = ValueOrDash(request.Name),
                ["CompanyName"] = ValueOrDash(request.Company),
                ["Email"] = ValueOrDash(request.Email),
                ["Phone"] = normalizedPhone
            }
        };

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

public sealed class DemoRequest
{
    [Required, MaxLength(60)]
    public string Phone { get; init; } = string.Empty;

    [EmailAddress, MaxLength(200)]
    public string? Email { get; init; }

    [MaxLength(120)]
    public string? Name { get; init; }

    [MaxLength(160)]
    public string? Company { get; init; }
}
