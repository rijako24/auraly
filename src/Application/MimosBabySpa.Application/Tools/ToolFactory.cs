using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Application.Tools;

/// <summary>
/// Implementación de la fábrica de herramientas.
/// Centraliza la creación de tool handlers usando el patrón Factory.
/// </summary>
public class ToolFactory : IToolFactory
{
    private readonly UpdateConversationStateToolHandler _updateStateHandler;
    private readonly CheckAvailabilityToolHandler _checkAvailabilityHandler;
    private readonly CreateReservationToolHandler _createReservationHandler;
    private readonly ILogger<ToolFactory> _logger;

    public ToolFactory(
        UpdateConversationStateToolHandler updateStateHandler,
        CheckAvailabilityToolHandler checkAvailabilityHandler,
        CreateReservationToolHandler createReservationHandler,
        ILogger<ToolFactory> logger)
    {
        _updateStateHandler = updateStateHandler ?? throw new ArgumentNullException(nameof(updateStateHandler));
        _checkAvailabilityHandler = checkAvailabilityHandler ?? throw new ArgumentNullException(nameof(checkAvailabilityHandler));
        _createReservationHandler = createReservationHandler ?? throw new ArgumentNullException(nameof(createReservationHandler));
        _logger = logger;
    }

    public IToolHandler GetTool(ToolType toolType)
    {
        return toolType switch
        {
            ToolType.UpdateConversationState => _updateStateHandler,
            ToolType.CheckAvailability => _checkAvailabilityHandler,
            ToolType.CreateReservation => _createReservationHandler,
            _ => throw new ArgumentException($"Tool type no soportado: {toolType}", nameof(toolType))
        };
    }
}
