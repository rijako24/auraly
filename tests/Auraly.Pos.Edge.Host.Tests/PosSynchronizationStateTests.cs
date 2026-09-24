using Auraly.Pos.Edge.Host;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PosSynchronizationStateTests
{
    [Fact]
    public void Approval_push_wakes_the_local_cashier()
    {
        var state = new PosUiStateSignal();
        var synchronization = new PosSynchronizationSignal();
        var (subscriptionId, reader) = state.Subscribe();

        PosWebPubSubConnection.RouteBusinessInvalidation(
            Auraly.BuildingBlocks.Application.Synchronization.PosSynchronizationStreams.Approvals,
            synchronization, state);

        Assert.True(reader.TryRead(out var notification));
        Assert.Equal("state", notification);
        state.Unsubscribe(subscriptionId);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(5, true)]
    public void Initial_push_connection_does_not_duplicate_startup_catchup(
        int connectionNumber,
        bool expected)
    {
        Assert.Equal(
            expected,
            PosSynchronizationConnectionPolicy.RequiresCatchUp(connectionNumber));
    }

    [Fact]
    public void Failed_stage_explains_manual_retry_and_clears_when_user_retries()
    {
        var state = new PosSynchronizationState();

        state.Begin();
        state.StageStarted("usuarios y permisos");
        Assert.Contains("usuarios y permisos", state.Current.ActiveStages);

        state.StageFailed(
            "usuarios y permisos",
            "No fue posible preparar usuarios y permisos. Pulsa Reintentar.");
        state.Failed();
        Assert.True(state.Current.LastAttemptFailed);
        Assert.Equal("usuarios y permisos", state.Current.FailedStage);
        Assert.Contains("Pulsa Reintentar", state.Current.LastError);

        state.Begin();
        Assert.Null(state.Current.FailedStage);
        Assert.Null(state.Current.LastError);
        state.StageStarted("usuarios y permisos");
        state.StageSucceeded("usuarios y permisos");
        state.Succeeded();

        Assert.False(state.Current.LastAttemptFailed);
        Assert.Null(state.Current.FailedStage);
        Assert.Null(state.Current.LastError);
    }

    [Fact]
    public void Operational_work_does_not_hide_a_paused_preparation_failure()
    {
        var state = new PosSynchronizationState();
        state.Begin();
        state.StageFailed("catálogo", "La descarga falló. Pulsa Reintentar.");
        state.Failed();

        state.Begin(preserveFailure: true);
        state.StageStarted("subida de documentos");
        state.StageSucceeded("subida de documentos");
        state.Succeeded(preserveFailure: true);

        Assert.True(state.Current.LastAttemptFailed);
        Assert.Equal("catálogo", state.Current.FailedStage);
        Assert.Equal("La descarga falló. Pulsa Reintentar.", state.Current.LastError);
        Assert.Empty(state.Current.ActiveStages);
    }
}
