# Configuración de WhatsApp Cloud API

Esta guía te ayudará a configurar WhatsApp Cloud API para Auraly usando el script de automatización.

## 📋 Requisitos Previos

1. **Cuenta de Meta for Developers**
   - Crea una cuenta en: https://developers.facebook.com/
   - Verifica tu cuenta de negocio

2. **Aplicación de WhatsApp Business**
   - Crea una aplicación en Meta for Developers
   - Agrega el producto "WhatsApp" a tu aplicación
   - Obtén tu App ID y App Secret

3. **Número de Teléfono**
   - Agrega un número de teléfono a tu aplicación
   - Verifica el número (puede requerir código SMS)

4. **Azure Function App desplegada**
   - La Function App debe estar publicada y funcionando
   - Debes tener el Function Key del webhook

## 🚀 Configuración Rápida

### Opción 1: Con URL del Webhook

```powershell
.\Setup-WhatsAppCloud.ps1 `
    -AppId "1234567890123456" `
    -AppSecret (ConvertTo-SecureString "tu-app-secret" -AsPlainText -Force) `
    -WebhookUrl "https://auraly-functions.azurewebsites.net/api/WhatsAppWebhook?code=abc123" `
    -VerifyToken "mi-token-secreto-123"
```

### Opción 2: Automático (recomendado)

El script obtendrá automáticamente la URL del webhook desde Azure:

```powershell
.\Setup-WhatsAppCloud.ps1 `
    -AppId "1234567890123456" `
    -AppSecret (ConvertTo-SecureString "tu-app-secret" -AsPlainText -Force) `
    -FunctionAppName "auraly-functions" `
    -ResourceGroupName "Auraly-RG" `
    -VerifyToken "mi-token-secreto-123"
```

### Opción 3: Con número de teléfono existente

Si ya tienes un número de WhatsApp Business configurado, puedes especificarlo:

```powershell
.\Setup-WhatsAppCloud.ps1 `
    -AppId "1234567890123456" `
    -AppSecret (ConvertTo-SecureString "tu-app-secret" -AsPlainText -Force) `
    -PhoneNumber "+1234567890" `
    -FunctionAppName "auraly-functions" `
    -ResourceGroupName "Auraly-RG" `
    -VerifyToken "mi-token-secreto-123"
```

**Nota**: El número debe estar previamente agregado y verificado en Meta for Developers.

### Formato del número

Puedes proporcionar el número en cualquier formato:
- `+1234567890` (con código de país)
- `1234567890` (sin código de país, se agregará automáticamente)
- `+1 (234) 567-890` (con formato, se normalizará automáticamente)

El script buscará el número en la lista de números asociados a tu aplicación y usará el Phone Number ID correspondiente.

## 📝 Pasos Manuales (si el script no funciona)

### 1. Obtener Credenciales

1. Ve a [Meta for Developers](https://developers.facebook.com/)
2. Selecciona tu aplicación
3. Ve a **Configuración > Básica**
4. Copia:
   - **App ID**
   - **App Secret** (haz clic en "Mostrar")

### 2. Obtener Phone Number ID

**Opción A: Usar el script automáticamente**
- El script obtendrá automáticamente el Phone Number ID
- Si tienes múltiples números, puedes especificar cuál usar con `-PhoneNumber`

**Opción B: Manualmente**
1. En tu aplicación, ve a **WhatsApp > API Setup**
2. Copia el **Phone number ID** (formato: `123456789012345`)

**Usar un número existente:**
Si ya tienes un número de WhatsApp Business configurado en otra aplicación o cuenta:
1. Agrega el número a tu nueva aplicación en Meta for Developers
2. Verifica el número (puede requerir código SMS)
3. Usa el parámetro `-PhoneNumber` en el script para especificarlo

### 3. Obtener Access Token

**Opción A: Token Temporal (para pruebas)**
1. En **WhatsApp > API Setup**
2. Copia el **Temporary access token**

**Opción B: Token Permanente (para producción)**
1. Ve a **Configuración > Sistema de usuarios**
2. Crea un System User
3. Genera un token para el System User
4. Asigna permisos: `whatsapp_business_messaging`, `whatsapp_business_management`

### 4. Configurar Webhook

1. Ve a **WhatsApp > Configuración**
2. En la sección **Webhook**, haz clic en **Configurar webhooks**
3. Ingresa:
   - **Callback URL**: `https://tu-function-app.azurewebsites.net/api/WhatsAppWebhook?code=TU_FUNCTION_KEY`
   - **Verify Token**: Un token personalizado (debe coincidir con el configurado en tu código)
4. Haz clic en **Verificar y guardar**

### 5. Suscribirse a Eventos

1. En la misma página de configuración del webhook
2. Haz clic en **Gestionar** junto a "Webhooks"
3. Suscríbete a:
   - ✅ **messages** (mensajes entrantes y salientes)

### 6. Obtener Function Key

```powershell
az functionapp function keys list `
    --name auraly-functions `
    --resource-group Auraly-RG `
    --function-name WhatsAppWebhook
```

## 🔧 Configurar Application Settings en Azure

Después de obtener las credenciales, configura la Function App:

```powershell
az functionapp config appsettings set `
    --name auraly-functions `
    --resource-group Auraly-RG `
    --settings `
    "WhatsApp:PhoneNumberId=123456789012345" `
    "WhatsApp:AccessToken=tu-access-token-permanente"
```

## 🔐 Seguridad del Verify Token

El Verify Token debe ser:
- Único y secreto
- Al menos 16 caracteres
- Guardado de forma segura

**Importante**: Este token debe coincidir con el configurado en:
1. Meta for Developers (al configurar el webhook)
2. Tu código de verificación (si implementas validación adicional)

## 🧪 Probar la Configuración

### 1. Verificar Webhook

Meta enviará una solicitud GET a tu webhook con:
- `hub.mode=subscribe`
- `hub.verify_token=tu-token`
- `hub.challenge=un-numero-aleatorio`

Tu Function App debe responder con el `hub.challenge`.

### 2. Enviar Mensaje de Prueba

1. Envía un mensaje de WhatsApp al número verificado
2. Verifica los logs en Azure Portal
3. Revisa la base de datos para confirmar que se guardó el mensaje

### 3. Verificar Respuesta

El bot debe responder automáticamente. Si no responde:
- Revisa los logs de la Function App
- Verifica que el Access Token no haya expirado
- Confirma que el Phone Number ID es correcto

## 📊 Monitoreo

### Ver Logs en Tiempo Real

```powershell
az functionapp log tail `
    --name auraly-functions `
    --resource-group Auraly-RG
```

### Ver Métricas en Azure Portal

1. Ve a tu Function App en Azure Portal
2. Sección **Monitoreo > Métricas**
3. Revisa:
   - Ejecuciones de funciones
   - Errores
   - Latencia

## 🐛 Troubleshooting

### Error: "Invalid OAuth access token"

**Causa**: El Access Token ha expirado o es inválido.

**Solución**:
- Genera un nuevo Access Token
- Para producción, usa un System User Token permanente
- Actualiza el Application Setting en Azure

### Error: "Webhook verification failed"

**Causa**: El Verify Token no coincide.

**Solución**:
- Verifica que el Verify Token en Meta coincida con el de tu código
- Asegúrate de que la Function App responda correctamente al GET request

### Error: "Phone number not found"

**Causa**: El Phone Number ID es incorrecto o el número no está verificado.

**Solución**:
- Verifica el Phone Number ID en Meta for Developers
- Asegúrate de que el número esté verificado
- Revisa que tengas permisos en la aplicación

### Los mensajes no llegan al webhook

**Causa**: El webhook no está configurado o no está suscrito a eventos.

**Solución**:
1. Verifica que el webhook esté configurado en Meta
2. Confirma que estés suscrito a eventos "messages"
3. Revisa que la URL del webhook sea accesible públicamente
4. Verifica que el Function Key sea correcto

### El bot no responde

**Causa**: Error en el procesamiento del mensaje.

**Solución**:
1. Revisa los logs de la Function App
2. Verifica la conexión a la base de datos
3. Confirma que OpenAI esté configurado correctamente
4. Revisa que el Access Token tenga permisos para enviar mensajes

## 📚 Referencias

- [WhatsApp Cloud API Documentation](https://developers.facebook.com/docs/whatsapp/cloud-api)
- [Meta for Developers](https://developers.facebook.com/)
- [Graph API Reference](https://developers.facebook.com/docs/graph-api)
- [Webhook Setup Guide](https://developers.facebook.com/docs/graph-api/webhooks/getting-started)

## 🔄 Renovar Access Token

Los tokens temporales expiran después de 24 horas. Para producción:

1. Crea un System User en Meta for Developers
2. Genera un token permanente para el System User
3. Asigna los permisos necesarios
4. Actualiza el Application Setting en Azure

## 📞 Soporte

Para problemas con la configuración de WhatsApp:
- Consulta la documentación oficial de Meta
- Revisa los logs de Azure Function App
- Contacta al equipo de desarrollo
