# Evidencia: sincronización POS dirigida por eventos

**Fecha de verificación:** 2026-08-01
**Rama:** `feature/auraly-commerce-accounting-engine`
**Decisión prevalente:** `docs/decision-pos-sync-push-sin-polling.md`

## Resultado

La sincronización automática de Auraly POS Edge dejó de ejecutar el sondeo HTTP
cada dos segundos. POS Edge mantiene una única conexión TLS saliente con Azure
Web PubSub y descarga datos desde las APIs existentes solo cuando ocurre uno de
estos eventos:

1. conexión o reconexión;
2. invalidación publicada por el servidor;
3. venta local confirmada y pendiente de carga;
4. cierre de una sesión offline pendiente de liberar;
5. actualización explícita solicitada desde el host local;
6. reintento posterior a un fallo real.

No existe `PeriodicTimer` en `Auraly.Pos.Edge.Host`. Los heartbeats y la
reconexión pertenecen al SDK de Web PubSub y no consultan SQL ni las APIs de
catálogo.

## Flujo conectado

```text
cambio de producto
  -> Products + CatalogChanges + PosSynchronizationOutboxMessages
     (misma transacción SQL)
  -> señal en memoria posterior al commit
  -> SqlPosSynchronizationOutboxDispatcher
  -> Azure Web PubSub, grupo autenticado del negocio
  -> PosWebPubSubConnection
  -> PosSynchronizationSignal
  -> PosCatalogSynchronizer
  -> delta autenticado por cursor
  -> SQLite de POS Edge
```

El mensaje push es una invalidación pequeña: `NotificationId`, `TenantId`,
`BusinessId`, `Stream`, `AvailableThroughCursor` y `OccurredAt`. No transporta
catálogo, inventario, costos ni secretos.

El publicador agrupa cambios pendientes por negocio y stream. Publicar el cursor
más alto permite marcar como publicados los cursores anteriores del mismo stream
sin enviar un mensaje por producto a cada dispositivo.

## Durabilidad y fallos

- La mutación del catálogo, `CatalogChanges` y el outbox se confirman en una
  única transacción SQL Server.
- Nunca se publica antes del commit.
- El aviso posterior al commit no depende de que la solicitud HTTP continúe
  abierta.
- Al iniciar, el worker lee una vez los ámbitos pendientes persistidos. Esto es
  recuperación de arranque, no sondeo periódico.
- Un fallo del gateway deja `PublishedAt` nulo, incrementa `AttemptCount`,
  conserva `LastError` y agenda un reintento por el fallo observado.
- Solo después de que Web PubSub acepta el mensaje se completa `PublishedAt` y
  se limpia `LastError`.
- La conexión usa el protocolo confiable
  `json.reliable.webpubsub.azure.v1`, renovación de URI corta,
  `AutoReconnect` y `AutoRejoinGroups`.
- Cada reconexión dispara una puesta al día completa por cursores locales. Un
  mensaje perdido no obliga a usar polling.

## Seguridad

`POST /api/pos/v1/synchronization/negotiate` usa la credencial protegida del
dispositivo. El autenticador comprueba que el `DeviceId` siga enrolado y activo
y que el tenant continúe activo; la sincronización técnica no depende de un
permiso asignado a la caja. `TenantId` y `DeviceId` salen de los claims
autenticados y el negocio solicitado se limita a los grupos del dispositivo.
Una caja desenrolada no puede negociar Web PubSub, descargar ni subir datos.

La URI temporal autoriza únicamente:

```text
tenant:{tenantId}:business:{businessId}
tenant:{tenantId}:device:{deviceId}
```

El dispositivo no recibe la cadena de conexión ni permisos para publicar.
Los permisos del usuario gobiernan la operación local; el transporte posterior
de una operación ya autorizada se autentica por enrolamiento activo.

Configuración requerida del servidor:

```text
Auraly__PosSynchronization__WebPubSub__ConnectionString
Auraly__PosSynchronization__WebPubSub__Hub=auraly_pos
```

La cadena de conexión no se versionó en el repositorio.

## Pruebas ejecutadas

```powershell
dotnet build Auraly.Commerce.sln --configuration Release
dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release --no-build
dotnet test tests/Auraly.Pos.Edge.Host.Tests/Auraly.Pos.Edge.Host.Tests.csproj --configuration Release --no-build
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release --no-build
```

Resultados:

- solución y DACPAC: 0 errores, 0 advertencias;
- fundación y arquitectura: 124/124;
- host POS Edge: 15/15;
- integración con SQL Server real y DACPAC: 72/72.

La integración demuestra:

- negociación permitida y denegada por dispositivo;
- ámbito derivado de los claims;
- alta, edición y bloqueo de producto con cursor creciente;
- outbox escrito en la misma transacción del catálogo;
- publicación posterior al commit;
- fallo transitorio inducido, conservación SQL y recuperación sin polling;
- exactamente dos intentos en el escenario fallido;
- catálogo SQLite actualizado desde el delta autenticado;
- prueba arquitectónica que prohíbe el antiguo timer de sincronización POS.

## Fuentes oficiales consultadas

Consultadas el 2026-08-01:

- [Protocolo confiable JSON de Azure Web PubSub](https://learn.microsoft.com/en-us/azure/azure-web-pubsub/reference-json-reliable-webpubsub-subprotocol)
- [Clientes confiables y recuperación](https://learn.microsoft.com/en-us/azure/azure-web-pubsub/howto-develop-reliable-clients)
- [SDK .NET del servidor](https://learn.microsoft.com/en-us/dotnet/api/overview/azure/messaging.webpubsub-readme?view=azure-dotnet)
- [Generación de URI de acceso](https://learn.microsoft.com/en-us/dotnet/api/azure.messaging.webpubsub.webpubsubserviceclient.getclientaccessuri?view=azure-dotnet)
- [SDK .NET del cliente](https://learn.microsoft.com/en-us/dotnet/api/azure.messaging.webpubsub.clients.webpubsubclient?view=azure-dotnet)

Versiones fijadas:

- `Azure.Messaging.WebPubSub` 1.6.0;
- `Azure.Messaging.WebPubSub.Client` 1.0.0.

## Límites honestos y siguiente rebanada

Esta entrega prueba automáticamente el protocolo mediante un gateway
determinístico y prueba la persistencia con SQL Server real. No se ejecutó una
prueba contra un recurso Azure Web PubSub real porque el entorno no contiene
una cadena de conexión del recurso; por ello no se afirma conectividad cloud
aprobada.

El productor transaccional conectado en esta rebanada es `Catalog`. Los
sincronizadores existentes de seguridad, clientes y estado fiscal se ejecutan
al conectar/reconectar, pero sus mutaciones del servidor todavía deben adoptar
el mismo patrón transaccional de outbox para obtener push inmediato.

Los workers fiscales `FiscalGenerationHostedService` y
`FiscalSubmissionHostedService` aún contienen temporizadores de servidor. No
son polling de las cajas, pero deben reemplazarse en la siguiente rebanada por
señales durables del broker.

El modelo anterior todavía conserva `RegisterId`/`CashRegister` en áreas no
migradas. La decisión vigente de usuario/dispositivo exige una migración
canónica separada y probada; esta rebanada no amplió ese modelo legacy.

Para on-premise falta la implementación real del adaptador push autohospedado.
No se agregó un fallback de polling ni una interfaz vacía para simularlo.
