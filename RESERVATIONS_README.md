# Sistema de Reservas con Integración a Calendario

## 📋 Descripción

Se ha implementado un sistema completo de reservas que permite crear reservas reales en un calendario (Google Calendar inicialmente) cuando el bot de WhatsApp detecta que un cliente quiere reservar.

## 🏗️ Arquitectura Implementada

### Capa Domain
- ✅ **Reservation** - Entidad de dominio
- ✅ **ReservationStatus** - Enum con estados: Pending, Confirmed, Completed, Cancelled, PendingCalendar
- ✅ **IReservationRepository** - Interfaz del repositorio
- ✅ **ICalendarService** - Interfaz para servicios de calendario (abstracción)

### Capa Application
- ✅ **CreateReservationRequest** - DTO de entrada
- ✅ **ReservationDto** - DTO de salida
- ✅ **IReservationService** - Interfaz del servicio de aplicación
- ✅ **ReservationService** - Implementación con lógica de negocio

### Capa Infrastructure
- ✅ **ReservationRepository** - Implementación EF Core
- ✅ **GoogleCalendarService** - Implementación de ICalendarService para Google Calendar
- ✅ **CalendarSettings** - Configuración usando Options Pattern
- ✅ **UnitOfWork** - Actualizado con Reservations

### Capa API
- ✅ **ReservationsFunction** - Azure Function con endpoints:
  - `POST /api/reservations` - Crear reserva
  - `GET /api/reservations/{id}` - Obtener reserva por ID
  - `GET /api/reservations/business/{businessId}` - Obtener reservas por negocio

## 🔧 Configuración

### 1. Configurar Google Calendar API

#### Paso 1: Crear Proyecto en Google Cloud Console
1. Ve a [Google Cloud Console](https://console.cloud.google.com/)
2. Crea un nuevo proyecto o selecciona uno existente
3. Habilita la API de Google Calendar

#### Paso 2: Crear Credenciales OAuth 2.0
1. Ve a "APIs & Services" > "Credentials"
2. Clic en "Create Credentials" > "OAuth 2.0 Client ID"
3. Selecciona "Web application"
4. Agrega las URLs de redirección autorizadas:
   - `http://localhost` (para desarrollo)
   - Tu dominio de producción
5. Guarda el **Client ID** y **Client Secret**

#### Paso 3: Obtener Refresh Token
1. Usa el siguiente script o herramienta para obtener el refresh token:

```bash
# Ejemplo usando oauth2l (herramienta de Google)
oauth2l fetch --credentials=client_secret.json \
  --scope=https://www.googleapis.com/auth/calendar \
  --output_format=json
```

O usa este script de Python:

```python
from google_auth_oauthlib.flow import InstalledAppFlow
from google.oauth2.credentials import Credentials

SCOPES = ['https://www.googleapis.com/auth/calendar']

flow = InstalledAppFlow.from_client_secrets_file(
    'client_secret.json', SCOPES)
creds = flow.run_local_server(port=0)

# Guarda el refresh_token
print(f"Refresh Token: {creds.refresh_token}")
```

#### Paso 4: Obtener Calendar ID
1. Ve a [Google Calendar](https://calendar.google.com/)
2. En configuración del calendario, copia el "Calendar ID"
3. Si quieres usar el calendario principal, usa `primary`

### 2. Actualizar Configuración

#### En `local.settings.json` (Azure Functions):
```json
{
  "Values": {
    "Calendar:Provider": "Google",
    "Calendar:ClientId": "<TU_CLIENT_ID>",
    "Calendar:ClientSecret": "<TU_CLIENT_SECRET>",
    "Calendar:RefreshToken": "<TU_REFRESH_TOKEN>",
    "Calendar:CalendarId": "<TU_CALENDAR_ID>",
    "Calendar:TimeZone": "America/Mexico_City",
    "Calendar:Scopes": "https://www.googleapis.com/auth/calendar"
  }
}
```

#### En `appsettings.json` (Console App):
```json
{
  "Calendar": {
    "Provider": "Google",
    "ClientId": "<TU_CLIENT_ID>",
    "ClientSecret": "<TU_CLIENT_SECRET>",
    "RefreshToken": "<TU_REFRESH_TOKEN>",
    "CalendarId": "<TU_CALENDAR_ID>",
    "TimeZone": "America/Mexico_City",
    "Scopes": "https://www.googleapis.com/auth/calendar"
  }
}
```

### 3. Aplicar Migración

```powershell
cd src\Infrastructure\MimosBabySpa.Infrastructure
dotnet ef database update --startup-project ..\..\API\MimosBabySpa.API\MimosBabySpa.API.csproj --context ApplicationDbContext
```

## 📡 Uso de la API

### Crear Reserva

**Endpoint:** `POST /api/reservations`

**Request Body:**
```json
{
  "businessId": "guid-del-negocio",
  "customerName": "María González",
  "phoneNumber": "+521234567890",
  "babyAge": 6,
  "serviceName": "Plan Premium",
  "reservationDate": "2024-02-15",
  "reservationTime": "10:00:00",
  "durationMinutes": 60,
  "notes": "Cliente prefiere sala tranquila"
}
```

**Response (201 Created):**
```json
{
  "reservationId": "guid-de-la-reserva",
  "businessId": "guid-del-negocio",
  "customerName": "María González",
  "phoneNumber": "+521234567890",
  "babyAge": 6,
  "serviceName": "Plan Premium",
  "reservationDate": "2024-02-15T00:00:00",
  "reservationTime": "10:00:00",
  "durationMinutes": 60,
  "status": 1,
  "calendarEventId": "event-id-de-google-calendar",
  "notes": "Cliente prefiere sala tranquila",
  "createdAt": "2024-01-19T23:25:45Z",
  "updatedAt": "2024-01-19T23:25:45Z",
  "reservationDateTime": "2024-02-15T10:00:00",
  "endDateTime": "2024-02-15T11:00:00"
}
```

### Obtener Reserva por ID

**Endpoint:** `GET /api/reservations/{reservationId}`

### Obtener Reservas por Negocio

**Endpoint:** `GET /api/reservations/business/{businessId}`

## 🤖 Integración con el Bot IA

El bot debe detectar la intención "reservar" y recopilar los siguientes datos:

1. **Nombre del cliente**
2. **Teléfono (WhatsApp)**
3. **Edad del bebé** (en meses)
4. **Servicio o plan elegido**
5. **Fecha deseada**
6. **Hora deseada**
7. **Duración del servicio** (calculada según el servicio)

Cuando el bot tenga todos los datos, debe llamar al endpoint:

```csharp
POST /api/reservations
```

Y confirmar al cliente con:

```
Perfecto 💙  
Ya reservé tu cita para el {Fecha} a las {Hora}.  
Te esperamos en Mimos Baby Spa.
```

## 🔄 Flujo de Creación de Reserva

1. **Validación de datos** - Se valida que el negocio existe
2. **Creación en BD** - Se persiste la reserva con estado `Pending`
3. **Creación en Calendario** - Se intenta crear el evento en Google Calendar
4. **Actualización de estado**:
   - Si el calendario se crea exitosamente → Estado `Confirmed` + `CalendarEventId`
   - Si falla el calendario → Estado `PendingCalendar` (la reserva queda en BD)

## 📝 Formato del Evento en Calendario

**Título:**
```
[Mimos Baby Spa] {Servicio} - {NombreCliente}
```

**Descripción:**
```
Cliente: {NombreCliente}
Teléfono: {Telefono}
Edad del bebé: {EdadBebe} meses
Servicio: {Servicio}

Reserva creada por bot IA.
```

**Inicio:** `{Fecha} {Hora}`  
**Fin:** `{Fecha} {Hora + Duración}`

## 🛡️ Manejo de Errores

- **Si falla la BD** → No se crea el evento en calendario
- **Si falla el calendario** → La reserva queda creada con estado `PendingCalendar`
- **Todos los errores se registran en logs** para diagnóstico

## 🔮 Extensibilidad Futura

El sistema está preparado para:

1. **Cambiar de Google Calendar a Outlook Calendar**:
   - Solo crear `OutlookCalendarService : ICalendarService`
   - Cambiar el registro en `Program.cs`

2. **Validación de disponibilidad**:
   - El método `ExistsOverlappingReservationAsync` ya está implementado
   - Se puede usar antes de crear la reserva

3. **Evitar choques de horarios**:
   - Usar `IsAvailableAsync` del `ICalendarService`
   - Implementar lógica de verificación de disponibilidad

## 📚 Archivos Creados/Modificados

### Nuevos Archivos:
- `Domain/Entities/Reservation.cs`
- `Domain/Enums/ReservationStatus.cs`
- `Domain/Repositories/IReservationRepository.cs`
- `Domain/Repositories/ICalendarService.cs`
- `Application/DTOs/CreateReservationRequest.cs`
- `Application/DTOs/ReservationDto.cs`
- `Application/Services/IReservationService.cs`
- `Application/Services/ReservationService.cs`
- `Infrastructure/Configuration/CalendarSettings.cs`
- `Infrastructure/Services/GoogleCalendarService.cs`
- `Infrastructure/Repositories/ReservationRepository.cs`
- `API/Functions/ReservationsFunction.cs`
- `Infrastructure/Migrations/20260119232545_AddReservations.cs`

### Archivos Modificados:
- `Domain/Repositories/IUnitOfWork.cs`
- `Domain/Entities/Business.cs`
- `Infrastructure/Repositories/UnitOfWork.cs`
- `Infrastructure/Data/ApplicationDbContext.cs`
- `API/Program.cs`
- `API/local.settings.json`
- `Console/appsettings.json`

## ✅ Checklist de Implementación

- [x] Entidad Reservation creada
- [x] Enum ReservationStatus creado
- [x] Interfaces IReservationRepository e ICalendarService creadas
- [x] DTOs creados
- [x] ReservationService implementado
- [x] GoogleCalendarService implementado
- [x] ReservationRepository implementado
- [x] UnitOfWork actualizado
- [x] ApplicationDbContext actualizado
- [x] ReservationsFunction creada
- [x] Servicios registrados en Program.cs
- [x] Migración EF Core creada
- [x] Configuración en appsettings.json actualizada

## 🚀 Próximos Pasos

1. **Aplicar la migración** a la base de datos
2. **Configurar las credenciales de Google Calendar**
3. **Integrar el bot IA** para detectar intención de reserva
4. **Probar el flujo completo** de creación de reserva
5. **Implementar validación de disponibilidad** (opcional)
6. **Agregar notificaciones** al cliente (opcional)
