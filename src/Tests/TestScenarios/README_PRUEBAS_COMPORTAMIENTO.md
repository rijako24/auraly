# Pruebas de Comportamiento Conversacional

Este documento explica cómo ejecutar las pruebas de comportamiento conversacional del bot de forma individual.

## Pruebas Disponibles

1. **Test 1**: Comportamiento de saludo contextual
   - Valida que el bot saluda correctamente en el primer mensaje
   - Verifica que NO repite saludos en mensajes posteriores

2. **Test 2**: Verificación automática de disponibilidad
   - Valida que el bot verifica disponibilidad cuando debe
   - Verifica que muestra los horarios disponibles del backend

3. **Test 3**: No promesas falsas
   - Valida que el bot NO promete acciones que no ejecuta
   - Verifica que pregunta antes de confirmar una reserva

4. **Test 4**: Horarios del backend (no inventados)
   - Valida que los horarios mostrados son del backend
   - Verifica que NO inventa horarios

5. **Test 5**: Inferencia de referencias implícitas
   - Valida que el bot infiere correctamente referencias como "el que me recomendaste"
   - Verifica que mantiene el contexto de la conversación

## Ejecución Individual

### Opción 1: Script de PowerShell (Recomendado)

```powershell
# Menú interactivo
.\RunBehaviorTests.ps1

# Ejecutar una prueba específica
.\RunBehaviorTests.ps1 1
.\RunBehaviorTests.ps1 2
.\RunBehaviorTests.ps1 3
.\RunBehaviorTests.ps1 4
.\RunBehaviorTests.ps1 5

# Ejecutar todas las pruebas
.\RunBehaviorTests.ps1 all
```

### Opción 2: Línea de comandos directa

```powershell
# Ejecutar prueba 1
dotnet run -- behavior:1

# Ejecutar prueba 2
dotnet run -- behavior:2

# Ejecutar todas las pruebas de comportamiento
dotnet run -- behavior

# Ejecutar todas las pruebas (incluye las automatizadas)
dotnet run -- all
```

## Flujo de Trabajo Recomendado

1. **Ejecutar una prueba individual** para identificar problemas específicos:
   ```powershell
   .\RunBehaviorTests.ps1 1
   ```

2. **Revisar los resultados** y los mensajes de error o advertencias

3. **Corregir el código** según sea necesario

4. **Volver a ejecutar la misma prueba** para validar la corrección:
   ```powershell
   .\RunBehaviorTests.ps1 1
   ```

5. **Repetir** con otras pruebas según sea necesario

6. **Ejecutar todas las pruebas** al final para asegurar que todo funciona:
   ```powershell
   .\RunBehaviorTests.ps1 all
   ```

## Requisitos

- `appsettings.json` debe estar presente en el directorio `src/Tests/TestScenarios/`
- La base de datos debe estar configurada y accesible
- Las credenciales de OpenAI deben estar configuradas en `appsettings.json`
