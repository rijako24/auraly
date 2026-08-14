namespace Auraly.Platform.Application.DTOs;

public class Change
{
    public string Field { get; set; } = string.Empty;
    public Value Value { get; set; } = new();
}
