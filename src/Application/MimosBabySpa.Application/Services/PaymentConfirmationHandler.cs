using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.GenericFlow;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Manejador de confirmación de pago (webhook Wompi, poller o confirmación manual).
/// La fuente de verdad del pago es <see cref="PaymentTransaction"/> (referencia, monto, conversación).
/// No lee nombres de variables del <c>DefinitionJson</c>: el grafo sigue siendo configurable sin acoplar el handler.
/// Marca el flag <c>payment_confirmed</c> (contrato con <c>WaitForEvent.event_type</c> en el flujo) y dispara un turno sintético.
/// </summary>
public class PaymentConfirmationHandler : IPaymentConfirmationHandler
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IPaymentTransactionRepository _paymentTransactionRepository;
    private readonly IConversationRepository _conversationRepository;
    private readonly IAgentRepository _agentRepository;
    private readonly IFlowExecutionStateRepository _flowExecutionStateRepository;
    private readonly IFlowOrchestrationService _flowOrchestrator;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IMessageService _messageService;
    private readonly ILogger<PaymentConfirmationHandler> _logger;

    public PaymentConfirmationHandler(
        IPaymentTransactionRepository paymentTransactionRepository,
        IConversationRepository conversationRepository,
        IAgentRepository agentRepository,
        IFlowExecutionStateRepository flowExecutionStateRepository,
        IFlowOrchestrationService flowOrchestrator,
        IWhatsAppService whatsAppService,
        IMessageService messageService,
        ILogger<PaymentConfirmationHandler> logger)
    {
        _paymentTransactionRepository = paymentTransactionRepository;
        _conversationRepository = conversationRepository;
        _agentRepository = agentRepository;
        _flowExecutionStateRepository = flowExecutionStateRepository;
        _flowOrchestrator = flowOrchestrator;
        _whatsAppService = whatsAppService;
        _messageService = messageService;
        _logger = logger;
    }

    public async Task<PaymentConfirmationResult> HandleAsync(
        string paymentReferenceId,
        string providerTransactionId,
        long amountInCents,
        string webhookPayload,
        CancellationToken ct = default)
    {
        var paymentTx = await _paymentTransactionRepository.GetByPaymentReferenceIdAsync(paymentReferenceId, ct);
        if (paymentTx == null)
        {
            _logger.LogWarning("Webhook: PaymentReferenceId={Ref} no encontrado en PaymentTransactions", paymentReferenceId);
            return new PaymentConfirmationResult(false, "Conversación no encontrada");
        }

        if (paymentTx.Status == PaymentTransactionStatus.Confirmed)
        {
            _logger.LogInformation("Webhook: pago ya procesado (idempotencia) Ref={Ref}", paymentReferenceId);
            return new PaymentConfirmationResult(true, null);
        }

        if (paymentTx.AmountInCents != amountInCents)
        {
            _logger.LogWarning(
                "Webhook: monto no coincide con PaymentTransaction esperado={Expected} recibido={Received} Ref={Ref}",
                paymentTx.AmountInCents, amountInCents, paymentReferenceId);
            return new PaymentConfirmationResult(false, "Monto no coincide");
        }

        var conversation = await _conversationRepository.GetByIdAsync(paymentTx.ConversationId);
        if (conversation == null)
        {
            _logger.LogWarning("Webhook: conversación no encontrada ConvId={ConvId}", paymentTx.ConversationId);
            return new PaymentConfirmationResult(false, "Conversación no encontrada");
        }

        FlowExecutionStateEntity? flowState = null;
        Agent? agent = null;

        if (conversation.AgentId is Guid preferredAgentId)
        {
            var preferredAgent = await _agentRepository.GetByIdAsync(preferredAgentId, ct);
            if (preferredAgent is { IsActive: true, BusinessId: var bid } && bid == conversation.BusinessId)
            {
                flowState = await _flowExecutionStateRepository.GetAsync(
                    conversation.BusinessId, conversation.UserNumber, preferredAgentId, ct);
                if (flowState != null)
                    agent = preferredAgent;
            }
            else
            {
                _logger.LogWarning(
                    "Webhook: Conversation.AgentId={AgentId} inválido o inactivo para Business={BusinessId}; heurística multi-agente",
                    preferredAgentId, conversation.BusinessId);
            }
        }

        if (flowState == null)
        {
            var agents = await _agentRepository.GetByBusinessAsync(conversation.BusinessId, ct);
            if (agents.Count == 0)
            {
                _logger.LogWarning("Webhook: sin agentes activos para BusinessId={BusinessId}", conversation.BusinessId);
                return new PaymentConfirmationResult(false, "Agente no encontrado");
            }

            (flowState, agent) = await ResolveFlowStateAsync(
                conversation.BusinessId, conversation.UserNumber, agents, ct);
        }

        if (flowState == null || agent == null)
        {
            _logger.LogWarning(
                "Webhook: no hay FlowExecutionState para User={User} Business={BusinessId}",
                conversation.UserNumber, conversation.BusinessId);
            return new PaymentConfirmationResult(false, "Estado de flujo no encontrado");
        }

        paymentTx.ProviderTransactionId = providerTransactionId;
        paymentTx.Status = PaymentTransactionStatus.Confirmed;
        paymentTx.ConfirmedAt = DateTime.UtcNow;
        paymentTx.WebhookPayloadJson = webhookPayload;
        await _paymentTransactionRepository.SaveAsync(paymentTx, ct);

        SetPaymentConfirmedFlag(flowState);
        await _flowExecutionStateRepository.UpsertAsync(flowState, ct);

        const string syntheticUserMessage = "[pago confirmado]";
        var result = await _flowOrchestrator.ProcessTurnAsync(
            paymentTx.ConversationId,
            agent.AgentId,
            conversation.UserNumber,
            syntheticUserMessage,
            ct);

        if (!string.IsNullOrWhiteSpace(result.BotResponse))
        {
            await _messageService.SaveMessageAsync(paymentTx.ConversationId, "User", syntheticUserMessage);
            await _messageService.SaveMessageAsync(paymentTx.ConversationId, "Bot", result.BotResponse);
            await _whatsAppService.SendTextMessageAsync(
                conversation.BusinessId, conversation.UserNumber, result.BotResponse);
        }

        if (!result.Success)
        {
            _logger.LogWarning(
                "Webhook: turno post-pago sin éxito Ref={Ref} Error={Error}",
                paymentReferenceId, result.ErrorMessage);
            return new PaymentConfirmationResult(false, result.ErrorMessage ?? "Error al procesar el pago");
        }

        _logger.LogInformation("Webhook: flujo post-pago completado Ref={Ref}", paymentReferenceId);
        return new PaymentConfirmationResult(true, null);
    }

    /// <summary>
    /// Elige la sesión de flujo más reciente para el usuario entre los agentes del negocio (sin leer VariablesJson).
    /// </summary>
    private async Task<(FlowExecutionStateEntity? State, Agent? Agent)> ResolveFlowStateAsync(
        Guid businessId,
        string userNumber,
        IReadOnlyList<Agent> agents,
        CancellationToken ct)
    {
        FlowExecutionStateEntity? bestState = null;
        Agent? bestAgent = null;
        var bestUpdated = DateTime.MinValue;

        foreach (var ag in agents)
        {
            var s = await _flowExecutionStateRepository.GetAsync(businessId, userNumber, ag.AgentId, ct);
            if (s == null)
                continue;

            if (s.UpdatedAt >= bestUpdated)
            {
                bestUpdated = s.UpdatedAt;
                bestState = s;
                bestAgent = ag;
            }
        }

        return (bestState, bestAgent);
    }

    private static void SetPaymentConfirmedFlag(FlowExecutionStateEntity entity)
    {
        var flags = DeserializeFlags(entity.FlagsJson);
        flags["payment_confirmed"] = true;
        entity.FlagsJson = JsonSerializer.Serialize(flags, JsonOpts);
    }

    private static Dictionary<string, bool> DeserializeFlags(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, bool>>(json, JsonOpts)
                   ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
