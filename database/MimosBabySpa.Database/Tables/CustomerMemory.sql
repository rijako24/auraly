CREATE TABLE [dbo].[CustomerMemory] (
    [CustomerMemoryId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId]       UNIQUEIDENTIFIER NOT NULL,
    [UserNumber]       NVARCHAR(50)     NOT NULL,
    [Field]            NVARCHAR(100)    NOT NULL,
    [Value]            NVARCHAR(MAX)    NOT NULL,
    [UpdatedAt]        DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [FK_CustomerMemory_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE CASCADE
);

GO

CREATE UNIQUE INDEX [UX_CustomerMemory_Business_User_Field]
    ON [dbo].[CustomerMemory] ([BusinessId], [UserNumber], [Field]);

GO
