namespace MimosBabySpa.Domain.Models;

/// <summary>
/// Entrada del ledger de verificaciones del agente, persistida inline en ConversationState.
/// </summary>
public sealed record VerificationEntry(
    DateTime VerifiedAt,
    DateTime? ExpiresAt,
    string? PayloadJson = null);
