SET NOCOUNT ON;

UPDATE dbo.Agents
SET [Name] = N'Catalina',
    [Description] = N'Vendedora digital de celulares nuevos y usados de Digital Shop.',
    UpdatedAt = SYSUTCDATETIME()
WHERE AgentId = 'D1617A10-0000-0000-0000-000000000020'
  AND BusinessId = 'D1617A10-0000-0000-0000-000000000010';

IF @@ROWCOUNT <> 1
    THROW 51000, 'RenameDigitalShopAgentCatalina: agente Digital Shop no encontrado.', 1;

PRINT N'RenameDigitalShopAgentCatalina: agente renombrado a Catalina.';
