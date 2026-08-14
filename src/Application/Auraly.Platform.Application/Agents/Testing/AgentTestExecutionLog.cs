namespace Auraly.Platform.Application.Agents.Testing;

public sealed class AgentTestExecutionLog
{
    private readonly List<AgentTestExecutionEvent> _events = [];

    public IReadOnlyList<AgentTestExecutionEvent> Events => _events;

    public void Add(string type, string source, object? payload = null) =>
        _events.Add(new AgentTestExecutionEvent(type, source, payload, DateTime.UtcNow));
}

public sealed record AgentTestExecutionEvent(
    string Type,
    string Source,
    object? Payload,
    DateTime TimestampUtc);
