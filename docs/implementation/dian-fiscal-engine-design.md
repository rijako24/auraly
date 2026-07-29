# Diseño del motor fiscal DIAN

Fecha de actualización: 2026-07-29.

## Flujo vertical conectado

1. POS Edge emite una venta, congela el snapshot fiscal, calcula CUFE/QR y conserva venta, consecutivos y outbox en SQLite.
2. El uploader durable envía el mismo `DocumentId` a `POST /api/pos/v1/sales`; un timeout no elimina ni renumera la venta local.
3. La API autentica dispositivo, empresa, caja, bodega y permiso; persiste venta, líneas, pagos, snapshot y proceso fiscal en SQL Server.
4. El servidor reconstruye el CUFE con `Auraly.Fiscal.Core`. Una diferencia termina en `FiscalIntegrityConflict` y no produce inventario, pago, XML ni envío.
5. El motor comercial procesa una sola vez inventario, pagos y outbox servidor.
6. `FiscalGenerationHostedService` adquiere `PendingGeneration` con lease SQL, genera UBL 2.1 desde el snapshot, valida los XSD oficiales, firma XAdES-EPES y persiste XML y hashes.
7. `FiscalSubmissionHostedService` adquiere `PendingSubmission`, crea un ZIP determinístico, registra el intento antes de usar la red, ejecuta `SendTestSetAsync` y consulta `GetStatusZip` cuando existe `TrackId`.
8. Aceptación, rechazo, pendiente o reintento quedan en `FiscalDocumentProcesses`, `FiscalTransmissionAttempts`, `FiscalArtifacts` y outbox servidor.
9. POS Edge sondea `GET /api/pos/v1/fiscal/statuses` con dispositivo autenticado y cursor `rowversion`; aplica página y cursor en una transacción SQLite.
10. La API local de POS expone el estado a la pantalla y la reimpresión usa exclusivamente el snapshot local original.

La solicitud HTTP de carga no permanece abierta mientras DIAN procesa. Los workers pueden extraerse después a Azure Functions, WebJob, servicio Windows o contenedor sin cambiar los contratos de aplicación.

## Snapshot e inmutabilidad

Auraly no reconstruye una factura histórica leyendo maestros actuales. El payload congelado contiene emisor, adquirente, autorización/rango, número DIAN, fecha/hora, moneda, líneas, cantidades, unidades, precios, descuentos, impuestos, totales, software y datos mínimos de pago.

La prueba SQL modifica nombres maestros después de recibir la venta y demuestra que el UBL conserva los datos históricos. Si falta un dato obligatorio, el proceso pasa a `MissingMandatoryFiscalData`; el servidor no inventa ni corrige silenciosamente la factura emitida.

La antigua responsabilidad de `SalidaDeMercanciaFolio` no se migra. No hace falta una tabla paralela de folio: `SalesDocuments` conserva número Auraly, número DIAN, prefijos, consecutivos, CUFE recibido/calculado y estado; `FiscalSnapshots` conserva el snapshot exacto, QR, hashes e integridad; `FiscalDocumentProcesses` conserva la evolución fiscal; `FiscalArtifacts` conserva XML, ZIP y respuestas.

## Impuestos y pagos

`SalesDocumentLines` conserva el impuesto de cada línea. Durante el procesamiento idempotente, el servidor agrupa por código y tarifa y crea `SalesDocumentTaxSummaries` con base, impuesto y total. Esta proyección sirve a reportes y no se replica en SQLite.

`SalesPayments` es el modelo canónico inicial de los medios de pago de la venta y reemplaza la responsabilidad útil de Tesorería para esta rebanada. Su clave `(DocumentId, PaymentNumber)` impide duplicados. Cartera, cuentas por cobrar/pagar y movimientos de tesorería más amplios pertenecen a rebanadas posteriores.

## Idempotencia y recuperación

- Venta: unicidad por `BusinessId + DocumentId`, clave idempotente y numeración fiscal.
- Generación: lease con `UPDLOCK`, `READPAST`, `ROWLOCK` y `RowVersion`.
- Artefactos: un tipo/versión por documento; SHA-256 persistido.
- Envío: intento y solicitud sanitizada se guardan antes de llamar a DIAN.
- ZIP: se genera una vez de forma determinística y se reutiliza.
- Resultado: aceptación/rechazo publica un solo evento de outbox.
- Timeout con `TrackId`: pasa a consulta, no crea otro documento.
- Timeout ambiguo sin `TrackId`: queda `PendingDianResult` para intervención/consulta; la retransmisión automática queda bloqueada.
- POS: el cursor solo avanza después de persistir la página; reiniciar no pierde venta, estado ni outbox.

## Seguridad y despliegue

On-premise usa `WindowsFiscalSigningCertificateProvider`. La referencia indica `StoreLocation/StoreName` y thumbprint; el certificado debe tener clave privada, vigencia, titular y cadena válidos. El PIN se resuelve por referencia `env://NOMBRE` y nunca se almacena en snapshot, respuesta, frontend o POS.

Para SaaS sigue pendiente una implementación de la misma abstracción respaldada por Azure Key Vault. No existe fallback que envíe el certificado, PIN o clave privada al POS.

## Límites todavía abiertos

Las pruebas automáticas usan clientes/transportes determinísticos a nivel de contrato y SQL Server real. Todavía no existe un servidor SOAP local que reproduzca completamente envelopes y WS-Security, ni se ejecutó la conectividad real de habilitación. Por eso no se afirma habilitación DIAN aprobada.

La rebanada de clientes/personas debe ser la siguiente rebanada funcional: el snapshot ya admite adquirente histórico, pero falta el módulo canónico para crear clientes desde web/POS y modelar una persona con múltiples establecimientos/roles.