using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MimosBabySpa.WebAPI.Configuration;

namespace MimosBabySpa.WebAPI.Controllers;

[ApiController]
[Route("api/demo-requests")]
public sealed class DemoRequestsController : ControllerBase
{
    private readonly DemoRequestOptions _options;
    private readonly ILogger<DemoRequestsController> _logger;

    public DemoRequestsController(
        IOptions<DemoRequestOptions> options,
        ILogger<DemoRequestsController> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Create([FromBody] DemoRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.RecipientEmail))
            return Problem("DemoRequests:RecipientEmail no esta configurado.", statusCode: StatusCodes.Status500InternalServerError);

        if (string.IsNullOrWhiteSpace(_options.Smtp.Host) ||
            string.IsNullOrWhiteSpace(_options.Smtp.FromEmail))
        {
            _logger.LogWarning(
                "Demo request received but SMTP is not configured. Recipient: {RecipientEmail}. Request: {@Request}",
                _options.RecipientEmail,
                request);

            return Problem("El correo de demos no esta configurado todavia.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        using var message = BuildMessage(request);
        using var client = new SmtpClient(_options.Smtp.Host, _options.Smtp.Port)
        {
            EnableSsl = _options.Smtp.EnableSsl
        };

        if (!string.IsNullOrWhiteSpace(_options.Smtp.Username))
            client.Credentials = new NetworkCredential(_options.Smtp.Username, _options.Smtp.Password);

        await client.SendMailAsync(message, ct);

        return Accepted(new { message = "Solicitud de demo enviada." });
    }

    private MailMessage BuildMessage(DemoRequest request)
    {
        var fromName = string.IsNullOrWhiteSpace(_options.Smtp.FromName)
            ? "AURALY"
            : _options.Smtp.FromName.Trim();

        var message = new MailMessage
        {
            From = new MailAddress(_options.Smtp.FromEmail.Trim(), fromName),
            Subject = $"Nueva demo AURALY - {request.Email.Trim()}",
            Body = BuildBody(request),
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };

        message.To.Add(_options.RecipientEmail.Trim());
        message.ReplyToList.Add(new MailAddress(request.Email.Trim()));

        return message;
    }

    private static string BuildBody(DemoRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Nueva solicitud de demo desde la landing de AURALY");
        builder.AppendLine();
        builder.AppendLine($"Email: {request.Email.Trim()}");
        builder.AppendLine($"Nombre: {ValueOrDash(request.Name)}");
        builder.AppendLine($"Empresa: {ValueOrDash(request.Company)}");
        builder.AppendLine($"WhatsApp: {ValueOrDash(request.Phone)}");
        builder.AppendLine();
        builder.AppendLine("Mensaje:");
        builder.AppendLine(ValueOrDash(request.Message));
        return builder.ToString();
    }

    private static string ValueOrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}

public sealed record DemoRequest(
    [property: EmailAddress, Required, MaxLength(200)] string Email,
    [property: MaxLength(120)] string? Name,
    [property: MaxLength(160)] string? Company,
    [property: MaxLength(60)] string? Phone,
    [property: MaxLength(1000)] string? Message);
