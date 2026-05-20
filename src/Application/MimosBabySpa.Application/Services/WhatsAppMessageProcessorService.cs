using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Orchestration;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
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
    private readonly HybridTransactionalOrchestrator _orchestrator;
    private readonly IAgentConversationService _agentService;
    private readonly IAgentRepository _agentRepository;
    private readonly IBlobStorageService _blobStorageService;
    private readonly IBusinessConfigurationService _businessConfig;
    private readonly ILogger<WhatsAppMessageProcessorService> _logger;

    public WhatsAppMessageProcessorService(
        IConversationService conversationService,
        IConversationStateManager stateManager,
        IMessageService messageService,
        ILeadService leadService,
        IWhatsAppService whatsAppService,
        HybridTransactionalOrchestrator orchestrator,
        IAgentConversationService agentService,
        IAgentRepository agentRepository,
        IBlobStorageService blobStorageService,
        IBusinessConfigurationService businessConfig,
        ILogger<WhatsAppMessageProcessorService> logger)
    {
        _conversationService = conversationService;
        _stateManager = stateManager;
        _messageService = messageService;
        _leadService = leadService;
        _whatsAppService = whatsAppService;
        _orchestrator = orchestrator;
        _agentService = agentService;
        _agentRepository = agentRepository;
        _blobStorageService = blobStorageService;
        _businessConfig = businessConfig;
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
    ///   - Routing por UseAgenticOrchestrator (feature flag por negocio).
    ///
    /// Orden deliberado:
    ///   1. Obtener/crear conversación y lead.
    ///   2. Chequeo Owner=Human.
    ///   3. Seleccionar motor (agentic vs legacy).
    ///   4. Guardar mensaje del usuario y respuesta del bot UNA SOLA VEZ.
    ///   5. Actualizar lead y enviar respuesta.
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

        // ── Routing: agentic vs legacy ───────────────────────────────────────
        if (await ShouldUseAgenticEngineAsync(businessId))
        {
            await ProcessWithAgenticEngineAsync(conversation, businessId, userNumber, messageText, lead);
        }
        else
        {
            await ProcessWithLegacyEngineAsync(conversation, businessId, userNumber, messageText, lead);
        }
    }

    // ── Motor agentico ───────────────────────────────────────────────────────

    private async Task ProcessWithAgenticEngineAsync(
        Conversation conversation,
        Guid businessId,
        string userNumber,
        string messageText,
        Lead lead)
    {
        var agent = await ResolveActiveAgentAsync(businessId);
        if (agent is null)
        {
            _logger.LogWarning(
                "Agentic engine enabled for {BusinessId} but no active agent found. Falling back to legacy.",
                businessId);
            await ProcessWithLegacyEngineAsync(conversation, businessId, userNumber, messageText, lead);
            return;
        }

        _logger.LogInformation("Conv {ConvId}: routing to agentic engine (AgentId={AgentId})",
            conversation.ConversationId, agent.AgentId);

        var result = await _agentService.ProcessMessageAsync(
            agent.AgentId,
            conversation.ConversationId,
            messageText);

        if (!result.Success)
        {
            _logger.LogError("Agentic engine failed for Conv {ConvId}: {Err}", conversation.ConversationId, result.ErrorMessage);
            // No guardamos mensajes en caso de fallo total para evitar corrupción del historial
            return;
        }

        // El AgentConversationService ya persiste mensajes internamente.
        // Solo actualizamos lead y enviamos al canal.
        await UpdateLeadStatusAsync(lead, result.ReservationCreated);

        if (!string.IsNullOrWhiteSpace(result.Response))
            await SendResponseAsync(userNumber, result.Response, conversation);
    }

    // ── Motor legacy ─────────────────────────────────────────────────────────

    private async Task ProcessWithLegacyEngineAsync(
        Conversation conversation,
        Guid businessId,
        string userNumber,
        string messageText,
        Lead lead)
    {
        var result = await _orchestrator.ProcessMessageAsync(
            conversation.ConversationId,
            businessId,
            userNumber,
            messageText);

        await _messageService.SaveMessageAsync(conversation.ConversationId, "User", messageText);
        await _messageService.SaveMessageAsync(conversation.ConversationId, "Bot", result.Response);

        await UpdateLeadStatusAsync(lead, result.ReservationCreated);
        await SendResponseAsync(userNumber, result.Response, conversation);
    }

    // ── Feature flag ─────────────────────────────────────────────────────────

    private async Task<bool> ShouldUseAgenticEngineAsync(Guid businessId)
    {
        try
        {
            var value = await _businessConfig.GetBusinessConfigurationValueAsync(
                businessId, BusinessConfigurationKey.UseAgenticOrchestrator);
            return string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task<Agent?> ResolveActiveAgentAsync(Guid businessId)
    {
        var agents = await _agentRepository.GetByBusinessAsync(businessId);
        return agents.FirstOrDefault(a => a.IsActive);
    }

    // ── Envío de respuesta ───────────────────────────────────────────────────

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

    // ── Lead tracking ────────────────────────────────────────────────────────

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
