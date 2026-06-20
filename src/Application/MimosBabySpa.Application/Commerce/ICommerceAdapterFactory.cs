using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Commerce;

public interface ICommerceAdapterFactory
{
    ICommerceAdapter Resolve(CommerceProvider provider);
}
