using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

internal static class ServiceSelectionToolResults
{
    public static string Unresolved(ServiceSelectionResolution resolution, string query) =>
        ToolResultHelper.ErrorWithLlm(
            ErrorCode(resolution),
            ErrorMessage(resolution),
            null,
            new
            {
                next_action = "get_service_catalog",
                view = "services",
                query = query.Trim(),
                selection_status = resolution.Status.ToString()
            },
            recoverable: true);

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