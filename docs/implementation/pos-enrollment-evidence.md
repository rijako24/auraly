# Evidencia de enrolamiento POS Edge

**Fecha:** 30 de julio de 2026
**Rama:** `feature/auraly-commerce-pos-enrollment`
**Base:** `3a4faabfbdfabb3a6562c264dd12afcb69f220b4`

## Flujo conectado

La rebanada conecta:

`UI POS -> API autenticada -> SQL Server -> autorización de un uso -> host
loopback -> canje servidor a servidor -> paquete protegido -> reinicio Edge ->
SQLite -> bootstrap de catálogo`

No existe una interfaz sin consumidor: el asistente llama el endpoint de
autorización, el host canjea el código, la configuración protegida alimenta el
runtime y el servicio hospedado llama al sincronizador de catálogo.

## Pruebas ejecutadas

```powershell
dotnet build Auraly.Commerce.sln --configuration Release
dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release
dotnet test tests/Auraly.Pos.Edge.Host.Tests/Auraly.Pos.Edge.Host.Tests.csproj --configuration Release
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release
dotnet build database/Auraly.Database/Auraly.Database.sqlproj --configuration Release
Set-Location admin
npx tsc --noEmit
npm run build
```

Resultados finales, repetidos después del ajuste de visibilidad por permiso:

- solución .NET: 0 errores, 0 advertencias;
- Foundation: 109/109;
- POS Edge Host: 7/7;
- integración servidor y SQL Server real: 45/45;
- DACPAC: 0 errores, 0 advertencias;
- TypeScript: correcto;
- Next.js: build correcto y ruta `/pos` generada.

La suite SQL despliega un DACPAC en una base aislada. Las pruebas nuevas
demuestran:

- usuario sin `pos.devices.enroll` recibe 403;
- autorización válida entrega una única configuración;
- el segundo canje falla;
- SQL conserva hash/salt de la credencial, no el secreto;
- series operativa y fiscal pertenecen a la caja autorizada;
- una caja con Edge también puede facturar en línea;
- un host sin configurar arranca en `EnrollmentRequired`;
- el endpoint de ventas no se publica mientras falta el enrolamiento.

Durante la ejecución apareció un deadlock real en la lectura concurrente de una
venta ya existente. Se añadió un reintento limitado exclusivamente al error SQL
1205; la prueba concurrente y la suite completa quedaron aprobadas.

## Evidencia pendiente

No se afirma todavía:

- login offline multiusuario;
- reasignación administrativa de dispositivos;
- prueba real del instalador/servicio Windows;
- selección física de impresora o balanza durante enrolamiento.

Estos puntos pertenecen a las rebanadas siguientes y no se representan mediante
mocks ni componentes vacíos.
