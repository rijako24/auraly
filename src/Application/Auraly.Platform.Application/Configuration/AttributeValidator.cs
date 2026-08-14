using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Auraly.Platform.Application.Configuration;

/// <summary>
/// Validador genérico de atributos basado en AttributeDefinition.
/// Valida valores según el tipo y reglas definidas en la configuración.
/// NO contiene lógica hardcodeada de negocio específico.
/// </summary>
public class AttributeValidator
{
    private readonly ILogger<AttributeValidator> _logger;

    public AttributeValidator(ILogger<AttributeValidator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Valida un valor contra su definición de atributo
    /// </summary>
    public ValidationResult Validate(
        AttributeDefinition definition,
        string value)
    {
        var result = new ValidationResult { IsValid = true };

        if (definition == null)
        {
            result.IsValid = false;
            result.ErrorMessage = "Definición de atributo no encontrada";
            return result;
        }

        // Validar requerido
        if (definition.IsRequired && string.IsNullOrWhiteSpace(value))
        {
            result.IsValid = false;
            result.ErrorMessage = $"{definition.DisplayName} es requerido";
            return result;
        }

        // Si no es requerido y está vacío, es válido
        if (!definition.IsRequired && string.IsNullOrWhiteSpace(value))
        {
            return result;
        }

        // Validar por tipo
        switch (definition.Type)
        {
            case AttributeType.Number:
                result = ValidateNumber(definition, value);
                break;

            case AttributeType.Date:
                result = ValidateDate(definition, value);
                break;

            case AttributeType.Time:
                result = ValidateTime(definition, value);
                break;

            case AttributeType.Email:
                result = ValidateEmail(definition, value);
                break;

            case AttributeType.Phone:
                result = ValidatePhone(definition, value);
                break;

            case AttributeType.Select:
                result = ValidateSelect(definition, value);
                break;

            case AttributeType.Boolean:
                result = ValidateBoolean(definition, value);
                break;

            case AttributeType.Text:
            default:
                result = ValidateText(definition, value);
                break;
        }

        // Validar patrón regex si existe
        if (result.IsValid && !string.IsNullOrWhiteSpace(definition.ValidationPattern))
        {
            try
            {
                var regex = new Regex(definition.ValidationPattern);
                if (!regex.IsMatch(value))
                {
                    result.IsValid = false;
                    result.ErrorMessage = $"{definition.DisplayName} no tiene el formato correcto";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al validar patrón regex para {Attribute}", definition.Name);
            }
        }

        return result;
    }

    private ValidationResult ValidateNumber(AttributeDefinition definition, string value)
    {
        var result = new ValidationResult { IsValid = true };

        if (!double.TryParse(value, out var number))
        {
            result.IsValid = false;
            result.ErrorMessage = $"{definition.DisplayName} debe ser un número válido";
            return result;
        }

        // Validaciones opcionales desde metadata
        if (definition.Metadata != null)
        {
            if (definition.Metadata.TryGetValue("min", out var minStr) &&
                double.TryParse(minStr, out var min) &&
                number < min)
            {
                result.IsValid = false;
                result.ErrorMessage = $"{definition.DisplayName} debe ser mayor o igual a {min}";
                return result;
            }

            if (definition.Metadata.TryGetValue("max", out var maxStr) &&
                double.TryParse(maxStr, out var max) &&
                number > max)
            {
                result.IsValid = false;
                result.ErrorMessage = $"{definition.DisplayName} debe ser menor o igual a {max}";
                return result;
            }
        }

        return result;
    }

    private ValidationResult ValidateDate(AttributeDefinition definition, string value)
    {
        var result = new ValidationResult { IsValid = true };

        if (!DateOnly.TryParse(value, out _))
        {
            result.IsValid = false;
            result.ErrorMessage = $"{definition.DisplayName} debe ser una fecha válida (formato: YYYY-MM-DD)";
        }

        return result;
    }

    private ValidationResult ValidateTime(AttributeDefinition definition, string value)
    {
        var result = new ValidationResult { IsValid = true };

        if (!TimeOnly.TryParse(value, out _))
        {
            result.IsValid = false;
            result.ErrorMessage = $"{definition.DisplayName} debe ser una hora válida (formato: HH:MM)";
        }

        return result;
    }

    private ValidationResult ValidateEmail(AttributeDefinition definition, string value)
    {
        var result = new ValidationResult { IsValid = true };

        try
        {
            var addr = new System.Net.Mail.MailAddress(value);
            if (addr.Address != value)
            {
                result.IsValid = false;
                result.ErrorMessage = $"{definition.DisplayName} no es un email válido";
            }
        }
        catch
        {
            result.IsValid = false;
            result.ErrorMessage = $"{definition.DisplayName} no es un email válido";
        }

        return result;
    }

    private ValidationResult ValidatePhone(AttributeDefinition definition, string value)
    {
        var result = new ValidationResult { IsValid = true };

        // Validación básica: solo números, +, -, (, ), espacios
        if (!Regex.IsMatch(value, @"^[\d\s\+\-\(\)]+$"))
        {
            result.IsValid = false;
            result.ErrorMessage = $"{definition.DisplayName} no es un teléfono válido";
        }

        return result;
    }

    private ValidationResult ValidateSelect(AttributeDefinition definition, string value)
    {
        var result = new ValidationResult { IsValid = true };

        if (definition.AllowedValues == null || !definition.AllowedValues.Any())
        {
            _logger.LogWarning(
                "AttributeDefinition de tipo Select no tiene AllowedValues configurados: {Name}",
                definition.Name);
            return result;
        }

        if (!definition.AllowedValues.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            result.IsValid = false;
            result.ErrorMessage = $"{definition.DisplayName} debe ser uno de: {string.Join(", ", definition.AllowedValues)}";
        }

        return result;
    }

    private ValidationResult ValidateBoolean(AttributeDefinition definition, string value)
    {
        var result = new ValidationResult { IsValid = true };

        var normalizedValue = value.ToLowerInvariant();
        if (normalizedValue != "true" && normalizedValue != "false" &&
            normalizedValue != "1" && normalizedValue != "0" &&
            normalizedValue != "yes" && normalizedValue != "no" &&
            normalizedValue != "sí" && normalizedValue != "si")
        {
            result.IsValid = false;
            result.ErrorMessage = $"{definition.DisplayName} debe ser verdadero o falso";
        }

        return result;
    }

    private ValidationResult ValidateText(AttributeDefinition definition, string value)
    {
        var result = new ValidationResult { IsValid = true };

        // Validaciones opcionales desde metadata
        if (definition.Metadata != null)
        {
            if (definition.Metadata.TryGetValue("minLength", out var minLengthStr) &&
                int.TryParse(minLengthStr, out var minLength) &&
                value.Length < minLength)
            {
                result.IsValid = false;
                result.ErrorMessage = $"{definition.DisplayName} debe tener al menos {minLength} caracteres";
                return result;
            }

            if (definition.Metadata.TryGetValue("maxLength", out var maxLengthStr) &&
                int.TryParse(maxLengthStr, out var maxLength) &&
                value.Length > maxLength)
            {
                result.IsValid = false;
                result.ErrorMessage = $"{definition.DisplayName} debe tener máximo {maxLength} caracteres";
                return result;
            }
        }

        return result;
    }
}
