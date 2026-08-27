# Contexto rapido para Codex - Auraly

Este es el punto de entrada corto al repositorio. Las reglas obligatorias viven en `AGENTS.md`, `docs/estandares-de-ingenieria.md` y `docs/invariantes-arquitectonicas-auraly.md`. El mapa operativo para ubicar cualquier cambio está en `docs/mapa-motores-flujos-y-extensiones.md`.

## Proposito y stack

Auraly es una plataforma comercial multi-tenant: catálogo, ventas/POS, compras, inventario, cartera, pagos, despacho, facturación electrónica DIAN, contabilidad y agentes conversacionales.

- Backend .NET 8/C#, Minimal API y workers.
- SQL Server/Azure SQL con proyecto schema-first en `database/Auraly.Database`.
- Admin/POS en Next.js 16, React 19 y TypeScript.
- Azure Service Bus o RabbitMQ como transportes; SQL conserva el trabajo durable.
- Integraciones fiscales, pagos, WhatsApp, Azure OpenAI y almacenamiento externo mediante adapters.

## Estructura vigente

- `Auraly.Commerce.sln`: solución comercial principal.
- `src/API/Auraly.Api`: composición y superficie HTTP.
- `src/API/Auraly.Platform.Worker`: entrada conversacional y procesos alojados como Functions.
- `src/Modules/*`: contratos, dominio, aplicación e infraestructura por capacidad.
- `src/Infrastructure/Auraly.Infrastructure.Persistence`: persistencia compartida heredada y writers canónicos en migración hacia módulos.
- `src/Pos/*`: host e infraestructura del POS edge.
- `src/Application`, `src/Domain`, `src/Infrastructure/Auraly.Platform.*`: plataforma conversacional.
- `database/Auraly.Database`: schemas, tablas y seeds idempotentes.
- `admin`: admin y POS web.
- `tests` y `src/Tests`: pruebas comerciales, arquitectura, integración y motor conversacional.

## Fuentes de verdad

- Motores, colas, writers y catálogos: `docs/invariantes-arquitectonicas-auraly.md`.
- Flujo y lugar correcto de extensión: `docs/mapa-motores-flujos-y-extensiones.md`.
- Prácticas de implementación: `docs/estandares-de-ingenieria.md`.
- Motor conversacional: `docs/agent-engine-manual.md`. DT-009 está aplazada; no se cambia su diseño por inferencia.
- Esquema desplegable: `database/Auraly.Database`; no crear tablas desde startup.
- Opciones visibles de negocio: tablas de catálogo + API; los enums solo tipan conjuntos cerrados.

## Reglas rápidas

1. Buscar el propietario existente antes de crear servicio, processor, engine, worker, cola, writer, tabla o lista.
   Toda funcionalidad nueva extiende un motor canonico; no se crea otro motor como parte de una implementacion funcional.
2. Inventario se escribe únicamente mediante `SqlInventoryLedgerWriter`, invocado desde handlers del motor documental.
3. Fiscal/DIAN converge en `FiscalProcessingCoordinator`; contabilidad en `AccountingProcessingCoordinator` y `SqlAccountingPostingProcessor`.
4. Un transporte activa el mismo proceso: no contiene reglas ni crea otro motor.
5. API autentica, autoriza, valida el contrato y llama casos de uso; SQL pertenece a persistencia.
6. Usar `IBusinessClock`/`TimeProvider` en reglas dependientes del tiempo.
7. Todo dropdown de negocio consume catálogo persistido. Mapas de iconos/colores son presentación, no catálogo.
8. Despues de implementar, auditar el diff completo contra `AGENTS.md`, estandares, invariantes y decisiones propietarias antes de declarar terminado.

## Motor conversacional

Ruta principal: `WhatsAppMessageProcessorService` → `AgentConversationService` → compilación de `Agents.SettingsJson` → posición/plan determinista → `DeterministicTurnCoordinator` → `DeterministicStageExecutor`/`IAgentOperation` → renderer y efectos.

Las reglas de tenant viven en configuración/seed, no en el motor ni en prompts globales. Catálogo, precios, disponibilidad, reservas y pagos se consultan mediante sus servicios propietarios. Para cualquier cambio leer completo el manual antes de actuar.

## Verificación

```powershell
dotnet build Auraly.Commerce.sln
dotnet test tests/Auraly.Foundation.Tests/Auraly.Foundation.Tests.csproj
cd admin
npm run lint
npm run test:pos
npm run build
```

La publicación de base de datos usa la conexión explícita del entorno objetivo. No asumir `localhost/Auraly` ni publicar sin autorización.

## Higiene documental

No crear bitácoras `*_COMPLETADO.md` o documentos duplicados. Actualizar el documento propietario. Las notas temporales pertenecen al issue/PR.
