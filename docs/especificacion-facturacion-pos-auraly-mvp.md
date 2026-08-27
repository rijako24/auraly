# Especificación funcional de Facturación POS para el MVP de Auraly

**Estado:** complemento obligatorio del diseño de Auraly Commerce  
**Fecha:** 23 de julio de 2026  
**Documentos relacionados:**

- `docs/diseno-auraly-commerce-mvp.md`
- `docs/parametros-caja-auraly-commerce-mvp.md`

Esta especificación tiene prioridad sobre cualquier recorte anterior del módulo POS. La decisión es conservar en el MVP la madurez funcional de facturación de Xion, rediseñándola para web y offline, sin copiar WinForms.

“Conservar” significa mantener la capacidad y la velocidad operativa, no reproducir las mismas ventanas, nombres técnicos ni código.

---

## 1. Objetivo

El cajero debe poder completar toda la operación normal sin abandonar la pantalla principal:

- iniciar o recuperar una venta;
- identificar cliente y vendedor;
- escanear o buscar productos;
- modificar cantidades;
- aplicar descuentos;
- eliminar líneas;
- cancelar toda la venta;
- guardar una venta temporal;
- recuperar ventas temporales;
- convertir pedidos y documentos comerciales;
- cobrar con uno o varios medios;
- vender a crédito;
- imprimir o reimprimir;
- registrar devoluciones;
- abrir el cajón;
- hacer ingresos o retiros;
- consultar existencias, cuando quiera hacerlo;
- continuar trabajando offline.

La pantalla está diseñada alrededor de lector y teclado. El mouse es opcional para la operación repetitiva.

---

## 2. Inventario verificado del formulario de Xion

Xion contiene estos accesos principales:

| Atajo Xion | Capacidad | Decisión Auraly MVP |
|---|---|---|
| F2 | Pagar | Conservar |
| F3 | Recuperar venta temporal | Conservar |
| F4 | Buscar producto | Conservar y ampliar |
| F5 | Descuentos | Conservar |
| F6 | Eliminar producto seleccionado | Conservar |
| F7 | Abono a cartera | Conservar |
| F8 | Seleccionar/crear cliente | Conservar |
| F9 | Eliminar toda la factura en curso | Conservar como cancelar borrador |
| F10 | Guardar venta temporal | Conservar |
| F11 | Entrada o salida de dinero | Conservar |
| F12 | Copia de factura | Conservar como reimpresión |
| Alt+C | Abrir cajón | Conservar |
| Alt+V | Seleccionar vendedor | Conservar |
| Ctrl+X | Cierre de sesión/turno | Conservar |
| Ctrl+Z | Informe Z | Conservar como cierre/reporte |
| Ctrl+D | Domicilios | Conservar mediante función habilitable |
| Ctrl+S | Sumar valor o auditoría | Rediseñar como ajuste autorizado |
| Alt+D | Devolución | Conservar |
| Alt+A | Generar apartado | Conservar mediante función habilitable |
| Ctrl+A | Recuperar apartado | Conservar mediante función habilitable |
| Ctrl+C | Cotización | Conservar mediante función habilitable |
| Alt+P | Recuperar pedido | Conservar; es esencial para Auraly |
| Alt+R | Recuperar remisión | Conservar mediante función habilitable |
| Ctrl+P | Productos/códigos no encontrados | Conservar como bandeja de excepciones |
| Alt+Q | Redimir puntos | Conservar mediante función habilitable |
| Ctrl+O | Observación de factura | Conservar |
| Alt+O | Observación de línea | Conservar |
| Alt+F5 | Cambiar descripción | Conservar con permiso y fotografía |
| Alt+X | Existencias en bodegas | Conservar como consulta informativa |
| Ctrl+F5 | Rentabilidad parcial | Conservar con permiso |
| Alt+B | Balanza | Conservar cuando la caja la use |
| Alt+F6 | Buscar y eliminar productos | Conservar |
| `/` | Cantidad por embalaje | Conservar mediante unidad/empaque |

Los atajos de Auraly podrán cambiar para evitar conflictos del navegador, pero cada función tendrá acceso rápido configurable y visible.

---

## 3. Estados de una venta

Se deben distinguir claramente:

```text
New
  -> InProgress
  -> Parked
  -> InProgress
  -> PaymentInProgress
  -> Confirmed
  -> FiscalPending / FiscalValidated / FiscalRejected / Contingency
```

Ramas adicionales:

```text
InProgress -> Cancelled
Confirmed -> PartiallyReturned
Confirmed -> FullyReturned
Confirmed -> CorrectedByCreditNote
```

Una venta confirmada no vuelve a `InProgress` y no se elimina. Cualquier corrección posterior se hace mediante devolución, nota crédito o movimiento compensatorio.

---

## 4. Venta temporal

En Auraly se llamará **venta guardada** o **venta en espera** en la interfaz. Internamente puede usarse `ParkedSale`.

### 4.1 Guardar

El cajero puede guardar explícitamente la venta con un atajo. Además existe borrador automático para recuperación ante cierre o falla.

Se conserva:

- líneas;
- cantidades y unidades;
- precios;
- descuentos;
- impuestos calculados;
- cliente;
- vendedor;
- observación general;
- observaciones por línea;
- descripción modificada;
- pedido/remisión de origen;
- bodega;
- caja;
- usuario;
- fecha;
- total;
- versión de precios e impuestos;
- origen online/offline.

Guardar temporalmente:

- no consume numeración fiscal;
- no envía a DIAN;
- no registra pagos;
- no crea cartera;
- no registra salida definitiva;
- no aparece como venta en reportes;
- puede reservar inventario solamente si el negocio habilita esa política.

### 4.2 Identificación

Cada venta guardada tiene:

- identificador interno;
- código corto visible;
- cliente o “sin identificar”;
- cajero;
- caja;
- fecha y hora;
- número de líneas;
- unidades;
- total estimado;
- observación;
- estado de sincronización;
- vencimiento;
- origen.

### 4.3 Recuperar

La ventana permite buscar por:

- código;
- cliente;
- identificación;
- cajero;
- caja;
- fecha;
- rango de total;
- observación.

Muestra encabezado y vista previa de líneas antes de recuperar.

Reglas:

- si hay otra venta en curso, se exige guardarla o cancelarla;
- una venta no puede quedar abierta simultáneamente en dos cajas;
- al recuperarla se adquiere un bloqueo con vencimiento;
- un supervisor puede liberar una venta bloqueada;
- una venta vencida requiere permiso, según configuración;
- se puede configurar máximo de ventas guardadas por caja o por día;
- recuperar no recalcula silenciosamente los precios;
- si el usuario decide actualizar precios, Auraly muestra las diferencias y audita la decisión;
- offline permite recuperar las ventas disponibles localmente;
- las ventas guardadas en otra caja requieren haber sido sincronizadas.

### 4.4 Eliminar una venta guardada

Cancelar una venta guardada exige:

- permiso;
- motivo;
- observación configurable;
- usuario y dispositivo;
- fecha;
- auditoría de encabezado y líneas.

No se borra físicamente.

---

## 5. Captura y modificación de productos

### 5.1 Escaneo

Después de cada lectura:

1. se resuelve el código;
2. se agrega una línea nueva al final, incluso si el producto ya existe;
3. se recalculan línea y factura;
4. se confirma con sonido/color;
5. la grilla baja hasta la línea nueva sin sacar el foco del lector, que queda
   listo para el siguiente producto.

No se abre una ventana por cada producto.

El comportamiento no varía por caja: una lectura repetida también crea una línea
independiente. La cantidad solo se modifica mediante edición explícita o por el
factor/cantidad de la captura actual.

### 5.2 Edición en grilla

El cajero puede, según permisos:

- cambiar cantidad;
- cambiar unidad o empaque;
- cambiar precio;
- editar descuento;
- editar descripción;
- agregar observación;
- seleccionar vendedor por línea;
- capturar lote, serial o vencimiento;
- eliminar línea.

Cambiar cualquier valor recalcula inmediatamente:

- valor bruto;
- descuento;
- base;
- impuestos;
- total;
- ahorro;
- unidades;
- peso;
- costo;
- margen visible;
- total general;
- cambio pendiente durante el pago.

El servidor vuelve a calcular al confirmar.

### 5.3 Productos no codificados

Se permite una línea manual solo con permiso y tipo de producto apropiado. Antes de cobrar se muestra una revisión de códigos no encontrados o productos no codificados.

La línea manual guarda:

- descripción;
- cantidad;
- unidad;
- precio;
- impuesto;
- motivo;
- usuario autorizador.

No se crea automáticamente un producto maestro desde la caja.

---

## 6. Búsqueda de productos

La búsqueda no se limita al nombre. Debe resolver por:

1. código de barras exacto;
2. código alterno;
3. código interno/SKU;
4. identificador numérico;
5. referencia;
6. nombre;
7. descripción corta;
8. descripción larga;
9. alias comercial;
10. marca/casa comercial;
11. categoría o familia;
12. código del proveedor;
13. palabras parciales.

La búsqueda textual será:

- insensible a mayúsculas y acentos;
- tolerante a orden de palabras;
- priorizada por coincidencia exacta;
- rápida mientras se escribe;
- paginada;
- disponible offline sobre un índice local.

Resultados:

- código principal;
- descripción;
- referencia;
- marca;
- unidad/empaque;
- precio vigente;
- precio promocional;
- existencia informativa en la bodega;
- existencia en otras bodegas bajo acción secundaria;
- estado;
- imagen opcional.

El cajero selecciona con flechas y Enter. Al agregar, el foco vuelve al receptor de escaneo.

### 6.1 Resolución determinista

Si un código identifica exactamente un producto y una unidad, se agrega sin abrir búsqueda.

Si hay más de una coincidencia válida, se abre selector. No se escoge arbitrariamente.

Si no hay coincidencia:

- se muestra error corto;
- se conserva el código en la bandeja de no encontrados;
- se permite buscar;
- se permite línea manual solo con autorización;
- el foco queda listo para continuar.

### 6.2 Invariante de línea, desplazamiento y foco

Toda adición exitosa de producto crea una línea nueva al final de la pantalla de
venta. La existencia previa del mismo producto en el borrador no autoriza
incrementar, reutilizar, agrupar ni consolidar esa línea. Cada lectura o selección
conserva su propia identidad de línea, cantidad, precio, impuesto, promoción,
vendedor, origen y trazabilidad.

Después de cada adición, la grilla debe desplazar su contenedor hasta que la línea
nueva quede completamente visible y debe conservar el foco DOM en el receptor del
lector, limpio y listo para la siguiente captura. La fila puede quedar resaltada
o seleccionada visualmente, pero no recibe el foco de teclado. Esta secuencia es
atómica desde la perspectiva del cajero: agregar, recalcular, mostrar la última
línea y continuar escaneando sin clic.

La regla aplica por igual a código exacto, búsqueda, código alterno, balanza,
empaque, línea manual autorizada y cargas que agreguen varias líneas. En un lote,
cada producto agregado mantiene su línea y al finalizar se hace visible la última.
Si una validación o autorización abre una interacción bloqueante, esta gestiona el
foco accesiblemente y lo devuelve al receptor al terminar. Si la captura falla o
se cancela, no se crea línea y el receptor también queda listo.

---

## 7. Descuentos

El MVP conserva descuentos por producto y documento:

- porcentaje por línea;
- valor por línea;
- porcentaje general distribuido;
- valor general distribuido;
- promoción automática;
- descuento por cantidad;
- precio de lista/evento;
- descuento autorizado durante el pago cuando el caso lo requiera.

Reglas:

- cada producto muestra descuento aplicado y origen;
- cambiar cantidad vuelve a evaluar descuentos por cantidad;
- descuentos automáticos y manuales tienen reglas explícitas de acumulación;
- se define máximo por rol;
- superar el máximo solicita supervisor;
- se puede configurar precio mínimo o margen mínimo;
- el cajero no ve costo ni margen sin permiso;
- el impuesto se recalcula sobre la base correcta;
- el total del XML, PDF, venta y pago debe coincidir;
- cada descuento conserva usuario, autorizador, motivo y regla;
- retirar un descuento restaura el cálculo anterior;
- reintentar no aplica el descuento dos veces.

La pantalla de Xion permite editar porcentaje por producto y valida rangos permitidos. Auraly conserva esa capacidad y añade edición directa de una línea para mayor velocidad.

---

## 8. Eliminar producto y cancelar factura

### 8.1 Eliminar una línea del borrador

Debe existir:

- acción sobre la fila;
- atajo;
- búsqueda de línea para facturas grandes;
- opción de eliminar una unidad o toda la línea;
- confirmación configurable;
- motivo;
- autorización según rol;
- recalculo inmediato;
- auditoría.

Una línea eliminada se marca en el historial del borrador. No aparece en el documento final, pero sí en auditoría.

### 8.2 Cancelar toda la factura en curso

La acción:

- exige permiso;
- puede exigir observación según la caja;
- muestra cantidad de líneas y total;
- solicita confirmación clara;
- cancela el borrador completo;
- libera cualquier reserva;
- conserva auditoría;
- limpia la pantalla;
- devuelve el foco al escáner.

### 8.3 Documento confirmado

“Eliminar factura” nunca se ofrece para una venta confirmada o documento fiscal emitido.

Se ofrecen:

- devolución parcial;
- devolución total;
- nota crédito;
- corrección fiscal permitida;
- reversión de pago;
- anulación comercial solo mediante estados y movimientos auditados.

---

## 9. Clientes y vendedores

Desde el POS se puede:

- usar consumidor final;
- buscar por identificación, nombre, teléfono o correo;
- crear cliente rápido;
- completar datos fiscales;
- seleccionar sucursal/dirección;
- consultar cupo y cartera;
- seleccionar vendedor;
- exigir vendedor según caja;
- usar vendedor por línea si está habilitado.

Crear cliente rápido no debe sacar al cajero del flujo. Los datos mínimos dependen del tipo de documento y la obligación fiscal.

---

## 10. Medios de pago

Xion contempla:

- efectivo;
- crédito/cartera;
- tarjeta débito;
- tarjeta crédito;
- bono;
- bono por devolución;
- bono de apartado;
- bono de puntos;
- cheque al día;
- cheque posfechado;
- transferencia;
- consignación;
- retención;
- descuento;
- domicilio;
- propina en los flujos que aplica.

Auraly usará un catálogo configurable de medios. El MVP preserva el motor capaz de combinarlos, aunque cada negocio habilite solo los que utilice.

### 10.1 Medios esenciales habilitados inicialmente

- efectivo;
- tarjeta débito;
- tarjeta crédito;
- transferencia;
- consignación;
- crédito/cartera;
- bono o vale;
- saldo por devolución;
- cheque;
- retención para clientes habilitados;
- pago mixto.

Apartados, puntos, domicilios, cheques posfechados y propina se activan por capacidad del negocio.

### 10.2 Pago mixto

Una venta admite varias filas de pago:

```text
Efectivo 50.000
Tarjeta  80.000
Total   130.000
```

La pantalla muestra siempre:

- total;
- pagado;
- faltante;
- cambio;
- medio activo;
- referencia requerida;
- validaciones pendientes.

Reglas:

- el cambio se entrega normalmente contra efectivo;
- no se confirma si falta dinero, salvo saldo a crédito válido;
- no se registra un valor negativo;
- pagos electrónicos guardan autorización/referencia;
- crédito valida caja, permiso, cliente, cupo y estado de cartera;
- retenciones guardan concepto, base y tasa;
- bonos no se consumen dos veces;
- pagos que requieren verificación online muestran su estado real;
- cada pago tiene idempotency key;
- al fallar la confirmación no se duplican cobros;
- offline deshabilita o deja pendiente cualquier medio que no pueda verificarse con seguridad.

### 10.3 Venta a crédito

El pago `Credit` crea:

- cuenta por cobrar;
- una o más cuotas;
- vencimientos;
- valor financiado;
- saldo;
- relación con venta y factura;
- aplicación de abonos posteriores.

Los pagos parciales pueden combinar efectivo y crédito.

---

## 11. Pedidos y documentos relacionados

Desde facturación se puede recuperar:

- pedido de Auraly;
- remisión;
- cotización;
- apartado;
- venta guardada.

Reglas comunes:

- no duplicar conversión;
- conservar documento origen;
- mostrar líneas y diferencias;
- permitir conversión parcial cuando la capacidad lo admita;
- bloquear edición de líneas que por negocio deban permanecer vinculadas;
- actualizar el estado del origen;
- operar idempotentemente;
- manejar documentos sincronizados offline.

Recuperar pedidos es obligatorio en el MVP porque conecta la conversación de Auraly con el POS.

Cotizaciones, apartados, remisiones y domicilios deben permanecer en el modelo y la navegación bajo feature flags. Su activación comercial puede hacerse por negocio sin reescribir facturación.

---

## 12. Otras capacidades preservadas

### 12.1 Observaciones

- observación general;
- observación por línea;
- motivo de eliminación;
- motivo de devolución;
- impresión configurable;
- inclusión fiscal solo cuando corresponda.

### 12.2 Consulta de existencias

La caja puede consultar:

- existencia de su bodega;
- otras bodegas;
- última sincronización;
- reservada;
- disponible estimada.

La consulta es informativa y no interrumpe la captura. La política de negativos pertenece a la caja.

### 12.3 Rentabilidad

Usuarios autorizados pueden consultar:

- costo;
- utilidad parcial;
- margen por línea;
- margen total;
- impacto del descuento.

### 12.4 Copia y reimpresión

- buscar factura;
- ver estado fiscal;
- reimprimir;
- marcar “copia” cuando corresponda;
- auditar quién reimprimió;
- no volver a emitir ni consumir numeración.

### 12.5 Caja

- abrir cajón con auditoría;
- ingreso de efectivo;
- retiro;
- abono a cartera;
- cierre de turno;
- informe de cierre/Z;
- máximo de efectivo;
- diferencia de arqueo.

---

## 13. Diseño web

### 13.1 Pantalla principal

Zonas:

1. estado de caja, red, sincronización y DIAN;
2. cliente, vendedor y documento origen;
3. receptor de código y búsqueda;
4. grilla;
5. unidades, ahorro, subtotal, impuestos y total;
6. acciones rápidas;
7. indicadores de venta guardada y permisos.

### 13.2 Paleta de comandos

Además de botones y atajos, una paleta permite escribir:

```text
guardar
recuperar
descuento
cliente
eliminar
devolver
pagar
```

Esto facilita descubrir funciones sin memorizar todas las teclas.

### 13.3 Continuidad del foco

Después de:

- agregar;
- eliminar;
- aplicar descuento;
- cerrar búsqueda;
- cambiar cliente;
- guardar una venta;
- terminar un cobro;

el foco vuelve al punto correcto, normalmente al escáner. Ningún recálculo debe desmontar la grilla y perder la celda activa.

---

## 14. Modelo de datos adicional

### `SaleDrafts`

- `Id`
- `TenantId`
- `BusinessId`
- `BranchId`
- `CashRegisterId`
- `WarehouseId`
- `CashSessionId`
- `CustomerId`
- `SellerId`
- `Status`
- `ShortCode`
- `SourceType`
- `SourceDocumentId`
- `Notes`
- `PricingVersion`
- `TaxRuleVersion`
- `ConfigurationVersion`
- `CreatedAt`
- `UpdatedAt`
- `ExpiresAt`
- `LockedByDeviceId`
- `LockExpiresAt`
- `SyncStatus`
- `RowVersion`

### `SaleDraftLines`

- producto y fotografía;
- código leído;
- unidad y factor;
- cantidad;
- precio de lista;
- precio aplicado;
- descuentos y orígenes;
- impuestos;
- costo capturado;
- observación;
- vendedor;
- lote/serial;
- documento origen;
- orden;
- estado;
- auditoría.

### `SaleDraftAudit`

Registra:

- línea agregada;
- cantidad cambiada;
- precio cambiado;
- descuento aplicado/retirado;
- línea eliminada;
- cliente/vendedor cambiado;
- venta guardada;
- recuperada;
- cancelada;
- supervisor autorizador.

Los borradores se transforman en `Sales` dentro de una operación idempotente. No se reutiliza el mismo registro mutable como venta definitiva.

---

## 15. Offline

Offline deben funcionar:

- escaneo;
- búsqueda local;
- cantidades y recálculo;
- descuentos dentro de permisos descargados;
- eliminación;
- cancelación;
- guardar y recuperar ventas locales;
- cliente local o creación pendiente;
- efectivo;
- crédito dentro de la política;
- medios no integrados configurados;
- impresión;
- salida de inventario;
- auditoría;
- sincronización posterior.

Consideraciones:

- las ventas guardadas locales se sincronizan con UUID;
- la recuperación usa bloqueo local y luego se concilia;
- una autorización offline tiene credencial o PIN seguro y caducidad;
- los límites de descuento y permisos están firmados/versionados;
- un medio que exige autorización remota no se muestra como aprobado sin obtenerla;
- la venta confirmada es durable antes de imprimir;
- reiniciar el equipo no pierde la venta ni el pago.

---

## 16. Permisos mínimos

- `pos.sale.pay`
- `pos.sale.park`
- `pos.sale.recover`
- `pos.sale.cancel`
- `pos.line.delete`
- `pos.line.change-price`
- `pos.line.change-description`
- `pos.discount.apply`
- `pos.discount.override-limit`
- `pos.negative-stock.override`
- `pos.customer.create`
- `pos.credit.sell`
- `pos.credit.override-limit`
- `pos.return.create`
- `pos.drawer.open`
- `pos.cash.in`
- `pos.cash.out`
- `pos.receipt.reprint`
- `pos.profit.view`
- `pos.parked-sale.recover-expired`
- `pos.parked-sale.unlock`

Autorizaciones de supervisor guardan usuario autorizador; nunca solo un booleano.

---

## 17. Criterios de aceptación

### Venta guardada

- se guarda con teclado;
- desaparece de la venta actual;
- se encuentra por código, cliente o fecha;
- muestra vista previa;
- se recupera completa;
- no consume número fiscal ni inventario definitivo;
- sobrevive reinicio;
- no se edita en dos cajas simultáneamente.

### Productos

- un código exacto agrega sin modal;
- cada adición crea una línea nueva aunque el producto ya exista en la venta;
- se busca por código, referencia y descripción como mínimo;
- también se indexan códigos alternos y alias;
- la grilla hace scroll hasta la última línea sin sacar el foco del escáner;
- una lectura repetida no incrementa ni agrupa automáticamente otra línea;
- cambiar cantidad recalcula todo;
- empaque aplica conversión correcta.

### Descuentos

- se aplica por línea y factura;
- valida límite;
- solicita supervisor si corresponde;
- recalcula impuestos y total;
- queda auditado;
- coincide en venta, XML, PDF y reporte.

### Eliminación

- una línea se elimina con atajo;
- factura grande permite buscar la línea;
- se pide motivo cuando está configurado;
- cancelar toda la venta es una acción distinta;
- documentos confirmados no se eliminan;
- auditoría conserva qué se retiró.

### Pagos

- se mezclan al menos efectivo, tarjeta, transferencia y crédito;
- se muestra faltante/cambio;
- se guarda referencia;
- crédito crea CxC;
- doble Enter o reintento no duplica;
- offline aplica restricciones reales.

### Operación

- las funciones principales son utilizables sin mouse;
- existe ayuda visible de atajos;
- el foco se conserva;
- permisos se validan en servidor y cliente;
- las funciones deshabilitadas por negocio no estorban la pantalla;
- pedido de Auraly se convierte una sola vez;
- reimpresión no genera una factura nueva.

---

## 18. Decisión de alcance

El módulo de facturación del MVP tendrá **paridad funcional operativa** con Xion en:

- captura;
- búsqueda;
- modificación;
- descuentos;
- eliminación;
- ventas temporales;
- clientes y vendedores;
- pagos;
- crédito;
- caja;
- devoluciones;
- pedidos;
- observaciones;
- consulta;
- reimpresión;
- permisos;
- offline.

Apartados, cotizaciones, remisiones, domicilios, puntos, bonos especializados, balanza y cheques posfechados se conservarán en el diseño y motor mediante capacidades activables. Si alguno es usado por el primer negocio piloto, su interfaz se incluye antes de declarar ese piloto terminado.

El MVP no debe salir con un POS “bonito” pero operacionalmente inferior al sistema que reemplaza. La referencia de éxito es que un cajero experimentado pueda atender la misma fila, con menos pasos y sin perder ninguna acción esencial.
