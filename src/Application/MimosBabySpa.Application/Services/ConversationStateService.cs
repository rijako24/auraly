using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public class ConversationStateService : IConversationStateService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ConversationStateService> _logger;

    public ConversationStateService(
        IUnitOfWork unitOfWork,
        ILogger<ConversationStateService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<ConversationState> GetStateAsync(Guid conversationId)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
        if (conversation == null)
        {
            _logger.LogWarning("Conversación {ConversationId} no encontrada, retornando Idle", conversationId);
            return ConversationState.Idle;
        }

        return conversation.State;
    }

    public async Task SetStateAsync(Guid conversationId, ConversationState newState)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
        if (conversation == null)
        {
            _logger.LogWarning("Conversación {ConversationId} no encontrada, no se puede actualizar estado", conversationId);
            return;
        }

        var currentState = conversation.State;
        
        if (!IsValidTransition(currentState, newState))
        {
            _logger.LogWarning(
                "Transición de estado inválida: {FromState} -> {ToState} en conversación {ConversationId}",
                currentState, newState, conversationId);
            return;
        }

        conversation.State = newState;
        await _unitOfWork.Conversations.UpdateAsync(conversation);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation(
            "Estado de conversación {ConversationId} actualizado: {FromState} -> {ToState}",
            conversationId, currentState, newState);
    }

    public bool IsValidTransition(ConversationState from, ConversationState to)
    {
        // Permitir transiciones válidas según la máquina de estados
        return (from, to) switch
        {
            // Mismo estado (no-op)
            _ when from == to => true,
            
            // Desde Idle se puede ir a CollectingData
            (ConversationState.Idle, ConversationState.CollectingData) => true,
            
            // Desde CollectingData se puede ir a CheckingAvailability o volver a Idle
            (ConversationState.CollectingData, ConversationState.CheckingAvailability) => true,
            (ConversationState.CollectingData, ConversationState.Idle) => true,
            
            // Desde CheckingAvailability se puede ir a ReadyToReserve o volver a CollectingData
            (ConversationState.CheckingAvailability, ConversationState.ReadyToReserve) => true,
            (ConversationState.CheckingAvailability, ConversationState.CollectingData) => true,
            
            // Desde ReadyToReserve se puede ir a CreatingReservation
            (ConversationState.ReadyToReserve, ConversationState.CreatingReservation) => true,
            
            // Desde CreatingReservation se puede ir a WaitingForPayment o Confirmed
            (ConversationState.CreatingReservation, ConversationState.WaitingForPayment) => true,
            (ConversationState.CreatingReservation, ConversationState.Confirmed) => true,
            
            // Desde WaitingForPayment se puede ir a Confirmed
            (ConversationState.WaitingForPayment, ConversationState.Confirmed) => true,
            
            // Desde cualquier estado se puede volver a Idle (reset) - debe ir al final para no capturar casos específicos
            (_, ConversationState.Idle) => true,
            
            // Cualquier otra transición es inválida
            _ => false
        };
    }
}
