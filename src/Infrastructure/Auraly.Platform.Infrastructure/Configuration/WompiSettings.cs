namespace Auraly.Platform.Infrastructure.Configuration;

/// <summary>
/// Configuración para la integración con Wompi (links de pago).
/// Si PrivateKey está vacío, no se generan links de pago.
/// </summary>
public class WompiSettings
{
    public const string SectionName = "Wompi";
    public string Mode { get; set; } = "test";

    /// <summary>
    /// Clave privada de Wompi (prv_test_xxx para sandbox, prv_prod_xxx para producción).
    /// Vacío = pagos deshabilitados (no se genera link).
    /// </summary>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>
    /// Clave pública de Wompi (pub_test_xxx para sandbox).
    /// </summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// Secreto de eventos para validar firma del webhook.
    /// </summary>
    public string EventsSecret { get; set; } = string.Empty;

    /// <summary>
    /// Secreto de integridad para validación adicional.
    /// </summary>
    public string IntegritySecret { get; set; } = string.Empty;

    /// <summary>
    /// true = sandbox, false = producción.
    /// </summary>
    /// <summary>
    /// URL base de la API en sandbox. Por defecto: https://sandbox.wompi.co/v1
    /// </summary>
    public string SandboxBaseUrl { get; set; } = "https://sandbox.wompi.co/v1";

    /// <summary>
    /// URL base de la API en producción. Por defecto: https://production.wompi.co/v1
    /// </summary>
    public string ProductionBaseUrl { get; set; } = "https://production.wompi.co/v1";

    /// <summary>
    /// URL base de la API. Si se especifica, tiene prioridad sobre Mode y las URLs por defecto.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Timeout en segundos para las llamadas HTTP a la API de Wompi.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// URL base del checkout (se concatena el id del payment link).
    /// Ej: https://checkout.wompi.co/l/
    /// </summary>
    public string CheckoutBaseUrl { get; set; } = "https://checkout.wompi.co/l/";

    internal string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            return BaseUrl.TrimEnd('/');
        var isTest = !string.Equals(Mode, "production", StringComparison.OrdinalIgnoreCase);
        var url = isTest ? SandboxBaseUrl : ProductionBaseUrl;
        if (string.IsNullOrWhiteSpace(url))
            return isTest ? "https://sandbox.wompi.co/v1" : "https://production.wompi.co/v1";
        return url.TrimEnd('/');
    }
}
