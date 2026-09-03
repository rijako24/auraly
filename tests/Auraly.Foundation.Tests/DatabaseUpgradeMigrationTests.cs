namespace Auraly.Foundation.Tests;

public sealed class DatabaseUpgradeMigrationTests
{
    [Fact]
    public void Production_database_deployment_skips_demo_tenant_seeds()
    {
        var root = FindRepositoryRoot();
        var databaseRoot = Path.Combine(root, "database", "Auraly.Database");
        var demoSeeds = new[]
        {
            "SeedAdminUser.sql",
            "SeedDevBusiness.sql",
            "SeedRadaConcept.sql",
            "SeedInmobiliariaDemo.sql",
            "SeedLuisPetitBarber.sql",
            "SeedSolorzanoBusinessIdentity.sql",
            "SeedCJDistribuciones.sql",
            "SeedDigitalShop.sql",
            "SeedAndinaSantander.sql",
            "SeedAndinaProductCategories.sql",
            "SeedMedidental.sql"
        };

        foreach (var seed in demoSeeds)
        {
            var contents = File.ReadAllText(Path.Combine(
                databaseRoot, "Scripts", "Seeds", seed));
            Assert.Contains("LOWER(N'$(DeploymentEnvironment)') = N'prod'", contents,
                StringComparison.Ordinal);
            Assert.Contains("RETURN;", contents, StringComparison.Ordinal);
        }

        foreach (var migration in new[]
                 {
                     "MigrateDigitalShopWhatsAppToCJ.sql",
                     "RenameDigitalShopAgentCatalina.sql"
                 })
        {
            var contents = File.ReadAllText(Path.Combine(
                databaseRoot, "Scripts", "Migrations", migration));
            Assert.Contains("LOWER(N'$(DeploymentEnvironment)') = N'prod'", contents,
                StringComparison.Ordinal);
            Assert.Contains("RETURN;", contents, StringComparison.Ordinal);
        }

        var auralySeed = File.ReadAllText(Path.Combine(
            databaseRoot, "Scripts", "Seeds", "SeedAuraly.sql"));
        Assert.DoesNotContain("seed de demostración omitido", auralySeed,
            StringComparison.OrdinalIgnoreCase);

        var project = File.ReadAllText(Path.Combine(
            databaseRoot, "Auraly.Database.sqlproj"));
        Assert.Contains("SqlCmdVariable Include=\"DeploymentEnvironment\"", project,
            StringComparison.Ordinal);
        Assert.Contains("<DefaultValue>dev</DefaultValue>", project,
            StringComparison.Ordinal);

        var pipeline = File.ReadAllText(Path.Combine(
            root, "infrastructure", "azure", "Publish-AuralyReleasePipeline.ps1"));
        Assert.Contains("/v:DeploymentEnvironment=$Environment", pipeline,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Tax_responsibilities_and_certificate_alert_permission_are_seeded_canonically()
    {
        var root = FindRepositoryRoot();
        var options = File.ReadAllText(Path.Combine(root, "database", "Auraly.Database",
            "Scripts", "Seeds", "SeedReferenceOptions.sql"));
        var platform = File.ReadAllText(Path.Combine(root, "database", "Auraly.Database",
            "Scripts", "Seeds", "SeedAuralyPlatformAdministration.sql"));

        Assert.Contains("N'tax-responsibility'", options, StringComparison.Ordinal);
        Assert.Contains("N'R-99-PN'", options, StringComparison.Ordinal);
        Assert.Contains("N'O-13'", options, StringComparison.Ordinal);
        Assert.Contains("platform.fiscal_certificates.expiry.read", platform,
            StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT dbo.AppUsers", platform,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BootstrapAdminPasswordHash", platform,
            StringComparison.Ordinal);
        Assert.Contains("NormalizedEmail=N'ADMIN@AURALY.AI'", platform,
            StringComparison.Ordinal);

        var releasePipeline = File.ReadAllText(Path.Combine(root,
            "infrastructure", "azure", "Publish-AuralyReleasePipeline.ps1"));
        Assert.DoesNotContain("JOIN dbo.AppUsers userValue", releasePipeline,
            StringComparison.Ordinal);
        Assert.Contains("ActiveObsoleteUsers", releasePipeline,
            StringComparison.Ordinal);
        Assert.Contains("sin identidades tecnicas activas", releasePipeline,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_seed_keeps_roles_and_supplier_but_creates_no_fictitious_people()
    {
        var root = FindRepositoryRoot();
        var databaseRoot = Path.Combine(root, "database", "Auraly.Database");
        var auraly = File.ReadAllText(Path.Combine(
            databaseRoot, "Scripts", "Seeds", "SeedAuraly.sql"));
        var roles = File.ReadAllText(Path.Combine(
            databaseRoot, "Scripts", "Seeds", "SeedDefaultBusinessRoles.sql"));
        var accounting = File.ReadAllText(Path.Combine(
            databaseRoot, "StoredProcedures", "AccountingDefaultsProvision.sql"));

        Assert.DoesNotContain("INSERT INTO dbo.Employees", auraly,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO dbo.EmployeeServices", auraly,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MERGE dbo.EmployeeWorkingHours", auraly,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RequireEmployee = 0", auraly, StringComparison.Ordinal);
        Assert.Contains("N'Cajero',N'CASHIER'", roles, StringComparison.Ordinal);
        Assert.Contains("N'Supervisor',N'SUPERVISOR'", roles, StringComparison.Ordinal);
        Assert.Contains("N'OCASIONAL'", accounting, StringComparison.Ordinal);
        Assert.Contains("N'Gasto ocasional / sin proveedor'", accounting,
            StringComparison.Ordinal);
    }

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
    public void Price_channel_upgrade_normalizes_legacy_values_before_checks_are_revalidated()
    {
        var root = FindRepositoryRoot();
        var migration = File.ReadAllText(Path.Combine(
            root, "database", "Auraly.Database", "Scripts", "Migrations",
            "20260828_NormalizePriceChannelValues.sql"));
        var preDeployment = File.ReadAllText(Path.Combine(
            root, "database", "Auraly.Database", "Scripts", "PreDeployment.sql"));

        Assert.Contains("Strategy = N''FixedMarginOverAverageCost''", migration,
            StringComparison.Ordinal);
        Assert.Contains("Value IS NULL OR Value < 0", migration,
            StringComparison.Ordinal);
        Assert.Contains("WHEN Value IS NULL OR Value < 0 THEN 0", migration,
            StringComparison.Ordinal);
        Assert.Contains("Strategy = N''PercentageOverAverageCost''", migration,
            StringComparison.Ordinal);
        Assert.Contains("Strategy = N''ProductMarginAdjustment''", migration,
            StringComparison.Ordinal);
        Assert.DoesNotContain("\nGO", migration.Replace("\r\n", "\n"),
            StringComparison.Ordinal);
        Assert.DoesNotContain("RETURN", migration, StringComparison.Ordinal);
        Assert.Contains("20260828_NormalizePriceChannelValues.sql", preDeployment,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Price_channel_upgrade_converts_average_cost_markup_to_latest_cost_margin_before_dacpac()
    {
        var root = FindRepositoryRoot();
        var migrationName = "20260828_ReplaceAverageCostMarkupWithLatestCostMargin.sql";
        var migration = File.ReadAllText(Path.Combine(
            root, "database", "Auraly.Database", "Scripts", "Migrations", migrationName));
        var preDeployment = File.ReadAllText(Path.Combine(
            root, "database", "Auraly.Database", "Scripts", "PreDeployment.sql"));

        Assert.Contains("NOCHECK CONSTRAINT CK_PriceChannels_Strategy", migration, StringComparison.Ordinal);
        Assert.Contains("NOCHECK CONSTRAINT CK_PriceChannels_Value", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP CONSTRAINT CK_PriceChannels", migration, StringComparison.Ordinal);
        Assert.Contains("Strategy = N''MarginOverLatestCost''", migration, StringComparison.Ordinal);
        Assert.Contains("100 * Value / (100 + Value)", migration, StringComparison.Ordinal);
        Assert.Contains("WHERE Strategy = N''PercentageOverAverageCost''", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("\nGO", migration.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains(migrationName, preDeployment, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_pipeline_publishes_installer_version_and_manifest_hash_atomically()
    {
        var pipeline = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "infrastructure", "azure",
            "Publish-AuralyReleasePipeline.ps1"));

        Assert.Contains("auraly-pos-$ReleaseVersion.exe", pipeline,
            StringComparison.Ordinal);
        Assert.Contains("auraly-pos-prod-$ReleaseVersion.exe", pipeline,
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
    public void Release_pipeline_syncs_and_verifies_all_function_triggers()
    {
        var pipeline = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "infrastructure", "azure",
            "Publish-AuralyReleasePipeline.ps1"));

        Assert.Contains("syncfunctiontriggers?api-version=2024-04-01", pipeline,
            StringComparison.Ordinal);
        Assert.Contains("Sync-AndAssertFunctionTriggers", pipeline,
            StringComparison.Ordinal);
        Assert.Contains("functions.metadata", pipeline,
            StringComparison.Ordinal);
        Assert.Contains("az functionapp function list", pipeline,
            StringComparison.Ordinal);
        Assert.Contains("Sync-AndAssertFunctionTriggers -PackagePath $zip", pipeline,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Authentication_session_schema_enforces_one_active_login_per_user()
    {
        var root = FindRepositoryRoot();
        var table = File.ReadAllText(Path.Combine(
            root, "database", "Auraly.Database", "Tables",
            "AuthenticationSessions.sql"));
        var preDeployment = File.ReadAllText(Path.Combine(
            root, "database", "Auraly.Database", "Scripts", "PreDeployment.sql"));
        var migration = File.ReadAllText(Path.Combine(
            root, "database", "Auraly.Database", "Scripts", "Migrations",
            "20260831_EnforceExclusiveUserSessions.sql"));

        Assert.Contains("UX_AuthenticationSessions_User_Active", table,
            StringComparison.Ordinal);
        Assert.DoesNotContain("UX_AuthenticationSessions_User_Client_Active", table,
            StringComparison.Ordinal);
        Assert.DoesNotContain("DROP INDEX [UX_AuthenticationSessions_User_Client_Active]", migration,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("20260831_EnforceExclusiveUserSessions.sql", preDeployment,
            StringComparison.Ordinal);
        Assert.DoesNotContain("DROP INDEX [UX_WorkSessions_User_Open]", migration,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Work_session_schema_enforces_tenant_user_and_business_scope()
    {
        var root = FindRepositoryRoot();
        var workSessions = File.ReadAllText(Path.Combine(
            root, "database", "Auraly.Database", "Tables", "WorkSessions.sql"));
        var sales = File.ReadAllText(Path.Combine(
            root, "database", "Auraly.Database", "Tables", "SalesDocuments.sql"));
        var cash = File.ReadAllText(Path.Combine(
            root, "database", "Auraly.Database", "Tables", "CashMovementDocuments.sql"));
        var returns = File.ReadAllText(Path.Combine(
            root, "database", "Auraly.Database", "Tables", "SalesReturns.sql"));
        var users = File.ReadAllText(Path.Combine(
            root, "database", "Auraly.Database", "Tables", "AppUsers.sql"));
        var devices = File.ReadAllText(Path.Combine(
            root, "database", "Auraly.Database", "Tables", "EnrolledDevices.sql"));
        var preDeployment = File.ReadAllText(Path.Combine(
            root, "database", "Auraly.Database", "Scripts", "PreDeployment.sql"));
        var pipeline = File.ReadAllText(Path.Combine(
            root, "infrastructure", "azure", "Publish-AuralyReleasePipeline.ps1"));

        Assert.Contains("UX_WorkSessions_Tenant_User_Open", workSessions,
            StringComparison.Ordinal);
        Assert.Contains("FK_WorkSessions_BusinessTenant", workSessions,
            StringComparison.Ordinal);
        Assert.Contains("FK_WorkSessions_BusinessWarehouse", workSessions,
            StringComparison.Ordinal);
        Assert.Contains("FK_WorkSessions_UserTenant", workSessions,
            StringComparison.Ordinal);
        Assert.Contains("FK_WorkSessions_DeviceTenant", workSessions,
            StringComparison.Ordinal);
        Assert.Contains("UQ_AppUsers_User_Tenant", users,
            StringComparison.Ordinal);
        Assert.Contains("UQ_EnrolledDevices_Device_Tenant", devices,
            StringComparison.Ordinal);
        Assert.Contains("FK_SalesDocuments_WorkSessionBusiness", sales,
            StringComparison.Ordinal);
        Assert.Contains("FK_CashMovementDocuments_SessionBusiness", cash,
            StringComparison.Ordinal);
        Assert.Contains("FK_SalesReturns_WorkSessionBusiness", returns,
            StringComparison.Ordinal);
        Assert.Contains("20260902_ScopeWorkSessionsByTenant.sql", preDeployment,
            StringComparison.Ordinal);
        var reviewedMigration = pipeline.IndexOf(
            "20260902_ScopeWorkSessionsByTenant.sql", StringComparison.Ordinal);
        var deployReport = pipeline.IndexOf("'/Action:DeployReport'", StringComparison.Ordinal);
        Assert.True(reviewedMigration >= 0 && reviewedMigration < deployReport,
            "La migración de sesiones debe ejecutarse antes del DeployReport para preservar filas existentes.");
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
