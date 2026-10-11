using Auraly.Application.Sales;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Sales;

namespace Auraly.Foundation.Tests;

public sealed class PosSaleRestaurantMetadataTests
{
    [Fact]
    public void Null_restaurant_fields_keep_existing_sale_payload_shape()
    {
        var sale = Sale();

        var json = PosSaleContractSerializer.Serialize(sale);

        Assert.DoesNotContain("\"tableId\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"addedByUserId\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"addedAt\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Null(PosSaleContractSerializer.Deserialize(json).TableId);
    }

    [Fact]
    public void Restaurant_fields_round_trip_with_line_provenance()
    {
        var tableId = Guid.NewGuid();
        var waiterId = Guid.NewGuid();
        var addedAt = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        var sale = Sale() with
        {
            TableId = tableId,
            Lines = [Sale().Lines[0] with
            {
                AddedByUserId = waiterId,
                AddedAt = addedAt
            }]
        };

        var restored = PosSaleContractSerializer.Deserialize(
            PosSaleContractSerializer.Serialize(sale));

        Assert.Equal(tableId, restored.TableId);
        Assert.Equal(waiterId, restored.Lines[0].AddedByUserId);
        Assert.Equal(addedAt, restored.Lines[0].AddedAt);
    }

    [Fact]
    public async Task Public_online_reception_rejects_client_supplied_table_metadata()
    {
        var sale = Sale() with { TableId = Guid.NewGuid() };
        var user = new OnlineSalesUserIdentity(sale.SoldByUserId, sale.TenantId,
            new HashSet<string> { CommercePermissionCodes.SalesCreate });
        var receiver = new ReceivePosSaleService(null!, null!, null!, null!,
            TimeProvider.System);

        await Assert.ThrowsAsync<PosSaleInvalidException>(() =>
            receiver.ReceiveOnlineAsync(user, "restaurant-metadata-test", sale));
    }

    [Fact]
    public async Task Edge_reception_rejects_client_supplied_line_provenance()
    {
        var sale = Sale() with
        {
            SourceMode = SaleSourceModes.PosEdge,
            Lines = [Sale().Lines[0] with { AddedByUserId = Guid.NewGuid() }]
        };
        var receiver = new ReceivePosSaleService(null!, null!, null!, null!,
            TimeProvider.System);

        await Assert.ThrowsAsync<PosSaleInvalidException>(() =>
            receiver.ReceiveAsync(new PosDeviceIdentity(sale.DeviceId, sale.TenantId),
                "restaurant-metadata-test", sale));
    }

    private static PosSaleUploadRequest Sale() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.Empty,
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        null!, null!, null,
        [new PosSaleLineContract(1, Guid.NewGuid(), "Plato", "01", 1m,
            100m, 0m, 0m, 100m, 100m, 0m, 0m)],
        [], SourceMode: SaleSourceModes.Online);
}
