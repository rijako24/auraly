# Evidencia de la rebanada POS Workstation

**Fecha:** 28 de julio de 2026
**Rama:** `feature/auraly-commerce-pos-workstation`

## Capacidades conectadas

- Host local protegido en loopback.
- Borrador POS durable en SQLite.
- Captura por escáner y recuperación de ventas temporales.
- Diálogo de pago con múltiples medios.
- Serie operativa Auraly separada de la serie fiscal DIAN.
- Formato operativo compacto por caja, por ejemplo `VTA03-00000042`.
- Consumo atómico de los dos consecutivos al emitir.
- Clave técnica cifrada localmente mediante Data Protection y Windows DPAPI.
- Snapshot fiscal, CUFE y QR calculados únicamente con la numeración DIAN.
- Persistencia de factura y outbox antes de imprimir.
- Tirilla ESC/POS para 58 y 80 mm con número Auraly, número DIAN, CUFE y QR.
- Limpieza de la venta y creación de un nuevo borrador únicamente después de imprimir.
- Reintento de impresión con el mismo DocumentId, número Auraly, número DIAN y CUFE.
- El borrador queda inmutable desde la emisión; un fallo de impresora no permite cambiar cantidades antes del reintento.
- Carga posterior a API y SQL Server con validación de ambos números.

## Comandos ejecutados

```powershell
dotnet build Auraly.Commerce.sln --configuration Release
dotnet test tests\Auraly.Foundation.Tests\Auraly.Foundation.Tests.csproj --configuration Release
dotnet test tests\Auraly.Pos.Edge.Host.Tests\Auraly.Pos.Edge.Host.Tests.csproj --configuration Release
dotnet test tests\Auraly.ServerSlice.IntegrationTests\Auraly.ServerSlice.IntegrationTests.csproj --configuration Release
dotnet build database\Auraly.Database\Auraly.Database.sqlproj --configuration Release
npx tsc --noEmit
npm run lint
npm run build
```

## Resultados finales

- Solución completa: 0 errores, 0 advertencias.
- Fundación: 70 pruebas correctas.
- Host POS: 3 pruebas correctas.
- Integración SQL Server real con despliegue DACPAC: 18 pruebas correctas.
- DACPAC: 0 errores, 0 advertencias.
- TypeScript: 0 errores.
- Build Next.js de producción: correcto; ruta `/pos` generada.
- `npm run lint`: no es una validación disponible todavía porque el repositorio no tiene ESLint configurado y `next lint` abre el asistente interactivo. No se contabiliza como aprobado.

## Límites de la evidencia

- La generación ESC/POS se prueba por bytes y mediante un adaptador de impresora sustituible.
- No se ha probado todavía una impresora física específica.
- No se implementa aún XML UBL, firma digital ni transmisión real a la DIAN.
