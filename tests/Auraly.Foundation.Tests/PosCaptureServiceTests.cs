using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Catalog;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Foundation.Tests;

public sealed class PosCaptureServiceTests
{
    [Fact]
    public async Task Scanner_uses_local_catalog_customer_price_and_persists_the_line()
    {
        await WithServiceAsync(async (service, drafts, scope, productId, customerId, availability) =>
        {
            availability.Response = new(
                productId, scope.WarehouseId.Value, 1m, 10m, true, true, "Available");

            var result = await service.CaptureAsync(
                "770123",
                scope,
                customerId,
                warehouseAllowsNegativeStock: false,
                Guid.NewGuid());

            Assert.True(result.Added);
            Assert.Equal(80m, result.Draft!.Lines.Single().UnitPrice);
            Assert.Equal("PriceChannel", result.Draft.Lines.Single().PriceSource);
            Assert.Single(availability.Requests);
            Assert.Equal(1m, availability.Requests[0].Quantity);
            Assert.Equal(
                result.Draft.DraftId,
                (await drafts.GetOrCreateActiveAsync(scope)).DraftId);
        });
    }

    [Fact]
    public async Task Quantity_change_revalidates_total_and_keeps_previous_value_when_unavailable()
    {
        await WithServiceAsync(async (service, _, scope, productId, customerId, availability) =>
        {
            availability.Response = new(
                productId, scope.WarehouseId.Value, 1m, 10m, true, true, "Available");
            var captured = await service.CaptureAsync(
                "770123", scope, customerId, false, Guid.NewGuid());
            var line = captured.Draft!.Lines.Single();
            availability.Response = new(
                productId, scope.WarehouseId.Value, 5m, 2m, true, false, "Insufficient");

            var changed = await service.ChangeQuantityAsync(
                captured.Draft.DraftId,
                line.LineId,
                5m,
                false,
                Guid.NewGuid());

            Assert.Equal(PosCaptureStatus.InsufficientInventory, changed.Status);
            Assert.Equal(1m, changed.Draft!.Lines.Single().Quantity);
            Assert.Equal(5m, availability.Requests[^1].Quantity);
        });
    }

    [Fact]
    public async Task Recovered_draft_reports_the_lines_that_no_longer_have_inventory()
    {
        await WithServiceAsync(async (service, _, scope, productId, customerId, availability) =>
        {
            availability.Response = new(
                productId, scope.WarehouseId.Value, 1m, 10m, true, true, "Available");
            var captured = await service.CaptureAsync(
                "770123", scope, customerId, false, Guid.NewGuid());
            availability.Response = new(
                productId, scope.WarehouseId.Value, 1m, 0m, true, false, "Insufficient");

            var validation = await service.ValidateDraftInventoryAsync(
                captured.Draft!.DraftId, false, Guid.NewGuid());

            Assert.True(validation.WasValidated);
            Assert.False(validation.IsValid);
            var issue = Assert.Single(validation.Issues);
            Assert.Equal(captured.Draft.Lines.Single().LineId, issue.LineId);
            Assert.Equal(1m, issue.RequestedQuantity);
            Assert.Equal(0m, issue.AvailableQuantity);
        });
    }

    [Fact]
    public async Task Recovered_draft_is_blocked_when_inventory_cannot_be_revalidated()
    {
        await WithServiceAsync(async (service, _, scope, productId, customerId, availability) =>
        {
            availability.Response = new(
                productId, scope.WarehouseId.Value, 1m, 10m, true, true, "Available");
            var captured = await service.CaptureAsync(
                "770123", scope, customerId, false, Guid.NewGuid());
            availability.Failure = new HttpRequestException("offline");

            var validation = await service.ValidateDraftInventoryAsync(
                captured.Draft!.DraftId, false, Guid.NewGuid());

            Assert.False(validation.WasValidated);
            Assert.False(validation.IsValid);
            Assert.Empty(validation.Issues);
        });
    }

    [Fact]
    public async Task Blocking_warehouse_does_not_add_a_product_with_zero_inventory()
    {
        await WithServiceAsync(async (service, _, scope, productId, customerId, availability) =>
        {
            availability.Response = new(
                productId, scope.WarehouseId.Value, 1m, 0m, true, false, "Insufficient");

            var result = await service.CaptureAsync(
                "770123", scope, customerId, false, Guid.NewGuid());

            Assert.Equal(PosCaptureStatus.InsufficientInventory, result.Status);
            Assert.Equal(0m, result.Availability!.AvailableQuantity);
            Assert.Empty(result.Draft!.Lines);
        });
    }

    [Fact]
    public async Task Blocking_warehouse_does_not_add_a_line_when_network_is_unavailable()
    {
        await WithServiceAsync(async (service, _, scope, _, customerId, availability) =>
        {
            availability.Failure = new HttpRequestException("offline");
            var result = await service.CaptureAsync(
                "770123", scope, customerId, false, Guid.NewGuid());

            Assert.Equal(PosCaptureStatus.OfflineValidationRequired, result.Status);
            Assert.Empty(result.Draft!.Lines);
        });
    }

    [Fact]
    public async Task Warehouse_that_allows_negatives_never_queries_inventory()
    {
        await WithServiceAsync(async (service, _, scope, _, customerId, availability) =>
        {
            var result = await service.CaptureAsync(
                "770123", scope, customerId, true, Guid.NewGuid());

            Assert.True(result.Added);
            Assert.Empty(availability.Requests);
        });
    }

    [Fact]
    public async Task Product_that_does_not_manage_stock_never_queries_or_blocks_inventory()
    {
        await WithServiceAsync(async (service, _, scope, _, customerId, availability) =>
        {
            availability.Failure = new HttpRequestException("Inventory must not be queried.");
            var captured = await service.CaptureAsync(
                "770123", scope, customerId, false, Guid.NewGuid());
            var line = Assert.Single(captured.Draft!.Lines);

            var changed = await service.ChangeQuantityAsync(
                captured.Draft.DraftId, line.LineId, 999_999m, false, Guid.NewGuid());
            var validation = await service.ValidateDraftInventoryAsync(
                captured.Draft.DraftId, false, Guid.NewGuid());

            Assert.True(captured.Added);
            Assert.True(changed.Added);
            Assert.Equal(999_999m, Assert.Single(changed.Draft!.Lines).Quantity);
            Assert.True(validation.WasValidated);
            Assert.True(validation.IsValid);
            Assert.Empty(validation.Issues);
            Assert.Empty(availability.Requests);
        }, managesStock: false);
    }

    private static async Task WithServiceAsync(
        Func<PosCaptureService, PosDraftStore, PosDraftScope, Guid, Guid, RecordingAvailabilityClient, Task> test,
        bool managesStock = true)
    {
        var path = Path.Combine(Path.GetTempPath(), $"auraly-capture-{Guid.NewGuid():N}.db");
        try
        {
            var catalog = new PosCatalogStore($"Data Source={path}");
            await catalog.InitializeAsync();
            var productId = Guid.NewGuid();
            var item = new PosCatalogItem(
                productId, "P-1", "REF-1", "Product", "EA", "VAT19", 19m,
                100m, "COP", IsActive: true, IsWeighable: false,
                AllowsFractionalSale: false, Scale: null, Barcodes: ["770123"],
                Identifiers: [], UnitCost: 0m, ManagesStock: managesStock);
            var sessionId = Guid.NewGuid();
            var items = new[] { item };
            var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(items))))
                .ToLowerInvariant();
            await catalog.BeginBootstrapAsync(
                new CatalogSyncSessionResponse(sessionId, 0, 1, DateTimeOffset.UtcNow.AddHours(1)));
            await catalog.ApplyBootstrapPageAsync(
                new CatalogBootstrapPage(sessionId, 0, null, false, hash, items));
            await catalog.PromoteBootstrapAsync();
            var customerId = Guid.NewGuid();
            var priceChannelId = Guid.NewGuid();
            await catalog.ApplyPricingSnapshotAsync(new PosPricingSnapshot(
                [new(priceChannelId, productId, 1m, 80m, "COP", false)],
                [new(customerId, "1", "Customer", priceChannelId, true)]));

            var drafts = new PosDraftStore(
                $"Data Source={path}",
                new TestIdGenerator(),
                TimeProvider.System);
            await drafts.InitializeAsync();
            var availability = new RecordingAvailabilityClient();
            var service = new PosCaptureService(catalog, drafts, availability);
            var scope = new PosDraftScope(
                new BusinessId(Guid.NewGuid()),
                new WarehouseId(Guid.NewGuid()),
                new DeviceId(Guid.NewGuid()),
                new WorkSessionId(Guid.NewGuid()),
                new UserId(Guid.NewGuid()));
            var active = await drafts.GetOrCreateActiveAsync(scope);
            await drafts.AssignPartiesAsync(active.DraftId, customerId, null);
            await test(service, drafts, scope, productId, customerId, availability);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            for (var attempt = 0; attempt < 10 && File.Exists(path); attempt++)
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException) when (attempt < 9)
                {
                    await Task.Delay(50);
                }
            }
        }
    }

    private sealed class RecordingAvailabilityClient : IPosInventoryAvailabilityClient
    {
        public List<InventoryAvailabilityRequest> Requests { get; } = [];
        public InventoryAvailabilityResponse? Response { get; set; }
        public Exception? Failure { get; set; }

        public Task<InventoryAvailabilityResponse> CheckAvailabilityAsync(
            InventoryAvailabilityRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (Failure is not null) return Task.FromException<InventoryAvailabilityResponse>(Failure);
            return Task.FromResult(Response ??
                new InventoryAvailabilityResponse(
                    request.ProductId,
                    request.WarehouseId,
                    request.Quantity,
                    request.Quantity,
                    true,
                    true,
                    "Available"));
        }
    }

    private sealed class TestIdGenerator : IAuralyIdGenerator
    {
        public Guid NewId() => Guid.NewGuid();
    }
}
