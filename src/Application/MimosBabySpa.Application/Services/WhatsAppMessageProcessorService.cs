using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Application.Services;

public class WhatsAppMessageProcessorService : IWhatsAppMessageProcessorService
{
    private readonly IConversationService _conversationService;
    private readonly IMessageService _messageService;
    private readonly ILeadService _leadService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IAIService _aiService;
    private readonly IBlobStorageService _blobStorageService;
    private readonly IConversationContextService _contextService;
    private readonly ILogger<WhatsAppMessageProcessorService> _logger;

    public WhatsAppMessageProcessorService(
        IConversationService conversationService,
        IMessageService messageService,
        ILeadService leadService,
        IWhatsAppService whatsAppService,
        IAIService aiService,
        IBlobStorageService blobStorageService,
        IConversationContextService contextService,
        ILogger<WhatsAppMessageProcessorService> logger)
    {
        _conversationService = conversationService;
        _messageService = messageService;
        _leadService = leadService;
        _whatsAppService = whatsAppService;
        _aiService = aiService;
        _blobStorageService = blobStorageService;
        _contextService = contextService;
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

            // 3. Clasificar intención y extraer contexto en una sola llamada a la IA
            var intentAndContext = await _messageService.ClassifyIntentAndExtractContextAsync(businessId, messageText, conversation);
            var intent = intentAndContext.Intent;

            // 3.1. Guardar contexto extraído por la IA
            await SaveContextBatchAsync(conversation.ConversationId, intentAndContext.Context, customerName);

            // 4. Guardar mensaje del usuario
            await _messageService.SaveMessageAsync(conversation.ConversationId, "User", messageText, intent);

            // 5. Manejar transferencia a humano
            if (intent.Equals("TalkToHuman", StringComparison.OrdinalIgnoreCase))
            {
                await HandleHumanTransferAsync(userNumber, conversation);
                return;
            }

            // 6. Generar respuesta con IA
            var aiResponse = await _aiService.GenerateResponseAsync(businessId, messageText, conversation, intent, lead);

            // 7. Guardar respuesta del bot
            await _messageService.SaveMessageAsync(conversation.ConversationId, "Bot", aiResponse, intent);

            // 8. Actualizar contexto de conversación
            await UpdateConversationContextAsync(conversation, messageText, intent, lead);

            // 9. Enviar respuesta
            await SendResponseAsync(userNumber, aiResponse, intent, conversation);

            // 10. Actualizar estado del lead
            await UpdateLeadStatusAsync(lead, intent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando mensaje de {UserNumber}", userNumber);
            await _whatsAppService.SendTextMessageAsync(userNumber, 
                "Disculpa, estoy teniendo dificultades técnicas. Por favor intenta de nuevo en un momento.");
        }
    }

    private async Task HandleHumanTransferAsync(string userNumber, Conversation conversation)
    {
        var transferMessage = "Perfecto, voy a transferirte con uno de nuestros asesores. " +
                              "Te contactarán en breve. ¡Gracias por confiar en Mimos Baby Spa! 👶✨";
        
        await _whatsAppService.SendTextMessageAsync(userNumber, transferMessage);
        
        // Aquí podrías integrar con un sistema de tickets o notificaciones
        _logger.LogInformation("Transferencia a humano solicitada por {UserNumber}", userNumber);
    }

    private async Task UpdateConversationContextAsync(Conversation conversation, string messageText, string intent, Lead lead)
    {
        // El contexto ya fue actualizado cuando se clasificó la intención
        // Solo actualizar LastMessage y LastIntent en Conversation (para compatibilidad)
        await _conversationService.UpdateConversationContextAsync(
            conversation.ConversationId,
            messageText,
            intent
        );
    }

    private async Task SendResponseAsync(string userNumber, string response, string intent, Conversation conversation)
    {
        // Si es AskPrice o ReservationRequest, intentar obtener el plan del contexto
        if (intent.Equals("AskPrice", StringComparison.OrdinalIgnoreCase) ||
            intent.Equals("ReservationRequest", StringComparison.OrdinalIgnoreCase))
        {
            // Buscar el plan recomendado en los contextos (buscar string que contenga "plan recomendado")
            var allContext = await _contextService.GetAllContextAsync(conversation.ConversationId);
            var planContext = allContext.FirstOrDefault(c => 
                c.Contains("plan recomendado", StringComparison.OrdinalIgnoreCase) || 
                c.Contains("plan", StringComparison.OrdinalIgnoreCase));
            
            if (!string.IsNullOrEmpty(planContext))
            {
                // Extraer el nombre del plan del string (ej: "El plan recomendado es Plan Marineritos")
                var planName = ExtractPlanNameFromContext(planContext);
                
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
            }
        }

        // Enviar solo texto
        await _whatsAppService.SendTextMessageAsync(userNumber, response);
    }

    private string? ExtractPlanNameFromContext(string contextString)
    {
        // Buscar patrones como "Plan Marineritos", "Plan Aventuras Marinas", etc.
        var planMatch = System.Text.RegularExpressions.Regex.Match(
            contextString, 
            @"Plan\s+([A-Za-záéíóúñÁÉÍÓÚÑ\s]+)", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (planMatch.Success && planMatch.Groups.Count > 1)
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

    private async Task UpdateLeadStatusAsync(Lead lead, string intent)
    {
        var newStatus = intent switch
        {
            "ReservationRequest" => "Closed",
            "AskPrice" or "FollowUp" => "Contacted",
            _ => lead.Status
        };

        if (newStatus != lead.Status)
        {
            await _leadService.UpdateLeadAsync(lead.LeadId, status: newStatus);
        }
    }

    private async Task SaveContextBatchAsync(Guid conversationId, List<string> aiContexts, string? customerName)
    {
        var contextsToSave = new List<string>();

        // Agregar todos los contextos de la IA (filtrando vacíos)
        if (aiContexts.Any())
        {
            contextsToSave.AddRange(aiContexts.Where(c => !string.IsNullOrWhiteSpace(c)));
        }

        // Agregar CustomerName si está disponible
        if (!string.IsNullOrEmpty(customerName))
        {
            var customerNameContext = $"El cliente se llama {customerName}";
            // Verificar que no esté ya en la lista antes de agregar
            if (!contextsToSave.Any(c => 
                c.Equals(customerNameContext, StringComparison.OrdinalIgnoreCase)))
            {
                contextsToSave.Add(customerNameContext);
            }
        }

        // Guardar todos los contextos en batch (validación de duplicados incluida)
        if (contextsToSave.Any())
        {
            await _contextService.AddContextBatchAsync(conversationId, contextsToSave);
        }
    }
}
