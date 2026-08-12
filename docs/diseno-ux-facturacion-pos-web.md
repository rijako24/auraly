# Diseño UX: Facturación POS web de Auraly

**Estado:** base aprobada para prototipo y pruebas de usabilidad  
**Dispositivo principal:** caja de escritorio con teclado, lector de códigos,
balanza, impresora y cajón monedero  
**Objetivo:** vender de forma continua, con el menor número posible de
interrupciones, conservando todas las capacidades de Facturación de Xion que
entran al MVP.

---

## 1. Investigación usada como referencia

No se copiará visualmente otro producto. Se toman patrones operativos comprobados:

### Microsoft Dynamics 365 Commerce

- [Configuración y layouts del POS](https://learn.microsoft.com/en-us/dynamics365/commerce/pos-screen-layouts):
  separa pantalla de bienvenida y pantalla transaccional, admite layouts completos
  y compactos, y asignación por tienda, caja o usuario.
- [Grilla transaccional moderna](https://learn.microsoft.com/en-us/dynamics365/commerce/pos-modern-transaction-grid):
  incorpora acciones sobre la línea, edición de cantidad y notificaciones dentro
  del carrito sin abandonar la venta.
- [Pagos optimizados](https://learn.microsoft.com/en-us/dynamics365/commerce/dev-itpro/faster-checkout-pos):
  concentra el pago en un panel, reduce cadenas de diálogos y ofrece pago exacto.
- [Suspender y recuperar transacciones](https://learn.microsoft.com/en-us/dynamics365/commerce/pos-suspend-recall-transactions):
  permite liberar la caja sin perder el carrito y recuperar mediante búsqueda o
  código de barras.
- [Operación offline](https://learn.microsoft.com/en-us/dynamics365/commerce/dev-itpro/pos-offline-functionality):
  usa catálogo local y sincroniza operaciones creadas sin conexión.
- [Recepción con lector](https://learn.microsoft.com/en-us/dynamics365/commerce/pos-inbound-inventory-operation):
  valida cada lectura contra el documento, conserva cantidades parciales y permite
  pausar/reanudar.

### Shopify POS

- [Venta presencial](https://help.shopify.com/en/manual/sell-in-person/shopify-pos):
  mantiene el carrito como centro del flujo y permite modificar líneas, descuentos
  y medios de pago.
- [Guardar y recuperar carrito](https://help.shopify.com/en/manual/sell-in-person/shopify-pos/order-management/save-retrieve-order):
  permite continuar con otra venta y recuperar después el borrador.
- [Editar o retirar productos](https://help.shopify.com/en/manual/sell-in-person/shopify-pos/order-management/edit-remove-item):
  cantidad y eliminación se operan directamente desde el carrito.

### Square

- [Construcción del carrito](https://squareup.com/help/us/en/article/8238-build-your-customer-s-cart-in-the-square-retail-pos-app):
  usa carritos guardados con nombre/nota para recuperarlos.
- [Lectura de códigos](https://squareup.com/help/us/en/article/8506):
  el escaneo sirve tanto en venta como en tareas de inventario.

### Accesibilidad y teclado

- [Patrón ARIA Grid](https://www.w3.org/WAI/ARIA/apg/patterns/grid/):
  navegación eficiente por celdas mediante flechas, `Home` y `End`.
- [Foco visible WCAG 2.2](https://www.w3.org/WAI/WCAG22/Understanding/focus-visible):
  el operador debe saber siempre qué control recibirá la siguiente tecla.
- [Tamaño mínimo del objetivo WCAG 2.2](https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html):
  exige al menos 24 × 24 CSS px o separación equivalente; Auraly apunta a 44 px
  para acciones táctiles primarias.

### Conclusiones aplicables

Se adoptan:

- carrito como superficie principal;
- acciones de línea en contexto;
- panel de totales y pago siempre visible;
- guardar/recuperar;
- búsqueda y lector compartiendo una entrada rápida;
- flujo offline explícito;
- teclado completo;
- layouts por capacidad y tamaño;
- pago en un solo panel;
- notificaciones no bloqueantes.

No se adopta:

- una cuadrícula enorme de imágenes como interacción principal;
- ocultar el carrito detrás de otra pantalla;
- múltiples modales encadenados;
- hacer clic después de cada escaneo;
- configurar libremente la ubicación de todos los controles en el MVP;
- mezclar venta, inventario, administración y reportes en la misma pantalla.

---

## 2. Principios

### Flujo dominante

```text
leer -> agregar/incrementar -> leer siguiente -> cobrar -> nueva venta
```

El sistema se optimiza primero para ese ciclo.

### El lector manda texto

El lector tipo keyboard wedge escribe el código y termina con `Enter`. El campo de
captura:

- siempre está listo al abrir la venta;
- no pierde foco por notificaciones;
- limpia el valor al resolver;
- agrega o incrementa la línea;
- queda listo para la siguiente lectura;
- no exige mouse;
- distingue lectura de escritura humana por configuración y timing solo como
  ayuda, nunca como única validación.

### Una acción, una respuesta

Cada lectura produce inmediatamente:

- línea agregada o cantidad incrementada;
- nombre y cantidad visibles;
- total recalculado;
- señal sonora/visual configurable;
- error concreto si no se pudo agregar.

No abre el detalle del producto en el camino feliz.

### El cajero no administra

La pantalla POS no expone costos, saldos, proveedores, auditoría ni configuración
completa del producto.

---

## 3. Layout balanceado

Diseño de escritorio, desde 1280 × 720:

```text
+--------------------------------------------------------------------------------+
| Auraly | Caja C03 · Bodega Norte | Cajero: Ana | ● En línea | Sync 12:41       |
+--------------------------------------------------------------------------------+
| [ Escanear código o buscar producto...                         ] [Buscar F2]    |
+------------------------------------------------------+-------------------------+
| Factura temporal #4 · Cliente: Consumidor final      | Resumen                 |
|------------------------------------------------------|                         |
| Código | Producto         | Cant. | Vr unit. | Dto | Total | Subtotal  128.000 |
| 10231  | Arroz 1 kg       |  2    |  18.000  |  0  | 36.000| Descuento   8.000 |
| 770... | Aceite 1 L       |  1    |  22.000  |  0  | 22.000| Impuestos  19.000 |
| 20811  | Café             |  4    |  19.500  | 10% | 70.200|-------------------|
|                                                      | TOTAL      139.000      |
|                                                      |                         |
|                                                      | [ COBRAR  F9 ]          |
+------------------------------------------------------+-------------------------+
| F4 Cliente | F6 Guardar | F7 Pedidos | F8 Descuento | Más ▾ | Nueva factura    |
+--------------------------------------------------------------------------------+
| ✓ Café agregado · Cantidad 4                         | Rango FV: 184/500        |
+--------------------------------------------------------------------------------+
```

### Proporciones

- carrito: 65–70 %;
- resumen/acciones: 30–35 %;
- encabezado y estado: compactos;
- captura: ancho completo y muy visible;
- total: siempre visible y con la mayor jerarquía tipográfica;
- acciones poco frecuentes dentro de `Más`.

El panel derecho no desplaza la grilla cuando cambia el total.

### Densidad

La densidad es media-alta porque la caja necesita ver líneas, no tarjetas
decorativas. Se conserva:

- altura de fila suficiente;
- foco de celda evidente;
- números alineados a la derecha;
- encabezado fijo;
- columnas principales fijadas;
- contraste alto;
- sin truncar cantidad, precio ni total.

---

## 4. Encabezado operativo

Siempre muestra:

```text
Caja
Bodega heredada
Usuario
Sesión/arqueo abierto
Estado online/offline
Estado de sincronización
Estado de catálogo/precios
Rango de numeración
```

Estados:

```text
En línea
Sin conexión
Sincronizando
Con operaciones pendientes
Catálogo desactualizado
Rango por agotarse
Rango agotado
Caja bloqueada
```

El color nunca es el único indicador. Se combina icono, texto y ayuda.

La pérdida de red no abre un modal en medio de una lectura. Muestra una banda
persistente y aplica la política de operación correspondiente.

---

## 5. Captura y búsqueda

### Entrada única

El control interpreta:

```text
barcode exacto
ProductCode exacto
SKU exacto
referencia exacta
texto de búsqueda
barcode de balanza
```

Con coincidencia exacta agrega de inmediato. Con texto o varias coincidencias abre
el panel de búsqueda.

### Panel de búsqueda

```text
+------------------------------------------------------------------------+
| Buscar producto                                      [Cerrar Esc]      |
| [ café 500______________________________________________ ]              |
| Código | Producto | Marca | Unidad | Precio | Canal | Disponible*      |
| 2019   | Café...  | ...   | und    | 19.500 | Detal | Consultar        |
+------------------------------------------------------------------------+
| ↑↓ mover · Enter agregar · Ctrl+Enter ver detalle                      |
+------------------------------------------------------------------------+
```

`Disponible`:

- no se descarga a la caja;
- se consulta online bajo demanda;
- solo aparece automáticamente cuando la política de la bodega bloquea negativos;
- offline se muestra `No verificable`.

Si la bodega bloquea negativos, Auraly valida online al resolver producto y
cantidad. Sin conectividad no puede afirmar disponibilidad; la venta se bloquea o
entra a un flujo de excepción expresamente configurado y autorizado. No se usa un
saldo local desactualizado.

### Producto no encontrado

El error:

- conserva el código;
- permite reintentar;
- ofrece búsqueda textual;
- registra código desconocido;
- permite línea manual solo con permiso;
- devuelve el foco a captura al cerrar.

---

## 6. Grilla transaccional

### Columnas base

```text
Código
Producto
Cantidad
Unidad
Valor unitario
Descuento
Total
Acciones
```

Opcionales por ancho/permiso:

```text
Impuesto
Canal de precio
Vendedor
Nota
```

No se muestran costo ni utilidad al cajero normal.

### Navegación

- `↑` / `↓`: línea anterior/siguiente;
- `←` / `→`: celda editable anterior/siguiente;
- `Enter`: editar/aceptar y avanzar;
- `Tab`: siguiente control lógico;
- `Shift+Tab`: anterior;
- `Home` / `End`: inicio/final de fila;
- `Ctrl+Home` / `Ctrl+End`: primera/última línea;
- `Delete`: retirar línea seleccionada con la regla de permiso;
- `Esc`: cancelar edición y regresar a captura.

Solo una celda entra al orden de tabulación; las flechas navegan dentro de la
grilla conforme al patrón ARIA.

### Edición

Cantidad, descuento y precio —este último solo con permiso— se editan en línea.

Al aceptar:

1. valida valor;
2. ejecuta motor local de cálculo;
3. recalcula base, descuento, impuesto y total;
4. muestra el resultado;
5. conserva selección;
6. vuelve a captura cuando termina la operación.

La edición no requiere guardar manualmente toda la factura.

### Lecturas repetidas

Por defecto, escanear el mismo producto:

- incrementa la línea compatible existente;
- resalta la línea durante un instante;
- muestra la nueva cantidad;
- recalcula.

Se crea otra línea si cambian unidad, precio, impuesto, promoción, vendedor o una
condición que impida consolidar.

### Deshacer

`Ctrl+Z` deshace la última mutación local de carrito:

- agregar;
- incrementar;
- cambiar cantidad;
- descuento;
- retirar.

No deshace un pago ya confirmado ni una factura emitida.

---

## 7. Acciones del MVP

Acciones visibles:

```text
Cobrar
Cliente
Guardar temporal
Recuperar temporal
Pedidos
Descuento
Nueva/Limpiar factura
```

Acciones de línea:

```text
Cambiar cantidad
Aplicar/quitar descuento
Cambiar precio con permiso
Eliminar producto
Agregar nota
```

En `Más`:

```text
Cambiar canal de precio
Cambiar vendedor
Reimprimir último comprobante
Ver operaciones pendientes
Configuración de periféricos
```

No entran en el MVP:

```text
apartados
cotizaciones
remisiones
domicilios
puntos
bonos especializados
cheques posfechados
```

Devoluciones tiene módulo propio; no se disfraza como cantidad negativa dentro de
una venta nueva.

---

## 8. Factura temporal

### Guardar

`Guardar temporal` solicita únicamente lo necesario para encontrarla:

```text
nombre corto o cliente opcional
nota opcional
```

El sistema asigna:

```text
SalesDraftId
CashRegisterId
CreatedBy
CreatedAt
estado local/sincronizado
```

Guardar libera inmediatamente la pantalla para una nueva venta.

### Recuperar

La vista muestra:

```text
nombre
cliente
líneas
total estimado
fecha
caja/usuario
estado de sincronización
```

Busca y filtra; `Enter` recupera. Si ya existe un carrito activo, exige guardar o
descartarlo. No mezcla silenciosamente dos facturas.

Los borradores creados offline se pueden recuperar en la misma caja. Cuando
sincronizan, la política define si otras cajas de la bodega pueden recuperarlos.

---

## 9. Pedidos

Pedidos conserva vista propia. Desde Facturación:

- abre selector online;
- permite seleccionar solo uno;
- recupera líneas, cliente, vendedor y condiciones;
- no factura por el acto de recuperar;
- deja la factura editable conforme a permisos y reglas;
- vuelve a la captura.

En la vista propia de Pedidos, el botón `Facturar` puede procesar varios
seleccionados, generando una factura por pedido.

Offline no se buscan pedidos porque no se descargan a la caja.

---

## 10. Pago

Al presionar `Cobrar` se abre un panel lateral, no una cadena de modales:

```text
+----------------------------------+
| Cobrar                 $139.000  |
|                                  |
| [Efectivo] [Tarjeta] [Transfer.] |
|                                  |
| Recibido [150.000________]       |
| Cambio             $11.000       |
|                                  |
| [Pago exacto]                    |
| [Agregar otro medio]             |
|                                  |
| [Confirmar pago]                 |
+----------------------------------+
```

Reglas:

- el medio más usado queda primero según la caja;
- pago exacto evita escribir el monto;
- denominaciones sugeridas reducen digitación;
- pagos mixtos muestran pagado y pendiente;
- errores aparecen dentro del panel;
- `Esc` regresa sin perder el carrito;
- confirmar dos veces no duplica la venta;
- después de éxito se muestra cambio, impresión y estado fiscal;
- luego se prepara automáticamente una nueva venta y enfoca captura.

Crédito:

- exige cliente identificado;
- valida límite/permiso si aplica;
- crea cuenta por cobrar;
- no se trata como un medio informal de efectivo.

---

## 11. Numeración local

Al confirmar, la caja consume transaccionalmente su serie o bloque exclusivo:

```text
SalesInvoiceId UUIDv7
ClientOperationId UUIDv7
DocumentNumber asignado localmente
```

La pantalla advierte:

```text
Rango FV · quedan 25
Rango por agotarse
Rango agotado
Resolución vencida
```

El detalle está en `decision-identificadores-auraly.md`.

---

## 12. Offline

### Puede operar con datos locales

- buscar catálogo descargado;
- resolver barcode;
- aplicar precios y promociones descargados;
- calcular;
- guardar/recuperar borrador local;
- consumir numeración preasignada;
- confirmar ventas permitidas;
- imprimir comprobante con estado correcto;
- encolar sincronización.

### Requiere red

- consultar saldo real;
- bloquear negativos con certeza;
- buscar pedidos;
- traer cliente no descargado;
- validar cupo de crédito central;
- enviar/validar factura electrónica;
- usar medios que requieran proveedor online.

La UI no simula disponibilidad de una capacidad online. Cada acción indica:

```text
Disponible
Disponible con limitación
No disponible sin conexión
Pendiente de sincronización
```

Al reconectar, la sincronización ocurre en segundo plano. La venta actual no
esperará la subida de todas las ventas anteriores, salvo una regla fiscal o de
seguridad que obligue.

---

## 13. Balanza

La balanza entra desde el MVP.

Se soportan:

1. barcode impreso por balanza con peso o precio embebido;
2. balanza conectada mediante Auraly POS Edge, cuando el hardware lo permita.

Al leer:

```text
validar patrón y checksum
resolver producto
obtener peso
convertir a unidad base
calcular valor
agregar línea
volver a captura
```

La línea muestra peso con precisión configurada. Un operador con permiso puede
corregirlo; la corrección queda auditada.

---

## 14. Confirmaciones y acciones destructivas

No se confirma cada operación cotidiana.

Sin confirmación:

- agregar;
- incrementar;
- cambiar cantidad válida;
- abrir búsqueda;
- guardar temporal.

Con confirmación:

- eliminar toda la factura;
- abandonar un carrito no guardado;
- retirar una línea con pago asociado;
- sobrescribir un precio fuera de límites;
- emitir con una excepción autorizada;
- cancelar una factura ya confirmada mediante su flujo legal.

Las confirmaciones destructivas nombran el impacto y el botón peligroso no recibe
foco por defecto.

---

## 15. Permisos

Si el usuario no puede:

- la acción no aparece en menú global;
- puede aparecer deshabilitada en contexto si eso explica una capacidad esperable;
- muestra motivo y permiso requerido;
- la API vuelve a validar.

Permisos mínimos:

```text
Sales.Create
Sales.ApplyLineDiscount
Sales.ApplyInvoiceDiscount
Sales.OverridePrice
Sales.RemoveLine
Sales.ClearDraft
Sales.SaveDraft
Sales.RecoverDraft
Sales.RecoverOrder
Sales.SellOnCredit
Sales.UseOfflineException
Sales.Reprint
```

Un supervisor puede autorizar una acción puntual sin iniciar sesión permanente en
la caja del cajero. La autorización queda ligada a la mutación exacta.

---

## 16. Rendimiento

Objetivos de experiencia:

```text
resolución local de barcode       p95 < 50 ms
lectura hasta línea visible       p95 < 100 ms
búsqueda local inicial            p95 < 150 ms
recalcular hasta 200 líneas        p95 < 100 ms
respuesta visual a tecla/clic      < 100 ms
apertura POS con catálogo listo    según presupuesto de aprovisionamiento
```

La grilla usa virtualización al crecer. No renderiza imágenes grandes ni consulta
el servidor por cada tecla.

Las llamadas online usan cancelación, timeout corto y estado explícito. Un spinner
no toma el foco del lector.

---

## 17. Responsive

### MVP

Prioridad:

```text
escritorio landscape con teclado/lector
tablet landscape
```

No se fuerza la misma composición en teléfono.

### Tablet

- carrito y resumen pueden alternarse;
- botones primarios >= 44 px;
- teclado virtual no tapa total ni confirmación;
- lector integrado conserva captura rápida.

### Pantallas pequeñas

Solo después de pruebas se define layout compacto. No se sacrifica la eficiencia
de caja de escritorio por una responsividad genérica.

---

## 18. Accesibilidad

- foco siempre visible;
- contraste WCAG AA;
- etiquetas además de iconos;
- estados no dependen solo de color;
- grilla operable con teclado;
- lector de pantalla anuncia producto, cantidad y total;
- `aria-live` breve para agregado/error, sin repetir toda la factura;
- botones táctiles primarios de 44 px;
- zoom de navegador sin perder acciones;
- preferencias para sonido y movimiento reducido.

El foco vuelve a captura después de operaciones que terminan. No salta mientras el
usuario está editando una celda.

---

## 19. Estados de la pantalla

Se deben prototipar y probar:

```text
vacía online
vacía offline
con una línea
con muchas líneas
producto desconocido
múltiples coincidencias
producto pesable
cantidad no disponible
catálogo desactualizado
borrador recuperado
pedido recuperado
pago mixto
pago fallido
venta confirmada offline
pendiente fiscal
rango por agotarse
rango agotado
sin permiso
periférico desconectado
```

Una pantalla feliz no es diseño suficiente.

---

## 20. Pruebas de usabilidad

Antes de construir toda la vista, crear prototipo navegable y probar con cajeros.

Tareas:

1. vender diez productos con dos repetidos;
2. corregir cantidad;
3. aplicar descuento autorizado;
4. eliminar una línea;
5. buscar producto sin barcode;
6. vender producto de balanza;
7. guardar y recuperar;
8. recuperar un pedido;
9. cobrar efectivo exacto;
10. cobrar efectivo con cambio;
11. pago mixto;
12. continuar tras perder red;
13. entender una venta pendiente de sincronización;
14. reaccionar a rango por agotarse.

Métricas:

```text
tiempo por tarea
errores
clics/teclas
pérdidas de foco
intervenciones del facilitador
ventas duplicadas
errores de cambio
percepción de confianza
```

Criterios iniciales:

- ninguna lectura exige clic adicional;
- cero ventas duplicadas en reintento;
- un cajero entrenado completa venta básica solo con lector y teclado;
- todos identifican online/offline y pendiente fiscal;
- la acción Cobrar se encuentra sin explicación;
- después de confirmar, el siguiente escaneo inicia la nueva venta.

---

## 21. Componentes web

```text
PosShell
PosOperationalHeader
ProductCapture
ProductSearchPanel
SalesCartGrid
SalesCartLineEditor
CartTotalsPanel
SalesActionsBar
PaymentPanel
SavedDraftsPanel
OrderRecoveryPanel
ConnectivityBanner
CatalogFreshnessIndicator
InvoiceNumberRangeIndicator
PeripheralStatus
SupervisorAuthorizationDialog
```

Estado local:

```text
activeDraft
calculatorSnapshot
focusTarget
connectivity
catalogRevision
numberAllocation
outboxState
peripheralState
```

La lógica fiscal, de precios y totales no vive en componentes React. Los
componentes ejecutan casos de uso y renderizan resultados.

---

## 22. Pruebas automatizadas

### Componentes

- foco inicial y retorno a captura;
- lectura + `Enter`;
- lectura repetida;
- navegación y edición de grilla;
- recálculo;
- permisos;
- panel de pago;
- bandas de estado;
- accesibilidad automática.

### Integración

- lector keyboard wedge;
- balanza/Edge;
- IndexedDB/SQLite;
- pérdida y regreso de red;
- outbox;
- asignación de número;
- impresión;
- recuperación de borrador/pedido.

### E2E

- venta online;
- venta offline;
- reintento idempotente;
- descuento;
- eliminar línea/factura;
- pago mixto;
- crédito y CxC;
- producto sin saldo bajo política bloqueante;
- rango agotado;
- permisos y autorización supervisor.

### Rendimiento

- catálogo grande;
- carrito de 200 líneas;
- ráfaga de lecturas;
- búsqueda mientras sincroniza;
- reconexión con muchas ventas pendientes.

---

## 23. Decisión final

La pantalla Auraly POS será una estación de trabajo web orientada a lector y
teclado, no un formulario administrativo ni un catálogo visual.

El equilibrio es:

- alta densidad donde ayuda a comparar líneas;
- espacio y jerarquía para total y pago;
- acciones frecuentes visibles;
- acciones excepcionales agrupadas;
- edición directa con recálculo;
- estado de red, catálogo y numeración siempre entendible;
- operación local real, sin fingir capacidades que exigen servidor.

El siguiente paso antes de implementar es producir un prototipo de alta fidelidad
de los estados definidos y probarlo con al menos tres usuarios que hayan operado
una caja real.
