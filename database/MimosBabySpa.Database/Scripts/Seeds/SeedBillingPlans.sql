MERGE dbo.SubscriptionPlans AS target
USING (VALUES
    ('essential', N'Esencial', CAST(389999 AS DECIMAL(18,2)), 15000, CAST(100000 AS DECIMAL(18,2)), CAST(25.64 AS DECIMAL(5,2)), 1, 1, 1,
     N'["1 agente de IA","1 linea de WhatsApp","15.000 creditos mensuales","Ideal para 30-40 conversaciones diarias","Reservas, pagos y catalogo","Dashboard de consumo"]'),
    ('growth', N'Crecimiento', CAST(899999 AS DECIMAL(18,2)), 45000, CAST(225000 AS DECIMAL(18,2)), CAST(25.00 AS DECIMAL(5,2)), 3, 3, 3,
     N'["3 agentes de IA","45.000 creditos mensuales","100-130 conversaciones diarias","Analytics avanzado","Soporte prioritario","Integraciones operativas"]'),
    ('pro', N'Pro', CAST(1799999 AS DECIMAL(18,2)), 120000, CAST(450000 AS DECIMAL(18,2)), CAST(25.00 AS DECIMAL(5,2)), 8, 8, 8,
     N'["8 agentes de IA","120.000 creditos mensuales","250-350 conversaciones diarias","Multi-sede","Reportes avanzados","Acompanamiento prioritario"]')
) AS source (Code, Name, MonthlyPriceCop, IncludedCredits, MaxVariableCostCop, MaxVariableCostPercent, IncludedAgents, IncludedUsers, IncludedWorkspaces, FeaturesJson)
ON target.Code = source.Code
WHEN MATCHED THEN
    UPDATE SET
        Name = source.Name,
        MonthlyPriceCop = source.MonthlyPriceCop,
        IncludedCredits = source.IncludedCredits,
        MaxVariableCostCop = source.MaxVariableCostCop,
        MaxVariableCostPercent = source.MaxVariableCostPercent,
        IncludedAgents = source.IncludedAgents,
        IncludedUsers = source.IncludedUsers,
        IncludedWorkspaces = source.IncludedWorkspaces,
        FeaturesJson = source.FeaturesJson,
        IsActive = 1,
        UpdatedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (Code, Name, MonthlyPriceCop, IncludedCredits, MaxVariableCostCop, MaxVariableCostPercent, IncludedAgents, IncludedUsers, IncludedWorkspaces, FeaturesJson, IsActive)
    VALUES (source.Code, source.Name, source.MonthlyPriceCop, source.IncludedCredits, source.MaxVariableCostCop, source.MaxVariableCostPercent, source.IncludedAgents, source.IncludedUsers, source.IncludedWorkspaces, source.FeaturesJson, 1);

MERGE dbo.UsageCostRates AS target
USING (VALUES
    ('gpt-4o-mini-input', 2, 'token', CAST(0.00000015 AS DECIMAL(18,8)), CAST(0.00054 AS DECIMAL(18,4))),
    ('gpt-4o-mini-output', 2, 'token', CAST(0.00000060 AS DECIMAL(18,8)), CAST(0.00216 AS DECIMAL(18,4))),
    ('tool-call', 3, 'operation', CAST(0 AS DECIMAL(18,8)), CAST(0.2500 AS DECIMAL(18,4))),
    ('whatsapp-session', 5, 'message', CAST(0 AS DECIMAL(18,8)), CAST(0.0500 AS DECIMAL(18,4))),
    ('whatsapp-utility-co', 6, 'message', CAST(0.00080000 AS DECIMAL(18,8)), CAST(2.8700 AS DECIMAL(18,4))),
    ('whatsapp-marketing-co', 7, 'message', CAST(0.01250000 AS DECIMAL(18,8)), CAST(44.8500 AS DECIMAL(18,4)))
) AS source (Code, OperationType, Unit, CostUsd, CostCop)
ON target.Code = source.Code AND target.OperationType = source.OperationType AND target.EffectiveTo IS NULL
WHEN MATCHED THEN
    UPDATE SET Unit = source.Unit, CostUsd = source.CostUsd, CostCop = source.CostCop, IsActive = 1
WHEN NOT MATCHED THEN
    INSERT (Code, OperationType, Unit, CostUsd, CostCop, EffectiveFrom, IsActive)
    VALUES (source.Code, source.OperationType, source.Unit, source.CostUsd, source.CostCop, '2026-01-01', 1);

DECLARE @EssentialPlanId UNIQUEIDENTIFIER = (SELECT SubscriptionPlanId FROM dbo.SubscriptionPlans WHERE Code = 'essential');

INSERT INTO dbo.BusinessSubscriptions (
    BusinessId,
    SubscriptionPlanId,
    [Status],
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
    b.BusinessId,
    p.SubscriptionPlanId,
    1,
    DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1),
    DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1)),
    p.Code,
    p.Name,
    p.MonthlyPriceCop,
    p.IncludedCredits,
    p.MaxVariableCostCop,
    p.MaxVariableCostPercent,
    0,
    0,
    1
FROM dbo.Businesses b
CROSS JOIN dbo.SubscriptionPlans p
WHERE p.SubscriptionPlanId = @EssentialPlanId
  AND NOT EXISTS (
      SELECT 1
      FROM dbo.BusinessSubscriptions bs
      WHERE bs.BusinessId = b.BusinessId
        AND bs.Status IN (1, 2, 3)
  );
