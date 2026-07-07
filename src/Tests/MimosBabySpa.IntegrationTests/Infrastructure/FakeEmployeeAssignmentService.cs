using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

/// <summary>
/// Returns the first available employee always, so CreateReservationToolHandler
/// never blocks on "no employee available" in tests.
/// </summary>
public class FakeEmployeeAssignmentService : IEmployeeAssignmentService
{
    private readonly Employee _employee;

    public FakeEmployeeAssignmentService(Guid businessId)
    {
        _employee = new Employee
        {
            EmployeeId = Guid.NewGuid(),
            BusinessId = businessId,
            Name       = "Maria Terapeuta",
            IsActive   = true,
            CreatedAt  = DateTime.UtcNow
        };
    }

    public Task<Employee?> FindBestAvailableEmployeeAsync(
        Guid businessId, Guid serviceId,
        DateTime startTime, DateTime endTime,
        CancellationToken cancellationToken = default,
        Guid? preferredEmployeeId = null) =>
        Task.FromResult<Employee?>(_employee);
}
