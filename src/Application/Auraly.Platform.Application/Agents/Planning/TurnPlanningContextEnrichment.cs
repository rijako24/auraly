using System.Text.Json;
using Auraly.Platform.Application.Agents.Operations;

namespace Auraly.Platform.Application.Agents.Planning;

public sealed record TurnPlanningContextFragment(string Key, JsonElement Value);

public interface ITurnPlanningContextEnricher
{
    Task<TurnPlanningContextFragment?> EnrichAsync(
        AgentConfig config,
        OperationContext operationContext,
        CancellationToken cancellationToken = default);
}