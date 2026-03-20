using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.GenericFlow;
using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Application.Services;

public class WhatsAppMessageProcessorService : IWhatsAppMessageProcessorService
{
    private readonly IConversationService _conversationService;
    private readonly IMessageService _messageService;
    private readonly ILeadService _leadService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IFlowOrchestrationService _flowOrchestrator;
    private readonly IBlobStorageService _blobStorageService;
    private readonly ILogger<WhatsAppMessageProcessorService> _logger;

    public WhatsAppMessageProcessorService(
        IConversationService conversationService,
        IMessageService messageService,
        ILeadService leadService,
        IWhatsAppService whatsAppService,
        IFlowOrchestrationService flowOrchestrator,
        IBlobStorageService blobStorageService,
        ILogger<WhatsAppMessageProcessorService> logger)
    {
        _conversationService = conversationService;
        _messageService = messageService;
        _leadService = leadService;
        _whatsAppService = whatsAppService;
        _flowOrchestrator = flowOrchestrator;
        _blobStorageService = blobStorageService;
        _logger = logger;
    }

    public async Task<string?> VerifyWebhookAsync(string mode, string token, string challenge)
    {
        var ok = await _whatsAppService.VerifyWebhookAsync(mode, token, challenge);
        return ok ? challenge : null;
    }

    /// <summary>
    /// Procesa un mensaje entrante de WhatsApp.
    ///
    /// ORDEN deliberado:
    ///   1. Obtener/crear conversación y lead.
    ///   2. Procesar con <see cref="IFlowOrchestrationService"/> (Generic Flow).
    ///   3. Guardar mensaje del usuario y respuesta del bot cuando corresponda.
    ///   4. Actualizar lead según resultado del flujo.
    ///   5. Enviar respuesta al usuario.
    /// </summary>
    public async Task ProcessIncomingMessageAsync(
        BusinessContext businessContext,
        string userNumber,
        string messageText,
        string? customerName = null)
    {
        _logger.LogDebug(
            "Procesando mensaje de {UserNumber} en negocio {BusinessId}: {Message}",
            userNumber, businessContext.BusinessId, messageText);

        if (businessContext.AgentId is null)
        {
            _logger.LogWarning(
                "Sin AgentId en el canal WhatsApp para BusinessId={BusinessId}; no se puede ejecutar el flujo",
                businessContext.BusinessId);
            var conv = await _conversationService.GetOrCreateConversationAsync(
                businessContext.BusinessId, userNumber, customerName, agentId: null);
            await _messageService.SaveMessageAsync(conv.ConversationId, "User", messageText);
            return;
        }

        var agentId = businessContext.AgentId.Value;

        var conversation = await _conversationService.GetOrCreateConversationAsync(
            businessContext.BusinessId, userNumber, customerName, agentId);
        var lead = await _leadService.GetOrCreateLeadAsync(
            businessContext.BusinessId, userNumber, customerName);

        var result = await _flowOrchestrator.ProcessTurnAsync(
            conversation.ConversationId,
            agentId,
            userNumber,
            messageText);

        if (string.IsNullOrWhiteSpace(result.BotResponse))
        {
            await _messageService.SaveMessageAsync(conversation.ConversationId, "User", messageText);
            _logger.LogDebug(
                "Sin respuesta del bot (humano o vacío) — Conv={ConvId}",
                conversation.ConversationId);
            return;
        }

        await _messageService.SaveMessageAsync(conversation.ConversationId, "User", messageText);
        await _messageService.SaveMessageAsync(conversation.ConversationId, "Bot", result.BotResponse);

        await UpdateLeadStatusAsync(lead, result);
        await SendResponseAsync(userNumber, result.BotResponse, conversation);
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

    private async Task UpdateLeadStatusAsync(Lead lead, FlowOrchestratorResult result)
    {
        var newStatus = result.IsFlowComplete ? "Closed" : "Contacted";

        if (newStatus != lead.Status)
        {
            _logger.LogInformation(
                "Actualizando Lead {LeadId}: {OldStatus} → {NewStatus}",
                lead.LeadId, lead.Status, newStatus);
            await _leadService.UpdateLeadAsync(lead.LeadId, status: newStatus);
        }
    }
}
