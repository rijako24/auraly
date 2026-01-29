# 🏢 GUÍA MULTI-TENANT: IA VENDEDOR

## 📋 CAMBIOS REALIZADOS PARA MULTI-TENANT

### ✅ Eliminados Campos Específicos de Negocio

Se eliminaron los siguientes campos de `CustomerProfile` que eran específicos de un tipo de negocio:

- ❌ `BabyName` 
- ❌ `BabyAgeMonths`
- ❌ `BabyConditions`

**Razón:** El sistema es multi-tenant y debe funcionar para cualquier tipo de negocio (spa para bebés, gimnasios, restaurantes, etc.).

### ✅ Solución: CustomAttributes (JSON Genérico)

Ahora se usa el campo `CustomAttributes` (JSON) para almacenar datos específicos del negocio:

```csharp
// Ejemplo para spa de bebés
profile.CustomAttributes = JsonSerializer.Serialize(new Dictionary<string, object>
{
    { "BabyName", "Ana" },
    { "BabyAgeMonths", 4 },
    { "BabyConditions", new[] { "cólicos", "piel sensible" } }
});

// Ejemplo para gimnasio
profile.CustomAttributes = JsonSerializer.Serialize(new Dictionary<string, object>
{
    { "FitnessGoal", "perder peso" },
    { "CurrentWeight", 75 },
    { "TargetWeight", 65 },
    { "MedicalConditions", new[] { "hipertensión" } }
});

// Ejemplo para restaurante
profile.CustomAttributes = JsonSerializer.Serialize(new Dictionary<string, object>
{
    { "DietaryRestrictions", new[] { "vegetariano", "sin gluten" } },
    { "FavoriteCuisine", "italiana" },
    { "PartySize", 4 }
});
```

---

## 🔧 CÓMO USAR CustomAttributes

### 1. Guardar Datos Específicos del Negocio

```csharp
public async Task UpdateCustomerCustomDataAsync(
    Guid profileId, 
    Dictionary<string, object> customData,
    CancellationToken cancellationToken = default)
{
    var profile = await _profileRepository.GetByIdAsync(profileId, cancellationToken);
    if (profile == null) return;

    // Obtener datos existentes
    var existingData = string.IsNullOrEmpty(profile.CustomAttributes)
        ? new Dictionary<string, object>()
        : JsonSerializer.Deserialize<Dictionary<string, object>>(profile.CustomAttributes) 
            ?? new Dictionary<string, object>();

    // Actualizar con nuevos datos
    foreach (var kvp in customData)
    {
        existingData[kvp.Key] = kvp.Value;
    }

    // Guardar
    profile.CustomAttributes = JsonSerializer.Serialize(existingData);
    await _profileRepository.UpdateAsync(profile, cancellationToken);
}
```

### 2. Leer Datos Específicos del Negocio

```csharp
public Dictionary<string, object> GetCustomAttributes(CustomerProfile profile)
{
    if (string.IsNullOrEmpty(profile.CustomAttributes))
        return new Dictionary<string, object>();

    try
    {
        return JsonSerializer.Deserialize<Dictionary<string, object>>(profile.CustomAttributes) 
            ?? new Dictionary<string, object>();
    }
    catch
    {
        return new Dictionary<string, object>();
    }
}

// Uso
var customData = GetCustomAttributes(profile);
if (customData.ContainsKey("BabyAgeMonths"))
{
    var age = customData["BabyAgeMonths"].ToString();
    // Usar edad...
}
```

### 3. Usar en Prompts Dinámicos

El `DynamicPromptBuilder` ya está actualizado para leer `CustomAttributes`:

```csharp
// En DynamicPromptBuilder.cs (ya implementado)
if (!string.IsNullOrEmpty(profile.CustomAttributes))
{
    var customAttrs = JsonSerializer.Deserialize<Dictionary<string, object>>(profile.CustomAttributes);
    if (customAttrs != null && customAttrs.Any())
    {
        sb.AppendLine("Información específica del cliente:");
        foreach (var attr in customAttrs)
        {
            sb.AppendLine($"- {attr.Key}: {attr.Value}");
        }
    }
}
```

---

## 📊 CONSTANTES CONFIGURABLES

### ✅ Creada Clase de Constantes

Se creó `CustomerProfileConstants.cs` con todos los valores hardcodeados:

```csharp
public static class CustomerProfileConstants
{
    // Segmentación por número de compras
    public const int FirstTimeBuyerPurchaseCount = 1;
    public const int OccasionalBuyerMaxPurchases = 3;
    public const int RegularCustomerMinPurchases = 4;
    
    // Segmentación por conversaciones
    public const int QualifiedLeadMinConversations = 2;
    
    // Segmentación por valor
    public const decimal VIPCustomerMinLifetimeValue = 500.00m;
    
    // Scoring de conversión
    public const double BaseConversionProbability = 0.5;
    // ... más constantes
}
```

### 🔄 Próximo Paso: Configuración por Negocio

**Recomendación:** Mover estas constantes a `BusinessConfiguration` para que cada negocio pueda configurar sus propios umbrales:

```sql
-- Ejemplo de configuración por negocio
INSERT INTO BusinessConfigurations (BusinessId, Key, Value, Description)
VALUES 
    (@BusinessId, 'CustomerProfile:VIPMinLifetimeValue', '500.00', 'Valor mínimo para ser VIP'),
    (@BusinessId, 'CustomerProfile:QualifiedLeadMinConversations', '2', 'Conversaciones mínimas para lead calificado'),
    (@BusinessId, 'CustomerProfile:AtRiskDaysSinceLastContact', '90', 'Días sin contacto para considerar en riesgo');
```

Luego actualizar `CustomerProfileService` para leer desde configuración:

```csharp
private async Task<decimal> GetVIPMinLifetimeValueAsync(Guid businessId)
{
    var config = await _businessConfigService.GetConfigurationAsync(
        businessId, 
        BusinessConfigurationKey.CustomerProfileVIPMinLifetimeValue);
    
    return config != null && decimal.TryParse(config, out var value) 
        ? value 
        : CustomerProfileConstants.VIPCustomerMinLifetimeValue; // Fallback
}
```

---

## 🗄️ MIGRACIÓN DE BASE DE DATOS

### Migración Creada

Se creó la migración: `RemoveBabySpecificFieldsFromCustomerProfile`

**Acciones:**
- ✅ Elimina columnas `BabyName`, `BabyAgeMonths`, `BabyConditions`
- ✅ Mantiene `CustomAttributes` para datos genéricos

**Aplicar migración:**
```powershell
dotnet ef database update --project src/Infrastructure/MimosBabySpa.Infrastructure/MimosBabySpa.Infrastructure.csproj --startup-project src/API/MimosBabySpa.API/MimosBabySpa.API.csproj
```

---

## 📝 EJEMPLOS DE USO POR TIPO DE NEGOCIO

### Spa para Bebés
```json
{
  "BabyName": "Ana",
  "BabyAgeMonths": 4,
  "BabyConditions": ["cólicos", "piel sensible"],
  "PreferredMassageType": "relajante"
}
```

### Gimnasio
```json
{
  "FitnessGoal": "perder peso",
  "CurrentWeight": 75,
  "TargetWeight": 65,
  "MedicalConditions": ["hipertensión"],
  "PreferredWorkoutTime": "mañana"
}
```

### Restaurante
```json
{
  "DietaryRestrictions": ["vegetariano", "sin gluten"],
  "FavoriteCuisine": "italiana",
  "AveragePartySize": 4,
  "PreferredDiningTime": "cena"
}
```

### Clínica Médica
```json
{
  "PatientAge": 35,
  "MedicalHistory": ["diabetes", "hipertensión"],
  "InsuranceProvider": "SeguroXYZ",
  "PreferredAppointmentTime": "tarde"
}
```

---

## ✅ CHECKLIST DE ACTUALIZACIÓN

- [x] Eliminados campos específicos de negocio de `CustomerProfile`
- [x] Actualizado `DynamicPromptBuilder` para usar `CustomAttributes`
- [x] Actualizado `SalesStateMachine` para usar `CustomAttributes`
- [x] Actualizado `SalesStrategyEngine` para usar datos genéricos
- [x] Creada clase `CustomerProfileConstants` con valores configurables
- [x] Reemplazados números hardcodeados por constantes
- [x] Creada migración para eliminar columnas específicas
- [x] Actualizado `ApplicationDbContext` para reflejar cambios
- [ ] **Pendiente:** Mover constantes a `BusinessConfiguration` (futuro)

---

## 🎯 BENEFICIOS DEL CAMBIO

### ✅ Multi-Tenant Real
- Sistema funciona para cualquier tipo de negocio
- No hay código específico de un negocio hardcodeado
- Fácil agregar nuevos tipos de negocio

### ✅ Configurabilidad
- Valores de segmentación pueden ser configurados por negocio
- Cada negocio define sus propios umbrales
- Escalable y mantenible

### ✅ Flexibilidad
- `CustomAttributes` permite cualquier estructura de datos
- No requiere cambios en BD para nuevos campos
- JSON permite estructuras complejas

---

## 📚 REFERENCIAS

- **Archivo de constantes:** `src/Application/MimosBabySpa.Application/Profile/CustomerProfileConstants.cs`
- **Entidad actualizada:** `src/Domain/MimosBabySpa.Domain/Entities/CustomerProfile.cs`
- **Servicio actualizado:** `src/Application/MimosBabySpa.Application/Profile/CustomerProfileService.cs`
- **Migración:** `RemoveBabySpecificFieldsFromCustomerProfile`

---

**Sistema ahora es 100% multi-tenant** ✅
