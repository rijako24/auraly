# Evidencia: sesión de autenticación única online

Fecha: 2026-08-01
Rama: `feature/auraly-commerce-accounting-engine`
Base: `bb77407eed56c2b7034ec993ce757773795f24de`

## Alcance conectado

Esta entrega implementa la parte online de
`decision-sesion-unica-usuario-online-offline.md` de extremo a extremo:

1. `POST /api/auth/login` valida la contraseña BCrypt y abre una
   `AuthenticationSession` durable.
2. SQL Server serializa la apertura por `TenantId + UserId`; un índice filtrado
   garantiza una sola sesión `Active` incluso ante solicitudes concurrentes.
3. El access token contiene `sid`, usuario y tenant. Cada request JWT atendida por
   `Auraly.Api` vuelve a comprobar que esa sesión siga activa.
4. El refresh token solo se persiste como SHA-256. Cada renovación rota el secreto;
   reutilizar el anterior revoca la sesión como comprometida.
5. El BFF conserva access token, refresh token y el identificador durable del
   navegador en cookies `HttpOnly`. Varias pestañas comparten el mismo cliente.
6. El logout cierra primero la `WorkSession` abierta, genera su cierre idempotente
   y después revoca la sesión de autenticación.
7. Un nuevo login solo es posible después de cerrar o expirar la sesión anterior.

No se introdujo polling. La numeración documental, el `DeviceId`, la serie fiscal
y la serie operativa no dependen de `AuthenticationSession`.

## Persistencia

`dbo.AuthenticationSessions` conserva:

- `AuthenticationSessionId`, `TenantId`, `UserId` y `ClientId`;
- hash SHA-256 del refresh token;
- emisión, expiración, último uso, revocación y motivo;
- estado explícito `Active`, `Revoked` o `Expired`;
- `rowversion` e índices de historia y expiración.

La unicidad activa se protege mediante
`UX_AuthenticationSessions_User_Active (TenantId, UserId) WHERE Status='Active'`.
La comprobación de aplicación no sustituye esta restricción.

## Configuración

`Auraly.Api` requiere configuración segura fuera del repositorio:

```text
Authentication__Jwt__Issuer
Authentication__Jwt__Audience
Authentication__Jwt__SigningKey
Authentication__Jwt__AccessTokenExpirationMinutes
Authentication__Jwt__RefreshTokenExpirationDays
ConnectionStrings__Auraly
```

La clave de firma debe tener al menos 32 bytes. No existe un valor de producción
en `appsettings.json`.

Cuando el host Commerce está separado, el BFF usa
`AURALY_COMMERCE_API_URL` para `api/auth/*` y `api/commerce/*`. Con gateway único,
`NEXT_PUBLIC_API_URL` sigue siendo suficiente. El browser nunca recibe el refresh
token mediante JavaScript.

## Pruebas ejecutadas

```powershell
dotnet build Auraly.Commerce.sln --configuration Release
dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release --no-build
dotnet test tests/Auraly.Pos.Edge.Host.Tests/Auraly.Pos.Edge.Host.Tests.csproj --configuration Release --no-build
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release --no-build
dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release
cd admin
npx tsc --noEmit
npm run test:pos
npm run build
```

Resultados:

- solución .NET y DACPAC: 0 errores, 0 advertencias;
- fundación y arquitectura: 124/124;
- POS Edge Host: 9/9;
- integración con SQL Server real y DACPAC desplegado: 66/66;
- pruebas Node BFF/POS: 25/25;
- TypeScript y build optimizado de Next.js: correctos.

La integración cubre específicamente:

- dos pestañas y una sola sesión;
- segundo cliente rechazado;
- dos logins simultáneos con exactamente un ganador;
- hash del refresh token, rotación y detección de reutilización;
- token revocado rechazado en la siguiente request;
- logout, cierre de `WorkSession` y nuevo login;
- permisos reales, FK de usuario y aislamiento de tenant.

## Límites pendientes explícitos

Esta evidencia no declara terminada toda la decisión online/offline:

- falta la concesión exclusiva firmada para login de POS Edge sin conexión;
- el controlador de autenticación del host administrativo histórico aún debe
  delegarse o retirarse; el BFF ya no lo usa cuando
  `AURALY_COMMERCE_API_URL` apunta a `Auraly.Api`, pero su URL directa sigue siendo
  una autoridad heredada;
- Google login y registro deben migrarse antes de desactivar definitivamente esa
  autoridad histórica;
- las APIs administrativas del host histórico aún deben validar `sid` contra
  `AuthenticationSessions` para que una revocación sea inmediata allí.

Estos puntos no están simulados ni marcados como aprobados. Constituyen la siguiente
rebanada de autenticación antes de implementar la concesión offline.
