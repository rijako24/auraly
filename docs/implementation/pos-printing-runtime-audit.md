# Auditoría del runtime e impresión POS

Fecha: 2026-09-19

## Decisión y causa raíz

El propietario de datos se resuelve una sola vez con
`resolvePosExecutionMode`:

- navegador o aplicación instalada no enrolada: `OnlinePosClient` y servidor;
- aplicación enrolada, conectada o desconectada: `PosEdgeClient` y SQLite;
- servicio instalado temporalmente inaccesible: no se adivina otro propietario
  mientras reinicia.

La instalación sólo cambia el adaptador de periféricos. Un `OnlinePosClient`
recibe el token local y envía la impresión a POS Edge; sin token usa el diálogo
del navegador. El enrolamiento no crea otra regla de impresión.

Se encontraron siete causas de divergencia:

1. la selección del runtime estaba expresada por dos funciones equivalentes;
2. POS Edge aceptaba nombres de impresora inexistentes y sólo fallaba al enviar;
3. un JSON local inválido se ocultaba cargando valores predeterminados;
4. el cajón usaba el campo heredado `receiptPrinterName` mientras los demás
   trabajos usaban el campo canónico `posPrinterName`.
5. el handler HTTP cambiaba `serverConnected`, pero no publicaba la transición
   al SSE local; por eso la UI podía seguir mostrando “Verificando conexión”
   mientras el catálogo ya se descargaba;
6. la configuración de impresión no exponía de forma consistente si los
   identificadores persistidos seguían presentes en Windows;
7. el editor intentaba enfocar el descuento de una línea genérica, aunque ese
   control está deshabilitado, y dejaba el recorrido de teclado sin foco inicial.

## Propietarios canónicos

| Regla o estado | Propietario | Fuente de verdad | Consumidores | Transporte |
|---|---|---|---|---|
| Web o SQLite | `resolvePosExecutionMode` | estado de enrolamiento informado por `/edge/v1/health` | login y página POS | no aplica |
| Estado operativo `Ready` | salud de POS Edge | identidad + catálogo + login local | preparación y POS | SSE local para invalidar |
| Conectividad servidor | `PosServerConnectionState` | última respuesta HTTP autenticada del dispositivo | salud y política de inventario | HTTP; Web PubSub es una señal separada |
| Configuración de impresoras | `PosPrinterConfigurationStore` | `printer-settings.json` local | UI, venta, pedidos, caja y cierres | no aplica |
| Validez y preparación de impresión | `PosPrinterConfigurationStore` | configuración local + catálogo de impresoras de Windows | `Periféricos` y trabajos | no aplica |
| Emisión online | `OnlinePosClient`/API canónica | SQL Server | POS web e instalado no enrolado | navegador o POS Edge |
| Emisión enrolada | `PosEdgeClient`/motor POS canónico | SQLite y outbox | POS enrolado | POS Edge |
| Pedidos | API de Pedidos | SQL Server; borrador de trabajo SQLite al recuperar en instalado | POS web y enrolado conectado | JWT web o proxy autenticado de POS Edge |
| Render del documento | `PosPrintTemplateCatalog` y renderizadores compartidos | snapshot emitido | todos los modos | navegador o Windows |

## Flujo anterior y final

Anterior:

`estado local -> funciones equivalentes de selección -> Online/Edge -> validación parcial en endpoint y otra validación en impresores -> transporte`

Final:

`salud/enrolamiento -> resolvePosExecutionMode -> motor online o SQLite -> snapshot canónico -> PosPrinterConfigurationStore -> resolvePosPrintRoute -> navegador o POS Edge/Windows -> resultado observable`

Un reintento idempotente de una venta ya emitida devuelve el resultado existente
y no repite impresión ni cajón. Una copia adicional se solicita mediante la
reimpresión explícita existente.

## Matriz verificada

| Escenario | Propietario esperado | Resultado |
|---|---|---|
| Web sin aplicación | servidor | probado por resolvedor y transporte navegador |
| Instalado no enrolado | servidor + periféricos POS Edge | probado por `EnrollmentRequired` y host real publicado |
| Enrolado desconectado | SQLite | probado por estados enrolados y pruebas del host |
| Enrolado sin impresora | SQLite; preparación y venta disponibles | no imprime; una solicitud de impresión se rechaza explícitamente |
| Enrolado con impresora válida | SQLite; impresión preparada | configuración y despacho canónicos |
| Impresora eliminada o renombrada | mismo motor; impresión no preparada | rechazo antes del trabajo |
| Reinicio de backend | no cambia configuración local | configuración independiente del servidor |
| Reinicio de aplicación | mismo archivo local | probado con binario `win-x64` publicado y reiniciado |
| Reconexión | mismo propietario | backoff compartido existente, sin polling nuevo |
| Cambio de impresora | misma mutación autoritativa | respuesta reutilizada sin segundo GET |
| Otro tenant o dispositivo | alcance de venta validado por su motor; impresora local a la estación | sin configuración remota cruzada |
| Reintento de venta confirmada | mismo snapshot, ningún efecto repetido | probado por regla pura de efecto |

Los pedidos no se duplican como maestro en SQLite. La recuperación instalada
hidrata un borrador local transitorio y crear, guardar y facturar vuelve siempre
al propietario de servidor mediante Edge. En una caja enrolada sin conexión se
rechaza explícitamente esa operación; las demás ventas locales y sus borradores
continúan funcionando con SQLite.

## Cobertura funcional ejecutada

| Camino | Evidencia |
|---|---|
| agregar producto, código de barras y búsqueda | frontend POS 228/228, Playwright de búsqueda |
| editar líneas, descuentos, costo, margen y teclado | unitarias del editor 15/15 y Playwright real |
| producto genérico | frontend POS y Playwright del editor |
| pausar, guardar y recuperar ventas | Edge 141/141 e integración SQL |
| crear, recuperar, editar y guardar pedidos | integración SQL 70/70 y frontend de Pedidos 20/20 |
| facturar pedido y lote de pedidos | integración SQL y Playwright secuencial instalado |
| contado y crédito | integración SQL, incluyendo validador autoritativo de cartera |
| bodega con y sin inventario negativo | pruebas de política, captura y pedidos |
| reinicio y login local posterior | jornada SQLite reabierta sobre el mismo archivo y directorio de llaves |
| desconexión, trabajo local y reconexión | jornada de identidad/sincronización y backoff acotado 7/7 |
| navegador, instalado no enrolado y enrolado | resolvedor único y rutas de impresión compartidas |
| impresión de factura, pedido, caja y cierre | Edge, rutas frontend y configuración canónica |
| reintento de venta confirmada | ninguna impresión ni apertura de cajón repetida |
| tenant/dispositivo incorrectos | integración de enrolamiento, seguridad y sincronización |

Resultados finales reproducibles:

- `Auraly.Pos.Edge.Host.Tests`: 136/136;
- `Auraly.Foundation.Tests`: 538/538;
- integración ServerSlice de ventas, pedidos, crédito, inventario,
  enrolamiento y sincronización: 70/70;
- recuperación de pedidos contra SQL real: 10/10;
- frontend POS: 228/228, impresión 15/15, reconexión 7/7, editor 15/15,
  Pedidos 20/20;
- Playwright sobre `standalone`: 4/4;
- lint: cero errores y ocho advertencias preexistentes fuera del slice;
- payload `win-x64`: build correcto; `Auraly.Desktop.exe` inició Node y POS
  Edge, `/login` respondió 200 y Edge rechazó salud sin token con 401;
- prueba Windows previa del mismo host: siete impresoras descubiertas,
  configuración persistida y recuperada tras reinicio.

No se envió papel a una impresora física durante esta auditoría automática. Se
validaron descubrimiento real de Windows, identidad persistente, render,
despacho y propagación de errores; papel, controlador, conexión USB/red, tóner y
corte físico sólo pueden certificarse con una impresión de aceptación en el
equipo destino.

## Viajes e I/O

- `GET /configuration/printers`: una lectura de archivo, una enumeración de
  impresoras y una enumeración de puertos; cero consultas de base de datos.
- `GET /health`: no lee configuración ni enumera impresoras; la preparación no
  depende de periféricos.
- `PUT /configuration/printers`: una enumeración de impresoras, una escritura
  atómica y la misma respuesta autoritativa; no vuelve a enumerar puertos COM.
  La interfaz reutiliza la respuesta y no hace un segundo GET del recurso.
- Despacho: una lectura de configuración y una enumeración de impresoras por
  lote/trabajo; no hay I/O por impresora ni consultas de base de datos.
- No se agregó polling, cola, tabla, worker ni segundo motor.

## Compatibilidad y rollback

Los campos heredados continúan proyectándose al contrato canónico al leer. El
archivo sólo se reescribe al guardar. Los nuevos campos de respuesta
`printingReady` y `validationErrors` son aditivos. El rollback de código conserva
el mismo archivo; no hay migración de datos ni esquema que revertir.
