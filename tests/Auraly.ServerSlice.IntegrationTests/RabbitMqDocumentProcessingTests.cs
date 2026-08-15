using System.Net;
using System.Net.Http.Json;
using Auraly.Api;
using Auraly.Application.Fiscal;
using Auraly.Commerce.Accounting.Application;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Inventory;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class RabbitMqDocumentProcessingTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Real_broker_preserves_order_processes_effects_once_and_dead_letters_failures()
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
        var documentQueue = $"auraly-tests-documents-{suffix}";
        var fiscalQueue = $"auraly-tests-fiscal-{suffix}";
        var options = new RabbitMqProcessingOptions(
            rabbitConnection, documentQueue, fiscalQueue, $"auraly-tests-accounting-{suffix}");
        await using var connection = new RabbitMqProcessingConnection(options);
        await using var transport = new RabbitMqProcessingTransport(
            connection, options, TimeProvider.System);
        var fiscal = new FiscalProcessingCoordinator(
            transport,
            fixture.Services.GetRequiredService<IAuralyIdGenerator>());
        var accounting = new AccountingProcessingCoordinator(
            transport,
            fixture.Services.GetRequiredService<IAuralyIdGenerator>());
        using var service = new RabbitMqDocumentProcessingHostedService(
            connection,
            transport,
            options,
            fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            fiscal,
            accounting,
            NullLogger<RabbitMqDocumentProcessingHostedService>.Instance);

        fixture.PauseDocumentProcessing();
        try
        {
            var first = CreateReceipt(1, 5_000m);
            var second = CreateReceipt(2, 6_000m);
            using var client = fixture.CreateAdminClient(
                PurchasingPermissionCodes.CreateGoodsReceipts,
                PurchasingPermissionCodes.ConfirmGoodsReceipts);
            await ConfirmAsync(client, first);
            await ConfirmAsync(client, second);

            var signals = fixture.DrainDocumentSignals().ToArray();
            Assert.Equal(2, signals.Length);
            Assert.Equal(first.DocumentId, signals[0].DocumentId);
            Assert.Equal(second.DocumentId, signals[1].DocumentId);
            Assert.Equal("Accepted", await ReadStatusAsync(first.DocumentId));
            Assert.Equal("Accepted", await ReadStatusAsync(second.DocumentId));

            await transport.PublishAsync(signals[0]);
            await transport.PublishAsync(signals[1]);
            await using (var inspection = await connection.CreateChannelAsync(false, default))
            {
                var queued = await inspection.QueueDeclarePassiveAsync(documentQueue);
                Assert.Equal(2u, queued.MessageCount);
            }

            await service.StartAsync(CancellationToken.None);
            await WaitUntilAsync(async () =>
                await ReadStatusAsync(first.DocumentId) == "Processed" &&
                await ReadStatusAsync(second.DocumentId) == "Processed");

            var evidence = await ReadSequenceEvidenceAsync(first.DocumentId, second.DocumentId);
            Assert.Equal(evidence.FirstSequence + 1, evidence.SecondSequence);
            Assert.True(evidence.FirstCompletedAt <= evidence.SecondCompletedAt);
            Assert.Equal(1, await CountAsync("InventoryMovements", first.DocumentId));
            Assert.Equal(1, await CountAsync("InventoryMovements", second.DocumentId));
            Assert.Equal(1, await CountAsync("Payables", first.DocumentId));
            Assert.Equal(1, await CountAsync("Payables", second.DocumentId));

            await transport.PublishAsync(signals[0]);
            await WaitUntilAsync(async () => await QueueCountAsync(connection, documentQueue) == 0);
            Assert.Equal(1, await CountAsync("InventoryMovements", first.DocumentId));
            Assert.Equal(1, await CountAsync("Payables", first.DocumentId));

            var failed = CreateReceipt(3, 7_000m);
            var afterFailure = CreateReceipt(4, 8_000m);
            await ConfirmAsync(client, failed);
            await ConfirmAsync(client, afterFailure);
            var failureSignals = fixture.DrainDocumentSignals().ToArray();
            Assert.Equal(2, failureSignals.Length);
            await CorruptPayloadAsync(failed.DocumentId);
            await transport.PublishAsync(failureSignals[0]);
            await transport.PublishAsync(failureSignals[1]);
            await WaitUntilAsync(
                async () =>
                    await QueueCountAsync(connection, $"{documentQueue}.dead") == 1 &&
                    await ReadStatusAsync(afterFailure.DocumentId) == "Processed",
                TimeSpan.FromSeconds(20));

            var failedJob = await ReadJobAsync(failed.DocumentId);
            Assert.Equal("DeadLettered", failedJob.Status);
            Assert.Equal(5, failedJob.AttemptCount);
            Assert.Equal(0, await CountAsync("InventoryMovements", failed.DocumentId));
            Assert.Equal(0, await CountAsync("Payables", failed.DocumentId));
            Assert.Equal(1, await CountAsync("InventoryMovements", afterFailure.DocumentId));
            Assert.Equal(1, await CountAsync("Payables", afterFailure.DocumentId));

            await SeedInventoryAdjustmentSeriesAsync();
            var adjustmentId = Guid.NewGuid();
            using var inventoryClient = fixture.CreateAdminClient(InventoryPermissionCodes.Adjust);
            await ConfirmAdjustmentAsync(inventoryClient, new ConfirmInventoryAdjustmentRequest(
                adjustmentId, fixture.BusinessId, fixture.WarehouseId, DateTimeOffset.UtcNow,
                "MANUAL_ADJUSTMENT", null, "Ajuste procesado por RabbitMQ",
                [new InventoryAdjustmentLineRequest(1, fixture.ProductId, 1m, 5_000m)]));
            var inventorySignal = Assert.Single(fixture.DrainDocumentSignals());
            Assert.Equal(InventoryDocumentTypes.Adjustment, inventorySignal.DocumentType);
            await transport.PublishAsync(inventorySignal);
            await WaitUntilAsync(async () =>
                await ReadInventoryStatusAsync(adjustmentId) == "Processed");
            Assert.Equal(1, await CountAsync("InventoryMovements", adjustmentId));

            await using (var inspection = await connection.CreateChannelAsync(false, default))
            {
                var dead = await inspection.BasicGetAsync(
                    $"{documentQueue}.dead", autoAck: true);
                Assert.NotNull(dead);
                Assert.Equal(
                    failureSignals[0].MovementId.ToString("D"),
                    dead.BasicProperties.MessageId);
            }
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            fixture.ResumeDocumentProcessing();
            fixture.DrainDocumentSignals();
            await DeleteQueuesAsync(connection, documentQueue, fiscalQueue);
        }
    }

    private ConfirmGoodsReceiptRequest CreateReceipt(int ordinal, decimal unitCost)
    {
        var received = DateTimeOffset.UtcNow.AddMinutes(ordinal);
        return new ConfirmGoodsReceiptRequest(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId, fixture.SupplierId,
            $"RABBIT-{ordinal}-{Guid.NewGuid():N}", received.AddDays(-1), received, true,
            received.AddDays(30), "cop", "Entrada procesada por RabbitMQ",
            [new GoodsReceiptLineRequest(
                1, fixture.ProductId, "Producto RabbitMQ", 1m, unitCost,
                0m, "01", 19m, PurchasingTaxTreatments.DeductibleInputVat)]);
    }

    private static async Task ConfirmAsync(
        HttpClient client,
        ConfirmGoodsReceiptRequest request)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post, "/api/commerce/v1/goods-receipts/confirm")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("Idempotency-Key", $"rabbit-{request.DocumentId:N}");
        using var response = await client.SendAsync(message);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private static async Task ConfirmAdjustmentAsync(
        HttpClient client,
        ConfirmInventoryAdjustmentRequest request)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post, "/api/commerce/v1/inventory-adjustments/confirm")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("Idempotency-Key", $"rabbit-inventory-{request.DocumentId:N}");
        using var response = await client.SendAsync(message);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private async Task SeedInventoryAdjustmentSeriesAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF NOT EXISTS(
                SELECT 1 FROM dbo.DocumentSeries
                WHERE BusinessId=@BusinessId AND DocumentType=N'InventoryAdjustment'
                  AND DeviceId IS NULL AND IsActive=1)
              INSERT dbo.DocumentSeries
                (DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,
                 Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
              VALUES(NEWID(),@BusinessId,NULL,N'InventoryAdjustment',N'AJI',N'00',
                 8,1,99999999,0,1,SYSDATETIMEOFFSET());
            """;
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string> ReadInventoryStatusAsync(Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Status FROM dbo.InventoryOperations WHERE InventoryOperationId=@Id";
        command.Parameters.AddWithValue("@Id", documentId);
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The inventory operation was not persisted."));
    }

    private async Task<string> ReadStatusAsync(Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Status FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id";
        command.Parameters.AddWithValue("@Id", documentId);
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The receipt was not persisted."));
    }

    private async Task<SequenceEvidence> ReadSequenceEvidenceAsync(Guid first, Guid second)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DocumentId,ProcessingSequence,CompletedAt
            FROM dbo.DocumentProcessingJobs
            WHERE DocumentId IN (@First,@Second);
            """;
        command.Parameters.AddWithValue("@First", first);
        command.Parameters.AddWithValue("@Second", second);
        var rows = new Dictionary<Guid, (long Sequence, DateTimeOffset Completed)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(reader.GetGuid(0), (reader.GetInt64(1), reader.GetDateTimeOffset(2)));
        return new SequenceEvidence(
            rows[first].Sequence, rows[first].Completed,
            rows[second].Sequence, rows[second].Completed);
    }

    private async Task CorruptPayloadAsync(Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.DocumentProcessingPayloads
            SET PayloadJson=N'{}'
            WHERE DocumentId=@DocumentId AND DocumentType=N'GoodsReceipt';
            """;
        command.Parameters.AddWithValue("@DocumentId", documentId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private async Task<JobEvidence> ReadJobAsync(Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Status,AttemptCount,LastError
            FROM dbo.DocumentProcessingJobs
            WHERE DocumentId=@DocumentId AND DocumentType=N'GoodsReceipt';
            """;
        command.Parameters.AddWithValue("@DocumentId", documentId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new JobEvidence(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    private async Task<int> CountAsync(string table, Guid documentId)
    {
        Assert.Contains(table, new[] { "InventoryMovements", "Payables" });
        var column = table == "Payables" ? "SourceDocumentId" : "DocumentId";
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM dbo.[{table}] WHERE [{column}]=@Id";
        command.Parameters.AddWithValue("@Id", documentId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<uint> QueueCountAsync(
        RabbitMqProcessingConnection connection,
        string queue)
    {
        await using var channel = await connection.CreateChannelAsync(false, default);
        return (await channel.QueueDeclarePassiveAsync(queue)).MessageCount;
    }

    private static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        TimeSpan? timeout = null)
    {
        using var cancellation = new CancellationTokenSource(
            timeout ?? TimeSpan.FromSeconds(10));
        while (!await condition())
            await Task.Delay(100, cancellation.Token);
    }

    private static async Task DeleteQueuesAsync(
        RabbitMqProcessingConnection connection,
        params string[] mainQueues)
    {
        await using var channel = await connection.CreateChannelAsync(false, default);
        foreach (var main in mainQueues)
        {
            var queues = new List<string> { main, $"{main}.dead" };
            if (main == mainQueues[^1])
                queues.AddRange(
                [
                    $"{main}.retry.2s",
                    $"{main}.retry.5s",
                    $"{main}.retry.15s",
                    $"{main}.retry.30s",
                    $"{main}.retry.120s",
                    $"{main}.retry.300s"
                ]);
            foreach (var queue in queues)
                await channel.QueueDeleteAsync(
                    queue, ifUnused: false, ifEmpty: false, noWait: false);
        }
    }

    private sealed record SequenceEvidence(
        long FirstSequence,
        DateTimeOffset FirstCompletedAt,
        long SecondSequence,
        DateTimeOffset SecondCompletedAt);

    private sealed record JobEvidence(string Status, int AttemptCount, string? LastError);
}
