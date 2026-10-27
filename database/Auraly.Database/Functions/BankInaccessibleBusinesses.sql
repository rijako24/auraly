CREATE FUNCTION accounting.BankInaccessibleBusinesses(
    @TenantId uniqueidentifier, @BankAccountId uniqueidentifier,
    @UserId uniqueidentifier, @Permission nvarchar(160))
RETURNS TABLE
AS RETURN
(
    SELECT DISTINCT entry.BusinessId
    FROM accounting.BankBookAccounts(@TenantId,@BankAccountId) account
    JOIN dbo.AccountingEntryLines line ON line.AccountId=account.AccountId
    JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId AND entry.TenantId=@TenantId
    WHERE NOT EXISTS(
        SELECT 1 FROM dbo.UserRoles assignment
        JOIN dbo.AppRoles role ON role.RoleId=assignment.RoleId AND role.IsActive=1
        JOIN dbo.RolePermissions granted ON granted.RoleId=role.RoleId
        JOIN dbo.Permissions permission ON permission.PermissionId=granted.PermissionId
        WHERE assignment.UserId=@UserId
          AND (assignment.BusinessId IS NULL OR assignment.BusinessId=entry.BusinessId)
          AND (role.TenantId IS NULL OR role.TenantId=@TenantId)
          AND permission.Resource=@Permission)
);
GO
