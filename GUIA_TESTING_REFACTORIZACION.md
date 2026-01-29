# 🧪 Guía de Testing Manual - Refactorización de Carga de Configuración

**Objetivo**: Verificar que la refactorización funciona correctamente y que la carga de configuración se ha optimizado.

---

## 📋 Pre-requisitos

Antes de empezar, verifica que tienes:

- ✅ Base de datos SQL configurada y con migraciones aplicadas
- ✅ Azure OpenAI configurado con deployment activo
- ✅ WhatsApp Business API configurado (opcional para testing básico)
- ✅ `local.settings.json` con todas las configuraciones

---

## 🚀 Paso 1: Preparar el Entorno

### 1.1 Verificar Configuración

```powershell
# Navegar al proyecto API
cd c:\Users\RichardJacome\MimosBabySpa\src\API\MimosBabySpa.API

# Verificar que local.settings.json existe
Get-Content local.settings.json | Select-String "OpenAI|WhatsApp|ConnectionStrings"
```

### 1.2 Compilar el Proyecto

```powershell
# Desde la raíz del proyecto
cd c:\Users\RichardJacome\MimosBabySpa

# Limpiar y compilar
dotnet clean
dotnet build

# Debe mostrar: "Compilación correcta"
```

### 1.3 Verificar Base de Datos

```powershell
# Verificar que las tablas existen
# Ejecutar desde SQL Server Management Studio o Azure Data Studio:

SELECT TABLE_NAME 
FROM INFORMATION_SCHEMA.TABLES 
WHERE TABLE_NAME IN (
    'Businesses',
    'Services', 
    'BusinessConfigurations',
    'ConversationStates'
)
ORDER BY TABLE_NAME;

# Debe mostrar las 4 tablas
```

---

## 🔍 Paso 2: Testing de Carga Única (Sin Caché)

### Objetivo
Verificar que `EntityExtractionConfig` se carga **1 sola vez** (no 3 veces como antes).

### 2.1 Habilitar Logging Detallado

Editar `src\API\MimosBabySpa.API\local.settings.json`:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "Logging__LogLevel__Default": "Debug",
    "Logging__LogLevel__MimosBabySpa.Application.Configuration": "Debug"
  }
}
```

### 2.2 Ejecutar la Aplicación

```powershell
cd c:\Users\RichardJacome\MimosBabySpa\src\API\MimosBabySpa.API

# Limpiar caché de functions
Remove-Item -Recurse -Force .\.azure\functions\* -ErrorAction SilentlyContinue

# Iniciar Azure Functions
func start --verbose
```

**Deberías ver**:
```
Azure Functions Core Tools
...
Functions:
    WhatsAppWebhook: [POST] http://localhost:7071/api/WhatsAppWebhook
```

### 2.3 Escenario de Prueba 1: Primera Carga

Abrir una **nueva terminal** y ejecutar:

```powershell
# Script de prueba simple
$body = @{
    entry = @(
        @{
            changes = @(
                @{
                    value = @{
                        messages = @(
                            @{
                                from = "1234567890"
                                text = @{
                                    body = "Hola, quiero información sobre sus servicios"
                                }
                                type = "text"
                            }
                        )
                    }
                }
            )
        }
    )
} | ConvertTo-Json -Depth 10

Invoke-RestMethod -Uri "http://localhost:7071/api/WhatsAppWebhook" `
    -Method POST `
    -Body $body `
    -ContentType "application/json"
```

### 2.4 Verificar Logs (IMPORTANTE)

En la terminal donde corre `func start`, busca estos logs:

#### ✅ Logs Esperados (CORRECTO):

```
[DEBUG] FASE 1: Cargando contexto...
[DEBUG] ⚠️ BusinessContext no en caché, cargando desde BD: BusinessId={guid}
[DEBUG] Cargando configuración completa para BusinessId={guid}...
[INFO]  ✅ Configuración cargada para BusinessId={guid} en 45ms: Services=3, Attributes=5
[INFO]  💾 BusinessContext cargado y guardado en caché: BusinessId={guid}, Expira en 30 minutos
[INFO]  ✅ Contexto cargado en 52ms: Version=1, Completitud=15%
```

**Puntos clave**:
- ✅ Solo 1 línea de "Cargando configuración completa"
- ✅ Ver "guardado en caché"
- ✅ Tiempo total ~50ms

#### ❌ Logs Antiguos (INCORRECTO - ya no debería pasar):

```
[DEBUG] Cargando atributos de negocio...  ← Primera vez
[DEBUG] Cargando atributos de negocio...  ← Segunda vez
[DEBUG] Cargando atributos de negocio...  ← Tercera vez (MAL)
```

Si ves esto, la refactorización no se está usando.

---

## ⚡ Paso 3: Testing de Caché

### Objetivo
Verificar que el caché funciona y reduce el tiempo a ~1ms.

### 3.1 Enviar Segundo Mensaje (Inmediatamente)

En la misma conversación, enviar otro mensaje:

```powershell
$body = @{
    entry = @(
        @{
            changes = @(
                @{
                    value = @{
                        messages = @(
                            @{
                                from = "1234567890"  # ← Mismo número
                                text = @{
                                    body = "Me interesa el Plan Marineritos"
                                }
                                type = "text"
                            }
                        )
                    }
                }
            )
        }
    )
} | ConvertTo-Json -Depth 10

Invoke-RestMethod -Uri "http://localhost:7071/api/WhatsAppWebhook" `
    -Method POST `
    -Body $body `
    -ContentType "application/json"
```

### 3.2 Verificar Logs de Caché (CRÍTICO)

Deberías ver:

```
[DEBUG] FASE 1: Cargando contexto...
[DEBUG] ✅ BusinessContext servido desde caché: BusinessId={guid}  ← ¡CACHE HIT!
[INFO]  ✅ Contexto cargado en 2ms: Version=2, Completitud=35%     ← ¡MUY RÁPIDO!
```

**Comparación de Tiempos**:

| Request | Tiempo Antes | Tiempo Después (sin caché) | Tiempo Después (con caché) |
|---------|--------------|---------------------------|---------------------------|
| **1er mensaje** | ~150ms | ~50ms | ~50ms |
| **2do mensaje** | ~150ms | ~50ms | **~2ms** ✅ |

### 3.3 Testing de Expiración de Caché

Para verificar que el caché expira correctamente:

```powershell
# Esperar 31 minutos (o cambiar CacheExpiration a 1 minuto para testing)
# Luego enviar otro mensaje

# Deberías ver nuevamente:
# "⚠️ BusinessContext no en caché, cargando desde BD"
```

**Tip**: Para testing más rápido, cambiar temporalmente en `CachedBusinessContextProvider.cs`:

```csharp
// Línea 19
private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(1); // ← Cambiar de 30 a 1
```

---

## 📊 Paso 4: Verificar Flujo Completo

### 4.1 Escenario Completo de Reserva

Script que simula una conversación completa:

```powershell
# Guardar como: TestConversacionCompleta.ps1

param(
    [string]$BaseUrl = "http://localhost:7071/api/WhatsAppWebhook",
    [string]$Phone = "521234567890"
)

function Send-Message {
    param([string]$Message)
    
    Write-Host "`n📱 Enviando: $Message" -ForegroundColor Cyan
    
    $body = @{
        entry = @(
            @{
                changes = @(
                    @{
                        value = @{
                            messages = @(
                                @{
                                    from = $Phone
                                    text = @{ body = $Message }
                                    type = "text"
                                }
                            )
                        }
                    }
                )
            }
        )
    } | ConvertTo-Json -Depth 10
    
    try {
        $response = Invoke-RestMethod -Uri $BaseUrl -Method POST -Body $body -ContentType "application/json"
        Write-Host "✅ Respuesta recibida" -ForegroundColor Green
        Start-Sleep -Seconds 2
    } catch {
        Write-Host "❌ Error: $_" -ForegroundColor Red
    }
}

Write-Host "🧪 Iniciando test de conversación completa..." -ForegroundColor Yellow

# Mensaje 1: Saludo inicial
Send-Message "Hola"

# Mensaje 2: Consulta de servicios
Send-Message "¿Qué planes tienen disponibles?"

# Mensaje 3: Información del bebé
Send-Message "Mi bebé tiene 4 meses"

# Mensaje 4: Nombre del bebé
Send-Message "Se llama Mateo"

# Mensaje 5: Selección de servicio
Send-Message "Me interesa el Plan Marineritos"

# Mensaje 6: Fecha deseada
Send-Message "Para mañana"

# Mensaje 7: Hora deseada
Send-Message "A las 3pm"

# Mensaje 8: Nombre del cliente
Send-Message "Mi nombre es María González"

# Mensaje 9: Confirmación
Send-Message "Sí, confirmo la reserva"

Write-Host "`n✅ Test completado. Revisa los logs de la función." -ForegroundColor Green
```

### 4.2 Ejecutar el Script

```powershell
# Guardar el script y ejecutar
.\TestConversacionCompleta.ps1

# Observar los logs en la terminal de func start
```

### 4.3 Métricas a Validar

Durante la conversación, verificar en los logs:

| Mensaje | Cache Hit Esperado | Tiempo Esperado |
|---------|-------------------|-----------------|
| 1. "Hola" | ❌ No (primera carga) | ~50ms |
| 2. "¿Qué planes tienen?" | ✅ Sí | ~2ms |
| 3. "Mi bebé tiene 4 meses" | ✅ Sí | ~2ms |
| 4. "Se llama Mateo" | ✅ Sí | ~2ms |
| ... | ✅ Sí | ~2ms |

**Resultado esperado**:
- ✅ 1 cache miss (primer mensaje)
- ✅ 8 cache hits (mensajes 2-9)
- ✅ **Cache hit rate: 88.9%** 🎉

---

## 🔬 Paso 5: Testing de Invalidación de Caché

### 5.1 Escenario: Actualizar Configuración

Simular actualización de configuración de negocio:

```sql
-- Ejecutar en SQL Server Management Studio

-- Actualizar configuración de EntityExtractionConfig
UPDATE BusinessConfigurations
SET Value = '<nuevo_json>'
WHERE BusinessId = '<tu_business_id>'
  AND [Key] = 2;  -- EntityExtractionConfig

-- Nota: El caché NO se invalida automáticamente aún
-- El sistema seguirá usando la versión en caché por 30 minutos
```

### 5.2 Invalidación Manual (Para Testing)

Crear un endpoint de testing para invalidar caché:

```powershell
# Agregar temporalmente en Program.cs para testing:

# app.MapGet("/api/admin/invalidate-cache/{businessId}", 
#     (Guid businessId, CachedBusinessContextProvider provider) =>
# {
#     provider.Invalidate(businessId);
#     return Results.Ok("Cache invalidated");
# });

# Luego llamar:
Invoke-RestMethod -Uri "http://localhost:7071/api/admin/invalidate-cache/{business-guid}" -Method GET
```

---

## 📈 Paso 6: Análisis de Logs

### 6.1 Filtrar Logs de Configuración

```powershell
# En PowerShell, mientras func start está corriendo
# Guardar logs en archivo
func start --verbose > logs.txt

# En otra terminal, analizar logs
Select-String -Path logs.txt -Pattern "BusinessContext|Configuración cargada|servido desde caché"
```

### 6.2 Contar Cache Hits y Misses

```powershell
# Script para análisis de logs

$logs = Get-Content logs.txt

$cacheHits = ($logs | Select-String "servido desde caché").Count
$cacheMisses = ($logs | Select-String "no en caché, cargando desde BD").Count
$totalRequests = $cacheHits + $cacheMisses

if ($totalRequests -gt 0) {
    $hitRate = ($cacheHits / $totalRequests) * 100
    
    Write-Host "`n📊 Estadísticas de Caché:" -ForegroundColor Yellow
    Write-Host "   Cache Hits:    $cacheHits" -ForegroundColor Green
    Write-Host "   Cache Misses:  $cacheMisses" -ForegroundColor Red
    Write-Host "   Total Requests: $totalRequests"
    Write-Host "   Hit Rate:      $($hitRate.ToString('F2'))%" -ForegroundColor Cyan
} else {
    Write-Host "No se encontraron requests en los logs" -ForegroundColor Red
}
```

### Resultado Esperado

```
📊 Estadísticas de Caché:
   Cache Hits:    25      ← Verde
   Cache Misses:  3       ← Rojo (solo primeras cargas por BusinessId)
   Total Requests: 28
   Hit Rate:      89.29%  ← Cian (debería ser >80%)
```

---

## ✅ Paso 7: Checklist de Validación

### 7.1 Funcionalidad Básica

- [ ] La aplicación compila sin errores
- [ ] Azure Functions inicia correctamente
- [ ] Puede recibir webhooks de WhatsApp
- [ ] Responde a mensajes de texto

### 7.2 Optimización de Carga

- [ ] Se ve log "Configuración cargada" solo 1 vez por BusinessId
- [ ] NO se ven 3 logs de "Cargando atributos"
- [ ] Tiempo de primera carga es ~50ms (antes ~150ms)
- [ ] Queries a BD en paralelo (Task.WhenAll)

### 7.3 Funcionamiento de Caché

- [ ] Primer request: "no en caché, cargando desde BD"
- [ ] Segundo request: "servido desde caché"
- [ ] Tiempo con caché es ~1-5ms (antes ~150ms)
- [ ] Cache hit rate > 80% en conversaciones normales

### 7.4 Comportamiento del Sistema

- [ ] La IA sigue respondiendo como "María"
- [ ] Los servicios se muestran correctamente
- [ ] Las reglas de conversación se aplican
- [ ] El flujo de reserva funciona correctamente
- [ ] La extracción de entidades funciona

---

## 🐛 Troubleshooting

### Problema 1: No Veo Logs de Caché

**Síntoma**: No aparecen logs de "BusinessContext servido desde caché"

**Causa**: Nivel de logging insuficiente

**Solución**:
```json
// local.settings.json
"Logging__LogLevel__MimosBabySpa.Application.Configuration": "Debug"
```

### Problema 2: Cache Hit Rate Muy Bajo

**Síntoma**: Cache hit rate < 50%

**Causas posibles**:
1. Cada request usa diferente BusinessId
2. Caché expira muy rápido
3. Application se reinicia frecuentemente

**Solución**:
- Usar mismo BusinessId en tests
- Aumentar tiempo de expiración para testing
- Verificar que func start no se reinicia

### Problema 3: Errores de Compilación

**Síntoma**: `func start` falla con errores de compilación

**Solución**:
```powershell
dotnet clean
dotnet build
cd src\API\MimosBabySpa.API
func start
```

### Problema 4: Timeout en Requests

**Síntoma**: Requests tardan mucho (>5 segundos)

**Causas**:
- BD lenta o sin conexión
- OpenAI no responde
- Cold start de Azure Functions

**Solución**:
- Verificar conexión a BD
- Verificar API key de OpenAI
- Esperar warm-up de functions

---

## 📊 Métricas de Éxito

### Resultados Esperados

| Métrica | Objetivo | Crítico |
|---------|----------|---------|
| **Cache Hit Rate** | >80% | >50% |
| **Tiempo 1er Request** | ~50ms | <100ms |
| **Tiempo Request Cached** | ~2ms | <10ms |
| **Cargas de EntityExtractionConfig** | 1 por request | <2 por request |
| **Errores en logs** | 0 | 0 |

### Comparación Antes/Después

| Escenario | Antes | Después | Mejora |
|-----------|-------|---------|--------|
| Conversación de 10 mensajes | 10 × 150ms = 1500ms | 50ms + (9 × 2ms) = 68ms | **95% más rápido** ✅ |
| Queries a BD | 10 × 3 = 30 queries | 1 query | **97% menos queries** ✅ |
| Carga de CPU/BD | Alta constante | Baja después de 1er request | **Mucho mejor** ✅ |

---

## 🎯 Conclusión

Si todos los checks están ✅, la refactorización fue exitosa:

1. ✅ **Carga única** de configuración
2. ✅ **Caché funcionando** correctamente
3. ✅ **Performance mejorada** significativamente
4. ✅ **Sistema funcional** sin regresiones

**¡El sistema está listo para producción!** 🚀

---

## 📝 Siguiente Paso (Opcional)

### Implementar Endpoint de Métricas

Crear endpoint para monitoreo en producción:

```csharp
// src/API/MimosBabySpa.API/Functions/MetricsFunction.cs

public class MetricsFunction
{
    [Function("GetCacheMetrics")]
    public IActionResult GetMetrics(
        [HttpTrigger(AuthorizationLevel.Admin, "get")] HttpRequest req)
    {
        // Implementar contadores de cache hits/misses
        // Retornar JSON con métricas
        
        return new OkObjectResult(new {
            cacheHits = 0,
            cacheMisses = 0,
            hitRate = 0.0
        });
    }
}
```

---

**Documentación creada el**: 28 de Enero de 2026  
**Autor**: AI Assistant  
**Última actualización**: 28 de Enero de 2026
