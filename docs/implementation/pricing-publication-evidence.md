# Evidencia: publicaci?n de precios y rentabilidad

**Fecha de ejecuci?n:** 2 de agosto de 2026  
**Rama:** feature/auraly-commerce-price-publication  
**Base:** 232de21852a4c6b35df42d21f516a90beec263b7

## Resultado funcional

La rebanada conecta de extremo a extremo:

```text
entrada de mercanc?a
-> motor durable
-> costo observado
-> propuesta de precio
-> revisi?n por margen o precio
-> publicaci?n transaccional
-> precio versionado
-> auditor?a
-> CatalogChanges
-> outbox push
-> delta por cursor
-> cat?logo SQLite del POS
```

La entrada no modifica autom?ticamente ProductPrices. Un cambio posterior desde el formulario general del producto tambi?n es rechazado; debe publicarse desde Pricing.

## Evidencia ejecutada

### Restore

```powershell
dotnet restore Auraly.Commerce.sln
```

Resultado: correcto. Los proyectos Contracts, Application e Infrastructure de Pricing restauraron sus dependencias y quedaron conectados a la soluci?n.

### Build completo

```powershell
dotnet build Auraly.Commerce.sln --configuration Release --no-restore
```

Resultado:

```text
0 errores
0 advertencias
```

### Fundaci?n y arquitectura

```powershell
dotnet test tests\Auraly.Foundation.Tests\Auraly.Foundation.Tests.csproj --configuration Release --no-build --no-restore
```

Resultado: 147 aprobadas, 0 fallidas.

Incluye c?lculo decimal, redondeo y reglas de arquitectura/conectividad existentes.

### POS Edge Host

```powershell
dotnet test tests\Auraly.Pos.Edge.Host.Tests\Auraly.Pos.Edge.Host.Tests.csproj --configuration Release --no-build --no-restore
```

Resultado: 15 aprobadas, 0 fallidas.

### Integraci?n SQL Server real

```powershell
dotnet test tests\Auraly.ServerSlice.IntegrationTests\Auraly.ServerSlice.IntegrationTests.csproj --configuration Release --no-build --no-restore
```

Resultado final: 85 aprobadas, 0 fallidas.

La suite despleg? el DACPAC en una base temporal real y comprob?:

- una entrada conserva el precio publicado anterior;
- se crea una propuesta por el costo observado;
- la API lista y pagina propuestas;
- el servidor calcula margen, precio y redondeo;
- guardar propuesta respeta rowversion;
- publicar crea una ?nica versi?n nueva;
- la versi?n anterior queda cerrada;
- existe una ?nica auditor?a;
- existe un ?nico CatalogChanges;
- existe una outbox durable;
- la se?al se dirige al BusinessId;
- un reintento responde conflicto y no duplica efectos;
- el dispositivo requiere una credencial de enrolamiento activa; la sincronización no usa permisos de usuario ni permisos duplicados por caja;
- POS Edge descarga el delta y aplica el precio en SQLite f?sico;
- Pricing soporta productos unificados existentes con ProductCode o Sku;
- los permisos y el alcance por negocio se validan en backend.

### DACPAC

```powershell
dotnet build database\Auraly.Database\Auraly.Database.sqlproj --configuration Release --no-restore
```

Resultado:

```text
0 errores
0 advertencias
DACPAC generado correctamente
```

### Frontend

```powershell
cd admin
npx tsc --noEmit
npm run build
npm run test:pos
```

Resultados:

- TypeScript: correcto.
- Next.js producci?n: correcto.
- ruta /dashboard/products/pricing generada.
- pruebas frontend POS: 25 aprobadas, 0 fallidas.

El comando npm run lint existe, pero el repositorio aislado no contiene una configuraci?n ESLint confirmada y Next solicita configurarla interactivamente. No se declara lint aprobado. El build de Next s? ejecut? su comprobaci?n de tipos.

## Incidencias detectadas y corregidas

1. La consulta paginada no separaba de forma inequ?voca SELECT, FROM y ORDER BY. Se corrigi? la construcci?n SQL.
2. La prueba histórica dependía de `catalog.sync`; esa autorización técnica duplicada fue retirada y sustituida por la validación canónica del enrolamiento activo.
3. La tabla Products unificada contiene registros anteriores sin ProductCode. La lectura usa ProductCode, luego Sku y finalmente el UUID, sin crear otra tabla.

Cada correcci?n se valid? primero con los escenarios focalizados y despu?s con las 85 pruebas juntas.

## L?mites de esta entrega

No se implementaron escalas de cantidad, listas o canales nuevos, reglas administrativas persistidas de redondeo ni publicaci?n manual sin propuesta. Esas capacidades permanecen en el dise?o de Pricing y deben entrar mediante rebanadas conectadas, no como tablas o interfaces vac?as.

No se modific? el workspace principal del usuario.
