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
        RequireEmployee = true
    };

    public Task<AvailabilityParams> GetAsync(Guid businessId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Policy);
}
