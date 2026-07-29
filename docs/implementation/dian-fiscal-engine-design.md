# Diseño del motor fiscal DIAN

## Flujo conectado actualmente

`POST /api/pos/v1/sales` verifica CUFE, persiste la venta, el snapshot exacto y crea `FiscalDocumentProcesses` en la misma transacción. Un conflicto queda en `FiscalIntegrityConflict`; una venta válida con versión emisora queda en `PendingGeneration`.

`FiscalGenerationHostedService` consume trabajo en lotes cortos sin mantener la solicitud HTTP abierta. `SqlFiscalGenerationWorkStore` adquiere un documento con lease durable y exclusión concurrente. `FiscalGenerationWorker` construye UBL 2.1 determinístico desde el snapshot, valida XSD oficial, firma en servidor y guarda los artefactos y sus hashes antes de mover el proceso a `PendingSubmission`.

`DianHabilitationTransport` implementa el contrato WCF `SendTestSetAsync`/`GetStatusZip`, distingue rechazo funcional de error transitorio y trata un timeout de envío como resultado ambiguo que exige consulta. Su consumo durable desde `PendingSubmission` es el siguiente tramo.

## Inmutabilidad

La auditoría de Xion demostró que `SalidasDeMercanciaDetalle` conserva precios, cantidades, descripciones e impuestos, pero su generador vuelve a consultar empresa, resolución y cliente, además de usar la hora actual. Auraly corrige esa brecha: el payload hasheado incluye emisor, adquirente, autorización, rango, software, líneas y pago tal como existían al emitir.

El XML nunca usa nombres o direcciones actuales de `Businesses`, clientes o productos. `FiscalIssuerConfigurations` aporta la versión y referencias seguras al PIN/certificado. Si falta un dato histórico obligatorio, el estado es `MissingMandatoryFiscalData`; no se inventa ni corrige.

## Persistencia

- `FiscalIssuerConfigurations`: versiones por negocio; referencias a secretos y certificado, nunca PIN ni clave privada.
- `FiscalDocumentProcesses`: máquina de estados durable, lease, intentos, `TrackId` y próximo intento.
- `FiscalArtifacts`: XML binario, SHA-256, versión, tipo y versión técnica.
- `FiscalTransmissionAttempts`: operación, correlación, disposición y evidencia de cada llamada.
- `FiscalSnapshots`: contrato completo serializado y hash del payload recibido.
- `SalesDocumentTaxSummaries`: proyección por documento, código y tarifa; conserva base, impuesto y total para reportes. Se deriva únicamente de las líneas congeladas al procesar y no se replica en SQLite.

`SalesDocuments`, `FiscalSnapshots` y `FiscalSeries` siguen siendo propietarios de venta, snapshot y numeración; no se duplican.

## Proveedores y despliegue

On-premise usa `WindowsFiscalSigningCertificateProvider` con referencia `StoreLocation/StoreName`; el certificado se busca por thumbprint y se exige cadena confiable en ejecución real. El PIN se resuelve mediante referencia `env://NOMBRE`, nunca desde el snapshot ni `appsettings`.

SaaS debe agregar la implementación Azure Key Vault de las mismas abstracciones antes de habilitar tenants reales. No existe un fallback que exponga PFX, PIN o clave privada al POS/frontend.

## Siguiente conexión obligatoria

Consumir `PendingSubmission`, crear ZIP e intento durable, enviar o consultar DIAN según exista `TrackId`, persistir respuesta/ApplicationResponse y avanzar a `DianAccepted`, `DianRejected`, `PendingDianResult` o `RetryScheduled`. Después se publicará el cambio por outbox y POS Edge lo sincronizará mediante sondeo incremental.

La interoperabilidad exacta WCF debe validarse con habilitación real. No se declara conectividad DIAN aprobada sin credenciales y una ejecución real.