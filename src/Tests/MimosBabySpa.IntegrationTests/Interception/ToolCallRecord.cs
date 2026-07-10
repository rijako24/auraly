namespace MimosBabySpa.IntegrationTests.Interception;

/// <summary>
/// Representa una invocacion de tool registrada durante un escenario de test.
/// </summary>
public record ToolCallRecord(
    string ToolName,
    string ArgumentsJson,
    string ResultJson,
    bool ResultIsError,
    DateTimeOffset CalledAt,
    long ElapsedMs,
    string FactsJson = "",
    string? ActivePaymentCheckoutSnapshotJson = null,
    long? ActivePaymentAmountInCents = null);
