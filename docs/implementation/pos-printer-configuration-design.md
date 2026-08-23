# Configuración de impresión POS

Fecha de cierre: 2026-07-29

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

- `WindowsRaw`: entrega bytes ESC/POS a una impresora térmica Windows. Es el
  modo de producción para una caja enrolada con hardware compatible.
- `BrowserPreview`: genera una representación HTML de ancho real, con CUFE y
  QR, la abre y presenta el diálogo de impresión. Desde allí se puede escoger
  Microsoft XPS Document Writer, PDF o una impresora normal.
- `File`: conserva el trabajo ESC/POS en disco para diagnóstico y pruebas sin
  hardware.

Microsoft XPS Document Writer no debe recibir bytes ESC/POS: es una impresora
gráfica. Por eso Auraly usa `BrowserPreview` para XPS y conserva
`WindowsRaw` para una térmica compatible.

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

## Alcance implementado

- selección explícita `BrowserPreview`, `File` o `WindowsRaw`;
- tirilla HTML de 58/80 mm;
- líneas, impuestos, totales, pagos, CUFE y QR;
- apertura de vista previa;
- impresión ESC/POS existente;
- reimpresión por F6 desde el snapshot original;
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

