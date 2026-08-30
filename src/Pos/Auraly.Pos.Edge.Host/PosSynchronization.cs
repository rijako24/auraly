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
    Authentication = 16,
    FiscalProvisioning = 32,
    Approvals = 64,
    All = Catalog | Security | FiscalStatus | LocalOutbox | Authentication | FiscalProvisioning | Approvals
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
    PosEdgeOutboxUploader uploader,
    PosCashMovementServerClient cashMovements,
    PosWorkSessionClosureUploader closures,
    PosUnifiedOutboxDispatcher outbox,
    PosFiscalStatusSynchronizer fiscalStatuses,
    PosFiscalProvisioningSynchronizer fiscalProvisioning,
    PosEdgeAuthenticationService authentication,
    PosUiStateSignal uiState,
    PosSynchronizationState state,
    PosSynchronizationEventLog events,
    PosSynchronizationSignal signal)
{
    public async Task ExecuteAsync(
        PosSynchronizationTrigger trigger,
        CancellationToken cancellationToken)
    {
        state.Begin();
        events.Record("Info", "Synchronization", "Sincronización iniciada", trigger.ToString());
        uiState.Publish();
        try
        {
            if (trigger.HasFlag(PosSynchronizationTrigger.Security))
                await identities.SynchronizeAsync(cancellationToken);
            if (trigger.HasFlag(PosSynchronizationTrigger.Catalog))
                await catalog.SynchronizeAsync(cancellationToken);
            if (trigger.HasFlag(PosSynchronizationTrigger.LocalOutbox))
            {
                while (await outbox.NextAsync(cancellationToken) is { } route)
                {
                    var dispatched = route switch
                    {
                        PosUnifiedOutboxRoute.Sale =>
                            await uploader.UploadNextAsync(cancellationToken),
                        PosUnifiedOutboxRoute.CashMovement =>
                            await cashMovements.UploadNextAsync(cancellationToken),
                        PosUnifiedOutboxRoute.WorkSessionClosure =>
                            await closures.UploadNextAsync(cancellationToken),
                        _ => throw new ArgumentOutOfRangeException(nameof(route))
                    };
                    if (!dispatched) break;
                }
                if (await outbox.NextRetryDelayAsync(cancellationToken) is { } delay)
                    signal.Schedule(
                        PosSynchronizationTrigger.LocalOutbox,
                        delay,
                        cancellationToken);
            }
            if (trigger.HasFlag(PosSynchronizationTrigger.Authentication))
            {
                while (await authentication.ReleasePendingAsync(cancellationToken))
                {
                }
            }
            if (trigger.HasFlag(PosSynchronizationTrigger.FiscalStatus))
                await fiscalStatuses.SynchronizeAsync(cancellationToken);
            if (trigger.HasFlag(PosSynchronizationTrigger.FiscalProvisioning))
                await fiscalProvisioning.SynchronizeAsync(cancellationToken);
            state.Succeeded();
            events.Record("Success", "Synchronization", "Sincronización completada", trigger.ToString());
        }
        catch (Exception exception)
        {
            state.Failed();
            events.Record("Error", "Synchronization", "Falló la sincronización", exception.Message);
            throw;
        }
        finally { uiState.Publish(); }
    }
}

public sealed record PosSynchronizationNegotiation(
    Uri ClientAccessUri,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string>? Groups = null);

public sealed class PosWebPubSubConnection : IAsyncDisposable
{
    private readonly HttpClient http;
    private readonly PosDeviceCredentials credentials;
    private readonly PosSynchronizationSignal signal;
    private readonly PosServerConnectionState connectionState;
    private readonly PosPushConnectionState pushState;
    private readonly PosUiStateSignal uiState;
    private readonly PosSynchronizationEventLog events;
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

    private Task OnGroupMessageReceivedAsync(
        WebPubSubGroupMessageEventArgs args)
    {
        try
        {
            var invalidation = args.Message.Data
                .ToObjectFromJson<PosSynchronizationInvalidation>();
            if (invalidation is null ||
                invalidation.TenantId != tenantId ||
                invalidation.BusinessId != businessId)
                return Task.CompletedTask;
            signal.Signal(ToTrigger(invalidation.Stream));
            events.Record("Info", "Push", "Evento recibido del servidor", invalidation.Stream);
        }
        catch (System.Text.Json.JsonException)
        {
        }
        return Task.CompletedTask;
    }

    private static PosSynchronizationTrigger ToTrigger(string stream) =>
        stream switch
        {
            PosSynchronizationStreams.Catalog => PosSynchronizationTrigger.Catalog,
            PosSynchronizationStreams.Customers => PosSynchronizationTrigger.Catalog,
            PosSynchronizationStreams.Security => PosSynchronizationTrigger.Security,
            PosSynchronizationStreams.FiscalStatus => PosSynchronizationTrigger.FiscalStatus,
            PosSynchronizationStreams.FiscalProvisioning => PosSynchronizationTrigger.FiscalProvisioning,
            PosSynchronizationStreams.LocalOutbox => PosSynchronizationTrigger.LocalOutbox,
            PosSynchronizationStreams.Authentication => PosSynchronizationTrigger.Authentication,
            PosSynchronizationStreams.Approvals => PosSynchronizationTrigger.Approvals,
            _ => PosSynchronizationTrigger.None
        };
}

internal sealed class PosEventDrivenSynchronizationHostedService(
    PosWebPubSubConnection push,
    PosSynchronizationSignal signal,
    PosSynchronizationWork work,
    ILogger<PosEventDrivenSynchronizationHostedService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial identity/catalog sync must not wait for the push channel.
        // A temporary Web PubSub outage must not leave the POS login empty.
        _ = ConnectAsync(stoppingToken);
        signal.Signal(PosSynchronizationTrigger.All);
        var failedAttempts = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var trigger = await signal.ReadAsync(stoppingToken);
            try
            {
                await work.ExecuteAsync(trigger, stoppingToken);
                failedAttempts = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                failedAttempts++;
                logger.LogWarning(
                    exception,
                    "Event-driven POS synchronization failed; the same work remains durable and will retry.");
                var delay = TimeSpan.FromSeconds(
                    Math.Min(60, Math.Pow(2, Math.Min(failedAttempts, 6))));
                await Task.Delay(delay, stoppingToken);
                signal.Signal(trigger);
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
