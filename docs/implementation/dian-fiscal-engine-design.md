# Diseño del motor fiscal DIAN

Fecha de actualización: 2026-08-21.

## Flujo vertical conectado

1. POS Edge emite una venta, congela el snapshot fiscal, calcula CUFE/QR y conserva venta, consecutivos y outbox en SQLite.
2. El uploader durable envía el mismo `DocumentId` a `POST /api/pos/v1/sales`; un timeout no elimina ni renumera la venta local.
3. La API autentica dispositivo, empresa, caja, bodega y permiso; persiste venta, líneas, pagos, snapshot y proceso fiscal en SQL Server.
4. El servidor conserva como autoritativo el CUFE creado una sola vez en POS Edge y ejecuta `Auraly.Fiscal.Core` solamente para compararlo, sin reemplazarlo. Una diferencia termina en `FiscalIntegrityConflict` y no produce inventario, pago, XML ni envío.
5. El motor comercial procesa una sola vez inventario, pagos y outbox servidor.
6. `FiscalGenerationHostedService` adquiere `PendingGeneration` con lease SQL, genera UBL 2.1 desde el snapshot, valida los XSD oficiales, firma XAdES-EPES y persiste XML y hashes.
7. `FiscalSubmissionHostedService` adquiere `PendingSubmission`, crea un ZIP determinístico y registra el intento antes de usar la red. En habilitación ejecuta `SendTestSetAsync`: el HTTP/SOAP exitoso y su `ZipKey` sólo prueban recepción, por lo que el worker consulta después `GetStatusZip`. Una respuesta individual válida prueba el documento; el código `2` prueba que el set está aceptado, pero no demuestra que un documento adicional enviado después del cierre haya sido incorporado. En producción ejecuta `SendBillSync`, cuya respuesta `DianResponse` es terminal y no dispara `GetStatusZip`. Ambos caminos usan el mismo XML, firma, ZIP, worker e historial durable.
8. Aceptación, rechazo, pendiente o reintento quedan en `FiscalDocumentProcesses`, `FiscalTransmissionAttempts`, `FiscalArtifacts` y outbox servidor.
9. El servidor notifica cambios fiscales mediante el transporte push configurado; POS Edge descarga entonces los estados posteriores a su cursor durable y los aplica en una transacción SQLite. No existe sondeo periódico.
10. La API local de POS expone el estado a la pantalla y la reimpresión usa exclusivamente el snapshot local original.

La solicitud HTTP de carga no permanece abierta mientras DIAN procesa. Los workers pueden extraerse después a Azure Functions, WebJob, servicio Windows o contenedor sin cambiar los contratos de aplicación.

## Snapshot e inmutabilidad

Auraly no reconstruye una factura histórica leyendo maestros actuales. El payload congelado contiene emisor, adquirente, autorización/rango, número DIAN, fecha/hora, moneda, líneas, cantidades, unidades, precios, descuentos, impuestos, totales, software y datos mínimos de pago.

La prueba SQL modifica nombres maestros después de recibir la venta y demuestra que el UBL conserva los datos históricos. Si falta un dato obligatorio, el proceso pasa a `MissingMandatoryFiscalData`; el servidor no inventa ni corrige silenciosamente la factura emitida.

La antigua responsabilidad de `SalidaDeMercanciaFolio` no se migra. No hace falta una tabla paralela de folio: `SalesDocuments` conserva los datos comerciales de la factura; `FiscalDocuments` es la raíz común para factura y nota crédito; `FiscalSnapshots` y `SalesReturnFiscalSnapshots` conservan los snapshots exactos; `FiscalDocumentProcesses` conserva la evolución fiscal; `FiscalArtifacts` conserva XML, ZIP y respuestas.

Las devoluciones procesadas generan una nota crédito que referencia el número y CUFE originales. Su CUDE se calcula durante la generación fiscal, se persiste una sola vez y se usa sin renumerar en todos los reintentos. Facturas y notas crédito comparten workers, leases, artefactos, intentos y estados, pero conservan snapshots tipados distintos.

La prueba de habilitación de devolución recorre la venta original, devolución parcial, `CreditNote` tipo `91`, concepto de corrección `1`, `ProfileExecutionID=2`, CUDE, firma, `SendTestSetAsync` y `GetStatusZip`. También verifica que el transporte productivo no sea invocado. La activación de producción exige evidencia durable de aceptación del set (`GetStatusZip`, código `2`); la aceptación individual de un documento con código `00` no abre esa puerta.

## Prueba real de nota crédito contra DIAN

El 2026-08-21 se generó con el motor de Auraly la nota crédito `NC260821113748`, referenciada a una factura SETP histórica, validada con XSD, firmada con el certificado fiscal instalado y enviada al endpoint oficial de habilitación. `SendTestSetAsync` entregó el `ZipKey` `b83fccfd-8d1f-469b-8736-b38908b3b754`, por lo que la recepción de ese ZIP por la DIAN quedó comprobada.

`GetStatusZip` devolvió código `2` y señaló que el set existente ya se encontraba aceptado, con `IsValid=false` y sin clave individual. La consulta adicional `GetStatus` por el CUDE de la nota devolvió código `66` (`TrackId no existe`). Por tanto, esta ejecución no se registra como aceptación individual de la nota: el set reutilizado ya estaba cerrado. Para validar una nota crédito nueva debe usarse un `TestSetId` vigente y aún abierto; reutilizar un set aceptado sólo prueba recepción del ZIP y el estado global previo del set.

## Impuestos y pagos

`SalesDocumentLines` conserva el impuesto de cada línea. Reporting agrupa la instantánea comercial por código y tarifa en `reporting.SalesReportTaxFacts`; DIAN y contabilidad consumen sus snapshots o documentos fuente inmutables. No existe una segunda tabla tributaria operacional y esa proyección no se replica en SQLite.

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

## Activación y numeración por sede

La configuración fiscal visible se concentra en un solo onboarding. Razón social, NIT, responsabilidad fiscal y dirección provienen del perfil legal; el usuario sólo entrega `SoftwareId`, `TestSetId`, PIN y certificado PFX/P12. El servidor valida tamaño, contraseña, clave privada única, vigencia, coincidencia exacta normalizada entre el NIT del perfil legal y el `SERIALNUMBER` del titular del certificado, uso de firma, cadena de confianza y una firma criptográfica de prueba antes de guardar.

Después de que el motor registra la aceptación del set de habilitación, Auraly usa `GetNumberingRange` contra producción. Las resoluciones devueltas forman un pool por tenant y conservan su clave técnica cifrada. La activación exige seleccionar una resolución libre para la sede activa. La reserva y la creación de emisor, autorización, series y cursores productivos ocurren en una sola transacción con bloqueo SQL, por lo que dos sedes no pueden tomar la misma resolución. Una asignación activa no se traslada desde la interfaz; una corrección excepcional debe tratarse como operación administrativa auditada y sólo antes de emitir documentos.

El onboarding presenta los ambientes como una progresión, no como un interruptor reversible. El asistente de habilitación abre el POS con factura electrónica fijada y reutiliza la captura, snapshot, firma y workers fiscales canónicos; la intención queda marcada como `FiscalHabilitationOnly`, conserva exclusivamente la evidencia técnica durable exigida para firma, transmisión y auditoría DIAN, y no genera líneas de venta, pagos, cartera, movimientos de inventario, movimientos de sesión, outbox comercial, trabajos contables ni proyecciones de analítica. Producción permanece bloqueada hasta la aceptación durable del set, la consulta de numeración y la selección explícita de una resolución disponible para la sede.

El POS ya no captura resolución, prefijo, rango ni consecutivo inicial. Únicamente consume la configuración activa y bloquea la factura electrónica mientras la sede no haya completado la activación.

## Seguridad y despliegue

En Azure, cada ambiente crea un Key Vault con RBAC, soft delete y purge protection. La identidad administrada del App Service importa un certificado distinto por tenant/sede y guarda el PIN en un secreto separado. SQL sólo conserva referencias opacas, thumbprint y vigencia. El PFX, su contraseña, el PIN y la clave privada nunca vuelven al navegador ni llegan al POS.

En desarrollo local se usa el mismo contrato respaldado por SQL Server con AES-GCM y una clave de protección de 256 bits configurada fuera del repositorio. Se guarda el PFX reexportado sin contraseña, incluida su cadena, dentro del sobre cifrado. El proveedor heredado de almacén Windows/variables de entorno sólo queda como compatibilidad para configuraciones antiguas.

## Límites todavía abiertos

Las pruebas automáticas usan clientes/transportes determinísticos a nivel de contrato y SQL Server real. La integración real sólo puede declararse aceptada para una sede cuando DIAN responda usando sus credenciales válidas; compilar o ejecutar dobles de prueba no sustituye esa aceptación externa.

La rebanada de clientes/personas debe ser la siguiente rebanada funcional: el snapshot ya admite adquirente histórico, pero falta el módulo canónico para crear clientes desde web/POS y modelar una persona con múltiples establecimientos/roles.
