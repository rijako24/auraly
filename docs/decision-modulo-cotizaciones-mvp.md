# Decisión: módulo mínimo de cotizaciones

Fecha: 2026-09-09

## Resultado

Auraly incorporará `Cotizaciones` como una entrada nueva del menú de Ventas. El
primer corte debe permitir preparar una propuesta comercial, entregarla al cliente,
registrar su decisión y facturarla una sola vez desde el punto de venta. La cotización
no reserva inventario ni causa contabilidad por sí misma y no crea una segunda ruta de
precios, impuestos, clientes, numeración ni facturación.

## Referencias revisadas

- Siigo permite cliente, contacto, vendedor, fecha, moneda, productos, cantidades,
  precios, descuentos, impuestos, condiciones, adjuntos, envío, aprobación o descarte
  y conversión posterior a factura o remisión:
  https://siigonube.portaldeclientes.siigo.com/elaborar-cotizaciones/
- Alegra cubre cliente, vigencia, productos o servicios, precio, descuento, impuesto,
  cantidad, notas, envío, impresión y conversión:
  https://ayuda.alegra.com/pan/crea-tus-cotizaciones-en-af
- Odoo separa la cotización del pedido confirmado y deja plantillas, firma y pago en
  línea como capacidades adicionales:
  https://www.odoo.com/documentation/18.0/applications/sales/sales/sales_quotations/create_quotations.html

El MVP toma el núcleo repetido en esos productos y deja por fuera automatizaciones,
CRM de seguimiento, firma electrónica, pagos, plantillas avanzadas, portal y constructor
visual de PDF.

## Alcance funcional del MVP

### Navegación

- Menú `Ventas > Cotizaciones`, visible con `quotations.read`.
- Lista paginada en `/dashboard/quotations` con búsqueda por número, cliente o texto,
  filtros por estado, fecha, vencimiento y vendedor.
- Acciones `Nueva cotización`, `Ver`, `Editar`, `Enviar`, `Aceptar`, `Rechazar`,
  `Facturar en POS`, `Duplicar` e `Imprimir / PDF`, según estado y permiso.
- Creación y detalle son páginas; las confirmaciones cortas pueden ser diálogos.

### Datos obligatorios

- Tenant y negocio obtenidos del contexto autenticado; nunca del cuerpo como autoridad.
- Número consecutivo asignado al emitir, por el propietario canónico de numeración.
- Cliente, contacto opcional, vendedor opcional, fecha, fecha de vencimiento, moneda,
  asunto, condiciones comerciales y nota visible al cliente.
- Una o más líneas de producto o servicio con snapshot de código, descripción,
  unidad, cantidad, precio unitario, descuento e impuesto.
- Subtotal, descuentos, impuestos y total calculados por el motor comercial existente.
- Versión de concurrencia, creador, editor y marcas de tiempo.

La captura de productos reutiliza catálogo y el resolvedor canónico de precio: precio
exclusivo del cliente, canal, precio público y promociones según la política vigente.
La cotización congela el resultado para que una variación futura del catálogo no cambie
lo ya ofrecido. Editar o duplicar vuelve a resolver solo cuando el usuario lo solicita.

### Estados y transiciones

- `Draft`: editable, sin número comercial definitivo.
- `Sent`: emitida y entregada; conserva el snapshot.
- `Accepted` o `Rejected`: decisión registrada manualmente con fecha y actor.
- `Expired`: estado derivado cuando venció y no fue aceptada, rechazada ni facturada.
- `Invoiced`: factura creada y enlazada de forma idempotente.
- `Cancelled`: anulación administrativa, sin borrado físico.

Solo `Draft` se edita libremente. Una cotización emitida se duplica para cambiar la
oferta. `Accepted` puede facturarse una vez. El POS recupera el snapshot de la
cotización en una venta vacía y, desde ese momento, el usuario completa cobro y emisión
por el flujo canónico del POS. La emisión conserva `SourceQuotationId` como origen y
no permite dos facturas ante doble clic, reintentos o concurrencia. Si el cobro o la
emisión falla, la cotización no cambia a `Invoiced`.

### Consulta y facturación desde POS

- Acceso desde un `Panel rápido` lateral izquierdo: una pestaña angosta, redondeada y
  siempre visible se despliega sobre la interfaz sin reducir el área de venta. Al
  abrirse muestra acciones secundarias con icono, nombre, atajo y disponibilidad; al
  cerrarse vuelve a dejar solo la pestaña. La transición respeta `prefers-reduced-motion`.
- El panel reúne inicialmente `Cotizaciones`, `Abrir cajón`, `Sincronización` y
  `Ayuda de atajos`; no duplica las acciones primarias de cobro o búsqueda.
- Se abre con clic o `Ctrl+H` dentro del POS y la misma combinación o `Escape` lo
  cierra. Como `Ctrl+H` puede pertenecer al navegador, la pestaña visible es siempre
  el acceso fiable y el atajo debe validarse en navegador, PWA y escritorio antes de
  liberarlo. Si alguna superficie no permite interceptarlo, el mapa canónico asignará
  una combinación POS sin conflicto en vez de mantener un atajo parcialmente roto.
- `Cotizaciones` conserva además el atajo directo `Ctrl+Q`. No se usa `Ctrl+C` porque
  es el atajo estándar de copiar y generaría conflictos de teclado.
- Al abrir, consulta paginada en servidor con rango `Desde / Hasta`; ambos valores
  inician en el día comercial actual. Puede filtrar además por número o cliente.
- Solo lista cotizaciones facturables del tenant y negocio activos. La autorización se
  vuelve a validar al recuperar y al emitir, no solo al pintar la lista.
- Requiere conexión con el servidor. Desconectado, el acceso se muestra no disponible,
  no consulta datos y no usa caché, réplica ni cola offline de cotizaciones.
- Solo puede cargar una cotización si la venta activa está vacía. Si hay líneas, el
  usuario debe facturarlas, cancelarlas o pausarlas antes de continuar.
- Al cargarla se conservan cliente, cantidades, descripción, precio, descuentos e
  impuestos ofrecidos. El POS no vuelve a calcular el precio desde el catálogo; sí
  ejecuta sus validaciones actuales de disponibilidad y emisión.
- La referencia a la cotización viaja con el borrador activo y se consume de manera
  atómica al emitir la factura. Pausar la venta conserva esa referencia.

### Entrega al cliente

- Vista imprimible y PDF con identidad del negocio, datos del cliente, vigencia,
  líneas, totales y condiciones.
- Envío por el canal de correo transaccional ya configurado; si no existe entrega de
  correo, la emisión sigue siendo válida y el fallo queda visible y reintentable.
- Historial auditable de emisión, envíos, cambio de estado y conversión.

## Permisos

- `quotations.read`: listar, consultar e imprimir.
- `quotations.manage`: crear y editar borradores, duplicar y enviar.
- `quotations.decide`: aceptar, rechazar o cancelar.
- `quotations.invoice`: recuperar una aceptada en POS y facturarla.

Son permisos ordinarios asignables desde Roles. El rol Administrador recibe todo el
catálogo permitido en su alcance. Ningún rol operativo, incluido Cajero, recibe estos
permisos nuevos por accidente: sus asignaciones iniciales deben ser explícitas.

## Propiedad técnica

- El agregado y writer pertenecen a `Sales`; no se reutiliza `OrderDrafts` ni
  `SalesDrafts` porque representan conversaciones y ventas activas de caja.
- Clientes pertenecen a `Parties`, productos a `Catalog`, precios a `Pricing`, impuestos
  al motor tributario, archivos al almacenamiento canónico, numeración a Documents y
  entrega a notificaciones.
- Persistencia mínima: `Quotations`, `QuotationLines`, eventos/auditoría y una referencia
  nullable `SourceQuotationId` en el documento de venta (o en su relación canónica).
  Esta referencia no acopla los cálculos: aporta trazabilidad e idempotencia y debe
  tener una restricción única para permitir una sola factura por cotización en el MVP.
- Lecturas de lista usan consulta paginada por tenant y negocio. No se carga el universo
  en memoria ni se crea una proyección o worker exclusivo para este MVP.

## Criterios de aceptación

1. Dos tenants no pueden ver, mutar ni convertir cotizaciones entre sí.
2. Los totales coinciden con los motores de precio, descuento e impuesto vigentes.
3. Emitir congela el snapshot; cambios posteriores de catálogo no alteran el documento.
4. Una emisión repetida devuelve la misma factura y nunca duplica cobros, inventario ni
   documentos fiscales.
5. Una cotización no mueve inventario, caja, cartera ni contabilidad.
6. Permisos, transiciones inválidas, concurrencia y límites de longitud tienen pruebas.
7. Lista, creación, detalle, PDF y facturación funcionan en teclado y pantalla móvil.
8. El POS abre la consulta con `Ctrl+Q`, filtra inicialmente el día actual y no muestra
   ni carga cotizaciones cuando está desconectado.
9. El panel lateral se opera con teclado, conserva el foco, no tapa los controles de
   cobro y no anima cuando el sistema solicita movimiento reducido.

## Fuera del MVP

Plantillas por sector, productos opcionales, firma electrónica, pago en línea, portal
del cliente, aprobaciones internas multinivel, versiones negociadas, recordatorios
automáticos, analítica de conversión, conversión a pedido y soporte offline. Se agregan
solo después de validar uso real del flujo mínimo.
