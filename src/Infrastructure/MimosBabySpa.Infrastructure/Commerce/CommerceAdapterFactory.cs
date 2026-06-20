using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Infrastructure.Commerce;

public sealed class CommerceAdapterFactory : ICommerceAdapterFactory
{
    private readonly IReadOnlyDictionary<CommerceProvider, ICommerceAdapter> _adapters;

    public CommerceAdapterFactory(IEnumerable<ICommerceAdapter> adapters)
    {
        _adapters = adapters.ToDictionary(a => a.Provider);
    }

    public ICommerceAdapter Resolve(CommerceProvider provider)
    {
        if (_adapters.TryGetValue(provider, out var adapter))
            return adapter;

        throw new InvalidOperationException($"Commerce provider '{provider}' is not registered.");
    }
}
