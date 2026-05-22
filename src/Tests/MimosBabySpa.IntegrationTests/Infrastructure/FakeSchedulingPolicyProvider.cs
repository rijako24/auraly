using MimosBabySpa.Application.Configuration;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

/// <summary>
/// Política de agendamiento fija para tests de integración (lun–sáb 08:00–18:00).
/// </summary>
internal sealed class FakeSchedulingPolicyProvider : ISchedulingPolicyProvider
{
    private static readonly AvailabilityParams Policy = new()
    {
        SlotIntervalMinutes = 60,
        BufferBetweenAppointmentsMinutes = 0,
        RequireEmployee = true,
        Schedule = new Dictionary<string, List<TimeBlock>>
        {
            ["monday"] = [new TimeBlock { Open = "08:00", Close = "18:00" }],
            ["tuesday"] = [new TimeBlock { Open = "08:00", Close = "18:00" }],
            ["wednesday"] = [new TimeBlock { Open = "08:00", Close = "18:00" }],
            ["thursday"] = [new TimeBlock { Open = "08:00", Close = "18:00" }],
            ["friday"] = [new TimeBlock { Open = "08:00", Close = "18:00" }],
            ["saturday"] = [new TimeBlock { Open = "08:00", Close = "18:00" }],
            ["sunday"] = []
        }
    };

    public Task<AvailabilityParams> GetAsync(Guid businessId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Policy);
}
