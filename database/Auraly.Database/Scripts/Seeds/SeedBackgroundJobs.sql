DECLARE @BackgroundJobsConfigurationId INT = 3;

DECLARE @BackgroundJobsValue NVARCHAR(MAX) = N'{

  "payment_link_polling": {

    "enabled": true,

    "intervalMinutes": 5

  },

  "reservation_automation": {

    "enabled": true,

    "intervalMinutes": 1

  },

  "external_escalation_expiration": {

    "enabled": true,

    "intervalMinutes": 15

  },

  "tenant_subscription_lifecycle": {

    "enabled": true,

    "intervalMinutes": 60

  }

}';



IF NOT EXISTS (

    SELECT 1

    FROM dbo.SystemConfigurations

    WHERE SystemConfigurationId = @BackgroundJobsConfigurationId

)

BEGIN

    INSERT INTO dbo.SystemConfigurations (SystemConfigurationId, [Value], [Description], IsActive, CreatedAt)

    VALUES (

        @BackgroundJobsConfigurationId,

        @BackgroundJobsValue,

        N'Intervalos de ejecucion para procesos temporizados de la plataforma.',

        1,

        SYSUTCDATETIME()

    );

END

ELSE

BEGIN

    UPDATE dbo.SystemConfigurations

    SET

        [Value] = @BackgroundJobsValue,

        [Description] = N'Intervalos de ejecucion para procesos temporizados de la plataforma.',

        IsActive = 1,

        UpdatedAt = SYSUTCDATETIME()

    WHERE SystemConfigurationId = @BackgroundJobsConfigurationId;

END

