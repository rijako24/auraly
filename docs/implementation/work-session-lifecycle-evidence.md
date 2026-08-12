# Evidencia — ciclo de WorkSession por usuario

Fecha: 2026-07-31  
Rama: `feature/auraly-commerce-accounting-engine`

## Resultado conectado

`WorkSession` representa el contexto operativo de un usuario en un negocio y una
bodega. No es una sesión de autenticación ni es propietaria de la numeración.

La API autenticada permite:

- consultar la sesión abierta del usuario;
- abrirla o recuperar idempotentemente la ya existente;
- cerrar la sesión con conteo opcional de efectivo;
- recuperar el comprobante de cierre inmutable.

La apertura valida en SQL Server que usuario, negocio y bodega pertenezcan al
tenant autenticado. Un dispositivo es opcional para operación online; cuando se
informa, debe estar enrolado, activo y asociado al mismo negocio y bodega.

La base impide más de una `WorkSession` abierta por usuario y más de una por
dispositivo. El cierre consolida `WorkSessionMovements` por medio de pago,
distingue ventas, devoluciones y otros movimientos, calcula el efectivo esperado
y su diferencia contra el conteo. El comprobante completo se conserva como un
snapshot JSON con SHA-256 y se verifica antes de cada lectura.

Los permisos efectivos son:

- `work-sessions.read`
- `work-sessions.open`
- `work-sessions.close`

La venta existente ya conecta cada pago procesado con la `WorkSession` mediante
`WorkSessionMovements`; sus pruebas siguen comprobando una sola venta, un solo
pago y un solo movimiento aun con duplicados y concurrencia.

## Corrección de concurrencia del motor

La recepción y el procesamiento documental adquieren bloqueos en el mismo orden:

1. cursor secuencial del negocio;
2. documento;
3. sesión de trabajo y efectos derivados.

Esto elimina el interbloqueo entre dos ventas simultáneas sin relajar la regla de
procesamiento estricto, uno por uno, dentro de cada negocio. Una prueba específica
con dos ventas concurrentes y la suite SQL completa validan el comportamiento.

## Evidencia ejecutada

```powershell
dotnet test tests/Auraly.ServerSlice.IntegrationTests/Auraly.ServerSlice.IntegrationTests.csproj --configuration Release --no-build
# 62 aprobadas; SQL Server real y DACPAC desplegado

dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj --configuration Release --no-build
# 124 aprobadas

dotnet test tests/Auraly.Pos.Edge.Host.Tests/Auraly.Pos.Edge.Host.Tests.csproj --configuration Release --no-build
# 9 aprobadas

dotnet build Auraly.Commerce.sln --configuration Release
# 0 errores, 0 advertencias; incluye Auraly.Database.dacpac

cd admin
npx tsc --noEmit
# correcto

npm run build
# correcto; 47 páginas estáticas, incluida /pos
```

La prueba nueva despliega el DACPAC en una base SQL Server aislada y comprueba:
apertura, recuperación, apertura concurrente sin duplicados, permisos, aislamiento
del tenant, rechazo de dispositivos ajenos, movimientos por medio de pago,
efectivo esperado, diferencia, cierre idempotente, conflicto por otra
clave, comprobante íntegro y reapertura posterior.

## Frontera deliberada

Esta entrega no afirma todavía:

- sesión única de autenticación por usuario;
- eliminación global de `CashRegister`, `CashSession`, `CashierShift` o
  `RegisterId`;
- reemplazo completo de las APIs y pantallas anteriores de arqueo;
- impresión física del comprobante de cierre.

El login canónico continúa siendo emitido por la API actual de identidad y
`Auraly.Api` valida sus JWT. La sesión única debe implementarse allí de extremo a
extremo; no se creó un segundo login ni un bypass en `Auraly.Api`.

La retirada del modelo anterior se hará solo cuando POS Edge, numeración,
enrolamiento, pedidos, ventas online y pruebas consuman `WorkSession` sin una
dependencia funcional de caja. Hasta entonces no se borran estructuras activas ni
se presenta una migración parcial como terminada.
