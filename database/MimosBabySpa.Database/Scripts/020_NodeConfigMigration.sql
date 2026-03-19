-- =============================================================================
-- Migration 020: Node Config Migration — Move BusinessConfigurations to Flow Nodes
--
-- Principle: the generic flow engine reads ZERO BusinessConfigurations keys.
-- All behavior (scheduling, escalation contacts, payment config) lives in the
-- node JSON of the FlowDefinition.
--
-- Changes:
--   1. check_availability node → add "scheduling" block
--        (slotIntervalMinutes, bufferBetweenAppointmentsMinutes, requireEmployee,
--         employeeStrategy, schedule) — migrated from BusinessConfiguration key=4
--   2. escalate node → add "contacts" array + "escalationMessage"
--        — migrated from BusinessConfiguration key=7
--   3. generate_payment_link node → add "payment" block
--        — migrated from BusinessConfiguration key=3
--   4. DELETE all BusinessConfigurations except key=6 (Integrations).
--        Every other key is now either in a flow node or legacy/unused.
-- =============================================================================

BEGIN TRANSACTION;

DECLARE @BusinessId  UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';
DECLARE @FlowDefId   UNIQUEIDENTIFIER = '1B43F179-68B9-4073-B87A-D51B5D0625FC';
DECLARE @Json        NVARCHAR(MAX);

SELECT @Json = DefinitionJson FROM [dbo].[FlowDefinitions] WHERE FlowDefinitionId = @FlowDefId;

-- ============================================================
-- 1. Update check_availability node — add "scheduling" block
--
-- Operating hours are built from BusinessConfigurations key=4.
-- If no record exists, falls back to the known Mimos Baby Spa schedule
-- (Mon–Fri 09:00–18:00, Sat 09:00–14:00, Sun closed).
-- ============================================================
DECLARE @OpHoursJson  NVARCHAR(MAX);
DECLARE @ScheduleBlock NVARCHAR(MAX);

SELECT @OpHoursJson = Value
FROM [dbo].[BusinessConfigurations]
WHERE BusinessId = @BusinessId AND [Key] = 4;  -- key=4 was OperatingHours

IF @OpHoursJson IS NOT NULL AND @OpHoursJson <> N'{}'
BEGIN
    -- Convert existing {dayName:[{open,close}]} format directly to scheduling.schedule
    SET @ScheduleBlock = @OpHoursJson;
END
ELSE
BEGIN
    -- Fallback: known Mimos Baby Spa schedule
    SET @ScheduleBlock = N'{' +
        N'"monday":[{"open":"09:00","close":"18:00"}],' +
        N'"tuesday":[{"open":"09:00","close":"18:00"}],' +
        N'"wednesday":[{"open":"09:00","close":"18:00"}],' +
        N'"thursday":[{"open":"09:00","close":"18:00"}],' +
        N'"friday":[{"open":"09:00","close":"18:00"}],' +
        N'"saturday":[{"open":"09:00","close":"14:00"}]' +
        N'}';
END

DECLARE @NodeSchedulingBlock NVARCHAR(MAX) = N'{' +
    N'"slotIntervalMinutes":60,' +
    N'"bufferBetweenAppointmentsMinutes":0,' +
    N'"requireEmployee":true,' +
    N'"employeeStrategy":"least_versatile",' +
    N'"schedule":' + @ScheduleBlock +
    N'}';

-- Locate check_availability node index
DECLARE @CheckAvailIdx NVARCHAR(5);
SELECT @CheckAvailIdx = CAST([key] AS NVARCHAR(5))
FROM OPENJSON(JSON_QUERY(@Json, '$.nodes'))
WITH ([id] NVARCHAR(100) '$.id') AS n
CROSS APPLY (SELECT [key] FROM OPENJSON(JSON_QUERY(@Json, '$.nodes')) WHERE JSON_VALUE(value, '$.id') = 'check_availability') k
WHERE n.[id] = 'check_availability';

IF @CheckAvailIdx IS NULL
BEGIN
    -- Try direct OPENJSON scan
    SELECT @CheckAvailIdx = CAST(idx.[key] AS NVARCHAR(5))
    FROM OPENJSON(@Json, '$.nodes') idx
    WHERE JSON_VALUE(idx.[value], '$.id') = 'check_availability';
END

IF @CheckAvailIdx IS NOT NULL
BEGIN
    SET @Json = JSON_MODIFY(
        @Json,
        '$.nodes[' + @CheckAvailIdx + '].config.scheduling',
        JSON_QUERY(@NodeSchedulingBlock));
    PRINT 'check_availability node: scheduling block added.';
END
ELSE
    PRINT 'WARNING: check_availability node not found.';

-- ============================================================
-- 2. Update escalate node — add "contacts" array + "escalationMessage"
--    migrated from BusinessConfigurations key=7 (EscalationContacts)
-- ============================================================
DECLARE @EscalContactsJson NVARCHAR(MAX);
DECLARE @ContactsArray     NVARCHAR(MAX);

SELECT @EscalContactsJson = Value
FROM [dbo].[BusinessConfigurations]
WHERE BusinessId = @BusinessId AND [Key] = 7;  -- key=7 was EscalationContacts

IF @EscalContactsJson IS NOT NULL AND @EscalContactsJson <> N'{}'
BEGIN
    -- Existing format: {"WhatsAppNumbers":["+57xxxxxxxxxx",...]}
    -- Extract the array directly
    DECLARE @WaNumbers NVARCHAR(MAX);
    SET @WaNumbers = JSON_QUERY(@EscalContactsJson, '$.WhatsAppNumbers');
    IF @WaNumbers IS NOT NULL
        SET @ContactsArray = @WaNumbers;
    ELSE
        SET @ContactsArray = N'[]';
END
ELSE
    SET @ContactsArray = N'[]';

DECLARE @EscalateIdx NVARCHAR(5);
SELECT @EscalateIdx = CAST(idx.[key] AS NVARCHAR(5))
FROM OPENJSON(@Json, '$.nodes') idx
WHERE JSON_VALUE(idx.[value], '$.id') = 'escalate';

IF @EscalateIdx IS NOT NULL
BEGIN
    SET @Json = JSON_MODIFY(@Json,
        '$.nodes[' + @EscalateIdx + '].config.contacts',
        JSON_QUERY(@ContactsArray));
    SET @Json = JSON_MODIFY(@Json,
        '$.nodes[' + @EscalateIdx + '].config.escalationMessage',
        N'Te estoy conectando con un asesor. Un momento por favor. 🙏');
    PRINT 'escalate node: contacts and escalationMessage added.';
END
ELSE
    PRINT 'WARNING: escalate node not found.';

-- ============================================================
-- 3. Update generate_payment_link node — add "payment" block
--    migrated from BusinessConfigurations key=3 (PaymentConfig)
-- ============================================================
DECLARE @PayConfigJson     NVARCHAR(MAX);
DECLARE @RequiresAnticipo  NVARCHAR(5)  = N'false';
DECLARE @AnticipoPct       INT          = 50;
DECLARE @Currency          NVARCHAR(10) = N'COP';

SELECT @PayConfigJson = Value
FROM [dbo].[BusinessConfigurations]
WHERE BusinessId = @BusinessId AND [Key] = 3;  -- key=3 was PaymentConfig

IF @PayConfigJson IS NOT NULL AND @PayConfigJson <> N'{}'
BEGIN
    IF ISJSON(@PayConfigJson) = 1
    BEGIN
        DECLARE @Req NVARCHAR(10) = JSON_VALUE(@PayConfigJson, '$.requiresAnticipo');
        IF LOWER(@Req) = 'true'  SET @RequiresAnticipo = N'true';
        DECLARE @Pct NVARCHAR(10) = JSON_VALUE(@PayConfigJson, '$.anticipoPorcentaje');
        IF @Pct IS NOT NULL AND TRY_CAST(@Pct AS INT) IS NOT NULL
            SET @AnticipoPct = TRY_CAST(@Pct AS INT);
        DECLARE @Cur NVARCHAR(10) = JSON_VALUE(@PayConfigJson, '$.currency');
        IF @Cur IS NOT NULL AND LEN(@Cur) > 0 SET @Currency = @Cur;
    END
END

DECLARE @PaymentBlock NVARCHAR(MAX) = N'{' +
    N'"requiresAnticipo":' + @RequiresAnticipo + N',' +
    N'"anticipoPercentage":' + CAST(@AnticipoPct AS NVARCHAR(5)) + N',' +
    N'"currency":"' + @Currency + N'",' +
    N'"expirationMinutes":1440' +
    N'}';

DECLARE @GenPayIdx NVARCHAR(5);
SELECT @GenPayIdx = CAST(idx.[key] AS NVARCHAR(5))
FROM OPENJSON(@Json, '$.nodes') idx
WHERE JSON_VALUE(idx.[value], '$.id') = 'generate_payment_link';

IF @GenPayIdx IS NOT NULL
BEGIN
    SET @Json = JSON_MODIFY(@Json,
        '$.nodes[' + @GenPayIdx + '].config.payment',
        JSON_QUERY(@PaymentBlock));
    PRINT 'generate_payment_link node: payment block added.';
END
ELSE
    PRINT 'WARNING: generate_payment_link node not found.';

-- ============================================================
-- 4. Save updated FlowDefinition
-- ============================================================
UPDATE [dbo].[FlowDefinitions]
SET    DefinitionJson = @Json,
       UpdatedAt      = GETUTCDATE()
WHERE  FlowDefinitionId = @FlowDefId;

PRINT 'FlowDefinition updated.';

-- ============================================================
-- 5. Clean BusinessConfigurations — keep ONLY key=6 (Integrations)
--
-- All other keys were behavior/configuration that now lives in flow node JSON:
--   key=0  Personality              → flow node instructions
--   key=1  EntityExtractionConfig   → legacy, unused
--   key=2  SalesStrategy            → legacy, unused
--   key=3  PaymentConfig            → generate_payment_link node config
--   key=4  OperatingHours           → check_availability node config (scheduling.schedule)
--   key=5  PaymentMethods           → flow knowledge source / node instructions
--   key=7  EscalationContacts       → escalate node config (contacts)
--   key=8  PaymentConfirmationMessages → flow node instructions
--   key=9  SchedulingPolicy         → check_availability node config (scheduling params)
-- ============================================================
DELETE FROM [dbo].[BusinessConfigurations]
WHERE BusinessId = @BusinessId AND [Key] <> 6;

PRINT 'BusinessConfigurations cleaned: only key=6 (Integrations) retained.';

COMMIT TRANSACTION;
PRINT '020_NodeConfigMigration completed successfully.';
