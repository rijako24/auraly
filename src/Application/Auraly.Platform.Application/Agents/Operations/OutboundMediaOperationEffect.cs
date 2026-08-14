namespace Auraly.Platform.Application.Agents.Operations;

/// <summary>
/// Requests one authoritative media message after the main rendered response.
/// The URL is resolved by the channel effect processor and is never reconstructed by the LLM.
/// </summary>
public sealed record OutboundMediaOperationEffect(
    string MediaReference,
    string MediaType,
    string? Caption = null,
    string? Filename = null)
    : OperationEffect("outbound.media");
