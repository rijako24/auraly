using System.Globalization;
using System.Collections.Concurrent;
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
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/pos/v1/work-sessions/{session.WorkSessionId:D}/close");
        request.Headers.Add("X-Auraly-Device-Id", credentials.DeviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", credentials.Secret);
        request.Headers.Add("Idempotency-Key", input.OperationId.ToString("D"));
        request.Content = JsonContent.Create(new DeviceCloseWorkSessionRequest(
            session.UserId,
            session.WorkSessionId,
            input.CountedCash,
            input.Note,
            authorizedByUserId,
            input.PaymentCounts));
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
        WindowsRawPrintJob.Print(
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

public sealed class PosWorkSessionClosurePrinter(
    PosPrinterConfigurationStore configuration)
{
    public Task PrintAsync(
        WorkSessionClosureView closure,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = configuration.Load();
        if (settings.ReceiptMode != PosPrinterModes.WindowsRaw ||
            string.IsNullOrWhiteSpace(settings.ReceiptPrinterName))
            throw new InvalidOperationException(
                "Configura una impresora de tirilla antes de cerrar la sesión de venta.");
        var bytes = WorkSessionClosureReceiptRenderer.Render(
            closure,
            settings.ReceiptPaperWidthMillimeters);
        WindowsRawPrintJob.Print(
            settings.ReceiptPrinterName,
            $"Auraly-Cierre-{closure.WorkSessionClosureId:N}",
            bytes);
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
                var preview = await server.PreviewAsync(session, ct);
                cashDrawer.Open();
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
            PosWorkSessionClosurePrinter printer,
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
                var closure = await server.CloseAsync(
                    session, request, authorization.AuthorizedByUserId, ct);
                await printer.PrintAsync(closure, ct);
                await authorizer.CompleteAsync(authorization, ct);
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
    private static readonly byte[] Cut = [0x1D, 0x56, 0x41, 0x03];

    public static byte[] Render(WorkSessionClosureView value, int width)
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
        Line(stream, "CIERRE DE SESION DE VENTA");
        Line(stream, value.BusinessName);
        Line(stream, value.WarehouseName);
        Line(stream, new string('-', columns));
        Write(stream, AlignLeft);
        Wrapped(stream, $"CAJERO: {value.UserName}", columns);
        Line(stream, $"APERTURA: {Date(value.OpenedAt)}");
        Line(stream, $"CIERRE:   {Date(value.ClosedAt)}");
        Line(stream, $"DURACION: {Duration(value.OpenedAt, value.ClosedAt)}");
        Line(stream, new string('-', columns));
        Line(stream, Pair("VENTAS", Money(value.TotalSales), columns));
        Line(stream, Pair("DEVOLUCIONES", Money(value.TotalRefunds), columns));
        Line(stream, Pair("OTROS MOVIMIENTOS", Money(value.TotalOther), columns));
        Line(stream, Pair("NETO", Money(value.NetAmount), columns));
        Line(stream, new string('-', columns));
        Line(stream, "CONCILIACION POR MEDIO");
        foreach (var payment in value.PaymentTotals)
        {
            Wrapped(stream, PaymentMethodName(payment.PaymentMethodCode).ToUpperInvariant(), columns);
            Line(stream, Pair("  ESPERADO", Money(payment.NetAmount), columns));
            if (RequiresManualCount(payment.PaymentMethodCode))
            {
                Line(stream, Pair("  CONTADO", Money(payment.CountedAmount ?? 0), columns));
                var paymentDifference = payment.Difference ?? 0;
                Line(stream, Pair("  " + DifferenceLabel(paymentDifference), SignedMoney(paymentDifference), columns));
            }
            else
            {
                Line(stream, "  CONCILIACION AUTOMATICA");
            }
        }
        Line(stream, new string('-', columns));
        Line(stream, Pair("EFECTIVO ESPERADO", Money(value.ExpectedCash), columns));
        Line(stream, Pair("EFECTIVO CONTADO", Money(value.CountedCash ?? 0), columns));
        var difference = value.CashDifference ?? 0;
        Line(stream, Pair(DifferenceLabel(difference), SignedMoney(difference), columns));
        if (!string.IsNullOrWhiteSpace(value.Note))
        {
            Line(stream, new string('-', columns));
            Wrapped(stream, "NOTA: " + value.Note, columns);
        }
        Line(stream, new string('-', columns));
        Wrapped(stream, $"CIERRE: {value.WorkSessionClosureId:D}", columns);
        Line(stream, string.Empty);
        Line(stream, string.Empty);
        Write(stream, Cut);
        return stream.ToArray();
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
        value > 0
            ? "+" + Money(value)
            : Money(value);
    private static string DifferenceLabel(decimal value) =>
        value > 0
            ? "DIFERENCIA (+) SOBRANTE"
            : value < 0
                ? "DIFERENCIA (-) FALTANTE"
                : "DIFERENCIA (CUADRADO)";
    private static string PaymentMethodName(string code) => code switch
    {
        "Cash" => "Efectivo",
        "DebitCard" => "Tarjeta debito",
        "CreditCard" => "Tarjeta credito",
        "Card" => "Tarjeta",
        "Transfer" => "Transferencia",
        _ => code
    };
    private static bool RequiresManualCount(string code) =>
        code.Equals("Cash", StringComparison.OrdinalIgnoreCase) ||
        code.Equals("Card", StringComparison.OrdinalIgnoreCase) ||
        code.Equals("DebitCard", StringComparison.OrdinalIgnoreCase) ||
        code.Equals("CreditCard", StringComparison.OrdinalIgnoreCase);
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
