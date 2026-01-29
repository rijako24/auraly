namespace MimosBabySpa.Application.Services;

/// <summary>
/// Servicio de localización para traducción de textos.
/// TODO: Expandir con soporte completo i18n en el futuro.
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Obtiene el nombre de un día de la semana en el idioma especificado.
    /// </summary>
    /// <param name="dayKey">Clave del día (ej: "monday", "tuesday")</param>
    /// <param name="language">Código de idioma ("es", "en", etc.)</param>
    /// <returns>Nombre del día traducido</returns>
    string GetDayName(string dayKey, string language = "es");

    /// <summary>
    /// Obtiene un mensaje de error en el idioma especificado.
    /// </summary>
    /// <param name="errorKey">Clave del error (ej: "TechnicalDifficulty", "ExtractionFailed")</param>
    /// <param name="language">Código de idioma</param>
    /// <returns>Mensaje de error traducido</returns>
    string GetErrorMessage(string errorKey, string language = "es");

    /// <summary>
    /// Obtiene una frase común del sistema.
    /// </summary>
    /// <param name="phraseKey">Clave de la frase (ej: "Greeting", "ThankYou")</param>
    /// <param name="language">Código de idioma</param>
    /// <returns>Frase traducida</returns>
    string GetCommonPhrase(string phraseKey, string language = "es");

    /// <summary>
    /// Obtiene el idioma actual del servicio.
    /// </summary>
    string GetCurrentLanguage();

    /// <summary>
    /// Establece el idioma actual del servicio.
    /// </summary>
    void SetLanguage(string language);
}
