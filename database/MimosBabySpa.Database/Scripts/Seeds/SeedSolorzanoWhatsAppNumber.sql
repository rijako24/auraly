-- =============================================================================
-- SeedSolorzanoWhatsAppNumber.sql
--
-- Configura el numero de WhatsApp de Vinos Artesanales Solorzano y lo enlaza
-- con el agente Camila. Requiere token propio previamente guardado para Solorzano.
-- =============================================================================

SET NOCOUNT ON;

DECLARE @SolorzanoBusinessId UNIQUEIDENTIFIER = 'FCEE3BA9-E6BF-43E2-8C1A-560CB724688B';
DECLARE @SolorzanoAgentId    UNIQUEIDENTIFIER = 'B0EE3BA9-E6BF-43E2-8C1A-560CB724688B';
DECLARE @PhoneNumber         NVARCHAR(20) = N'+573005942096';
DECLARE @WhatsAppPhoneId     NVARCHAR(100) = N'1149544704907546';
DECLARE @WhatsAppBusinessAccountId NVARCHAR(100) = N'2562841327443156';
DECLARE @AccessToken         NVARCHAR(500);

IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId = @SolorzanoBusinessId)
BEGIN
    PRINT N'SeedSolorzanoWhatsAppNumber: negocio Solorzano no encontrado; omitiendo.';
    RETURN;
END

IF NOT EXISTS (SELECT 1 FROM dbo.Agents WHERE AgentId = @SolorzanoAgentId AND BusinessId = @SolorzanoBusinessId)
BEGIN
    PRINT N'SeedSolorzanoWhatsAppNumber: agente Camila no encontrado para Solorzano; omitiendo.';
    RETURN;
END

SELECT TOP (1) @AccessToken = WhatsAppAccessToken
FROM dbo.BusinessWhatsAppNumbers
WHERE BusinessId = @SolorzanoBusinessId
  AND NULLIF(LTRIM(RTRIM(WhatsAppAccessToken)), N'') IS NOT NULL
ORDER BY IsActive DESC, CreatedAt DESC;


IF @AccessToken IS NULL
BEGIN
    PRINT N'SeedSolorzanoWhatsAppNumber: no se encontro token propio de Solorzano; omitiendo numero.';
END
ELSE
BEGIN
    DECLARE @ExistingWhatsAppNumberId UNIQUEIDENTIFIER;

    SELECT TOP (1) @ExistingWhatsAppNumberId = BusinessWhatsAppNumberId
    FROM dbo.BusinessWhatsAppNumbers
    WHERE WhatsAppPhoneNumberId = @WhatsAppPhoneId
       OR BusinessId = @SolorzanoBusinessId
    ORDER BY
        CASE WHEN WhatsAppPhoneNumberId = @WhatsAppPhoneId THEN 0 ELSE 1 END,
        IsActive DESC,
        CreatedAt DESC;

    IF @ExistingWhatsAppNumberId IS NULL
    BEGIN
        INSERT INTO dbo.BusinessWhatsAppNumbers (
            BusinessWhatsAppNumberId,
            BusinessId,
            AgentId,
            PhoneNumber,
            WhatsAppPhoneNumberId,
            WhatsAppBusinessAccountId,
            WhatsAppAccessToken,
            IsActive,
            CreatedAt
        )
        VALUES (
            NEWID(),
            @SolorzanoBusinessId,
            @SolorzanoAgentId,
            @PhoneNumber,
            @WhatsAppPhoneId,
            @WhatsAppBusinessAccountId,
            @AccessToken,
            1,
            GETUTCDATE()
        );
    END
    ELSE
    BEGIN
        UPDATE dbo.BusinessWhatsAppNumbers
        SET BusinessId            = @SolorzanoBusinessId,
            AgentId               = @SolorzanoAgentId,
            PhoneNumber           = @PhoneNumber,
            WhatsAppPhoneNumberId = @WhatsAppPhoneId,
            WhatsAppBusinessAccountId = @WhatsAppBusinessAccountId,
            WhatsAppAccessToken   = @AccessToken,
            IsActive              = 1
        WHERE BusinessWhatsAppNumberId = @ExistingWhatsAppNumberId;
    END

    PRINT N'SeedSolorzanoWhatsAppNumber: WhatsApp configurado para Solorzano.';
END

DECLARE @SolorzanoPlanId UNIQUEIDENTIFIER;

SELECT TOP (1) @SolorzanoPlanId = SubscriptionPlanId
FROM dbo.SubscriptionPlans
WHERE Code = 'essential'
  AND IsActive = 1;

IF @SolorzanoPlanId IS NULL
BEGIN
    PRINT N'SeedSolorzanoWhatsAppNumber: plan essential no encontrado; omitiendo suscripcion de Solorzano.';
END
ELSE IF NOT EXISTS (
    SELECT 1
    FROM dbo.BusinessSubscriptions
    WHERE BusinessId = @SolorzanoBusinessId
      AND Status IN (1, 2, 3)
)
BEGIN
    INSERT INTO dbo.BusinessSubscriptions (
        BusinessId,
        SubscriptionPlanId,
        Status,
        CurrentPeriodStart,
        CurrentPeriodEnd,
        PlanCodeSnapshot,
        PlanNameSnapshot,
        MonthlyPriceCop,
        IncludedCredits,
        MaxVariableCostCop,
        MaxVariableCostPercent,
        ExtraCredits,
        ExtraVariableCostCop,
        AutoRenew
    )
    SELECT
        @SolorzanoBusinessId,
        SubscriptionPlanId,
        1,
        DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1),
        DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1)),
        Code,
        Name,
        MonthlyPriceCop,
        IncludedCredits,
        MaxVariableCostCop,
        MaxVariableCostPercent,
        0,
        0,
        1
    FROM dbo.SubscriptionPlans
    WHERE SubscriptionPlanId = @SolorzanoPlanId;

    PRINT N'SeedSolorzanoWhatsAppNumber: suscripcion essential creada para Solorzano.';
END
ELSE
BEGIN
    PRINT N'SeedSolorzanoWhatsAppNumber: suscripcion propia de Solorzano preservada.';
END

GO

