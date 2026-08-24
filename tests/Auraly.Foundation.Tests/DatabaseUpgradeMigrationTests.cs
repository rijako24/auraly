namespace Auraly.Foundation.Tests;

public sealed class DatabaseUpgradeMigrationTests
{
    [Fact]
    public void Fiscal_document_backfill_only_migrates_complete_sales_invoices()
    {
        var migration = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "database",
            "Auraly.Database",
            "Scripts",
            "Migrations",
            "20260801_CreateFiscalDocumentRoot.sql"));

        Assert.Contains("d.DocumentType=N'SalesInvoice'", migration, StringComparison.Ordinal);
        Assert.Contains("d.FiscalNumber IS NOT NULL", migration, StringComparison.Ordinal);
        Assert.Contains("d.FiscalStatus IS NOT NULL", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_platform_tenant_migration_preserves_key_uniqueness()
    {
        var migration = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "database",
            "Auraly.Database",
            "Scripts",
            "Migrations",
            "20260817_NormalizeAuralyPlatformTenantKey.sql"));

        Assert.Contains("TenantKey=N''@auraly''", migration, StringComparison.Ordinal);
        Assert.Contains("TenantId<>@AuralyTenantId", migration, StringComparison.Ordinal);
        Assert.Contains("UPDATE dbo.Tenants", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void Tenant_branding_migration_preserves_the_existing_business_logo_before_removing_it()
    {
        var migration = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "database",
            "Auraly.Database",
            "Scripts",
            "Migrations",
            "20260823_MoveBusinessLogoToTenant.sql"));

        var copy = migration.IndexOf(
            "SET LogoMediaRef = business.LogoUrl", StringComparison.Ordinal);
        var remove = migration.IndexOf(
            "ALTER TABLE dbo.Businesses DROP COLUMN LogoUrl", StringComparison.Ordinal);

        Assert.True(copy >= 0, "La migración no preserva el logo existente.");
        Assert.True(remove > copy, "La columna anterior se retira antes de preservar el logo.");
        Assert.Contains("COL_LENGTH(N'dbo.Businesses', N'LogoUrl')", migration,
            StringComparison.Ordinal);
        Assert.Contains("EXEC sys.sp_executesql", migration, StringComparison.Ordinal);

        var pipeline = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "infrastructure", "azure",
            "Publish-AuralyReleasePipeline.ps1"));
        var reviewedMigration = pipeline.IndexOf(
            "Invoke-ReviewedPreDacpacMigration", StringComparison.Ordinal);
        var deployReport = pipeline.IndexOf(
            "'/Action:DeployReport'", StringComparison.Ordinal);
        Assert.True(reviewedMigration >= 0 && reviewedMigration < deployReport,
            "La migración revisada debe ejecutarse antes de generar el DeployReport.");
        Assert.Contains("20260823_MoveBusinessLogoToTenant.sql", pipeline,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Fiscal_credentials_migration_consolidates_each_tenant_before_removing_business_scope()
    {
        var root = FindRepositoryRoot();
        var migration = File.ReadAllText(Path.Combine(
            root, "database", "Auraly.Database", "Scripts", "Migrations",
            "20260824_MoveFiscalCredentialsToTenant.sql"));

        var consolidate = migration.IndexOf("PARTITION BY TenantId", StringComparison.Ordinal);
        var remove = migration.IndexOf("DROP COLUMN BusinessId", StringComparison.Ordinal);
        Assert.True(consolidate >= 0, "La migración no consolida las credenciales por tenant.");
        Assert.True(remove > consolidate,
            "La migración elimina BusinessId antes de preservar una credencial por tenant.");
        Assert.Contains("FK_FiscalCredentialSecrets_Tenants", migration, StringComparison.Ordinal);

        var preDeployment = File.ReadAllText(Path.Combine(
            root, "database", "Auraly.Database", "Scripts", "PreDeployment.sql"));
        Assert.Contains("20260824_MoveFiscalCredentialsToTenant.sql", preDeployment,
            StringComparison.Ordinal);

        var pipeline = File.ReadAllText(Path.Combine(
            root, "infrastructure", "azure", "Publish-AuralyReleasePipeline.ps1"));
        var reviewedMigration = pipeline.IndexOf(
            "20260824_MoveFiscalCredentialsToTenant.sql", StringComparison.Ordinal);
        var deployReport = pipeline.IndexOf("'/Action:DeployReport'", StringComparison.Ordinal);
        Assert.True(reviewedMigration >= 0 && reviewedMigration < deployReport,
            "La migración fiscal revisada debe ejecutarse antes del DeployReport.");
    }
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Auraly repository root.");
    }
}
