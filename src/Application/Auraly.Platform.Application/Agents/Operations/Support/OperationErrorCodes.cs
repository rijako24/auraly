namespace Auraly.Platform.Application.Agents.Operations.Support;

/// <summary>
/// Codigos de error estandar devueltos por operations ({ "error": { "code": "..." } }).
/// La recuperabilidad se declara en cada respuesta con error.recoverable.
/// </summary>
public static class OperationErrorCodes
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
    public const string AmbiguousServiceSelection = "ambiguous_service_selection";
    public const string ServiceSelectionMismatch = "service_selection_mismatch";
}
