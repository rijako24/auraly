# Evidencia: bootstrap online y contexto visual AURALY

**Fecha:** 30 de julio de 2026  
**Commit funcional:** `8896a2e`

## Corrección conectada

El arranque online del POS ya no depende de dos APIs para obtener primero el
perfil y luego las cajas. La operación autenticada:

```text
GET /api/commerce/v1/pos/register-context/bootstrap
```

devuelve en una sola respuesta el nombre visible del usuario y todas las cajas
permitidas por su tenant. La selección de negocio, sede y caja usa el componente
visual `Select` de Auraly, no el `<select>` nativo.

Facturación incluye **Menú** para volver al sistema general sin perder el
borrador durable.

## Entorno visual reproducible

El script de desarrollo:

```powershell
sqlcmd -S .\TEST -d AuralyPosVisual -E -b `
  -i database\Auraly.Database\Scripts\Seeds\SeedAuralyOnlinePosDemo.sql
```

reutiliza el negocio existente `AURALY` y crea idempotentemente:

- Sede principal;
- Bodega principal;
- Caja 01;
- serie operativa `VTA01`;
- serie fiscal de habilitación `FE`;
- configuración fiscal de demostración;
- cajero local;
- producto de venta `7700000000001`;
- dos pedidos reales en `dbo.Orders` con `Source = Bot`.

El script no forma parte del post-deployment y no inserta secretos de
producción.

La llamada local autenticada confirmó:

- usuario `Cajero Auraly`;
- negocio `AURALY`;
- sede `Sede principal`;
- caja `Caja 01`;
- bodega `Bodega principal`;
- política de negativos heredada desde la bodega;
- caja sin enrolamiento Edge, disponible para uso online.

## Pruebas ejecutadas

```powershell
dotnet build Auraly.Commerce.sln --configuration Release
dotnet test tests\Auraly.Foundation.Tests\Auraly.Foundation.Tests.csproj `
  --configuration Release --no-build
dotnet test tests\Auraly.Pos.Edge.Host.Tests\Auraly.Pos.Edge.Host.Tests.csproj `
  --configuration Release --no-build
dotnet test tests\Auraly.ServerSlice.IntegrationTests\Auraly.ServerSlice.IntegrationTests.csproj `
  --configuration Release --no-build
cd admin
npm run test:pos
npx --no-install tsc --noEmit
npm run build
```

Resultados:

- solución .NET: 0 errores, 0 advertencias;
- fundación: 109/109;
- host Edge: 6/6;
- integración SQL Server real: 43/43;
- POS/BFF: 23/23;
- TypeScript: correcto;
- Next.js 14.2.21: build correcto, incluida `/pos`.

La nueva prueba de integración verifica que identidad y cajas llegan en una
sola llamada autenticada.

## Límite comprobado

El asistente visual de enrolamiento Edge todavía no está conectado. El diseño
definitivo y sus pruebas requeridas están en
[`pos-enrollment-design.md`](./pos-enrollment-design.md). La configuración
manual `PosEdge:*` no se declara equivalente a ese enrolamiento.
