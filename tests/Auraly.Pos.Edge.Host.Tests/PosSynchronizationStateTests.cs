using Auraly.Pos.Edge.Host;
using Xunit;

namespace Auraly.Pos.Edge.Host.Tests;

public sealed class PosSynchronizationStateTests
{
    [Fact]
    public void Failed_stage_remains_actionable_during_retry_and_clears_after_success()
    {
        var state = new PosSynchronizationState();

        state.Begin();
        state.StageStarted("usuarios y permisos");
        Assert.Contains("usuarios y permisos", state.Current.ActiveStages);

        state.StageFailed("usuarios y permisos");
        state.Failed();
        Assert.True(state.Current.LastAttemptFailed);
        Assert.Equal("usuarios y permisos", state.Current.FailedStage);
        Assert.Contains("intentará de nuevo", state.Current.LastError);

        state.Begin();
        Assert.Equal("usuarios y permisos", state.Current.FailedStage);
        state.StageStarted("usuarios y permisos");
        state.StageSucceeded("usuarios y permisos");
        state.Succeeded();

        Assert.False(state.Current.LastAttemptFailed);
        Assert.Null(state.Current.FailedStage);
        Assert.Null(state.Current.LastError);
    }
}
