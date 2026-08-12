SET NOCOUNT ON;

IF COL_LENGTH('dbo.ConversationStates', 'RuntimeStateJson') IS NULL
BEGIN
    ALTER TABLE dbo.ConversationStates ADD [RuntimeStateJson] NVARCHAR(MAX) NULL;
    PRINT N'MigrateDeterministicRuntimeState: RuntimeStateJson column added.';
END
ELSE
BEGIN
    PRINT N'MigrateDeterministicRuntimeState: RuntimeStateJson column already exists.';
END
GO