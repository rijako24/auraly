# Alcance definitivo del MVP: POS, balanza y devoluciones

**Estado:** consolidación histórica; vigente solo donde no fue reemplazada por decisiones posteriores
**Fecha:** 23 de julio de 2026  
**Prioridad:** este documento reemplaza las decisiones contradictorias de documentos anteriores sobre negativos, balanza, módulos comerciales opcionales y devoluciones.

> **Prevalencia posterior:** la política de negativos y la ausencia de inventario
> local se rigen por `decision-definitiva-negativos-por-bodega-y-sin-inventario-local.md`;
> el contexto operativo por `decision-sesiones-trabajo-equipos-enrolados-sin-caja.md`;
> y la captura de productos por `diseno-ux-facturacion-pos-web.md`. Las secciones
> reemplazadas se conservan solo como contexto histórico y no son requisitos de
> implementación.

---

## 1. Decisiones cerradas

### Incluido desde el primer MVP

- política de venta con inventario negativo por bodega;
- balanza;
- facturación POS con paridad funcional operativa;
- ventas guardadas/temporales;
- descuentos;
- eliminación de productos;
- cancelación de toda la venta en curso;
- múltiples medios de pago;
- búsqueda amplia de productos;
- clientes y vendedores;
- pedidos de Auraly convertidos a venta;
- devoluciones de venta;
- devoluciones de compras o a proveedor;
- cambios de mercancía como devolución más nueva venta;
- efectos de inventario, caja, CxC y CxP;
- nota crédito electrónica;
- operación offline dentro de reglas explícitas;
- reportes de devoluciones.

### Fuera del MVP

- apartados;
- cotizaciones;
- remisiones;
- domicilios;
- puntos y fidelización;
- bonos especializados;
- cheques posfechados.

Estos elementos no se desarrollarán ni se dejarán a medio construir en la primera versión. Podrán agregarse como módulos posteriores.

### Medios de pago que sí quedan

- efectivo;
- tarjeta débito;
- tarjeta crédito;
- transferencia;
- consignación;
- crédito/cartera;
- cheque al día, solo si el piloto lo necesita;
- retenciones para operaciones autorizadas;
- pagos mixtos.

No quedan en el MVP:

- bono de puntos;
- bono de apartado;
- bono por devolución;
- cheque posfechado;
- pago de domicilio.

Cuando una devolución produzca valor a favor del cliente, se resuelve mediante devolución de dinero, reverso al medio permitido, aplicación a cartera o saldo a favor contable. No se requiere imprimir un bono especializado.

---

## 2. Política de negativos por bodega

La decisión vigente es:

```text
Warehouse.AllowNegativeStockSales
```

La propiedad pertenece a la bodega y toda sesión de trabajo que opera sobre ella
hereda la misma política sin poder sobrescribirla.

La sesión de trabajo opera sobre una bodega:

```text
WorkSession.WarehouseId -> Warehouse.Id
```

Dos sesiones o dispositivos sobre la misma bodega no pueden tener políticas
diferentes.

### Comportamiento habilitado

- el POS no valida existencia para bloquear;
- el producto se agrega y vende;
- la salida afecta la bodega de la caja;
- el saldo puede quedar negativo;
- se crea alerta de conciliación;
- offline se comporta igual.

### Comportamiento deshabilitado

- el POS valida en línea al capturar y el motor revalida en la transacción final;
- presenta faltantes;
- permite eliminar las líneas o pedir autorización;
- una autorización de supervisor queda auditada;
- sin red no inventa existencia ni captura una línea que requiera validación.

### Precedencia

Para POS:

```text
configuración de bodega
    -> autorización excepcional
    -> movimiento de inventario
```

Para traslados, inventarios, entradas, averías y devoluciones se usan las reglas de cada módulo. La propiedad de la caja no gobierna esos movimientos.

### Criterios

- cambiar de bodega cambia la política efectiva heredada y exige resincronizar configuración;
- una sesión o dispositivo no copia ni modifica la política;
- cada venta guarda sesión, dispositivo opcional, bodega y versión de configuración;
- el administrador muestra la política en la bodega y solo la presenta como heredada en el POS;
- los reportes muestran qué sesión, dispositivo y bodega originaron el negativo.

---

## 3. Balanza incluida desde el inicio

La balanza no será un feature posterior.

### 3.1 Casos soportados

1. **Código de barras de balanza:** el código contiene producto y peso o precio.
2. **Balanza conectada:** Auraly POS Edge lee el peso desde puerto serial, USB o adaptador soportado.
3. **Peso manual autorizado:** el cajero escribe el peso cuando el dispositivo no está disponible.

### 3.2 Configuración

`CashRegisterScaleSettings`:

- `Enabled`;
- `Mode`: barcode, serial, USB o manual;
- `PeripheralId`;
- protocolo;
- puerto;
- velocidad;
- unidad;
- estabilidad mínima;
- tara;
- cantidad de decimales;
- patrón del código;
- dígitos de producto;
- dígitos de peso/precio;
- factor;
- checksum;
- tiempo de espera;
- permitir captura manual;
- permiso de modificación.

### 3.3 Productos por peso

El producto contiene:

- `SoldByWeight`;
- unidad base;
- precisión;
- peso mínimo/máximo;
- código usado por la balanza;
- precio por unidad de peso;
- tara predeterminada opcional.

### 3.4 Flujo

1. escanear etiqueta o seleccionar producto;
2. resolver producto;
3. obtener peso;
4. validar estabilidad y rango;
5. agregar cantidad decimal;
6. calcular total;
7. volver al foco de escaneo.

Modificar el peso recalcula todo y queda auditado si se hizo manualmente.

### 3.5 Offline

- patrones y productos se descargan localmente;
- el agente Edge controla la balanza;
- el peso no depende del servidor;
- la venta conserva lectura cruda, peso interpretado, dispositivo y modo;
- un error de balanza no puede convertir silenciosamente gramos en kilogramos.

---

## 4. Facturación POS del MVP

Se conserva:

- escaneo continuo;
- cantidades y empaques;
- balanza;
- cambio de cantidad;
- precio autorizado;
- descuento por línea y factura;
- búsqueda por múltiples campos;
- eliminación de línea;
- cancelación de venta en curso;
- ventas temporales;
- recuperación;
- cliente;
- vendedor;
- observaciones;
- pedidos;
- pago mixto;
- cartera;
- devolución;
- apertura/cierre de caja;
- ingreso/retiro;
- reimpresión;
- consulta informativa de inventario;
- rentabilidad con permiso;
- offline.

Se eliminan del mapa de navegación del MVP:

- apartado;
- cotización;
- remisión;
- domicilio;
- redención de puntos;
- bonos especializados.

La ausencia de esos módulos también elimina atajos, botones, estados, campos y tablas que solo existían para ellos.

---

## 5. Módulo Returns

Devoluciones será un módulo propio y no solamente un diálogo dentro de facturación.

Submódulos:

```text
Returns
├── Sales Returns
├── Purchase Returns
├── Exchanges
├── Refunds and Applications
├── Fiscal Credit Notes
└── Return Reporting
```

## 6. Devolución de venta

### 6.1 Tipos

- parcial;
- total.

Toda devolución normal referencia una venta original.

Una devolución sin documento original requiere permiso especial, identificación del cliente, precio verificable, motivo y auditoría. Puede excluirse del primer piloto si el negocio no la necesita.

### 6.2 Búsqueda de la venta

Se busca por:

- número de factura;
- prefijo y consecutivo;
- CUFE;
- identificación del cliente;
- fecha;
- caja;
- vendedor;
- valor;
- código de producto;
- pedido de origen.

La búsqueda muestra estado:

- comercial;
- fiscal;
- pago;
- cartera;
- devoluciones anteriores;
- saldo devolvible.

### 6.3 Selección de productos

La grilla carga únicamente líneas de la venta original.

Columnas:

- código;
- producto;
- unidad;
- cantidad vendida;
- cantidad devuelta anteriormente;
- cantidad máxima disponible para devolver;
- cantidad a devolver;
- precio original;
- descuento original;
- impuesto original;
- total;
- lote/serial;
- condición física;
- destino.

El lector puede identificar una línea de la factura. Escanear no agrega un producto que no fue vendido.

Reglas:

- no devolver más de lo vendido menos devoluciones previas;
- no cambiar precio ni impuesto original;
- usar la fotografía fiscal de la venta;
- recalcular proporcionalmente descuentos e impuestos;
- productos serializados validan serial vendido;
- lotes conservan trazabilidad;
- cantidades decimales se permiten para productos por peso;
- una devolución total selecciona el saldo pendiente de todas las líneas.

### 6.4 Motivo y condición

El encabezado exige:

- motivo;
- observación configurable;
- usuario;
- caja;
- bodega receptora;
- fecha;
- cliente;
- documento original.

Cada línea puede indicar:

- producto en buen estado;
- empaque abierto;
- averiado;
- vencido;
- incompleto;
- requiere inspección.

### 6.5 Destino físico

Una devolución no siempre regresa a inventario vendible.

| Condición | Efecto |
|---|---|
| Vendible | Entrada a la bodega de la caja |
| Requiere inspección | Entrada a bodega/cuarentena |
| Averiado | Entrada directa al flujo o bodega de averías |
| No retornado | No aumenta inventario; requiere motivo |

Esto integra devoluciones con averías sin confundir ambos documentos.

### 6.6 Resolución económica

Modos:

- devolución de efectivo;
- reverso/reembolso a tarjeta, si la integración lo soporta;
- transferencia al cliente;
- aplicación a cuenta por cobrar;
- saldo a favor del cliente.

No se genera bono especializado en el MVP.

Reglas:

- el valor máximo se deriva de la venta y devoluciones previas;
- una venta a crédito reduce primero la cuenta por cobrar;
- si el valor devuelto supera el saldo de cartera, el excedente se reembolsa o queda como saldo a favor;
- una venta pagada produce reembolso según política y medios originales;
- el retiro de efectivo afecta la sesión de caja;
- todo reembolso tiene idempotency key;
- un reembolso electrónico conserva referencia y estado;
- no se marca pagado si el proveedor de pagos no lo confirmó.

### 6.7 Efecto fiscal

Si la venta tiene factura electrónica:

- se genera nota crédito;
- referencia factura y CUFE originales;
- usa el motivo/código fiscal correspondiente;
- conserva cantidades, bases, impuestos y descuentos;
- se firma;
- se envía directamente a la DIAN;
- se guarda XML, respuesta, CUDE/CUFE aplicable, QR y PDF;
- la devolución comercial y la nota tienen estados separados;
- un rechazo fiscal no borra el movimiento físico/económico; genera caso de corrección.

Si la devolución es parcial, pueden existir varias notas sin exceder el valor original.

### 6.8 Estados

```text
Draft
  -> Confirmed
  -> InventoryPosted
  -> RefundPending / ReceivableApplied
  -> FiscalPending
  -> Completed
```

Ramas:

- `RefundFailed`;
- `FiscalRejected`;
- `PendingInspection`;
- `CancelledBeforeConfirmation`.

Una devolución confirmada no se elimina. Se revierte con un movimiento compensatorio autorizado.

---

## 7. Devolución de compras

Como las entradas crean cuentas por pagar, las devoluciones al proveedor deben estar en el MVP.

### 7.1 Origen

Referencia:

- documento del proveedor;
- entrada de mercancía;
- líneas recibidas;
- proveedor;
- bodega.

### 7.2 Tipos

- parcial;
- total;
- anulación de una recepción incorrecta antes de cierres aplicables.

### 7.3 Captura

La grilla soporta escáner y teclado:

- código;
- producto;
- recibido;
- devuelto previamente;
- disponible para devolver;
- cantidad;
- costo original;
- descuentos;
- impuestos;
- lote;
- serial;
- total;
- motivo.

No se devuelve más de lo recibido neto.

### 7.4 Efectos

- salida de inventario;
- costo basado en la recepción original;
- disminución de cuenta por pagar;
- saldo a favor con proveedor si la obligación ya fue pagada;
- registro de reembolso del proveedor;
- relación con nota crédito del proveedor;
- reverso proporcional de impuestos, descuentos y retenciones;
- actualización de reportes de compras.

El sistema no emite una nota crédito de venta propia por devolver una compra. Registra el documento emitido por el proveedor cuando corresponda.

### 7.5 Lotes y seriales

- el lote debe existir en la recepción o inventario;
- el serial debe pertenecer a la bodega;
- la salida conserva trazabilidad;
- un serial no se devuelve dos veces.

---

## 8. Cambios de mercancía

Un cambio no tendrá un tercer motor de inventario.

Se modela como:

```text
SalesReturn + NewSale + Settlement
```

Flujo:

1. localizar venta;
2. seleccionar producto devuelto;
3. registrar condición y entrada;
4. crear devolución y nota crédito;
5. abrir una nueva venta vinculada;
6. agregar producto de reemplazo;
7. calcular diferencia;
8. cobrar diferencia o reembolsar excedente;
9. emitir nueva factura cuando aplique.

Ventajas:

- inventario explicable;
- fiscalidad correcta;
- pagos trazables;
- reportes sin reglas especiales;
- permite cambio por producto de valor diferente.

---

## 9. Modelo de datos

### Ventas

- `SalesReturns`
- `SalesReturnLines`
- `ReturnLineDispositions`
- `CustomerRefunds`
- `CustomerCreditBalances`
- `SalesReturnApplications`
- `ExchangeLinks`

### Compras

- `PurchaseReturns`
- `PurchaseReturnLines`
- `SupplierRefunds`
- `SupplierCreditBalances`
- `PurchaseReturnApplications`

### Campos clave de devolución

- tenant, negocio, sede;
- caja y sesión;
- bodega;
- documento original;
- cliente/proveedor;
- tipo;
- motivo;
- observación;
- fecha efectiva y registrada;
- estado comercial;
- estado de inventario;
- estado económico;
- estado fiscal;
- dispositivo;
- idempotency key;
- configuración y reglas;
- usuario y supervisor;
- totales.

Cada línea guarda fotografía de:

- producto;
- unidad;
- cantidad;
- precio/costo original;
- descuento;
- impuestos;
- total;
- costo;
- lote/serial;
- condición;
- destino.

---

## 10. Transacción y eventos

Al confirmar una devolución de venta se guardan atómicamente:

- encabezado;
- líneas;
- aplicaciones económicas solicitadas;
- movimiento lógico de inventario;
- auditoría;
- outbox.

Eventos:

```text
SalesReturnConfirmed
ReturnedInventoryReceived
ReturnedItemSentToDamage
CustomerRefundRequested
ReceivableCreditRequested
FiscalCreditNoteRequested
SalesReturnCompleted
```

Para compras:

```text
PurchaseReturnConfirmed
SupplierInventoryDispatched
PayableCreditRequested
SupplierRefundRecorded
```

DIAN, procesador de pagos, correo y proyecciones se ejecutan asíncronamente e idempotentemente.

---

## 11. Offline

### Devolución de venta offline

Se permite cuando:

- la venta original existe en la base local;
- sus devoluciones previas conocidas están sincronizadas;
- el usuario tiene permiso;
- las reglas fiscales/configuración están vigentes.

Si la venta no está local:

- se solicita conexión;
- no se inventa una devolución sin referencia.

La caja guarda:

- devolución;
- movimiento;
- reembolso permitido;
- nota crédito pendiente;
- auditoría.

Riesgo: otra caja pudo devolver el mismo producto mientras ambas estaban offline. El servidor detecta el exceso, bloquea la segunda aplicación económica/fiscal y crea un conflicto; nunca lo resuelve silenciosamente.

Para reducir ese riesgo:

- devoluciones offline pueden limitarse a ventas de la misma caja;
- se descarga historial reciente;
- se muestra última sincronización;
- se exige supervisor para ventas antiguas.

### Devolución de compra offline

No es prioritaria para operar desconectado. El MVP puede requerir conexión para devolver al proveedor, porque normalmente es una operación administrativa y necesita estado actual de entrada y CxP.

---

## 12. Permisos

- crear devolución de venta;
- devolución parcial;
- devolución total;
- devolución sin factura;
- devolver de otra caja;
- devolver fuera del plazo;
- cambiar bodega receptora;
- enviar a inventario vendible;
- enviar a averías;
- reembolsar efectivo;
- aplicar a cartera;
- crear saldo a favor;
- autorizar exceso/conflicto;
- cancelar borrador;
- reimprimir;
- ver costo;
- crear devolución a proveedor;
- afectar CxP;
- registrar nota crédito del proveedor.

---

## 13. Reportes

### Devoluciones de venta

- cantidad y valor;
- parcial/total;
- motivo;
- condición;
- producto;
- categoría;
- cliente;
- caja;
- cajero;
- vendedor;
- bodega;
- reembolso;
- aplicación a cartera;
- estado de nota crédito;
- impacto en venta neta, costo y utilidad;
- tiempo entre venta y devolución.

### Devoluciones de compra

- proveedor;
- documento y entrada;
- producto;
- cantidad;
- costo;
- motivo;
- bodega;
- reducción de CxP;
- saldo a favor;
- nota crédito del proveedor.

### Indicadores

- tasa de devolución;
- productos más devueltos;
- motivos principales;
- devoluciones por caja/cajero;
- devoluciones enviadas a averías;
- notas crédito pendientes/rechazadas;
- reembolsos pendientes.

---

## 14. Criterios de aceptación

### Negativos

- la política se configura en cada caja;
- dos cajas de una bodega pueden diferir;
- habilitada no bloquea;
- deshabilitada valida y autoriza;
- venta guarda la política usada.

### Balanza

- una etiqueta resuelve producto y peso;
- una balanza conectada entrega peso estable;
- cantidades decimales calculan correctamente;
- funciona offline;
- modificación manual queda auditada.

### Devolución de venta

- encuentra la factura;
- muestra cantidades ya devueltas;
- permite parcial y total;
- no excede lo vendido;
- escáner solo selecciona productos originales;
- solicita motivo;
- separa vendible, inspección y avería;
- revierte inventario exactamente una vez;
- devuelve dinero o aplica CxC;
- genera nota crédito electrónica;
- recalcula reportes;
- funciona offline bajo la política definida.

### Devolución de compra

- referencia entrada y proveedor;
- no excede lo recibido;
- disminuye inventario;
- afecta CxP o saldo del proveedor;
- conserva lotes y seriales;
- aparece en reportes de compra.

### Exclusiones

- no aparecen apartados, cotizaciones, remisiones, domicilios ni puntos;
- no existen medios de pago de bonos especializados;
- no se ofrece cheque posfechado;
- no quedan tablas o estados obligatorios que bloqueen por módulos excluidos.

---

## 15. Impacto en el cronograma

La balanza y devoluciones dejan de ser trabajo posterior. Deben incorporarse así:

1. catálogo incluye productos por peso;
2. POS Edge incluye balanza;
3. inventario incluye destinos de devolución y avería;
4. ventas incluye devolución parcial/total;
5. cartera incluye aplicaciones de devolución;
6. caja incluye reembolsos;
7. fiscal incluye nota crédito;
8. compras/CxP incluye devolución a proveedor;
9. reportes incluyen devoluciones.

Eliminar apartados, cotizaciones, remisiones, domicilios, puntos, bonos especializados y cheques posfechados compensa parte del alcance y mantiene el MVP enfocado.
