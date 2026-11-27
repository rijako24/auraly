namespace Auraly.Api;

public sealed record PosSynchronizationNegotiationResponse(
    Uri ClientAccessUri,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string>? Groups = null);
