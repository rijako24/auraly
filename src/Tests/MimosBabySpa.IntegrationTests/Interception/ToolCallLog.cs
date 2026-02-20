using MimosBabySpa.Application.Tools;

namespace MimosBabySpa.IntegrationTests.Interception;

/// <summary>
/// Thread-safe log of all tool calls made during a test scenario.
/// </summary>
public class ToolCallLog
{
    private readonly List<ToolCallRecord> _records = [];
    private readonly object _lock = new();

    public void Add(ToolCallRecord record)
    {
        lock (_lock) _records.Add(record);
    }

    public IReadOnlyList<ToolCallRecord> All
    {
        get { lock (_lock) return _records.ToList(); }
    }

    public bool WasCalled(ToolType toolType) =>
        All.Any(r => r.ToolType == toolType);

    public int CallCount(ToolType toolType) =>
        All.Count(r => r.ToolType == toolType);

    public ToolCallRecord? LastCall(ToolType toolType) =>
        All.LastOrDefault(r => r.ToolType == toolType);

    public IReadOnlyList<ToolCallRecord> AllCalls(ToolType toolType) =>
        All.Where(r => r.ToolType == toolType).ToList();

    /// <summary>Returns true if CheckAvailability was called before CreateReservation in sequence.</summary>
    public bool CheckAvailabilityCalledBefore(ToolType laterTool)
    {
        var all        = All.ToList();
        var checkIndex = all.FindIndex(r => r.ToolType == ToolType.CheckAvailability);
        var laterIndex = all.FindIndex(r => r.ToolType == laterTool);
        if (checkIndex < 0 || laterIndex < 0) return false;
        return checkIndex < laterIndex;
    }

    public void Clear()
    {
        lock (_lock) _records.Clear();
    }
}
