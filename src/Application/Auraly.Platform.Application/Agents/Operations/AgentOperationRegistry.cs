namespace Auraly.Platform.Application.Agents.Operations;

public sealed class AgentOperationRegistry
{
    private readonly IReadOnlyDictionary<string, IAgentOperation> _operations;

    public AgentOperationRegistry(IEnumerable<IAgentOperation> operations)
    {
        _operations = operations.ToDictionary(
            operation => operation.Descriptor.Id,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<IAgentOperation> All => _operations.Values.ToArray();

    public bool TryGet(string id, out IAgentOperation operation) =>
        _operations.TryGetValue(id, out operation!);
}
