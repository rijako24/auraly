# Auditoría funcional consolidada de Auraly Commerce MVP

**Estado:** auditoría histórica de descubrimiento; no define prevalencia sobre
las decisiones canónicas posteriores.

## 1. Propósito y prevalencia

Este documento consolida la auditoría realizada sobre:

- Xion WinForms;
- Motor Principal y Motor Cajas;
- Pedidos OK;
- Xion Web;
- el módulo actual de Pedidos de Auraly;
- los documentos de diseño creados durante este análisis.

Su objetivo es identificar comportamientos importantes que todavía no estaban explícitos, evitar copiar complejidad que no aplica y establecer el alcance funcional real del MVP.

En caso de contradicción prevalecen `AGENTS.md`,
`docs/invariantes-arquitectonicas-auraly.md`,
`docs/mapa-motores-flujos-y-extensiones.md` y la decisión propietaria vigente del
módulo. La fecha por sí sola no convierte esta auditoría en fuente de verdad.

---

## 2. Correcciones de alcance confirmadas

### 2.1. No aplican al MVP

- listas de precios heredadas de Xion;
- lotes;
- seriales;
- conversiones de productos;
- producción;
- redondeos comerciales configurables;
- retenciones;
- fletes y descargues;
- apartados;
- cotizaciones;
- remisiones;
- domicilios;
- puntos;
- bonos especializados;
- cheques posfechados;
- contabilidad general completa;
- nómina;
- órdenes de compra como módulo independiente;
- rutas, GPS, visitas y metas de vendedores;
- mapas de vendedores;
- comisiones avanzadas;
- Pareto, temporada y rotación avanzada;
- aduana y cargue de mercancía;
- concesiones;
- préstamos;
- cruces de cuentas;
- financiación por cuotas e intereses.

Excluirlos del MVP no significa borrar cualquier dato histórico necesario para migrar. Significa que no se construirán reglas, pantallas ni tablas operativas nuevas para esas capacidades.

### 2.2. Promociones

Los `Eventos` de Xion equivalen conceptualmente al módulo Promociones ya existente en Auraly.

No se crea `Auraly.*.Events` para descuentos comerciales. La migración debe absorber únicamente reglas útiles dentro de:

```text
Auraly.Domain.Promotions
Auraly.Application.Promotions
Auraly.Infrastructure.Promotions
Auraly.Contracts.Promotions
```

### 2.3. Precio sin listas heredadas

Excluir listas no significa eliminar el precio.

El MVP utilizará:

- un precio efectivo de venta por producto y negocio;
- una posible sobreescritura por sucursal únicamente si Auraly ya la necesita;
- promociones;
- historial de cambios de precio;
- snapshot del precio aplicado en cada línea.

La caja sincroniza el precio efectivo, no el modelo complejo de listas y canales de Xion.

### 2.4. Cierre renombrado

La operación se mostrará como:

```text
Arqueo de caja
```

El arqueo compara valores esperados y contados. Al confirmarlo, cierra la sesión de caja. Internamente existirán los conceptos separados:

- sesión de caja;
- arqueo;
- cierre de sesión.

---

## 3. Hallazgos principales que faltaban

La revisión confirmó que los huecos más importantes eran:

1. ciclo de vida formal de todos los documentos;
2. anulación y reversión, no eliminación de documentos confirmados;
3. costo de inventario y utilidad real;
4. apertura, movimientos, arqueo y cierre de caja;
5. aplicación de pagos a cuentas por cobrar y pagar;
6. concurrencia entre cajas, pedidos y existencias;
7. numeración operativa y numeración DIAN;
8. impresión, reimpresión y periféricos;
9. permisos por acción, bodega y negocio;
10. auditoría antes/después;
11. reconciliación de operaciones del motor;
12. importación de saldos iniciales;
13. manejo de fecha operativa y zona horaria;
14. estados de error y reintento;
15. restricciones de precisión monetaria y cantidades;
16. modelo web de documentos temporales;
17. relación exacta entre documento origen y documento resultante;
18. comportamiento parcial de inventarios, traslados y devoluciones;
19. forma de entregar una venta offline sin confundirla con una factura DIAN aceptada;
20. operación del motor cuando un lote tiene éxitos y errores parciales.

---

## 4. Contrato transversal de todos los documentos

Todos los documentos operativos compartirán conceptos, no una entidad gigante.

### 4.1. Identificación

- nuevo ID interno Auraly;
- número humano independiente;
- tenant;
- empresa;
- negocio;
- sucursal;
- bodega cuando aplique;
- caja cuando aplique;
- dispositivo;
- usuario;
- fecha operativa;
- fecha UTC de creación;
- origen;
- `CorrelationId`;
- `IdempotencyKey`;
- versión de concurrencia.

### 4.2. Estados base

```text
Draft
ReadyToProcess
Processing
Posted
Rejected
Voided
Reversed
```

Cada módulo puede agregar estados, pero debe mapearlos al ciclo común.

### 4.3. Reglas

- Un borrador puede editarse o eliminarse.
- Un documento confirmado no se elimina físicamente.
- Un confirmado se anula o revierte con motivo, permiso y documento compensatorio.
- Un error externo no borra el documento interno.
- Toda transición queda auditada.
- Un reintento con la misma idempotencia devuelve el mismo resultado.
- El servidor calcula el resultado definitivo.
- Las integraciones externas ocurren después del commit mediante outbox.

### 4.4. Snapshots

Cada documento conserva los datos usados al confirmarse:

- nombre e identificación del tercero;
- descripción del producto;
- código capturado;
- unidad;
- precio;
- costo;
- impuesto;
- promoción;
- configuración relevante.

Los cambios posteriores en maestros no modifican documentos históricos.

---

## 5. Motor servidor de documentos

### Obligatorio

- registro durable de operación;
- procesador por tipo documental;
- idempotencia;
- transacción SQL;
- bloqueo o `rowversion`;
- auditoría;
- outbox;
- reintentos;
- errores clasificados;
- resultado por ítem en procesos masivos;
- consulta de progreso;
- reconciliación.

### Importante

El motor no puede depender de la UI ni contener reglas en un único `switch`.

Cada módulo implementa su procesador. DocumentProcessing controla el pipeline.

### Reconciliación

Debe existir una bandeja técnica para detectar:

- operación recibida sin documento;
- documento confirmado sin movimiento de inventario;
- factura sin efecto de caja o cartera;
- outbox pendiente;
- documento fiscal sin respuesta;
- operación offline duplicada;
- pedido marcado facturado sin vínculo a factura.

Esta bandeja es parte del MVP operativo, aunque sea accesible solo a soporte.

---

## 6. Catálogo de productos

### Obligatorio

- nuevo `ProductId`;
- referencia o SKU;
- descripción corta y larga;
- múltiples códigos de barras;
- código principal;
- unidad de medida;
- producto por peso;
- configuración de balanza;
- impuesto de venta;
- impuesto de compra si las entradas lo requieren;
- precio efectivo de venta;
- costo vigente;
- categoría;
- marca opcional;
- producto activo;
- permite vender;
- permite comprar/recibir;
- maneja inventario;
- permite traslado;
- permite ajuste;
- permite devolución;
- ubicación informativa por bodega;
- existencias online por bodega;
- auditoría de cambios.

### Comportamientos omitidos

- conflicto de código de barras entre productos;
- cambio de código principal;
- desactivación sin borrar historia;
- producto de servicio que no mueve inventario;
- producto pesado con cantidades decimales;
- precisión y cantidad mínima;
- producto no vendible pero visible históricamente;
- cambio de impuesto o precio que notifica cajas;
- clonar producto como comodidad web;
- validación de que no se desactive un producto incluido en una operación en curso.

### Excluido

- conjuntos y producción;
- lotes y seriales;
- puntos;
- comisiones;
- listas/canales;
- costos por flete;
- múltiples niveles de utilidad de Xion.

### Diseño web

Pestañas:

```text
General
Códigos
Precio e impuestos
Comportamiento
Bodegas
Auditoría
```

No se replica el formulario con decenas de grillas simultáneas.

---

## 7. Promociones

### Obligatorio

- vigencia desde/hasta;
- estado;
- condición;
- beneficio;
- productos incluidos;
- prioridad;
- combinable o excluyente;
- límite por línea/documento;
- versión;
- aplicación automática;
- snapshot de la promoción aplicada;
- explicación de por qué se aplicó o no.

### Offline

La caja recibe la proyección ejecutable de promociones que le aplican. El servidor vuelve a validarlas al procesar.

### Omisión importante

Debe definirse una regla determinista cuando dos promociones compiten. No se puede depender del orden casual en que una consulta las devuelva.

---

## 8. Inventario y costo

### 8.1. Dos representaciones

```text
InventoryBalances
```

Mantiene el saldo actual para consultas rápidas.

```text
InventoryLedger
```

Mantiene movimientos inmutables.

Cada confirmación actualiza ambos dentro de la misma transacción.

### 8.2. Costo

Sin costo consistente no se puede calcular utilidad.

Decisión recomendada para el MVP:

```text
Costo promedio ponderado perpetuo por producto y negocio
```

Cada salida conserva `UnitCostSnapshot` y `TotalCostSnapshot`.

Un traslado no genera utilidad y conserva el costo. Una devolución de venta revierte al costo de la venta original cuando sea posible.

### 8.3. Tipos mínimos de movimiento

- saldo inicial;
- entrada de mercancía;
- venta;
- devolución de venta;
- devolución de compra;
- traslado salida;
- traslado entrada;
- ajuste positivo;
- ajuste negativo;
- avería;
- reversión.

### 8.4. Negativos

- La política de venta negativa pertenece a la caja.
- La caja que bloquea valida online al agregar o cambiar cantidad.
- El motor valida nuevamente al confirmar.
- Traslados, averías y devoluciones de compra no crean negativos silenciosamente.
- Una sobreescritura administrativa exige permiso y auditoría.

### 8.5. Omisiones importantes

- movimientos con fecha operativa y fecha real;
- reconstrucción de saldo;
- detección de saldo diferente a la suma del kardex;
- reserva de concurrencia;
- bloqueo optimista;
- reversión ligada al movimiento original;
- saldo inicial para puesta en marcha;
- reporte de inventario negativo;
- valoración de inventario.

---

## 9. Conteo de inventario

### Flujo

```text
Draft
Counting
PendingReview
Posted
Cancelled
Reversed
```

### Obligatorio

- seleccionar bodega;
- conteo general o parcial;
- snapshot de existencia teórica al iniciar;
- escaneo continuo;
- productos repetidos incrementan o enfocan según configuración;
- cantidad contada;
- diferencia;
- motivo;
- guardar temporal;
- recuperar;
- reconteo;
- revisión;
- confirmar ajustes;
- reporte de diferencias;
- usuario que contó y usuario que aprobó.

### Concurrencia

No es correcto bloquear toda una bodega durante horas.

El sistema registra movimientos ocurridos mientras se cuenta y recalcula la existencia teórica al cerrar. Si la diferencia cambió materialmente, exige revisión.

---

## 10. Entradas de mercancía

### Obligatorio

- borrador;
- recuperar temporal;
- proveedor;
- bodega;
- fecha del documento del proveedor;
- número de factura del proveedor;
- fecha de vencimiento;
- contado o crédito;
- productos y cantidades;
- costo unitario;
- descuento comercial simple si aplica;
- impuesto;
- totales;
- observaciones;
- prevención de factura duplicada por proveedor;
- confirmación;
- movimiento de inventario;
- actualización del costo promedio;
- creación de cuenta por pagar cuando sea crédito;
- pago inmediato cuando sea contado;
- anulación/reversión;
- informe y auditoría.

### Fuera

- retenciones;
- fletes;
- descargues;
- lotes;
- seriales;
- orden de compra;
- diferencias contables automáticas;
- notas contables.

### Omisiones importantes

- una entrada no debe duplicarse por doble envío;
- no puede editarse después de confirmada;
- una reversión debe afectar inventario, costo y CxP;
- el documento del proveedor debe ser único en el ámbito correcto;
- una entrada parcialmente capturada debe autoguardarse;
- debe definirse qué ocurre si el producto no existe;
- debe validarse el impuesto de compra;
- cantidades y valores usan `decimal`, nunca `double`.

---

## 11. Traslados

### Modalidades del MVP

```text
Immediate
DispatchAndReceive
```

### Flujo de dos pasos

```text
Draft
Dispatched
PartiallyReceived
Received
Cancelled
Reversed
```

### Obligatorio

- bodega origen y destino diferentes;
- responsable de origen;
- responsable de destino;
- motivo;
- fecha;
- observación;
- captura por lector;
- cantidad;
- disponibilidad en origen;
- guardar temporal;
- despacho;
- recepción;
- diferencias;
- recepción parcial;
- pendientes;
- informe;
- auditoría.

### Efecto

En traslado inmediato se actualizan ambas bodegas en una transacción.

En despacho/recepción:

- despacho reduce origen y aumenta tránsito;
- recepción reduce tránsito y aumenta destino;
- diferencias requieren motivo y autorización.

### Fuera

- lotes;
- seriales;
- traslado sugerido avanzado;
- conversión automática a entrada de mercancía.

---

## 12. Averías

### Flujo

```text
Draft
Posted
PendingResolution
Resolved
Cancelled
Reversed
```

### Obligatorio

- bodega;
- fecha;
- motivo;
- proveedor opcional;
- productos;
- cantidad;
- costo snapshot;
- observación;
- guardar temporal;
- confirmar;
- efecto de inventario;
- consulta;
- reversión;
- informe;
- auditoría.

### Destino físico

Una avería no debe desaparecer del inventario sin indicar destino:

```text
MoveToDamagedStock
WriteOff
PendingSupplierClaim
```

Para el primer MVP se pueden habilitar `MoveToDamagedStock` y `WriteOff`. El reclamo y cambio con proveedor puede quedar preparado.

### Excluido

- lotes;
- seriales;
- nota contable automática;
- cambio complejo producto por producto con proveedor.

---

## 13. Pedidos

### Vista propia

Pedidos conserva:

- listado;
- filtros por número, cliente, vendedor, producto, fecha y estado;
- detalle;
- anulación con permiso;
- selección múltiple;
- botón Facturar;
- progreso por pedido;
- relación con factura.

### Desde Facturación

- se abre la vista en modo recuperación;
- consulta siempre online;
- selección única;
- recupera un pedido;
- crea un claim;
- carga cantidades pendientes;
- vuelve al borrador POS;
- no factura automáticamente.

### Facturación múltiple

- solo desde la vista propia;
- uno o varios seleccionados;
- una factura por pedido;
- cada pedido se procesa independientemente;
- no se consolidan;
- no se detiene todo el lote por el primer error;
- muestra éxitos, omitidos y fallos;
- cada pedido tiene idempotencia propia.

### Omisiones importantes

- no asumir efectivo como medio de pago del pedido;
- exigir condición de pago completa antes del lote;
- validar datos fiscales;
- validar inventario;
- impedir doble facturación;
- conservar cantidades pendientes si se permite facturación parcial;
- liberar claims vencidos;
- registrar pedido anulado y motivo;
- no reutilizar ID legado.

---

## 14. Facturación POS

### Apertura

Antes de vender se valida:

- dispositivo aprovisionado;
- caja asignada;
- bodega;
- usuario;
- sesión de caja abierta;
- catálogo local válido;
- configuración;
- resolución fiscal cuando aplica.

### Captura

- lector continuo;
- balanza;
- búsqueda por código, referencia, nombre y alias;
- producto duplicado incrementa o enfoca según configuración;
- cambiar cantidad;
- cambiar precio solo con permiso;
- descuento por línea y documento;
- eliminar línea;
- observación de línea y encabezado;
- cliente;
- vendedor opcional u obligatorio;
- recálculo inmediato;
- validación temprana de inventario;
- producto desconocido con mensaje claro;
- foco listo para siguiente lectura.

### Temporales

- autoguardado;
- guardar manualmente;
- nombre o referencia;
- recuperar;
- vencimiento configurable;
- límite operativo;
- eliminar borrador con permiso;
- no perder el borrador al abrir Pedidos;
- versión para evitar edición simultánea.

### Pago

Medios iniciales:

- efectivo;
- tarjeta débito/crédito como categoría `Card`;
- transferencia;
- crédito/CxC.

Comportamientos:

- pago mixto;
- faltante;
- exceso;
- cambio;
- valor recibido;
- referencia opcional de tarjeta o transferencia;
- bloqueo de total negativo;
- validación de crédito;
- no confirmar si la distribución no cuadra;
- idempotencia;
- impresión opcional;
- apertura de cajón por POS Edge.

### Confirmación

- motor servidor;
- número definitivo;
- factura y líneas;
- pagos;
- caja;
- inventario;
- costo;
- utilidad;
- CxC;
- vínculo con pedido;
- auditoría;
- outbox fiscal.

### Después de confirmar

- no editar;
- reimprimir;
- enviar PDF;
- consultar estado DIAN;
- devolver;
- anular/revertir mediante flujo autorizado.

---

## 15. Sesión y arqueo de caja

### Estados

```text
Open
InOperation
Counting
Closed
ReopenedByException
```

### Apertura

- caja;
- usuario responsable;
- fecha/hora;
- base inicial;
- moneda;
- observación;
- dispositivo;
- impedir dos sesiones incompatibles.

### Durante la sesión

- ventas;
- devoluciones;
- abonos CxC;
- entradas de efectivo;
- salidas de efectivo;
- pagos por medio;
- cambios entregados;
- usuario de cada movimiento;
- máximo de efectivo en cajón y alerta opcional.

### Arqueo

- conteo por medio de pago;
- efectivo contado por valor total o denominación;
- valor esperado;
- valor contado;
- diferencia;
- ventas;
- devoluciones;
- crédito;
- abonos;
- entradas y salidas;
- descuentos;
- impuestos;
- motivos de diferencia;
- autorización de diferencia;
- observación;
- impresión/PDF;
- auditoría.

### Modo ciego

Puede configurarse que el cajero capture primero los valores sin ver lo esperado. El supervisor revisa diferencias después.

### Cierre

- bloquea nuevos documentos en la sesión;
- no puede cerrarse con pagos incompletos;
- advierte operaciones offline pendientes;
- no borra diferencias;
- una reapertura es excepcional, con permiso y auditoría;
- usa fecha operativa del negocio, no solo medianoche UTC.

---

## 16. Cuentas por cobrar

### Obligatorio

- documento origen;
- cliente;
- valor original;
- fecha;
- vencimiento;
- saldo;
- estado;
- abonos parciales;
- aplicación de pago;
- reversión de aplicación;
- nota o ajuste simple;
- crédito disponible;
- límite de crédito;
- cartera vencida;
- antigüedad;
- estado de cuenta;
- historial/kardex;
- pago desde caja.

### Estados

```text
Open
PartiallyPaid
Paid
Overdue
Cancelled
WrittenOff
```

### Fuera

- cuotas;
- intereses;
- intereses de mora;
- cheques posfechados;
- retenciones;
- cruces complejos;
- programación avanzada.

### Omisión importante

Un abono recibido en caja debe actualizar CxC y la sesión de caja dentro de una misma transacción.

---

## 17. Cuentas por pagar

### Obligatorio

- origen en entrada;
- proveedor;
- factura del proveedor;
- valor original;
- vencimiento;
- saldo;
- estado;
- abonos parciales;
- aplicación;
- reversión;
- ajuste simple;
- antigüedad;
- estado de cuenta;
- historial/kardex;
- pago registrado.

### Efectos relacionados

- una devolución de compra reduce saldo o crea crédito a favor;
- una reversión de entrada revierte la CxP;
- no se puede pagar más del saldo sin una política explícita;
- la duplicidad de factura de proveedor se bloquea.

### Fuera

- retenciones;
- cheques;
- tarjetas;
- cruces de cuenta;
- programación avanzada de pagos.

---

## 18. Devoluciones

### Venta

- buscar factura;
- seleccionar cantidades no devueltas;
- motivo;
- condición del producto;
- destino: inventario disponible, avería o no reingresar;
- resolución económica: devolución por medio permitido, reducir CxC o saldo a favor simple;
- movimiento de inventario;
- reversión de ingreso;
- nota crédito electrónica cuando aplique;
- relación por línea;
- impresión;
- auditoría.

### Compra

- buscar entrada;
- seleccionar cantidades disponibles para devolver;
- motivo;
- reducir inventario;
- reducir CxP o registrar crédito de proveedor;
- mantener costo original;
- relación por línea;
- auditoría.

### Reglas

- no devolver más de lo vendido/recibido;
- no duplicar devolución;
- no usar bonos especializados;
- no operar offline en el MVP;
- no eliminar confirmadas;
- una reversión de devolución restaura sus efectos.

---

## 19. Facturación electrónica propia

### Configuración

- datos fiscales de empresa;
- responsabilidad tributaria;
- resolución;
- prefijo;
- rango;
- vigencia;
- ambiente;
- software ID/PIN;
- certificado;
- contraseña protegida en Key Vault;
- numeración por tipo de documento.

### Procesamiento

- UBL;
- impuestos;
- CUFE/CUDE;
- QR;
- firma XAdES;
- ZIP;
- envío;
- consulta;
- validaciones;
- XML de respuesta;
- representación PDF;
- correo al adquirente;
- almacenamiento inmutable;
- reintentos;
- estados;
- nota crédito por devolución;
- contingencia.

### Estados

```text
Pending
Building
Signed
Submitted
Accepted
Rejected
Contingency
CancelledByCreditNote
```

### Omisiones importantes

- una factura comercial confirmada no debe desaparecer si DIAN rechaza;
- el rechazo debe ser corregible y trazable;
- nunca reutilizar un consecutivo;
- alertar vencimiento de resolución y certificado;
- conservar payload, hash, respuesta y tiempos;
- separar número interno de número fiscal;
- definir la entrega al cliente;
- tablero de pendientes y rechazadas.

---

## 20. Impresión y POS Edge

El navegador por sí solo no debe asumir control confiable de:

- balanza;
- impresora térmica;
- corte de papel;
- cajón monedero;
- reimpresión silenciosa.

POS Edge será responsable de periféricos.

### MVP

- perfil de impresión por caja;
- tirilla;
- PDF carta para documento electrónico;
- número de copias;
- reimpresión marcada como copia;
- selección de impresora;
- abrir cajón solo en eventos autorizados;
- cola local de impresión;
- reintento;
- estado del dispositivo.

No se migran todos los formatos de Xion. Se diseñan plantillas nuevas Auraly.

---

## 21. Offline y sincronización

### Catálogo

- primera sincronización automática;
- snapshot comprimido;
- staging;
- intercambio atómico;
- deltas;
- tombstones para eliminaciones/desactivaciones;
- checkpoint;
- recuperación de huecos;
- promociones;
- precio efectivo;
- configuración de caja;
- no inventario;
- no pedidos.

### Venta offline

- borrador local;
- `LocalDraftId`;
- `ClientOperationId`;
- catálogo/revisión usados;
- pagos capturados;
- cola outbox;
- estado visible;
- reintentos;
- envío al motor;
- resultado definitivo;
- prevención de duplicados.

### Restricción legal

Una venta pendiente offline no debe mostrarse como factura electrónica aceptada.

Se debe distinguir:

```text
Comprobante provisional
Factura interna procesada
Factura electrónica aceptada
```

### Omisiones importantes

- sesión de caja debe conocer operaciones offline pendientes;
- el arqueo debe advertirlas;
- cierre con pendientes requiere política;
- una caja no puede perder outbox al actualizar catálogo;
- un producto desactivado después de una venta offline exige resolución del motor;
- promociones vencidas o precios cambiados deben generar una decisión determinista;
- observabilidad por dispositivo.

---

## 22. Seguridad y auditoría

### Ámbitos

- tenant;
- empresa;
- negocio;
- sucursal;
- bodega;
- caja.

### Permisos mínimos

- vender;
- vender negativos;
- cambiar precio;
- aplicar descuento;
- eliminar línea;
- cancelar borrador;
- anular/revertir documento;
- reimprimir;
- ver costo;
- ver utilidad;
- abrir caja;
- registrar entrada/salida de efectivo;
- arquear;
- aprobar diferencia;
- recuperar pedido;
- facturar pedidos masivamente;
- confirmar entrada;
- ajustar inventario;
- trasladar;
- recibir traslado;
- registrar avería;
- devolver;
- pagar CxP;
- recibir CxC;
- administrar resolución DIAN.

### Autorización de supervisor

Una autorización debe registrar:

- acción;
- usuario solicitante;
- supervisor;
- motivo;
- documento;
- valores antes/después;
- fecha;
- dispositivo.

No se debe compartir contraseña ni registrar únicamente un booleano.

### Auditoría

Debe conservar:

- quién;
- cuándo;
- desde qué dispositivo;
- qué cambió;
- antes/después;
- motivo;
- correlación;
- resultado.

---

## 23. Precisión, fechas y concurrencia

### Dinero

Usar `DECIMAL`, nunca `FLOAT`, `REAL` o `double`.

Recomendación:

```text
DECIMAL(19,4) para dinero y costos
DECIMAL(19,6) para cantidades
```

La presentación puede redondear a la precisión de COP, pero el cálculo conserva precisión.

### Fechas

- persistir instantes técnicos en UTC;
- conservar fecha operativa local;
- negocio con zona horaria;
- sesiones pueden cruzar medianoche;
- documentos no cambian de día por conversión incorrecta.

### Concurrencia

Agregar `rowversion` a:

- productos editables;
- pedidos;
- borradores compartidos;
- saldos de inventario;
- sesiones de caja;
- cuentas por cobrar/pagar;
- secuencias.

El motor debe reintentar deadlocks seguros sin duplicar operaciones.

---

## 24. Reportes mínimos revisados

### Ventas

- total;
- impuestos;
- descuentos;
- devoluciones;
- costo;
- utilidad;
- por empresa, negocio, caja, usuario y rango.

### Compras

- entradas;
- devoluciones;
- costo;
- proveedor;
- vencimientos.

### Inventario

- existencias;
- kardex;
- valoración;
- negativos;
- conteos y diferencias;
- traslados pendientes;
- averías.

### Caja

- apertura;
- ventas por medio;
- devoluciones;
- crédito;
- abonos;
- entradas/salidas;
- esperado;
- contado;
- diferencia;
- arqueo.

### Cartera

- CxC y CxP abiertas;
- vencidas;
- antigüedad;
- abonos;
- saldos por tercero.

### Fiscal

- pendientes;
- aceptadas;
- rechazadas;
- contingencia;
- resolución y rango restante.

### Operación

- pedidos facturados/fallidos;
- ventas offline pendientes;
- errores del motor;
- reconciliaciones.

Xion Web contiene analítica adicional valiosa, pero comparativas de proveedores, temporadas, GPS, comisiones y metas quedan posteriores.

---

## 25. Migración de datos

### IDs

- generar nuevos IDs Auraly;
- preservar mapeo legado;
- no usar código de barras como PK;
- no usar número de documento como PK.

### Datos necesarios para iniciar

- empresas y negocios;
- usuarios y permisos;
- bodegas;
- cajas;
- clientes;
- proveedores;
- productos;
- códigos;
- precio efectivo;
- impuestos;
- promociones vigentes;
- existencias iniciales;
- costo inicial;
- pedidos pendientes;
- CxC abiertas;
- CxP abiertas;
- configuración y resolución DIAN.

### Historia

No es obligatorio migrar toda la historia al modelo operacional nuevo.

Opciones:

- importar historia resumida para reportes;
- mantener Xion como consulta histórica;
- importar documentos recientes seleccionados.

### Proceso

1. extracción;
2. limpieza;
3. staging;
4. asignación de IDs;
5. validación de duplicados;
6. importación;
7. conciliación;
8. ensayo de corte;
9. corte final;
10. acta de saldos.

Conciliar:

- productos;
- existencias por bodega;
- costo total;
- pedidos pendientes;
- CxC;
- CxP;
- numeración.

---

## 26. Diseño web por módulo

| Módulo | Vista web obligatoria |
|---|---|
| Productos | listado, editor por pestañas, códigos, bodegas, auditoría |
| Promociones | listado, editor de regla, simulación, vigencia |
| Pedidos | vista propia, detalle, selección múltiple, progreso |
| Facturación | mesa POS, temporales, pedido único, pago |
| Caja | apertura, movimientos, arqueo, historial |
| Inventario | existencias, kardex, conteo, diferencias |
| Entradas | bandeja, documento con grilla, temporal, detalle |
| Traslados | creación, despacho, recepción, pendientes |
| Averías | creación, pendientes, resolución, consulta |
| Devoluciones | búsqueda origen, selección de líneas, resolución |
| CxC | bandeja, detalle, abono, estado de cuenta |
| CxP | bandeja, detalle, pago, estado de cuenta |
| Fiscal | resolución, certificado, documentos, errores |
| Motor | operaciones, reintentos, reconciliación |
| PosSync | dispositivos, revisión, salud, outbox |
| Reportes | filtros, indicadores, tabla y exportación |

Todos los documentos con líneas comparten comportamiento de teclado y escáner, pero no una lógica de negocio genérica.

---

## 27. Priorización

### P0 — Imprescindible para vender en piloto

- renombrado Auraly;
- arquitectura modular;
- proyecto SQL;
- motor documental;
- catálogo;
- códigos;
- precio;
- promociones existentes;
- caja y sesión;
- POS;
- pagos;
- arqueo;
- pedidos online y facturación seleccionada;
- inventario y costo;
- entrada y CxP;
- CxC;
- devolución;
- DIAN;
- impresión;
- permisos;
- auditoría;
- reportes operativos mínimos.

### P1 — Imprescindible para operación estable

- conteos con reconteo;
- traslado en dos pasos;
- averías;
- offline;
- reconciliación;
- migración de saldos;
- alertas fiscales;
- monitoreo.

### P2 — Preparado, no necesariamente habilitado

- reserva de inventario por pedido;
- reclamación de avería a proveedor;
- facturación parcial de pedido;
- importación histórica profunda;
- analítica avanzada.

### Fuera

Todo lo listado en 2.1.

---

## 28. Riesgo de alcance

Con este alcance, el MVP ya es un ERP comercial operativo, no un módulo pequeño de factura.

Para salir rápido se debe construir por cortes verticales:

1. producto vendido en POS;
2. caja abierta, venta, pago y arqueo;
3. pedido recuperado y pedido facturado en lote;
4. entrada que crea inventario y CxP;
5. crédito que crea CxC y recibe abono;
6. devolución que revierte efectos;
7. documento electrónico;
8. offline y reconciliación.

Cada corte debe funcionar extremo a extremo antes de abrir otro.

---

## 29. Criterios de aceptación de la auditoría

- Las capacidades excluidas no aparecen como dependencias del MVP.
- Eventos se trata como Promociones.
- Cierre se presenta como Arqueo de caja.
- El costo permite calcular utilidad real.
- Cada documento tiene estados y reversión.
- Ningún confirmado se elimina.
- El motor procesa todos los efectos definitivos.
- Pedidos se consulta online.
- Desde POS se recupera uno.
- Desde la vista se facturan varios, una factura por pedido.
- El arqueo concilia medios y diferencias.
- Entradas crean inventario y CxP.
- Ventas a crédito crean CxC.
- Devoluciones revierten inventario, caja/cartera y fiscal.
- Impresión y balanza pasan por POS Edge.
- La caja no descarga inventario ni pedidos.
- Los cambios SQL pertenecen a `Auraly.Database.sqlproj`.
- IDs, dinero, fechas y concurrencia usan tipos adecuados.
- La migración concilia saldos iniciales.
- Existe una bandeja de errores y reconciliación del motor.
