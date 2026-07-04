using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public class WhatsAppMessageProcessorService : IWhatsAppMessageProcessorService
{
    private readonly IConversationService _conversationService;
    private readonly IConversationStateManager _stateManager;
    private readonly IMessageService _messageService;
    private readonly ILeadService _leadService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IAgentConversationService _agentService;
    private readonly IAgentRepository _agentRepository;
    private readonly IBlobStorageService _blobStorageService;
    private readonly IOutboundMessageDispatcher _outboundDispatcher;
    private readonly IBusinessInboundContactRouter _businessInboundContactRouter;
    private readonly ILogger<WhatsAppMessageProcessorService> _logger;

    public WhatsAppMessageProcessorService(
        IConversationService conversationService,
        IConversationStateManager stateManager,
        IMessageService messageService,
        ILeadService leadService,
        IWhatsAppService whatsAppService,
        IAgentConversationService agentService,
        IAgentRepository agentRepository,
        IBlobStorageService blobStorageService,
        IOutboundMessageDispatcher outboundDispatcher,
        IBusinessInboundContactRouter businessInboundContactRouter,
        ILogger<WhatsAppMessageProcessorService> logger)
    {
        _conversationService = conversationService;
        _stateManager = stateManager;
        _messageService = messageService;
        _leadService = leadService;
        _whatsAppService = whatsAppService;
        _agentService = agentService;
        _agentRepository = agentRepository;
        _blobStorageService = blobStorageService;
        _outboundDispatcher = outboundDispatcher;
        _businessInboundContactRouter = businessInboundContactRouter;
        _logger = logger;
    }

    public Task<string?> VerifyWebhookAsync(string mode, string token, string challenge)
    {
        return _whatsAppService.VerifyWebhookAsync(mode, token, challenge)
            .ContinueWith(t => t.Result ? challenge : (string?)null);
    }

    /// <summary>
    /// Procesa un mensaje entrante de WhatsApp.
    ///
    /// Capa 1 (guardrail pre-LLM):
    ///   - Owner=Human → guarda mensaje y detiene procesamiento del bot.
    ///
    /// Orden:
    ///   1. Obtener/crear conversación; solo clientes externos generan lead.
    ///   2. Chequeo Owner=Human.
    ///   3. Resolver agente activo y delegar a AgentConversationService.
    ///   4. Actualizar lead y enviar respuesta al canal.
    /// </summary>
    public async Task ProcessIncomingMessageAsync(
        Guid businessId,
        string userNumber,
        string messageText,
        string? customerName = null,
        AgentInboundMetadata? inboundMetadata = null)
    {
        _logger.LogDebug(
            "Procesando mensaje de {UserNumber} en negocio {BusinessId}: {Message}",
            userNumber, businessId, messageText);

        var inboundRoute = await _businessInboundContactRouter.ResolveAsync(businessId, userNumber);

        // 1. Obtener/crear conversación; los contactos inbound no generan lead de cliente
        var conversation = await _conversationService.GetOrCreateConversationAsync(businessId, userNumber, customerName);
        Lead? lead = null;
        if (inboundRoute is null)
            lead = await _leadService.GetOrCreateLeadAsync(businessId, userNumber, customerName);

        // ── Capa 1: handover humano — corto-circuito ─────────────────────────
        var state = await _stateManager.GetStateByConversationIdAsync(conversation.ConversationId);
        if (state?.Owner == ConversationOwner.Human)
        {
            await _messageService.SaveMessageAsync(conversation.ConversationId, "User", messageText);
            _logger.LogInformation(
                "Conv {ConvId} en manos de humano — mensaje guardado, bot inhibido",
                conversation.ConversationId);
            return;
        }

        // 2. Resolver agente activo para el negocio
        var agentId = inboundRoute?.AgentId;
        if (agentId is null)
        {
            var agent = await ResolveActiveAgentAsync(businessId);
            agentId = agent?.AgentId;
        }

        if (agentId is null)
        {
            throw new InvalidOperationException($"No hay agente activo para negocio {businessId}.");
        }

        // 3. Procesar con el motor agentico
        var result = await _agentService.ProcessMessageAsync(
            agentId.Value,
            conversation.ConversationId,
            messageText,
            userNumber,
            inboundMetadata: inboundMetadata);

        if (!result.Success)
        {
            _logger.LogError("AgentConversationService falló para Conv {ConvId}: {Err}",
                conversation.ConversationId, result.ErrorMessage);
            throw new InvalidOperationException(
                $"AgentConversationService fallo para la conversacion {conversation.ConversationId}: {result.ErrorMessage ?? "sin detalle"}");
        }

        // 4. Actualizar lead y enviar al canal
        if (lead is not null)
            await UpdateLeadStatusAsync(lead, result.RequestCompleted);

        if (!string.IsNullOrWhiteSpace(result.Response))
            await SendResponseAsync(userNumber, result.Response, conversation);

        if (result.OutboundMessages.Count > 0)
        {
            await _outboundDispatcher.SendAllAsync(
                conversation.BusinessId,
                userNumber,
                result.OutboundMessages,
                conversation.ConversationId,
                throwOnFailure: true);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Task<Agent?> ResolveActiveAgentAsync(Guid businessId) =>
        _agentRepository.GetActiveCustomerByBusinessAsync(businessId);

    private async Task SendResponseAsync(string userNumber, string response, Conversation conversation)
    {
        var planName = ExtractPlanNameFromResponse(response);

        if (!string.IsNullOrEmpty(planName))
        {
            var imageFileName = BuildPlanImageFileName(planName);
            var imageUrl = await _blobStorageService.GetImageUrlAsync(conversation.BusinessId, imageFileName);

            if (!string.IsNullOrEmpty(imageUrl))
            {
                await _whatsAppService.SendImageMessageAsync(conversation.BusinessId, userNumber, imageUrl, response);
                return;
            }
        }

        await _whatsAppService.SendTextMessageAsync(conversation.BusinessId, userNumber, response);
    }

    private string? ExtractPlanNameFromResponse(string response)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            response,
            @"\bPlan\s+([A-Za-záéíóúñÁÉÍÓÚÑ\s]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return match.Success ? match.Groups[0].Value.Trim() : null;
    }

    private static string BuildPlanImageFileName(string planName)
    {
        var normalized = System.Text.RegularExpressions.Regex
            .Replace(planName, @"[^a-zA-Z0-9-]", "")
            .ToLowerInvariant();

        return $"plan-{normalized}.jpg";
    }

    private async Task UpdateLeadStatusAsync(Lead lead, bool requestCompleted)
    {
        var newStatus = requestCompleted ? "Closed" : "Contacted";

        if (newStatus != lead.Status)
        {
            _logger.LogInformation(
                "Actualizando Lead {LeadId}: {OldStatus} → {NewStatus}",
                lead.LeadId, lead.Status, newStatus);
            await _leadService.UpdateLeadAsync(lead.LeadId, status: newStatus);
        }
    }
}
