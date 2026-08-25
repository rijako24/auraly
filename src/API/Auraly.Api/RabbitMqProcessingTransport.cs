using System.Text;
using Auraly.Application.DocumentProcessing;
using Auraly.Application.Fiscal;
using Auraly.Application.Sales;
using Auraly.Commerce.Accounting.Application;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Auraly.Api;

public sealed record RabbitMqProcessingOptions(
    string ConnectionString,
    string DocumentQueueName,
    string FiscalQueueName,
    string AccountingQueueName,
    string SalesReportingQueueName);

public sealed class RabbitMqProcessingConnection(
    RabbitMqProcessingOptions options) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private IConnection? connection;

    public async Task<IConnection> GetAsync(CancellationToken cancellationToken)
    {
        if (connection is { IsOpen: true }) return connection;

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (connection is { IsOpen: true }) return connection;
            if (connection is not null) await connection.DisposeAsync();

            var factory = new ConnectionFactory
            {
                Uri = new Uri(options.ConnectionString, UriKind.Absolute),
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                ConsumerDispatchConcurrency = 1
            };
            connection = await factory.CreateConnectionAsync(
                "auraly-processing", cancellationToken);
            return connection;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IChannel> CreateChannelAsync(
        bool publisherConfirmations,
        CancellationToken cancellationToken)
    {
        var activeConnection = await GetAsync(cancellationToken);
        return await activeConnection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: publisherConfirmations,
                publisherConfirmationTrackingEnabled: publisherConfirmations),
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (connection is not null) await connection.DisposeAsync();
        gate.Dispose();
    }
}

public sealed class RabbitMqProcessingTransport(
    RabbitMqProcessingConnection connections,
    RabbitMqProcessingOptions options,
    TimeProvider timeProvider) :
    IDocumentProcessingSignalPublisher,
    IFiscalProcessingSignalPublisher,
    IAccountingProcessingSignalPublisher,
    ISalesReportingProcessingSignalPublisher,
    IAsyncDisposable
{
    private static readonly int[] RetryBucketsInSeconds = [2, 5, 15, 30, 120, 300];
    private readonly SemaphoreSlim publishGate = new(1, 1);
    private IChannel? channel;

    public async Task PublishAsync(
        DocumentProcessingSignal signal,
        CancellationToken cancellationToken = default)
    {
        DocumentProcessingSignalCodec.Validate(signal);
        await PublishAsync(
            options.DocumentQueueName,
            Encoding.UTF8.GetBytes(DocumentProcessingSignalCodec.Serialize(signal)),
            signal.MovementId,
            signal.BusinessId,
            signal.DocumentType,
            signal.DocumentId,
            cancellationToken);
    }

    public async Task PublishAsync(
        FiscalProcessingSignal signal,
        DateTimeOffset? scheduledEnqueueTime = null,
        CancellationToken cancellationToken = default)
    {
        FiscalProcessingSignalCodec.Validate(signal);
        var delay = scheduledEnqueueTime is null
            ? TimeSpan.Zero
            : scheduledEnqueueTime.Value - timeProvider.GetUtcNow();
        await PublishAsync(
            QueueForDelay(options.FiscalQueueName, delay),
            Encoding.UTF8.GetBytes(FiscalProcessingSignalCodec.Serialize(signal)),
            signal.SignalId,
            signal.BusinessId,
            signal.Stage.ToString(),
            signal.DocumentId,
            cancellationToken);
    }

    public async Task PublishAsync(
        AccountingProcessingSignal signal,
        CancellationToken cancellationToken = default)
    {
        AccountingProcessingSignalCodec.Validate(signal);
        await PublishAsync(
            options.AccountingQueueName,
            Encoding.UTF8.GetBytes(AccountingProcessingSignalCodec.Serialize(signal)),
            signal.SignalId,
            signal.BusinessId,
            signal.DocumentType,
            signal.DocumentId,
            cancellationToken);
    }

    public async Task PublishAsync(
        SalesReportingProcessingSignal signal,
        CancellationToken cancellationToken = default)
    {
        SalesReportingProcessingSignalCodec.Validate(signal);
        await PublishAsync(
            options.SalesReportingQueueName,
            Encoding.UTF8.GetBytes(SalesReportingProcessingSignalCodec.Serialize(signal)),
            signal.SignalId,
            signal.BusinessId,
            signal.DocumentType,
            signal.DocumentId,
            cancellationToken);
    }

    public Task RetryFiscalAsync(
        FiscalProcessingSignal signal,
        TimeSpan delay,
        CancellationToken cancellationToken) =>
        PublishAsync(
            QueueForDelay(options.FiscalQueueName, delay),
            Encoding.UTF8.GetBytes(FiscalProcessingSignalCodec.Serialize(signal)),
            signal.SignalId,
            signal.BusinessId,
            signal.Stage.ToString(),
            signal.DocumentId,
            cancellationToken);

    public async Task EnsureTopologyAsync(CancellationToken cancellationToken)
    {
        await publishGate.WaitAsync(cancellationToken);
        try
        {
            var activeChannel = await GetChannelAsync(cancellationToken);
            await DeclareQueueFamilyAsync(
                activeChannel, options.DocumentQueueName, false, cancellationToken);
            await DeclareQueueFamilyAsync(
                activeChannel, options.FiscalQueueName, true, cancellationToken);
            await DeclareQueueFamilyAsync(
                activeChannel, options.AccountingQueueName, false, cancellationToken);
            await DeclareQueueFamilyAsync(
                activeChannel, options.SalesReportingQueueName, false, cancellationToken);
        }
        finally
        {
            publishGate.Release();
        }
    }

    private async Task PublishAsync(
        string queueName,
        ReadOnlyMemory<byte> body,
        Guid messageId,
        Guid businessId,
        string messageType,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        await publishGate.WaitAsync(cancellationToken);
        try
        {
            var activeChannel = await GetChannelAsync(cancellationToken);
            await DeclareQueueFamilyAsync(
                activeChannel, options.DocumentQueueName, false, cancellationToken);
            await DeclareQueueFamilyAsync(
                activeChannel, options.FiscalQueueName, true, cancellationToken);
            await DeclareQueueFamilyAsync(
                activeChannel, options.AccountingQueueName, false, cancellationToken);
            await DeclareQueueFamilyAsync(
                activeChannel, options.SalesReportingQueueName, false, cancellationToken);
            var properties = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json",
                MessageId = messageId.ToString("D"),
                Type = messageType,
                Headers = new Dictionary<string, object?>
                {
                    ["businessId"] = businessId.ToString("D"),
                    ["documentId"] = documentId.ToString("D")
                }
            };
            await activeChannel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: queueName,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);
        }
        finally
        {
            publishGate.Release();
        }
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (channel is { IsOpen: true }) return channel;
        if (channel is not null) await channel.DisposeAsync();
        channel = await connections.CreateChannelAsync(true, cancellationToken);
        return channel;
    }

    private static async Task DeclareQueueFamilyAsync(
        IChannel target,
        string mainQueue,
        bool includeScheduledRetries,
        CancellationToken cancellationToken)
    {
        var deadQueue = $"{mainQueue}.dead";
        await target.QueueDeclareAsync(
            deadQueue, true, false, false, null, false, false, cancellationToken);
        await target.QueueDeclareAsync(
            mainQueue,
            true,
            false,
            false,
            new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = deadQueue
            },
            false,
            false,
            cancellationToken);
        if (!includeScheduledRetries) return;
        foreach (var seconds in RetryBucketsInSeconds)
        {
            await target.QueueDeclareAsync(
                RetryQueue(mainQueue, seconds),
                true,
                false,
                false,
                new Dictionary<string, object?>
                {
                    ["x-message-ttl"] = seconds * 1000,
                    ["x-dead-letter-exchange"] = string.Empty,
                    ["x-dead-letter-routing-key"] = mainQueue
                },
                false,
                false,
                cancellationToken);
        }
    }

    private static string QueueForDelay(string mainQueue, TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero) return mainQueue;
        var seconds = RetryBucketsInSeconds.FirstOrDefault(
            value => delay <= TimeSpan.FromSeconds(value));
        if (seconds == 0) seconds = RetryBucketsInSeconds[^1];
        return RetryQueue(mainQueue, seconds);
    }

    private static string RetryQueue(string mainQueue, int seconds) =>
        $"{mainQueue}.retry.{seconds}s";

    public async ValueTask DisposeAsync()
    {
        if (channel is not null) await channel.DisposeAsync();
        publishGate.Dispose();
    }
}

public sealed class RabbitMqDocumentProcessingHostedService(
    RabbitMqProcessingConnection connections,
    RabbitMqProcessingTransport transport,
    RabbitMqProcessingOptions options,
    IServiceScopeFactory scopeFactory,
    FiscalProcessingCoordinator fiscalProcessing,
    AccountingProcessingCoordinator accountingProcessing,
    SalesReportingProcessingCoordinator salesReporting,
    ILogger<RabbitMqDocumentProcessingHostedService> logger) : BackgroundService
{
    private const int MaximumAttempts = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await transport.EnsureTopologyAsync(stoppingToken);
        var channels = new List<IChannel>();
        try
        {
            var channel = await connections.CreateChannelAsync(false, stoppingToken);
            channels.Add(channel);
            await channel.BasicQosAsync(0, 1, false, stoppingToken);
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (_, args) => ProcessAsync(channel, args);
            await channel.BasicConsumeAsync(
                options.DocumentQueueName,
                autoAck: false,
                consumerTag: $"auraly-documents-{Environment.ProcessId}",
                noLocal: false,
                exclusive: false,
                arguments: null,
                consumer: consumer,
                cancellationToken: stoppingToken);

            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            foreach (var channel in channels) await channel.DisposeAsync();
        }
    }

    private async Task ProcessAsync(IChannel channel, BasicDeliverEventArgs args)
    {
        DocumentProcessingSignal? signal = null;
        try
        {
            signal = DocumentProcessingSignalCodec.Deserialize(
                Encoding.UTF8.GetString(args.Body.Span));
            DocumentProcessingSignalCodec.Validate(signal);
            ValidateBusinessHeader(args.BasicProperties, signal.BusinessId);

            for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var worker = scope.ServiceProvider
                        .GetRequiredService<DocumentProcessingWorker>();
                    var result = await worker.ProcessOneAsync(
                        signal, args.CancellationToken);
                    if (string.Equals(
                            signal.DocumentType,
                            Auraly.Contracts.Sales.PosSaleDocumentTypes.Invoice,
                            StringComparison.Ordinal) ||
                        string.Equals(signal.DocumentType,
                            Auraly.Contracts.Returns.SalesReturnDocumentTypes.SalesReturn,
                            StringComparison.Ordinal))
                        await fiscalProcessing.RequestGenerationAsync(
                            signal.BusinessId,
                            signal.DocumentId,
                            args.CancellationToken);
                    if (signal.EconomicEffectsEnabled && AccountingProcessingPolicy.Supports(signal.DocumentType))
                        await accountingProcessing.RequestPostingAsync(
                            signal.BusinessId,
                            signal.DocumentId,
                            signal.DocumentType,
                            args.CancellationToken);
                    if (signal.EconomicEffectsEnabled && SalesReportingProcessingPolicy.Supports(signal.DocumentType))
                        await salesReporting.RequestProjectionAsync(
                            signal.BusinessId,
                            signal.DocumentId,
                            signal.DocumentType,
                            args.CancellationToken);

                    await channel.BasicAckAsync(
                        args.DeliveryTag, false, args.CancellationToken);
                    logger.LogInformation(
                        "Movement {MovementId} completed with {Result} for business {BusinessId}.",
                        signal.MovementId, result, signal.BusinessId);
                    return;
                }
                catch (Exception exception) when (
                    attempt < MaximumAttempts &&
                    exception is not OperationCanceledException)
                {
                    logger.LogWarning(
                        exception,
                        "Movement {MovementId} failed on attempt {Attempt}; the ordered consumer remains blocked.",
                        signal.MovementId, attempt);
                    await Task.Delay(RetryDelay, args.CancellationToken);
                }
            }

            throw new InvalidOperationException(
                "The movement exhausted its processing attempts.");
        }
        catch (OperationCanceledException) when (args.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (signal is not null)
        {
            logger.LogError(
                exception,
                "Movement {MovementId} exhausted its attempts and was dead-lettered.",
                signal.MovementId);
            await channel.BasicNackAsync(
                args.DeliveryTag, false, false, args.CancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "An invalid document-processing message was dead-lettered.");
            await channel.BasicNackAsync(
                args.DeliveryTag, false, false, args.CancellationToken);
        }
    }

    internal static void ValidateBusinessHeader(
        IReadOnlyBasicProperties properties,
        Guid expectedBusinessId)
    {
        if (properties.Headers is null ||
            !properties.Headers.TryGetValue("businessId", out var value) ||
            !string.Equals(
                HeaderText(value),
                expectedBusinessId.ToString("D"),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "RabbitMQ businessId differs from the movement BusinessId.");
    }

    private static string? HeaderText(object? value) => value switch
    {
        byte[] bytes => Encoding.UTF8.GetString(bytes),
        ReadOnlyMemory<byte> memory => Encoding.UTF8.GetString(memory.Span),
        _ => value?.ToString()
    };
}

public sealed class RabbitMqFiscalProcessingHostedService(
    RabbitMqProcessingConnection connections,
    RabbitMqProcessingTransport transport,
    RabbitMqProcessingOptions options,
    IServiceScopeFactory scopes,
    FiscalProcessingCoordinator processing,
    TimeProvider timeProvider,
    ILogger<RabbitMqFiscalProcessingHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan WorkLease = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private readonly string workerId = $"{Environment.MachineName}:fiscal-rabbit:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await transport.EnsureTopologyAsync(stoppingToken);
        var channel = await connections.CreateChannelAsync(false, stoppingToken);
        await using (channel)
        {
            await channel.BasicQosAsync(0, 1, false, stoppingToken);
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (_, args) => ProcessAsync(channel, args);
            await channel.BasicConsumeAsync(
                options.FiscalQueueName,
                autoAck: false,
                consumerTag: $"auraly-fiscal-{Environment.ProcessId}",
                noLocal: false,
                exclusive: false,
                arguments: null,
                consumer: consumer,
                cancellationToken: stoppingToken);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }
    }

    private async Task ProcessAsync(IChannel channel, BasicDeliverEventArgs args)
    {
        FiscalProcessingSignal? signal = null;
        try
        {
            signal = FiscalProcessingSignalCodec.Deserialize(
                Encoding.UTF8.GetString(args.Body.Span));
            FiscalProcessingSignalCodec.Validate(signal);
            RabbitMqDocumentProcessingHostedService.ValidateBusinessHeader(
                args.BasicProperties, signal.BusinessId);

            await using var scope = scopes.CreateAsyncScope();
            if (signal.Stage == FiscalProcessingStage.Generation)
            {
                var worker = scope.ServiceProvider.GetRequiredService<FiscalGenerationWorker>();
                var generated = await worker.ProcessAsync(
                    signal.BusinessId, signal.DocumentId, workerId, args.CancellationToken);
                if (generated)
                    await processing.RequestSubmissionAsync(
                        signal.BusinessId, signal.DocumentId,
                        cancellationToken: args.CancellationToken);
                else
                {
                    var store = scope.ServiceProvider
                        .GetRequiredService<IFiscalGenerationWorkStore>();
                    var resumeAt = await store.GetResumeAtAsync(
                        signal.BusinessId, signal.DocumentId,
                        timeProvider.GetUtcNow(), WorkLease, args.CancellationToken);
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
                    signal.BusinessId, signal.DocumentId, workerId, args.CancellationToken);
                if (!result.WorkFound)
                {
                    var store = scope.ServiceProvider
                        .GetRequiredService<IFiscalSubmissionWorkStore>();
                    var resumeAt = await store.GetResumeAtAsync(
                        signal.BusinessId, signal.DocumentId,
                        timeProvider.GetUtcNow(), WorkLease, args.CancellationToken);
                    if (resumeAt is not null)
                        await processing.RequestSubmissionAsync(
                            signal.BusinessId, signal.DocumentId,
                            resumeAt.Value, args.CancellationToken);
                }
                if (result.NextAttemptAt is DateTimeOffset nextAttemptAt)
                    await processing.RequestSubmissionAsync(
                        signal.BusinessId, signal.DocumentId,
                        nextAttemptAt, args.CancellationToken);
            }

            await channel.BasicAckAsync(args.DeliveryTag, false, args.CancellationToken);
        }
        catch (OperationCanceledException) when (args.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (signal is not null)
        {
            await transport.RetryFiscalAsync(signal, RetryDelay, args.CancellationToken);
            await channel.BasicAckAsync(args.DeliveryTag, false, args.CancellationToken);
            logger.LogError(
                exception,
                "Fiscal {Stage} for {DocumentId} was durably rescheduled.",
                signal.Stage, signal.DocumentId);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "An invalid fiscal-processing message was dead-lettered.");
            await channel.BasicNackAsync(
                args.DeliveryTag, false, false, args.CancellationToken);
        }
    }
}
