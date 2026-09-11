using System.Net.Http.Json;
using System.Threading.Channels;
using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.Pos.Edge.Infrastructure;
using Azure.Messaging.WebPubSub.Clients;

namespace Auraly.Pos.Edge.Host;

[Flags]
public enum PosSynchronizationTrigger
{
    None = 0,
    Catalog = 1,
    Security = 2,
    FiscalStatus = 4,
    LocalOutbox = 8,
    Manual = 16,
    FiscalProvisioning = 32,
    Customers = 64,
    Configuration = 128,
    AutomaticRetry = 256,
    All = Catalog | Security | FiscalStatus | LocalOutbox | FiscalProvisioning | Customers | Configuration
}

public sealed class PosSynchronizationSignal
{
    private readonly Channel<PosSynchronizationTrigger> channel =
        Channel.CreateUnbounded<PosSynchronizationTrigger>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

    public void Signal(PosSynchronizationTrigger trigger)
    {
        if (trigger != PosSynchronizationTrigger.None)
            channel.Writer.TryWrite(trigger);
    }

    public void Schedule(
        PosSynchronizationTrigger trigger,
        TimeSpan delay,
        CancellationToken cancellationToken) =>
        _ = ScheduleCoreAsync(trigger, delay, cancellationToken);

    private async Task ScheduleCoreAsync(
        PosSynchronizationTrigger trigger,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            Signal(trigger);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    internal async ValueTask<PosSynchronizationTrigger> ReadAsync(
        CancellationToken cancellationToken)
    {
        var combined = await channel.Reader.ReadAsync(cancellationToken);
        while (channel.Reader.TryRead(out var next)) combined |= next;
        return combined;
    }
}

internal sealed class PosSynchronizationWork(
    PosIdentitySynchronizer identities,
    PosCatalogSynchronizer catalog,
    PosCustomerServerClient customerDirectory,
    PosEdgeOutboxUploader uploader,
    PosCashMovementServerClient cashMovements,
    PosWorkSessionClosureUploader closures,
    PosWorkSessionOpenUploader workSessionOpenings,
    PosCustomerOutboxUploader customers,
    PosUnifiedOutboxDispatcher outbox,
    PosFiscalStatusSynchronizer fiscalStatuses,
    PosFiscalProvisioningSynchronizer fiscalProvisioning,
    PosUiStateSignal uiState,
    PosSynchronizationState state,
    PosSynchronizationEventLog events,
    PosSynchronizationSignal signal,
    PosSynchronizationLaneExecutor lanes,
    PosCatalogStore catalogStore)
{
    public async Task<PosSynchronizationExecutionResult> ExecuteAsync(
        PosSynchronizationTrigger trigger,
        CancellationToken cancellationToken,
        bool preservePreparationFailure = false)
    {
        state.Begin(preservePreparationFailure);
        events.Record("Info", "Synchronization", "Sincronización iniciada", trigger.ToString());
        uiState.Publish();
        try
        {
            await catalogStore.InitializeAsync(cancellationToken);
            var initialPreparation = (await catalogStore.StatusAsync(cancellationToken)).Status != "Ready";
            var pending = new List<PosSynchronizationLane>();
            if (trigger.HasFlag(PosSynchronizationTrigger.LocalOutbox))
                pending.Add(new PosSynchronizationLane(
                    PosSynchronizationTrigger.LocalOutbox,
                    "subida de documentos",
                    async () =>
                    {
                        const int batchSize = 25;
                        var processed = 0;
                        while (processed < batchSize &&
                               await outbox.NextAsync(cancellationToken) is { } route)
                        {
                            var dispatched = route switch
                            {
                                PosUnifiedOutboxRoute.WorkSessionOpened =>
                                    await workSessionOpenings.UploadNextAsync(cancellationToken),
                                PosUnifiedOutboxRoute.Sale =>
                                    await uploader.UploadNextAsync(cancellationToken),
                                PosUnifiedOutboxRoute.CashMovement =>
                                    await cashMovements.UploadNextAsync(cancellationToken),
                                PosUnifiedOutboxRoute.WorkSessionClosure =>
                                    await closures.UploadNextAsync(cancellationToken),
                                PosUnifiedOutboxRoute.CustomerCreated =>
                                    await customers.UploadNextAsync(cancellationToken),
                                _ => throw new ArgumentOutOfRangeException(nameof(route))
                            };
                            processed++;
                            if (!dispatched) continue;
                        }
                        if (processed == batchSize &&
                            await outbox.NextAsync(cancellationToken) is not null)
                            signal.Signal(PosSynchronizationTrigger.LocalOutbox);
                        if (await outbox.NextRetryDelayAsync(cancellationToken) is { } delay)
                            signal.Schedule(
                                PosSynchronizationTrigger.LocalOutbox,
                                delay,
                                cancellationToken);
                    }));
            if (trigger.HasFlag(PosSynchronizationTrigger.Security))
                pending.Add(new PosSynchronizationLane(
                    PosSynchronizationTrigger.Security,
                    "usuarios y permisos",
                    () => identities.SynchronizeAsync(cancellationToken)));
            if (!initialPreparation && trigger.HasFlag(PosSynchronizationTrigger.Customers))
                pending.Add(new PosSynchronizationLane(
                    PosSynchronizationTrigger.Customers,
                    "clientes",
                    () => catalog.SynchronizeCustomersAsync(cancellationToken)));
            if (trigger.HasFlag(PosSynchronizationTrigger.Catalog))
            {
                pending.Add(new PosSynchronizationLane(
                    PosSynchronizationTrigger.Catalog,
                    "catálogo",
                    async () =>
                    {
                        await catalog.SynchronizeAsync(cancellationToken);
                    }));
            }
            if (!initialPreparation && trigger.HasFlag(PosSynchronizationTrigger.Configuration))
            {
                pending.Add(new PosSynchronizationLane(
                    PosSynchronizationTrigger.Configuration,
                    "precios y configuración",
                    async () =>
                    {
                        await catalog.SynchronizeConfigurationAsync(cancellationToken);
                        await customerDirectory.RefreshGeographyAsync(cancellationToken);
                        await cashMovements.RefreshReasonsAsync(cancellationToken);
                    }));
            }
            if (trigger.HasFlag(PosSynchronizationTrigger.FiscalStatus))
                pending.Add(new PosSynchronizationLane(
                    PosSynchronizationTrigger.FiscalStatus,
                    "estados fiscales",
                    () => fiscalStatuses.SynchronizeAsync(cancellationToken)));
            if (trigger.HasFlag(PosSynchronizationTrigger.FiscalProvisioning))
                pending.Add(new PosSynchronizationLane(
                    PosSynchronizationTrigger.FiscalProvisioning,
                    "configuración fiscal",
                    () => fiscalProvisioning.SynchronizeAsync(cancellationToken)));
            var result = await lanes.ExecuteAllAsync(pending, cancellationToken);
            if (!result.Succeeded)
            {
                state.Failed();
                events.Record(
                    "Warning",
                    "Synchronization",
                    result.HasRetryableFailure
                        ? "Sincronización parcial; Auraly volverá a intentarlo automáticamente"
                        : "Sincronización parcial; revisa el enrolamiento o la configuración",
                    trigger.ToString());
            }
            else
            {
                state.Succeeded(preservePreparationFailure);
                events.Record(
                    "Success",
                    "Synchronization",
                    "Sincronización completada",
                    trigger.ToString());
            }
            return result;
        }
        finally { uiState.Publish(); }
    }
}

internal sealed record PosSynchronizationLane(
    PosSynchronizationTrigger Trigger,
    string Label,
    Func<Task> Execute);

internal sealed record PosSynchronizationExecutionResult(
    bool Succeeded,
    bool HasRetryableFailure,
    bool HasPermanentFailure,
    PosSynchronizationTrigger RetryableTriggers);

internal sealed record PosSynchronizationLaneResult(
    bool Succeeded,
    bool Retryable,
    PosSynchronizationTrigger Trigger);

internal sealed class PosSynchronizationLaneExecutor(
    PosSynchronizationEventLog events,
    ILogger<PosSynchronizationLaneExecutor> logger,
    PosSynchronizationState? state = null,
    PosUiStateSignal? uiState = null)
{
    public async Task<PosSynchronizationExecutionResult> ExecuteAllAsync(
        IReadOnlyCollection<PosSynchronizationLane> lanes,
        CancellationToken cancellationToken)
    {
        var results = await Task.WhenAll(lanes.Select(
            lane => ExecuteAsync(lane, cancellationToken)));
        return new PosSynchronizationExecutionResult(
            results.All(value => value.Succeeded),
            results.Any(value => !value.Succeeded && value.Retryable),
            results.Any(value => !value.Succeeded && !value.Retryable),
            results
                .Where(value => !value.Succeeded && value.Retryable)
                .Aggregate(
                    PosSynchronizationTrigger.None,
                    (combined, value) => combined | value.Trigger));
    }

    private async Task<PosSynchronizationLaneResult> ExecuteAsync(
        PosSynchronizationLane lane,
        CancellationToken cancellationToken)
    {
        state?.StageStarted(lane.Label);
        uiState?.Publish();
        try
        {
            await lane.Execute();
            state?.StageSucceeded(lane.Label);
            return new PosSynchronizationLaneResult(true, false, lane.Trigger);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var reason = DescribeFailure(lane.Label, exception);
            state?.StageFailed(lane.Label, reason);
            logger.LogWarning(
                exception,
                "POS synchronization lane {Lane} failed without blocking other lanes.",
                lane.Trigger);
            events.Record(
                "Warning",
                "Synchronization",
                $"Pendiente de sincronizar: {lane.Label}",
                PosSynchronizationFailurePresenter.EventDetail(exception));
            return new PosSynchronizationLaneResult(
                false,
                PosSynchronizationFailurePresenter.IsRetryable(exception),
                lane.Trigger);
        }
        finally
        {
            uiState?.Publish();
        }
    }

    private static string DescribeFailure(string lane, Exception exception)
    {
        if (exception is HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound })
            return $"No fue posible preparar {lane}: el servidor no ofrece una operación requerida por esta versión de Auraly. Revisa la versión instalada o repite el enrolamiento.";
        if (exception is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden })
            return $"No fue posible preparar {lane}: el servidor rechazó la identidad de esta caja. Repite el enrolamiento o solicita ayuda al supervisor.";
        if (PosSynchronizationFailurePresenter.IsRetryable(exception))
            return $"No fue posible preparar {lane}: la conexión con Auraly se interrumpió. Se reintentará automáticamente; también puedes reintentar ahora.";
        if (exception is InvalidDataException)
            return $"No fue posible preparar {lane}: los datos descargados no pasaron la validación. Reintenta; si se repite, informa al supervisor.";
        return $"No fue posible preparar {lane}. Reintenta; si se repite, informa al supervisor.";
    }
}

internal static class PosSynchronizationFailurePresenter
{
    public static bool IsRetryable(Exception exception) => exception switch
    {
        TaskCanceledException => true,
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: var status } =>
            status is System.Net.HttpStatusCode.RequestTimeout or
                System.Net.HttpStatusCode.TooManyRequests ||
            (int)status! >= 500,
        _ => false
    };

    public static string EventDetail(Exception exception) => IsRetryable(exception)
        ? "La conexión con Auraly se interrumpió. El sistema volverá a intentarlo automáticamente."
        : exception is HttpRequestException
            ? "Auraly rechazó la solicitud de sincronización. Revisa el enrolamiento o la versión instalada."
            : "La etapa no pudo completarse. Reintenta y solicita ayuda si el problema continúa.";

    public static string? StoredError(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : "Hay información pendiente de sincronizar. Auraly volverá a intentarlo automáticamente.";
}

internal static class PosSynchronizationRetryPolicy
{
    public static TimeSpan? NextDelay(int completedAutomaticRetries) =>
        completedAutomaticRetries switch
        {
            0 => TimeSpan.FromSeconds(5),
            1 => TimeSpan.FromSeconds(10),
            2 => TimeSpan.FromSeconds(20),
            _ => null
        };
}

public sealed record PosSynchronizationNegotiation(
    Uri ClientAccessUri,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string>? Groups = null);

public sealed class PosEnrollmentRevocationHandler(
    PosEdgeEnrollmentStore enrollments,
    IHostApplicationLifetime lifetime,
    ILogger<PosEnrollmentRevocationHandler> logger)
{
    private int applied;

    public Task ApplyAsync(Guid targetDeviceId, Guid currentDeviceId)
    {
        if (targetDeviceId != currentDeviceId ||
            Interlocked.Exchange(ref applied, 1) != 0)
            return Task.CompletedTask;
        enrollments.Clear();
        logger.LogInformation(
            "Enrollment for device {DeviceId} was revoked remotely; restarting in online mode.",
            currentDeviceId);
        lifetime.StopApplication();
        return Task.CompletedTask;
    }
}

public sealed class PosWebPubSubConnection : IAsyncDisposable
{
    private readonly HttpClient http;
    private readonly PosDeviceCredentials credentials;
    private readonly PosSynchronizationSignal signal;
    private readonly PosServerConnectionState connectionState;
    private readonly PosPushConnectionState pushState;
    private readonly PosUiStateSignal uiState;
    private readonly PosSynchronizationEventLog events;
    private readonly PosEnrollmentRevocationHandler enrollmentRevocation;
    private readonly Guid tenantId;
    private readonly Guid businessId;
    private readonly WebPubSubClient client;
    private IReadOnlyList<string> authorizedGroups = [];
    private readonly Channel<bool> terminalDisconnections =
        Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });

    public PosWebPubSubConnection(
        HttpClient http,
        PosDeviceCredentials credentials,
        PosSynchronizationSignal signal,
        PosServerConnectionState connectionState,
        PosPushConnectionState pushState,
        PosUiStateSignal uiState,
        PosSynchronizationEventLog events,
        PosEnrollmentRevocationHandler enrollmentRevocation,
        Guid tenantId,
        Guid businessId)
    {
        this.http = http;
        this.credentials = credentials;
        this.signal = signal;
        this.connectionState = connectionState;
        this.pushState = pushState;
        this.uiState = uiState;
        this.events = events;
        this.enrollmentRevocation = enrollmentRevocation;
        this.tenantId = tenantId;
        this.businessId = businessId;
        var credential = new WebPubSubClientCredential(NegotiateAsync);
        client = new WebPubSubClient(
            credential,
            new WebPubSubClientOptions
            {
                AutoReconnect = true,
                AutoRejoinGroups = true,
                Protocol = new WebPubSubJsonReliableProtocol()
            });
        client.Connected += OnConnectedAsync;
        client.Disconnected += OnDisconnectedAsync;
        client.GroupMessageReceived += OnGroupMessageReceivedAsync;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        while (terminalDisconnections.Reader.TryRead(out _)) { }
        events.Record("Info", "Push", "Conectando canal de eventos");
        await client.StartAsync(cancellationToken);
        foreach (var group in authorizedGroups)
            await client.JoinGroupAsync(group, cancellationToken: cancellationToken);
        events.Record("Success", "Push", "Canal suscrito a cambios del negocio");
    }

    public async Task WaitForTerminalDisconnectionAsync(
        CancellationToken cancellationToken) =>
        await terminalDisconnections.Reader.ReadAsync(cancellationToken);

    public Task StopAsync() => client.StopAsync();

    public async ValueTask DisposeAsync() => await client.DisposeAsync();

    private async ValueTask<Uri> NegotiateAsync(
        CancellationToken cancellationToken)
    {
        events.Record("Info", "Push", "Negociando señal en tiempo real");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/pos/v1/synchronization/negotiate?businessId={businessId:D}");
        request.Headers.Add(
            "X-Auraly-Device-Id",
            credentials.DeviceId.ToString("D"));
        request.Headers.Add(
            "X-Auraly-Device-Secret",
            credentials.Secret);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized)
        {
            await enrollmentRevocation.ApplyAsync(
                credentials.DeviceId, credentials.DeviceId);
            throw new UnauthorizedAccessException(
                "The POS enrollment is no longer authorized by Auraly Server.");
        }
        response.EnsureSuccessStatusCode();
        var negotiation = await response.Content
            .ReadFromJsonAsync<PosSynchronizationNegotiation>(
                cancellationToken)
            ?? throw new InvalidDataException(
                "Auraly Server returned an empty synchronization negotiation.");
        authorizedGroups = negotiation.Groups ?? [];
        if (authorizedGroups.Count == 0)
            throw new InvalidDataException(
                "Auraly Server did not authorize a synchronization group.");
        events.Record("Info", "Push", "Señal en tiempo real autorizada");
        return negotiation.ClientAccessUri;
    }

    private Task OnConnectedAsync(WebPubSubConnectedEventArgs _)
    {
        connectionState.MarkConnected();
        pushState.MarkConnected();
        events.Record("Success", "Connection", "Caja conectada con Auraly Server");
        uiState.Publish();
        signal.Signal(PosSynchronizationTrigger.All);
        return Task.CompletedTask;
    }

    private Task OnDisconnectedAsync(WebPubSubDisconnectedEventArgs _)
    {
        // The push channel is optional transport. Its disconnection does not prove that
        // the HTTP server is unreachable; PosServerConnectionHandler owns that signal.
        pushState.MarkDisconnected();
        events.Record("Warning", "Connection", "Canal de eventos desconectado; reconectando");
        uiState.Publish();
        terminalDisconnections.Writer.TryWrite(true);
        return Task.CompletedTask;
    }

    private async Task OnGroupMessageReceivedAsync(
        WebPubSubGroupMessageEventArgs args)
    {
        try
        {
            var invalidation = args.Message.Data
                .ToObjectFromJson<PosSynchronizationInvalidation>();
            if (invalidation is null ||
                invalidation.TenantId != tenantId ||
                invalidation.BusinessId != businessId)
                return;
            if (invalidation.Stream == PosSynchronizationStreams.DeviceEnrollment &&
                invalidation.TargetDeviceId is { } targetDeviceId)
            {
                events.Record("Warning", "Enrollment", "El equipo fue desenrolado desde Auraly");
                await enrollmentRevocation.ApplyAsync(targetDeviceId, credentials.DeviceId);
                return;
            }
            signal.Signal(ToTrigger(invalidation.Stream));
            events.Record("Info", "Push", "Evento recibido del servidor", invalidation.Stream);
        }
        catch (System.Text.Json.JsonException exception)
        {
            events.Record(
                "Warning",
                "Push",
                "Se ignoró un evento de sincronización inválido",
                exception.Message);
        }
    }

    private static PosSynchronizationTrigger ToTrigger(string stream) =>
        stream switch
        {
            PosSynchronizationStreams.Catalog => PosSynchronizationTrigger.Catalog,
            PosSynchronizationStreams.Customers => PosSynchronizationTrigger.Customers,
            PosSynchronizationStreams.Security => PosSynchronizationTrigger.Security,
            PosSynchronizationStreams.FiscalStatus => PosSynchronizationTrigger.FiscalStatus,
            PosSynchronizationStreams.FiscalProvisioning => PosSynchronizationTrigger.FiscalProvisioning,
            PosSynchronizationStreams.LocalOutbox => PosSynchronizationTrigger.LocalOutbox,
            PosSynchronizationStreams.Authentication => PosSynchronizationTrigger.Security,
            PosSynchronizationStreams.Configuration => PosSynchronizationTrigger.Configuration,
            _ => PosSynchronizationTrigger.None
        };
}

internal sealed class PosEventDrivenSynchronizationHostedService(
    PosWebPubSubConnection push,
    PosSynchronizationSignal signal,
    PosSynchronizationWork work,
    PosSynchronizationState state,
    PosUiStateSignal uiState,
    PosCatalogStore catalog,
    PosSynchronizationEventLog events,
    ILogger<PosEventDrivenSynchronizationHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial identity/catalog sync must not wait for the push channel.
        // A temporary Web PubSub outage must not leave the POS login empty.
        _ = ConnectAsync(stoppingToken);
        signal.Signal(PosSynchronizationTrigger.All);
        var automaticRetryAttempts = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var trigger = await signal.ReadAsync(stoppingToken);
            var isManual = trigger.HasFlag(PosSynchronizationTrigger.Manual);
            trigger &= ~PosSynchronizationTrigger.AutomaticRetry;
            if (isManual) automaticRetryAttempts = 0;
            var preservePreparationFailure = false;
            if (!isManual && await catalog.IsPreparationPausedAsync(stoppingToken))
            {
                var operational = trigger & (PosSynchronizationTrigger.LocalOutbox |
                    PosSynchronizationTrigger.FiscalStatus | PosSynchronizationTrigger.FiscalProvisioning);
                if (operational == PosSynchronizationTrigger.None)
                {
                    logger.LogInformation(
                        "POS preparation remains paused after a failure until a manual retry is requested.");
                    continue;
                }
                trigger = operational;
                preservePreparationFailure = true;
            }
            try
            {
                var result = await work.ExecuteAsync(
                    trigger, stoppingToken, preservePreparationFailure);
                var masterDataRequested = (trigger & (
                    PosSynchronizationTrigger.Catalog |
                    PosSynchronizationTrigger.Customers |
                    PosSynchronizationTrigger.Configuration |
                    PosSynchronizationTrigger.Security)) != PosSynchronizationTrigger.None;
                if (result.Succeeded)
                {
                    automaticRetryAttempts = 0;
                    await catalog.SetPreparationPausedAsync(false, stoppingToken);
                }
                else if (result.HasRetryableFailure && !result.HasPermanentFailure)
                {
                    var delay = PosSynchronizationRetryPolicy.NextDelay(automaticRetryAttempts);
                    if (delay is not null)
                    {
                        await catalog.SetPreparationPausedAsync(false, stoppingToken);
                        automaticRetryAttempts++;
                        state.RetryScheduled(automaticRetryAttempts);
                        uiState.Publish();
                        signal.Schedule(
                            result.RetryableTriggers | PosSynchronizationTrigger.AutomaticRetry,
                            delay.Value,
                            stoppingToken);
                        logger.LogInformation(
                            "POS synchronization retry {Attempt} of 3 scheduled in {Delay}.",
                            automaticRetryAttempts,
                            delay.Value);
                    }
                    else
                    {
                        state.AutomaticRetriesExhausted();
                        if (masterDataRequested)
                            await catalog.SetPreparationPausedAsync(true, stoppingToken);
                        events.Record(
                            "Warning",
                            "Synchronization",
                            "No fue posible sincronizar después de tres reintentos",
                            "Usa Reintentar ahora o repite el enrolamiento.");
                        uiState.Publish();
                    }
                }
                else if (masterDataRequested)
                {
                    await catalog.SetPreparationPausedAsync(true, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Event-driven POS synchronization stopped and requires a manual retry.");
                state.StageFailed(
                    "sincronización",
                    "La preparación se detuvo por un error inesperado. Pulsa Reintentar; si se repite, informa al supervisor.");
                state.Failed();
                if ((await catalog.StatusAsync(stoppingToken)).Status != "Ready")
                    await catalog.SetPreparationPausedAsync(true, stoppingToken);
                uiState.Publish();
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await push.StopAsync();
        await base.StopAsync(cancellationToken);
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var failedAttempts = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await push.StartAsync(cancellationToken);
                failedAttempts = 0;
                await push.WaitForTerminalDisconnectionAsync(cancellationToken);
                // WebPubSub reports a terminal disconnect before its client state is
                // reusable. Complete the lifecycle explicitly; starting the same
                // instance again while it is still Disconnected throws and leaves the
                // enrolled checkout permanently without real-time synchronization.
                await push.StopAsync();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failedAttempts++;
                logger.LogInformation(
                    exception,
                    "The POS push channel is unavailable and will reconnect with backoff.");
                var delay = TimeSpan.FromSeconds(
                    Math.Min(60, Math.Pow(2, Math.Min(failedAttempts, 6))));
                await Task.Delay(delay, cancellationToken);
            }
        }
    }
}
