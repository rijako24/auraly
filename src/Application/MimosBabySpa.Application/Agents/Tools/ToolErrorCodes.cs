namespace MimosBabySpa.Application.Agents.Tools;

/// <summary>
/// Codigos de error estandar devueltos por tools ({ "error": { "code": "..." } }).
/// La recuperabilidad se declara en cada respuesta con error.recoverable.
/// </summary>
public static class ToolErrorCodes
{
    public const string UnknownFactKey = "unknown_fact_key";
    public const string InvalidKey = "invalid_key";
    public const string InvalidValue = "invalid_value";
    public const string InvalidType = "invalid_type";
    public const string MissingPrerequisites = "missing_prerequisites";
    public const string InvalidAddOns = "invalid_add_ons";
    public const string AmbiguousAddOns = "ambiguous_add_ons";
    public const string DuplicateAddOnGroup = "duplicate_add_on_group";
    public const string ServiceNotResolved = "service_not_resolved";
}
