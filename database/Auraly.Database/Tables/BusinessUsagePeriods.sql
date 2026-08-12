CREATE TABLE [dbo].[BusinessUsagePeriods] (
    [BusinessUsagePeriodId]  UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_BusinessUsagePeriods] PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    [BusinessSubscriptionId] UNIQUEIDENTIFIER NOT NULL,
    [BusinessId]             UNIQUEIDENTIFIER NOT NULL,
    [PeriodStart]            DATETIME2        NOT NULL,
    [PeriodEnd]              DATETIME2        NOT NULL,
    [CreditsIncluded]        INT              NOT NULL,
    [CreditsExtra]           INT              NOT NULL DEFAULT 0,
    [CreditsUsed]            INT              NOT NULL DEFAULT 0,
    [VariableCostLimitCop]   DECIMAL(18, 2)   NOT NULL,
    [VariableCostExtraCop]   DECIMAL(18, 2)   NOT NULL DEFAULT 0,
    [VariableCostUsedCop]    DECIMAL(18, 2)   NOT NULL DEFAULT 0,
    [Status]                 INT              NOT NULL DEFAULT 1,
    [ExceededAt]             DATETIME2        NULL,
    [CreatedAt]              DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
    [UpdatedAt]              DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [FK_BusinessUsagePeriods_BusinessSubscriptions] FOREIGN KEY ([BusinessSubscriptionId])
        REFERENCES [dbo].[BusinessSubscriptions] ([BusinessSubscriptionId]) ON DELETE CASCADE,
    CONSTRAINT [FK_BusinessUsagePeriods_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId])
);

GO

CREATE UNIQUE INDEX [UX_BusinessUsagePeriods_Subscription_Period] ON [dbo].[BusinessUsagePeriods] ([BusinessSubscriptionId], [PeriodStart], [PeriodEnd]);
GO
CREATE INDEX [IX_BusinessUsagePeriods_BusinessId_Period] ON [dbo].[BusinessUsagePeriods] ([BusinessId], [PeriodStart], [PeriodEnd]);
