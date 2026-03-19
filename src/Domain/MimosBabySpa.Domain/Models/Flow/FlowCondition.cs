namespace MimosBabySpa.Domain.Models.Flow;

public class FlowCondition
{
    /// <summary>
    /// Supported types:
    /// FlagIsTrue | FlagIsFalse | VariableEquals | VariableIsNull | VariableIsNotNull | And | Or
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Parameters for the condition. Keys depend on Type:
    /// - FlagIsTrue/FlagIsFalse: { "flag": "flag_name" }
    /// - VariableEquals: { "variable": "key", "value": "expected" }
    /// - VariableIsNull/IsNotNull: { "variable": "key" }
    /// </summary>
    public Dictionary<string, string> Parameters { get; set; } = new();

    /// <summary>
    /// Sub-conditions for And/Or compound conditions.
    /// </summary>
    public List<FlowCondition>? Conditions { get; set; }
}
