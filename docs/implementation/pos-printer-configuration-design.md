# Configuración de impresión POS

Fecha de cierre: 2026-07-29

## Extensión gastronómica diseñada

Las salidas virtual/física y receptor de comandas se especifican en el
[diseño de restaurante](restaurant-pos-design.md#8-estaciones-comandas-push-e-impresión).
Reutilizan perfiles, plantillas y adaptadores de este documento. No cambian el
enrolamiento, preparación o envío de facturas de la caja local. Esta extensión
está diseñada, no conectada por esta entrega documental.

## Referencia funcional revisada en Xion

Se revisaron `FrmConfigurarImpresora`, `SConfiguracionImpresora`,
`ConfigurationImpresoraService` y el flujo de impresión invocado por
`FrmFacturacionPago`.

Xion aporta reglas útiles:

- descubre las impresoras instaladas en Windows;
- guarda una elección local por equipo y propósito;
- separa tirilla, carta y media carta/oficio;
- permite copias configurables;
- imprime directamente el reporte de la venta;
- utiliza la impresora de tirilla para abrir el cajón cuando corresponde.

No se traslada:

- Crystal Reports como motor de tirilla;
- formularios Windows Forms;
- enumeraciones y nombres heredados;
- una impresora única implícita para todos los documentos;
- acoplamiento entre cobrar, renderizar, imprimir y abrir cajón.

## Decisión Auraly

La configuración pertenece a la estación POS y se expresa mediante perfiles
por propósito. Los propósitos iniciales son:

- `SalesReceipt`;
- `SalesReturnReceipt`;
- `CashCountReceipt`;
- `AdministrativeReport`.

Cada perfil define:

- modo de salida;
- nombre de impresora cuando el modo es directo;
- ancho de papel de 58 u 80 mm;
- cantidad de copias;
- corte de papel;
- apertura de cajón;
- codificación;
- impresora de respaldo cuando aplique.

Los modos conectados son:

- `WindowsRaw`: conserva el nombre compatible del perfil de impresión directa,
  pero los documentos se renderizan antes de entregarse silenciosamente a la
  impresora Windows. Así QR, tablas, tipografía y espaciado no dependen de las
  variantes ESC/POS de cada fabricante. Los comandos crudos quedan reservados
  para periféricos como apertura de cajón.
- `BrowserPreview`: genera una representación HTML de ancho real, con CUFE y
  QR, la abre y presenta el diálogo de impresión. Desde allí se puede escoger
  Microsoft XPS Document Writer, PDF o una impresora normal.
- `File`: conserva el trabajo ESC/POS en disco para diagnóstico y pruebas sin
  hardware.

Microsoft XPS Document Writer y las impresoras térmicas reciben el mismo
documento renderizado cuando la aplicación instalada imprime directamente.
`BrowserPreview` se reserva para el navegador y abre el diálogo del sistema.

## Flujo

1. El POS confirma la venta y congela el snapshot.
2. La venta y la outbox quedan durables antes de imprimir.
3. El servicio de impresión recibe un `PosReceipt` derivado del snapshot.
4. El perfil activo selecciona el render y el transporte.
5. La vista previa HTML se escribe mediante archivo temporal y promoción
   atómica.
6. El lanzador abre la vista y esta invoca el diálogo de impresión.
7. Una falla de impresión no renumera ni elimina la venta; permite reimpresión
   desde el snapshot original.

## Plantilla canónica

Venta, entrada de dinero, salida de dinero, conteo de denominaciones y cierre conservan una sola
definición documental por tipo. Navegador y POS Edge son transportes: no poseen
una versión funcional distinta del reporte. En venta, la misma definición de
contenido alimenta tirilla, media carta, medio oficio y carta; el formato solo
decide tamaño, paginación y rotación.

### Versionado de reportes impresos

El catálogo canónico de plantillas vive en `PosPrintTemplateCatalog`. La versión
1 aprobada incluye `sales-invoice`, `sales-receipt`, `work-session-closure`,
`cash-entry` y `cash-exit`. El HTML conserva el código y la versión como metadatos
no visibles (`data-auraly-report` y `data-auraly-report-version`) para diagnóstico
y regresión sin agregar texto técnico a la tirilla.

`cash-entry` y `cash-exit` tienen una versión 3 activa: muestran únicamente la
sede en el encabezado, separan al responsable como dato propio, usan reglas
punteadas alrededor del valor y recuperan el espacio amplio de firma. Las
versiones 1 y 2 permanecen disponibles e inmutables para reproducir los formatos
publicados originalmente.
`work-session-closure` tiene una versión 3 activa: conserva Actividad, Totales,
Ventas a cartera —una fila por cliente y factura con su valor financiado— y
Detalle por medio de pago, y agrega secciones independientes
de entradas y salidas de efectivo con cada movimiento, responsable y valor. Los
subtotales brutos de entradas y salidas del medio `Cash` se derivan del mismo
lote de movimientos que alimenta esas secciones, por lo que cuadran por
construcción sin agregar una guarda que pueda impedir el cierre. Las versiones 1 y 2
permanecen disponibles e inmutables para reimpresiones históricas.

`credit-sale-acknowledgement` es la plantilla canónica del comprobante de
cartera que firma el cliente. Se deriva del mismo resultado transaccional de la
validación de crédito —sin una segunda consulta— y congela factura, cliente,
identificación, valor financiado, cupo restante, usuario, fecha y hora. Usa el
mismo formato e impresora del perfil `Facturas`. Se entrega siempre después de
la factura como un segundo trabajo físico: primero termina y corta la factura y
después imprime y corta el comprobante. La secuencia aplica a tirilla de 58/80
mm, media carta, media oficio y carta.

La versión 2 activa de `sales-invoice` y `sales-receipt` conserva el contenido
de la versión 1 y agrega, cuando el pago en efectivo registró un valor entregado,
`Efectivo recibido` y `Cambio` inmediatamente después del total. La versión 1
permanece disponible e inmutable para reimpresiones históricas y nunca inventa
un valor recibido a partir del importe aplicado.

La definición HTML de cierre vive únicamente en `Auraly.Pos.Printing`.
Servidor, navegador y POS Edge consumen esa misma plantilla; TypeScript solo
solicita o transporta el HTML y no conserva una segunda implementación.

Un ajuste compatible que corrija el transporte, la nitidez o el soporte de otra
impresora sin alterar contenido ni composición conserva la versión. Todo cambio
intencional en campos, jerarquía, orden, márgenes o composición aprobada crea una
nueva versión en el catálogo y sus pruebas; no se modifica silenciosamente la
versión publicada. Navegador, aplicación instalada, caja enrolada y formatos de
hoja consumen la misma versión documental: el transporte nunca crea una versión
paralela.

El adaptador térmico recorta la imagen hasta la última fila con tinta y avanza
una sola línea antes del corte parcial. Ese avance pertenece al transporte y no
a la plantilla; aumentar esa alimentación agrega una cola blanca a todos los
reportes y no crea una versión documental nueva.

Una versión publicada es inmutable y no se elimina, aunque deje de ser la activa.
La reimpresión histórica resuelve la versión congelada con el documento y no la
reemplaza por la versión vigente. Esta regla aplica también a facturación, POS y
los demás reportes fuera de tirilla que usen el catálogo canónico.

Todas las salidas operativas usan contenido con capitalización natural, títulos
de documento y sección destacados, alineación clara y jerarquía en negrita para
totales, medios de pago y valores de efectivo. La impresión directa conserva el
mismo orden, campos y composición que la representación web. Una regresión debe
comparar ambos transportes y los cuatro formatos antes de publicar.

## Alcance implementado

- selección explícita `BrowserPreview`, `File` o `WindowsRaw`;
- tirilla HTML de 58/80 mm;
- formatos documentales `HalfLetter`, `HalfLegal` y `Letter`, además de
  `Receipt`, con enrutamiento independiente por tipo de documento y formato;
- media carta y media oficio imprimen dos copias completas por hoja, cada una
  girada 90 grados dentro de su mitad y sin guía de corte imprimible;
- media oficio usa el tamaño oficio colombiano de 21,59 x 33,02 cm, no el
  tamaño Legal estadounidense de 21,59 x 35,56 cm;
- carta imprime una copia a página completa; los formatos documentales incluyen
  marca dinámica del tenant, impuestos agrupados por tarifa, todos los medios de
  pago, CUFE, QR, hora de emisión, paginación y el crédito de emisión de Auraly;
- líneas, impuestos, totales, pagos, CUFE y QR;
- apertura de vista previa;
- impresión directa de documentos renderizados y comandos ESC/POS periféricos;
- reimpresión por F6 desde el snapshot original;
- comprobante de cartera separado para ventas a crédito, usando la misma
  configuración de Facturas y un segundo trabajo de impresión;
- tirilla de conteo de denominaciones con usuario, hora, cantidades, subtotales y total;
- cierre con detalle conciliado y totalizado de entradas y salidas de efectivo;
- pruebas del contenido y escritura atómica.

## Siguiente incremento de configuración

La pantalla de configuración deberá consumir un caso de uso real de POS Edge
para descubrir impresoras y guardar los perfiles locales. Incluirá impresión
de prueba y validará capacidades antes de activar corte o cajón. No se agrega
ahora una pantalla desconectada ni una lista de impresoras simulada.

Sin Auraly POS, la estación web usa el diálogo de impresión del navegador. Con
Auraly POS instalado, POS Edge puede imprimir directamente con `WindowsRaw` y
usar balanza o cajón aunque la venta continúe online y el equipo no esté
enrolado. La vista `Periféricos` ofrece la descarga del instalador completo;
el enrolamiento posterior solo habilita el respaldo offline.

La configuración de pedidos tiene un único contrato entre la web, POS Edge y
el archivo local: `orderOutputFormat`, `orderPrinterName` y
`orderReceiptPaperWidthMillimeters`. No se aceptan alias plurales ni rutas de
compatibilidad; guardar y volver a abrir consume exactamente esos mismos campos.

Al actualizar una instalación que todavía conserva los campos locales anteriores
de tirilla y documentos, POS Edge los proyecta una sola vez sobre los nombres
canónicos por flujo al leer la configuración. La interfaz completa ambos flujos
con la única impresora instalada cuando Windows reporta exactamente una. El
archivo vuelve a escribirse únicamente al guardar explícitamente; no existe una
segunda fuente de configuración ni una migración remota por tenant.

