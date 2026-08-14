# Configuración de secretos

Los archivos `appsettings.json` y `appSettings.json` **no se commitean** porque contienen credenciales.

## Configuración inicial

1. Copia la plantilla correspondiente:
   - Console: `src/Console/Auraly.Platform.Console/appSettings.Example.json` → `appSettings.json`

2. Reemplaza los placeholders (`<...>`) con tus credenciales reales.

3. **Nunca** commitees los archivos con secretos. Están en `.gitignore`.

## Alternativa: User Secrets (recomendado para desarrollo)

```bash
cd src/Console/Auraly.Platform.Console
dotnet user-secrets init
dotnet user-secrets set "OpenAI:ApiKey" "tu-api-key"
# etc.
```

Los User Secrets tienen prioridad sobre `appsettings.json` y no se commitean.
