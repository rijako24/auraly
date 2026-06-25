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

- `MimosBabySpa.sln`: solucion principal.
- `src/API/MimosBabySpa.API`: Azure Functions productivas.
- `src/API/MimosBabySpa.WebAPI`: API web/admin si se necesita superficie HTTP tradicional.
- `src/Application/MimosBabySpa.Application`: casos de uso, servicios, motor agentic, DTOs, reglas.
- `src/Domain/MimosBabySpa.Domain`: entidades, enums e interfaces de repositorios.
- `src/Infrastructure/MimosBabySpa.Infrastructure`: EF Core, repositorios, servicios externos.
- `src/Console/MimosBabySpa.Console`: utilidades/runner de consola.
- `src/Tests`: pruebas unitarias/integracion y utilidades de testing.
- `database/MimosBabySpa.Database`: proyecto SQL y scripts de seed/tablas.
- `admin`: frontend/admin separado si aplica.

## Entradas runtime importantes

Azure Functions actuales en `src/API/MimosBabySpa.API/Functions`:

- `WhatsAppWebhookFunction`: recibe mensajes/eventos de WhatsApp.
- `WompiWebhookFunction`: recibe confirmaciones/eventos de pago.
- `PaymentLinkPollerFunction`: consulta links de pago pendientes.
- Confirmacion manual de pagos: accion autenticada en admin (`POST /api/payments/{id}/confirm-manual`).
- `ReleaseConversationFunction`: libera conversaciones escaladas a humano.

La DI principal esta en `src/API/MimosBabySpa.API/Program.cs`. Antes de asumir que un servicio existe, confirmar ahi o en el proyecto WebAPI si se trabaja esa superficie.

## Motor agentic

Carpeta principal: `src/Application/MimosBabySpa.Application/Agents`.

Flujo general:

1. `WhatsAppMessageProcessorService` identifica negocio/conversacion y delega al agente.
2. `AgentConversationService` carga configuracion, estado, facts y contexto.
3. `AgentPromptComposer` arma el prompt desde persona, politicas, flow, facts, catalogo/contexto y guardrails.
4. `AzureOpenAIChatClient` ejecuta chat/function calling.
5. `AgentToolRegistry` expone herramientas permitidas por agente/stage.
6. El resultado se persiste y se envia por WhatsApp con servicios outbound.

Herramientas actuales en `Agents/Tools/Impl`:

- `CheckAvailabilityTool`
- `ResolvePricingTool`
- `ResolveCheckoutQuoteTool`
- `PrepareCheckoutTool`
- `CreateReservationTool`
- `AssignPaidSlotTool`
- `RescheduleReservationTool`
- `SuspendReservationTool`
- `GeneratePaymentLinkTool`
- `VerifyPaymentTool`
- `EscalateToHumanTool`
- `GetServiceCatalogTool`
- `SetFactTool`
- `SendMessageSequenceTool`

Configuracion del agente:

- Fuente de verdad: `Agents.SettingsJson` en base de datos.
- Seed principal: `database/MimosBabySpa.Database/Scripts/Seeds/SeedAgenticConfiguration.sql`.
- `SystemPromptMarkdown` es legacy/fallback; no deberia ser la fuente principal.
- Persona, policies, tools habilitadas, flow, guards, factSchema, templates, checkout y webhooks viven en `SettingsJson`.
- El catalogo no debe duplicarse en prompts: `get_service_catalog` lo arma desde tablas de servicios.

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
dotnet build MimosBabySpa.sln
```

Tests de integracion del motor:

```powershell
dotnet run --project src/Tests/MimosBabySpa.IntegrationTests/MimosBabySpa.IntegrationTests.csproj
```

Publicacion de base de datos:

- Para publicar BD, usar la cadena `ConnectionStrings:DefaultConnection` de `src/Console/MimosBabySpa.Console/appsettings.json`.
- No asumir defaults de `database/MimosBabySpa.Database/Scripts/config.json` (`localhost/MimosBabySpa`) salvo que el usuario lo pida explicitamente.
- Si se usa el proyecto SQL/DACPAC, derivar `ServerInstance`, `DatabaseName` y credenciales desde ese connection string antes de ejecutar `Publish.ps1`/`SqlPackage`.

Azure Functions local:

```powershell
cd src/API/MimosBabySpa.API
func start
```

Migraciones EF manuales, si se usan:

```powershell
cd src/Infrastructure/MimosBabySpa.Infrastructure
dotnet ef database update --startup-project ../../API/MimosBabySpa.API/MimosBabySpa.API.csproj --context ApplicationDbContext
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
