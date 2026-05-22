namespace MimosBabySpa.Domain.Entities;

/// <summary>
/// Hecho verificado por el backend durante un flujo agentic (ledger de precondiciones).
/// </summary>
public sealed class ConversationVerification
{
    public Guid VerificationId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid BusinessId { get; set; }
    public string FactType { get; set; } = string.Empty;
    public string ScopeKey { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public DateTime VerifiedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
