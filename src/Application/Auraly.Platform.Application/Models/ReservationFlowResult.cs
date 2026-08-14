namespace Auraly.Platform.Application.Models;

public class ReservationFlowResult
{
    public bool HasAllData { get; set; }
    public string Message { get; set; } = string.Empty;
    public ReservationData? ReservationData { get; set; }
}
