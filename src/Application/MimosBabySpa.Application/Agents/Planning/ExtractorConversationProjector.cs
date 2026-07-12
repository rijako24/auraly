using MimosBabySpa.Application.LLM;

namespace MimosBabySpa.Application.Agents.Planning;

public static class ExtractorConversationProjector
{
    public static IReadOnlyList<ChatMessage> Project(
        AgentConfig config,
        IReadOnlyList<ChatMessage> conversation)
    {
        if (conversation.Count == 0)
            return [];

        var upperBound = Math.Max(1, config.HistoryWindowSize);
        var window = Math.Clamp(config.ExtractorHistoryWindowSize, 1, upperBound);
        return conversation.TakeLast(window).ToList();
    }
}
