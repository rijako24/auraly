using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Application.Services;

public class WhatsAppMessageProcessorService : IWhatsAppMessageProcessorService
{
    private readonly IConversationService _conversationService;
    private readonly IMessageService _messageService;
    private readonly ILeadService _leadService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IConversationAgent _conversationAgent;
    private readonly IBlobStorageService _blobStorageService;
    private readonly ILogger<WhatsAppMessageProcessorService> _logger;

    public WhatsAppMessageProcessorService(
        IConversationService conversationService,
        IMessageService messageService,
        ILeadService leadService,
        IWhatsAppService whatsAppService,
        IConversationAgent conversationAgent,
        IBlobStorageService blobStorageService,
        ILogger<WhatsAppMessageProcessorService> logger)
    {
        _conversationService = conversationService;
        _messageService = messageService;
        _leadService = leadService;
        _whatsAppService = whatsAppService;
        _conversationAgent = conversationAgent;
        _blobStorageService = blobStorageService;
        _logger = logger;
    }

    public async Task<string?> VerifyWebhookAsync(string mode, string token, string challenge)
    {
        var isValid = await _whatsAppService.VerifyWebhookAsync(mode, token, challenge);
        return isValid ? challenge : null;
    }

    public async Task ProcessIncomingMessageAsync(Guid businessId, string userNumber, string messageText, string? customerName = null)
    {
        try
        {
            _logger.LogDebug("Procesando mensaje de {UserNumber} en negocio {BusinessId}: {Message}", userNumber, businessId, messageText);

            // 1. Obtener o crear conversación
            var conversation = await _conversationService.GetOrCreateConversationAsync(businessId, userNumber, customerName);

            // 2. Obtener o crear lead
            var lead = await _leadService.GetOrCreateLeadAsync(businessId, userNumber, customerName);

            // 3. Guardar mensaje del usuario (sin clasificar intención manualmente)
            await _messageService.SaveMessageAsync(conversation.ConversationId, "User", messageText, "FollowUp");

            // 4. Procesar mensaje con el agente conversacional autónomo
            // El agente decide cuándo usar tools (check_availability, create_reservation)
            var agentResponse = await _conversationAgent.ProcessMessageAsync(
                businessId,
                messageText,
                conversation,
                lead);

            // 5. Guardar respuesta del bot
            await _messageService.SaveMessageAsync(conversation.ConversationId, "Bot", agentResponse, "FollowUp");

            // 6. Actualizar contexto de conversación (para compatibilidad)
            await UpdateConversationContextAsync(conversation, messageText, "FollowUp", lead);

            // 7. Enviar respuesta (con soporte para imágenes si aplica)
            await SendResponseAsync(userNumber, agentResponse, conversation);

            // 8. Actualizar estado del lead si es necesario
            await UpdateLeadStatusAsync(lead, agentResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando mensaje de {UserNumber}", userNumber);
            await _whatsAppService.SendTextMessageAsync(userNumber, 
                "Disculpa, estoy teniendo dificultades técnicas. Por favor intenta de nuevo en un momento.");
        }
    }


    private async Task UpdateConversationContextAsync(Conversation conversation, string messageText, string intent, Lead lead)
    {
        // Actualizar LastMessage y LastIntent en Conversation (para compatibilidad)
        await _conversationService.UpdateConversationContextAsync(
            conversation.ConversationId,
            messageText,
            intent
        );
    }

    private async Task SendResponseAsync(string userNumber, string response, Conversation conversation)
    {
        // Intentar detectar si hay un plan mencionado en la respuesta para enviar imagen
        // Buscar directamente en la respuesta del agente
        var planName = ExtractPlanNameFromResponse(response);
        
        if (!string.IsNullOrEmpty(planName))
        {
            var imageFileName = await GetPlanImageFileNameAsync(planName, conversation.BusinessId);
            if (!string.IsNullOrEmpty(imageFileName))
            {
                var imageUrl = await _blobStorageService.GetImageUrlAsync(imageFileName);
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    await _whatsAppService.SendImageMessageAsync(userNumber, imageUrl, response);
                    return;
                }
            }
        }

        // Enviar solo texto
        await _whatsAppService.SendTextMessageAsync(userNumber, response);
    }

    private string? ExtractPlanNameFromResponse(string response)
    {
        // Buscar patrones como "Plan Marineritos", "Plan Aventuras Marinas", etc. en la respuesta
        var planMatch = System.Text.RegularExpressions.Regex.Match(
            response, 
            @"Plan\s+([A-Za-záéíóúñÁÉÍÓÚÑ\s]+)", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (planMatch.Success && planMatch.Groups.Count > 0)
        {
            return planMatch.Groups[0].Value.Trim(); // Retornar "Plan Marineritos" completo
        }
        
        return null;
    }


    private Task<string> GetPlanImageFileNameAsync(string planName, Guid businessId)
    {
        if (string.IsNullOrEmpty(planName))
        {
            return Task.FromResult("plan-default.jpg");
        }

        // Convertir nombre del plan a nombre de archivo de forma genérica
        // Ejemplo: "Plan Marineritos" -> "plan-marineritos.jpg"
        var normalizedPlanName = System.Text.RegularExpressions.Regex.Replace(planName, @"[^a-zA-Z0-9-]", "").ToLower();
        return Task.FromResult($"plan-{normalizedPlanName}.jpg");
    }

    private async Task UpdateLeadStatusAsync(Lead lead, string agentResponse)
    {
        // Detectar si la respuesta indica una reserva exitosa
        var reservationKeywords = new[] { "reserva confirmada", "reserva creada", "reservación exitosa", "confirmada" };
        var isReservationConfirmed = reservationKeywords.Any(keyword => 
            agentResponse.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        var newStatus = isReservationConfirmed ? "Closed" : "Contacted";

        if (newStatus != lead.Status)
        {
            await _leadService.UpdateLeadAsync(lead.LeadId, status: newStatus);
        }
    }


}
