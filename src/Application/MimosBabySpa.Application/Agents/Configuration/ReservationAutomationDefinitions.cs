namespace MimosBabySpa.Application.Agents.Configuration;

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
}

public sealed class ReservationAutomationTrigger
{
    public string Type { get; set; } = "relative";

    public int? HoursBefore { get; set; }

    public int DaysBefore { get; set; }

    public string? Time { get; set; }
}
