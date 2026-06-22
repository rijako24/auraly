-- =============================================================================
-- SeedSolorzanoWhatsAppNumber.sql
--
-- Configura el numero de WhatsApp de Vinos Artesanales Solorzano y lo enlaza
-- con el agente Camila. Usa el token activo de Mimos para no escribir secretos
-- nuevos en el repositorio.
-- =============================================================================

SET NOCOUNT ON;

DECLARE @MimosBusinessId     UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';
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
    SELECT TOP (1) @AccessToken = WhatsAppAccessToken
    FROM dbo.BusinessWhatsAppNumbers
    WHERE BusinessId = @MimosBusinessId
      AND IsActive = 1
      AND NULLIF(LTRIM(RTRIM(WhatsAppAccessToken)), N'') IS NOT NULL
    ORDER BY CreatedAt DESC;
END

IF @AccessToken IS NULL
BEGIN
    PRINT N'SeedSolorzanoWhatsAppNumber: no se encontro token activo de Mimos; omitiendo numero.';
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

DECLARE @MimosSubscriptionId UNIQUEIDENTIFIER;

SELECT TOP (1) @MimosSubscriptionId = BusinessSubscriptionId
FROM dbo.BusinessSubscriptions
WHERE BusinessId = @MimosBusinessId
  AND Status IN (1, 2, 3)
ORDER BY CreatedAt DESC;

IF @MimosSubscriptionId IS NULL
BEGIN
    PRINT N'SeedSolorzanoWhatsAppNumber: suscripcion activa de Mimos no encontrada; omitiendo copia de plan.';
END
ELSE IF EXISTS (
    SELECT 1
    FROM dbo.BusinessSubscriptions
    WHERE BusinessId = @SolorzanoBusinessId
      AND Status IN (1, 2, 3)
)
BEGIN
    UPDATE target
    SET SubscriptionPlanId     = source.SubscriptionPlanId,
        Status                 = source.Status,
        CurrentPeriodStart     = source.CurrentPeriodStart,
        CurrentPeriodEnd       = source.CurrentPeriodEnd,
        PlanCodeSnapshot       = source.PlanCodeSnapshot,
        PlanNameSnapshot       = source.PlanNameSnapshot,
        MonthlyPriceCop        = source.MonthlyPriceCop,
        IncludedCredits        = source.IncludedCredits,
        MaxVariableCostCop     = source.MaxVariableCostCop,
        MaxVariableCostPercent = source.MaxVariableCostPercent,
        ExtraCredits           = source.ExtraCredits,
        ExtraVariableCostCop   = source.ExtraVariableCostCop,
        AutoRenew              = source.AutoRenew,
        UpdatedAt              = SYSUTCDATETIME()
    FROM dbo.BusinessSubscriptions target
    CROSS JOIN dbo.BusinessSubscriptions source
    WHERE source.BusinessSubscriptionId = @MimosSubscriptionId
      AND target.BusinessId = @SolorzanoBusinessId
      AND target.Status IN (1, 2, 3);

    PRINT N'SeedSolorzanoWhatsAppNumber: suscripcion de Solorzano alineada con Mimos.';
END
ELSE
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
    FROM dbo.BusinessSubscriptions
    WHERE BusinessSubscriptionId = @MimosSubscriptionId;

    PRINT N'SeedSolorzanoWhatsAppNumber: suscripcion de Mimos copiada a Solorzano.';
END

GO

