using Auraly.Application.Fiscal;
using Auraly.Application.Sales;
using Auraly.Commerce.Accounting.Application;
using Azure.Messaging.ServiceBus;
using System.Text.Json;

namespace Auraly.Platform.Infrastructure.Processing;

public sealed record ServiceBusCommerceProcessingOptions(
    string FiscalQueueName,
    string AccountingQueueName,
    string SalesReportingQueueName);

/// <summary>
/// Canonical Service Bus transport shared by every host that can confirm a
/// commercial payment. Processing ownership remains in the fiscal,
/// accounting and reporting engines; this adapter only publishes their
/// existing signals.
/// </summary>
public sealed class ServiceBusCommerceProcessingPublisher(
    ServiceBusClient client,
    ServiceBusCommerceProcessingOptions options)
    : IFiscalProcessingSignalPublisher,
      IAccountingProcessingSignalPublisher,
      ISalesReportingProcessingSignalPublisher,
      IAsyncDisposable
{
    private readonly ServiceBusSender fiscal =
        client.CreateSender(options.FiscalQueueName);
    private readonly ServiceBusSender accounting =
        client.CreateSender(options.AccountingQueueName);
    private readonly ServiceBusSender reporting =
        client.CreateSender(options.SalesReportingQueueName);

    public async Task PublishAsync(
        FiscalProcessingSignal signal,
        DateTimeOffset? scheduledEnqueueTime = null,
        CancellationToken cancellationToken = default)
    {
        FiscalProcessingSignalCodec.Validate(signal);
        var message = CreateMessage(
            FiscalProcessingSignalCodec.Serialize(signal),
            signal.SignalId,
            signal.BusinessId,
            signal.DocumentId,
            signal.Stage.ToString());
        if (scheduledEnqueueTime is null)
            await fiscal.SendMessageAsync(message, cancellationToken);
        else
            await fiscal.ScheduleMessageAsync(
                message, scheduledEnqueueTime.Value, cancellationToken);
    }

    public Task PublishAsync(
        AccountingProcessingSignal signal,
        CancellationToken cancellationToken = default)
    {
        AccountingProcessingSignalCodec.Validate(signal);
        return accounting.SendMessageAsync(
            CreateMessage(
                AccountingProcessingSignalCodec.Serialize(signal),
                signal.SignalId,
                signal.BusinessId,
                signal.DocumentId,
                signal.DocumentType),
            cancellationToken);
    }

    public Task PublishAsync(
        SalesReportingProcessingSignal signal,
        CancellationToken cancellationToken = default)
    {
        SalesReportingProcessingSignalCodec.Validate(signal);
        return reporting.SendMessageAsync(
            CreateMessage(
                SalesReportingProcessingSignalCodec.Serialize(signal),
                signal.SignalId,
                signal.BusinessId,
                signal.DocumentId,
                signal.DocumentType),
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await fiscal.DisposeAsync();
        await accounting.DisposeAsync();
        await reporting.DisposeAsync();
    }

    private static ServiceBusMessage CreateMessage(
        string payload,
        Guid signalId,
        Guid businessId,
        Guid documentId,
        string subject)
    {
        var message = new ServiceBusMessage(BinaryData.FromString(payload))
        {
            MessageId = signalId.ToString("D"),
            SessionId = businessId.ToString("D"),
            Subject = subject,
            ContentType = "application/json"
        };
        message.ApplicationProperties["documentId"] = documentId.ToString("D");
        return message;
    }
}

public static class FiscalProcessingSignalCodec
{
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web);

    public static string Serialize(FiscalProcessingSignal signal) =>
        JsonSerializer.Serialize(signal, Options);

    public static FiscalProcessingSignal Deserialize(string value) =>
        JsonSerializer.Deserialize<FiscalProcessingSignal>(value, Options)
        ?? throw new InvalidOperationException(
            "The fiscal-processing signal is invalid.");

    public static void Validate(FiscalProcessingSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (signal.SignalId == Guid.Empty || signal.BusinessId == Guid.Empty ||
            signal.DocumentId == Guid.Empty || !Enum.IsDefined(signal.Stage))
            throw new InvalidOperationException(
                "The fiscal-processing signal has invalid identifiers or stage.");
    }
}

public static class AccountingProcessingSignalCodec
{
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web);

    public static string Serialize(AccountingProcessingSignal signal) =>
        JsonSerializer.Serialize(signal, Options);

    public static AccountingProcessingSignal Deserialize(string value) =>
        JsonSerializer.Deserialize<AccountingProcessingSignal>(value, Options)
        ?? throw new InvalidOperationException(
            "The accounting-processing signal is invalid.");

    public static void Validate(AccountingProcessingSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (signal.SignalId == Guid.Empty || signal.BusinessId == Guid.Empty ||
            signal.DocumentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(signal.DocumentType) ||
            signal.DocumentType.Length > 64 ||
            !AccountingProcessingPolicy.Supports(signal.DocumentType))
            throw new InvalidOperationException(
                "The accounting-processing signal has invalid identifiers or document type.");
    }
}

public static class SalesReportingProcessingSignalCodec
{
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web);

    public static string Serialize(SalesReportingProcessingSignal signal) =>
        JsonSerializer.Serialize(signal, Options);

    public static SalesReportingProcessingSignal Deserialize(string value) =>
        JsonSerializer.Deserialize<SalesReportingProcessingSignal>(value, Options)
        ?? throw new InvalidOperationException(
            "The sales-reporting signal is invalid.");

    public static void Validate(SalesReportingProcessingSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (signal.SignalId == Guid.Empty || signal.BusinessId == Guid.Empty ||
            signal.DocumentId == Guid.Empty || signal.SourceVersion <= 0 ||
            !SalesReportingProcessingPolicy.Supports(signal.DocumentType))
            throw new InvalidOperationException(
                "The sales-reporting signal has invalid identifiers or document type.");
    }
}
