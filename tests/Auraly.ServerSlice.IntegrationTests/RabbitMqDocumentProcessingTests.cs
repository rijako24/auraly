using System.Net;
using System.Net.Http.Json;
using Auraly.Api;
using Auraly.Application.Fiscal;
using Auraly.BuildingBlocks.Domain.Identifiers;
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
        if (string.IsNullOrWhiteSpace(rabbitConnection)) return;

        var suffix = Guid.NewGuid().ToString("N");
        var documentQueue = $"auraly-tests-documents-{suffix}";
        var fiscalQueue = $"auraly-tests-fiscal-{suffix}";
        var options = new RabbitMqProcessingOptions(
            rabbitConnection, documentQueue, fiscalQueue);
        await using var connection = new RabbitMqProcessingConnection(options);
        await using var transport = new RabbitMqProcessingTransport(
            connection, options, TimeProvider.System);
        var fiscal = new FiscalProcessingCoordinator(
            transport,
            fixture.Services.GetRequiredService<IAuralyIdGenerator>());
        using var service = new RabbitMqDocumentProcessingHostedService(
            connection,
            transport,
            options,
            fixture.Services.GetRequiredService<IServiceScopeFactory>(),
            fiscal,
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

            var invalid = new Auraly.Application.DocumentProcessing.DocumentProcessingSignal(
                Guid.NewGuid(), fixture.BusinessId, Guid.NewGuid(), "GoodsReceipt");
            await transport.PublishAsync(invalid);
            await WaitUntilAsync(
                async () => await QueueCountAsync(connection, $"{documentQueue}.dead") == 1,
                TimeSpan.FromSeconds(15));
            await using (var inspection = await connection.CreateChannelAsync(false, default))
            {
                var dead = await inspection.BasicGetAsync(
                    $"{documentQueue}.dead", autoAck: true);
                Assert.NotNull(dead);
                Assert.Equal(invalid.MovementId.ToString("D"), dead.BasicProperties.MessageId);
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
                0m, "01", 19m)]);
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
                    $"{main}.retry.2s", $"{main}.retry.5s",
                    $"{main}.retry.15s", $"{main}.retry.30s",
                    $"{main}.retry.120s", $"{main}.retry.300s"
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
}
