using System.Text.Json;
using Auraly.Application.Fiscal;
using Azure.Messaging.ServiceBus;

namespace Auraly.Api;

public sealed record FiscalProcessingServiceBusOptions(string QueueName);

public sealed class ServiceBusFiscalProcessingPublisher(
    ServiceBusClient client,
    FiscalProcessingServiceBusOptions options)
    : IFiscalProcessingSignalPublisher, IAsyncDisposable
{
    private readonly ServiceBusSender sender = client.CreateSender(options.QueueName);

    public async Task PublishAsync(
        FiscalProcessingSignal signal,
        DateTimeOffset? scheduledEnqueueTime = null,
        CancellationToken cancellationToken = default)
    {
        FiscalProcessingSignalCodec.Validate(signal);
        var message = new ServiceBusMessage(BinaryData.FromString(
            FiscalProcessingSignalCodec.Serialize(signal)))
        {
            MessageId = signal.SignalId.ToString("D"),
            SessionId = signal.BusinessId.ToString("D"),
            Subject = signal.Stage.ToString(),
            ContentType = "application/json"
        };
        message.ApplicationProperties["documentId"] = signal.DocumentId.ToString("D");
        if (scheduledEnqueueTime is null)
            await sender.SendMessageAsync(message, cancellationToken);
        else
            await sender.ScheduleMessageAsync(
                message,
                scheduledEnqueueTime.Value,
                cancellationToken);
    }

    public ValueTask DisposeAsync() => sender.DisposeAsync();
}

public sealed class FiscalProcessingHostedService(
    ServiceBusClient client,
    FiscalProcessingServiceBusOptions options,
    IServiceScopeFactory scopes,
    FiscalProcessingCoordinator processing,
    TimeProvider timeProvider,
    ILogger<FiscalProcessingHostedService> logger) : BackgroundService
{
    private const int MaximumDeliveries = 5;
    private static readonly TimeSpan WorkLease = TimeSpan.FromMinutes(2);
    private readonly string workerId = $"{Environment.MachineName}:fiscal:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var processor = client.CreateSessionProcessor(
            options.QueueName,
            new ServiceBusSessionProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentSessions = 16,
                MaxConcurrentCallsPerSession = 1,
                MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(10),
                PrefetchCount = 0
            });
        processor.ProcessMessageAsync += ProcessMessageAsync;
        processor.ProcessErrorAsync += ProcessErrorAsync;
        await processor.StartProcessingAsync(stoppingToken);
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await processor.StopProcessingAsync(CancellationToken.None);
        }
    }

    private async Task ProcessMessageAsync(ProcessSessionMessageEventArgs args)
    {
        FiscalProcessingSignal? signal = null;
        try
        {
            signal = FiscalProcessingSignalCodec.Deserialize(args.Message.Body.ToString());
            FiscalProcessingSignalCodec.Validate(signal);
            if (!string.Equals(
                    args.Message.SessionId,
                    signal.BusinessId.ToString("D"),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Service Bus SessionId differs from the fiscal BusinessId.");

            await using var scope = scopes.CreateAsyncScope();
            if (signal.Stage == FiscalProcessingStage.Generation)
            {
                var worker = scope.ServiceProvider.GetRequiredService<FiscalGenerationWorker>();
                var generated = await worker.ProcessAsync(
                    signal.BusinessId,
                    signal.DocumentId,
                    workerId,
                    args.CancellationToken);
                if (generated)
                    await processing.RequestSubmissionAsync(
                        signal.BusinessId,
                        signal.DocumentId,
                        cancellationToken: args.CancellationToken);
                else
                {
                    var store = scope.ServiceProvider
                        .GetRequiredService<IFiscalGenerationWorkStore>();
                    var resumeAt = await store.GetResumeAtAsync(
                        signal.BusinessId,
                        signal.DocumentId,
                        timeProvider.GetUtcNow(),
                        WorkLease,
                        args.CancellationToken);
                    if (resumeAt is not null)
                        await processing.ScheduleGenerationAsync(
                            signal.BusinessId, signal.DocumentId,
                            resumeAt.Value, args.CancellationToken);
                }
            }
            else
            {
                var worker = scope.ServiceProvider.GetRequiredService<FiscalSubmissionWorker>();
                var result = await worker.ProcessAsync(
                    signal.BusinessId,
                    signal.DocumentId,
                    workerId,
                    args.CancellationToken);
                if (!result.WorkFound)
                {
                    var store = scope.ServiceProvider
                        .GetRequiredService<IFiscalSubmissionWorkStore>();
                    var resumeAt = await store.GetResumeAtAsync(
                        signal.BusinessId, signal.DocumentId,
                        timeProvider.GetUtcNow(), WorkLease,
                        args.CancellationToken);
                    if (resumeAt is not null)
                        await processing.RequestSubmissionAsync(
                            signal.BusinessId, signal.DocumentId,
                            resumeAt.Value, args.CancellationToken);
                }
                if (result.NextAttemptAt is DateTimeOffset nextAttemptAt)
                    await processing.RequestSubmissionAsync(
                        signal.BusinessId,
                        signal.DocumentId,
                        nextAttemptAt,
                        args.CancellationToken);
            }

            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        }
        catch (OperationCanceledException) when (args.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Fiscal {Stage} failed for document {DocumentId} on delivery {DeliveryCount}.",
                signal?.Stage,
                signal?.DocumentId,
                args.Message.DeliveryCount);
            if (args.Message.DeliveryCount >= MaximumDeliveries)
            {
                await args.DeadLetterMessageAsync(
                    args.Message,
                    "FiscalProcessingNeedsIntervention",
                    "The fiscal document could not be processed after five deliveries.",
                    args.CancellationToken);
                return;
            }

            await args.AbandonMessageAsync(
                args.Message,
                cancellationToken: args.CancellationToken);
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogError(
            args.Exception,
            "Fiscal Service Bus processing failed for {EntityPath} from {ErrorSource}.",
            args.EntityPath,
            args.ErrorSource);
        return Task.CompletedTask;
    }
}

internal static class FiscalProcessingSignalCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(FiscalProcessingSignal signal) =>
        JsonSerializer.Serialize(signal, Options);

    public static FiscalProcessingSignal Deserialize(string value) =>
        JsonSerializer.Deserialize<FiscalProcessingSignal>(value, Options)
        ?? throw new InvalidOperationException("The fiscal-processing signal is invalid.");

    public static void Validate(FiscalProcessingSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (signal.SignalId == Guid.Empty ||
            signal.BusinessId == Guid.Empty ||
            signal.DocumentId == Guid.Empty ||
            !Enum.IsDefined(signal.Stage))
            throw new InvalidOperationException(
                "The fiscal-processing signal has invalid identifiers or stage.");
    }
}
