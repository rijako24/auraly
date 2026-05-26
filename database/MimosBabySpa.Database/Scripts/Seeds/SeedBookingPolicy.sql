-- ============================================================
-- Script: SeedBookingPolicy
-- Política de reserva y anticipo (Key=3) para negocios activos.
-- Mimo's: sin anticipo (confirmación verbal).
-- Idempotente (MERGE por BusinessId + Key).
-- ============================================================

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

DECLARE @BookingPolicyValue NVARCHAR(MAX) = N'{
  "depositRequired": false,
  "depositPercentage": 0,
  "currency": "COP"
}';

MERGE dbo.BusinessConfigurations AS target
USING (
    SELECT b.BusinessId
    FROM dbo.Businesses b
    WHERE b.IsActive = 1
) AS src
   ON target.BusinessId = src.BusinessId AND target.[Key] = 3
WHEN MATCHED THEN
    UPDATE SET
        [Value] = @BookingPolicyValue,
        [Description] = N'Política de reserva: sin anticipo, confirmación verbal',
        UpdatedAt = GETUTCDATE(),
        IsActive = 1
WHEN NOT MATCHED THEN
    INSERT (BusinessConfigurationId, BusinessId, [Key], [Value], [Description], IsActive, CreatedAt)
    VALUES (
        NEWID(),
        src.BusinessId,
        3,
        @BookingPolicyValue,
        N'Política de reserva: sin anticipo, confirmación verbal',
        1,
        GETUTCDATE()
    );

PRINT N'SeedBookingPolicy: Key=3 (BookingPolicy) aplicada a negocios activos.';
GO
