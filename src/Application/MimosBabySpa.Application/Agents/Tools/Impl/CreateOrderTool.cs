using System.Text.Json;
using MimosBabySpa.Application.Commerce;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

public sealed class CreateOrderTool : IAgentTool
{
    private readonly ICommerceService _commerce;

    public CreateOrderTool(ICommerceService commerce) => _commerce = commerce;

    public string Name => "create_order";
    public IReadOnlyList<string> Capabilities => [ToolCapabilities.OrderCreate];
    public string Description => "Creates the current order locally and, when configured, sends it to the commerce provider.";
    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "customer_confirmed": { "type": "boolean" },
            "customer_name": { "type": "string" },
            "customer_email": { "type": "string" },
            "customer_phone": { "type": "string" },
            "customer_document": { "type": "string" },
            "delivery_address": { "type": "string" },
            "notes": { "type": "string" }
          },
          "required": ["customer_confirmed"]
        }
        """;

    public async Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext ctx, CancellationToken cancellationToken = default)
    {
        var confirmed = ToolResultHelper.TryGetBool(arguments, "customer_confirmed", out var c) && c;
        var order = await _commerce.CreateOrderAsync(
            ctx,
            new CreateOrderRequest(
                confirmed,
                Get(arguments, "customer_name"),
                Get(arguments, "customer_email"),
                Get(arguments, "customer_phone"),
                Get(arguments, "customer_document"),
                Get(arguments, "delivery_address"),
                Get(arguments, "notes")),
            cancellationToken);

        return ToolResultHelper.Ok(new { order, is_order_confirmed = order.Status is Domain.Enums.OrderStatus.Confirmed or Domain.Enums.OrderStatus.Synced });
    }

    private static string? Get(JsonElement args, string property) =>
        ToolResultHelper.TryGetString(args, property, out var value) ? value : null;
}
