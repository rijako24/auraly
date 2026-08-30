CREATE TABLE [fiscal].[FiscalResolutionAlertSettings]
(
    [BusinessId] UNIQUEIDENTIFIER NOT NULL,
    [ExpirationWarningDays] INT NOT NULL
        CONSTRAINT [DF_FiscalResolutionAlertSettings_ExpirationWarningDays] DEFAULT (3),
    [RemainingNumberWarningThreshold] BIGINT NOT NULL
        CONSTRAINT [DF_FiscalResolutionAlertSettings_RemainingNumberWarningThreshold] DEFAULT (100),
    [UpdatedAt] DATETIMEOFFSET(7) NOT NULL,
    [UpdatedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [RowVersion] ROWVERSION NOT NULL,
    CONSTRAINT [PK_FiscalResolutionAlertSettings] PRIMARY KEY ([BusinessId]),
    CONSTRAINT [FK_FiscalResolutionAlertSettings_Businesses]
        FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]),
    CONSTRAINT [FK_FiscalResolutionAlertSettings_Users]
        FOREIGN KEY ([UpdatedByUserId]) REFERENCES [dbo].[AppUsers]([UserId]),
    CONSTRAINT [CK_FiscalResolutionAlertSettings_ExpirationWarningDays]
        CHECK ([ExpirationWarningDays] BETWEEN 0 AND 365),
    CONSTRAINT [CK_FiscalResolutionAlertSettings_RemainingNumberWarningThreshold]
        CHECK ([RemainingNumberWarningThreshold] BETWEEN 0 AND 1000000000)
);
GO
