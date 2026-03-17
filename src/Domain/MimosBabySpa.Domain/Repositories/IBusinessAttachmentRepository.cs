using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IBusinessAttachmentRepository
{
    Task<BusinessAttachment?> GetByIdAsync(Guid businessAttachmentId);
    Task<IEnumerable<BusinessAttachment>> GetByBusinessIdAsync(Guid businessId);
}
