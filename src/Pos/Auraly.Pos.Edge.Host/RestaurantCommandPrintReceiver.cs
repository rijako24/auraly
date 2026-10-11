using System.Net.Http.Json;
using System.Threading.Channels;
using Auraly.Contracts.Sales;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Pos.Edge.Host;

public sealed record RestaurantPrintClaim(Guid CardId, Guid LeaseToken,
    string ProfileKey, bool IsCopy, RestaurantCommandCardView Card,
    string PrintKind = "Original", decimal? RemovedQuantity = null,
    string? RemovedProductName = null);

public sealed class RestaurantCommandPrintSignal
{
    private readonly Channel<bool> channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { SingleReader = true,
            SingleWriter = false, FullMode = BoundedChannelFullMode.DropWrite });

    public void Wake() => channel.Writer.TryWrite(true);
    public ValueTask<bool> WaitAsync(CancellationToken ct) =>
        channel.Reader.ReadAsync(ct);
}

internal sealed class RestaurantCommandPrintReceiver(
    HttpClient http, PosDeviceCredentials credentials,
    RestaurantCommandPrintSignal signal,
    PosPrinterConfigurationStore configuration,
    RestaurantCommandTicketPrinter printer,
    PosSynchronizationEventLog events,
    ILogger<RestaurantCommandPrintReceiver> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        signal.Wake(); // Catch up work committed before this computer started.
        var automaticRetryAttempts = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            await signal.WaitAsync(stoppingToken);
            try
            {
                await DrainAsync(stoppingToken);
                automaticRetryAttempts = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            { break; }
            catch (Exception error)
            {
                logger.LogWarning(error, "Restaurant command print receiver could not claim jobs.");
                events.Record("Warning", "RestaurantPrint",
                    "No se pudieron recibir las comandas para imprimir", error.Message);
                // A transient failure gets the same finite backoff as the POS
                // synchronizer. Once exhausted, a fresh push, reconnection or
                // explicit configuration change must wake the receiver.
                var delay = PosSynchronizationRetryPolicy.NextDelay(
                    automaticRetryAttempts);
                if (delay is { } boundedDelay)
                {
                    automaticRetryAttempts++;
                    await Task.Delay(boundedDelay, stoppingToken);
                    signal.Wake();
                }
                else
                {
                    automaticRetryAttempts = 0;
                    events.Record("Warning", "RestaurantPrint",
                        "La impresión automática espera una nueva conexión o señal de comanda");
                }
            }
        }
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        // Existing cash registers have no restaurant profile. Their startup and
        // synchronization paths remain unchanged, with no extra server request.
        if (configuration.CommandProfiles().Count == 0) return;
        while (!ct.IsCancellationRequested)
        {
            using var claimRequest = new HttpRequestMessage(HttpMethod.Post,
                "/api/pos/v1/restaurant/print-jobs/claim");
            Authorize(claimRequest);
            using var claimResponse = await http.SendAsync(claimRequest, ct);
            claimResponse.EnsureSuccessStatusCode();
            var claims = await claimResponse.Content.ReadFromJsonAsync<
                List<RestaurantPrintClaim>>(cancellationToken: ct) ?? [];
            if (claims.Count == 0) return;
            foreach (var claim in claims)
            {
                var state = "Submitted";
                string? errorText = null;
                try
                {
                    // Claiming is exclusive and durable. Never repeat the physical
                    // print automatically after a crash or uncertain spooler result.
                    await printer.PrintAsync(new(claim.ProfileKey, claim.Card,
                        claim.IsCopy, claim.PrintKind,
                        claim.RemovedQuantity, claim.RemovedProductName), ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                { return; }
                catch (Exception error)
                {
                    state = error is ArgumentException or InvalidOperationException or
                        PlatformNotSupportedException ? "Failed" : "Uncertain";
                    errorText = state == "Failed" ? error.Message :
                        "Windows no pudo confirmar la impresión; revisa la tirilla antes de pedir una copia.";
                    logger.LogWarning(error,
                        "Restaurant command {CardId} did not reach the printer.",
                        claim.CardId);
                }
                using var ackRequest = new HttpRequestMessage(HttpMethod.Post,
                    $"/api/pos/v1/restaurant/print-jobs/{claim.CardId:D}/ack")
                {
                    Content = JsonContent.Create(new
                    {
                        claim.LeaseToken, State = state, Error = errorText
                    })
                };
                Authorize(ackRequest);
                try
                {
                    using var ackResponse = await http.SendAsync(ackRequest, ct);
                    ackResponse.EnsureSuccessStatusCode();
                    events.Record(state == "Submitted" ? "Success" : "Warning",
                        "RestaurantPrint", state == "Submitted"
                            ? "Comanda entregada al sistema de impresión"
                            : "No se pudo imprimir una comanda",
                        claim.CardId.ToString("D"));
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    // The server keeps Claimed. A physical retry would duplicate a
                    // ticket if Windows accepted the first request.
                    logger.LogWarning(error,
                        "Restaurant command {CardId} print acknowledgement is uncertain.",
                        claim.CardId);
                }
            }
        }
    }

    private void Authorize(HttpRequestMessage request)
    {
        request.Headers.Add("X-Auraly-Device-Id",
            credentials.DeviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", credentials.Secret);
    }
}
