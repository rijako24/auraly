using Auraly.Application.DocumentProcessing;
using Auraly.Application.Dispatching;
using Auraly.Application.Fiscal;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.BuildingBlocks.Infrastructure.Identifiers;
using Auraly.BuildingBlocks.Infrastructure.Persistence;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Contracts.Dispatching;
using Auraly.Infrastructure.Fiscal;
using Auraly.Infrastructure.Persistence;
using Auraly.Platform.Infrastructure.Processing;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);
var sql = builder.Configuration.GetConnectionString("Auraly");
if (string.IsNullOrWhiteSpace(sql))
    throw new InvalidOperationException("ConnectionStrings:Auraly is required.");

builder.Services.AddSingleton(new AuralySqlConnectionSource(sql));
builder.Services.AddSingleton<SqlServerConnectionFactory>();
builder.Services.AddSingleton<SqlDatabaseConnectivityProbe>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAuralyIdGenerator, Uuid7AuralyIdGenerator>();
builder.Services.AddSingleton<AccountingSqlConnectionFactory>();

var configuration = builder.Configuration;
var documentQueue = Required("DocumentProcessingQueueName");
builder.Services.AddSingleton(_ => AzureRuntimeClientFactory.CreateServiceBusClient(configuration));
builder.Services.AddSingleton(services => services.GetRequiredService<ServiceBusClient>().CreateSender(documentQueue));
if (configuration.GetValue("Auraly:DocumentProcessing:DurableActivation:Enabled", false))
    builder.Services.AddSingleton<IDocumentInitialSignalPublicationStore, SqlDocumentInitialSignalPublicationStore>();
if (configuration.GetValue("Auraly:DocumentProcessing:DurableContinuation:Enabled", false))
    builder.Services.AddSingleton<IDocumentContinuationSignalPublicationStore, SqlDocumentContinuationSignalPublicationStore>();
builder.Services.AddSingleton<ServiceBusDocumentProcessingPublisher>();
builder.Services.AddSingleton<IDocumentProcessingSignalPublisher>(services =>
    services.GetRequiredService<ServiceBusDocumentProcessingPublisher>());
builder.Services.AddSingleton(new ServiceBusCommerceProcessingOptions(
    Required("FiscalProcessingQueueName"), Required("AccountingProcessingQueueName"),
    Required("SalesReportingQueueName")));
builder.Services.AddSingleton<ServiceBusCommerceProcessingPublisher>();
builder.Services.AddSingleton<IDispatchSettlementSignalPublisher>(services =>
    services.GetRequiredService<ServiceBusCommerceProcessingPublisher>());
if (configuration.GetValue(DispatchSettlementQueue.EnabledSetting, false))
{
    if (!configuration.GetValue("Auraly:DocumentProcessing:DurableActivation:Enabled", false)
        || !configuration.GetValue("Auraly:DocumentProcessing:DurableContinuation:Enabled", false))
        throw new InvalidOperationException("Queued dispatch settlement requires durable document publication.");
    builder.Services.AddSingleton<SqlDispatchSettlementActivation>();
}
builder.Services.AddSingleton<IFiscalProcessingSignalPublisher>(services =>
    services.GetRequiredService<ServiceBusCommerceProcessingPublisher>());
builder.Services.AddSingleton<IAccountingProcessingSignalPublisher>(services =>
    services.GetRequiredService<ServiceBusCommerceProcessingPublisher>());
builder.Services.AddSingleton<ISalesReportingProcessingSignalPublisher>(services =>
    services.GetRequiredService<ServiceBusCommerceProcessingPublisher>());
builder.Services.AddSingleton<FiscalProcessingCoordinator>();
builder.Services.AddSingleton<AccountingProcessingCoordinator>();
builder.Services.AddSingleton<SalesReportingProcessingCoordinator>();
builder.Services.AddSingleton<IAccountingProcessingSignalGate, SqlAccountingProcessingSignalGate>();

builder.Services.AddSingleton(_ => AzureRuntimeClientFactory.CreateWebPubSubServiceClient(
    configuration,
    configuration["Auraly:PosSynchronization:WebPubSub:ConnectionString"],
    configuration["Auraly:PosSynchronization:WebPubSub:Endpoint"],
    Required("Auraly:PosSynchronization:WebPubSub:Hub")));
builder.Services.AddSingleton<IPosSynchronizationPushGateway, AzureWebPubSubSynchronizationGateway>();
builder.Services.AddSingleton(services =>
    ActivatorUtilities.CreateInstance<SqlPosSynchronizationOutboxDispatcher>(services, true));
builder.Services.AddSingleton<IPosSynchronizationOutboxDispatcher>(services =>
    services.GetRequiredService<SqlPosSynchronizationOutboxDispatcher>());

builder.Services.AddWithholdingCalculation();
builder.Services.AddDocumentProcessingExecution();
builder.Services.AddDispatchSettlementExecution();
builder.Services.AddAccountingProcessingExecution();
builder.Services.AddFiscalProcessingPersistence();
builder.Services.AddFiscalProcessingExecution<SqlProtectedFiscalCredentialVault>(
    configuration, builder.Environment.IsDevelopment());
builder.Services.AddSalesReportingExecution();

using var host = builder.Build();
using (var scope = host.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    _ = services.GetRequiredService<DocumentProcessingWorker>();
    _ = services.GetRequiredService<DispatchSettlementPreparation>();
    _ = services.GetRequiredService<SqlAccountingPostingProcessor>();
    _ = services.GetRequiredService<FiscalProcessingMessageHandler>();
    _ = services.GetRequiredService<SqlSalesReportingProcessor>();
}
await host.RunAsync();

string Required(string setting) =>
    configuration[setting] is { } value && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new InvalidOperationException($"Configure {setting}.");
