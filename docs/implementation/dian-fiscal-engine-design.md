# Diseño del motor fiscal DIAN

## Flujo conectado actualmente

`POST /api/pos/v1/sales` verifica el snapshot y CUFE, persiste la venta y crea en la misma transacción un `FiscalDocumentProcesses`. Un conflicto crea estado `FiscalIntegrityConflict`; una venta verificada crea `PendingGeneration`. La consulta administrativa lee este proceso por `BusinessId` y el reintento solo acepta estados recuperables.

El generador `Auraly.Fiscal.Ubl` construye factura UBL 2.1 determinística, calcula `SoftwareSecurityCode` y valida contra los XSD oficiales. `DianXadesSigner` firma en servidor con XAdES-EPES, valida emisor, vigencia, uso de clave y la firma resultante. `DianHabilitationTransport` implementa el contrato WCF `SendTestSetAsync`/`GetStatusZip`, distingue rechazo funcional de error transitorio y trata un timeout de envío como resultado ambiguo que exige consulta.

## Persistencia

- `FiscalIssuerConfigurations`: versiones inmutables por negocio; conserva referencias a secretos y certificado, nunca PIN ni clave privada.
- `FiscalDocumentProcesses`: máquina de estados durable, lock, intentos, TrackId y próximo intento.
- `FiscalArtifacts`: contenido binario, hash, versión, tipo y versión técnica.
- `FiscalTransmissionAttempts`: operación, correlación, disposición y evidencia de cada llamada.

`SalesDocuments`, `FiscalSnapshots` y `FiscalSeries` siguen siendo propietarios de venta, snapshot y numeración; no se duplican.

## Siguiente conexión obligatoria

Falta implementar el worker que adquiere `PendingGeneration`, resuelve una configuración fiscal versionada y el PIN mediante almacenamiento seguro, reconstruye el UBL desde un snapshot completo, guarda hashes/artefactos, firma, envía o consulta y publica el resultado por outbox. El snapshot de la rebanada anterior aún no contiene todos los datos UBL obligatorios; no se inventarán. Hasta completar ese modelo, esos documentos deben terminar explícitamente en `MissingMandatoryFiscalData`.

La suite WCF moderna expuesta por .NET 8 ofrece `Basic256Sha256`; la interoperabilidad exacta con la suite indicada por el endpoint debe validarse con habilitación real. No se declara conectividad DIAN aprobada sin credenciales y una ejecución real.