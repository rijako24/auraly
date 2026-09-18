using Auraly.Pos.Edge.Host;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PosInitialPreparationTests
{
    [Fact]
    public async Task Catalog_readiness_checkpoint_runs_after_every_required_offline_projection()
    {
        var stages = new List<string>();

        await PosInitialPreparation.SynchronizeAsync(
            _ => RecordAsync("geography"),
            _ => RecordAsync("cash-reasons"),
            _ => RecordAsync("catalog-ready"),
            CancellationToken.None);

        Assert.Equal(["geography", "cash-reasons", "catalog-ready"], stages);

        Task RecordAsync(string stage)
        {
            stages.Add(stage);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Failed_required_projection_never_reaches_catalog_readiness_checkpoint()
    {
        var catalogStarted = false;

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PosInitialPreparation.SynchronizeAsync(
                _ => Task.CompletedTask,
                _ => throw new InvalidDataException("missing cash reasons"),
                _ =>
                {
                    catalogStarted = true;
                    return Task.CompletedTask;
                },
                CancellationToken.None));

        Assert.False(catalogStarted);
    }
}
