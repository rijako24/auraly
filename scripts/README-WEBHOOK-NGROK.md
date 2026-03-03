# Configurar Webhook de Meta con ngrok

## Requisitos previos

1. **Azure Functions Core Tools** (para ejecutar la API localmente):
   ```powershell
   npm install -g azure-functions-core-tools@4
   ```
   O con winget: `winget install Microsoft.Azure.FunctionsCoreTools`

2. **ngrok** instalado:
   ```powershell
   winget install ngrok.ngrok
   ```
   Primera vez: crea cuenta en ngrok.com y ejecuta `ngrok config add-authtoken TU_TOKEN`

## Pasos

### 1. Inicia la API (Terminal 1)

```powershell
cd src\API\MimosBabySpa.API
func start
```

Espera hasta ver: `Functions: WhatsAppWebhook: [GET,POST] http://localhost:7071/api/WhatsAppWebhook`

### 2. Inicia ngrok y obtén la URL (Terminal 2)

```powershell
cd scripts
.\Start-WebhookNgrok.ps1
```

El script abrirá ngrok en una ventana y mostrará:
- **Callback URL** → copia esta URL
- **Verify Token** → `mimos-meta-verify-2024`

### 3. Configura en Meta for Developers

1. Ve a [developers.facebook.com](https://developers.facebook.com) → Tu app
2. **WhatsApp** → **Configuración**
3. **Configurar webhooks**
4. Callback URL: pega la URL que te mostró el script
5. Verify Token: `mimos-meta-verify-2024`
6. **Verificar y guardar**
7. **Gestionar** → suscríbete a **messages**

## Valores por defecto

| Campo | Valor |
|-------|-------|
| **Callback URL** | `https://[tu-subdominio].ngrok-free.app/api/WhatsAppWebhook` |
| **Verify Token** | `mimos-meta-verify-2024` |

El Verify Token está en `local.settings.json` → `WhatsApp__Webhook__VerifyToken`.
Puedes cambiarlo; debe coincidir en Meta y en la configuración local.
