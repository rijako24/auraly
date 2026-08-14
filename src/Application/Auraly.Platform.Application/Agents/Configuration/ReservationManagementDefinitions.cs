namespace Auraly.Platform.Application.Agents.Configuration;

public sealed class ReservationManagementDefinitions
{
    public IReadOnlyList<string> AutomaticChangeFields { get; init; } = [];

    public IReadOnlyList<string> EscalateChangeFields { get; init; } = [];

    public string? EscalationReasonCode { get; init; }

    public string? ManageableReservationGuidance { get; init; }
}
