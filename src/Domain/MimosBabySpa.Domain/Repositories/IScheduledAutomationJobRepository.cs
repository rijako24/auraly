using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Repositories;

public interface IScheduledAutomationJobRepository
{
    Task<Dictionary<string, ScheduledAutomationJob>> GetByDeduplicationKeysAsync(
        IReadOnlyCollection<string> keys,
        CancellationToken ct = default);

    Task<IReadOnlyList<ScheduledAutomationJob>> GetDueAsync(
        DateTime utcNow,
        int limit,
        CancellationToken ct = default);

    Task<ScheduledAutomationJob?> GetByIdAsync(Guid jobId, CancellationToken ct = default);

    Task<ScheduledAutomationJob?> GetLatestByReservationAndTypeAsync(
        Guid businessId,
        Guid reservationId,
        ScheduledAutomationJobType jobType,
        CancellationToken ct = default);

    Task<ScheduledAutomationJob> AddAsync(ScheduledAutomationJob job, CancellationToken ct = default);
    Task<ScheduledAutomationJob> UpdateAsync(ScheduledAutomationJob job, CancellationToken ct = default);
}
