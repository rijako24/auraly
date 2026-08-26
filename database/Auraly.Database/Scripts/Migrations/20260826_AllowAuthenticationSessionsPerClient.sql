SET NOCOUNT ON;

IF EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.AuthenticationSessions')
      AND name = N'UX_AuthenticationSessions_User_Active'
)
BEGIN
    DROP INDEX [UX_AuthenticationSessions_User_Active]
        ON [dbo].[AuthenticationSessions];
END;

PRINT N'AuthenticationSessions permite una sesión activa por usuario y cliente.';
