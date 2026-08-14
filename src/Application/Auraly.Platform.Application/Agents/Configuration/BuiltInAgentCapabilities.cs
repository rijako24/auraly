using System.Text.Json;

namespace Auraly.Platform.Application.Agents.Configuration;

/// <summary>
/// Adds engine-owned, read-only capabilities implied by authoritative tenant configuration.
/// </summary>
public static class BuiltInAgentCapabilities
{
    public const string PaymentMethodsActionId = "system_checkout_payment_methods";
    public const string PaymentMethodsSignalType = "payment_methods_query";
    public const string PaymentMethodsOperationId = "checkout.list_payment_methods";
    public const string PaymentMethodsTemplateId = "system_checkout_payment_methods";
    public const string PaymentMethodsListedOutcome = "checkout.payment_methods_listed";
    public const string PaymentMethodsNotConfiguredOutcome = "checkout.payment_methods_not_configured";

    private const string DefaultPaymentMethodsTemplate = """
        *Medios de pago disponibles*
        {{#each payment_methods}}
        - {{label}}
        {{/each}}
        """;


    public static IReadOnlyList<AgentGlobalAction> AddPaymentMethodsAction(
        IReadOnlyList<AgentGlobalAction> configured,
        CheckoutDefinitions checkout)
    {
        if (!HasPaymentMethods(checkout)
            || configured.Any(action =>
                action.Id.Equals(PaymentMethodsActionId, StringComparison.OrdinalIgnoreCase)
                || action.Signal.Type.Equals(PaymentMethodsSignalType, StringComparison.OrdinalIgnoreCase)))
        {
            return configured;
        }

        return configured.Concat([CreatePaymentMethodsAction()]).ToArray();
    }

    public static IReadOnlyDictionary<string, string> AddPaymentMethodsTemplate(
        IReadOnlyDictionary<string, string> configured,
        CheckoutDefinitions checkout)
    {
        if (!HasPaymentMethods(checkout) || configured.ContainsKey(PaymentMethodsTemplateId))
            return configured;

        return new Dictionary<string, string>(configured, StringComparer.OrdinalIgnoreCase)
        {
            [PaymentMethodsTemplateId] = DefaultPaymentMethodsTemplate
        };
    }

    public static bool HasPaymentMethods(CheckoutDefinitions checkout) =>
        checkout.Modes.Values.Any(mode => mode.PaymentMethods.Count > 0);

    private static AgentGlobalAction CreatePaymentMethodsAction() => new()
    {
        Id = PaymentMethodsActionId,
        Priority = 800,
        Goal = "Informar los medios de pago configurados sin seleccionar uno ni modificar la transaccion.",
        ConversationGuidance = "Emite esta senal cuando el cliente pregunte cuales medios, metodos, formas u opciones de pago estan disponibles.",
        Signal = new StageSignalDefinition
        {
            Type = PaymentMethodsSignalType,
            Description = "Consulta explicita del cliente sobre los medios, metodos, formas u opciones de pago disponibles.",
            ValueSchema = ParseJson("""
                {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": {}
                }
                """)
        },
        Actions =
        [
            new StageActionDefinition
            {
                Id = "list_configured_payment_methods",
                Operation = PaymentMethodsOperationId,
                Trigger = StageActionTriggers.OnSignal,
                Signal = PaymentMethodsSignalType,
                Execution = new StageActionExecutionDefinition
                {
                    Idempotency = StageActionIdempotency.None,
                    MaxAttempts = 1
                },
                OnOutcome = new Dictionary<string, StageOutcomeHandlerDefinition>(StringComparer.OrdinalIgnoreCase)
                {
                    [PaymentMethodsListedOutcome] = new()
                    {
                        Response = new StageResponseDefinition
                        {
                            Mode = "continue",
                            Guidance = "Presenta todos los medios de pago devueltos por la operacion. No selecciones ninguno."
                        }
                    },
                    [PaymentMethodsNotConfiguredOutcome] = new()
                    {
                        Response = new StageResponseDefinition
                        {
                            Mode = "continue",
                            Guidance = "Indica que no hay medios de pago configurados disponibles para informar."
                        }
                    }
                }
            }
        ]
    };

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
