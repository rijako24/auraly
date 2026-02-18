namespace MimosBabySpa.Application.LLM.Extraction;

/// <summary>
/// Constantes compartidas para el pipeline de extracción.
/// ÚNICA fuente de verdad para umbrales: usados por prompt, validator y orquestador.
/// </summary>
public static class ExtractionConstants
{
    /// <summary>
    /// Confidence mínima para que un campo se considere válido y se aplique al estado.
    /// Por debajo de este valor el LLM debe incluir el campo en ambiguities, no en extracted_fields.
    /// Referenciado por: prompt de extracción, ExtractionValidator, HybridTransactionalOrchestrator.
    /// </summary>
    public const double MinConfidence = 0.6;

    /// <summary>
    /// Confidence mínima del resultado de validación para aceptar la extracción LLM sin fallback.
    /// </summary>
    public const double MinValidationConfidence = 0.6;

    /// <summary>
    /// Máximo número de palabras que puede tener un valor estructurado.
    /// Más palabras → probablemente es una frase, no un dato.
    /// </summary>
    public const int MaxValueWordCount = 7;
}
