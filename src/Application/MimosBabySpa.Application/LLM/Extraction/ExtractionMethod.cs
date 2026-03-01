namespace MimosBabySpa.Application.LLM.Extraction;

public enum ExtractionMethod
{
    /// <summary>Extracción exitosa por LLM con validación aprobada.</summary>
    LLM,

    /// <summary>Cualquier fallo — sin datos confiables. Solo intenciones críticas (regex) se preservan.</summary>
    Degraded
}
