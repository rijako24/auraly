# Rebanada conectada de devoluciones de venta

Fecha: 2026-08-03

## Diagnóstico

Auraly ya confirma una devolución como documento durable. El motor operacional
procesa exactamente una vez sus líneas e inventario vendible y publica señales
durables; los motores contable y fiscal canónicos procesan respectivamente el
resumen económico/asiento y la nota crédito. No se crea un segundo motor ni una
venta negativa.

La rebanada pendiente debe conectar esa base con consultas y experiencia operativa, y cerrar dos efectos económicos que hoy están incompletos: la aplicación a una cuenta por cobrar y el registro de un reembolso de efectivo dentro de la sesión de trabajo.

## Alcance de esta rebanada

- búsqueda paginada de facturas retornables por número Auraly, número fiscal, CUFE, cliente y producto;
- detalle basado en la factura original, incluyendo cantidades ya devueltas y saldo por línea;
- devolución parcial o total, con cantidades decimales;
- motivo, observación y disposición física por línea;
- consulta paginada e historial de devoluciones;
- confirmación desde la vista web;
- acceso desde facturación online reutilizando el mismo editor;
- reembolso en efectivo por el valor confirmado de la devolución, aunque la venta se haya pagado con otro medio; si el POS aporta una sesión de trabajo abierta, el movimiento queda asociado a esa caja;
- aplicación primero a la cuenta por cobrar originada por la factura y creación de saldo a favor solamente por el excedente;
- inventario, contabilidad y nota crédito mediante los motores canónicos existentes y sus señales de outbox;
- `sales.returns.create` como único permiso operativo para buscar la factura y confirmar la devolución, sin depender del usuario o sesión que emitieron la venta;
- aislamiento por negocio, idempotencia y concurrencia con SQL Server real.

## Reglas económicas

`CustomerCredit` no significa crear siempre un saldo a favor. El motor contable
aplica el valor en este orden:

1. reduce el saldo abierto de la cuenta por cobrar de la factura original;
2. registra un movimiento compensatorio inmutable en el libro CxC;
3. si queda un excedente, crea el saldo a favor del cliente.

El reembolso no puede superar el valor retornable de la venta; en efectivo no
exige que el pago original haya sido en efectivo. Desde POS incluye la sesión abierta del usuario para
afectar su cierre; desde administración puede omitirse y se registra como
liquidación de tesorería/contabilidad, sin inventar un movimiento de caja.

El importe de una devolución incluye la parte del ajuste al peso cobrado en la
factura original. El documento operacional asigna ese ajuste proporcionalmente
al valor acumulado de líneas y cargos devueltos, con cuatro decimales; la última
devolución recibe el remanente exacto. El detalle consultado muestra el ajuste
pendiente para anticipar el importe. Los motores contable y fiscal consumen el
importe ya confirmado; ninguno vuelve a decidirlo.

## Destino físico

- `Sellable`: retorna a inventario vendible en la bodega receptora.
- `NotReturned`: no crea entrada física.
- `Inspection` y `Damaged`: permanecen bloqueados en la interfaz hasta existir una bodega o estado de inventario canónico para cuarentena y averías. No se aceptan silenciosamente sin movimiento.

Esto evita la pérdida contable que produciría marcar un artículo como recibido sin representar dónde quedó.

## Límites explícitos

La primera conexión POS de esta rebanada es online. El navegador web llega mediante
su sesión HttpOnly; una caja enrolada llega a la misma API y a los mismos servicios
canónicos a través del transporte autenticado de Edge. Edge no persiste ni decide
la devolución: sin conexión al servidor, la operación se rechaza explícitamente.

La devolución offline en POS Edge requiere persistencia local del documento,
historial sincronizado de devoluciones, resolución de conflictos y outbox propia;
se construirá como rebanada separada y no se simula llamando al servidor.

Los reversos reales a tarjeta, intereses, cambios de mercancía y devoluciones sin factura original quedan fuera de este corte.

## Ampliación de diseño: historial y reimpresión (2026-09-30)

Estado: implementado localmente; validación y limitaciones del corte al final.
Se conserva una única `SalesReturnWorkspace`, compartida por la entrada del menú
y la ventana del POS, con dos pestañas:

- **Nueva devolución:** conserva la búsqueda de documentos con cantidades
  retornables y el editor actual.
- **Devoluciones realizadas:** consulta devoluciones ya confirmadas, ofrece **Ver detalle** y **Reimprimir** con icono de impresora en cada fila. Imprimir no exige abrir el detalle. No abre el editor de
  creación ni vuelve a confirmar una devolución existente.

### Búsqueda y detalle

Cada pestaña conserva sus propios filtros y página. Solo la pestaña activa
mantiene una consulta habilitada, con un único propietario de carga; no se
consulta el historial como contador auxiliar ni se introduce polling.

| Filtro | Nueva devolución | Devoluciones realizadas |
| --- | --- | --- |
| Buscar | Factura/comprobante original, número fiscal, CUFE o producto según búsqueda existente | Número de devolución o número interno del documento original |
| Cliente | Combo canónico de terceros por `CustomerId` | Mismo combo, filtro por `CustomerId` |
| Desde / hasta | Fecha de la venta | Fecha de la devolución |
| Solo con saldo | Se conserva | No aplica |
| Estado | Se conserva el comportamiento actual | Se muestra el estado operativo y fiscal; el contrato conserva el filtro operativo, sin agregar otro selector local |

Presupuesto local: página de 25 filas en menos de 2 segundos con 1.000 devoluciones; una solicitud HTTP y un comando SQL para página/conteo, dos comandos para detalle y cero lecturas de datos al renderizar. Medir sin builds/suites concurrentes.

El historial muestra número de devolución, documento original, cliente, fecha,
importe devuelto y estados operativo y fiscal separados; el detalle incluye la resolución económica.
La búsqueda existente del historial no cubre CUFE, producto ni número fiscal;
no se anuncia esa cobertura en el campo. La página inicial contiene 25 filas,
con límite de servidor de 100 y total de resultados fiable incluso cuando una
página queda vacía. Aplicar filtros reinicia la página de esa pestaña.

El detalle presenta líneas y cantidades devueltas, motivo, observaciones,
resolución económica, importes, cargos y ajuste al peso cuando existan. Se
reutiliza el documento aceptado; no se recalcula con precios, impuestos o datos
comerciales actuales. El estado fiscal se presenta con su estado real, sin
mostrar una devolución pendiente como nota crédito fiscal validada.

### Impresión heredada de facturas

**Reimprimir** usa la configuración vigente de salida de facturas de la misma
sede/caja y su transporte actual: tirilla de 58/80 mm, media hoja u otro formato
ya soportado. Reutiliza selección de impresora y preparación de marca; no agrega
configuración independiente de devoluciones. En navegador conserva la salida
de navegador; en POS instalado utiliza el transporte de impresión existente.

El contenido identifica la devolución y su documento original. El formato físico
se toma de la configuración actual, pero los importes y datos históricos salen
del snapshot aceptado. Esta acción imprime una copia operacional de la devolución,
con su estado fiscal; no sustituye la representación de la nota crédito electrónica
del artefacto fiscal canónico. La plantilla `sales-return` v1 es independiente de
las versiones publicadas de factura. Los documentos nuevos guardan la versión en
el payload durable; los anteriores sin esa propiedad se representan con v1.

Reimprimir es lectura y salida a impresora: no repite inventario, reembolso,
contabilidad, envío fiscal ni apertura del cajón. Si falla la impresión, informa
el fallo y permite volver a imprimir el mismo documento sin reconfirmarlo.
La disponibilidad de logo/impresora nunca bloquea la confirmación operacional;
se aplica la frontera de marca de las invariantes arquitectónicas.

### Implementación y propietarios

- `SalesReturnQueryService` y `SqlSalesReturnQueryStore` ya ofrecen historial
  paginado y detalle. Se extienden esos contratos; no se crea otra tabla, cola,
  motor ni copia de devoluciones para esta pantalla.
- El frontend y los adaptadores API/Edge conectan historial/detalle al mismo
  propietario de consultas; el transporte de Edge no crea otra proyección local.
- El historial exige el permiso existente de lectura; no se concede por tener
  permiso de creación. El alcance autorizado debe resolver tenant y negocio
  seleccionado de manera consistente también en POS, sin inventar una sesión
  de caja abierta para consultar o reimprimir.
- El detalle incorpora líneas, cargos y ajuste al peso del documento aceptado,
  y la identidad histórica del cliente de la venta original. `SalesReturnReceipt`
  adapta esos importes al contrato compartido de impresión sin volver a calcularlos.
- Los renderers existentes de tirilla HTML, ESC/POS y hoja reconocen la copia
  operacional versionada mediante `SalesReturnPrintDetails`. La representación
  fiscal existente mediante `CreditNotePrintDetails` mantiene su recorrido.

### Evidencia exigida para la implementación

1. Misma vista y comportamiento desde menú, POS web y POS instalado online;
   cambio de pestaña sin consultas de vistas ocultas ni pérdida de filtros.
2. Historial paginado: una solicitud por carga/filtro explícito, consulta SQL
   por conjunto con cantidad acotada de viajes, sin cargar todo ni I/O por fila.
   Detalle e impresión con lecturas acotadas independientes del número de líneas;
   medir viajes y latencia con datos representativos antes de entregar.
3. Verificar tirilla de ambos anchos y media hoja, devoluciones parciales y
   completas, cargos/ajuste al peso y estado fiscal pendiente/validado. Comparar
   datos y totales contra el documento aceptado; conservar las plantillas viejas.
4. Probar aislamiento por tenant/negocio y permiso de lectura en ambos
   transportes. Un identificador ajeno falla también en detalle e impresión.
5. Reimprimir repetidamente y tras fallo de impresora produce cero escrituras
   operacionales/financieras/fiscales y cero aperturas del cajón. Probar además
   que confirmar conserva sus efectos una sola vez, independientemente de imprimir.

No se incorpora historial offline ni una nueva política de sincronización en
esta ampliación. La revisión del precio/descuento del producto genérico es un
recorrido independiente de ventas y reporting, no una regla de devoluciones.

### Evidencia de integración y auditoría del corte

La suite enfocada de SQL real pasó 8 casos el 2026-09-30, incluidas dos
devoluciones parciales con ajuste al peso positivo/negativo, permiso de lectura,
rechazo de negocio distinto al contexto autorizado, total de una página vacía,
filtros de cliente y cuenta, y compatibilidad de opciones compactas de Gastos.
`Two_partial_returns_reverse_the_exact_rounded_sale_total` instrumenta SqlClient:
una página de 25 entre 1.000 devoluciones tomó 97,8 ms, con un comando para
listado/conteo y dos para detalle. Medición local aislada; no es un p95 productivo.

Auditoría: se reutilizan Returns, Accounting, Parties y los transportes/renderers
de impresión existentes. No se agregan tablas, jobs, workers, escrituras financieras
ni polling. El payload de devolución incorpora solamente la versión de su nueva
copia; la clave/hash de confirmación y el writer operacional no cambian. Las
consultas aplican tenant/negocio y permisos; la API web rechaza que el parámetro
de negocio sustituya el contexto ya autorizado. API/Edge conservan autenticación
e identidad del usuario. El listado carga solo con su pestaña activa y cada acción
de detalle/impresión tiene un propietario; renderizar no consulta datos ni marca.

La ampliación de contratos es aditiva y no requiere migración SQL. El frontend
nuevo requiere desplegar primero sus API/Edge compatibles; revertir la UI no borra
datos ni revierte devoluciones aceptadas. No se hizo despliegue. Las impresiones
se validan con renderers y transportes simulados; no se certifica una impresora física.

Regresiones ejecutadas: 61 pruebas Foundation de cálculo/mapeo/renderers, 14 del
host Edge y 5 E2E de navegador (cuentas, tirilla/media hoja, instalada online y
caja preparada), además de los 8 casos SQL. La consulta independiente no prepara
la marca al entrar: la valida solo al reimprimir. La caja preparada usa su copia
local y la devolución incrustada reutiliza la preparación propietaria del POS.
Un fallo de preparación se informa al imprimir y permite usar el nombre disponible.
Se retiró el mapeo duplicado de recibos en el host para conservar referencia,
estado, ajuste al peso y metadata a través del mapeo compartido.
