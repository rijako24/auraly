# Evidencia de implementación DIAN

Fecha de actualización: 2026-07-29.

## Resultado actual

- CUFE validado con vector oficial y truncamiento monetario.
- UBL determinístico y validado por XSD oficial.
- XAdES-EPES verificada criptográficamente; alterar el XML invalida la firma.
- Certificado vencido, de otro emisor o sin clave privada es rechazado.
- Transporte clasifica recepción, pendiente, aceptación, rechazo y timeout ambiguo.
- Cada venta o conflicto crea exactamente un proceso fiscal en la transacción de recepción.
- El snapshot durable ahora incluye emisor, adquirente, autorización/rango, software, metadatos UBL de líneas y pago.
- POS Edge valida la coherencia del snapshot antes de consumir consecutivos y lo conserva en SQLite/outbox.
- Un worker real de `Auraly.Api` adquiere `PendingGeneration` mediante lease SQL con `UPDLOCK`, `READPAST` y `ROWLOCK`.
- El worker genera UBL desde el snapshot histórico, valida XSD, firma y persiste XML sin firmar/firmado con hash en una sola transacción.
- La configuración emisora se usa para verificar la versión y resolver PIN/certificado; nombres, direcciones, precios y datos del cliente no se releen de maestros actuales.
- API fiscal protegida por permisos, aislada por `BusinessId` y paginada en SQL Server.
- DACPAC compilado y desplegado en SQL Server real.
- El detalle conserva la tarifa tributaria y el servidor crea `SalesDocumentTaxSummaries` por código + tarifa dentro de la transacción idempotente de procesamiento; POS Edge no duplica esa proyección en SQLite.
- La prueba multitarifa verifica bases e IVA separados al 5 % y 19 %, repetición sin duplicados y ausencia de proyección ante conflicto.

## Ejecuciones aprobadas

- `Auraly.Foundation.Tests`: 91/91.
- `Auraly.ServerSlice.IntegrationTests`: 22/22 con SQL Server real y despliegue DACPAC.
- `Auraly.Pos.Edge.Host.Tests`: 3/3.
- `Auraly.Commerce.sln` Release: 0 errores, 0 advertencias.
- La prueba SQL cambia los maestros después de recibir la venta y demuestra que el XML conserva el snapshot histórico.
- Dos workers concurrentes producen un único procesamiento y exactamente dos artefactos.

## No aprobado todavía

- Conectividad real con habilitación DIAN: faltan certificado, software, PIN, `TestSetId` y configuración válida.
- El siguiente estado `PendingSubmission` todavía no es consumido por el worker de transmisión/consulta DIAN.
- Sincronización del resultado hacia POS Edge y reimpresión según estado.
- Nota crédito/débito y matriz ejecutable de contingencia.
- Proveedor Azure Key Vault para SaaS; esta entrega incluye proveedor real de almacén de certificados Windows y referencias `env://` para on-premise.

No se declara completa la rebanada DIAN mientras estos puntos sigan pendientes.