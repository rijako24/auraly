namespace MimosBabySpa.Domain.Models.Flow;

public enum NodeExecutionStatus
{
    /// <summary>
    /// Node sent a message and is waiting for user input. Stop processing this turn.
    /// </summary>
    WaitForUser,

    /// <summary>
    /// Node completed. Follow the edge with the given port to the next node.
    /// </summary>
    Advance,

    /// <summary>
    /// Node was skipped due to executeWhen=false. Follow the "skipped" edge.
    /// </summary>
    Skipped,

    /// <summary>
    /// An unrecoverable error occurred.
    /// </summary>
    Error
}

public class NodeExecutionResult
{
    public NodeExecutionStatus Status { get; private init; }
    public string? NextPort { get; private init; }
    public string? BotResponse { get; private init; }
    public string? ErrorMessage { get; private init; }

    public static NodeExecutionResult WaitForUser(string botResponse) =>
        new() { Status = NodeExecutionStatus.WaitForUser, BotResponse = botResponse };

    public static NodeExecutionResult Advance(string? port = null) =>
        new() { Status = NodeExecutionStatus.Advance, NextPort = port };

    public static NodeExecutionResult Skipped() =>
        new() { Status = NodeExecutionStatus.Skipped, NextPort = "skipped" };

    public static NodeExecutionResult Error(string message) =>
        new() { Status = NodeExecutionStatus.Error, ErrorMessage = message };
}
