IF OBJECT_ID(N'dbo.DispatchReasons', N'U') IS NOT NULL
   AND SCHEMA_ID(N'dispatch') IS NULL
    EXEC(N'CREATE SCHEMA [dispatch] AUTHORIZATION [dbo]');
GO

IF OBJECT_ID(N'dbo.DispatchReasons',N'U') IS NOT NULL
   AND OBJECT_ID(N'dispatch.DispatchReasons',N'U') IS NULL
    ALTER SCHEMA [dispatch] TRANSFER [dbo].[DispatchReasons];
GO
