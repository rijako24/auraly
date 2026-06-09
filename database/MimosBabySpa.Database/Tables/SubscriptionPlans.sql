CREATE TABLE [dbo].[SubscriptionPlans] (
    [SubscriptionPlanId]       UNIQUEIDENTIFIER NOT NULL CONSTRAINT [PK_SubscriptionPlans] PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    [Code]                     NVARCHAR(50)     NOT NULL,
    [Name]                     NVARCHAR(100)    NOT NULL,
    [MonthlyPriceCop]          DECIMAL(18, 2)   NOT NULL,
    [IncludedCredits]          INT              NOT NULL,
    [MaxVariableCostCop]       DECIMAL(18, 2)   NOT NULL,
    [MaxVariableCostPercent]   DECIMAL(5, 2)    NOT NULL,
    [IncludedAgents]           INT              NOT NULL DEFAULT 1,
    [IncludedUsers]            INT              NOT NULL DEFAULT 1,
    [IncludedWorkspaces]       INT              NOT NULL DEFAULT 1,
    [FeaturesJson]             NVARCHAR(MAX)    NOT NULL DEFAULT N'[]',
    [IsActive]                 BIT              NOT NULL DEFAULT 1,
    [CreatedAt]                DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
    [UpdatedAt]                DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME()
);

GO

CREATE UNIQUE INDEX [UX_SubscriptionPlans_Code] ON [dbo].[SubscriptionPlans] ([Code]);
