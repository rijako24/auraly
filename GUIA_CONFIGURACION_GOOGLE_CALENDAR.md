# Guía de Configuración de Google Calendar para Reservas

Esta guía te ayudará a configurar Google Calendar para que el sistema pueda crear reservas automáticamente usando el correo **richardjacomeg@gmail.com**.

## 📋 Requisitos Previos

- Cuenta de Google (richardjacomeg@gmail.com)
- Acceso a Google Cloud Console
- Acceso a la configuración de la aplicación (Azure Function App o archivo appsettings.json)

---

## 🔧 Paso 1: Crear Proyecto en Google Cloud Console

1. Ve a [Google Cloud Console](https://console.cloud.google.com/)
2. Inicia sesión con **richardjacomeg@gmail.com**
3. Crea un nuevo proyecto o selecciona uno existente:
   - Haz clic en el selector de proyectos (arriba a la izquierda)
   - Haz clic en "NUEVO PROYECTO"
   - Nombre: `MimosBabySpa` (o el que prefieras)
   - Haz clic en "CREAR"

---

## 🔑 Paso 2: Habilitar Google Calendar API

1. En el menú lateral, ve a **APIs y servicios** > **Biblioteca**
2. Busca "Google Calendar API"
3. Haz clic en "Google Calendar API"
4. Haz clic en **HABILITAR**

---

## 🔐 Paso 3: Crear Credenciales OAuth 2.0

### 3.1 Configurar Pantalla de Consentimiento

1. Ve a **APIs y servicios** > **Pantalla de consentimiento de OAuth**
2. Selecciona **Externo** (si es para uso personal) o **Interno** (si tienes Google Workspace)
3. Haz clic en **CREAR**
4. Completa el formulario:
   - **Nombre de la aplicación**: Mimos Baby Spa
   - **Correo electrónico de soporte**: richardjacomeg@gmail.com
   - **Correo electrónico del desarrollador**: richardjacomeg@gmail.com
5. Haz clic en **GUARDAR Y CONTINUAR**
6. **Configurar Scopes (Permisos) - OPCIONAL:**
   
   **Opción A - Si ves la sección de Scopes:**
   - Busca la pestaña o sección llamada **"Scopes"**, **"Ámbitos"** o **"Permisos"**
   - Haz clic en **+ AGREGAR O QUITAR SCOPES** o **AGREGAR O QUITAR ÁMBITOS**
   - Busca: `https://www.googleapis.com/auth/calendar`
   - Selecciónalo y haz clic en **ACTUALIZAR**
   - Haz clic en **GUARDAR Y CONTINUAR**
   
   **Opción B - Si NO encuentras la sección de Scopes (MÁS COMÚN):**
   - **Simplemente continúa** haciendo clic en **GUARDAR Y CONTINUAR** en cada pantalla
   - Los scopes se agregarán automáticamente cuando uses la API por primera vez
   - O puedes agregarlos después editando la pantalla de consentimiento
   - **Lo más importante es llegar a la sección de "Usuarios de prueba"** (paso 7)
   
   **Nota:** Para uso personal, los scopes se pueden agregar automáticamente cuando autorizas la aplicación. Lo crítico es agregar tu correo como usuario de prueba.
7. **CRÍTICO - Agregar Usuarios de Prueba:**
   - En la sección **Usuarios de prueba**, haz clic en **+ AGREGAR USUARIOS**
   - Ingresa tu correo: **richardjacomeg@gmail.com**
   - Haz clic en **AGREGAR**
   - **IMPORTANTE**: Sin este paso, recibirás el error "Error 403: access_denied"
8. Haz clic en **GUARDAR Y CONTINUAR** y luego **VOLVER AL PANEL**

**⚠️ NOTA MUY IMPORTANTE:** Si ya creaste la pantalla de consentimiento pero no agregaste usuarios de prueba, debes editarla:
1. Ve a **APIs y servicios** > **Pantalla de consentimiento de OAuth**
2. Haz clic en **EDITAR APP** (botón azul)
3. Ve a la pestaña **Usuarios de prueba** (o desplázate hasta esa sección)
4. Haz clic en **+ AGREGAR USUARIOS**
5. Ingresa: **richardjacomeg@gmail.com**
6. Haz clic en **AGREGAR**
7. Haz clic en **GUARDAR Y CONTINUAR**

### 3.2 Crear Credenciales OAuth 2.0

1. Ve a **APIs y servicios** > **Credenciales**
2. Haz clic en **+ CREAR CREDENCIALES** > **ID de cliente de OAuth**
3. Selecciona **Aplicación de escritorio**
4. Nombre: `Mimos Baby Spa Calendar`
5. **IMPORTANTE - Configurar Redirect URIs:**
   - En la sección **URIs de redirección autorizados**, haz clic en **+ AGREGAR URI**
   - Agrega estos URIs (uno por uno):
     - `https://developers.google.com/oauthplayground`
     - `http://localhost` (para desarrollo local si usas scripts)
     - `http://localhost:8080` (si usas otro puerto)
   - Haz clic en **GUARDAR** después de agregar cada URI
6. Haz clic en **CREAR**
7. **IMPORTANTE**: Guarda el **ID de cliente** y el **Secreto de cliente** que aparecen
   - Estos son tu `ClientId` y `ClientSecret`

**Nota:** Si ya creaste las credenciales sin los redirect URIs, puedes editarlas:
1. En la página de **Credenciales**, encuentra tu credencial "Mimos Baby Spa Calendar" (tipo "Escritorio")
2. En la columna **Acciones** (a la derecha), verás dos íconos: un **lápiz (✏️)** para editar y una papelera para eliminar
3. **Haz clic en el ícono del lápiz (✏️)** para editar la credencial
4. Esto te llevará a la página de configuración detallada donde verás:
   - **Nombre**
   - **Tipo de aplicación**
   - **URIs de redirección autorizados** ← **AQUÍ está lo que buscas**
5. En la sección **URIs de redirección autorizados**, haz clic en **+ AGREGAR URI**
6. Agrega exactamente: `https://developers.google.com/oauthplayground`
7. Haz clic en **GUARDAR** (botón azul en la parte inferior)

---

## 🎫 Paso 4: Obtener Refresh Token

Para obtener el Refresh Token, necesitas hacer una autorización inicial. Hay varias formas:

### Opción A: Usar Google OAuth 2.0 Playground (Recomendado)

1. Ve a [Google OAuth 2.0 Playground](https://developers.google.com/oauthplayground/)
2. Haz clic en el ícono de configuración (⚙️) en la esquina superior derecha
3. Marca la casilla **"Use your own OAuth credentials"**
4. Ingresa tu **Client ID** y **Client Secret**
5. En el panel izquierdo, busca y selecciona:
   - `https://www.googleapis.com/auth/calendar`
6. Haz clic en **Authorize APIs**
7. Inicia sesión con **richardjacomeg@gmail.com** y autoriza la aplicación
8. Haz clic en **Exchange authorization code for tokens**
9. **IMPORTANTE**: Copia el **Refresh token** que aparece (es un string largo)
   - Este es tu `RefreshToken`

### Opción B: Usar Script de Python (Alternativa)

Si prefieres usar un script, puedes usar este código Python:

```python
from google_auth_oauthlib.flow import InstalledAppFlow
from google.auth.transport.requests import Request
import pickle
import os

SCOPES = ['https://www.googleapis.com/auth/calendar']

def get_refresh_token():
    creds = None
    
    # Descarga el archivo JSON de credenciales desde Google Cloud Console
    # (Credenciales > Descargar JSON)
    
    flow = InstalledAppFlow.from_client_secrets_file(
        'credentials.json', SCOPES)
    creds = flow.run_local_server(port=0)
    
    # El refresh token está en creds.refresh_token
    print(f"Refresh Token: {creds.refresh_token}")
    
    return creds.refresh_token

if __name__ == '__main__':
    refresh_token = get_refresh_token()
    print(f"\nRefresh Token: {refresh_token}")
```

---

## ⚙️ Paso 5: Obtener Calendar ID (Opcional)

Por defecto, el sistema usa el calendario "primary" (calendario principal). Si quieres usar un calendario específico:

1. Ve a [Google Calendar](https://calendar.google.com/)
2. En el panel izquierdo, encuentra el calendario que quieres usar
3. Haz clic en los tres puntos (⋮) junto al calendario
4. Selecciona **Configuración y uso compartido**
5. En la sección **Integrar calendario**, copia el **ID de calendario**
   - Ejemplo: `richardjacomeg@gmail.com` o un ID personalizado

---

## 🔧 Paso 6: Configurar en la Aplicación

### Opción A: Azure Function App (Producción)

Ejecuta este comando de PowerShell para configurar las variables de entorno:

```powershell
az functionapp config appsettings set `
  --name mimosbabyspa-functions `
  --resource-group MimosBabySpa `
  --settings `
    "Calendar:ClientId=TU_CLIENT_ID" `
    "Calendar:ClientSecret=TU_CLIENT_SECRET" `
    "Calendar:RefreshToken=TU_REFRESH_TOKEN" `
    "Calendar:CalendarId=primary" `
    "Calendar:TimeZone=America/Bogota"
```

**Reemplaza:**
- `TU_CLIENT_ID`: El Client ID que obtuviste en el Paso 3.2
- `TU_CLIENT_SECRET`: El Client Secret que obtuviste en el Paso 3.2
- `TU_REFRESH_TOKEN`: El Refresh Token que obtuviste en el Paso 4
- `CalendarId`: "primary" o el ID del calendario específico
- `TimeZone`: "America/Bogota" para Colombia (o la zona horaria que necesites)

### Opción B: appsettings.json (Desarrollo Local)

Agrega esta sección a tu `appsettings.json`:

```json
{
  "Calendar": {
    "Provider": "Google",
    "ClientId": "TU_CLIENT_ID",
    "ClientSecret": "TU_CLIENT_SECRET",
    "RefreshToken": "TU_REFRESH_TOKEN",
    "CalendarId": "primary",
    "TimeZone": "America/Bogota"
  }
}
```

---

## ✅ Paso 7: Verificar la Configuración

Para verificar que todo funciona:

1. Crea una reserva de prueba desde WhatsApp
2. Verifica que el evento se creó en Google Calendar
3. Revisa los logs de la aplicación para confirmar que no hay errores

---

## 🔒 Seguridad

**IMPORTANTE:**
- **NUNCA** compartas tu `ClientSecret` o `RefreshToken`
- **NUNCA** los subas a repositorios públicos
- Usa variables de entorno o Azure Key Vault para almacenarlos
- El `RefreshToken` no expira a menos que lo revoques manualmente

---

## 🐛 Solución de Problemas

### Error: "Error 400: redirect_uri_mismatch" ⚠️

Este error ocurre porque las aplicaciones de tipo "Escritorio" no muestran explícitamente la sección "URIs de redirección autorizados" en Google Cloud Console.

**Solución ALTERNATIVA - Usar método sin OAuth Playground:**

Como las aplicaciones de escritorio no permiten configurar fácilmente redirect URIs para OAuth Playground, usa este método alternativo:

#### Opción 1: Usar Script de Python (Recomendado)

1. Descarga el archivo JSON de credenciales desde Google Cloud Console:
   - Ve a **Credenciales** > Haz clic en tu credencial "Mimos Baby Spa Calendar"
   - Haz clic en **Descargar JSON** (ícono de descarga junto al Client ID)
   - Guarda el archivo como `credentials.json`

2. Instala las librerías necesarias:
```bash
pip install google-auth google-auth-oauthlib google-auth-httplib2 google-api-python-client
```

3. Crea un archivo `get_refresh_token.py` con este código:

```python
from google_auth_oauthlib.flow import InstalledAppFlow
import json

SCOPES = ['https://www.googleapis.com/auth/calendar']

def get_refresh_token():
    # Carga las credenciales desde el archivo JSON descargado
    flow = InstalledAppFlow.from_client_secrets_file(
        'credentials.json', SCOPES)
    
    # Esto abrirá un navegador para autorizar
    creds = flow.run_local_server(port=0)
    
    # El refresh token está aquí
    print("\n" + "="*50)
    print("REFRESH TOKEN:")
    print("="*50)
    print(creds.refresh_token)
    print("="*50)
    print("\nGuarda este Refresh Token de forma segura!")
    
    return creds.refresh_token

if __name__ == '__main__':
    refresh_token = get_refresh_token()
```

4. Ejecuta el script:
```bash
python get_refresh_token.py
```

5. Se abrirá tu navegador para autorizar. Inicia sesión con **richardjacomeg@gmail.com** y autoriza.

6. Copia el Refresh Token que aparece en la consola.

#### Opción 2: Crear nueva credencial tipo "Aplicación web" (Alternativa)

Si prefieres usar OAuth Playground, puedes crear una credencial adicional de tipo "Aplicación web":

1. Ve a **Credenciales** > **+ CREAR CREDENCIALES** > **ID de cliente de OAuth**
2. Selecciona **Aplicación web** (en lugar de "Escritorio")
3. Nombre: `Mimos Baby Spa Calendar Web`
4. En **URIs de redirección autorizados**, agrega: `https://developers.google.com/oauthplayground`
5. Haz clic en **CREAR**
6. Usa este nuevo Client ID y Client Secret en OAuth Playground
7. Una vez que obtengas el Refresh Token, puedes usar las mismas credenciales en tu aplicación

**Nota:** El Refresh Token funciona igual sin importar si viene de una credencial "Escritorio" o "Aplicación web".

### Error: "Invalid grant"
- El Refresh Token puede haber expirado o sido revocado
- Genera un nuevo Refresh Token siguiendo el Paso 4
- Verifica que el redirect URI esté correctamente configurado

### Error: "Calendar not found"
- Verifica que el `CalendarId` sea correcto
- Usa "primary" si no estás seguro

### Error: "Error 403: access_denied" ⚠️ **CRÍTICO**

Este error significa que la aplicación está en modo de prueba y tu correo **NO está en la lista de usuarios de prueba**.

**Solución:**
1. Ve a **Google Cloud Console** > **APIs y servicios** > **Pantalla de consentimiento de OAuth**
2. Haz clic en **EDITAR APP** (botón azul)
3. Ve a la pestaña o sección **Usuarios de prueba**
4. Haz clic en **+ AGREGAR USUARIOS**
5. Ingresa exactamente: **richardjacomeg@gmail.com**
6. Haz clic en **AGREGAR**
7. Haz clic en **GUARDAR Y CONTINUAR**
8. Espera 1-2 minutos y vuelve a intentar la autorización

**Verificar que el usuario está agregado:**
- En la sección **Usuarios de prueba**, deberías ver una lista con tu correo
- Si no aparece, agrégalo nuevamente siguiendo los pasos anteriores

**Nota:** Mientras la aplicación esté en modo de prueba (sin verificación de Google), SOLO los usuarios en la lista de "Usuarios de prueba" podrán autorizar la aplicación.

### Error: "Insufficient permissions"
- Verifica que el scope `https://www.googleapis.com/auth/calendar` esté habilitado
- Verifica que hayas autorizado la aplicación correctamente
- **Asegúrate de que el correo richardjacomeg@gmail.com esté en la lista de usuarios de prueba** (ver error 403 arriba)

### Los eventos no se crean
- Revisa los logs de la aplicación
- Verifica que las credenciales estén correctamente configuradas
- Asegúrate de que la API de Google Calendar esté habilitada
- Verifica que el Refresh Token sea válido y no haya expirado

---

## 📚 Referencias

- [Google Calendar API Documentation](https://developers.google.com/calendar/api)
- [OAuth 2.0 for Desktop Apps](https://developers.google.com/identity/protocols/oauth2/native-app)
- [Google OAuth 2.0 Playground](https://developers.google.com/oauthplayground/)

---

## 📝 Resumen de Valores Necesarios

Una vez completados todos los pasos, necesitarás estos valores:

```
ClientId: [Del Paso 3.2]
ClientSecret: [Del Paso 3.2]
RefreshToken: [Del Paso 4]
CalendarId: "primary" o [ID específico del Paso 5]
TimeZone: "America/Bogota" (o tu zona horaria)
```

¡Listo! Con estos valores configurados, el sistema podrá crear reservas automáticamente en Google Calendar usando el correo richardjacomeg@gmail.com.
