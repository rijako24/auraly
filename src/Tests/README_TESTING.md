# 🧪 Scripts de Testing - Refactorización de Carga de Configuración

Esta carpeta contiene scripts automatizados para validar la refactorización del sistema de carga de configuración.

---

## 📁 Archivos Disponibles

### 1. **IniciarTesting.ps1** - Script Principal ⭐
Script maestro que automatiza todo el proceso de testing.

```powershell
# Opción 1: Modo interactivo (recomendado para primera vez)
.\IniciarTesting.ps1

# Opción 2: Modo automático (ejecuta tests y analiza resultados)
.\IniciarTesting.ps1 -RunTests -AnalyzeLogs
```

**¿Qué hace?**
- ✅ Verifica pre-requisitos (.NET, Azure Functions Core Tools)
- ✅ Compila el proyecto
- ✅ Inicia Azure Functions con logging detallado
- ✅ Opcionalmente ejecuta tests automáticos
- ✅ Opcionalmente analiza logs

### 2. **TestConversacionCompleta.ps1** - Tests Automatizados
Simula una conversación completa de WhatsApp para validar el sistema.

```powershell
# Uso básico (local)
.\TestConversacionCompleta.ps1

# Con parámetros personalizados
.\TestConversacionCompleta.ps1 -BaseUrl "http://localhost:7071/api/WhatsAppWebhook" -Phone "521234567890" -DelaySeconds 2

# Para ambiente de staging/producción
.\TestConversacionCompleta.ps1 -BaseUrl "https://mi-function.azurewebsites.net/api/WhatsAppWebhook?code=ABC123"
```

**¿Qué valida?**
- ✅ Carga única de configuración (no 3 veces como antes)
- ✅ Funcionamiento del caché en memoria
- ✅ Tiempos de respuesta optimizados
- ✅ Flujo completo de conversación

**Mensajes enviados:**
1. "Hola"
2. "¿Qué planes tienen disponibles?"
3. "Mi bebé tiene 4 meses"
4. "Se llama Mateo"
5. "Me interesa el Plan Marineritos"
6. "Para mañana"
7. "A las 3pm"
8. "Mi nombre es María González"
9. "Sí, confirmo la reserva"

### 3. **AnalizarLogs.ps1** - Análisis de Performance
Analiza los logs generados para extraer métricas de performance.

```powershell
# Analizar archivo de logs existente
.\AnalizarLogs.ps1 -LogFile "logs.txt"

# Monitorear logs en tiempo real
Get-Content -Wait -Tail 50 logs.txt | .\AnalizarLogs.ps1 -Watch
```

**Métricas reportadas:**
- 📊 Cache hits y misses
- ⏱️ Tiempos de carga (promedio, mínimo, máximo)
- 🔄 Número de cargas de configuración
- 📈 Mejora de performance vs versión anterior
- 💡 Recomendaciones de optimización

---

## 🚀 Quick Start - Testing en 3 Pasos

### Opción A: Modo Rápido (Todo Automático)

```powershell
# Paso 1: Navegar a la carpeta de tests
cd c:\Users\RichardJacome\Auraly\src\Tests

# Paso 2: Ejecutar script maestro con tests automáticos
.\IniciarTesting.ps1 -RunTests -AnalyzeLogs

# ¡Eso es todo! El script hace todo por ti.
```

### Opción B: Modo Manual (Paso a Paso)

```powershell
# Paso 1: Iniciar entorno
cd c:\Users\RichardJacome\Auraly\src\Tests
.\IniciarTesting.ps1

# En otra terminal PowerShell...

# Paso 2: Ejecutar tests
cd c:\Users\RichardJacome\Auraly\src\Tests
.\TestConversacionCompleta.ps1

# Paso 3: Analizar resultados
.\AnalizarLogs.ps1 -LogFile "logs.txt"

# Paso 4: Detener Functions (Ctrl+C en la primera terminal)
```

---

## 📊 Interpretación de Resultados

### ✅ Resultados Esperados (CORRECTO)

#### Logs de Carga:
```
[DEBUG] FASE 1: Cargando contexto...
[DEBUG] ⚠️ BusinessContext no en caché, cargando desde BD: BusinessId={guid}
[INFO]  ✅ Configuración cargada para BusinessId={guid} en 45ms: Services=3, Attributes=5
[INFO]  💾 BusinessContext cargado y guardado en caché: BusinessId={guid}
[INFO]  ✅ Contexto cargado en 52ms: Version=1, Completitud=15%
```

#### Logs de Caché Hit:
```
[DEBUG] FASE 1: Cargando contexto...
[DEBUG] ✅ BusinessContext servido desde caché: BusinessId={guid}
[INFO]  ✅ Contexto cargado en 2ms: Version=2, Completitud=35%
```

#### Análisis de Logs:
```
📊 ESTADÍSTICAS DE CACHÉ
────────────────────────────────────────
Cache Hits:       8
Cache Misses:     1
Total Operaciones: 9

Cache Hit Rate:   88.89%  ← Excelente (>80%)

⏱️ TIEMPOS DE CARGA
────────────────────────────────────────
Total de cargas:  9
Tiempo promedio:  8.22ms  ← Excelente (mucho mejor que 150ms)
Tiempo mínimo:    2ms     ← Caché funcionando
Tiempo máximo:    52ms    ← Carga inicial eficiente

📊 COMPARACIÓN DE PERFORMANCE
────────────────────────────────────────
Tiempo ANTES:     ~150ms (promedio)
Tiempo DESPUÉS:   8.22ms (promedio)
Mejora:           94.52%  ← ¡Excelente!
```

### ❌ Problemas Comunes

#### Problema 1: No hay cache hits
```
Cache Hits:       0
Cache Misses:     9
Cache Hit Rate:   0%  ← MAL
```

**Posibles causas:**
- Caché no está funcionando
- Cada request usa diferente BusinessId
- Application se reinicia entre requests

**Solución:**
- Verificar que `IMemoryCache` está registrado en DI
- Usar mismo BusinessId en todos los tests
- No reiniciar Functions entre tests

#### Problema 2: Tiempos lentos
```
Tiempo promedio:  150ms  ← Sin mejora
```

**Posibles causas:**
- Refactorización no se está usando
- BD lenta
- Cold start de Azure Functions

**Solución:**
- Verificar que `CachedBusinessContextProvider` está registrado
- Verificar que `HybridTransactionalOrchestrator` usa el nuevo provider
- Optimizar conexión a BD

#### Problema 3: Múltiples cargas de configuración
```
Total de cargas:  27  ← MAL (debería ser ~3, uno por BusinessId único)
```

**Causa:**
- Sistema sigue usando código viejo
- Múltiples instancias del orquestador

**Solución:**
- Verificar que cambios se compilaron
- Reiniciar Azure Functions
- Hacer `dotnet clean` y `dotnet build`

---

## 🎯 Métricas de Éxito

### Objetivos de Performance

| Métrica | Objetivo | Crítico | Tu Resultado |
|---------|----------|---------|--------------|
| **Cache Hit Rate** | >80% | >50% | ___% |
| **Tiempo 1er Request** | ~50ms | <100ms | ___ms |
| **Tiempo Request Cached** | ~2ms | <10ms | ___ms |
| **Cargas de Config** | 1 por BusinessId | <2 | ___ |

### Checklist de Validación

#### Funcionalidad Básica
- [ ] Aplicación compila sin errores
- [ ] Azure Functions inicia correctamente
- [ ] Puede recibir webhooks de WhatsApp
- [ ] Responde a mensajes

#### Optimización de Carga
- [ ] Solo 1 log "Configuración cargada" por BusinessId
- [ ] NO se ven múltiples "Cargando atributos"
- [ ] Tiempo de carga < 100ms
- [ ] Queries en paralelo (Task.WhenAll)

#### Caché
- [ ] Primer request: "no en caché"
- [ ] Segundo request: "servido desde caché"
- [ ] Tiempo con caché < 10ms
- [ ] Cache hit rate > 50%

#### Sistema
- [ ] IA responde como "María"
- [ ] Servicios se muestran
- [ ] Flujo de reserva funciona
- [ ] Extracción de entidades funciona

---

## 🔧 Configuración Avanzada

### Ajustar Tiempo de Expiración de Caché

Para testing más rápido de expiración:

```csharp
// src/Application/.../CachedBusinessContextProvider.cs
// Línea 19

// Cambiar de 30 minutos a 1 minuto
private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(1);

// Recompilar y reiniciar
```

### Habilitar Logging MÁS Detallado

```json
// src/API/Auraly.Platform.Worker/local.settings.json

{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Auraly.Platform.Application.Configuration": "Debug",
      "Auraly.Platform.Application.Orchestration": "Debug",
      "Auraly.Platform.Application.LLM": "Information"
    }
  }
}
```

### Crear Tests Personalizados

Duplicar `TestConversacionCompleta.ps1` y modificar:

```powershell
# MiTestPersonalizado.ps1

# Cambiar mensajes
Send-Message "Tu mensaje personalizado aquí" -Step "1/X"

# Agregar más aserciones
# Validar respuestas específicas
# Medir métricas custom
```

---

## 📚 Documentación Relacionada

- **Guía completa**: `../../GUIA_TESTING_REFACTORIZACION.md`
- **Documentación técnica**: `../../REFACTORIZACION_CARGA_CONFIGURACION_COMPLETADA.md`
- **Arquitectura**: `../../ARQUITECTURA_HYBRID_TRANSACTIONAL_BRAIN.md`

---

## 💡 Tips y Mejores Prácticas

### 1. Ejecutar Tests Regularmente
```powershell
# Agregar a CI/CD
.\IniciarTesting.ps1 -RunTests -AnalyzeLogs

# Validar antes de commits importantes
git add .
.\TestConversacionCompleta.ps1
git commit -m "..."
```

### 2. Monitorear Logs en Tiempo Real
```powershell
# Terminal 1: Iniciar Functions
cd src\API\Auraly.Platform.Worker
func start --verbose | Tee-Object -FilePath ..\..\Tests\logs.txt

# Terminal 2: Ver logs filtrados
Get-Content -Wait -Tail 20 ..\..\Tests\logs.txt | Select-String "BusinessContext|Configuración"
```

### 3. Debugging de Performance
```powershell
# Agregar timestamps a requests
Measure-Command { .\TestConversacionCompleta.ps1 }

# Comparar con versión anterior (git)
git stash
.\TestConversacionCompleta.ps1  # Versión vieja
git stash pop
.\TestConversacionCompleta.ps1  # Versión nueva
```

---

## ❓ FAQ

### ¿Cuánto debería durar el primer request?
**Respuesta**: ~50ms es normal. Si tarda >200ms, revisar conexión a BD.

### ¿Qué cache hit rate es bueno?
**Respuesta**: 
- >80% = Excelente ✅
- 50-80% = Bueno ⚠️
- <50% = Revisar ❌

### ¿Los tests modifican la BD?
**Respuesta**: Sí, crean ConversationStates. Usar BD de testing separada.

### ¿Puedo ejecutar en producción?
**Respuesta**: Sí, pero con precaución. Usar parámetro `-BaseUrl` con URL de producción y autenticación.

---

## 🆘 Soporte

Si encuentras problemas:

1. Revisar logs detallados en `logs.txt`
2. Verificar pre-requisitos (dotnet, func, BD)
3. Consultar troubleshooting en `GUIA_TESTING_REFACTORIZACION.md`
4. Validar que configuración en `local.settings.json` es correcta

---

**Última actualización**: 28 de Enero de 2026  
**Versión**: 1.0.0  
**Autor**: AI Assistant
