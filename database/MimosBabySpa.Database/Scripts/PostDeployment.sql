-- =============================================================================
-- PostDeployment.sql
--
-- Hook SSDT que se ejecuta despues de publicar el dacpac.
-- Orquesta los seeds idempotentes que dejan la BD lista para ejecutar
-- el motor agentico (OpenAI Function Calling).
--
-- Orden:
--   1. Admin user, rol y tenant base
--   2. Configuracion agentic del agente Mimo Bot (AgentType, Agent,
--      PromptSections, KnowledgeSources, link a WhatsApp)
--   3. Categorias de servicio iniciales para negocios sin catalogo
--   4. Recursos y empleados de prueba para el negocio dev
--   5. Negocio dev (tenant 2222, attachments, configuraciones de pago)
--   6. Política de agendamiento (horarios por día de la semana)
--
-- Todos los seeds son idempotentes (MERGE / IF NOT EXISTS) y seguros de
-- re-ejecutar en cada publish.
-- =============================================================================

:r .\Migrations\MigrateMultitenantStateArchitecture.sql
:r .\Migrations\MigratePaymentSnapshotAndReservationCustomAttrs.sql
:r .\Seeds\SeedAdminUser.sql
:r .\Seeds\SeedSqlAppLoginPermissions.sql
:r .\Seeds\SeedServiceCategoriesForNewBusinesses.sql
:r .\Seeds\SeedDevBusiness.sql
:r .\Seeds\SeedSchedulingPolicy.sql
:r .\Seeds\SeedBookingPolicy.sql
:r .\Seeds\SeedAgenticConfiguration.sql
:r .\Seeds\SeedDefaultResources.sql
:r .\Seeds\SeedDefaultEmployees.sql

PRINT 'Post-deployment scripts executed successfully.';
