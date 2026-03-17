CREATE TABLE [dbo].[BusinessWhatsAppNumbers] (
    [BusinessWhatsAppNumberId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [PhoneNumber] NVARCHAR(20) NOT NULL,
    [WhatsAppPhoneNumberId] NVARCHAR(100) NOT NULL,
    [WhatsAppAccessToken] NVARCHAR(500) NOT NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [FK_BusinessWhatsAppNumbers_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE CASCADE
);

GO

CREATE UNIQUE INDEX [IX_BusinessWhatsAppNumbers_WhatsAppPhoneNumberId] ON [dbo].[BusinessWhatsAppNumbers] ([WhatsAppPhoneNumberId]);

GO

CREATE INDEX [IX_BusinessWhatsAppNumbers_BusinessId] ON [dbo].[BusinessWhatsAppNumbers] ([BusinessId]);

GO
