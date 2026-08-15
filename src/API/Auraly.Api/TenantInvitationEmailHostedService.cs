using System.Data;
using System.Net;
using System.Text.Json;
using Azure;
using Azure.Communication.Email;
using Auraly.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace Auraly.Api;

public sealed record TenantInvitationEmailOptions(
    string? ConnectionString,
    string SenderAddress,
    string PublicAppUrl,
    string LogoUrl,
    string SupportEmail);

public sealed class TenantInvitationEmailHostedService(
    SqlServerConnectionFactory connections,
    TenantInvitationEmailOptions options,
    ILogger<TenantInvitationEmailHostedService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly int[] RetrySeconds = [15, 60, 300, 900, 3600];
    private static readonly Lazy<BinaryData> LogoContent = new(LoadLogoContent);
    private const string LogoContentId = "auraly-logo";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            logger.LogWarning("Tenant invitation email delivery is disabled because Auraly:Email:ConnectionString is missing.");
            return;
        }

        var client = new EmailClient(options.ConnectionString);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var message = await ClaimAsync(stoppingToken);
                if (message is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    continue;
                }

                await DeliverAsync(client, message, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Tenant invitation outbox loop failed.");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private async Task DeliverAsync(EmailClient client, ClaimedMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<InvitationPayload>(message.Payload, Json)
                ?? throw new InvalidOperationException("The invitation payload is empty.");
            var recipient = await LoadRecipientAsync(payload, cancellationToken);
            var activationUrl = $"{options.PublicAppUrl.TrimEnd('/')}/activate?token={Uri.EscapeDataString(payload.ActivationToken)}";
            var subject = $"Activa tu acceso a {recipient.TenantName} en Auraly";
            var html = BuildHtml(recipient, activationUrl);
            var plain = BuildPlain(recipient, activationUrl);

            var content = new EmailContent(subject)
            {
                Html = html,
                PlainText = plain
            };
            var email = new EmailMessage(options.SenderAddress, payload.Email, content);
            email.Attachments.Add(new EmailAttachment(
                "auraly-mark.png",
                "image/png",
                LogoContent.Value)
            {
                ContentId = LogoContentId
            });

            await client.SendAsync(WaitUntil.Completed, email, cancellationToken);

            await CompleteAsync(message, cancellationToken);
            logger.LogInformation("Tenant administrator invitation {MessageId} delivered.", message.MessageId);
        }
        catch (Exception exception)
        {
            await RetryAsync(message, exception, cancellationToken);
            logger.LogError(exception, "Tenant administrator invitation {MessageId} failed.", message.MessageId);
        }
    }

    private async Task<ClaimedMessage?> ClaimAsync(CancellationToken cancellationToken)
    {
        var leaseId = Guid.NewGuid();
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            DECLARE @Now datetimeoffset(7)=SYSDATETIMEOFFSET();
            ;WITH Candidate AS (
              SELECT TOP(1) *
              FROM dbo.TenantProvisioningOutboxMessages WITH(UPDLOCK,READPAST,ROWLOCK)
              WHERE ProcessedAt IS NULL AND Type=N'TenantAdministratorInvitation'
                AND AttemptCount<10 AND AvailableAt<=@Now
                AND (LeaseExpiresAt IS NULL OR LeaseExpiresAt<=@Now)
              ORDER BY OccurredAt,MessageId
            )
            UPDATE Candidate
            SET LeaseId=@LeaseId,LeaseExpiresAt=DATEADD(minute,2,@Now),
                AttemptCount=AttemptCount+1,LastError=NULL
            OUTPUT inserted.MessageId,inserted.TenantId,inserted.Payload,
                   inserted.AttemptCount,inserted.LeaseId;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@LeaseId", leaseId);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ClaimedMessage(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetInt32(3), reader.GetGuid(4))
            : null;
    }

    private async Task<RecipientContext> LoadRecipientAsync(InvitationPayload payload, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            SELECT t.Name
            FROM dbo.Tenants t
            WHERE t.TenantId=@TenantId;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TenantId", payload.TenantId);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The invitation recipient no longer exists.");
        return new RecipientContext(reader.GetString(0), "Administrador");
    }

    private async Task CompleteAsync(ClaimedMessage message, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            UPDATE dbo.TenantProvisioningOutboxMessages
            SET ProcessedAt=SYSDATETIMEOFFSET(),LeaseId=NULL,LeaseExpiresAt=NULL,LastError=NULL
            WHERE MessageId=@MessageId AND LeaseId=@LeaseId AND ProcessedAt IS NULL;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@MessageId", message.MessageId);
        command.Parameters.AddWithValue("@LeaseId", message.LeaseId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task RetryAsync(ClaimedMessage message, Exception exception, CancellationToken cancellationToken)
    {
        var delay = RetrySeconds[Math.Min(message.AttemptCount - 1, RetrySeconds.Length - 1)];
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            UPDATE dbo.TenantProvisioningOutboxMessages
            SET AvailableAt=DATEADD(second,@Delay,SYSDATETIMEOFFSET()),
                LeaseId=NULL,LeaseExpiresAt=NULL,LastError=@Error
            WHERE MessageId=@MessageId AND LeaseId=@LeaseId AND ProcessedAt IS NULL;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Delay", delay);
        command.Parameters.AddWithValue("@Error", exception.Message.Length > 1900 ? exception.Message[..1900] : exception.Message);
        command.Parameters.AddWithValue("@MessageId", message.MessageId);
        command.Parameters.AddWithValue("@LeaseId", message.LeaseId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private string BuildHtml(RecipientContext recipient, string activationUrl)
    {
        var tenant = WebUtility.HtmlEncode(recipient.TenantName);
        var name = WebUtility.HtmlEncode(recipient.Name);
        var url = WebUtility.HtmlEncode(activationUrl);
        var support = WebUtility.HtmlEncode(options.SupportEmail);
        return $$"""
            <!doctype html>
            <html lang="es">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width">
              <meta name="color-scheme" content="light dark">
              <meta name="supported-color-schemes" content="light dark">
              <style>
                :root { color-scheme: light dark; supported-color-schemes: light dark; }
                @media (prefers-color-scheme: dark) {
                  .email-page { background-color:#0b1117 !important; }
                  .email-card { background-color:#171c23 !important; border-color:#2d3748 !important; }
                  .primary-copy { color:#f8fafc !important; }
                  .secondary-copy { color:#cbd5e1 !important; }
                  .notice { background-color:#111827 !important; border-color:#334155 !important; color:#cbd5e1 !important; }
                  .footer { border-color:#334155 !important; color:#94a3b8 !important; }
                }
              </style>
            </head>
            <body class="email-page" style="margin:0;background-color:#eef3f5;font-family:Inter,Segoe UI,Arial,sans-serif;color:#13202b">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" class="email-page" bgcolor="#eef3f5" style="background-color:#eef3f5;padding:32px 12px">
                <tr><td align="center">
                  <table role="presentation" width="600" cellspacing="0" cellpadding="0" class="email-card" bgcolor="#ffffff" style="max-width:600px;width:100%;background-color:#ffffff;border:1px solid #dce5e9;border-radius:22px;overflow:hidden">
                    <tr><td bgcolor="#14b8a6" style="height:7px;line-height:7px;background-color:#14b8a6;font-size:1px">&nbsp;</td></tr>
                    <tr><td style="padding:28px 32px 18px">
                      <table role="presentation" width="100%" cellspacing="0" cellpadding="0">
                        <tr>
                          <td width="56" valign="middle">
                            <table role="presentation" width="48" height="48" cellspacing="0" cellpadding="0" bgcolor="#ccfbf1" style="width:48px;height:48px;background-color:#ccfbf1;border-radius:13px">
                              <tr><td align="center" valign="middle"><img src="cid:{{LogoContentId}}" width="34" height="34" alt="Auraly" style="display:block;border:0;width:34px;height:34px"></td></tr>
                            </table>
                          </td>
                          <td valign="middle" style="padding-left:12px">
                            <div style="color:#0f766e;font-size:12px;font-weight:800;letter-spacing:.18em;text-transform:uppercase">Auraly</div>
                            <div class="secondary-copy" style="margin-top:4px;color:#64748b;font-size:13px">Tu organización está lista</div>
                          </td>
                        </tr>
                      </table>
                      <h1 class="primary-copy" style="margin:26px 0 0;color:#13202b;font-size:30px;line-height:1.18;letter-spacing:-.02em">Bienvenido a Auraly</h1>
                    </td></tr>
                    <tr><td style="padding:8px 32px 32px">
                      <p class="primary-copy" style="margin:0 0 16px;color:#13202b;font-size:16px;line-height:1.55">Hola, <strong>{{name}}</strong>.</p>
                      <p class="secondary-copy" style="margin:0 0 24px;color:#526170;font-size:16px;line-height:1.65">La organización <strong>{{tenant}}</strong> ya está preparada. Completa tus datos y define tu contraseña para crear de forma segura tu acceso de administrador.</p>
                      <table role="presentation" cellspacing="0" cellpadding="0"><tr><td bgcolor="#0f766e" style="border-radius:12px;background-color:#0f766e">
                        <a href="{{url}}" style="display:inline-block;padding:14px 24px;color:#ffffff!important;-webkit-text-fill-color:#ffffff;text-decoration:none;font-size:15px;font-weight:800">Completar mi registro</a>
                      </td></tr></table>
                      <table role="presentation" width="100%" cellspacing="0" cellpadding="0" class="notice" bgcolor="#f6f9fa" style="margin-top:24px;background-color:#f6f9fa;border:1px solid #dce5e9;border-radius:14px">
                        <tr><td class="secondary-copy" style="padding:16px;color:#526170;font-size:13px;line-height:1.55">Este enlace vence en 48 horas y solo puede utilizarse una vez. Auraly nunca te pedirá compartir tu contraseña.</td></tr>
                      </table>
                      <p class="secondary-copy" style="margin:24px 0 8px;color:#64748b;font-size:12px">Si el botón no abre, copia esta dirección:</p>
                      <p style="margin:0;word-break:break-all;font-size:12px"><a href="{{url}}" style="color:#0f766e">{{url}}</a></p>
                    </td></tr>
                    <tr><td class="footer" style="padding:20px 32px;border-top:1px solid #e2e8f0;color:#64748b;font-size:12px;line-height:1.55">
                      ¿Necesitas ayuda? Escríbenos a <a href="mailto:{{support}}" style="color:#0f766e">{{support}}</a><br>
                      © {{DateTime.UtcNow.Year}} Auraly · Operaciones conectadas, decisiones claras.
                    </td></tr>
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;
    }

    private static BinaryData LoadLogoContent()
    {
        using var stream = typeof(TenantInvitationEmailHostedService).Assembly
            .GetManifestResourceStream("Auraly.Api.Assets.auraly-mark.png")
            ?? throw new InvalidOperationException("The embedded Auraly email logo is missing.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return new BinaryData(memory.ToArray());
    }
    private string BuildPlain(RecipientContext recipient, string activationUrl) =>
        $"""
        Hola, {recipient.Name}.

        Tu organización {recipient.TenantName} ya está lista en Auraly.
        Completa tus datos de administrador y define tu contraseña aquí:
        {activationUrl}

        El enlace vence en 48 horas y solo puede utilizarse una vez.
        Soporte: {options.SupportEmail}
        """;

    private sealed record ClaimedMessage(Guid MessageId, Guid TenantId, string Payload, int AttemptCount, Guid LeaseId);
    private sealed record InvitationPayload(Guid InvitationId, Guid TenantId, string Email, string ActivationToken);
    private sealed record RecipientContext(string TenantName, string Name);
}
