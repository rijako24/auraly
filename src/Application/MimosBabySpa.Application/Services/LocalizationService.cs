using MimosBabySpa.Application.Constants;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Implementación básica del servicio de localización.
/// Usa LocalizationConstants como fuente de traducciones.
/// TODO: En el futuro, migrar a un sistema i18n completo con archivos .resx o JSON.
/// </summary>
public class LocalizationService : ILocalizationService
{
    private string _currentLanguage = "es"; // Por defecto español

    public string GetCurrentLanguage() => _currentLanguage;

    public void SetLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return;

        _currentLanguage = language.ToLower();
    }

    public string GetDayName(string dayKey, string language = "es")
    {
        return LocalizationConstants.DayNames.Get(dayKey, language);
    }

    public string GetErrorMessage(string errorKey, string language = "es")
    {
        // Por ahora, solo español
        // TODO: Implementar diccionario multiidioma cuando sea necesario
        return errorKey switch
        {
            "TechnicalDifficulty" => LocalizationConstants.ErrorMessages.TechnicalDifficulty,
            "ExtractionFailed" => LocalizationConstants.ErrorMessages.ExtractionFailed,
            "AIServiceUnavailable" => LocalizationConstants.ErrorMessages.AIServiceUnavailable,
            "ValidationError" => LocalizationConstants.ErrorMessages.ValidationError,
            _ => $"Error: {errorKey}"
        };
    }

    public string GetCommonPhrase(string phraseKey, string language = "es")
    {
        // Por ahora, solo español
        // TODO: Implementar diccionario multiidioma cuando sea necesario
        return phraseKey switch
        {
            "Greeting" => LocalizationConstants.CommonPhrases.Greeting,
            "ThankYou" => LocalizationConstants.CommonPhrases.ThankYou,
            "YoureWelcome" => LocalizationConstants.CommonPhrases.YoureWelcome,
            "Goodbye" => LocalizationConstants.CommonPhrases.Goodbye,
            "PleaseWait" => LocalizationConstants.CommonPhrases.PleaseWait,
            _ => phraseKey
        };
    }
}
