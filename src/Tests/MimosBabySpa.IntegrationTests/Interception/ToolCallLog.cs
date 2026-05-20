using System.Text.Json;

namespace MimosBabySpa.IntegrationTests.Interception;

/// <summary>
/// Registro thread-safe de todas las invocaciones de tools durante un escenario de test.
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

    public bool WasCalled(string toolName) =>
        All.Any(r => string.Equals(r.ToolName, toolName, StringComparison.OrdinalIgnoreCase));

    public int CallCount(string toolName) =>
        All.Count(r => string.Equals(r.ToolName, toolName, StringComparison.OrdinalIgnoreCase));

    public ToolCallRecord? LastCall(string toolName) =>
        All.LastOrDefault(r => string.Equals(r.ToolName, toolName, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<ToolCallRecord> AllCalls(string toolName) =>
        All.Where(r => string.Equals(r.ToolName, toolName, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// Retorna true si firstTool fue llamada antes que secondTool.
    /// </summary>
    public bool CalledBefore(string firstTool, string secondTool)
    {
        var all = All.ToList();
        var firstIdx = all.FindIndex(r => string.Equals(r.ToolName, firstTool, StringComparison.OrdinalIgnoreCase));
        var secondIdx = all.FindIndex(r => string.Equals(r.ToolName, secondTool, StringComparison.OrdinalIgnoreCase));
        if (firstIdx < 0 || secondIdx < 0) return false;
        return firstIdx < secondIdx;
    }

    /// <summary>
    /// Intenta parsear el resultado JSON de la última llamada exitosa a una tool.
    /// </summary>
    public bool TryGetLastResult(string toolName, out JsonElement element)
    {
        var call = LastCall(toolName);
        if (call == null || call.ResultIsError)
        {
            element = default;
            return false;
        }
        try
        {
            using var doc = JsonDocument.Parse(call.ResultJson);
            element = doc.RootElement.Clone();
            return true;
        }
        catch
        {
            element = default;
            return false;
        }
    }

    public void Clear()
    {
        lock (_lock) _records.Clear();
    }
}
