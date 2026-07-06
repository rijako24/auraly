namespace MimosBabySpa.Application.Agents;

public sealed record InteractivePayloadAction(
    string Scope,
    string Outcome,
    string SourceId,
    string RawPayload);

public static class InteractivePayloadParser
{
    public static bool TryParse(string? payload, out InteractivePayloadAction action)
    {
        action = default!;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        var parts = payload.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3
            || string.IsNullOrWhiteSpace(parts[0])
            || string.IsNullOrWhiteSpace(parts[1])
            || string.IsNullOrWhiteSpace(parts[2]))
        {
            return false;
        }

        action = new InteractivePayloadAction(parts[0], parts[1], parts[2], payload.Trim());
        return true;
    }
}
