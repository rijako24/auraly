using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Domain.Fiscal;

namespace Auraly.Foundation.Tests;

public sealed class DocumentSeriesTests
{
    [Fact]
    public async Task Concurrent_consumption_assigns_each_number_once()
    {
        var deviceId = new DeviceId(Guid.NewGuid());
        var today = new DateOnly(2026, 7, 27);
        var series = new DocumentSeries(
            Guid.NewGuid(),
            new BusinessId(Guid.NewGuid()),
            deviceId,
            "FV01",
            1,
            100,
            today.AddDays(-1),
            today.AddYears(1));
        series.Activate(today);

        var assignments = await Task.WhenAll(
            Enumerable.Range(0, 100)
                .Select(_ => Task.Run(() => series.Consume(deviceId, today))));

        Assert.Equal(100, assignments.Select(x => x.Consecutive).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 100).Select(x => (long)x), assignments.Select(x => x.Consecutive).Order());
        Assert.Equal(DocumentSeriesStatus.Exhausted, series.Status);
    }

    [Fact]
    public void Device_cannot_consume_another_devices_series()
    {
        var today = new DateOnly(2026, 7, 27);
        var series = new DocumentSeries(
            Guid.NewGuid(),
            new BusinessId(Guid.NewGuid()),
            new DeviceId(Guid.NewGuid()),
            "FV01",
            1,
            10,
            today,
            today.AddYears(1));
        series.Activate(today);

        Assert.Throws<InvalidOperationException>(
            () => series.Consume(new DeviceId(Guid.NewGuid()), today));
    }
}
