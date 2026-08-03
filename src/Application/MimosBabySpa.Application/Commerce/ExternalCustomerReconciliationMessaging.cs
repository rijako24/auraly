using Auraly.Contracts.Parties;

namespace MimosBabySpa.Application.Commerce;

public interface IExternalCustomerReconciliationSignalPublisher
{
    Task PublishAsync(
        ExternalCustomerReconciliationSignal signal,
        CancellationToken cancellationToken = default);
}
