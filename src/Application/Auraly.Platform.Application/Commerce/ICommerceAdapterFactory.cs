using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Application.Commerce;

public interface ICommerceAdapterFactory
{
    ICommerceAdapter Resolve(CommerceProvider provider);
}
