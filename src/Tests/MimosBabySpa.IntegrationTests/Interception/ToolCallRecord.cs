namespace MimosBabySpa.IntegrationTests.Interception;

/// <summary>
/// Representa una invocación de tool registrada durante un escenario de test.
/// </summary>
public record ToolCallRecord(
    string ToolName,
    string ArgumentsJson,
    string ResultJson,
    bool ResultIsError,
    DateTimeOffset CalledAt,
    long ElapsedMs);
