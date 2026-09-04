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

public sealed record CloseLocalWorkSessionResult(
    WorkSessionClosureView Closure,
    bool PrintedDirectly,
    string? PrintError);

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

    public async Task<WorkSessionClosureView?> GetClosureAsync(
        Guid workSessionId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/pos/v1/work-sessions/{workSessionId:D}/closure?userId={userId:D}");
        request.Headers.Add("X-Auraly-Device-Id", credentials.DeviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", credentials.Secret);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
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
        var printerName = settings.PosPrinterName;
        if (settings.ReceiptMode != PosPrinterModes.WindowsRaw ||
            string.IsNullOrWhiteSpace(printerName))
            throw new InvalidOperationException(
                "Configura la impresora de facturación antes de cerrar la sesión de venta.");
        var companyName = workstation?.CompanyName ?? closure.BusinessName;
        var companyLogoSource = workstation?.CompanyLogoSource;
        var documentName = $"Cierre-{closure.WorkSessionClosureId:N}";
        return renderedPrintJob.PrintAsync(
            printerName,
            documentName,
            WorkSessionClosureReceiptRenderer.RenderHtml(
                closure, companyName, companyLogoSource,
                settings.ReceiptPaperWidthMillimeters),
            configuration.ReceiptOutputDirectory,
            settings.ReceiptPaperWidthMillimeters,
            cancellationToken);
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
            PosOfflineWorkSessionClosureService offline,
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
                var preview = await offline.PreviewAsync(session, ct);
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
            PosOfflineWorkSessionClosureService offline,
            IPosWorkSessionClosurePrinter printer,
            ILogger<PosOfflineWorkSessionClosureService> logger,
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
                var closure = await offline.CloseAsync(
                    session, request, authorization.AuthorizedByUserId, ct);
                await authorizer.CompleteAsync(authorization, ct);
                await offline.MarkClosedAsync(session, closure.ClosedAt, ct);
                pending.Remove(request.AuthorizationToken);
                string? printError = null;
                try
                {
                    await printer.PrintAsync(closure, ct);
                }
                catch (Exception error) when (
                    error is not OperationCanceledException)
                {
                    // The closure and its outbox message are already durable. A
                    // printer/Windows association failure must never reopen the
                    // work session or make the cashier submit the count again.
                    logger.LogWarning(
                        error,
                        "Work-session closure {ClosureId} was completed but direct printing failed.",
                        closure.WorkSessionClosureId);
                    printError = error.Message;
                }
                return Results.Ok(new CloseLocalWorkSessionResult(
                    closure, printError is null, printError));
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
    private static readonly byte[] BoldOn = [0x1B, 0x45, 0x01];
    private static readonly byte[] BoldOff = [0x1B, 0x45, 0x00];
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
        Write(stream, DoubleHeight);
        BoldLine(stream, companyName ?? value.BusinessName);
        Write(stream, NormalSize);
        BoldLine(stream, "Arqueo de caja - Cierre confirmado");
        Wrapped(stream, $"Sede: {Location(value)}", columns);
        Line(stream, new string('-', columns));
        Write(stream, AlignLeft);
        Wrapped(stream, $"Usuario que trabajo: {value.UserName}", columns);
        Line(stream, $"Apertura: {Date(value.OpenedAt)}");
        Line(stream, $"Cierre:   {Date(value.ClosedAt)}");
        Line(stream, $"Duracion: {Duration(value.OpenedAt, value.ClosedAt)}");
        Line(stream, new string('-', columns));
        Line(stream, Pair("Numero de ventas", value.SalesCount.ToString(CultureInfo.InvariantCulture), columns));
        Line(stream, Pair("Ventas a cartera", value.CreditSalesCount.ToString(CultureInfo.InvariantCulture), columns));
        Line(stream, Pair("Num. devoluciones", value.ReturnCount.ToString(CultureInfo.InvariantCulture), columns));
        Line(stream, Pair("Ventas", Money(value.TotalSales), columns));
        Line(stream, Pair("Devoluciones", Money(value.TotalRefunds), columns));
        Line(stream, Pair("Valor a cartera", Money(value.CreditSalesAmount), columns));
        Line(stream, Pair("Entradas de caja", Money(CashEntries(value)), columns));
        Line(stream, Pair("Salidas de caja", Money(CashExits(value)), columns));
        Line(stream, new string('-', columns));
        BoldLine(stream, "Conciliacion por medio");
        foreach (var payment in value.PaymentTotals)
        {
            BoldWrapped(stream, PaymentMethodName(payment.PaymentMethodCode), columns);
            Line(stream, Pair("  Ventas", Money(payment.SalesAmount), columns));
            Line(stream, Pair("  Devoluciones", Money(payment.RefundAmount), columns));
            if (IsCash(payment.PaymentMethodCode))
            {
                BoldLine(stream, Pair("  Entradas", Money(Math.Max(0, payment.OtherAmount)), columns));
                BoldLine(stream, Pair("  Salidas", Money(Math.Abs(Math.Min(0, payment.OtherAmount))), columns));
                Line(stream, new string('-', columns));
                BoldLine(stream, Pair("  Efectivo esperado", Money(value.ExpectedCash), columns));
                BoldLine(stream, Pair("  Efectivo contado", Money(value.CountedCash ?? 0), columns));
                Line(stream, new string('-', columns));
            }
        }
        Line(stream, new string('-', columns));
        var difference = value.CashDifference ?? 0;
        Write(stream, DoubleHeight);
        Line(stream, Pair(DifferenceLabel(difference), SignedMoney(difference), columns));
        Write(stream, NormalSize);
        if (!string.IsNullOrWhiteSpace(value.Note))
        {
            Line(stream, new string('-', columns));
            Wrapped(stream, "Nota: " + value.Note, columns);
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
        string? companyLogoSource = null,
        int paperWidthMillimeters = 80)
    {
        if (paperWidthMillimeters is not (58 or 80))
            throw new ArgumentOutOfRangeException(nameof(paperWidthMillimeters));
        var payments = string.Join(string.Empty, value.PaymentTotals.Select(payment =>
        {
            var cashDetails = IsCash(payment.PaymentMethodCode)
                ? $"<div class=\"payment-details\"><span>Entradas <strong>{Money(Math.Max(0, payment.OtherAmount))}</strong></span><span>Salidas <strong>{Money(Math.Abs(Math.Min(0, payment.OtherAmount)))}</strong></span></div>"
                : string.Empty;
            return $"<section class=\"payment\" data-payment-method=\"{Encode(payment.PaymentMethodCode)}\"><h3>{Encode(PaymentMethodName(payment.PaymentMethodCode))}</h3><div class=\"payment-details\"><span>Ventas <strong>{Money(payment.SalesAmount)}</strong></span><span>Devoluciones <strong>{Money(payment.RefundAmount)}</strong></span></div>{cashDetails}</section>";
        }));
        var note = string.IsNullOrWhiteSpace(value.Note)
            ? string.Empty
            : $"<p><strong>Nota:</strong> {Encode(value.Note)}</p>";
        var logo = string.IsNullOrWhiteSpace(companyLogoSource)
            ? string.Empty
            : $"<img class=\"brand-logo\" src=\"{Encode(companyLogoSource)}\" alt=\"Logo de {Encode(companyName ?? value.BusinessName)}\">";
        var html = $$"""
<!doctype html><html lang="es"><head><meta charset="utf-8"><title>Cierre de sesión de venta</title>
<style>@page{size:{{paperWidthMillimeters}}mm auto;margin:3mm}*{box-sizing:border-box}body{width:{{paperWidthMillimeters}}mm;font:11px/1.35 ui-monospace,Consolas,monospace;color:#111;margin:0;padding:5mm 3mm 2mm 2mm}.brand-logo{display:block;max-width:48mm;max-height:18mm;object-fit:contain;margin:0 auto 3mm}header{text-align:center;border-bottom:1px dashed #555;padding-bottom:9px}h1{font:800 19px/1.2 Arial,sans-serif;margin:3px 0;text-transform:uppercase}header h2{font-size:12px;margin:7px 0 4px;text-transform:uppercase}.scope{margin:3px 0}.section-title{font-size:11px;margin:13px 0 6px;padding-bottom:4px;border-bottom:1px dashed #777;text-transform:uppercase}.session-details{text-align:left;font-size:11px;line-height:1.5;margin-top:10px}.rows{width:100%;border-collapse:collapse;margin:0}.rows td{padding:3px 1px;text-align:right;font-size:11px;font-variant-numeric:tabular-nums}.rows td:first-child{width:64%;text-align:left}.count-row td{font-size:12px;font-weight:800}.payment{border-bottom:1px dashed #aaa;padding:5px 0}.payment h3{font-size:11px;margin:2px 0 4px;text-transform:uppercase}.payment-details span{display:flex;justify-content:space-between;gap:8px;width:100%;padding:2px 1px}.payment-details span>strong{font-variant-numeric:tabular-nums;text-align:right}.difference{display:flex;justify-content:space-between;font-size:16px;border:2px solid #111;padding:8px;margin-top:12px;font-weight:800}.note{margin-top:9px;padding:6px;border:1px dashed #777}</style></head><body>
<header>{{logo}}<h1>{{Encode(companyName ?? value.BusinessName)}}</h1><h2>Arqueo de caja · Cierre confirmado</h2><p class="scope">Sede: {{Encode(Location(value))}}</p>
<p class="session-details"><strong>Usuario que trabajó:</strong> {{Encode(value.UserName)}}<br><strong>Apertura:</strong> {{Date(value.OpenedAt)}}<br><strong>Cierre:</strong> {{Date(value.ClosedAt)}}<br><strong>Duración:</strong> {{Duration(value.OpenedAt, value.ClosedAt)}}</p>
</header>
<h2 class="section-title">Actividad del turno</h2><table class="rows"><tbody><tr class="count-row"><td>Número de ventas</td><td>{{value.SalesCount}}</td></tr><tr class="count-row"><td>Ventas a cartera</td><td>{{value.CreditSalesCount}}</td></tr><tr class="count-row"><td>Devoluciones</td><td>{{value.ReturnCount}}</td></tr></tbody></table>
<h2 class="section-title">Detalle por medio de pago</h2>{{payments}}
<h2 class="section-title">Totales del turno</h2><table class="rows"><tbody><tr><td>Ventas</td><td>{{Money(value.TotalSales)}}</td></tr><tr><td>Devoluciones</td><td>{{Money(value.TotalRefunds)}}</td></tr><tr><td>Valor a cartera</td><td>{{Money(value.CreditSalesAmount)}}</td></tr><tr><td>Entradas de caja</td><td>{{Money(CashEntries(value))}}</td></tr><tr><td>Salidas de caja</td><td>{{Money(CashExits(value))}}</td></tr><tr><td>Efectivo esperado</td><td>{{Money(value.ExpectedCash)}}</td></tr><tr><td>Efectivo contado</td><td>{{Money(value.CountedCash ?? 0)}}</td></tr></tbody></table>
<div class="difference"><strong>{{Encode(DifferenceLabel(value.CashDifference ?? 0))}}</strong><strong>{{SignedMoney(value.CashDifference ?? 0)}}</strong></div>{{note}}
</body></html>
""";
        return html
            .Replace(
                "</style>",
                ".platform-footer{margin-top:10px;text-align:center;font:700 12px/1.4 Arial,sans-serif}</style>",
                StringComparison.Ordinal)
            .Replace(
                "</body>",
                "<footer class=\"platform-footer\">www.auralyapp.co</footer></body>",
                StringComparison.Ordinal)
            .Replace(
                "<html lang=\"es\">",
                $"<html lang=\"es\" data-auraly-report=\"{PosPrintTemplateCatalog.WorkSessionClosure.Code}\" data-auraly-report-version=\"{PosPrintTemplateCatalog.WorkSessionClosure.Version}\">",
                StringComparison.Ordinal);
    }

    private static string Date(DateTimeOffset value) =>
        value.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("es-CO"));
    private static string Duration(DateTimeOffset start, DateTimeOffset end)
    {
        var duration = end - start;
        return $"{(int)duration.TotalDays}d {duration.Hours:00}h {duration.Minutes:00}m";
    }
    private static string Money(decimal value) =>
        "$ " + value.ToString("N0", CultureInfo.GetCultureInfo("es-CO"));
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
    private static decimal CashEntries(WorkSessionClosureView value) =>
        value.PaymentTotals.Sum(payment => Math.Max(0, payment.OtherAmount));
    private static decimal CashExits(WorkSessionClosureView value) =>
        value.PaymentTotals.Sum(payment => Math.Abs(Math.Min(0, payment.OtherAmount)));
    private static string Location(WorkSessionClosureView value) => value.BusinessName;
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
    private static void BoldWrapped(Stream stream, string value, int columns)
    {
        Write(stream, BoldOn);
        Wrapped(stream, value, columns);
        Write(stream, BoldOff);
    }
    private static void BoldLine(Stream stream, string value)
    {
        Write(stream, BoldOn);
        Line(stream, value);
        Write(stream, BoldOff);
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
