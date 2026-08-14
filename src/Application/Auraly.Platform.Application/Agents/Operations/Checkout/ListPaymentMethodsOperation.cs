using System.Text.Json;
using Auraly.Platform.Application.Agents.Configuration;
using Auraly.Platform.Application.Agents.Templates;

namespace Auraly.Platform.Application.Agents.Operations.Checkout;

/// <summary>
/// Lists every customer-facing payment-method label configured for the current tenant.
/// </summary>
public sealed class ListPaymentMethodsOperation : IAgentOperation
{
    private const string InputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {}
        }
        """;

    public OperationDescriptor Descriptor { get; } = new(
        BuiltInAgentCapabilities.PaymentMethodsOperationId,
        InputSchema,
        [
            BuiltInAgentCapabilities.PaymentMethodsListedOutcome,
            BuiltInAgentCapabilities.PaymentMethodsNotConfiguredOutcome
        ],
        [],
        [BuiltInAgentCapabilities.PaymentMethodsTemplateId],
        []);

    public Task<OperationOutcome> ExecuteAsync(
        JsonElement input,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paymentMethods = context.Config.Checkout.Modes.Values
            .SelectMany(mode => mode.PaymentMethods)
            .Select(method => string.IsNullOrWhiteSpace(method.Value.Label)
                ? method.Key.Trim()
                : method.Value.Label.Trim())
            .Where(label => !string.IsNullOrWhiteSpace(label) && seen.Add(label))
            .Select(label => new Dictionary<string, object?> { ["label"] = label })
            .ToArray();

        if (paymentMethods.Length == 0)
        {
            return Task.FromResult(OperationOutcome.Fail(
                BuiltInAgentCapabilities.PaymentMethodsNotConfiguredOutcome,
                "No payment methods are configured for this agent."));
        }

        var data = new Dictionary<string, object?>
        {
            ["payment_methods"] = paymentMethods
        };
        return Task.FromResult(OperationOutcome.Ok(
            BuiltInAgentCapabilities.PaymentMethodsListedOutcome,
            data,
            [
                new OperationPresentation(
                    BuiltInAgentCapabilities.PaymentMethodsTemplateId,
                    data,
                    FragmentRenderMode.Exclusive,
                    FragmentPriority.Required)
            ]));
    }
}
