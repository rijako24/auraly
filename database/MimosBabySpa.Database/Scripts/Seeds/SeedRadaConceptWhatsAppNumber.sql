-- =============================================================================

-- SeedRadaConceptWhatsAppNumber.sql

--

-- Configura el numero de WhatsApp de Rada Concept y lo enlaza con su agente.

-- Idempotente.

-- =============================================================================



SET NOCOUNT ON;



DECLARE @RadaBusinessId      UNIQUEIDENTIFIER = 'AADA0000-0000-0000-0000-000000000001';

DECLARE @RadaAgentId         UNIQUEIDENTIFIER = 'AADA0000-0000-0000-0000-000000000002';

DECLARE @PhoneNumber         NVARCHAR(20) = N'+573007047440';

DECLARE @WhatsAppPhoneId     NVARCHAR(100) = N'1175075569022227';

DECLARE @AccessToken         NVARCHAR(500) = N'EAANr66CaCCkBRpM5KHo2A2zmCzk7qa4yjrnCwOB3KEgwVZBGuGxevngy7qpJktt4XBoVnfbUzNX6QblsdHPT2dcYatCORh0CQI0Y6hrwoddQ3KJtA1CSCawCvcQTBlotBvTwQ8xo7uiXuuAby4jCfWWKjdZC3tPH8ZCvllbgIxHgb7ixyjWa5gAg8SMwpcnBgZDZD';



IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId = @RadaBusinessId)

BEGIN

    PRINT N'SeedRadaConceptWhatsAppNumber: negocio Rada Concept no encontrado; omitiendo.';

    RETURN;

END



IF NOT EXISTS (SELECT 1 FROM dbo.Agents WHERE AgentId = @RadaAgentId AND BusinessId = @RadaBusinessId)

BEGIN

    PRINT N'SeedRadaConceptWhatsAppNumber: agente Rada Concept no encontrado; omitiendo.';

    RETURN;

END



DECLARE @ExistingWhatsAppNumberId UNIQUEIDENTIFIER;



SELECT TOP (1) @ExistingWhatsAppNumberId = BusinessWhatsAppNumberId

FROM dbo.BusinessWhatsAppNumbers

WHERE WhatsAppPhoneNumberId = @WhatsAppPhoneId

   OR BusinessId = @RadaBusinessId

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

        WhatsAppAccessToken,

        IsActive,

        CreatedAt

    )

    VALUES (

        NEWID(),

        @RadaBusinessId,

        @RadaAgentId,

        @PhoneNumber,

        @WhatsAppPhoneId,

        @AccessToken,

        1,

        GETUTCDATE()

    );

END

ELSE

BEGIN

    UPDATE dbo.BusinessWhatsAppNumbers

    SET BusinessId            = @RadaBusinessId,

        AgentId               = @RadaAgentId,

        PhoneNumber           = @PhoneNumber,

        WhatsAppPhoneNumberId = @WhatsAppPhoneId,

        WhatsAppAccessToken   = @AccessToken,

        IsActive              = 1

    WHERE BusinessWhatsAppNumberId = @ExistingWhatsAppNumberId;

END



PRINT N'SeedRadaConceptWhatsAppNumber: WhatsApp configurado para Rada Concept.';

GO

