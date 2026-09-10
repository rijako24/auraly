using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
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
                input.PaymentCounts,
                session.Permissions.Contains(WorkSessionPermissionCodes.CloseWithPausedSales)),
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
    public string RenderHtml(WorkSessionClosureView closure)
    {
        var settings = configuration.Load();
        return Auraly.Pos.Printing.WorkSessionClosureReceiptRenderer.RenderHtml(
            closure,
            workstation?.CompanyName ?? closure.BusinessName,
            workstation?.CompanyLogoSource,
            settings.ReceiptPaperWidthMillimeters);
    }

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
        var documentName = $"Cierre-{closure.WorkSessionClosureId:N}";
        return renderedPrintJob.PrintAsync(
            printerName,
            documentName,
            RenderHtml(closure),
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
            PosDraftStore drafts,
            PosEdgeRuntimeContext runtime,
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
            var hasPausedSales = await drafts.HasTemporariesAsync(
                runtime.BusinessId,
                new Auraly.BuildingBlocks.Domain.Identifiers.WorkSessionId(session.WorkSessionId),
                new Auraly.BuildingBlocks.Domain.Identifiers.UserId(session.UserId),
                ct);
            var requiredPermission = hasPausedSales
                ? WorkSessionPermissionCodes.CloseWithPausedSales
                : WorkSessionPermissionCodes.Close;
            var authorization = await authorizer.AuthorizeAsync(
                session,
                requiredPermission,
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
                    session, request, authorization.AuthorizedByUserId,
                    string.Equals(
                        authorization.PermissionResource,
                        WorkSessionPermissionCodes.CloseWithPausedSales,
                        StringComparison.Ordinal),
                    ct);
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
