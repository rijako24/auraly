-- Migration: MigrateServiceKeywords
-- Adds configurable comma-separated search keywords for catalog service resolution.

IF COL_LENGTH('dbo.Services', 'Keywords') IS NULL
BEGIN
    ALTER TABLE dbo.Services ADD [Keywords] NVARCHAR(1000) NULL;
END
GO