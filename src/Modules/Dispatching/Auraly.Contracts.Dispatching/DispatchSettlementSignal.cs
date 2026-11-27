namespace Auraly.Contracts.Dispatching;

public sealed record DispatchSettlementSignal(Guid OperationId, Guid BusinessId, int Version);

public interface IDispatchSettlementSignalPublisher
{
    Task PublishAsync(DispatchSettlementSignal signal, CancellationToken cancellationToken);
}

public static class DispatchSettlementQueue
{
    public const string Subject = "DispatchSettlementPreparation.v1";
}
