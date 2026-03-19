namespace MimosBabySpa.Domain.Enums;

/// <summary>
/// Defines how a node behaves when it is re-entered after a WaitForUser state.
/// </summary>
public enum ReEntryBehavior
{
    /// <summary>
    /// Call ExecuteAsync again. Used for CollectFields, Action, LLMClassify, WaitForEvent.
    /// </summary>
    ReExecute = 0,

    /// <summary>
    /// Skip the handler and automatically advance via the default port.
    /// Used for GenerateResponse nodes that already sent their message.
    /// </summary>
    AdvancePast = 1
}
