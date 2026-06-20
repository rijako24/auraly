using System.Text.Json;
using MimosBabySpa.Application.Commerce;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

public sealed class RemoveOrderItemTool : IAgentTool
{
    private readonly ICommerceService _commerce;

    public RemoveOrderItemTool(ICommerceService commerce) => _commerce = commerce;

    public string Name => "remove_order_item";
    public IReadOnlyList<string> Capabilities => [ToolCapabilities.OrderDraftUpdate];
    public string Description => "Removes an item from the current conversation order draft.";
    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "order_item_id": { "type": "string" }
          },
          "required": ["order_item_id"]
        }
        """;

    public async Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext ctx, CancellationToken cancellationToken = default)
    {
        if (!ToolResultHelper.TryGetString(arguments, "order_item_id", out var id) || !Guid.TryParse(id, out var orderItemId))
            return ToolResultHelper.MissingPrerequisites(["order_item_id"]);
        var draft = await _commerce.RemoveItemAsync(ctx, orderItemId, cancellationToken);
        return ToolResultHelper.Ok(new { order = draft });
    }
}
