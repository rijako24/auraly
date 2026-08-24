# Auditoría del snapshot de ventas de Xion

Fecha de revisión: 2026-07-29.

## Alcance inspeccionado

Se revisaron el DDL histórico de `SalidasDeMercancia` y sus tablas detalle, las entidades locales y de servidor, `ZFacturaService`, `DianService` y el flujo de integración electrónica. Xion se usa únicamente como referencia funcional.

## Lo que Xion conserva correctamente

El encabezado conserva número, consecutivo, empresa, sucursal, bodega, cliente/vendedor, fecha y hora de salida, fecha del sistema, prefijo y resolución DIAN, totales, descuentos y valores pagados/crédito.

`SalidasDeMercanciaDetalle` conserva por línea el producto, código alterno, descripciones, cantidad, precio de venta, precio público aplicado, descuentos, IVA, impoconsumo, total, canal/lista/evento y trazabilidad de usuario/equipo. Las tablas relacionadas conservan impuestos agrupados, descuentos, pagos/cartera y artefactos como CUFE/QR o documento retornado.

Esta separación encabezado/líneas/impuestos/pagos es una referencia válida. Los tipos `float`, la clave comercial como PK, los campos no usados y los nombres legacy no se trasladan.

## Brecha crítica encontrada

Xion no conserva un snapshot fiscal completo. `DianService.GenerarInvoice` vuelve a consultar la empresa, la sucursal, la resolución y el cliente desde tablas locales; además calcula fecha y hora con `DateTime.Now`. `SiigoApiService` también vuelve a consultar el tercero. Por ello, una modificación posterior de razón social, dirección, responsabilidades o datos del adquirente puede alterar el documento generado respecto de la venta original.

## Decisión Auraly

Auraly persiste en la outbox y en `FiscalSnapshots.SnapshotJson` el contrato completo y hasheado que existía al emitir:

- identidad interna, número Auraly y número DIAN;
- instante exacto de emisión;
- emisor y adquirente con identificación, dígito, tipo, nombre, responsabilidades, esquema tributario, dirección y contacto;
- autorización, prefijo, rango y vigencia;
- versión emisora y `SoftwareIdentificationCode`, sin PIN ni clave privada;
- líneas con código comercial, esquema, descripción, unidad, cantidad, precio, descuento e impuesto;
- totales tributarios y monetarios;
- forma/medio de pago, vencimiento y referencia;
- CUFE y QR.

El servidor usa la configuración emisora únicamente para comprobar la versión y resolver PIN/certificado por referencia segura. El generador UBL no consulta nombres, direcciones, precios ni datos actuales del cliente o producto.

Los documentos históricos anteriores que no tengan estos datos pasan a `MissingMandatoryFiscalData`; Auraly no completa ni corrige silenciosamente una factura ya emitida.

## Evidencia automatizada

- `FiscalGenerationWorkerTests.Generates_from_the_immutable_snapshot_after_master_data_changes` demuestra que el XML conserva emisor y adquirente congelados aunque el maestro entregue otros nombres.
- `FiscalGenerationSqlTests.Snapshot_is_leased_once_and_persisted_without_reading_changed_master_values` usa SQL Server real, cambia `Businesses` y `FiscalIssuerConfigurations` después de recibir la venta, ejecuta dos workers concurrentes y verifica un único procesamiento y dos artefactos durables basados en el snapshot.
## Impuestos totalizados por tarifa

Xion conserva en `SalidasDeMercanciaImpuestos` una proyección por documento e impuesto con base, IVA, impoconsumo y total. Auraly conserva esa capacidad sin una tabla duplicada: `SalesDocumentLines` congela `TaxCode`, `TaxRate`, base e impuesto por línea y permite agrupar por `DocumentId + TaxCode + TaxRate`; reporting materializa su proyección de lectura en `reporting.SalesReportTaxFacts`. La contabilidad usa el documento fuente inmutable y no depende de una proyección tributaria lateral.

El retiro de las antiguas `SalesDocumentTaxSummaries` y `SalesReturnTaxSummaries` se hace en dos pasos. Esta versión deja de escribirlas y las retira del modelo declarativo. El pipeline normal publica con `DropObjectsNotInSource=False`, por lo que no borra físicamente las tablas mientras todavía pueda existir una API anterior. Después de confirmar que API y workers de esta versión están activos y que no existen consumidores externos, un cutover controlado puede eliminarlas con respaldo, plan destructivo explícitamente revisado y rollback documentado. No se debe habilitar `DropObjectsNotInSource=True` para todo el esquema.

POS Edge conserva las líneas y el snapshot fiscal exactos, suficientes para reintentar. Un conflicto fiscal no genera documento servidor y un reintento idempotente no duplica líneas. La proyección de reporting continúa siendo un modelo de lectura derivado, no otra fuente operativa del impuesto.

## Pagos y Tesorería

Xion usa `Tesoreria` como registro transversal de documentos: conserva tipo de documento, persona, medio, naturaleza de entrada o salida, entidad, autorización, equipo, usuario y fechas, y después alimenta arqueos. También registra cartera por separado para ventas a crédito.

En la rebanada actual, `SalesPayments` es el snapshot canónico de los medios aplicados a la venta y `sales.invoice.processed` es el evento durable de integración. No se crea una segunda tabla de tesorería incompleta: sin la semántica del medio de pago no sería correcto convertir crédito en entrada de caja. El módulo Cash/Treasury deberá consumir el evento para crear movimientos de caja o banco; el módulo Receivables deberá crear la cuenta por cobrar cuando la clasificación sea crédito. Ambos efectos deberán formar parte de una rebanada conectada y probada antes de implementar arqueo.
