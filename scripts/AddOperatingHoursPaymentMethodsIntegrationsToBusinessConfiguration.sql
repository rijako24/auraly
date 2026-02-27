-- ============================================================================
-- Script: Agregar BusinessConfiguration para Keys 4, 5, 6
-- (OperatingHours, PaymentMethods, Integrations)
-- Ejecutar con: .\AddOperatingHoursPaymentMethodsIntegrationsToBusinessConfiguration.ps1
-- ============================================================================

DECLARE @OperatingHours NVARCHAR(MAX) = N'{
  "monday":    [{"open":"08:00","close":"12:00"},{"open":"14:00","close":"18:00"}],
  "tuesday":   [{"open":"08:00","close":"12:00"},{"open":"14:00","close":"18:00"}],
  "wednesday": [{"open":"08:00","close":"12:00"},{"open":"14:00","close":"18:00"}],
  "thursday":  [{"open":"08:00","close":"12:00"},{"open":"14:00","close":"18:00"}],
  "friday":    [{"open":"08:00","close":"12:00"},{"open":"14:00","close":"18:00"}],
  "saturday":  [{"open":"08:00","close":"13:00"}],
  "sunday":    []
}';

DECLARE @PaymentMethods NVARCHAR(MAX) = N'[
  {"name":"Efectivo","icon":"💵"},
  {"name":"Nequi","icon":"📱"},
  {"name":"Daviplata","icon":"📱"},
  {"name":"Transferencia bancaria","icon":"🏦"}
]';

DECLARE @Integrations NVARCHAR(MAX) = N'{"GoogleCalendar":{"Enabled":false,"CalendarId":"primary"}}';

-- Key=4 (OperatingHours)
MERGE [dbo].[BusinessConfigurations] AS target
USING (SELECT [BusinessId] FROM [dbo].[Businesses]) AS source
ON target.[BusinessId] = source.[BusinessId] AND target.[Key] = 4
WHEN NOT MATCHED BY TARGET THEN
    INSERT (BusinessConfigurationId, BusinessId, [Key], [Value], [Description], IsActive, CreatedAt)
    VALUES (NEWID(), source.[BusinessId], 4, @OperatingHours,
        N'Horarios de operación por día de la semana', 1, GETUTCDATE())
WHEN MATCHED THEN
    UPDATE SET [UpdatedAt] = GETUTCDATE();

-- Key=5 (PaymentMethods)
MERGE [dbo].[BusinessConfigurations] AS target
USING (SELECT [BusinessId] FROM [dbo].[Businesses]) AS source
ON target.[BusinessId] = source.[BusinessId] AND target.[Key] = 5
WHEN NOT MATCHED BY TARGET THEN
    INSERT (BusinessConfigurationId, BusinessId, [Key], [Value], [Description], IsActive, CreatedAt)
    VALUES (NEWID(), source.[BusinessId], 5, @PaymentMethods,
        N'Métodos de pago aceptados', 1, GETUTCDATE())
WHEN MATCHED THEN
    UPDATE SET [UpdatedAt] = GETUTCDATE();

-- Key=6 (Integrations)
MERGE [dbo].[BusinessConfigurations] AS target
USING (SELECT [BusinessId] FROM [dbo].[Businesses]) AS source
ON target.[BusinessId] = source.[BusinessId] AND target.[Key] = 6
WHEN NOT MATCHED BY TARGET THEN
    INSERT (BusinessConfigurationId, BusinessId, [Key], [Value], [Description], IsActive, CreatedAt)
    VALUES (NEWID(), source.[BusinessId], 6, @Integrations,
        N'Integraciones externas (Google Calendar, etc.)', 1, GETUTCDATE())
WHEN MATCHED THEN
    UPDATE SET [UpdatedAt] = GETUTCDATE();

PRINT '✅ BusinessConfigurations agregadas para Keys 4 (OperatingHours), 5 (PaymentMethods), 6 (Integrations).';
