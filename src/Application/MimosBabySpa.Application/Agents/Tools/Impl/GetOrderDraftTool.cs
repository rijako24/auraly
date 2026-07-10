using System.Text.Json;
using MimosBabySpa.Application.Commerce;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

[AgentToolMetadata("get_order_draft")]
public sealed class GetOrderDraftTool : IAgentTool
{
private readonly ICommerceService _commerce;

    public GetOrderDraftTool(ICommerceService commerce) => _commerce = commerce;

    public string Name => "get_order_draft";
    public string Description => "Returns the current conversation order draft, including items and totals.";
    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {}
        }
        """;

    public async Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext ctx, CancellationToken cancellationToken = default)
    {
        var draft = await _commerce.GetDraftAsync(ctx, cancellationToken);
        return ToolResultHelper.Ok(new { order = draft });
    }
}
