# Evidencia: eventos de conciliación de clientes externos

Fecha: 2026-08-02.
Rama: feature/auraly-commerce-external-customer-events.
Base: 82cedf8d7b40aa7c19d8b8128f2a6b903c9cd078.

## Resultado

La conciliación automática quedó conectada de extremo a extremo, sin polling:

    productor externo
      -> repositorio real
      -> ExternalCommerceCustomers + outbox en la misma transacción
      -> dispatcher durable
      -> RabbitMQ o Azure Service Bus
      -> Auraly.Api
      -> Party + Customer + recibo idempotente + invalidación POS

No existe dependencia de Auraly hacia proyectos legacy. Los adaptadores existentes son productores y solo conocen el contrato canónico de Parties.

## Pruebas ejecutadas

- Auraly.Commerce.sln Release: correcto, 0 errores y 0 advertencias.
- MimosBabySpa.sln Release: correcto, 0 errores y 0 advertencias.
- Auraly.Database.sqlproj Release: correcto, 0 errores y 0 advertencias.
- Auraly.Foundation.Tests: 150 correctas.
- Auraly.ServerSlice.IntegrationTests: 96 correctas con SQL Server real, DACPAC y RabbitMQ requerido.
- Auraly.Pos.Edge.Host.Tests: 16 correctas.
- MimosBabySpa.Tests: 701 correctas, incluido SQL Server real y RabbitMQ requerido.
- POS frontend: 25 correctas.
- TypeScript: npx tsc --noEmit correcto.
- Admin: npm run build correcto; 54 páginas estáticas, incluidas /dashboard/parties/imports y /pos.

La prueba ExternalCustomerReconciliationSqlOutboxIntegrationTests despliega una base aislada desde el DACPAC, crea el cliente mediante ExternalCommerceCustomerRepository, confirma el outbox, ejecuta SqlExternalCustomerReconciliationOutboxDispatcher, consume el mensaje persistente desde RabbitMQ y verifica PublishedAt. Un segundo mensaje usa un publicador que falla y verifica que permanece pendiente con AttemptCount, LastError y próxima disponibilidad.

La prueba ExternalCustomerReconciliationRabbitMqTests publica el contrato canónico y prueba consumo válido, entrega duplicada y poison message en dead-letter. ExternalCustomerReconciliationEventTests comprueba con SQL Server real idempotencia, concurrencia, separación por Business y que solo se crean una Party, un Customer, un recibo y una notificación.

## Defectos encontrados por las pruebas y corregidos

- La lectura SQL del dispatcher seguía abierta al confirmar la transacción. Se dispone el lector antes del commit.
- Una carrera entre el canal de activación y el backoff podía dejar una lectura abandonada y perder la siguiente señal. La espera perdedora se cancela y se observa antes de continuar.
- La creación técnica requería falsamente un usuario en Party y Customer. CreatedBy admite nulo para integraciones; el origen Integration queda auditado en el cliente externo. Los flujos manuales conservan ActorId.
- El arnés productor apuntaba a una instancia SQL inválida. Ahora usa .\LOCAL o AURALY_TEST_SQLSERVER, igual que el arnés canónico.
- Una prueba histórica de pagos usaba un código de serie no válido para el modelo canónico. Se corrigió solo la semilla de prueba.
- Una primera prueba E2E hacía que el servidor canónico referenciara infraestructura legacy. Esa dependencia fue eliminada; el contrato se publica en la frontera y las pruebas de arquitectura lo hacen cumplir.

## Garantías verificadas

- El mensaje y el cliente externo se confirman juntos.
- El host solo despierta el dispatcher después del commit y al iniciar para recuperación.
- No hay timer, sondeo SQL ni fallback en memoria.
- Los mensajes Rabbit son persistentes y usan publisher confirms.
- Service Bus usa SessionId por Business; Rabbit usa cola durable, prefetch 1 y ack explícito.
- MessageId es UUIDv7 y el recibo SQL impide repetir efectos.
- Un crash después de publicar puede redeliver, pero no duplica Party, Customer ni notificación.
- Un conflicto de identidad se conserva como resultado de negocio; un fallo técnico reintenta y finalmente llega a dead-letter.
- Un registro Linked no se sobrescribe automáticamente con datos incompletos posteriores del origen.
- No hay TODO, NotImplementedException, secretos o componentes canónicos desconectados en el alcance nuevo.

## Reproducción

Los comandos y variables sin secretos están documentados en docs/implementation/run-external-customer-events.md. Para exigir infraestructura real se definen AURALY_TEST_RABBITMQ, AURALY_REQUIRE_RABBITMQ_TEST=1 y, si SQL no está en .\LOCAL, AURALY_TEST_SQLSERVER.

## Alcance pendiente

La retención y purga de outbox, recibos y dead-letter se definirá en la política operativa transversal. No se agregó un borrado local o automático específico de esta rebanada. La vista administrativa continúa siendo el punto humano para resolver conflictos de identidad.
