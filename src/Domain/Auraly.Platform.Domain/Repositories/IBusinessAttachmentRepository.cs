using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public interface IBusinessAttachmentRepository
{
    Task<BusinessAttachment?> GetByIdAsync(Guid businessAttachmentId);
    Task<IEnumerable<BusinessAttachment>> GetByBusinessIdAsync(Guid businessId);
}
