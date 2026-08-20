using System.Text.RegularExpressions;

namespace Auraly.Foundation.Tests;

public sealed class ArchitectureDebtRatchetTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void InventoryLedger_HasOneWriter()
    {
        var files = CSharpFiles("src");
        var writerPattern = new Regex(
            @"\b(?:INSERT(?:\s+INTO)?|UPDATE)\s+dbo\.(?:InventoryBalances|InventoryMovements)\b",
            RegexOptions.IgnoreCase);
        var writers = files.Where(file => writerPattern.IsMatch(File.ReadAllText(file)));

        var writer = Assert.Single(writers);
        Assert.Equal(
            Path.Combine(RepositoryRoot, "src", "Infrastructure", "Auraly.Infrastructure.Persistence", "SqlInventoryLedgerWriter.cs"),
            writer);
    }

    [Fact]
    public void CanonicalEngines_AreNotDuplicated()
    {
        AssertSingleClass("DocumentProcessingEngine");
        AssertSingleClass("FiscalProcessingCoordinator");
        AssertSingleClass("AccountingProcessingCoordinator");
    }

    [Fact]
    public void ApiSqlDebt_CannotGrow()
    {
        var count = CSharpFiles(Path.Combine("src", "API", "Auraly.Api"))
            .Sum(file => Regex.Matches(File.ReadAllText(file), @"(?:new\s+)?SqlCommand\s*\(").Count);

        Assert.True(count <= 36,
            $"Direct SQL in the API grew to {count}. Extract persistence instead of increasing the DT-003 baseline of 36.");
    }

    [Fact]
    public void LegacyDboTableDebt_CannotGrow()
    {
        var count = SqlFiles(Path.Combine("database", "Auraly.Database", "Tables"))
            .Count(file => Regex.IsMatch(File.ReadAllText(file), @"CREATE\s+TABLE\s+\[?dbo\]?\.", RegexOptions.IgnoreCase));

        Assert.True(count <= 143,
            $"Legacy dbo tables grew to {count}. New module/catalog tables require an owned schema; DT-004 baseline is 143.");
    }

    [Fact]
    public void MigratedBusinessSelectors_DoNotReintroduceHardcodedLists()
    {
        var paths = new[]
        {
            "admin/src/app/(pos)/pos/pos-payment-dialog.tsx",
            "admin/src/app/(pos)/pos/pos-document-type-dialog.tsx",
            "admin/src/components/inventory/inventory-reason-master.tsx",
            "admin/src/app/(dashboard)/dashboard/agents/new/page.tsx",
            "admin/src/components/products/product-create-workspace.tsx",
            "admin/src/components/products/product-supplier-editor.tsx"
        };

        foreach (var path in paths)
            Assert.Contains("useReferenceOptions", File.ReadAllText(Path.Combine(RepositoryRoot, path)));
    }

    private static void AssertSingleClass(string className)
    {
        var matches = CSharpFiles("src")
            .Where(file => Regex.IsMatch(File.ReadAllText(file), $@"\bclass\s+{className}\b"))
            .ToArray();
        Assert.Single(matches);
    }

    private static IEnumerable<string> CSharpFiles(string relativePath) =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot, relativePath), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    private static IEnumerable<string> SqlFiles(string relativePath) =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot, relativePath), "*.sql", SearchOption.AllDirectories);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auraly.Commerce.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
