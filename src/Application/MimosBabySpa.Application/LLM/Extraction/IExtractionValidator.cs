using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.LLM.Extraction;

/// <summary>
/// Validador de extracciones del LLM
/// </summary>
public interface IExtractionValidator
{
    Task<ValidationResult> ValidateExtractionAsync(
        StructuredExtractionResponse extraction,
        string originalMessage,
        ConversationState currentState);
}
