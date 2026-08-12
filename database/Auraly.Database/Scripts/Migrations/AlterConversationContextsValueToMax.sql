IF EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.ConversationContexts')
      AND name = N'Value'
      AND max_length <> -1
)
BEGIN
    ALTER TABLE dbo.ConversationContexts ALTER COLUMN [Value] NVARCHAR(MAX) NOT NULL;
END
GO