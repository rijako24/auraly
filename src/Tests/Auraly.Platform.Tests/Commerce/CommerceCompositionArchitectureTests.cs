using FluentAssertions;
using Xunit;

namespace Auraly.Platform.Tests.Commerce;

public sealed class CommerceCompositionArchitectureTests
{
    [Theory]
    [InlineData("src/API/Auraly.Platform.Worker/Program.cs")]
    [InlineData("src/Console/Auraly.Console/Program.cs")]
    public void Agent_hosts_register_the_canonical_commerce_customer_lookup(string relativePath)
    {
        var program = File.ReadAllText(Path.Combine(
            FindSolutionRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

        program.Should().Contain(
            "AddScoped<ICanonicalCommerceCustomerLookup, CanonicalCommerceCustomerLookup>()",
            "every host that resolves CommerceCustomerResolver must provide its canonical lookup dependency");
    }

    [Fact]
    public void Console_registers_the_id_generator_required_by_its_unit_of_work()
    {
        var program = File.ReadAllText(Path.Combine(
            FindSolutionRoot(),
            "src/Console/Auraly.Console/Program.cs".Replace('/', Path.DirectorySeparatorChar)));

        program.Should().Contain(
            "AddSingleton<IAuralyIdGenerator, Uuid7AuralyIdGenerator>()",
            "the console must compose the same UnitOfWork dependency as the live worker");
    }

    [Fact]
    public void Cj_seed_provisions_one_active_subscription_and_the_final_channel_route()
    {
        var root = FindSolutionRoot();
        var seed = Read(root, "database/Auraly.Database/Scripts/Seeds/SeedCJDistribuciones.sql");
        var postDeployment = Read(root, "database/Auraly.Database/Scripts/PostDeployment.sql");
        var finalRoute = Read(root,
            "database/Auraly.Database/Scripts/Migrations/MigrateDigitalShopWhatsAppToCJ.sql");

        seed.Should().Contain("MERGE dbo.BusinessSubscriptions")
            .And.Contain("INSERT INTO dbo.BusinessUsagePeriods")
            .And.Contain("debe existir exactamente una suscripcion activa");
        postDeployment.IndexOf("MigrateDigitalShopWhatsAppToCJ.sql", StringComparison.Ordinal)
            .Should().BeGreaterThan(
                postDeployment.IndexOf("MigrateMedidentalWhatsAppToDigitalShop.sql", StringComparison.Ordinal));
        finalRoute.Should().Contain("SET BusinessId = @CJBusinessId")
            .And.Contain("AgentId = @CJAgentId")
            .And.Contain("N'573117323198'")
            .And.Contain("N'1234810033044432'")
            .And.Contain("N'4841200399440958'")
            .And.Contain("$(CJWhatsAppAccessToken)")
            .And.Contain("canal inactivo hasta cargar un access token valido")
            .And.Contain("IntegrationChannelWarehouses");
    }

    [Fact]
    public void Dev_release_injects_cj_whatsapp_secrets_without_committing_values()
    {
        var root = FindSolutionRoot();
        var workflow = Read(root, ".github/workflows/deploy-auraly-release.yml");
        var publisher = Read(root, "infrastructure/azure/Publish-AuralyReleasePipeline.ps1");

        workflow.Should().Contain("secrets.CJ_WHATSAPP_ACCESS_TOKEN")
            .And.Contain("secrets.CJ_WHATSAPP_FUNCTION_KEY")
            .And.Contain("secrets.CJ_WHATSAPP_VERIFY_TOKEN");
        publisher.Should().Contain("/v:CJWhatsAppAccessToken=$($env:CJ_WHATSAPP_ACCESS_TOKEN)")
            .And.Contain("function Set-FunctionKeyWithRetry")
            .And.Contain("-KeyName 'meta-cj'")
            .And.Contain("WhatsApp__Webhook__VerifyToken")
            .And.Contain("az appconfig kv set")
            .And.Contain("--auth-mode login")
            .And.NotContain("whatsapp-config.bicep")
            .And.Contain("AppConfiguration = 'cfg-auraly-dev-w5usmo6w'");
    }

    private static string Read(string root, string relativePath) =>
        File.ReadAllText(Path.Combine(root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Auraly.Commerce.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Auraly.Commerce.sln.");
    }
}
