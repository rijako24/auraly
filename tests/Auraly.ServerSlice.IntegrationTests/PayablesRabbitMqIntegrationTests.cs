using System.Net;
using System.Net.Http.Json;
using Auraly.Api;
using Auraly.Application.Fiscal;
using Auraly.Commerce.Accounting.Application;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Purchasing;
using Auraly.Application.Sales;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class PayablesRabbitMqIntegrationTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Real_broker_processes_a_supplier_payment_once()
    {
        var rabbitConnection = Environment.GetEnvironmentVariable("AURALY_TEST_RABBITMQ");
        if (string.IsNullOrWhiteSpace(rabbitConnection))
        {
            Assert.False(
                string.Equals(Environment.GetEnvironmentVariable("AURALY_REQUIRE_RABBITMQ_TEST"),
                    "1", StringComparison.Ordinal),
                "AURALY_TEST_RABBITMQ is required for the explicit RabbitMQ E2E run.");
            return;
        }

        var suffix = Guid.NewGuid().ToString("N");
        var documentQueue = $"auraly-tests-payables-{suffix}";
        var fiscalQueue = $"auraly-tests-payables-fiscal-{suffix}";
        var options = new RabbitMqProcessingOptions(
            rabbitConnection, documentQueue, fiscalQueue, $"auraly-tests-payables-accounting-{suffix}",
            $"auraly-tests-payables-reporting-{suffix}");
        await using var connection = new RabbitMqProcessingConnection(options);
        await using var transport = new RabbitMqProcessingTransport(
            connection, options, TimeProvider.System);
        var fiscal = new FiscalProcessingCoordinator(
            transport,
            fixture.Services.GetRequiredService<IAuralyIdGenerator>());
        var accounting = new AccountingProcessingCoordinator(
            transport,
            fixture.Services.GetRequiredService<IAuralyIdGenerator>());
        var reporting = new SalesReportingProcessingCoordinator(
            transport,
            fixture.Services.GetRequiredService<IAuralyIdGenerator>());
        using var service = new RabbitMqDocumentProcessingHostedService(
            connection,
            transport,
            options,
            fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            fiscal,
            accounting,
            reporting,
            NullLogger<RabbitMqDocumentProcessingHostedService>.Instance);

        fixture.PauseDocumentProcessing();
        try
        {
            using var client = fixture.CreateAdminClient(
                PurchasingPermissionCodes.CreateGoodsReceipts,
                PurchasingPermissionCodes.ConfirmGoodsReceipts,
                PayablesPermissionCodes.Read,
                PayablesPermissionCodes.RegisterPayment);
            var receivedAt = DateTimeOffset.UtcNow;
            var receipt = new ConfirmGoodsReceiptRequest(
                Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
                fixture.SupplierId, $"PAY-RABBIT-{Guid.NewGuid():N}",
                receivedAt.AddDays(-1), receivedAt, true, receivedAt.AddDays(29),
                "COP", "Obligacion para pago por RabbitMQ",
                [new GoodsReceiptLineRequest(
                    1, fixture.ProductId, "Producto de cartera RabbitMQ", 1m,
                    5_000m, 0m, "00", 0m,
                    PurchasingTaxTreatments.NotApplicable)]);
            await ConfirmReceiptAsync(client, receipt);
            var receiptSignal = Assert.Single(fixture.DrainDocumentSignals());
            await transport.PublishAsync(receiptSignal);
            await service.StartAsync(CancellationToken.None);
            await WaitUntilAsync(async () =>
                await ReadScalarAsync<string>(
                    "SELECT Status FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id",
                    receipt.DocumentId) == "Processed");

            var payableId = await ReadScalarAsync<Guid>(
                "SELECT PayableId FROM dbo.Payables WHERE SourceDocumentId=@Id",
                receipt.DocumentId);
            await SeedPaymentSeriesAsync();
            var payment = new ConfirmSupplierPaymentRequest(
                Guid.NewGuid(), fixture.BusinessId, fixture.SupplierId,
                receivedAt.AddHours(1), "COP", SupplierPaymentMethods.Cash,
                "RABBIT-CASH", "Pago procesado por el broker real",
                [new SupplierPaymentAllocationRequest(payableId, 2_000m)]);
            await ConfirmPaymentAsync(client, payment);
            var paymentSignal = Assert.Single(fixture.DrainDocumentSignals());
            Assert.Equal(PayablesDocumentTypes.Payment, paymentSignal.DocumentType);
            await transport.PublishAsync(paymentSignal);
            await WaitUntilAsync(async () =>
                await ReadScalarAsync<string>(
                    "SELECT Status FROM dbo.SupplierPayments WHERE PaymentId=@Id",
                    payment.PaymentId) == "Processed");

            Assert.Equal(3_000m, await ReadScalarAsync<decimal>(
                "SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id",
                payableId));
            Assert.Equal(1, await CountAsync(
                "PayableTransactions", "SourceDocumentId", payment.PaymentId));
            Assert.Equal(1, await CountAsync(
                "SupplierPaymentApplications", "PaymentId", payment.PaymentId));

            await transport.PublishAsync(paymentSignal);
            await WaitUntilAsync(async () =>
                await QueueCountAsync(connection, documentQueue) == 0);
            Assert.Equal(3_000m, await ReadScalarAsync<decimal>(
                "SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id",
                payableId));
            Assert.Equal(1, await CountAsync(
                "PayableTransactions", "SourceDocumentId", payment.PaymentId));
        }
        finally
        {
            fixture.ResumeDocumentProcessing();
            fixture.DrainDocumentSignals();
            try
            {
                await service.StopAsync(CancellationToken.None);
            }
            finally
            {
                await DeleteQueuesAsync(connection, documentQueue, fiscalQueue);
            }
        }
    }

    private static async Task ConfirmReceiptAsync(
        HttpClient client, ConfirmGoodsReceiptRequest request)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post, "/api/commerce/v1/goods-receipts/confirm")
        { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", $"rabbit-receipt-{request.DocumentId:N}");
        using var response = await client.SendAsync(message);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private static async Task ConfirmPaymentAsync(
        HttpClient client, ConfirmSupplierPaymentRequest request)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post, "/api/commerce/v1/payable-payments/confirm")
        { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", $"rabbit-payment-{request.PaymentId:N}");
        using var response = await client.SendAsync(message);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private async Task SeedPaymentSeriesAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF NOT EXISTS(
                SELECT 1 FROM dbo.DocumentSeries
                WHERE BusinessId=@BusinessId AND DocumentType=N'PayablePayment'
                  AND DeviceId IS NULL AND IsActive=1)
              INSERT dbo.DocumentSeries
                (DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,
                 Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
              VALUES(NEWID(),@BusinessId,NULL,N'PayablePayment',N'PGP',N'00',
                 8,1,99999999,0,1,SYSDATETIMEOFFSET());
            """;
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> ReadScalarAsync<T>(string sql, Guid id)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);
        return (T)Convert.ChangeType(
            await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Expected persisted test evidence."),
            typeof(T));
    }

    private async Task<int> CountAsync(string table, string column, Guid id)
    {
        Assert.Contains($"{table}:{column}", new[]
        {
            "PayableTransactions:SourceDocumentId",
            "SupplierPaymentApplications:PaymentId"
        });
        return await ReadScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.[{table}] WHERE [{column}]=@Id", id);
    }

    private static async Task<uint> QueueCountAsync(
        RabbitMqProcessingConnection connection, string queue)
    {
        await using var channel = await connection.CreateChannelAsync(false, default);
        return (await channel.QueueDeclarePassiveAsync(queue)).MessageCount;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!await condition())
            await Task.Delay(100, cancellation.Token);
    }

    private static async Task DeleteQueuesAsync(
        RabbitMqProcessingConnection connection, params string[] mainQueues)
    {
        await using var channel = await connection.CreateChannelAsync(false, default);
        foreach (var main in mainQueues)
        {
            foreach (var queue in new[] { main, $"{main}.dead" })
                await channel.QueueDeleteAsync(
                    queue, ifUnused: false, ifEmpty: false, noWait: false);
        }
    }
}
