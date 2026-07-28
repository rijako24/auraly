CREATE TABLE [dbo].[BusinessSubscriptions] (
    [BusinessSubscriptionId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_BusinessSubscriptions] PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    [BusinessId]             UNIQUEIDENTIFIER NOT NULL,
    [SubscriptionPlanId]     UNIQUEIDENTIFIER NOT NULL,
    [Status]                 INT              NOT NULL DEFAULT 1,
    [CurrentPeriodStart]     DATETIME2        NOT NULL,
    [CurrentPeriodEnd]       DATETIME2        NOT NULL,
    [PlanCodeSnapshot]       NVARCHAR(50)     NOT NULL,
    [PlanNameSnapshot]       NVARCHAR(100)    NOT NULL,
    [MonthlyPriceCop]        DECIMAL(18, 2)   NOT NULL,
    [IncludedCredits]        INT              NOT NULL,
    [MaxVariableCostCop]     DECIMAL(18, 2)   NOT NULL,
    [MaxVariableCostPercent] DECIMAL(5, 2)    NOT NULL,
    [ExtraCredits]           INT              NOT NULL DEFAULT 0,
    [ExtraVariableCostCop]   DECIMAL(18, 2)   NOT NULL DEFAULT 0,
    [AutoRenew]              BIT              NOT NULL DEFAULT 1,
    [CreatedAt]              DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
    [UpdatedAt]              DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [FK_BusinessSubscriptions_Businesses] FOREIGN KEY ([BusinessId])
        REFERENCES [dbo].[Businesses] ([BusinessId]),
    CONSTRAINT [FK_BusinessSubscriptions_SubscriptionPlans] FOREIGN KEY ([SubscriptionPlanId])
        REFERENCES [dbo].[SubscriptionPlans] ([SubscriptionPlanId])
);

GO

CREATE INDEX [IX_BusinessSubscriptions_BusinessId] ON [dbo].[BusinessSubscriptions] ([BusinessId]);
GO
CREATE INDEX [IX_BusinessSubscriptions_BusinessId_Status] ON [dbo].[BusinessSubscriptions] ([BusinessId], [Status]);
