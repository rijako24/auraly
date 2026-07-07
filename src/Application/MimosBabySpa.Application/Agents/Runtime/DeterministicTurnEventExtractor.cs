namespace MimosBabySpa.Application.Agents.Runtime;

/// <summary>
/// Default extractor for the engine core. Tenant/domain language detection must be provided
/// by configuration or a specialized implementation, not hardcoded in the engine.
/// </summary>
public sealed class NoOpTurnEventExtractor : ITurnEventExtractor
{
    public IReadOnlyList<TurnEvent> Extract(string userMessage) => [];
}
