namespace MimosBabySpa.Infrastructure.Configuration;

/// <summary>
/// Configuración del modelo de texto (GPT) de Azure OpenAI.
/// </summary>
public class OpenAITextModelOptions
{
    public const string SectionName = "OpenAI:TextModel";

    /// <summary>
    /// API Key del recurso Azure OpenAI para el modelo de texto.
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Endpoint del recurso Azure OpenAI (ej: https://tu-recurso.openai.azure.com/).
    /// </summary>
    public string Endpoint { get; set; } = "";

    /// <summary>
    /// Nombre del deployment del modelo (ej: gpt-4.1-mini).
    /// </summary>
    public string DeploymentName { get; set; } = "gpt-4.1-mini";
}

/// <summary>
/// Configuración del modelo de audio (Whisper) de Azure OpenAI.
/// Puede estar en un recurso diferente al de texto.
/// </summary>
public class OpenAIAudioModelOptions
{
    public const string SectionName = "OpenAI:AudioModel";

    /// <summary>
    /// API Key del recurso Azure OpenAI para Whisper.
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Endpoint del recurso Azure OpenAI para Whisper (ej: https://recurso-whisper.openai.azure.com/).
    /// </summary>
    public string Endpoint { get; set; } = "";

    /// <summary>
    /// Nombre del deployment de Whisper (ej: whisper-1).
    /// </summary>
    public string DeploymentName { get; set; } = "whisper";
}
