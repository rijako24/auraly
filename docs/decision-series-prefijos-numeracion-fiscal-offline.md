# Decisión: series, prefijos y numeración documental offline

**Estado:** aceptada y prevalente  
**Fecha:** 27 de julio de 2026  
**Alcance:** Auraly Commerce MVP

## Contexto

Auraly separa el identificador técnico de un documento de su número visible. El
identificador interno es un UUIDv7 y no tiene significado fiscal ni comercial.
La numeración visible debe ser legible, auditable y, cuando corresponda, cumplir
la autorización de numeración de la DIAN.

El POS puede confirmar e imprimir facturas sin conexión. Por tanto, el número
fiscal, el prefijo y los datos necesarios para calcular el CUFE deben quedar
determinados localmente antes de imprimir. El servidor no puede reemplazarlos ni
corregirlos después.

## Decisión

### Identidad y número visible

Cada documento conserva dos conceptos distintos:

- `DocumentId`: UUIDv7 globalmente único, generado donde nace el documento.
- `DocumentNumber`: número comercial o fiscal visible, asignado por una serie.

El número visible nunca se usa como clave primaria ni como mecanismo de
idempotencia. La idempotencia usa `DocumentId` y una clave explícita de
operación.

### Serie documental

`DocumentSeries` define la política de numeración de un tipo de documento:

- empresa y establecimiento propietarios;
- tipo de documento;
- código o prefijo visible;
- siguiente consecutivo;
- límite inferior y superior;
- vigencia;
- estado;
- ámbito de asignación;
- caja exclusiva cuando la serie sea fiscal y permita emisión offline;
- versión para concurrencia;
- reglas de formato.

Las series internas son configurables para pedidos, entradas de mercancía,
traslados, movimientos manuales, averías, devoluciones, conversiones y arqueos.
Esos documentos no deben reutilizar una serie fiscal.

El número externo de una factura de proveedor se conserva como referencia del
tercero y nunca sustituye el número interno de Auraly.

### Autorización fiscal

`FiscalAuthorization` representa la resolución o autorización vigente:

- empresa o responsable fiscal;
- ambiente;
- tipo de documento fiscal;
- número y vigencia de la autorización;
- rango autorizado;
- prefijo autorizado;
- versión cifrada de la clave técnica;
- estado, rotación y revocación;
- metadatos normativos requeridos para el snapshot fiscal.

La clave técnica no llega a React, no se escribe en logs y se almacena cifrada
en POS Edge. El certificado privado de firma permanece exclusivamente en el
servidor o en su almacén seguro.

### Regla del MVP para cajas offline

Cada caja que pueda emitir facturas electrónicas sin conexión tiene una serie
fiscal exclusiva. Dos cajas offline no comparten la misma serie ni el mismo
cursor de consecutivos.

La exclusividad evita depender de reservas distribuidas o de coordinación entre
cajas desconectadas. La asignación por bloques queda fuera del MVP y requerirá
una ADR posterior con reglas de reserva, expiración, recuperación y auditoría de
rangos no consumidos.

Una serie fiscal solo puede estar activa en una instalación POS Edge a la vez.
El enrolamiento registra el dispositivo propietario. El cambio de dispositivo,
la revocación o la reasignación exigen cerrar la asignación anterior y dejan
traza auditable.

### Consumo del número

Un borrador no consume numeración. Al confirmar una venta, POS Edge ejecuta en
una única transacción local durable:

1. valida que la serie esté activa, vigente y dentro del rango autorizado;
2. consume el siguiente consecutivo de manera atómica;
3. crea el snapshot fiscal inmutable;
4. calcula CUFE y QR con `Auraly.Fiscal.Core`;
5. persiste factura, snapshot y mensaje de outbox;
6. confirma la transacción;
7. imprime la representación local.

Si la transacción local falla antes del `commit`, el número no se considera
consumido. Después del `commit` el número no se reutiliza, aunque falle la
impresión, la sincronización, la transmisión fiscal, se anule el documento o el
dispositivo se apague.

Los saltos y fallos posteriores quedan registrados en `NumberingAuditEvent`.

### Snapshot fiscal

El snapshot inmutable incluye, como mínimo:

- `DocumentId`;
- número completo, prefijo y consecutivo;
- autorización fiscal y rango;
- fecha y hora de emisión con zona horaria;
- identificación del emisor y adquirente;
- líneas, cantidades, precios y descuentos;
- impuestos y totales;
- medios y forma de pago requeridos fiscalmente;
- ambiente;
- versión de la clave técnica usada;
- CUFE y datos del QR.

Después de imprimir no se modifica ningún campo que participe en el CUFE. Un
cambio comercial posterior se expresa mediante el documento fiscal
correspondiente, no editando la factura emitida.

### Verificación en servidor

Al recibir una factura, el servidor valida de forma idempotente:

- propiedad y asignación de la serie;
- vigencia y rango de la autorización;
- unicidad de empresa, tipo fiscal, prefijo y consecutivo;
- integridad del snapshot;
- igualdad exacta del CUFE recalculado;
- coincidencia entre el documento recibido y el mensaje de outbox.

Si el CUFE no coincide, el documento pasa a `FiscalIntegrityConflict`. La venta
local y su evidencia no se eliminan ni se corrigen silenciosamente.

El servidor conserva el snapshot exacto, genera y firma el XML UBL, transmite a
la DIAN y registra estados y respuestas. Que el XML se genere no demuestra por
sí solo cumplimiento fiscal.

### Conectividad

Con Internet se conserva el mismo flujo local: asignación, snapshot, CUFE,
outbox e impresión. La sincronización se intenta inmediatamente y el servidor
continúa el proceso fiscal.

Sin Internet, la factura queda `LocallyIssuedPendingSync` y se imprime con el
mismo número, CUFE y QR que el servidor verificará al recuperar la conexión.

## Restricciones de base de datos

El proyecto SQL Database es el único dueño de la evolución del esquema. Debe
garantizar:

- unicidad del número fiscal por empresa, tipo, prefijo y consecutivo;
- una sola asignación activa de serie fiscal por caja/dispositivo;
- concurrencia optimista para cursores de series del servidor;
- inmutabilidad lógica del snapshot fiscal emitido;
- auditoría de consumo, revocación, agotamiento y reasignación.

No se permite `MAX(numero) + 1`.

## Pruebas obligatorias

- dos confirmaciones concurrentes no reciben el mismo consecutivo;
- cerrar y abrir POS Edge conserva cursor, factura y outbox;
- un reintento no consume otro número;
- una factura confirmada nunca reutiliza su número;
- una caja no puede activar la serie exclusiva de otra caja;
- agotamiento, expiración y revocación bloquean nuevas emisiones;
- POS Edge y servidor calculan exactamente el mismo CUFE;
- alterar número, prefijo, resolución o cualquier dato fiscal produce
  `FiscalIntegrityConflict`;
- el documento duplicado se procesa una sola vez;
- una caída durante sincronización reanuda sin renumerar.

## Decisiones desplazadas

Esta ADR reemplaza cualquier interpretación histórica que permita:

- calcular el número mediante `MAX + 1`;
- compartir una misma serie entre cajas offline en el MVP;
- consumir números al guardar borradores;
- reemplazar en el servidor el número emitido por POS Edge;
- corregir el snapshot después de imprimir;
- usar el número fiscal como identificador interno.

