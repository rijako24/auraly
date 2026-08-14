namespace Auraly.Platform.Application.Services;

public class BookedSlot
{
    public string Time { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public int Duration { get; set; }
    public string Service { get; set; } = string.Empty;
}
