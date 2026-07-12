using System.Text.Json;
using MimosBabySpa.Application.Agents.Operations;

namespace MimosBabySpa.Application.Agents.Planning;

public sealed record TurnPlanningContextFragment(string Key, JsonElement Value);

public interface ITurnPlanningContextEnricher
{
    Task<TurnPlanningContextFragment?> EnrichAsync(
        AgentConfig config,
        OperationContext operationContext,
        CancellationToken cancellationToken = default);
}