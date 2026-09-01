# Evidencia: concesion exclusiva de autenticacion offline

> Actualización 2026-08-30: este mecanismo queda limitado al traspaso inicial de
> enrolamiento y a compatibilidad con ejecutables anteriores. En el ejecutable
> vigente, un login local posterior no llama este endpoint ni exige que la
> concesión siga vigente. La autoridad local durable es el equipo enrolado más la
> proyección protegida de identidades; su antigüedad no bloquea el acceso.

Fecha: 2026-08-01
Rama: `feature/auraly-commerce-accounting-engine`

## Resultado conectado

La autenticacion offline de POS Edge ya no depende solamente de una copia local
de usuarios. Antes de poder iniciar sesion sin red, el dispositivo enrolado debe
obtener de `Auraly.Api` una concesion exclusiva firmada:

1. POS Edge envia usuario y contrasena por el canal autenticado del dispositivo.
2. El servidor valida tenant, dispositivo enrolado, usuario, BCrypt y bloqueos.
3. SQL Server serializa la operacion por usuario y dispositivo.
4. Elegir el modo local constituye un traspaso autenticado: revoca las sesiones
   web activas del mismo usuario y adquiere la concesion offline sin cerrar su
   turno ni la sesion fisica de caja.
5. Una concesion offline activa impide abrir otra sesion online.
6. El servidor firma con RSA-PSS/SHA-256 (`PS256`) un payload que contiene
   `LeaseId`, `TenantId`, `UserId`, `DeviceId`, vigencia y nonce.
7. El enrolamiento entrega al POS solamente la clave publica de confianza. La
   clave privada nunca llega al instalador, SQLite, navegador ni respuesta HTTP.
8. El ejecutable actual verifica firma, tenant, dispositivo, usuario y que el
   artefacto no tenga una fecha futura imposible. La expiración histórica del
   lease se conserva como metadata de compatibilidad y no invalida el enrolamiento.
9. La concesion, su payload exacto y la liberacion pendiente se conservan en
   SQLite; cerrar o reiniciar la aplicacion no los pierde.
10. El logout cierra primero la sesion local, deja durable la liberacion y solo la
    marca como completada despues de la confirmacion idempotente del servidor.

La duración configurada no puede superar 24 horas para el traspaso heredado. No
limita sesiones ni logins locales posteriores en un equipo ya preparado.

## Persistencia

`dbo.OfflineAuthenticationLeases` conserva:

- identidad de concesion, tenant, usuario y dispositivo;
- identificador y algoritmo de la clave;
- payload y firma exactos;
- nonce, emision, inicio de vigencia y expiracion;
- estado `Active`, `Released`, `Revoked` o `Expired`;
- finalizacion, motivo, actualizacion y `rowversion`.

Indices filtrados impiden simultaneamente dos concesiones activas para el mismo
usuario o dispositivo. La apertura online y la adquisicion offline toman el lock
del mismo usuario y revisan la autoridad opuesta dentro de una transaccion
serializable.

SQLite agrega `PosOfflineAuthenticationLeases` de manera aditiva. No elimina ni
recrea facturas, borradores, series, outbox, catalogo o usuarios existentes.

## Contratos HTTP

```text
POST /api/pos/v1/authentication/offline-leases/
POST /api/pos/v1/authentication/offline-leases/{leaseId}/release
```

Ambos endpoints usan autenticacion de dispositivo enrolado y activo. No exigen
un permiso técnico por caja. El tenant y el `DeviceId` se obtienen de los claims
firmados del dispositivo, no del body.

## Configuracion segura

El servidor requiere valores externos al repositorio:

```text
Authentication__OfflineLeaseSigning__KeyId
Authentication__OfflineLeaseSigning__PrivateKeyPem
Authentication__OfflineLeaseSigning__DurationHours
```

La clave debe ser RSA de al menos 2048 bits. En SaaS el PEM debe resolverse desde
Key Vault o el proveedor seguro del despliegue; on-premise debe inyectarse desde
un almacen protegido. No existe una clave privada de desarrollo en `appsettings`.

El paquete de enrolamiento protegido incluye el mapa de claves publicas
confiables. `PosEdgeEnrollmentStore` lo transforma en
`PosEdge:OfflineLeaseTrust:TrustedPublicKeys:{KeyId}` al reiniciar el host.

## Pruebas ejecutadas

```powershell
dotnet build Auraly.Commerce.sln --configuration Release --no-restore
dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release
dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release
dotnet test tests/Auraly.Pos.Edge.Host.Tests/Auraly.Pos.Edge.Host.Tests.csproj --configuration Release
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release
```

Evidencia obtenida:

- solucion Auraly y DACPAC: 0 errores, 0 advertencias;
- arquitectura y fundacion: 124/124;
- POS Edge Host: 15/15;
- integracion con SQL Server real y DACPAC desplegado: 69/69.

Los escenarios nuevos prueban:

- dos adquisiciones concurrentes producen una sola concesion durable;
- la firma RSA-PSS se valida con la clave publica del enrolamiento;
- el traspaso a modo local revoca una sesion online activa y entrega una unica
  concesion offline; mientras esta siga activa no se abre otra sesion online;
- liberar dos veces es idempotente y vuelve a permitir login online;
- un dispositivo desenrolado o con credencial inválida no adquiere concesiones;
- firma alterada, dispositivo diferente y concesion vencida son rechazados;
- retroceder el reloj local bloquea el acceso;
- SQLite conserva la concesion y una liberacion pendiente despues de reabrir.

## Limites que no se ocultan

- La autoridad de dispositivo de esta rama todavia se materializa en
  `dbo.PosDevices`; la concesion nueva no contiene `RegisterId`, bodega, serie ni
  concepto de caja. El cambio fisico de esa autoridad a `EnrolledDevice` debe
  hacerse en la rebanada canonica que elimina caja sin duplicar tablas.
- El servicio general de sincronizacion de POS preexistente aun usa un
  `PeriodicTimer`. La concesion no crea un segundo sondeo, pero la decision global
  de operar exclusivamente mediante Pub/Sub todavia exige reemplazar ese ciclo
  completo. No se declara resuelto aqui.
- Una revocación, desactivación o cambio de permisos ocurrido mientras el POS
  está totalmente desconectado solo se conoce al reconectar. Esta es una
  consecuencia explícita del acceso local durable sin vencimiento temporal.
