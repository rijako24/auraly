# Contexto rapido para Codex - Talkio AI / Mimos Baby Spa

Este archivo reemplaza las bitacoras antiguas de arquitectura/refactor. Usalo como primer punto de lectura para ahorrar tokens; si hay duda, valida contra el codigo actual.

## Proposito

Backend .NET 8 para automatizar conversaciones de WhatsApp de Mimo's Baby Spa: ventas, catalogo, reservas, pagos, handoff humano y administracion multi-tenant. La entrada principal de mensajeria son Azure Functions.

## Stack

- .NET 8, C#.
- Azure Functions isolated worker.
- Entity Framework Core con SQL Server/Azure SQL.
- Azure OpenAI: texto para agente conversacional, audio/Whisper para transcripcion.
- WhatsApp Cloud API.
- Wompi para links/webhooks de pago.
- Azure Blob Storage para adjuntos/media.

## Estructura principal

- `Auraly.Commerce.sln`: solucion principal.
- `src/API/Auraly.Platform.Worker`: Azure Functions productivas.
- `src/API/Auraly.WebAPI`: API web/admin si se necesita superficie HTTP tradicional.
- `src/Application/Auraly.Platform.Application`: casos de uso, servicios, motor agentic, DTOs, reglas.
- `src/Domain/Auraly.Platform.Domain`: entidades, enums e interfaces de repositorios.
- `src/Infrastructure/Auraly.Platform.Infrastructure`: EF Core, repositorios, servicios externos.
- `src/Console/Auraly.Platform.Console`: utilidades/runner de consola.
- `src/Tests`: pruebas unitarias/integracion y utilidades de testing.
- `database/Auraly.Database`: proyecto SQL y scripts de seed/tablas.
- `admin`: frontend/admin separado si aplica.

## Entradas runtime importantes

Azure Functions actuales en `src/API/Auraly.Platform.Worker/Functions`:

- `WhatsAppWebhookFunction`: recibe mensajes/eventos de WhatsApp.
- `WompiWebhookFunction`: recibe confirmaciones/eventos de pago.
- `PaymentLinkPollerFunction`: consulta links de pago pendientes.
- Confirmacion manual de pagos: accion autenticada en admin (`POST /api/payments/{id}/confirm-manual`).
- `ReleaseConversationFunction`: libera conversaciones escaladas a humano.

La DI principal esta en `src/API/Auraly.Platform.Worker/Program.cs`. Antes de asumir que un servicio existe, confirmar ahi o en el proyecto WebAPI si se trabaja esa superficie.

## Motor agentic

Carpeta principal: `src/Application/Auraly.Platform.Application/Agents`.

Flujo actual:

1. `WhatsAppMessageProcessorService` resuelve negocio, conversacion y agente; inbound controla recibos, idempotencia y debounce.
2. `AgentConversationService` carga configuracion, historial, facts, memoria y estado.
3. `AgentConfigProvider` y `AgentConfigurationCompiler` deserializan y validan `Agents.SettingsJson`.
4. `DeterministicConversationPosition` y `TurnPlanScopeBuilder` determinan flow, etapa y contrato permitido.
5. `LlmTurnPlanner` propone un `TurnPlan` estructurado con facts, signals, decision y directiva de respuesta; no cambia estado directamente.
6. `DeterministicTurnCoordinator` valida el plan, aplica protecciones, gobierna facts, selecciones pendientes y transiciones.
7. `DeterministicStageExecutor` ejecuta las `IAgentOperation` registradas en `Agents/Operations`.
8. Los outcomes agregan efectos y presentaciones autoritativas. `DeterministicResponseRenderer` compone la respuesta y `DeterministicTurnEffectProcessor` persiste/envia efectos.

Configuracion del agente:

- Fuente de verdad: `Agents.SettingsJson` en base de datos.
- Seeds por tenant: `database/Auraly.Database/Scripts/Seeds`.
- `SystemPromptMarkdown` es legacy/fallback; no debe gobernar reglas deterministas.
- Persona, policies, flows, facts, signals, operaciones, outcomes, templates, checkout, commerce y webhooks viven en `SettingsJson`.
- El catalogo no se duplica en prompts: operaciones y adapters lo consultan desde tablas o integraciones.
- La memoria efimera `system.pending_cart_commands` conserva lotes que necesitan aclaracion; el cierre principal se extrae semanticamente mediante el fact con rol `order.finalized`, mientras `commerce.pendingCart` define respaldos deterministas, descartes contextuales y si una finalizacion explicita puede excluir todos los pendientes. `cartReviewRules` protege consultas de solo lectura. Los reemplazos se enlazan semanticamente mediante `replacement_reference`; `productReplacementRules` solo actua como respaldo, el retiro del producto anterior se difiere hasta elegir una opcion inequivoca con cantidad y nunca se inventa cantidad al responder solo “agregame”.

Manual detallado: `docs/agent-engine-manual.md`.

## Datos y dominio

`ApplicationDbContext` expone, entre otras, estas familias:

- Conversacion: `Conversations`, `Messages`, `ConversationContexts`, `ConversationStates`, `CustomerMemory`.
- Comercial/reservas: `Leads`, `Reservations`, `ReservationAddOns`, `Services`, `ServiceCategories`, `ServiceBundleItems`, `ServiceAddOnRules`.
- Recursos/capacidad: `BusinessResources`, `ServiceResourceUsages`, `Employees`, `EmployeeServices`.
- Pagos/inscripciones: `PaymentTransactions`, `Enrollments`.
- Multi-tenant/admin: `Tenants`, `Businesses`, `BusinessWhatsAppNumbers`, `BusinessConfigurations`, `SystemConfigurations`.
- Auth/admin: `AppUsers`, `AppRoles`, `Permissions`, `UserRoles`, `RolePermissions`, `RefreshTokens`, `AuditLogs`.
- Agentic: `AgentTypes`, `Agents`.

Cuando agregues entidad:

1. Crear entidad/enums en Domain.
2. Agregar interfaz de repo si aplica.
3. Registrar `DbSet` y configuracion EF.
4. Implementar repositorio en Infrastructure.
5. Actualizar `UnitOfWork` e interfaces.
6. Actualizar proyecto SQL en `database` si se mantiene schema-first para deploy.
7. Registrar DI en `Program.cs`.
8. Ajustar tests/in-memory repos si existen.

## Pagos y checkout

- Wompi esta detras de `IPaymentLinkService` / `WompiPaymentLinkService`.
- Confirmaciones pasan por `IPaymentConfirmationHandler` y `PaymentLifecycleService`.
- `CheckoutQuoteService`, `PrepareCheckoutTool` y `ResolveCheckoutQuoteTool` manejan cotizaciones/modos.
- El seed de agente define modos de checkout: reservas y enrollments/clases/talleres.
- No marcar reserva como confirmada antes de pago cuando el flujo requiere deposito.

## Reglas importantes del agente

- Responder siempre en espanol al cliente final.
- No inventar precios, disponibilidad, horarios ni servicios: consultar tools/backend.
- Fechas: usar reloj del negocio (`IBusinessClock`, `BusinessToday`) y normalizar a `YYYY-MM-DD` / `HH:mm`.
- Antes de crear o asignar slots, validar fecha no pasada y disponibilidad.
- No decir "confirmado" o "reserve" si aun no hay reserva confirmada.
- Escalar a humano con phrases de kill switch o errores consecutivos segun configuracion.

## Comandos utiles

Build principal:

```powershell
dotnet build Auraly.Commerce.sln
```

Tests de integracion del motor:

```powershell
dotnet run --project src/Tests/Auraly.IntegrationTests/Auraly.IntegrationTests.csproj
```

Publicacion de base de datos:

- Para publicar BD, usar la cadena `ConnectionStrings:DefaultConnection` de `src/Console/Auraly.Platform.Console/appsettings.json`.
- No asumir defaults de `database/Auraly.Database/Scripts/config.json` (`localhost/Auraly`) salvo que el usuario lo pida explicitamente.
- Si se usa el proyecto SQL/DACPAC, derivar `ServerInstance`, `DatabaseName` y credenciales desde ese connection string antes de ejecutar `Publish.ps1`/`SqlPackage`.

Azure Functions local:

```powershell
cd src/API/Auraly.Platform.Worker
func start
```

Migraciones EF manuales, si se usan:

```powershell
cd src/Infrastructure/Auraly.Platform.Infrastructure
dotnet ef database update --startup-project ../../API/Auraly.Platform.Worker/Auraly.Platform.Worker.csproj --context ApplicationDbContext
```

## Documentacion que se conserva

- `README.md`: onboarding general, aunque puede estar algo desactualizado.
- `DEPLOY.md`: notas de despliegue.
- `CONFIGURACION_SECRETOS.md`: claves/settings esperados.
- README especificos dentro de `database`, `infrastructure`, `scripts`, `src` y `docs`.

## Higiene para futuras sesiones

- Evitar crear bitacoras tipo `*_COMPLETADO.md`, `REFACTOR_*.md`, `FIX_*.md` en la raiz.
- Si una decision de arquitectura sigue vigente, actualizar este archivo en lugar de crear otro documento largo.
- Si es una nota temporal, ponerla en el issue/PR o borrarla al terminar.
