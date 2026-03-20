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
    Error,

    /// <summary>
    /// Agent detected an escape intent that doesn't match its domain.
    /// The engine should jump to the Router for re-classification.
    /// <see cref="NodeExecutionResult.DetectedIntent"/> contains the intent key.
    /// </summary>
    ReRoute
}

public class NodeExecutionResult
{
    public NodeExecutionStatus Status { get; private init; }
    public string? NextPort { get; private init; }
    public string? BotResponse { get; private init; }
    public string? ErrorMessage { get; private init; }

    /// <summary>
    /// The routing intent detected by an agent that triggered a ReRoute.
    /// Set only when Status == ReRoute.
    /// </summary>
    public string? DetectedIntent { get; private init; }

    public static NodeExecutionResult WaitForUser(string botResponse) =>
        new() { Status = NodeExecutionStatus.WaitForUser, BotResponse = botResponse };

    public static NodeExecutionResult Advance(string? port = null) =>
        new() { Status = NodeExecutionStatus.Advance, NextPort = port };

    public static NodeExecutionResult Skipped() =>
        new() { Status = NodeExecutionStatus.Skipped, NextPort = "skipped" };

    public static NodeExecutionResult Error(string message) =>
        new() { Status = NodeExecutionStatus.Error, ErrorMessage = message };

    /// <summary>
    /// Agent detected an intent change — re-route through the Router.
    /// The detected intent is passed so the Router can skip LLM classification.
    /// </summary>
    public static NodeExecutionResult ReRoute(string intentKey) =>
        new() { Status = NodeExecutionStatus.ReRoute, DetectedIntent = intentKey };
}
