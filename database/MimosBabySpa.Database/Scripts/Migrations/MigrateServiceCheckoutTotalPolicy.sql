-- ============================================================
-- Migration: MigrateServiceCheckoutTotalPolicy
-- Adds a per-tenant service pricing policy used by checkout totals.
-- ============================================================

SET NOCOUNT ON;

IF COL_LENGTH('dbo.Services', 'IncludeInCheckoutTotal') IS NULL
BEGIN
    ALTER TABLE dbo.Services
        ADD IncludeInCheckoutTotal BIT NOT NULL
            CONSTRAINT DF_Services_IncludeInCheckoutTotal DEFAULT (1);

    PRINT N'Columna Services.IncludeInCheckoutTotal agregada.';
END
ELSE
BEGIN
    PRINT N'Columna Services.IncludeInCheckoutTotal ya existe.';
END
GO
