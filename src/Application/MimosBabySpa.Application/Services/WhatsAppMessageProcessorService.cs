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
    ///   1. Obtener/crear conversación y lead.
    ///   2. Chequeo Owner=Human.
    ///   3. Resolver agente activo y delegar a AgentConversationService.
    ///   4. Actualizar lead y enviar respuesta al canal.
    /// </summary>
    public async Task ProcessIncomingMessageAsync(
        Guid businessId,
        string userNumber,
        string messageText,
        string? customerName = null)
    {
        _logger.LogDebug(
            "Procesando mensaje de {UserNumber} en negocio {BusinessId}: {Message}",
            userNumber, businessId, messageText);

        // 1. Obtener/crear conversación y lead
        var conversation = await _conversationService.GetOrCreateConversationAsync(businessId, userNumber, customerName);
        var lead = await _leadService.GetOrCreateLeadAsync(businessId, userNumber, customerName);

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
        var agent = await ResolveActiveAgentAsync(businessId);
        if (agent is null)
        {
            _logger.LogWarning("No hay agente activo para negocio {BusinessId}. Mensaje ignorado.", businessId);
            return;
        }

        // 3. Procesar con el motor agentico
        var result = await _agentService.ProcessMessageAsync(
            agent.AgentId,
            conversation.ConversationId,
            messageText,
            userNumber);

        if (!result.Success)
        {
            _logger.LogError("AgentConversationService falló para Conv {ConvId}: {Err}",
                conversation.ConversationId, result.ErrorMessage);
            return;
        }

        // 4. Actualizar lead y enviar al canal
        await UpdateLeadStatusAsync(lead, result.ReservationCreated);

        if (!string.IsNullOrWhiteSpace(result.Response))
            await SendResponseAsync(userNumber, result.Response, conversation);

        if (result.OutboundMessages.Count > 0)
        {
            await _outboundDispatcher.SendAllAsync(
                conversation.BusinessId,
                userNumber,
                result.OutboundMessages);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Agent?> ResolveActiveAgentAsync(Guid businessId)
    {
        var agents = await _agentRepository.GetByBusinessAsync(businessId);
        return agents.FirstOrDefault(a => a.IsActive);
    }

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

    private async Task UpdateLeadStatusAsync(Lead lead, bool reservationCreated)
    {
        var newStatus = reservationCreated ? "Closed" : "Contacted";

        if (newStatus != lead.Status)
        {
            _logger.LogInformation(
                "Actualizando Lead {LeadId}: {OldStatus} → {NewStatus}",
                lead.LeadId, lead.Status, newStatus);
            await _leadService.UpdateLeadAsync(lead.LeadId, status: newStatus);
        }
    }
}
