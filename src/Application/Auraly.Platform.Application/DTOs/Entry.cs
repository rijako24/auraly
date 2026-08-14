namespace Auraly.Platform.Application.DTOs;

public class Entry
{
    public string Id { get; set; } = string.Empty;
    public List<Change> Changes { get; set; } = new();
}
