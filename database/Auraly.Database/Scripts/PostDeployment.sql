-- =============================================================================
-- PostDeployment.sql
--
-- Hook SSDT que se ejecuta despues de publicar el dacpac.
-- Orquesta los seeds idempotentes que dejan la BD lista para ejecutar
-- el motor conversacional determin?stico (extracci?n estructurada).
--
-- Orden:
--   1. Admin user, rol y tenant base
--   2. Configuracion agentic del agente Mimo Bot (AgentType, Agent,
--      PromptSections, KnowledgeSources, link a WhatsApp)
--   3. Categorias de servicio iniciales para negocios sin catalogo
--   4. Recursos y empleados de prueba para el negocio dev
--   5. Negocio dev (tenant 2222, attachments y datos base)
--   6. Politica de agendamiento (settings y horarios por dia de la semana)
--
-- Todos los seeds son idempotentes (MERGE / IF NOT EXISTS) y seguros de
-- re-ejecutar en cada publish.
-- =============================================================================

:r .\Migrations\MigrateMultitenantStateArchitecture.sql
:r .\Migrations\MigratePaymentSnapshotAndReservationCustomAttrs.sql
:r .\Migrations\MigrateConversationStateVerifications.sql
:r .\Migrations\MigrateObsoleteReservationStatuses.sql
:r .\Migrations\MigrateConversationLifecycle.sql
:r .\Migrations\MigrateLeadQualification.sql
:r .\Migrations\MigratePaymentSupersession.sql
:r .\Migrations\MigrateFlowStageSnapshots.sql
:r .\Migrations\MigrateDeterministicRuntimeState.sql
:r .\Migrations\MigrateConversationFollowUp.sql
:r .\Migrations\MigrateConversationCurrentStageName.sql
:r .\Migrations\MigrateSchedulingPolicyToSettings.sql
:r .\Migrations\MigrateSchedulingMinimumLeadTime.sql
:r .\Migrations\MigrateBusinessAvailabilityBlocks.sql
:r .\Migrations\MigrateServiceCheckoutTotalPolicy.sql
:r .\Migrations\BackfillDocumentProcessingPayloads.sql
:r .\Migrations\MigrateServiceKeywords.sql
:r .\Migrations\MigrateNullableServiceCategory.sql
:r .\Migrations\MigrateBabySpaPlanPrices2026.sql
:r .\Migrations\MigrateExternalEscalationAttempts.sql
:r .\Migrations\MigrateExternalEscalationOutcomeDeliveries.sql
:r .\Migrations\MigrateAgentTemplatesAndInboundContacts.sql
:r .\Migrations\MigrateOrderCheckoutPayments.sql
:r .\Migrations\MigrateRemoveOrderAssignmentState.sql
:r .\Migrations\MigrateBusinessTimeZone.sql
:r .\Migrations\AlterConversationContextsValueToMax.sql
:r .\Migrations\MigrateSuppliersToParties.sql
:r .\Migrations\RemoveLegacyTriggers.sql
:r .\Migrations\ClassifySystemWarehouses.sql
:r .\Migrations\MigrateProductsToCanonicalPrices.sql
:r .\Migrations\BackfillPreparedProductPrices.sql
:r .\Migrations\MigratePricePublicationAuditOrigins.sql
:r .\Migrations\RemoveLegacyFiscalConfiguration.sql
GO
:r .\Seeds\SeedReferenceOptions.sql
GO
:r .\Seeds\SeedAuralyGeography.sql
:r .\Seeds\SeedAdminUser.sql
:r .\Seeds\SeedAgentPermissions.sql
:r .\Seeds\SeedCatalogPermissions.sql
:r .\Seeds\SeedSqlAppLoginPermissions.sql
:r .\Seeds\SeedDevBusiness.sql
:r .\Seeds\SeedServiceCategoriesForNewBusinesses.sql
:r .\Migrations\MigrateIniciacionJardinService2026.sql
:r .\Migrations\MigrateBabySpaServiceKeywords2026.sql
:r .\Seeds\SeedWorkSessionPermissions.sql
:r .\Seeds\SeedSalesReturnPermissions.sql
:r .\Seeds\SeedAccountingPermissions.sql
:r .\Seeds\SeedTaxationPermissions.sql
:r .\Seeds\SeedPayablesPermissions.sql
:r .\Seeds\SeedReceivablesPermissions.sql
:r .\Seeds\SeedPurchasingPermissions.sql
:r .\Seeds\SeedFiscalConfigurationPermissions.sql
:r .\Seeds\SeedExpensePermissions.sql
:r .\Seeds\SeedPartyWorkspacePermissions.sql
:r .\Seeds\SeedBillingPlans.sql
:r .\Seeds\SeedRadaConcept.sql
:r .\Seeds\SeedAuraly.sql
:r .\Seeds\SeedAuralyPlatformAdministration.sql
:r .\Seeds\SeedInmobiliariaDemo.sql
:r .\Seeds\SeedLuisPetitBarber.sql
:r .\Seeds\SeedPricingPermissions.sql
:r .\Seeds\SeedGoogleCalendarIntegrations.sql
:r .\Seeds\SeedBackgroundJobs.sql
:r .\Seeds\SeedSolorzanoBusinessIdentity.sql
:r .\Seeds\SeedBusinessWorkingHours.sql
:r .\Seeds\SeedSolorzanoWorkingHours.sql
:r .\Seeds\SeedAgenticConfiguration.sql
:r .\Seeds\SeedSolorzanoWhatsAppNumber.sql
:r .\Migrations\AllowInformationalProductLinks.sql
:r .\Seeds\SeedRadaConceptWhatsAppNumber.sql
:r .\Seeds\SeedSolorzanoDomicilioAgent.sql
:r .\Seeds\SeedCJDistribuciones.sql
GO
:r .\Seeds\SeedDigitalShop.sql
GO
:r .\Seeds\SeedAndinaSantander.sql
GO
:r .\Seeds\SeedAndinaProductCategories.sql
GO
:r .\Seeds\SeedMedidental.sql
GO
:r .\Seeds\SeedProductCategoriesFromProducts.sql
GO
:r .\Seeds\SeedSystemAgentTemplatesAndInboundContacts.sql
GO
:r .\Seeds\SeedCJPaymentApprovalAgent.sql
GO
:r .\Migrations\MigrateLuisWhatsAppToCJ.sql
GO
:r .\Migrations\MigrateCJWhatsAppToMedidental.sql
GO
:r .\Migrations\MigrateMedidentalWhatsAppToDigitalShop.sql
GO
:r .\Migrations\RenameDigitalShopAgentCatalina.sql
GO
:r .\Migrations\BackfillConversationAgents.sql
GO
:r .\Migrations\MigrateAuditAgentTemplates.sql
:r .\Seeds\SeedDefaultResources.sql
:r .\Seeds\SeedWorkshopSchedulesInCatalog.sql
:r .\Seeds\SeedDefaultEmployees.sql
:r .\Seeds\SeedPlanAddOns.sql
:r .\Seeds\CleanupDefaultTestServices.sql
:r .\Seeds\SeedPosEnrollmentPermission.sql
:r .\Seeds\SeedPosIdentityPermission.sql
:r .\Seeds\SeedPosSupervisionPermissions.sql
:r .\Seeds\SeedOrderPermissions.sql
:r .\Seeds\SeedSolorzanoAgentConfiguration.sql
GO
:r .\Migrations\MigrateAgentBotType.sql
GO
:r .\Migrations\BackfillOrderSalesWarehouses.sql
GO

PRINT 'Post-deployment scripts executed successfully.';

:r .\Seeds\SeedInventoryPermissions.sql
:r .\Seeds\SeedInventoryReasons.sql
:r .\Seeds\SeedInventoryDocumentSeries.sql
:r .\Seeds\SeedRoutePermissions.sql
:r .\Seeds\SeedSellerRole.sql
:r .\Seeds\SeedDispatchPermissions.sql
:r .\Seeds\SeedTransporterRole.sql
:r .\Seeds\SeedSalesDocumentSeries.sql
:r .\Seeds\SeedProductMerchandisingMasters.sql
:r .\Migrations\NormalizeLegacyProductCodesAndPricing.sql
:r .\Seeds\SeedDefaultBusinessRoles.sql
:r .\Migrations\MigrateEmployeesAndUsersToParties.sql
:r .\Seeds\EnsureFinalConsumer.sql
:r .\Migrations\BackfillEngineOwnedSourcesAndReportingJobs.sql
:r .\Seeds\SeedAccountingDefaults.sql
:r .\Seeds\SeedComplianceReportDefinitions.sql
:r .\Seeds\SeedDispatchReasons.sql
