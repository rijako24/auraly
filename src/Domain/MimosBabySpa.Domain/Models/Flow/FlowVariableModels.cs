namespace MimosBabySpa.Domain.Models.Flow;

public class FlowVariable
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// String | Email | Phone | Date | Time | Number | Enum | Boolean
    /// </summary>
    public string DataType { get; set; } = "String";

    public bool Required { get; set; }

    /// <summary>
    /// Variable group: transaction | identity | business | system
    /// </summary>
    public string Group { get; set; } = "transaction";

    public int DisplayOrder { get; set; }
    public bool ShowInSummary { get; set; }

    /// <summary>
    /// When true, this variable is managed by the system/actions and never asked to the user.
    /// </summary>
    public bool IsSystemManaged { get; set; }

    public string? ExtractionHint { get; set; }
    public FlowVariableValidation? Validation { get; set; }
    public List<FlowVariableGuard> ApplyGuards { get; set; } = new();
    public FlowVariableOnChange? OnChange { get; set; }
}

public class FlowVariableValidation
{
    public string? KnowledgeSourceId { get; set; }
    public string? KnowledgeSourceField { get; set; }
    public string? Pattern { get; set; }
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }

    /// <summary>
    /// "today" or ISO date string. Variable value must be >= this date.
    /// </summary>
    public string? MinDate { get; set; }
}

public class FlowVariableGuard
{
    /// <summary>
    /// Guard type. Supported: "skipWhenIntention"
    /// </summary>
    public string Type { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class FlowVariableOnChange
{
    /// <summary>
    /// Variables to reset when this variable changes (excluding those extracted in the same turn).
    /// </summary>
    public List<string> ResetVariables { get; set; } = new();

    /// <summary>
    /// Flags to reset when this variable changes.
    /// </summary>
    public List<string> ResetFlags { get; set; } = new();

    /// <summary>
    /// If set, the engine jumps to this node after processing all onChange rules.
    /// When multiple variables change, the topologically earliest gotoNode wins.
    /// </summary>
    public string? GotoNode { get; set; }
}
