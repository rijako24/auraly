# Evidencia — modos de documento de venta

Fecha: 2026-08-10

## Resultado implementado

- `SalesInvoice`: serie operativa `VTA`, serie DIAN, snapshot fiscal, CUFE, QR y etapa fiscal.
- `SalesReceipt`: serie operativa independiente `CVI`; no genera número DIAN, snapshot fiscal, CUFE, QR, UBL, cartera ni trabajo contable.
- Ambos recorren el mismo motor documental para líneas, inventario y evento
  operativo. Los pagos y demás efectos financieros pertenecen al motor contable
  canónico cuando el tipo los admite.
- El enrolamiento de POS Edge provisiona y persiste ambas series.
- La pantalla POS permite seleccionar el tipo antes de capturar líneas y consulta el siguiente número de la serie seleccionada.
- La impresión ESC/POS y HTML construye directamente la tirilla correspondiente, sin generar campos fiscales ocultos para retirarlos después.

## Comandos y resultados

### Solución y DACPAC

```powershell
dotnet build Auraly.Commerce.sln --configuration Release --no-restore
```

Resultado: correcto, 0 errores y 0 advertencias. El build incluye `Auraly.Database.dacpac`.

### Fundación

```powershell
dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release --no-build
```

Resultado: 164 correctas, 0 fallidas.

Incluye emisión durable offline del comprobante, replay idempotente y tirillas sin artefactos fiscales.

### Host local de POS Edge

```powershell
dotnet test tests/Auraly.Pos.Edge.Host.Tests/Auraly.Pos.Edge.Host.Tests.csproj --configuration Release --no-build
```

Resultado: 21 correctas, 0 fallidas.

Incluye emisión de `CVI` a través de la API loopback real, numeración independiente e impresión sin valores fiscales.

### Ventas online con SQL Server real

```powershell
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release --no-build --filter "FullyQualifiedName~OnlineSalesCheckoutTests"
```

Resultado: 5 correctas, 0 fallidas. La fixture despliega el DACPAC en una base SQL Server aislada.

La prueba del comprobante verifica:

- un solo documento, movimiento de inventario, pago y evento operativo;
- cero snapshots/procesos/documentos fiscales;
- cero cuentas por cobrar;
- cero trabajos contables;
- replay sin efectos duplicados.

### Frontend

```powershell
cd admin
npx tsc --noEmit
npm run build
```

Resultado: TypeScript correcto; codificación UTF-8 verificada; build Next.js correcto con 58 rutas, incluida `/pos`.

## Deuda detectada fuera de esta rebanada

La corrida conjunta de las 117 pruebas de `Auraly.ServerSlice.IntegrationTests` produjo 102 correctas y 15 fallidas por contaminación de estado compartido entre pruebas anteriores de permisos, sesiones, rutas y datos semilla. Ejemplos: inserción repetida del país `ZZ` y permisos alterados por otra clase. Las clases de ventas online pasan completas cuando usan una base aislada. Esta deuda debe resolverse separando el estado por clase o restaurando cada mutación; no se ocultó ni se atribuyó a la funcionalidad nueva.
