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
    All = Catalog | Security | FiscalStatus | LocalOutbox | Authentication | FiscalProvisioning
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
    PosWorkSessionClosureOutboxUploader closures,
    PosFiscalStatusSynchronizer fiscalStatuses,
    PosFiscalProvisioningSynchronizer fiscalProvisioning,
    PosEdgeAuthenticationService authentication,
    PosUiStateSignal uiState,
    PosSynchronizationState state)
{
    public async Task ExecuteAsync(
        PosSynchronizationTrigger trigger,
        CancellationToken cancellationToken)
    {
        state.Begin();
        uiState.Publish();
        try
        {
            if (trigger.HasFlag(PosSynchronizationTrigger.Security))
                await identities.SynchronizeAsync(cancellationToken);
            if (trigger.HasFlag(PosSynchronizationTrigger.Catalog))
                await catalog.SynchronizeAsync(cancellationToken);
            if (trigger.HasFlag(PosSynchronizationTrigger.LocalOutbox))
            {
                while (await uploader.UploadNextAsync(cancellationToken))
                {
                }
                while (await cashMovements.UploadNextAsync(cancellationToken))
                {
                }
                while (await closures.UploadNextAsync(cancellationToken))
                {
                }
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
        }
        catch
        {
            state.Failed();
            throw;
        }
        finally { uiState.Publish(); }
    }
}

public sealed record PosSynchronizationNegotiation(
    Uri ClientAccessUri,
    DateTimeOffset ExpiresAt);

public sealed class PosWebPubSubConnection : IAsyncDisposable
{
    private readonly HttpClient http;
    private readonly PosDeviceCredentials credentials;
    private readonly PosSynchronizationSignal signal;
    private readonly PosServerConnectionState connectionState;
    private readonly PosUiStateSignal uiState;
    private readonly Guid tenantId;
    private readonly Guid businessId;
    private readonly WebPubSubClient client;

    public PosWebPubSubConnection(
        HttpClient http,
        PosDeviceCredentials credentials,
        PosSynchronizationSignal signal,
        PosServerConnectionState connectionState,
        PosUiStateSignal uiState,
        Guid tenantId,
        Guid businessId)
    {
        this.http = http;
        this.credentials = credentials;
        this.signal = signal;
        this.connectionState = connectionState;
        this.uiState = uiState;
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

    public Task StartAsync(CancellationToken cancellationToken) =>
        client.StartAsync(cancellationToken);

    public Task StopAsync() => client.StopAsync();

    public async ValueTask DisposeAsync() => await client.DisposeAsync();

    private async ValueTask<Uri> NegotiateAsync(
        CancellationToken cancellationToken)
    {
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
        return negotiation.ClientAccessUri;
    }

    private Task OnConnectedAsync(WebPubSubConnectedEventArgs _)
    {
        connectionState.MarkConnected();
        uiState.Publish();
        signal.Signal(PosSynchronizationTrigger.All);
        return Task.CompletedTask;
    }

    private Task OnDisconnectedAsync(WebPubSubDisconnectedEventArgs _)
    {
        connectionState.MarkDisconnected();
        uiState.Publish();
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
                return;
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
