-- Auraly does not use database triggers. Invariants live in explicit transactional services.
DROP TRIGGER IF EXISTS [dbo].[TR_FiscalSeries_PreventOverlappingActiveRanges];
DROP TRIGGER IF EXISTS [dbo].[TR_Tenants_KeepTenantKeyImmutable];
DROP TRIGGER IF EXISTS [dbo].[TR_AppUsers_EnforceTenantCapacity];
DROP TRIGGER IF EXISTS [dbo].[TR_EnrolledDevices_EnforceTenantCapacity];
GO
