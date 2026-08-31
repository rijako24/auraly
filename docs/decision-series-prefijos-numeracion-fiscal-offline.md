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

### Regla para cajas offline

Cada resolución DIAN completa tiene un solo emisor. Para emisión offline se
asigna explícita y exclusivamente a un dispositivo enrolado. No se divide en
bloques, no existe un pool compartido, no hay standby y el enrolamiento no toma
una resolución automáticamente.

La asignación se realiza desde DIAN en una transacción serializable. La base de
datos impide que el mismo rango DIAN se vincule a más de una autorización y que
un dispositivo tenga más de una serie fiscal activa del mismo tipo. Repetir la
misma asignación es idempotente; intentar asignar la resolución o el dispositivo
a otro propietario falla de forma explícita.

POS Edge descarga únicamente la resolución asignada al equipo, incluida su
vigencia, rango completo, prefijo y clave técnica protegida. Mantiene localmente
un cursor durable y atómico. Al llegar al final del rango o vencer la resolución,
bloquea nuevas facturas electrónicas; nunca solicita otro bloque, inventa una
serie ni toma la resolución de otra caja. El administrador debe asignar una nueva
resolución completa conforme al procedimiento auditado.

La asignación y descarga no dependen de que la sede ya haya activado producción.
El paquete de aprovisionamiento lleva por separado la resolución exclusiva y el
estado productivo del emisor. POS Edge conserva la resolución inmediatamente,
pero solo la habilita para emitir cuando producción está activa. La prueba de
habilitación es exclusivamente online, usa el `TestSetId` y la configuración de
ambiente 2; nunca consume la resolución productiva preparada para una caja.
Activar producción no exige que todas las cajas tengan resolución y publica una
invalidación `FiscalProvisioning` para que las que ya la tienen habiliten su
cursor. Una asignación posterior, incluso de reemplazo, publica la misma señal;
la caja desactiva localmente la serie anterior y adopta la nueva sin borrar el
histórico ni renumerar documentos emitidos.

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
- igualdad exacta con el resultado de verificación del CUFE.

El CUFE autoritativo se genera una sola vez en POS Edge. La operación del
servidor es una comparación interna: no crea, sustituye, corrige ni devuelve un
CUFE diferente para la factura emitida.
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
- máximo una resolución DIAN completa y activa por caja/dispositivo y tipo;
- un único propietario por rango DIAN;
- asignación serializable, explícita e idempotente;
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
- dos cajas que compiten por la misma resolución obtienen un solo ganador;
- el equipo recibe exactamente los límites completos del rango DIAN;
- reiniciar POS Edge conserva el cursor local sin reasignar la resolución;
- repetir la consulta de aprovisionamiento no crea autorizaciones ni series.
- una resolución asignada durante habilitación se descarga pero no queda
  disponible para emitir hasta activar producción;
- reemplazar la resolución publica una nueva invalidación y deja una sola serie
  activa tanto en servidor como en POS Edge.

## Decisiones desplazadas

Esta ADR reemplaza cualquier interpretación histórica que permita:

- calcular el número mediante `MAX + 1`;
- compartir una resolución, cursor o rango entre cajas offline;
- dividir una resolución en bloques activos o preparados;
- consumir números al guardar borradores;
- reemplazar en el servidor el número emitido por POS Edge;
- corregir el snapshot después de imprimir;
- usar el número fiscal como identificador interno.

