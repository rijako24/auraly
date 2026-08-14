using Auraly.Platform.Application.Services;

namespace Auraly.Platform.Application.Agents.Operations.Support;

internal static class ServiceSelectionResults
{
    public static string Unresolved(ServiceSelectionResolution resolution, string query) =>
        OperationJsonHelper.ErrorWithLlm(ErrorCode(resolution), ErrorMessage(resolution), new
            {
                next_action = "show_service_catalog",
                view = "services",
                query = query.Trim(),
                selection_status = resolution.Status.ToString()
            }, recoverable: true);

    private static string ErrorCode(ServiceSelectionResolution resolution) => resolution.Status switch
    {
        ServiceSelectionStatus.Ambiguous => "service_selection_ambiguous",
        ServiceSelectionStatus.NotFound => "service_selection_not_found",
        _ => "service_selection_unresolved"
    };

    private static string ErrorMessage(ServiceSelectionResolution resolution) => resolution.Status switch
    {
        ServiceSelectionStatus.Ambiguous => "Service selection is ambiguous.",
        ServiceSelectionStatus.NotFound => "Service selection was not found.",
        _ => "Service selection could not be resolved."
    };
}
