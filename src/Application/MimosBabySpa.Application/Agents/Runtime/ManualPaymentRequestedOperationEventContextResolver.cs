using System.Text.Json;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Runtime;

public sealed class ManualPaymentRequestedOperationEventContextResolver : IOperationEventContextResolver
{
    public bool CanResolve(string eventName) =>
        eventName.Equals("manual_payment_requested", StringComparison.OrdinalIgnoreCase);

    public Task<MessageSequenceContext> ResolveAsync(
        OperationEvent operationEvent,
        IReadOnlyDictionary<string, string> facts,
        CancellationToken cancellationToken = default)
    {
        var custom = new Dictionary<string, string>(facts, StringComparer.OrdinalIgnoreCase);
        if (operationEvent.Payload.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in operationEvent.Payload.EnumerateObject())
            {
                var value = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => null
                };
                if (value is not null)
                    custom[property.Name] = value;
            }
        }

        return Task.FromResult(new MessageSequenceContext { Custom = custom });
    }
}
