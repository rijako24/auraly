using Auraly.Contracts.Parties;

namespace Auraly.Platform.Application.Commerce;

public interface IExternalCustomerReconciliationSignalPublisher
{
    Task PublishAsync(
        ExternalCustomerReconciliationSignal signal,
        CancellationToken cancellationToken = default);
}
