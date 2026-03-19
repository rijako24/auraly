namespace MimosBabySpa.Domain.Models.Flow;

public class FlowActionResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }

    /// <summary>
    /// Custom output port override. When set, the engine uses this port instead of "success"/"failure".
    /// Allows actions to return more than two outcomes (e.g. "not_required", "partial").
    /// </summary>
    public string? OutputPort { get; set; }

    public Dictionary<string, object?> Data { get; set; } = new();

    public static FlowActionResult Succeeded(Dictionary<string, object?>? data = null, string? outputPort = null) =>
        new() { Success = true, Data = data ?? new(), OutputPort = outputPort };

    public static FlowActionResult Failed(string message, Dictionary<string, object?>? data = null) =>
        new() { Success = false, Message = message, Data = data ?? new() };

    public static FlowActionResult Custom(string outputPort, Dictionary<string, object?>? data = null) =>
        new() { Success = true, OutputPort = outputPort, Data = data ?? new() };
}
