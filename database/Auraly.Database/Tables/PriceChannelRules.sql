CREATE TABLE [dbo].[PriceChannelRules] (
    [PriceChannelRuleId] UNIQUEIDENTIFIER NOT NULL,
    [PriceChannelId] UNIQUEIDENTIFIER NOT NULL,
    [RuleKind] NVARCHAR(40) NOT NULL,
    [AppliesTo] NVARCHAR(24) NOT NULL,
    [NumericValue] DECIMAL(19, 6) NOT NULL,
    [ValidFrom] DATETIMEOFFSET(7) NOT NULL,
    [ValidUntil] DATETIMEOFFSET(7) NULL,
    [IsActive] BIT NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_PriceChannelRules] PRIMARY KEY ([PriceChannelRuleId]),
    CONSTRAINT [FK_PriceChannelRules_PriceChannels] FOREIGN KEY ([PriceChannelId]) REFERENCES [dbo].[PriceChannels] ([PriceChannelId]),
    CONSTRAINT [CK_PriceChannelRules_RuleKind] CHECK ([RuleKind] IN (N'PercentageVariation')),
    CONSTRAINT [CK_PriceChannelRules_AppliesTo] CHECK ([AppliesTo] IN (N'AllProducts')),
    CONSTRAINT [CK_PriceChannelRules_Value] CHECK ([NumericValue] >= -100 AND [NumericValue] <= 1000),
    CONSTRAINT [CK_PriceChannelRules_Validity] CHECK ([ValidUntil] IS NULL OR [ValidUntil] > [ValidFrom])
);
GO

CREATE UNIQUE INDEX [UX_PriceChannelRules_ActiveKind]
    ON [dbo].[PriceChannelRules] ([PriceChannelId], [RuleKind], [AppliesTo])
    WHERE [IsActive] = 1;
GO
