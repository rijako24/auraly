using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.FlowEngine;
using MimosBabySpa.Application.LLM.Extraction;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Application.Tools;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.Orchestration;

/// <summary>
/// Contexto unificado para el procesamiento de mensajes.
/// Centraliza estado, configuración y evaluación del flujo.
/// ✅ Refactorizado para incluir LoadedBusinessContext (evita cargas redundantes).
/// </summary>
public class ProcessingContext
{
    private readonly IFlowEngine _flowEngine;
    private readonly IConversationStateManager _stateManager;
    private readonly Guid _conversationId;
    private readonly Guid _businessId;
    private readonly string _customerPhone;

    public ConversationState State { get; private set; }
    public RequiredFieldsConfiguration RequiredFields { get; }
    public string SystemPrompt { get; }
    public LoadedBusinessContext BusinessContext { get; } // ✅ Nuevo: Contexto de negocio precargado
    public FlowEvaluationResult FlowEvaluation { get; private set; }
    public ToolExecutionContext ToolContext { get; private set; }
    public ExtractionResult? ExtractionResult { get; set; }
    /// <summary>Vista de extracción para el flujo (mapeada desde ExtractionResult).</summary>
    public ExtractionOutput? ExtractionOutput { get; set; }
    /// <summary>Acciones ejecutadas en este turno (FASE 4).</summary>
    public TurnActions TurnActions { get; set; } = new();

    /// <summary>Historial de conversación filtrado por sesión (cargado en FASE 2, reutilizado en FASE 5).</summary>
    public List<Message> ConversationHistory { get; set; } = new();

    public ProcessingContext(
        ConversationState state,
        RequiredFieldsConfiguration requiredFields,
        string systemPrompt,
        LoadedBusinessContext businessContext,
        IFlowEngine flowEngine,
        IConversationStateManager stateManager,
        Guid conversationId,
        Guid businessId,
        string customerPhone,
        string userMessage)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        RequiredFields = requiredFields ?? throw new ArgumentNullException(nameof(requiredFields));
        SystemPrompt = systemPrompt ?? throw new ArgumentNullException(nameof(systemPrompt));
        BusinessContext = businessContext ?? throw new ArgumentNullException(nameof(businessContext));
        _flowEngine = flowEngine ?? throw new ArgumentNullException(nameof(flowEngine));
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
        _conversationId = conversationId;
        _businessId = businessId;
        _customerPhone = customerPhone;

        // Evaluación inicial
        FlowEvaluation = _flowEngine.Evaluate(State, RequiredFields);

        // Contexto para tools
        ToolContext = new ToolExecutionContext
        {
            ConversationId = conversationId,
            BusinessId = businessId,
            State = State,
            RequiredFields = RequiredFields,
            UserMessage = userMessage
        };
    }

    /// <summary>
    /// Recarga el estado desde BD y re-evalúa el flujo.
    /// Usar cuando tools externos hayan modificado el estado.
    /// </summary>
    public async Task<FlowEvaluationResult> ReloadAndEvaluateAsync(
        CancellationToken cancellationToken = default)
    {
        State = await _stateManager.GetOrCreateStateAsync(
            _conversationId, _businessId, _customerPhone, cancellationToken);

        FlowEvaluation = _flowEngine.Evaluate(State, RequiredFields);
        
        // Actualizar ToolContext con estado fresco
        ToolContext = new ToolExecutionContext
        {
            ConversationId = _conversationId,
            BusinessId = _businessId,
            State = State,
            RequiredFields = RequiredFields,
            UserMessage = ToolContext.UserMessage
        };

        return FlowEvaluation;
    }

    /// <summary>
    /// Re-evalúa el flujo con el estado actual (sin recargar desde BD).
    /// Usar cuando se modifique el estado localmente.
    /// </summary>
    public FlowEvaluationResult ReEvaluate()
    {
        FlowEvaluation = _flowEngine.Evaluate(State, RequiredFields);
        return FlowEvaluation;
    }

    /// <summary>
    /// Actualiza los metadatos de mensajes del estado.
    /// </summary>
    public void UpdateMessageMetadata(string userMessage, string botResponse)
    {
        State.LastUserMessage = userMessage;
        State.LastBotMessage = botResponse;
        State.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Guarda el estado actual en BD.
    /// </summary>
    public async Task SaveStateAsync(CancellationToken cancellationToken = default)
    {
        await _stateManager.SaveStateAsync(_conversationId, State, cancellationToken);
    }
}
