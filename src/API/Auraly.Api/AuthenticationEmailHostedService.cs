using System.Data;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Azure;
using Azure.Communication.Email;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Sales;
using Auraly.Commerce.Taxation.Contracts;
using Auraly.Fiscal.Ubl;
using Auraly.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace Auraly.Api;

public sealed record PlatformEmailOptions(
    string? ConnectionString,
    string SenderAddress,
    string PublicAppUrl,
    string LogoUrl,
    string SupportEmail);

public sealed class PlatformEmailOutboxHostedService(
    SqlServerConnectionFactory connections,
    PlatformEmailOptions options,
    DianAttachedDocumentBuilder attachedDocuments,
    DianInvoicePdfRenderer invoicePdfs,
    DianSchemaValidator fiscalSchemaValidator,
    IFiscalXmlSigner fiscalXmlSigner,
    TimeProvider timeProvider,
    ILogger<PlatformEmailOutboxHostedService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly int[] RetrySeconds = [15, 60, 300, 900, 3600];
    private const int MaximumDianPackageBytes = 2 * 1024 * 1024;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            logger.LogWarning("Platform email delivery is disabled because Auraly:Email:ConnectionString is missing.");
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
                logger.LogError(exception, "Platform email outbox loop failed.");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private async Task DeliverAsync(EmailClient client, ClaimedMessage message, CancellationToken cancellationToken)
    {
        try
        {
            switch (message.Type)
            {
                case "TenantAdministratorInvitation":
                    await DeliverInvitationAsync(client, message, cancellationToken);
                    break;
                case "PasswordRecoveryEmail":
                    await DeliverPasswordRecoveryAsync(client, message, cancellationToken);
                    break;
                case "SubscriptionPaymentReminder":
                    await DeliverSubscriptionReminderAsync(client, message, cancellationToken);
                    break;
                case "FiscalInvoiceDelivery":
                    await DeliverFiscalInvoiceAsync(client, message, cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported authentication email type '{message.Type}'.");
            }
            await CompleteAsync(message, cancellationToken);
            logger.LogInformation("Platform email {Type}/{MessageId} delivered.", message.Type, message.MessageId);
        }
        catch (Exception exception)
        {
            await RetryAsync(message, exception, cancellationToken);
            logger.LogError(exception, "Platform email {Type}/{MessageId} failed.", message.Type, message.MessageId);
        }
    }

    private async Task DeliverInvitationAsync(EmailClient client, ClaimedMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<InvitationPayload>(message.Payload, Json)
            ?? throw new InvalidOperationException("The invitation payload is empty.");
        var recipient = await LoadRecipientAsync(payload, cancellationToken);
        var activationUrl = $"{options.PublicAppUrl.TrimEnd('/')}/activate?token={Uri.EscapeDataString(payload.ActivationToken)}";
        await SendAsync(client, payload.Email, $"Activa tu acceso a {recipient.TenantName} en Auraly", BuildHtml(recipient, activationUrl), BuildPlain(recipient, activationUrl), cancellationToken);
    }

    private async Task DeliverPasswordRecoveryAsync(EmailClient client, ClaimedMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<PasswordRecoveryPayload>(message.Payload, Json)
            ?? throw new InvalidOperationException("The password recovery payload is empty.");
        var recipient = await LoadPasswordRecoveryRecipientAsync(payload.RequestId, cancellationToken);
        var resetUrl = $"{options.PublicAppUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(payload.ResetToken)}";
        await SendAsync(client, recipient.Email, "Recupera tu acceso a Auraly", BuildPasswordRecoveryHtml(recipient, resetUrl), BuildPasswordRecoveryPlain(recipient, resetUrl), cancellationToken);
    }

    private async Task DeliverSubscriptionReminderAsync(
        EmailClient client, ClaimedMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SubscriptionReminderPayload>(message.Payload, Json)
            ?? throw new InvalidOperationException("The subscription reminder payload is empty.");
        var recipient = await LoadSubscriptionReminderAsync(
            payload.NotificationId, message.MessageId, message.TenantId, cancellationToken);
        if (recipient is null) return;
        var paymentUrl = $"{options.PublicAppUrl.TrimEnd('/')}/dashboard/subscription?order={recipient.RenewalOrderId:D}";
        await SendAsync(client, recipient.Email, recipient.Title,
            BuildSubscriptionReminderHtml(recipient, paymentUrl),
            BuildSubscriptionReminderPlain(recipient, paymentUrl), cancellationToken);
    }

    private async Task DeliverFiscalInvoiceAsync(
        EmailClient client, ClaimedMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<FiscalInvoicePayload>(message.Payload, Json)
            ?? throw new InvalidOperationException("The fiscal invoice payload is empty.");
        var invoice = await LoadFiscalInvoiceAsync(
            payload.DocumentId, message.MessageId, message.TenantId, cancellationToken);
        if (invoice is null)
            throw new InvalidOperationException(
                "The accepted fiscal invoice delivery package is not ready.");
        var metadata = attachedDocuments.ReadMetadata(invoice.SignedXml);
        var signedAttachedDocument = invoice.SignedAttachedDocument;
        var signedAttachedDocumentFileName = invoice.SignedAttachedDocumentFileName;
        byte[] container;
        string attachmentFileName;
        string subject;
        string html;
        string plain;
        if (invoice.IsHabilitation && signedAttachedDocument is null)
        {
            if (invoice.DianStatusResponse is not { Length: > 0 })
                throw new InvalidOperationException(
                    "The accepted DIAN habilitation response is not ready.");
            container = BuildHabilitationContainer(
                invoice.FiscalNumber, invoice.SignedXml, invoice.DianStatusResponse,
                invoice.IssuedAt);
            attachmentFileName = $"PruebaHabilitacion-{SafeFileName(invoice.FiscalNumber)}.zip";
            subject = $"[HABILITACIÓN DIAN] {BuildFiscalInvoiceSubject(metadata)}";
            html = BuildHabilitationInvoiceHtml(invoice, metadata);
            plain = BuildHabilitationInvoicePlain(invoice, metadata);
        }
        else
        {
            var deliveryArtifactsChanged = false;
            if (signedAttachedDocument is null)
            {
                if (invoice.ApplicationResponse is not { Length: > 0 })
                    throw new InvalidOperationException(
                        "The accepted fiscal invoice has no DIAN ApplicationResponse.");
                var built = attachedDocuments.Build(
                    invoice.SignedXml, invoice.ApplicationResponse, timeProvider.GetUtcNow());
                var unsignedValidation = fiscalSchemaValidator.Validate(built.Xml);
                if (!unsignedValidation.IsValid)
                    throw new InvalidOperationException(
                        $"The unsigned AttachedDocument is not schema-valid: {string.Join(" | ", unsignedValidation.Errors.Take(5))}");
                var signed = await fiscalXmlSigner.SignAsync(new FiscalSigningRequest(
                    invoice.BusinessId,
                    built.Metadata.SupplierTaxId,
                    built.Xml,
                    new FiscalCertificateReference(
                        invoice.BusinessId,
                        invoice.CertificateProvider,
                        invoice.CertificateKeyReference,
                        invoice.CertificateThumbprint),
                    timeProvider.GetUtcNow()), cancellationToken);
                var signedValidation = fiscalSchemaValidator.Validate(signed.SignedXml);
                if (!signedValidation.IsValid)
                    throw new InvalidOperationException(
                        $"The signed AttachedDocument is not schema-valid: {string.Join(" | ", signedValidation.Errors.Take(5))}");
                signedAttachedDocument = signed.SignedXml;
                signedAttachedDocumentFileName =
                    $"AttachedDocument-{SafeFileName(invoice.FiscalNumber)}.xml";
                deliveryArtifactsChanged = true;
            }
            var pdf = invoice.GraphicalRepresentationPdf;
            var pdfFileName = invoice.GraphicalRepresentationPdfFileName;
            if (pdf is null)
            {
                var fiscalReceipt = invoicePdfs.ReadReceipt(invoice.SignedXml);
                var receipt = ApplyInvoiceSettlement(fiscalReceipt with { DocumentId = invoice.DocumentId },
                    invoice.DocumentNumber, invoice.PaymentsJson, invoice.CreditAmount, invoice.WithholdingJson);
                pdf = await invoicePdfs.RenderAsync(receipt, cancellationToken);
                pdfFileName =
                    $"RepresentacionGrafica-{SafeFileName(invoice.FiscalNumber)}.pdf";
                deliveryArtifactsChanged = true;
            }
            if (deliveryArtifactsChanged)
                await SaveFiscalDeliveryArtifactsAsync(
                    invoice, message,
                    signedAttachedDocument,
                    System.Security.Cryptography.SHA256.HashData(signedAttachedDocument),
                    signedAttachedDocumentFileName ?? "AttachedDocument.xml",
                    pdf,
                    System.Security.Cryptography.SHA256.HashData(pdf),
                    pdfFileName ?? "RepresentacionGrafica.pdf",
                    cancellationToken);
            container = BuildFiscalContainer(
                signedAttachedDocumentFileName ?? "AttachedDocument.xml",
                signedAttachedDocument,
                pdfFileName ?? "RepresentacionGrafica.pdf",
                pdf,
                invoice.IssuedAt);
            attachmentFileName = $"FacturaElectronica-{SafeFileName(invoice.FiscalNumber)}.zip";
            subject = BuildFiscalInvoiceSubject(metadata);
            html = BuildFiscalInvoiceHtml(invoice, metadata);
            plain = BuildFiscalInvoicePlain(invoice, metadata);
        }
        if (container.Length > MaximumDianPackageBytes)
            throw new InvalidOperationException(
                $"The DIAN delivery package exceeds the 2 MB limit ({container.Length} bytes).");
        await SendAsync(client, invoice.Email, subject, html, plain, cancellationToken,
            [new(attachmentFileName,
                "application/zip", new BinaryData(container))]);
    }

    internal static OnlineSalesReceipt ApplyInvoiceSettlement(OnlineSalesReceipt fiscalReceipt,
        string documentNumber, string paymentsJson, decimal creditAmount, string? withholdingJson)
    {
        var payments = JsonSerializer.Deserialize<OnlineSalesPayment[]>(paymentsJson)
            ?? throw new InvalidOperationException("The invoice has no payment presentation data.");
        var withholding = withholdingJson is null ? null :
            JsonSerializer.Deserialize<WithholdingCalculationSnapshot>(withholdingJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var netAmount = withholding?.NetAmount ?? fiscalReceipt.PayableAmount;
        if (payments.Any(payment => payment.Amount <= 0) || creditAmount < 0 ||
            payments.Sum(payment => payment.Amount) + creditAmount != netAmount)
            throw new InvalidOperationException("The invoice payment detail does not match its payable amount.");
        return fiscalReceipt with
        {
            DocumentNumber = documentNumber,
            Payments = creditAmount > 0
                ? payments.Append(new OnlineSalesPayment("Credit", creditAmount, null)).ToArray()
                : payments,
            WithholdingTotal = withholding?.WithholdingTotal ?? 0m,
            NetPayableAmount = netAmount,
            Withholdings = withholding?.Lines
        };
    }

    private async Task SendAsync(EmailClient client, string recipient, string subject,
        string html, string plain, CancellationToken cancellationToken,
        IReadOnlyList<EmailAttachment>? attachments = null)
    {
        var email = BuildEmailMessage(
            options.SenderAddress, recipient, subject, html, plain, attachments);
        await client.SendAsync(WaitUntil.Completed, email, cancellationToken);
    }

    internal static EmailMessage BuildEmailMessage(
        string senderAddress,
        string recipient,
        string subject,
        string html,
        string plain,
        IReadOnlyList<EmailAttachment>? attachments = null)
    {
        var content = new EmailContent(subject) { Html = html, PlainText = plain };
        var email = new EmailMessage(senderAddress, recipient, content);
        if (attachments is not null)
            foreach (var attachment in attachments) email.Attachments.Add(attachment);
        return email;
    }
    private async Task<ClaimedMessage?> ClaimAsync(CancellationToken cancellationToken)
    {
        var leaseId = Guid.NewGuid();
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = Procedure("dbo.AuthenticationEmailOutboxClaim", connection);
        command.Parameters.AddWithValue("@LeaseId", leaseId);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ClaimedMessage(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetGuid(5))
            : null;
    }

    private async Task<RecipientContext> LoadRecipientAsync(InvitationPayload payload, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = Procedure("dbo.AuthenticationInvitationRecipientGet", connection);
        command.Parameters.AddWithValue("@TenantId", payload.TenantId);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The invitation recipient no longer exists.");
        return new RecipientContext(reader.GetString(0), "Administrador");
    }

    private async Task<PasswordRecoveryRecipient> LoadPasswordRecoveryRecipientAsync(Guid requestId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = Procedure("dbo.AuthenticationPasswordRecoveryRecipientGet", connection);
        command.Parameters.AddWithValue("@RequestId", requestId);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("The password recovery request is no longer available.");
        return new PasswordRecoveryRecipient(reader.GetString(0), reader.GetString(1), reader.GetString(2));
    }

    private async Task<SubscriptionReminderRecipient?> LoadSubscriptionReminderAsync(
        Guid notificationId, Guid messageId, Guid tenantId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = Procedure("dbo.TenantBillingReminderRecipientGet", connection);
        command.Parameters.AddWithValue("@NotificationId", notificationId);
        command.Parameters.AddWithValue("@MessageId", messageId);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetGuid(5), reader.GetFieldValue<DateTimeOffset>(6), reader.GetDecimal(7))
            : null;
    }

    private async Task<FiscalInvoiceRecipient?> LoadFiscalInvoiceAsync(
        Guid documentId, Guid messageId, Guid tenantId,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = Procedure("dbo.FiscalInvoiceDeliveryRecipientGet", connection);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@MessageId", messageId);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SingleRow, cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(documentId, reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetDecimal(5), (byte[])reader[6],
                reader.IsDBNull(7) ? null : (byte[])reader[7],
                reader.IsDBNull(8) ? null : (byte[])reader[8],
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : (byte[])reader[10],
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.GetString(12), reader.GetString(13), reader.GetString(14),
                !reader.IsDBNull(15), reader.IsDBNull(16) ? null : (byte[])reader[16],
                reader.GetString(17), reader.GetDecimal(18), reader.IsDBNull(19) ? null : reader.GetString(19))
            : null;
    }

    private async Task SaveFiscalDeliveryArtifactsAsync(
        FiscalInvoiceRecipient invoice,
        ClaimedMessage message,
        byte[] attachedDocument,
        byte[] attachedDocumentHash,
        string attachedDocumentFileName,
        byte[] graphicalRepresentation,
        byte[] graphicalRepresentationHash,
        string graphicalRepresentationFileName,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = Procedure(
            "dbo.FiscalInvoiceDeliveryArtifactSave", connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", invoice.DocumentId);
        command.Parameters.AddWithValue("@MessageId", message.MessageId);
        command.Parameters.AddWithValue("@TenantId", message.TenantId);
        command.Parameters.AddWithValue("@LeaseId", message.LeaseId);
        command.Parameters.AddWithValue("@AttachedDocument", attachedDocument);
        command.Parameters.AddWithValue("@AttachedDocumentHash", attachedDocumentHash);
        command.Parameters.AddWithValue("@AttachedDocumentFileName", attachedDocumentFileName);
        command.Parameters.AddWithValue("@GraphicalRepresentation", graphicalRepresentation);
        command.Parameters.AddWithValue("@GraphicalRepresentationHash", graphicalRepresentationHash);
        command.Parameters.AddWithValue("@GraphicalRepresentationFileName", graphicalRepresentationFileName);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
    private async Task CompleteAsync(ClaimedMessage message, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = Procedure(
            "dbo.AuthenticationEmailOutboxComplete", connection, transaction);
        command.Parameters.AddWithValue("@MessageId", message.MessageId);
        command.Parameters.AddWithValue("@LeaseId", message.LeaseId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task RetryAsync(ClaimedMessage message, Exception exception, CancellationToken cancellationToken)
    {
        var delay = RetrySeconds[Math.Min(message.AttemptCount - 1, RetrySeconds.Length - 1)];
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = Procedure("dbo.AuthenticationEmailOutboxRetry", connection);
        command.Parameters.AddWithValue("@Delay", delay);
        command.Parameters.AddWithValue("@Error", exception.Message.Length > 1900 ? exception.Message[..1900] : exception.Message);
        command.Parameters.AddWithValue("@MessageId", message.MessageId);
        command.Parameters.AddWithValue("@LeaseId", message.LeaseId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SqlCommand Procedure(string name, SqlConnection connection) =>
        new(name, connection) { CommandType = CommandType.StoredProcedure };

    private static SqlCommand Procedure(
        string name, SqlConnection connection, SqlTransaction transaction) =>
        new(name, connection, transaction) { CommandType = CommandType.StoredProcedure };

    private string BuildHtml(RecipientContext recipient, string activationUrl)
    {
        var tenant = WebUtility.HtmlEncode(recipient.TenantName);
        var name = WebUtility.HtmlEncode(recipient.Name);
        var url = WebUtility.HtmlEncode(activationUrl);
        var support = WebUtility.HtmlEncode(options.SupportEmail);
        var logo = WebUtility.HtmlEncode(options.LogoUrl);
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
                              <tr><td align="center" valign="middle"><img src="{{logo}}" width="34" height="34" alt="Auraly" style="display:block;border:0;width:34px;height:34px"></td></tr>
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

    private string BuildPasswordRecoveryHtml(PasswordRecoveryRecipient recipient, string resetUrl)
    {
        var name = WebUtility.HtmlEncode(recipient.Name);
        var tenant = WebUtility.HtmlEncode(recipient.TenantName);
        var url = WebUtility.HtmlEncode(resetUrl);
        var support = WebUtility.HtmlEncode(options.SupportEmail);
        var logo = WebUtility.HtmlEncode(options.LogoUrl);
        return $$"""
            <!doctype html><html lang="es"><body style="margin:0;background:#eef3f5;font-family:Inter,Segoe UI,Arial,sans-serif;color:#13202b">
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="padding:32px 12px"><tr><td align="center">
            <table role="presentation" width="600" cellspacing="0" cellpadding="0" style="max-width:600px;width:100%;background:#fff;border:1px solid #dce5e9;border-radius:22px;overflow:hidden">
            <tr><td style="height:7px;background:#14b8a6"></td></tr><tr><td style="padding:30px 32px">
            <img src="{{logo}}" width="44" height="44" alt="Auraly" style="display:block"><h1 style="margin:24px 0 12px;font-size:28px">Recupera tu acceso</h1>
            <p style="font-size:16px;line-height:1.6">Hola, <strong>{{name}}</strong>. Recibimos una solicitud para cambiar la contraseña de tu acceso a <strong>{{tenant}}</strong>.</p>
            <p style="margin:26px 0"><a href="{{url}}" style="display:inline-block;padding:14px 24px;border-radius:12px;background:#0f766e;color:#fff;text-decoration:none;font-weight:800">Crear nueva contraseña</a></p>
            <p style="padding:16px;border:1px solid #dce5e9;border-radius:14px;background:#f6f9fa;font-size:13px;line-height:1.55">Este enlace vence en 30 minutos y solo funciona una vez. Si no solicitaste el cambio, ignora este correo; tu contraseña actual seguirá vigente.</p>
            <p style="font-size:12px;color:#64748b">Si el botón no abre, copia: <a href="{{url}}">{{url}}</a></p>
            </td></tr><tr><td style="padding:20px 32px;border-top:1px solid #e2e8f0;color:#64748b;font-size:12px">Soporte: <a href="mailto:{{support}}">{{support}}</a></td></tr>
            </table></td></tr></table></body></html>
            """;
    }

    private string BuildPasswordRecoveryPlain(PasswordRecoveryRecipient recipient, string resetUrl) =>
        $"""
        Hola, {recipient.Name}.
        Recibimos una solicitud para cambiar la contraseña de tu acceso a {recipient.TenantName}.
        Crea una nueva contraseña aquí: {resetUrl}
        El enlace vence en 30 minutos y solo funciona una vez.
        Si no solicitaste el cambio, ignora este correo. Soporte: {options.SupportEmail}
        """;

    private string BuildSubscriptionReminderHtml(SubscriptionReminderRecipient recipient, string paymentUrl)
    {
        var name = WebUtility.HtmlEncode(recipient.Name);
        var tenant = WebUtility.HtmlEncode(recipient.TenantName);
        var title = WebUtility.HtmlEncode(recipient.Title);
        var message = WebUtility.HtmlEncode(recipient.Message);
        var url = WebUtility.HtmlEncode(paymentUrl);
        var amount = WebUtility.HtmlEncode(recipient.Amount.ToString("C0", new System.Globalization.CultureInfo("es-CO")));
        var due = WebUtility.HtmlEncode(recipient.DueAt.ToString("dd/MM/yyyy"));
        var support = WebUtility.HtmlEncode(options.SupportEmail);
        var logo = WebUtility.HtmlEncode(options.LogoUrl);
        return $$"""
            <!doctype html><html lang="es"><body style="margin:0;background:#eef3f5;font-family:Inter,Segoe UI,Arial,sans-serif;color:#13202b">
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="padding:32px 12px"><tr><td align="center">
            <table role="presentation" width="600" cellspacing="0" cellpadding="0" style="max-width:600px;width:100%;background:#fff;border:1px solid #dce5e9;border-radius:22px;overflow:hidden">
            <tr><td style="height:7px;background:#14b8a6"></td></tr><tr><td style="padding:30px 32px">
            <img src="{{logo}}" width="44" height="44" alt="Auraly" style="display:block"><h1 style="margin:24px 0 12px;font-size:28px">{{title}}</h1>
            <p style="font-size:16px;line-height:1.6">Hola, <strong>{{name}}</strong>. {{message}}</p>
            <table role="presentation" width="100%" style="margin:20px 0;background:#f6f9fa;border:1px solid #dce5e9;border-radius:14px"><tr><td style="padding:16px;line-height:1.7"><strong>{{tenant}}</strong><br>Total: <strong>{{amount}}</strong><br>Vencimiento: {{due}}</td></tr></table>
            <p style="margin:26px 0"><a href="{{url}}" style="display:inline-block;padding:14px 24px;border-radius:12px;background:#0f766e;color:#fff;text-decoration:none;font-weight:800">Revisar y pagar en Auraly</a></p>
            <p style="font-size:12px;color:#64748b">Por seguridad, Auraly validará nuevamente tu organización, el estado y el valor antes de abrir Wompi.</p>
            </td></tr><tr><td style="padding:20px 32px;border-top:1px solid #e2e8f0;color:#64748b;font-size:12px">Soporte: <a href="mailto:{{support}}">{{support}}</a></td></tr>
            </table></td></tr></table></body></html>
            """;
    }

    private static string BuildSubscriptionReminderPlain(
        SubscriptionReminderRecipient recipient, string paymentUrl) =>
        $"""
        Hola, {recipient.Name}.
        {recipient.Title}: {recipient.Message}
        Organización: {recipient.TenantName}
        Total: {recipient.Amount:C0} COP. Vencimiento: {recipient.DueAt:dd/MM/yyyy}.
        Revisa y paga de forma segura en Auraly: {paymentUrl}
        """;

    internal static byte[] BuildFiscalContainer(
        string fileName,
        byte[] signedAttachedDocument,
        string pdfFileName,
        byte[] graphicalRepresentation,
        DateTimeOffset issuedAt)
    {
        if (graphicalRepresentation.Length == 0 ||
            !graphicalRepresentation.AsSpan().StartsWith("%PDF-"u8))
            throw new ArgumentException(
                "The graphical representation must be a PDF.",
                nameof(graphicalRepresentation));
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, SafeFileName(fileName), signedAttachedDocument, issuedAt);
            WriteEntry(archive, SafeFileName(pdfFileName), graphicalRepresentation, issuedAt);
        }
        return output.ToArray();
    }

    internal static byte[] BuildHabilitationContainer(
        string fiscalNumber,
        byte[] signedXml,
        byte[] dianStatusResponse,
        DateTimeOffset issuedAt)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var safeNumber = SafeFileName(fiscalNumber);
            WriteEntry(archive, $"{safeNumber}-signed.xml", signedXml, issuedAt);
            WriteEntry(archive, $"{safeNumber}-dian-test-set-response.json",
                dianStatusResponse, issuedAt);
        }
        return output.ToArray();
    }

    private static void WriteEntry(
        ZipArchive archive,
        string fileName,
        byte[] content,
        DateTimeOffset issuedAt)
    {
        var entry = archive.CreateEntry(fileName, CompressionLevel.Optimal);
        entry.LastWriteTime = issuedAt.Year < 1980
            ? new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero)
            : issuedAt.Year > 2107
                ? new DateTimeOffset(2107, 12, 31, 23, 59, 58, TimeSpan.Zero)
                : issuedAt;
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static string SafeFileName(string value)
    {
        var result = string.Concat(value.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        return string.IsNullOrWhiteSpace(result) ? "documento" : result;
    }

    internal static string BuildFiscalInvoiceSubject(DianAttachedDocumentMetadata metadata) =>
        string.Join(';',
            SubjectField(metadata.SupplierTaxId),
            SubjectField(metadata.SupplierLegalName),
            SubjectField(metadata.FiscalNumber),
            SubjectField(metadata.DocumentTypeCode),
            SubjectField(metadata.SupplierTradeName));

    private static string SubjectField(string value) =>
        value.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ').Trim();

    private string BuildHabilitationInvoiceHtml(
        FiscalInvoiceRecipient invoice,
        DianAttachedDocumentMetadata metadata)
    {
        var business = WebUtility.HtmlEncode(metadata.SupplierLegalName);
        var customer = WebUtility.HtmlEncode(metadata.CustomerName);
        var fiscal = WebUtility.HtmlEncode(invoice.FiscalNumber);
        return $$"""
            <!doctype html><html lang="es"><body style="margin:0;background:#eef3f5;font-family:Inter,Segoe UI,Arial,sans-serif;color:#13202b">
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="padding:32px 12px"><tr><td align="center">
            <table role="presentation" width="620" cellspacing="0" cellpadding="0" style="max-width:620px;width:100%;background:#fff;border:1px solid #dce5e9;border-radius:22px;overflow:hidden">
            <tr><td style="height:7px;background:#7c3aed"></td></tr><tr><td style="padding:30px 32px">
            <p style="margin:0 0 5px;color:#6d28d9;font-size:12px;font-weight:800;letter-spacing:.12em;text-transform:uppercase">Prueba de habilitación DIAN</p>
            <h1 style="margin:0 0 14px;font-size:28px">El envío electrónico de prueba está listo</h1>
            <p style="font-size:16px;line-height:1.6">Hola, <strong>{{customer}}</strong>. <strong>{{business}}</strong> emitió el documento de habilitación <strong>{{fiscal}}</strong>.</p>
            <p style="padding:14px;border-radius:12px;background:#f5f3ff;border:1px solid #ddd6fe;font-size:13px;line-height:1.6">Este documento pertenece al ambiente de pruebas. El ZIP contiene el XML firmado y la respuesta de aceptación del set DIAN; no es un soporte fiscal de producción.</p>
            </td></tr><tr><td style="padding:20px 32px;border-top:1px solid #e2e8f0;color:#64748b;font-size:12px">Soporte: <a href="mailto:{{WebUtility.HtmlEncode(options.SupportEmail)}}">{{WebUtility.HtmlEncode(options.SupportEmail)}}</a> · Enviado por Auraly.</td></tr>
            </table></td></tr></table></body></html>
            """;
    }

    private string BuildHabilitationInvoicePlain(
        FiscalInvoiceRecipient invoice,
        DianAttachedDocumentMetadata metadata) =>
        $"""
        Prueba de habilitación DIAN de {metadata.SupplierLegalName} para {metadata.CustomerName}.
        Documento: {invoice.FiscalNumber}.
        El ZIP contiene el XML firmado y la respuesta de aceptación del set DIAN.
        No es un soporte fiscal de producción.
        Soporte: {options.SupportEmail}
        """;

    private string BuildFiscalInvoiceHtml(
        FiscalInvoiceRecipient invoice,
        DianAttachedDocumentMetadata metadata)
    {
        var business = WebUtility.HtmlEncode(metadata.SupplierLegalName);
        var customer = WebUtility.HtmlEncode(metadata.CustomerName);
        var document = WebUtility.HtmlEncode(invoice.DocumentNumber);
        var fiscal = WebUtility.HtmlEncode(invoice.FiscalNumber);
        var amount = WebUtility.HtmlEncode(invoice.Amount.ToString(
            "C0", new System.Globalization.CultureInfo("es-CO")));
        var support = WebUtility.HtmlEncode(options.SupportEmail);
        var logo = WebUtility.HtmlEncode(options.LogoUrl);
        return $$"""
            <!doctype html><html lang="es"><body style="margin:0;background:#eef3f5;font-family:Inter,Segoe UI,Arial,sans-serif;color:#13202b">
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="padding:32px 12px"><tr><td align="center">
            <table role="presentation" width="620" cellspacing="0" cellpadding="0" style="max-width:620px;width:100%;background:#fff;border:1px solid #dce5e9;border-radius:22px;overflow:hidden">
            <tr><td style="height:7px;background:#14b8a6"></td></tr><tr><td style="padding:30px 32px">
            <img src="{{logo}}" width="44" height="44" alt="Auraly" style="display:block"><p style="margin:20px 0 5px;color:#0f766e;font-size:12px;font-weight:800;letter-spacing:.12em;text-transform:uppercase">Documento validado por la DIAN</p>
            <h1 style="margin:0 0 14px;font-size:28px">Tu factura electrónica está lista</h1>
            <p style="font-size:16px;line-height:1.6">Hola, <strong>{{customer}}</strong>. <strong>{{business}}</strong> emitió la factura electrónica de venta que encontrarás adjunta a este correo.</p>
            <table role="presentation" width="100%" style="margin:22px 0;background:#f0fdfa;border:1px solid #99f6e4;border-radius:14px"><tr><td style="padding:17px;line-height:1.8">Documento Auraly: <strong>{{document}}</strong><br>Número DIAN: <strong>{{fiscal}}</strong><br>Fecha: {{invoice.IssuedAt:dd/MM/yyyy HH:mm}}<br>Total: <strong>{{amount}}</strong></td></tr></table>
            <p style="font-size:14px;line-height:1.6;color:#526170">El archivo ZIP adjunto contiene el AttachedDocument firmado con la factura XML, la respuesta electrónica de validación de la DIAN y la representación gráfica en PDF. Guárdalo como soporte del documento.</p>
            <p style="font-size:12px;line-height:1.6;color:#64748b">Correo autorrespuesta: {{support}}</p>
            <p style="padding:14px;border-radius:12px;background:#f6f9fa;border:1px solid #dce5e9;font-size:12px;color:#64748b">Este mensaje es informativo. No respondas con claves, contraseñas ni datos de pago.</p>
            </td></tr><tr><td style="padding:20px 32px;border-top:1px solid #e2e8f0;color:#64748b;font-size:12px">Soporte: <a href="mailto:{{support}}">{{support}}</a> · Enviado de forma segura por Auraly.</td></tr>
            </table></td></tr></table></body></html>
            """;
    }

    private string BuildFiscalInvoicePlain(
        FiscalInvoiceRecipient invoice,
        DianAttachedDocumentMetadata metadata) =>
        $"""
        Hola, {metadata.CustomerName}.
        {metadata.SupplierLegalName} emitió la factura electrónica {invoice.FiscalNumber}.
        Documento Auraly: {invoice.DocumentNumber}. Fecha: {invoice.IssuedAt:dd/MM/yyyy HH:mm}.
        Total: {invoice.Amount:C0} COP.
        El ZIP adjunto contiene el AttachedDocument firmado con la factura XML, la respuesta de validación de la DIAN y la representación gráfica en PDF.
        Correo autorrespuesta: {options.SupportEmail}
        """;

    private string BuildPlain(RecipientContext recipient, string activationUrl) =>
        $"""
        Hola, {recipient.Name}.

        Tu organización {recipient.TenantName} ya está lista en Auraly.
        Completa tus datos de administrador y define tu contraseña aquí:
        {activationUrl}

        El enlace vence en 48 horas y solo puede utilizarse una vez.
        Soporte: {options.SupportEmail}
        """;

    private sealed record ClaimedMessage(Guid MessageId, Guid TenantId, string Type, string Payload, int AttemptCount, Guid LeaseId);
    private sealed record InvitationPayload(Guid InvitationId, Guid TenantId, string Email, string ActivationToken);
    private sealed record PasswordRecoveryPayload(Guid RequestId, string Email, string ResetToken);
    private sealed record SubscriptionReminderPayload(Guid NotificationId);
    private sealed record FiscalInvoicePayload(Guid DocumentId);
    private sealed record PasswordRecoveryRecipient(string Email, string Name, string TenantName);
    private sealed record SubscriptionReminderRecipient(
        string Email, string Name, string TenantName, string Title, string Message,
        Guid RenewalOrderId, DateTimeOffset DueAt, decimal Amount);
    private sealed record FiscalInvoiceRecipient(
        Guid DocumentId,
        Guid BusinessId,
        string Email,
        string DocumentNumber,
        string FiscalNumber,
        DateTimeOffset IssuedAt,
        decimal Amount,
        byte[] SignedXml,
        byte[]? ApplicationResponse,
        byte[]? SignedAttachedDocument,
        string? SignedAttachedDocumentFileName,
        byte[]? GraphicalRepresentationPdf,
        string? GraphicalRepresentationPdfFileName,
        string CertificateProvider,
        string CertificateKeyReference,
        string CertificateThumbprint,
        bool IsHabilitation,
        byte[]? DianStatusResponse,
        string PaymentsJson,
        decimal CreditAmount,
        string? WithholdingJson);
    private sealed record RecipientContext(string TenantName, string Name);
}
