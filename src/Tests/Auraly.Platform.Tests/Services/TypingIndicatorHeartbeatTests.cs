using Auraly.Platform.Infrastructure.Services;
using Xunit;

namespace Auraly.Platform.Tests.Services;

public sealed class TypingIndicatorHeartbeatTests
{
    [Fact]
    public async Task Heartbeat_RenewsUntilDisposed_AndNeverRenewsAfterDispose()
    {
        var renewedThreeTimes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshCount = 0;
        var heartbeat = new TypingIndicatorHeartbeat(
            _ =>
            {
                if (Interlocked.Increment(ref refreshCount) >= 3)
                    renewedThreeTimes.TrySetResult();
                return Task.CompletedTask;
            },
            TimeSpan.FromMilliseconds(20));

        await renewedThreeTimes.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await heartbeat.DisposeAsync();
        var countAfterDispose = Volatile.Read(ref refreshCount);

        await Task.Delay(80);

        Assert.True(countAfterDispose >= 3);
        Assert.Equal(countAfterDispose, Volatile.Read(ref refreshCount));
    }

    [Fact]
    public async Task Heartbeat_ContinuesWhenOneRefreshFails()
    {
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshCount = 0;
        await using var heartbeat = new TypingIndicatorHeartbeat(
            _ =>
            {
                var current = Interlocked.Increment(ref refreshCount);
                if (current == 1)
                    throw new HttpRequestException("transient");
                recovered.TrySetResult();
                return Task.CompletedTask;
            },
            TimeSpan.FromMilliseconds(20));

        await recovered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(Volatile.Read(ref refreshCount) >= 2);
    }
}
