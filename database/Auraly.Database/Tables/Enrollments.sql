CREATE TABLE [dbo].[Enrollments] (
    [EnrollmentId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ConversationId] UNIQUEIDENTIFIER NOT NULL,
    [ServiceId] UNIQUEIDENTIFIER NOT NULL,
    [PaymentTransactionId] UNIQUEIDENTIFIER NOT NULL,
    [CustomerName] NVARCHAR(200) NOT NULL,
    [CustomerPhone] NVARCHAR(50) NOT NULL,
    [CustomerEmail] NVARCHAR(200) NULL,
    [FixedScheduleLabel] NVARCHAR(500) NULL,
    [Status] INT NOT NULL DEFAULT 0,
    [CustomAttributesJson] NVARCHAR(MAX) NULL,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT [FK_Enrollments_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_Enrollments_Conversations] FOREIGN KEY ([ConversationId])
        REFERENCES [dbo].[Conversations] ([ConversationId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_Enrollments_Services] FOREIGN KEY ([ServiceId])
        REFERENCES [dbo].[Services] ([ServiceId])
        ON DELETE NO ACTION,
    CONSTRAINT [FK_Enrollments_PaymentTransactions] FOREIGN KEY ([PaymentTransactionId])
        REFERENCES [dbo].[PaymentTransactions] ([PaymentTransactionId])
        ON DELETE NO ACTION
);

GO

CREATE INDEX [IX_Enrollments_BusinessId] ON [dbo].[Enrollments] ([BusinessId]);

GO

CREATE INDEX [IX_Enrollments_ConversationId] ON [dbo].[Enrollments] ([ConversationId]);

GO

CREATE INDEX [IX_Enrollments_ServiceId] ON [dbo].[Enrollments] ([ServiceId]);

GO

CREATE UNIQUE INDEX [IX_Enrollments_PaymentTransactionId] ON [dbo].[Enrollments] ([PaymentTransactionId]);

GO
