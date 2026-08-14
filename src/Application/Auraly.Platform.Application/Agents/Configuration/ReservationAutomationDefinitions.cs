namespace Auraly.Platform.Application.Agents.Configuration;

using System.Text.Json;

public sealed class ReservationAutomationDefinitions
{
    public ReservationAutomationConfig? Confirmation { get; set; }

    public ReservationAutomationConfig? Reminder { get; set; }
}

public sealed class ReservationAutomationConfig
{
    public bool Enabled { get; set; }

    public ReservationAutomationTrigger Trigger { get; set; } = new();

    public string? SendMessageSequence { get; set; }

    public Dictionary<string, ReservationAutomationActionConfig> Actions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ReservationAutomationTrigger
{
    public string Type { get; set; } = "relative";

    public int? HoursBefore { get; set; }

    public int DaysBefore { get; set; }

    public string? Time { get; set; }
}

public sealed class ReservationAutomationActionConfig
{
    public string Operation { get; set; } = string.Empty;

    public Dictionary<string, JsonElement> Arguments { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string? SendMessageSequence { get; set; }
}
