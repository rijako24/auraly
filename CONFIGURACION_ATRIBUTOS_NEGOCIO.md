# Configuración de Atributos de Negocio

## ⚠️ Principio Fundamental

**NUNCA hardcodear atributos específicos de negocio en el código.**

Todos los atributos deben configurarse dinámicamente en la base de datos usando la tabla `BusinessConfiguration` con la clave `EntityExtractionFields`.

## 📋 Estructura de Configuración

Los atributos se almacenan como JSON en la base de datos:

```sql
-- Tabla: BusinessConfiguration
-- Key: EntityExtractionFields
-- Value: JSON con definición de atributos
```

## 🏗️ Formato JSON de Atributos

### Estructura Base

```json
{
  "AttributeName": {
    "Name": "AttributeName",
    "DisplayName": "Nombre para mostrar",
    "Description": "Descripción del atributo",
    "Type": "Number|Text|Date|Time|Email|Phone|Select|Boolean",
    "IsRequired": false,
    "ValidationPattern": "regex opcional",
    "DefaultValue": "valor por defecto opcional",
    "AllowedValues": ["valor1", "valor2"],
    "Metadata": {
      "min": "0",
      "max": "120",
      "minLength": "1",
      "maxLength": "100"
    }
  }
}
```

## 📝 Ejemplos por Tipo de Negocio

### Baby Spa

```json
{
  "BabyAge": {
    "Name": "BabyAge",
    "DisplayName": "Edad del bebé",
    "Description": "Edad del bebé en meses",
    "Type": "Number",
    "IsRequired": false,
    "ValidationPattern": "^\\d{1,3}$",
    "Metadata": {
      "min": "0",
      "max": "120"
    }
  },
  "BabyName": {
    "Name": "BabyName",
    "DisplayName": "Nombre del bebé",
    "Description": "Nombre del bebé",
    "Type": "Text",
    "IsRequired": false,
    "Metadata": {
      "minLength": "2",
      "maxLength": "50"
    }
  },
  "SpecialConditions": {
    "Name": "SpecialConditions",
    "DisplayName": "Condiciones especiales",
    "Description": "Condiciones médicas o especiales del bebé",
    "Type": "Text",
    "IsRequired": false,
    "Metadata": {
      "maxLength": "500"
    }
  },
  "PreviousVisits": {
    "Name": "PreviousVisits",
    "DisplayName": "¿Ha visitado antes?",
    "Description": "Indica si el bebé ya visitó el spa",
    "Type": "Boolean",
    "IsRequired": false
  }
}
```

### Restaurant

```json
{
  "PartySize": {
    "Name": "PartySize",
    "DisplayName": "Número de personas",
    "Description": "Cantidad de personas en la reserva",
    "Type": "Number",
    "IsRequired": true,
    "ValidationPattern": "^[1-9][0-9]?$",
    "Metadata": {
      "min": "1",
      "max": "50"
    }
  },
  "DietaryRestrictions": {
    "Name": "DietaryRestrictions",
    "DisplayName": "Restricciones dietéticas",
    "Description": "Alergias o restricciones alimentarias",
    "Type": "Text",
    "IsRequired": false,
    "Metadata": {
      "maxLength": "300"
    }
  },
  "SpecialOccasion": {
    "Name": "SpecialOccasion",
    "DisplayName": "Ocasión especial",
    "Description": "Cumpleaños, aniversario, etc.",
    "Type": "Select",
    "IsRequired": false,
    "AllowedValues": [
      "Cumpleaños",
      "Aniversario",
      "Cita romántica",
      "Negocios",
      "Otro",
      "Ninguna"
    ]
  },
  "PreferredSeating": {
    "Name": "PreferredSeating",
    "DisplayName": "Preferencia de asiento",
    "Description": "Preferencia de ubicación en el restaurante",
    "Type": "Select",
    "IsRequired": false,
    "AllowedValues": [
      "Ventana",
      "Interior",
      "Terraza",
      "Privado",
      "Sin preferencia"
    ]
  }
}
```

### Medical Clinic

```json
{
  "Symptoms": {
    "Name": "Symptoms",
    "DisplayName": "Síntomas",
    "Description": "Descripción de síntomas del paciente",
    "Type": "Text",
    "IsRequired": true,
    "Metadata": {
      "minLength": "10",
      "maxLength": "500"
    }
  },
  "Insurance": {
    "Name": "Insurance",
    "DisplayName": "Seguro médico",
    "Description": "Nombre del seguro médico",
    "Type": "Text",
    "IsRequired": false,
    "Metadata": {
      "maxLength": "100"
    }
  },
  "IsFirstVisit": {
    "Name": "IsFirstVisit",
    "DisplayName": "¿Primera visita?",
    "Description": "Indica si es la primera vez que visita la clínica",
    "Type": "Boolean",
    "IsRequired": false
  },
  "PreferredLanguage": {
    "Name": "PreferredLanguage",
    "DisplayName": "Idioma preferido",
    "Description": "Idioma de preferencia para la consulta",
    "Type": "Select",
    "IsRequired": false,
    "AllowedValues": [
      "Español",
      "English",
      "Português"
    ]
  }
}
```

## 🔧 Cómo Configurar en la Base de Datos

### Opción 1: Script SQL

```sql
-- Insertar configuración de atributos para un negocio
INSERT INTO BusinessConfiguration (
    ConfigurationId,
    BusinessId,
    ConfigurationKey,
    ConfigurationValue,
    CreatedAt,
    UpdatedAt
)
VALUES (
    NEWID(),
    'TU-BUSINESS-ID-AQUI',
    'EntityExtractionFields',
    '{
      "BabyAge": {
        "Name": "BabyAge",
        "DisplayName": "Edad del bebé",
        "Description": "Edad del bebé en meses",
        "Type": "Number",
        "IsRequired": false,
        "ValidationPattern": "^\\\\d{1,3}$",
        "Metadata": {
          "min": "0",
          "max": "120"
        }
      },
      "BabyName": {
        "Name": "BabyName",
        "DisplayName": "Nombre del bebé",
        "Description": "Nombre del bebé",
        "Type": "Text",
        "IsRequired": false
      }
    }',
    GETUTCDATE(),
    GETUTCDATE()
);
```

### Opción 2: API/Service

```csharp
public async Task ConfigureBusinessAttributesAsync(
    Guid businessId,
    Dictionary<string, AttributeDefinition> attributes)
{
    var json = System.Text.Json.JsonSerializer.Serialize(
        attributes,
        new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

    await _businessConfigService.SetConfigurationValueAsync(
        businessId,
        BusinessConfigurationKey.EntityExtractionFields,
        json);
}
```

## ✅ Validación Automática

El sistema valida automáticamente los atributos según su definición:

```csharp
// El AttributeValidator valida según la definición
var validator = new AttributeValidator(logger);
var result = validator.Validate(attributeDefinition, value);

if (!result.IsValid)
{
    // Error: result.ErrorMessage
}
```

### Validaciones por Tipo

| Tipo | Validaciones |
|------|--------------|
| **Number** | Debe ser número válido, respeta min/max en Metadata |
| **Text** | Respeta minLength/maxLength en Metadata |
| **Date** | Formato YYYY-MM-DD |
| **Time** | Formato HH:MM |
| **Email** | Formato de email válido |
| **Phone** | Solo números, +, -, (, ), espacios |
| **Select** | Debe estar en AllowedValues |
| **Boolean** | true/false, 1/0, yes/no, sí/si |

### ValidationPattern (Regex)

Todos los tipos pueden tener un `ValidationPattern` adicional:

```json
{
  "PostalCode": {
    "Name": "PostalCode",
    "Type": "Text",
    "ValidationPattern": "^\\d{5}$"
  }
}
```

## 📖 Uso en el System Prompt

Los atributos se incluyen automáticamente en el system prompt:

```
# ATRIBUTOS ADICIONALES
- Edad del bebé (BabyAge): Edad del bebé en meses
- Nombre del bebé (BabyName): Nombre del bebé
- Condiciones especiales (SpecialConditions): Condiciones médicas o especiales

Para actualizar estos atributos, usa:
update_conversation_state("Attribute:BabyAge", "6")
update_conversation_state("Attribute:BabyName", "Lucas")
```

## 🚫 Lo que NO Debes Hacer

```csharp
// ❌ MAL - Hardcoding en código
if (state.HasAttribute("BabyAge"))
{
    var age = int.Parse(state.GetAttribute("BabyAge"));
    if (age < 0 || age > 120)
    {
        // Validación hardcodeada
    }
}

// ✅ BIEN - Validación genérica basada en configuración
var definition = await _configProvider.GetBusinessAttributesAsync(businessId);
if (definition.TryGetValue(attributeName, out var def))
{
    var result = _validator.Validate(def, value);
    if (!result.IsValid)
    {
        // Usar result.ErrorMessage genérico
    }
}
```

## 🔄 Migración de Atributos Hardcodeados

Si ya tienes código con atributos hardcodeados:

1. **Identificar** todos los campos específicos de negocio
2. **Extraer** a configuración JSON
3. **Insertar** en BusinessConfiguration
4. **Eliminar** código hardcodeado
5. **Testear** con el nuevo flujo genérico

## 💡 Tips

1. **Empieza simple:** Solo Name, DisplayName, Type, IsRequired
2. **Agrega validaciones gradualmente:** ValidationPattern, Metadata
3. **Documenta bien:** Description ayuda al LLM y a los desarrolladores
4. **Testa cada atributo:** Verifica que la validación funcione
5. **Sin miedo a JSON largo:** Es mejor configuración que código

## 🎯 Beneficios

✅ **Extensibilidad:** Agregar atributos sin desplegar código  
✅ **Multi-tenant:** Cada negocio tiene sus propios atributos  
✅ **Mantenibilidad:** Cambiar validaciones sin tocar código  
✅ **Auditoría:** Cambios de configuración registrados en BD  
✅ **Testing:** Más fácil probar con diferentes configuraciones  

---

**Recuerda:** El código NUNCA debe conocer "BabyAge", "PartySize" o cualquier campo específico. Solo debe trabajar con `state.Attributes[key]` de forma genérica.
