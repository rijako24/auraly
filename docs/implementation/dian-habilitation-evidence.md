# Evidencia de implementación DIAN

Fecha de actualización: 2026-09-12.

## Capacidades verificadas

- CUFE determinístico con vector oficial y reglas monetarias vigentes.
- UBL 2.1 determinístico, namespaces explícitos y validación con XSD versionados.
- Firma XAdES-EPES en servidor; alterar el XML invalida la firma.
- Rechazo de certificado vencido, de otro emisor o sin clave privada.
- Snapshot histórico completo; cambios posteriores de maestros no alteran el XML.
- Resumen tributario multitarifa por documento, código y porcentaje, sin duplicados.
- Persistencia de pagos de venta, movimiento de inventario y outbox exactamente una vez.
- ZIP de envío determinístico y persistido con hash.
- Intentos `SendTestSetAsync`/`GetStatusZip` durables, correlacionados e idempotentes.
- Aceptación y rechazo conservan factura, CUFE, XML, respuesta y trazabilidad.
- Timeout ambiguo no provoca retransmisión ciega ni renumeración.
- API fiscal y API de estados POS autenticadas y aisladas por `BusinessId`, dispositivo y caja.
- Sincronización incremental por cursor SQL `rowversion` y persistencia transaccional del cursor en SQLite.
- Estado local inicial `LocallyIssuedPendingSync`; aceptación/rechazo/conflicto reemplaza ese estado cuando llega del servidor.
- Reimpresión controlada desde el snapshot original: mismo número Auraly, número DIAN, CUFE, QR y totales, con auditoría SQLite.
- Actualización del esquema SQLite anterior sin borrar facturas, series ni outbox.
- Reglas mandatorias FAB22/FAB23 y FAB34/FAB35 verificadas antes de firmar: el proveedor informa DV en `schemeID` y tipo `31` en `schemeName`.
- Consumidor final certificado de extremo a extremo con `PartyIdentification`, documento `222222222222`, tipo `13`, responsabilidad `R-99-PN` y tributo `ZZ` / `No aplica`.
- Los arreglos `ErrorMessage` devueltos por DIAN se conservan en la trazabilidad, acotados y sin duplicados, en vez de reducir el rechazo a un mensaje genérico.

## Ejecuciones aprobadas

Ejecutadas el 2026-07-29 en el worktree aislado de `feature/auraly-commerce-dian-habilitation`:

- `dotnet build Auraly.Commerce.sln --configuration Release`: 0 errores, 0 advertencias.
- `Auraly.Foundation.Tests`: 97/97.
- `Auraly.Pos.Edge.Host.Tests`: 3/3.
- `Auraly.ServerSlice.IntegrationTests`: 23/23 con SQL Server real y despliegue del DACPAC.
- `Auraly.Database.sqlproj` Release: 0 errores, 0 advertencias.
- `npm run build`: correcto; ruta `/pos` generada.
- `npx tsc --noEmit`: correcto después del build de Next.js.

La prueba SQL principal verifica generación, firma simulable, envío/consulta determinísticos, aceptación, un único ZIP, dos intentos durables, un único evento terminal y consulta incremental autenticada desde POS. Las pruebas SQLite verifican reinicio, cursor durable, outbox intacta y reimpresión fiel.

## Commits de esta continuación

- `82421ca feat: submit fiscal documents durably`
- `fb7a4a4 feat: synchronize fiscal results with POS Edge`

## No aprobado todavía

- Aceptación terminal del set de pruebas con el artefacto corregido: requiere desplegar exactamente el artefacto certificado y recibir código DIAN `00` o `2`.
- Proveedor Azure Key Vault para SaaS.
- Textos definitivos de tirilla para cada resultado/contingencia, sujetos a validación normativa y habilitación.

La implementación tiene conectividad real y conserva la respuesta SOAP de la DIAN, pero no se declara legalmente habilitada hasta obtener una aceptación terminal del set con el artefacto corregido.

## Regresión del incidente Megafruver

Ejecutada el 2026-09-12 antes del despliegue:

- Construcción, XSD, reglas mandatorias, firma y transporte DIAN: 36/36.
- Flujo real de aplicación `POS de habilitación -> checkout técnico -> snapshot -> generación -> firma real -> validación del XML firmado`: 1/1.
- Regresión SQL de generación, factura, nota crédito, nota débito y devolución: 10/10.
- Suite Foundation completa: 461/461.

La aceptación remota del set de pruebas debe registrarse después del despliegue del mismo artefacto y de una respuesta terminal `00` o `2` de la DIAN; hasta entonces no se declara habilitación aceptada.
