using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Auraly.Platform.Application.Services;
using Xunit;

namespace Auraly.Platform.Tests.Services;

public sealed class TimedProcessSchedulerTests
{
    [Fact]
    public void Evaluate_WhenProcessIsNotConfigured_RunsForBackwardCompatibility()
    {
        var policy = new TimedProcessSchedulePolicy();
        var process = new TestTimedProcess("unconfigured");

        var decision = policy.Evaluate(
            process,
            TimedProcessScheduleSnapshot.Empty,
            new DateTime(2026, 7, 6, 12, 1, 0, DateTimeKind.Utc));

        decision.ShouldRun.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_WhenProcessIsDisabled_Skips()
    {
        var policy = new TimedProcessSchedulePolicy();
        var process = new TestTimedProcess("payment_link_polling");
        var schedule = CreateSchedule(process.Name, enabled: false, intervalMinutes: 5);

        var decision = policy.Evaluate(
            process,
            schedule,
            new DateTime(2026, 7, 6, 12, 5, 0, DateTimeKind.Utc));

        decision.ShouldRun.Should().BeFalse();
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void Evaluate_UsesConfiguredMinuteInterval(int minute, bool expected)
    {
        var policy = new TimedProcessSchedulePolicy();
        var process = new TestTimedProcess("payment_link_polling");
        var schedule = CreateSchedule(process.Name, enabled: true, intervalMinutes: 5);

        var decision = policy.Evaluate(
            process,
            schedule,
            new DateTime(2026, 7, 6, 12, minute, 30, DateTimeKind.Utc));

        decision.ShouldRun.Should().Be(expected);
    }

    [Fact]
    public async Task RunDueAsync_RunsOnlyDueProcesses()
    {
        var due = new TestTimedProcess("due");
        var skipped = new TestTimedProcess("skipped");
        var schedule = new TimedProcessScheduleSnapshot(new Dictionary<string, TimedProcessSchedule>
        {
            [due.Name] = new() { Enabled = true, IntervalMinutes = 1 },
            [skipped.Name] = new() { Enabled = true, IntervalMinutes = 5 }
        });
        var scheduler = new TimedProcessScheduler(
            [due, skipped],
            new TestScheduleProvider(schedule),
            new TimedProcessSchedulePolicy(),
            NullLogger<TimedProcessScheduler>.Instance);

        await scheduler.RunDueAsync(new DateTime(2026, 7, 6, 12, 1, 0, DateTimeKind.Utc));

        due.RunCount.Should().Be(1);
        skipped.RunCount.Should().Be(0);
    }

    private static TimedProcessScheduleSnapshot CreateSchedule(
        string processName,
        bool enabled,
        int intervalMinutes) =>
        new(new Dictionary<string, TimedProcessSchedule>
        {
            [processName] = new() { Enabled = enabled, IntervalMinutes = intervalMinutes }
        });

    private sealed class TestScheduleProvider : ITimedProcessScheduleProvider
    {
        private readonly TimedProcessScheduleSnapshot _schedule;

        public TestScheduleProvider(TimedProcessScheduleSnapshot schedule)
        {
            _schedule = schedule;
        }

        public Task<TimedProcessScheduleSnapshot> GetScheduleAsync(CancellationToken ct = default) =>
            Task.FromResult(_schedule);
    }

    private sealed class TestTimedProcess : ITimedProcess
    {
        public TestTimedProcess(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public int RunCount { get; private set; }

        public Task RunAsync(CancellationToken ct = default)
        {
            RunCount++;
            return Task.CompletedTask;
        }
    }
}
