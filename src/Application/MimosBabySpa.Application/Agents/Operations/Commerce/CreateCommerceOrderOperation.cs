using System.Text.Json;
using MimosBabySpa.Application.Commerce;

namespace MimosBabySpa.Application.Agents.Operations.Commerce;

public sealed class CreateCommerceOrderOperation : IAgentOperation
{
    private readonly ICommerceService _commerce;

    public CreateCommerceOrderOperation(ICommerceService commerce) => _commerce = commerce;

    public OperationDescriptor Descriptor { get; } = new(
        "commerce.create_order",
        """{"type":"object","additionalProperties":false,"properties":{"customer_confirmed":{"type":["boolean","string"]},"customer_name":{"type":"string"},"customer_email":{"type":"string"},"customer_phone":{"type":"string"},"customer_document":{"type":"string"},"delivery_address":{"type":"string"},"notes":{"type":"string"}},"required":["customer_confirmed"]}""",
        ["order.created", "order.not_confirmed", "product_inactive", "order.creation_failed"],
        ["commerce.order.create"], [], []);

    public async Task<OperationOutcome> ExecuteAsync(
        JsonElement input,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        var session = context.Session
            ?? throw new InvalidOperationException("commerce.create_order requires a conversation session.");
        var confirmed = ReadBoolean(input, "customer_confirmed");
        try
        {
            var order = await _commerce.CreateOrderAsync(
                session,
                new CreateOrderRequest(
                    confirmed,
                    ReadString(input, "customer_name"),
                    ReadString(input, "customer_email"),
                    ReadString(input, "customer_phone"),
                    ReadString(input, "customer_document"),
                    ReadString(input, "delivery_address"),
                    ReadString(input, "notes")),
                cancellationToken);
            if (!confirmed)
                return OperationOutcome.Ok("order.not_confirmed", new { order, is_order_confirmed = false });

            return OperationOutcome.Ok(
                "order.created",
                new { order, is_order_confirmed = true },
                effects: [new CompleteRequestOperationEffect()],
                events: ["order_created"],
                domainEvents: [OperationEvent.Create("order_created", new { businessId = context.BusinessId, orderId = order.OrderId })]);
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("Product inactive", StringComparison.OrdinalIgnoreCase))
        {
            return OperationOutcome.Fail("product_inactive", "The order contains an inactive product.", true);
        }
        catch (Exception exception)
        {
            return OperationOutcome.Fail("order.creation_failed", exception.Message, true);
        }
    }

    private static string? ReadString(JsonElement input, string name) =>
        input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool ReadBoolean(JsonElement input, string name) =>
        input.TryGetProperty(name, out var value) && (value.ValueKind == JsonValueKind.True
            || value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) && parsed);
}
