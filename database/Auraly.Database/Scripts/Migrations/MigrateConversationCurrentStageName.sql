-- MigrateConversationCurrentStageName.sql
-- Agrega el nombre amigable de la etapa actual a Conversations para consultas del admin.

IF COL_LENGTH('dbo.Conversations', 'CurrentStageName') IS NULL
BEGIN
    ALTER TABLE dbo.Conversations ADD [CurrentStageName] NVARCHAR(100) NULL;
    PRINT N'MigrateConversationCurrentStageName: CurrentStageName column added.';
END
ELSE
BEGIN
    PRINT N'MigrateConversationCurrentStageName: CurrentStageName column already exists.';
END
