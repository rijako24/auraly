using Microsoft.Extensions.DependencyInjection;
using MimosBabySpa.Application.Agents.Tools;

namespace MimosBabySpa.Application.Agents.Operations;

public static class AgentMethodOperationRegistration
{
    public static IServiceCollection AddDeterministicAgentMethodOperations(this IServiceCollection services)
    {
        Add(services, "get_customer_reservations", "reservation.list", "reservation.listed",
            ["method_failed"]);
        Add(services, "manage_reservation", "reservation.manage", "reservation.managed",
            ["missing_prerequisites", "reservation_not_found", "ambiguous_reservation", "slot_unavailable", "no_employee_available", "method_failed"]);
        Add(services, "search_web_recipes", "commerce.search_recipes", "recipes.found",
            ["missing_prerequisites", "recipe_search_failed", "recipe_search_timeout", "method_failed"]);
        Add(services, "search_products", "commerce.search_products", "products.found",
            ["method_failed"]);
        Add(services, "get_order_draft", "commerce.get_order_draft", "order.draft_loaded",
            ["order_draft_missing", "method_failed"]);
        Add(services, "prepare_order_checkout", "commerce.prepare_checkout", "order.checkout_prepared",
            ["order.checkout_ready", "order.checkout_payment_required", "order.checkout_pending_manual_payment", "missing_prerequisites", "order_draft_missing", "product_inactive", "checkout_mode_missing", "invalid_order_total", "payment_link_failed", "manual_payment_failed", "method_failed"]);
        Add(services, "create_order", "commerce.create_order", "order.created",
            ["missing_prerequisites", "order_draft_missing", "product_inactive", "payment_pending", "method_failed"]);
        Add(services, "escalate_to_human", "escalation.request_human", "escalation.requested",
            ["method_failed"]);
        return services;
    }

    private static string MinimalInputSchema(string operationId) => operationId switch
    {
        "reservation.manage" => """
            {"type":"object","properties":{"action":{"type":"string"},"reservation_id":{"type":["string","null"]},"payment_transaction_id":{"type":["string","null"]},"job_id":{"type":["string","null"]},"service":{"type":["string","null"]},"date":{"type":["string","null"]},"time":{"type":["string","null"]},"add_ons":{"type":["array","string","null"]},"add_ons_mode":{"type":["string","null"]},"customer_confirmed":{"type":["boolean","string","null"]},"notes":{"type":["string","null"]}},"required":["action"]}
            """,
        "commerce.search_recipes" => """
            {"type":"object","properties":{"ingredient":{"type":"string"},"query":{"type":"string"},"limit":{"type":"integer"}},"required":["ingredient"]}
            """,
        "commerce.search_products" => """
            {"type":"object","properties":{"queries":{"type":["array","string"]},"limit":{"type":"integer"}},"required":["queries"]}
            """,
        "escalation.request_human" => """
            {"type":"object","properties":{"reason":{"type":"string"},"last_user_message":{"type":"string"}},"required":["reason","last_user_message"]}
            """,
        "commerce.create_order" => """
            {"type":"object","properties":{"customer_confirmed":{"type":["boolean","string"]}},"required":["customer_confirmed"]}
            """,
        _ => "{\"type\":\"object\",\"properties\":{},\"required\":[]}"
    };
    private static IReadOnlyList<string> MutationScopes(string operationId) => operationId switch
    {
        "reservation.manage" => ["reservation.manage"],
        "commerce.prepare_checkout" => ["commerce.checkout.prepare"],
        "commerce.create_order" => ["commerce.order.create"],
        "escalation.request_human" => ["conversation.escalate"],
        _ => []
    };
    private static void Add(
        IServiceCollection services,
        string methodName,
        string operationId,
        string successCode,
        IReadOnlyList<string> errors)
    {
        services.AddScoped<IAgentOperation>(provider => new AgentMethodOperation(
            provider,
            methodName,
            operationId,
            successCode,
            errors,
            MinimalInputSchema(operationId),
            MutationScopes(operationId)));
    }
}
