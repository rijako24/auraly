using System.Text.Json;
using Auraly.Platform.Application.Agents.Operations;
using Auraly.Platform.Application.Agents.Runtime;

namespace Auraly.Platform.Application.Agents.Testing;

/// <summary>
/// Executes read-only operations in agent tests and simulates every mutation.
/// This keeps the admin test console representative without writing business state.
/// </summary>
internal sealed class AgentTestOperationDecorator : IAgentOperation
{
    private readonly IAgentOperation _inner;
    private readonly AgentTestExecutionLog _log;

    public AgentTestOperationDecorator(IAgentOperation inner, AgentTestExecutionLog log)
    {
        _inner = inner;
        _log = log;
    }

    public OperationDescriptor Descriptor => _inner.Descriptor;

    public async Task<OperationOutcome> ExecuteAsync(
        JsonElement input,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        if (Descriptor.MutationScopes.Count > 0)
        {
            var simulatedCode = Descriptor.OutcomeCodes.FirstOrDefault() ?? "test.simulated";
            _log.Add("operation_executed", Descriptor.Id, new
            {
                mocked = true,
                arguments = SafeJson(input),
                outcome = simulatedCode
            });
            return OperationOutcome.Ok(simulatedCode, new { simulated = true });
        }

        var outcome = await _inner.ExecuteAsync(input, context, cancellationToken);
        _log.Add("operation_executed", Descriptor.Id, new
        {
            mocked = false,
            arguments = SafeJson(input),
            outcome = outcome.Code,
            outcome.Success
        });
        return outcome;
    }

    private static object? SafeJson(JsonElement element)
    {
        try
        {
            return JsonSerializer.Deserialize<object>(element.GetRawText());
        }
        catch (JsonException)
        {
            return element.GetRawText();
        }
    }
}

internal sealed class AgentTestTurnEffectProcessor : IDeterministicTurnEffectProcessor
{
    public Task<DeterministicTurnEffectResult> ProcessAsync(
        DeterministicTurnEffectRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new DeterministicTurnEffectResult([], [], request.TurnResult.RequestCompleted));
}
