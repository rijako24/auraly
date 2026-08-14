# Evidencia: sesión de autenticación única online

Fecha: 2026-08-01
Rama: `feature/auraly-commerce-accounting-engine`

Actualizacion 2026-08-01: la concesion exclusiva offline ya esta implementada.
La evidencia y los conteos vigentes estan en
`docs/implementation/offline-authentication-lease-evidence.md`; ese documento
reemplaza el limite pendiente y los conteos historicos indicados mas abajo.

## Alcance conectado

La autenticación online tiene una sola autoridad: `Auraly.Api`.

1. `POST /api/auth/login` valida BCrypt y abre una `AuthenticationSession` durable.
2. SQL Server serializa la apertura por `TenantId + UserId`; un índice filtrado
   garantiza una única sesión `Active`, incluso con logins concurrentes.
3. El access token contiene `sid`, usuario y tenant. Cada solicitud JWT atendida
   por `Auraly.Api` o por el host administrativo vuelve a validar esa sesión.
4. El refresh token se persiste como SHA-256, rota en cada renovación y su
   reutilización revoca la sesión comprometida.
5. El BFF guarda access token, refresh token y el identificador durable del
   navegador en cookies `HttpOnly`; varias pestañas comparten el mismo cliente.
6. El logout cierra primero la `WorkSession` abierta y después revoca la sesión.
7. El host administrativo histórico ya no expone login, Google login, refresh,
   revoke ni `me`; solo conserva el cambio de contraseña autenticado.
8. Las páginas de registro y recuperación ya no simulan operaciones. Informan de
   forma explícita que el alta pública y la recuperación todavía no están
   habilitadas.

No se introdujo polling. La numeración documental, `DeviceId`, serie fiscal y
serie operativa no dependen de `AuthenticationSession`.

## Persistencia y validación compartidas

`dbo.AuthenticationSessions` conserva identidad, cliente, hash del refresh,
emisión, expiración, último uso, revocación, motivo, estado y `rowversion`.

La unicidad activa se protege mediante
`UX_AuthenticationSessions_User_Active (TenantId, UserId) WHERE Status='Active'`.
La comprobación de aplicación no sustituye esa restricción.

Los dos hosts resuelven `IAuthenticationSessionStore` mediante
`SqlAuthenticationSessionStore`. El host administrativo usa un validador
dedicado de solo lectura y rechaza:

- tokens sin `sid`;
- sesiones revocadas, expiradas o que no coincidan con usuario y tenant;
- issuer, audience o firma diferentes a la configuración canónica.

## Configuración

Ambos hosts deben recibir los mismos valores, fuera del repositorio:

```text
Authentication__Jwt__Issuer
Authentication__Jwt__Audience
Authentication__Jwt__SigningKey
ConnectionStrings__Auraly
```

`Auraly.Api` también utiliza:

```text
Authentication__Jwt__AccessTokenExpirationMinutes
Authentication__Jwt__RefreshTokenExpirationDays
```

La clave debe tener al menos 32 bytes. El host administrativo acepta
temporalmente `Jwt:Issuer`, `Jwt:Audience`, `Jwt:Secret` y
`ConnectionStrings:DefaultConnection` como nombres de configuración de
despliegues existentes, pero ya no emite tokens con ellos.

## Pruebas ejecutadas

```powershell
dotnet build Auraly.Commerce.sln --configuration Release
dotnet build Auraly.Commerce.sln --configuration Release
dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release
dotnet test src/Tests/Auraly.Platform.Tests/Auraly.Platform.Tests.csproj --configuration Release --no-build
dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release --no-build
dotnet test tests/Auraly.Pos.Edge.Host.Tests/Auraly.Pos.Edge.Host.Tests.csproj --configuration Release --no-build
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release --no-build
cd admin
npx tsc --noEmit
npm run test:pos
npm run build
```

Resultados:

- ambas soluciones .NET y DACPAC: 0 errores, 0 advertencias;
- aplicación administrativa: 696/696;
- fundación y arquitectura: 124/124;
- POS Edge Host: 9/9;
- integración con SQL Server real y DACPAC desplegado: 66/66;
- BFF/POS Node: 25/25;
- TypeScript y build optimizado de Next.js: correctos.

Las pruebas nuevas demuestran por reflexión que el controlador histórico no
emite tokens y verifican que su middleware rechaza una sesión inactiva o un JWT
sin `sid`.

## Límites pendientes explícitos

- falta la concesión exclusiva y firmada para autenticar POS Edge sin conexión;
- el alta pública de empresas/usuarios aún no está implementada;
- la recuperación segura de contraseña aún no está implementada;
- Google login no está disponible hasta reconstruirlo sobre la autoridad
  canónica sin autoaprovisionamiento ambiguo.

Ninguno de estos puntos se simula ni se declara terminado. La siguiente
rebanada es la concesión offline firmada y revocable del dispositivo enrolado.
