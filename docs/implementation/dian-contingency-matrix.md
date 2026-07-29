# Matriz fiscal de fallos y contingencia

Esta matriz separa comportamiento técnico confirmado de una declaración jurídica de contingencia.

| Escenario | Comportamiento Auraly | Clasificación |
|---|---|---|
| POS sin Internet | Conserva factura, CUFE, número y outbox; carga el mismo snapshot al regresar | Pendiente local; no se llama automáticamente contingencia DIAN |
| Servidor sin Internet | Conserva trabajo durable y programa reintento acotado | `RetryScheduled` |
| DIAN no disponible | Conserva intento y consulta/reintenta según respuesta oficial | Solo contingencia cuando la condición y procedimiento oficiales lo permitan |
| Timeout después de enviar | No retransmite a ciegas; consulta `GetStatusZip` con el TrackId si existe | `PendingDianResult` |
| Error XSD | No firma ni transmite; conserva errores y artefacto | `SchemaValidationFailed` |
| Error de firma/certificado | No transmite; conserva evidencia sin secreto | `SignatureFailed` |
| Certificado vencido | Bloquea firma y requiere intervención/configuración válida | `SignatureFailed` |
| Resolución vencida o rango agotado | No emite nueva numeración con esa serie | Bloqueo de emisión; no renumera documentos existentes |
| Rechazo funcional DIAN | Conserva venta, número, CUFE, XML y respuesta; no reintenta como timeout | `DianRejected` |
| CUFE inconsistente | No genera, firma ni transmite | `FiscalIntegrityConflict` |
| Faltan datos UBL del snapshot | No inventa ni corrige el documento emitido | `MissingMandatoryFiscalData` |

La implementación concreta de cada contingencia normativa se realizará solo después de confirmar la regla vigente y probarla en habilitación.