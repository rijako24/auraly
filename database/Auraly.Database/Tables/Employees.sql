CREATE TABLE [dbo].[Employees] (
    [EmployeeId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [PartyId] UNIQUEIDENTIFIER NULL,
    [Name] NVARCHAR(200) NOT NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 NULL,
    CONSTRAINT [FK_Employees_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_Employees_Parties] FOREIGN KEY ([PartyId]) REFERENCES [dbo].[Parties] ([PartyId])
);

GO

CREATE INDEX [IX_Employees_BusinessId] ON [dbo].[Employees] ([BusinessId]);

GO

CREATE INDEX [IX_Employees_BusinessId_Name] ON [dbo].[Employees] ([BusinessId], [Name]);
GO

CREATE UNIQUE INDEX [UX_Employees_BusinessId_PartyId] ON [dbo].[Employees] ([BusinessId], [PartyId]) WHERE [PartyId] IS NOT NULL;


GO
