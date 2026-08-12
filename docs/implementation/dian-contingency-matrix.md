# Matriz fiscal de fallos y contingencia

Esta matriz separa recuperación técnica de una declaración jurídica de contingencia.

| Escenario | Comportamiento Auraly | Estado/decisión |
|---|---|---|
| POS sin Internet | Conserva factura, número, CUFE, QR y outbox; imprime desde snapshot y carga al regresar | `LocallyIssuedPendingSync`; no se denomina automáticamente contingencia DIAN |
| Servidor Auraly no disponible | El uploader conserva el mensaje y aplica backoff | `RetryScheduled` local |
| Servidor sin salida a Internet | Conserva trabajo fiscal durable | `RetryScheduled` |
| DIAN no disponible | Conserva intento y consulta/reintenta según resultado oficial | Solo es contingencia cuando la norma y evidencia oficial lo permitan |
| Timeout de envío con `TrackId` | Consulta `GetStatusZip`; no retransmite la factura | `PendingDianResult` |
| Timeout ambiguo sin `TrackId` | Bloquea retransmisión automática y requiere resolución segura | `PendingDianResult` con evidencia del intento |
| Error XSD | No firma ni transmite | `SchemaValidationFailed` |
| Error de firma | No transmite y conserva error sin secretos | `SignatureFailed` |
| Certificado vencido/incorrecto | Bloquea firma | `SignatureFailed` |
| Resolución vencida o rango agotado | No emite nueva numeración de esa serie | Bloqueo de emisión; documentos existentes no se renumeran |
| Rechazo funcional DIAN | Conserva venta, CUFE, XML y respuesta; no reintenta como timeout | `DianRejected` |
| Aceptación DIAN | Conserva ApplicationResponse y publica evento terminal una vez | `DianAccepted` |
| CUFE inconsistente | No genera, firma ni transmite | `FiscalIntegrityConflict` |
| Snapshot sin datos UBL obligatorios | No inventa ni relee maestros actuales | `MissingMandatoryFiscalData` |
| POS reiniciado durante sincronización | Conserva cursor anterior hasta aplicar la página completa | Reanudación idempotente |

La implementación de documentos de contingencia, notas crédito/débito y leyendas definitivas de tirilla requiere validación en habilitación y fundamento oficial específico.