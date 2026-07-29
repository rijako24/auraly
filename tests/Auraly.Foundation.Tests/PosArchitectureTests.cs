namespace Auraly.Foundation.Tests;

public sealed class PosArchitectureTests
{
    [Fact]
    public void Pos_edge_is_canonical_and_part_of_the_solution()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "src",
            "Pos",
            "Auraly.Pos.Edge.Infrastructure",
            "Auraly.Pos.Edge.Infrastructure.csproj");
        var solution = File.ReadAllText(Path.Combine(repositoryRoot, "Auraly.Commerce.sln"));
        var forbidden = new[]
        {
            string.Concat("Talk", "io"),
            string.Concat("Mi", "mos"),
            string.Concat("Xi", "on")
        };
        var sourceFiles = Directory.GetFiles(
            Path.GetDirectoryName(projectPath)!,
            "*",
            SearchOption.AllDirectories)
            .Where(file =>
                file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));

        Assert.True(File.Exists(projectPath));
        Assert.Contains(Path.GetFileName(projectPath), solution, StringComparison.Ordinal);
        foreach (var file in sourceFiles)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain(
                forbidden,
                token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Pos_scanner_remains_editable_while_the_local_edge_reconnects()
    {
        var repositoryRoot = FindRepositoryRoot();
        var pagePath = Path.Combine(
            repositoryRoot,
            "admin",
            "src",
            "app",
            "(pos)",
            "pos",
            "page.tsx");
        var page = File.ReadAllText(pagePath);
        var scannerStart = page.IndexOf("id=\"pos-scanner\"", StringComparison.Ordinal);
        var scannerEnd = page.IndexOf("/>", scannerStart, StringComparison.Ordinal);

        Assert.True(scannerStart >= 0, "The POS scanner input was not found.");
        Assert.True(scannerEnd > scannerStart, "The POS scanner input is malformed.");

        var scanner = page[scannerStart..scannerEnd];
        Assert.Contains("disabled={busy}", scanner, StringComparison.Ordinal);
        Assert.DoesNotContain("edgeReady", scanner, StringComparison.Ordinal);
        Assert.Contains(
            "POS Edge no est\\u00e1 conectado. El c\\u00f3digo se conservar\\u00e1 para reintentar.",
            page,
            StringComparison.Ordinal);
        Assert.Contains(
            "window.setInterval(() => void connect(), 3_000)",
            page,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Pos_uses_Auraly_keyboard_shortcuts_and_returns_quantity_focus_to_the_scanner()
    {
        var repositoryRoot = FindRepositoryRoot();
        var posDirectory = Path.Combine(
            repositoryRoot,
            "admin",
            "src",
            "app",
            "(pos)",
            "pos");
        var page = File.ReadAllText(Path.Combine(posDirectory, "page.tsx"));
        var paymentDialog = File.ReadAllText(
            Path.Combine(posDirectory, "pos-payment-dialog.tsx"));
        var productSearchDialog = File.ReadAllText(
            Path.Combine(posDirectory, "pos-product-search-dialog.tsx"));

        Assert.Contains("event.key === \"F1\"", page, StringComparison.Ordinal);
        Assert.Contains("event.key === \"F2\"", page, StringComparison.Ordinal);
        Assert.Contains("Buscar <span", page, StringComparison.Ordinal);
        Assert.Contains(">F2</span>", page, StringComparison.Ordinal);
        Assert.Contains(">F1</span>", page, StringComparison.Ordinal);
        Assert.Contains(
            "event.currentTarget.blur();",
            page,
            StringComparison.Ordinal);
        Assert.Contains(
            "focusScanner();",
            page,
            StringComparison.Ordinal);

        foreach (var shortcut in new[] { "F1", "F2", "F3", "F4", "F5" })
        {
            Assert.Contains(
                $"shortcut: \"{shortcut}\"",
                paymentDialog,
                StringComparison.Ordinal);
        }

        Assert.Contains(
            "onSearch={searchProducts}",
            page,
            StringComparison.Ordinal);
        Assert.Contains(
            "Tab y Shift+Tab navegan; Enter agrega el producto enfocado; Esc vuelve al lector.",
            productSearchDialog,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Destructive_shortcuts_require_an_accessible_confirmation_focused_on_accept()
    {
        var repositoryRoot = FindRepositoryRoot();
        var posDirectory = Path.Combine(
            repositoryRoot,
            "admin",
            "src",
            "app",
            "(pos)",
            "pos");
        var page = File.ReadAllText(Path.Combine(posDirectory, "page.tsx"));
        var dialog = File.ReadAllText(Path.Combine(posDirectory, "pos-confirm-dialog.tsx"));

        Assert.Contains("event.key === \"F5\"", page, StringComparison.Ordinal);
        Assert.Contains("requestRemoveLine(selectedLineId)", page, StringComparison.Ordinal);
        Assert.Contains("event.key === \"F6\"", page, StringComparison.Ordinal);
        Assert.Contains("requestCancelSale();", page, StringComparison.Ordinal);
        Assert.Contains("<PosConfirmDialog", page, StringComparison.Ordinal);
        Assert.Contains("role=\"alertdialog\"", dialog, StringComparison.Ordinal);
        Assert.Contains("aria-modal=\"true\"", dialog, StringComparison.Ordinal);
        Assert.Contains("autoFocus", dialog, StringComparison.Ordinal);
        Assert.Contains("type=\"submit\"", dialog, StringComparison.Ordinal);
        Assert.Contains("event.key === \"Escape\"", dialog, StringComparison.Ordinal);
        Assert.Contains("Enter acepta · Esc cancela", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void Pos_orders_chip_opens_the_real_online_orders_view()
    {
        var repositoryRoot = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "admin",
            "src",
            "app",
            "(pos)",
            "pos",
            "page.tsx"));

        Assert.Contains("Pedidos", page, StringComparison.Ordinal);
        Assert.Contains("window.location.assign(\"/dashboard/orders\")", page, StringComparison.Ordinal);
        Assert.Contains("if (!serverConnected)", page, StringComparison.Ordinal);
        Assert.Contains(
            "Los pedidos se consultan en línea. Auraly Server no está disponible.",
            page,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Auraly.Commerce.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Auraly.Commerce.sln.");
    }
}
