CREATE TRIGGER [dbo].[TR_Tenants_KeepTenantKeyImmutable]
ON [dbo].[Tenants]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    IF UPDATE([TenantKey]) AND EXISTS (
        SELECT 1
        FROM inserted i
        INNER JOIN deleted d ON d.[TenantId] = i.[TenantId]
        WHERE i.[TenantKey] COLLATE Latin1_General_100_BIN2 <> d.[TenantKey] COLLATE Latin1_General_100_BIN2)
        THROW 51040, 'TenantKey is immutable after tenant provisioning.', 1;
END;
