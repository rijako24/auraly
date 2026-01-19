using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Domain.Repositories;

public interface IBusinessWhatsAppNumberRepository
{
    Task<BusinessWhatsAppNumber?> GetByWhatsAppPhoneNumberIdAsync(string whatsAppPhoneNumberId);
    Task<IEnumerable<BusinessWhatsAppNumber>> GetByBusinessIdAsync(Guid businessId);
    Task<BusinessWhatsAppNumber> CreateAsync(BusinessWhatsAppNumber whatsAppNumber);
    Task<BusinessWhatsAppNumber> UpdateAsync(BusinessWhatsAppNumber whatsAppNumber);
}
