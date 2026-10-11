using Auraly.BuildingBlocks.Application.Synchronization;

namespace Auraly.Foundation.Tests;

public sealed class RestaurantCommandPushGroupTests
{
    [Fact]
    public void Physical_command_notice_is_addressed_only_to_its_printer_device()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var notice = new PosSynchronizationInvalidation(Guid.NewGuid(),
            tenantId, businessId,
            PosSynchronizationStreams.RestaurantCommandPrint, 1,
            DateTimeOffset.UtcNow, deviceId);

        Assert.Equal(PosSynchronizationGroups.Device(tenantId, deviceId),
            PosSynchronizationGroups.Target(notice));
        Assert.NotEqual(PosSynchronizationGroups.RestaurantCommands(
            tenantId, businessId), PosSynchronizationGroups.Target(notice));
    }

    [Fact]
    public void Command_notice_uses_only_the_restaurant_group()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var notice = new PosSynchronizationInvalidation(
            Guid.NewGuid(), tenantId, businessId,
            PosSynchronizationStreams.RestaurantCommands, 1,
            DateTimeOffset.UtcNow);

        Assert.Equal(
            PosSynchronizationGroups.RestaurantCommands(tenantId, businessId),
            PosSynchronizationGroups.Target(notice));
        Assert.NotEqual(
            PosSynchronizationGroups.Business(tenantId, businessId),
            PosSynchronizationGroups.Target(notice));
    }

    [Fact]
    public void Existing_streams_and_device_targets_keep_their_groups()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var notice = new PosSynchronizationInvalidation(
            Guid.NewGuid(), tenantId, businessId,
            PosSynchronizationStreams.Configuration, 1,
            DateTimeOffset.UtcNow);

        Assert.Equal(PosSynchronizationGroups.Business(tenantId, businessId),
            PosSynchronizationGroups.Target(notice));
        Assert.Equal(PosSynchronizationGroups.Device(tenantId, deviceId),
            PosSynchronizationGroups.Target(notice with {
                Stream = PosSynchronizationStreams.RestaurantCommands,
                TargetDeviceId = deviceId
            }));
    }
}
