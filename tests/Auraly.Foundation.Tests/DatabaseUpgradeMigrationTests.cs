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
        Assert.Contains("EXEC sys.sp_executesql", migration, StringComparison.Ordinal);
        Assert.True(migration.Split("EXEC sys.sp_executesql", StringSplitOptions.None).Length >= 5,
            "Cada cambio de metadatos dependiente debe compilarse después del anterior.");

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

    [Fact]
    public void Purchase_evidence_migration_compiles_column_dependent_work_after_the_alter()
    {
        var root = FindRepositoryRoot();
        var migration = File.ReadAllText(Path.Combine(
            root, "database", "Auraly.Database", "Scripts", "Migrations",
            "20260825_AddPurchaseEvidence.sql"));

        var addColumn = migration.IndexOf(
            "ALTER TABLE dbo.GoodsReceipts ADD PurchaseEvidenceType", StringComparison.Ordinal);
        var deferredBackfill = migration.IndexOf(
            "EXEC sys.sp_executesql", addColumn, StringComparison.Ordinal);
        Assert.True(addColumn >= 0 && deferredBackfill > addColumn,
            "El backfill debe compilarse después de crear PurchaseEvidenceType.");
        Assert.DoesNotContain("\nGO", migration.Replace("\r\n", "\n"),
            StringComparison.Ordinal);

        var pipeline = File.ReadAllText(Path.Combine(
            root, "infrastructure", "azure", "Publish-AuralyReleasePipeline.ps1"));
        var reviewedMigration = pipeline.IndexOf(
            "20260825_AddPurchaseEvidence.sql", StringComparison.Ordinal);
        var deployReport = pipeline.IndexOf("'/Action:DeployReport'", StringComparison.Ordinal);
        Assert.True(reviewedMigration >= 0 && reviewedMigration < deployReport,
            "La migración de soportes debe ejecutarse antes del DeployReport.");
    }

    [Fact]
    public void Release_pipeline_publishes_installer_version_and_manifest_hash_atomically()
    {
        var pipeline = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "infrastructure", "azure",
            "Publish-AuralyReleasePipeline.ps1"));

        Assert.Contains("auraly-pos-$ReleaseVersion.exe", pipeline,
            StringComparison.Ordinal);
        Assert.Contains("PosInstaller__Version=$ReleaseVersion", pipeline,
            StringComparison.Ordinal);
        Assert.Contains("PosInstaller__Sha256=$($installerArtifact.sha256)", pipeline,
            StringComparison.Ordinal);
        Assert.Contains("$installerVersion -ne $ReleaseVersion", pipeline,
            StringComparison.Ordinal);
        Assert.Contains("$installerSha256 -ne \"$($installerMetadata.sha256)\"", pipeline,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WhatsApp_configuration_uses_the_app_configuration_data_plane()
    {
        var root = FindRepositoryRoot();
        var pipeline = File.ReadAllText(Path.Combine(
            root, "infrastructure", "azure", "Publish-AuralyReleasePipeline.ps1"));
        var syncWorkflow = File.ReadAllText(Path.Combine(
            root, ".github", "workflows", "sync-cj-whatsapp-dev.yml"));

        Assert.Contains("az appconfig kv set", pipeline, StringComparison.Ordinal);
        Assert.Contains("--auth-mode login", pipeline, StringComparison.Ordinal);
        Assert.Contains("az appconfig kv set", syncWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("whatsapp-config.bicep", pipeline, StringComparison.Ordinal);
        Assert.DoesNotContain("whatsapp-config.bicep", syncWorkflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Authentication_session_schema_leaves_index_replacement_to_the_dacpac_plan()
    {
        var root = FindRepositoryRoot();
        var table = File.ReadAllText(Path.Combine(
            root, "database", "Auraly.Database", "Tables",
            "AuthenticationSessions.sql"));
        var preDeployment = File.ReadAllText(Path.Combine(
            root, "database", "Auraly.Database", "Scripts", "PreDeployment.sql"));

        Assert.DoesNotContain("UX_AuthenticationSessions_User_Active", table,
            StringComparison.Ordinal);
        Assert.Contains("UX_AuthenticationSessions_User_Client_Active", table,
            StringComparison.Ordinal);
        Assert.DoesNotContain("AllowAuthenticationSessionsPerClient", preDeployment,
            StringComparison.Ordinal);
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
