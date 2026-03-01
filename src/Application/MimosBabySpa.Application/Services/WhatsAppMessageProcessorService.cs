using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Orchestration;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.Services;

public class WhatsAppMessageProcessorService : IWhatsAppMessageProcessorService
{
    private readonly IConversationService _conversationService;
    private readonly IConversationStateManager _stateManager;
    private readonly IMessageService _messageService;
    private readonly ILeadService _leadService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly HybridTransactionalOrchestrator _orchestrator;
    private readonly IBlobStorageService _blobStorageService;
    private readonly ILogger<WhatsAppMessageProcessorService> _logger;

    public WhatsAppMessageProcessorService(
        IConversationService conversationService,
        IConversationStateManager stateManager,
        IMessageService messageService,
        ILeadService leadService,
        IWhatsAppService whatsAppService,
        HybridTransactionalOrchestrator orchestrator,
        IBlobStorageService blobStorageService,
        ILogger<WhatsAppMessageProcessorService> logger)
    {
        _conversationService = conversationService;
        _stateManager = stateManager;
        _messageService = messageService;
        _leadService = leadService;
        _whatsAppService = whatsAppService;
        _orchestrator = orchestrator;
        _blobStorageService = blobStorageService;
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
    /// ORDEN deliberado:
    ///   1. Obtener/crear conversación y lead.
    ///   2. Procesar con el orquestador (historial NO incluye el mensaje actual porque aún no se guardó).
    ///   3. Guardar mensaje del usuario y respuesta del bot — UNA SOLA VEZ cada uno.
    ///   4. Actualizar lead basado en ReservationCreated (dato determinístico, no parseo de texto).
    ///   5. Enviar respuesta al usuario.
    ///
    /// Con este orden el LLM nunca ve el mensaje actual duplicado en el historial.
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

        // 2. Routing: si está en manos del humano, solo guardar mensaje y retornar (bot inhibido)
        var state = await _stateManager.GetStateByConversationIdAsync(conversation.ConversationId);
        if (state?.Owner == ConversationOwner.Human)
        {
            await _messageService.SaveMessageAsync(conversation.ConversationId, "User", messageText);
            _logger.LogInformation(
                "Conv {ConvId} en manos de humano — mensaje guardado, bot inhibido",
                conversation.ConversationId);
            return;
        }

        // 3. Procesar con el orquestador
        //    El historial que carga el orquestador NO incluye el mensaje actual (no se guardó aún).
        //    El mensaje se añade explícitamente como último rol "user" dentro del orquestador.
        var result = await _orchestrator.ProcessMessageAsync(
            conversation.ConversationId,
            businessId,
            userNumber,
            messageText);

        // 4. Guardar mensajes — UNA VEZ cada uno, DESPUÉS del orquestador
        await _messageService.SaveMessageAsync(conversation.ConversationId, "User", messageText);
        await _messageService.SaveMessageAsync(conversation.ConversationId, "Bot", result.Response);

        // 5. Actualizar lead con dato determinístico (no parseo de texto)
        await UpdateLeadStatusAsync(lead, result.ReservationCreated);

        // 6. Enviar respuesta (con imagen si aplica)
        await SendResponseAsync(userNumber, result.Response, conversation);
    }

    // ─────────────────────────────────────────────────────────────────
    // Envío de respuesta (texto o imagen según contexto)
    // ─────────────────────────────────────────────────────────────────

    private async Task SendResponseAsync(string userNumber, string response, Conversation conversation)
    {
        var planName = ExtractPlanNameFromResponse(response);

        if (!string.IsNullOrEmpty(planName))
        {
            var imageFileName = BuildPlanImageFileName(planName);
            var imageUrl      = await _blobStorageService.GetImageUrlAsync(imageFileName);

            if (!string.IsNullOrEmpty(imageUrl))
            {
                await _whatsAppService.SendImageMessageAsync(conversation.BusinessId, userNumber, imageUrl, response);
                return;
            }
        }

        await _whatsAppService.SendTextMessageAsync(conversation.BusinessId, userNumber, response);
    }

    /// <summary>
    /// Extrae el nombre de un plan de la respuesta del bot para acompañarlo con imagen.
    /// Multitenant: busca el patrón genérico "Plan X" sin depender de nombres específicos.
    /// </summary>
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

    // ─────────────────────────────────────────────────────────────────
    // Actualización del lead — basada en dato determinístico
    // ─────────────────────────────────────────────────────────────────

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
