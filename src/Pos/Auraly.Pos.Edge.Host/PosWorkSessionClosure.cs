using System.Globalization;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Auraly.Contracts.WorkSessions;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Pos.Edge.Host;

public sealed record PreviewLocalWorkSessionClosureRequest(Guid DraftId);

public sealed record CloseLocalWorkSessionRequest(
    Guid OperationId,
    Guid AuthorizationToken,
    decimal CountedCash,
    IReadOnlyList<WorkSessionPaymentCount> PaymentCounts,
    string? Note);

public sealed record AuthorizedWorkSessionClosurePreview(
    Guid AuthorizationToken,
    WorkSessionClosurePreviewView Preview);

public sealed class PosWorkSessionClosureServerClient(
    HttpClient http,
    PosDeviceCredentials credentials)
{
    public async Task<WorkSessionClosurePreviewView> PreviewAsync(
        PosLocalUserSession session,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/pos/v1/work-sessions/{session.WorkSessionId:D}/closure-preview?userId={session.UserId:D}");
        request.Headers.Add("X-Auraly-Device-Id", credentials.DeviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", credentials.Secret);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new PosWorkSessionClosureException(
                (int)response.StatusCode,
                await response.Content.ReadAsStringAsync(cancellationToken));
        return await response.Content.ReadFromJsonAsync<WorkSessionClosurePreviewView>(
                   cancellationToken: cancellationToken)
               ?? throw new InvalidDataException(
                   "Auraly Server devolvió una vista previa de cierre vacía.");
    }

    public async Task<WorkSessionClosureView> CloseAsync(
        PosLocalUserSession session,
        CloseLocalWorkSessionRequest input,
        Guid authorizedByUserId,
        CancellationToken cancellationToken)
    {
        return await CloseAsync(
            new DeviceCloseWorkSessionRequest(
                session.UserId,
                session.WorkSessionId,
                input.CountedCash,
                input.Note,
                authorizedByUserId,
                input.PaymentCounts),
            input.OperationId,
            cancellationToken);
    }

    public async Task<WorkSessionClosureView> CloseAsync(
        DeviceCloseWorkSessionRequest input,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/pos/v1/work-sessions/{input.WorkSessionId:D}/close");
        request.Headers.Add("X-Auraly-Device-Id", credentials.DeviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", credentials.Secret);
        request.Headers.Add("Idempotency-Key", operationId.ToString("D"));
        request.Content = JsonContent.Create(input);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new PosWorkSessionClosureException(
                (int)response.StatusCode,
                await response.Content.ReadAsStringAsync(cancellationToken));
        return await response.Content.ReadFromJsonAsync<WorkSessionClosureView>(
                   cancellationToken: cancellationToken)
               ?? throw new InvalidDataException(
                   "Auraly Server devolvió un cierre de sesión vacío.");
    }
}

public sealed class PosCashDrawer(
    PosPrinterConfigurationStore configuration,
    IWindowsRawPrintJob rawPrintJob,
    ILogger<PosCashDrawer> logger)
{
    // Same ESC/POS pulse used by Xion's RawPrinterHelper.AbrirCajon. This is a
    // dedicated RAW job: it contains no text, line feed or paper cut command.
    private static readonly byte[] Pulse = [0x1B, 0x70, 0x00, 0x0A, 0x28];

    public void Open()
    {
        var settings = configuration.Load();
        if (settings.ReceiptMode != PosPrinterModes.WindowsRaw ||
            string.IsNullOrWhiteSpace(settings.ReceiptPrinterName))
            throw new InvalidOperationException(
                "Configura la impresora de tirilla conectada al cajón.");
        rawPrintJob.Print(
            settings.ReceiptPrinterName,
            "Auraly-Abrir-Cajon",
            Pulse);
    }

    public bool TryOpen()
    {
        try
        {
            Open();
            return true;
        }
        catch (Exception error) when (error is InvalidOperationException or IOException)
        {
            logger.LogWarning(error, "No fue posible abrir el cajón de dinero.");
            return false;
        }
    }
}

public sealed class PosPendingClosureAuthorizationStore(TimeProvider timeProvider)
{
    private sealed record Pending(
        Guid WorkSessionId,
        PosSensitiveActionAuthorization Authorization,
        DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<Guid, Pending> values = new();

    public Guid Add(Guid workSessionId, PosSensitiveActionAuthorization authorization)
    {
        PurgeExpired();
        var token = Guid.NewGuid();
        values[token] = new Pending(
            workSessionId,
            authorization,
            timeProvider.GetUtcNow().AddMinutes(10));
        return token;
    }

    public PosSensitiveActionAuthorization Required(Guid token, Guid workSessionId)
    {
        PurgeExpired();
        if (!values.TryGetValue(token, out var pending) ||
            pending.WorkSessionId != workSessionId)
            throw new PosLocalApprovalException(
                "ApprovalRequired",
                "La autorización del supervisor venció. Solicítala nuevamente.");
        return pending.Authorization;
    }

    public void Remove(Guid token) => values.TryRemove(token, out _);

    private void PurgeExpired()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var pair in values)
            if (pair.Value.ExpiresAt <= now) values.TryRemove(pair.Key, out _);
    }
}

public interface IPosWorkSessionClosurePrinter
{
    Task PrintAsync(
        WorkSessionClosureView closure,
        CancellationToken cancellationToken);
}

public sealed class PosWorkSessionClosurePrinter(
    PosPrinterConfigurationStore configuration,
    IWindowsRawPrintJob rawPrintJob,
    IWindowsRenderedPrintJob renderedPrintJob,
    PosWorkstationIdentity? workstation = null)
    : IPosWorkSessionClosurePrinter
{
    public Task PrintAsync(
        WorkSessionClosureView closure,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = configuration.Load();
        var printerName = settings.PosPrinterName ?? settings.ReceiptPrinterName;
        if (settings.ReceiptMode != PosPrinterModes.WindowsRaw ||
            string.IsNullOrWhiteSpace(printerName))
            throw new InvalidOperationException(
                "Configura una impresora de tirilla antes de cerrar la sesión de venta.");
        var companyName = workstation?.CompanyName ?? closure.BusinessName;
        var companyLogoSource = workstation?.CompanyLogoSource;
        var documentName = $"Cierre-{closure.WorkSessionClosureId:N}";
        if (WindowsPrinterOutput.RequiresRenderedDocument(printerName) ||
            !string.IsNullOrWhiteSpace(companyLogoSource))
            return renderedPrintJob.PrintAsync(
                printerName,
                documentName,
                WorkSessionClosureReceiptRenderer.RenderHtml(
                    closure, companyName, companyLogoSource),
                configuration.ReceiptOutputDirectory,
                cancellationToken);
        var bytes = WorkSessionClosureReceiptRenderer.Render(
            closure, settings.ReceiptPaperWidthMillimeters, companyName);
        rawPrintJob.Print(printerName, documentName, bytes);
        return Task.CompletedTask;
    }
}

public static class PosWorkSessionClosureEndpoints
{
    public static RouteGroupBuilder MapPosWorkSessionClosure(this RouteGroupBuilder edge)
    {
        edge.MapPost("/work-sessions/current/closure-preview", async (
            PreviewLocalWorkSessionClosureRequest request,
            HttpContext context,
            PosLocalSessionAccessor sessions,
            PosSensitiveActionAuthorizer authorizer,
            PosPendingClosureAuthorizationStore pending,
            PosWorkSessionClosureServerClient server,
            PosOfflineWorkSessionClosureService offline,
            PosServerConnectionState connection,
            PosCashDrawer cashDrawer,
            CancellationToken ct) =>
        {
            if (request.DraftId == Guid.Empty)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.DraftId)] = ["La venta activa es obligatoria."]
                });
            var session = sessions.Required();
            var authorization = await authorizer.AuthorizeAsync(
                session,
                WorkSessionPermissionCodes.Close,
                request.DraftId,
                null,
                context.Request.Headers["X-Auraly-Approval-Id"],
                context.Request.Headers["X-Auraly-Operation-Id"],
                context.Request.Headers["X-Auraly-Supervisor-Secret"],
                ct);
            try
            {
                var preview = connection.IsConnected
                    ? await PreviewWithOfflineFallbackAsync(server, offline, session, ct)
                    : await offline.PreviewAsync(session, ct);
                cashDrawer.TryOpen();
                var token = pending.Add(session.WorkSessionId, authorization);
                return Results.Ok(new AuthorizedWorkSessionClosurePreview(token, preview));
            }
            catch
            {
                // The authorization is deliberately not consumed unless the
                // final close succeeds, but failed previews must not remain usable.
                throw;
            }
        });

        edge.MapPost("/work-sessions/current/close", async (
            CloseLocalWorkSessionRequest request,
            PosLocalSessionAccessor sessions,
            PosSensitiveActionAuthorizer authorizer,
            PosPendingClosureAuthorizationStore pending,
            PosWorkSessionClosureServerClient server,
            PosOfflineWorkSessionClosureService offline,
            PosServerConnectionState connection,
            IPosWorkSessionClosurePrinter printer,
            CancellationToken ct) =>
        {
            if (request.OperationId == Guid.Empty ||
                request.AuthorizationToken == Guid.Empty ||
                request.CountedCash < 0 ||
                request.PaymentCounts is null ||
                request.PaymentCounts.Any(value => value.CountedAmount < 0))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request)] = ["El identificador y los valores conciliados son obligatorios."]
                });
            var session = sessions.Required();
            try
            {
                var authorization = pending.Required(
                    request.AuthorizationToken,
                    session.WorkSessionId);
                var closure = connection.IsConnected
                    ? await CloseWithOfflineFallbackAsync(
                        server, offline, session, request,
                        authorization.AuthorizedByUserId, ct)
                    : await offline.CloseAsync(
                        session, request, authorization.AuthorizedByUserId, ct);
                await printer.PrintAsync(closure, ct);
                await authorizer.CompleteAsync(authorization, ct);
                await offline.MarkClosedAsync(session, closure.ClosedAt, ct);
                pending.Remove(request.AuthorizationToken);
                return Results.Ok(closure);
            }
            catch (PosWorkSessionClosureException exception)
            {
                return Results.Problem(
                    exception.Message,
                    statusCode: exception.StatusCode);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                return Results.Problem(
                    exception.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
        return edge;
    }

    private static async Task<WorkSessionClosurePreviewView> PreviewWithOfflineFallbackAsync(
        PosWorkSessionClosureServerClient server,
        PosOfflineWorkSessionClosureService offline,
        PosLocalUserSession session,
        CancellationToken cancellationToken)
    {
        try { return await server.PreviewAsync(session, cancellationToken); }
        catch (Exception exception) when (CanUseOfflineFallback(exception, cancellationToken))
        {
            return await offline.PreviewAsync(session, cancellationToken);
        }
    }

    private static async Task<WorkSessionClosureView> CloseWithOfflineFallbackAsync(
        PosWorkSessionClosureServerClient server,
        PosOfflineWorkSessionClosureService offline,
        PosLocalUserSession session,
        CloseLocalWorkSessionRequest request,
        Guid authorizedByUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await server.CloseAsync(
                session, request, authorizedByUserId, cancellationToken);
        }
        catch (Exception exception) when (CanUseOfflineFallback(exception, cancellationToken))
        {
            return await offline.CloseAsync(
                session, request, authorizedByUserId, cancellationToken);
        }
    }

    internal static bool CanUseOfflineFallback(
        Exception exception,
        CancellationToken cancellationToken) =>
        exception is HttpRequestException ||
        exception is PosWorkSessionClosureException { StatusCode: 404 or 408 or 409 or >= 500 } ||
        exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;
}

public sealed class PosWorkSessionClosureException(int statusCode, string message)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

internal static class WorkSessionClosureReceiptRenderer
{
    private static readonly byte[] Initialize = [0x1B, 0x40];
    private static readonly byte[] AlignLeft = [0x1B, 0x61, 0x00];
    private static readonly byte[] AlignCenter = [0x1B, 0x61, 0x01];
    private static readonly byte[] DoubleHeight = [0x1D, 0x21, 0x10];
    private static readonly byte[] NormalSize = [0x1D, 0x21, 0x00];
    private static readonly byte[] Cut = [0x1D, 0x56, 0x41, 0x03];

    public static byte[] Render(
        WorkSessionClosureView value,
        int width,
        string? companyName = null)
    {
        var columns = width switch
        {
            58 => 32,
            80 => 42,
            _ => throw new ArgumentOutOfRangeException(nameof(width))
        };
        using var stream = new MemoryStream();
        Write(stream, Initialize);
        Write(stream, AlignCenter);
        Line(stream, companyName ?? value.BusinessName);
        Line(stream, $"SEDE: {value.BusinessName}");
        Line(stream, "ARQUEO DE CAJA");
        Line(stream, "CIERRE CONFIRMADO");
        Line(stream, value.WarehouseName);
        Line(stream, new string('-', columns));
        Write(stream, AlignLeft);
        Wrapped(stream, $"USUARIO QUE TRABAJO: {value.UserName}", columns);
        Line(stream, $"APERTURA: {Date(value.OpenedAt)}");
        Line(stream, $"CIERRE:   {Date(value.ClosedAt)}");
        Line(stream, $"DURACION: {Duration(value.OpenedAt, value.ClosedAt)}");
        Line(stream, new string('-', columns));
        Line(stream, Pair("NUMERO DE VENTAS", value.SalesCount.ToString(CultureInfo.InvariantCulture), columns));
        Line(stream, Pair("VENTAS A CARTERA", value.CreditSalesCount.ToString(CultureInfo.InvariantCulture), columns));
        Line(stream, Pair("VALOR A CARTERA", Money(value.CreditSalesAmount), columns));
        Line(stream, Pair("NUM. DEVOLUCIONES", value.ReturnCount.ToString(CultureInfo.InvariantCulture), columns));
        Line(stream, Pair("VENTAS", Money(value.TotalSales), columns));
        Line(stream, Pair("DEVOLUCIONES", Money(value.TotalRefunds), columns));
        Line(stream, Pair("ENTRADAS / SALIDAS", Money(value.TotalOther), columns));
        Line(stream, Pair("NETO", Money(value.NetAmount), columns));
        Line(stream, new string('-', columns));
        Line(stream, "CONCILIACION POR MEDIO");
        foreach (var payment in value.PaymentTotals)
        {
            Wrapped(stream, PaymentMethodName(payment.PaymentMethodCode).ToUpperInvariant(), columns);
            Line(stream, Pair("  VENTAS", Money(payment.SalesAmount), columns));
            Line(stream, Pair("  DEVOLUCIONES", Money(payment.RefundAmount), columns));
            if (IsCash(payment.PaymentMethodCode))
            {
                Line(stream, Pair("  ENTR./SAL.", Money(payment.OtherAmount), columns));
                Line(stream, Pair("  ESPERADO", Money(payment.NetAmount), columns));
                Line(stream, Pair("  CONTADO", Money(payment.CountedAmount ?? 0), columns));
                var paymentDifference = payment.Difference ?? 0;
                Line(stream, Pair("  " + DifferenceLabel(paymentDifference), SignedMoney(paymentDifference), columns));
            }
        }
        Line(stream, new string('-', columns));
        Line(stream, Pair("EFECTIVO ESPERADO", Money(value.ExpectedCash), columns));
        Line(stream, Pair("EFECTIVO CONTADO", Money(value.CountedCash ?? 0), columns));
        var difference = value.CashDifference ?? 0;
        Write(stream, DoubleHeight);
        Line(stream, Pair(DifferenceLabel(difference), SignedMoney(difference), columns));
        Write(stream, NormalSize);
        if (!string.IsNullOrWhiteSpace(value.Note))
        {
            Line(stream, new string('-', columns));
            Wrapped(stream, "NOTA: " + value.Note, columns);
        }
        Line(stream, new string('-', columns));
        Line(stream, string.Empty);
        Line(stream, string.Empty);
        Write(stream, Cut);
        return stream.ToArray();
    }

    public static string RenderHtml(
        WorkSessionClosureView value,
        string? companyName = null,
        string? companyLogoSource = null)
    {
        var payments = string.Join(string.Empty, value.PaymentTotals.Select(payment =>
        {
            var cashDetails = IsCash(payment.PaymentMethodCode)
                ? $"<div class=\"payment-details\"><span>Entradas / salidas <strong>{Money(payment.OtherAmount)}</strong></span><span>Esperado <strong>{Money(payment.NetAmount)}</strong></span><span>Contado <strong>{Money(payment.CountedAmount ?? 0)}</strong></span><span class=\"payment-result\">{Encode(DifferenceLabel(payment.Difference ?? 0))} <strong>{SignedMoney(payment.Difference ?? 0)}</strong></span></div>"
                : string.Empty;
            return $"<section class=\"payment\" data-payment-method=\"{Encode(payment.PaymentMethodCode)}\"><h3>{Encode(PaymentMethodName(payment.PaymentMethodCode))}</h3><div class=\"payment-details\"><span>Ventas <strong>{Money(payment.SalesAmount)}</strong></span><span>Devoluciones <strong>{Money(payment.RefundAmount)}</strong></span></div>{cashDetails}</section>";
        }));
        var note = string.IsNullOrWhiteSpace(value.Note)
            ? string.Empty
            : $"<p><strong>Nota:</strong> {Encode(value.Note)}</p>";
        var logo = string.IsNullOrWhiteSpace(companyLogoSource)
            ? string.Empty
            : $"<img class=\"brand-logo\" src=\"{Encode(companyLogoSource)}\" alt=\"Logo de {Encode(companyName ?? value.BusinessName)}\">";
        return $$"""
<!doctype html><html lang="es"><head><meta charset="utf-8"><title>Cierre de sesión de venta</title>
<style>@page{size:80mm auto;margin:4mm}body{width:72mm;font:10px Arial,sans-serif;color:#111;margin:auto}.brand-logo{display:block;max-width:48mm;max-height:18mm;object-fit:contain;margin:0 auto 3mm}h1{text-align:center;font-size:15px;margin:3px 0}h2{text-align:center;font-size:11px;margin:3px 0}table{width:100%;border-collapse:collapse;margin:8px 0}td{padding:3px 1px;border-bottom:1px solid #ddd;text-align:right;font-size:8px}td:first-child{text-align:left}.summary{font-weight:bold}.payment{border-top:1px dashed #777;padding:4px 0}.payment h3{font-size:10px;margin:2px 0}.payment-details{display:flex;justify-content:space-between;gap:4px;flex-wrap:wrap}.payment-details span{display:flex;justify-content:space-between;gap:4px;min-width:46%}.payment-result{font-weight:bold}.difference{font-size:16px;border:2px solid #111;padding:5px;text-align:center}</style></head><body>
{{logo}}<h1>{{Encode(companyName ?? value.BusinessName)}}</h1><h2>ARQUEO DE CAJA · CIERRE CONFIRMADO</h2><h2>Sede: {{Encode(value.BusinessName)}} · {{Encode(value.WarehouseName)}}</h2>
<p><strong>Usuario que trabajó:</strong> {{Encode(value.UserName)}}<br><strong>Apertura:</strong> {{Date(value.OpenedAt)}}<br><strong>Cierre:</strong> {{Date(value.ClosedAt)}}<br><strong>Duración:</strong> {{Duration(value.OpenedAt, value.ClosedAt)}}</p>
<table><tbody><tr><td>Número de ventas</td><td>{{value.SalesCount}}</td></tr><tr><td>Ventas a cartera</td><td>{{value.CreditSalesCount}}</td></tr><tr><td>Valor a cartera</td><td>{{Money(value.CreditSalesAmount)}}</td></tr><tr><td>Devoluciones</td><td>{{value.ReturnCount}}</td></tr><tr><td>Total ventas</td><td>{{Money(value.TotalSales)}}</td></tr><tr><td>Total devoluciones</td><td>{{Money(value.TotalRefunds)}}</td></tr><tr><td>Entradas / salidas</td><td>{{Money(value.TotalOther)}}</td></tr><tr class="summary"><td>Neto</td><td>{{Money(value.NetAmount)}}</td></tr></tbody></table>
<h3>Todos los medios de pago</h3>{{payments}}
<p>Efectivo esperado: <strong>{{Money(value.ExpectedCash)}}</strong><br>Efectivo contado: <strong>{{Money(value.CountedCash ?? 0)}}</strong></p>
<p class="difference"><strong>{{Encode(DifferenceLabel(value.CashDifference ?? 0))}}:</strong> {{SignedMoney(value.CashDifference ?? 0)}}</p>{{note}}
</body></html>
""";
    }

    private static string Date(DateTimeOffset value) =>
        value.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
    private static string Duration(DateTimeOffset start, DateTimeOffset end)
    {
        var duration = end - start;
        return $"{(int)duration.TotalDays}d {duration.Hours:00}h {duration.Minutes:00}m";
    }
    private static string Money(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string SignedMoney(decimal value) =>
        Money(Math.Abs(value));
    private static string DifferenceLabel(decimal value) =>
        value > 0
            ? "SOBRANTE"
            : value < 0
                ? "FALTANTE"
                : "CUADRA";
    private static string PaymentMethodName(string code) => code switch
    {
        "Cash" => "Efectivo",
        "DebitCard" => "Tarjeta debito",
        "CreditCard" => "Tarjeta credito",
        "Card" => "Tarjeta",
        "Transfer" => "Transferencia",
        "Credit" => "Credito / cartera",
        "Voucher" => "Bono / vale",
        "Check" => "Cheque",
        "Withholding" => "Retencion",
        _ => code
    };
    private static bool IsCash(string code) =>
        code.Equals("Cash", StringComparison.OrdinalIgnoreCase);
    private static string Encode(string value) => WebUtility.HtmlEncode(value);
    private static string Pair(string label, string value, int columns)
    {
        var available = Math.Max(1, columns - value.Length);
        var left = label.Length > available ? label[..available] : label;
        return left.PadRight(available) + value;
    }
    private static void Wrapped(Stream stream, string value, int columns)
    {
        var text = Normalize(value);
        for (var offset = 0; offset < text.Length; offset += columns)
            Line(stream, text.Substring(offset, Math.Min(columns, text.Length - offset)));
        if (text.Length == 0) Line(stream, string.Empty);
    }
    private static void Line(Stream stream, string value)
    {
        Write(stream, Encoding.ASCII.GetBytes(Normalize(value)));
        stream.WriteByte(0x0A);
    }
    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) ==
                UnicodeCategory.NonSpacingMark) continue;
            builder.Append(character <= 0x7F ? character : '?');
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
    private static void Write(Stream stream, ReadOnlySpan<byte> bytes) => stream.Write(bytes);
}
