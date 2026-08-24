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
    public void Cached_online_pos_still_refreshes_workspace_options()
    {
        var repositoryRoot = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(
            repositoryRoot, "admin", "src", "app", "(pos)", "pos", "page.tsx"));
        var cachedClientStart = page.IndexOf(
            "if (cachedOnlineClient)", StringComparison.Ordinal);
        var bootstrapCall = page.IndexOf(
            "const serverBootstrap = await loadSalesWorkspaceBootstrap();",
            cachedClientStart,
            StringComparison.Ordinal);

        Assert.True(cachedClientStart >= 0, "The cached online client branch was not found.");
        Assert.True(bootstrapCall > cachedClientStart, "The workspace bootstrap call was not found after the cached client branch.");
        Assert.DoesNotContain(
            "return;",
            page[cachedClientStart..bootstrapCall],
            StringComparison.Ordinal);
    }

    [Fact]
    public void Pos_scanner_remains_editable_and_server_sync_has_no_polling_timer()
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
        Assert.Contains("disabled={busy || !salesReady}", scanner, StringComparison.Ordinal);
        Assert.DoesNotContain("edgeReady", scanner, StringComparison.Ordinal);
        Assert.Contains(
            "Los servicios locales del equipo no est\\u00e1n disponibles.",
            page,
            StringComparison.Ordinal);
        Assert.Contains(
            "No hay conexi\\u00f3n con Auraly. La venta en l\\u00ednea requiere conexi\\u00f3n con el servidor.",
            page,
            StringComparison.Ordinal);
        Assert.Contains(
            "client.mode === \"edge\"",
            page,
            StringComparison.Ordinal);
        Assert.DoesNotContain("window.setInterval(() => void connect(), 3_000)", page, StringComparison.Ordinal);
        Assert.Contains("watchLocalState", page, StringComparison.Ordinal);

        var edgeHost = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Pos",
            "Auraly.Pos.Edge.Host",
            "Program.cs"));
        Assert.DoesNotContain("PeriodicTimer", edgeHost, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "PosServerSynchronizationHostedService",
            edgeHost,
            StringComparison.Ordinal);
        Assert.Contains("PosEventDrivenSynchronizationHostedService", edgeHost);
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
            "event.key === \"Tab\"",
            page,
            StringComparison.Ordinal);
        Assert.Contains("navigateFromQuantity(", page, StringComparison.Ordinal);
        Assert.Contains("quantityInputs.current.get(nextLineId)?.focus()", page, StringComparison.Ordinal);
        Assert.Contains("setSelectedLineId(null);", page, StringComparison.Ordinal);
        Assert.Contains("focusScanner();", page, StringComparison.Ordinal);
        Assert.Contains(
            "focusScanner();",
            page,
            StringComparison.Ordinal);

        Assert.Contains(
            "useReferenceOptions(\"payment-method\")",
            paymentDialog,
            StringComparison.Ordinal);
        Assert.Contains(
            "shortcut: `F${index + 1}`",
            paymentDialog,
            StringComparison.Ordinal);

        Assert.Contains(
            "onSearch={searchProducts}",
            page,
            StringComparison.Ordinal);
        Assert.Contains("event.key === \"ArrowDown\"", productSearchDialog, StringComparison.Ordinal);
        Assert.Contains("event.key === \"ArrowUp\"", productSearchDialog, StringComparison.Ordinal);
        Assert.Contains("moveSelection(1)", productSearchDialog, StringComparison.Ordinal);
        Assert.Contains("moveSelection(-1)", productSearchDialog, StringComparison.Ordinal);
        Assert.Contains(
            "Flechas recorren; Tab entra al listado; Enter agrega; Esc vuelve al lector.",
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

        Assert.Contains("event.key === \"F3\"", page, StringComparison.Ordinal);
        Assert.Contains("requestRemoveLine(selectedLineId)", page, StringComparison.Ordinal);
        Assert.Contains("event.key === \"F4\"", page, StringComparison.Ordinal);
        Assert.Contains("event.key === \"F5\"", page, StringComparison.Ordinal);
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
    public void Pos_groups_temporary_sales_and_online_orders_in_the_side_panel()
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
        var headerStart = page.IndexOf("<header", StringComparison.Ordinal);
        var headerEnd = page.IndexOf("</header>", headerStart, StringComparison.Ordinal);
        var header = page[headerStart..headerEnd];

        Assert.DoesNotContain("{temporaries.length} temporales", header, StringComparison.Ordinal);
        Assert.DoesNotContain("Pedidos", header, StringComparison.Ordinal);
        Assert.Contains("role=\"tablist\"", page, StringComparison.Ordinal);
        Assert.Contains("setSidePanel(\"temporaries\")", page, StringComparison.Ordinal);
        Assert.Contains("setSidePanel(\"orders\")", page, StringComparison.Ordinal);
        Assert.Contains("sidePanel === \"temporaries\"", page, StringComparison.Ordinal);
        Assert.Contains("setOrdersExpanded(true)", page, StringComparison.Ordinal);
        Assert.Contains("<OrdersWorkspace", page, StringComparison.Ordinal);
        Assert.Contains("ordersExpanded && client", page, StringComparison.Ordinal);
        Assert.Contains("{ordersCount}", page, StringComparison.Ordinal);
        Assert.Contains("setSelectedCustomer(recoveredCustomer)", page, StringComparison.Ordinal);
        Assert.Contains("setMessage(\"Los pedidos se consultan", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Pos_search_loading_indicators_spin_without_moving_their_vertical_anchor()
    {
        var repositoryRoot = FindRepositoryRoot();
        var dialogs = new[]
        {
            "pos-product-search-dialog.tsx",
            "pos-customer-search-dialog.tsx",
            "pos-invoice-search-dialog.tsx",
        };

        foreach (var dialog in dialogs)
        {
            var source = File.ReadAllText(Path.Combine(
                repositoryRoot,
                "admin",
                "src",
                "app",
                "(pos)",
                "pos",
                dialog));

            Assert.Contains(
                "absolute right-4 top-1/2 grid h-5 w-5 -translate-y-1/2 place-items-center",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "h-5 w-5 -translate-y-1/2 animate-spin",
                source,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Installed_pos_exposes_independent_printers_scale_and_tenant_branding()
    {
        var repositoryRoot = FindRepositoryRoot();
        var peripheralDialog = File.ReadAllText(Path.Combine(
            repositoryRoot, "admin", "src", "app", "(pos)", "pos",
            "pos-printer-dialog.tsx"));
        var onlineClient = File.ReadAllText(Path.Combine(
            repositoryRoot, "admin", "src", "services", "pos",
            "online-pos-client.ts"));

        Assert.Contains("title=\"Punto de venta\"", peripheralDialog, StringComparison.Ordinal);
        Assert.Contains("title=\"Pedidos\"", peripheralDialog, StringComparison.Ordinal);
        Assert.Contains("Impresora del sistema", peripheralDialog, StringComparison.Ordinal);
        Assert.Contains("Posición inicial", peripheralDialog, StringComparison.Ordinal);
        Assert.Contains("Dividir el valor por 1.000", peripheralDialog, StringComparison.Ordinal);
        Assert.Contains("Probar balanza", peripheralDialog, StringComparison.Ordinal);
        Assert.Contains("receiptBrandMarkup(branding)", onlineClient, StringComparison.Ordinal);
        Assert.DoesNotContain("<h1>Auraly</h1>", onlineClient, StringComparison.Ordinal);
    }

    [Fact]
    public void Platform_and_commerce_share_one_http_host()
    {
        var repositoryRoot = FindRepositoryRoot();
        var canonicalHost = File.ReadAllText(Path.Combine(
            repositoryRoot, "src", "API", "Auraly.Api", "Auraly.Api.csproj"));
        var backendRouting = File.ReadAllText(Path.Combine(
            repositoryRoot, "admin", "src", "lib", "backend-request-url.ts"));
        var desktop = File.ReadAllText(Path.Combine(
            repositoryRoot, "src", "Desktop", "Auraly.Desktop", "Program.cs"));
        var desktopApplicationContext = File.ReadAllText(Path.Combine(
            repositoryRoot, "src", "Desktop", "Auraly.Desktop",
            "AuralyDesktopApplicationContext.cs"));
        Assert.Contains(
            "/pos-launch#edgeToken=",
            desktopApplicationContext,
            StringComparison.Ordinal);
        var apiProjects = Directory.GetFiles(
            Path.Combine(repositoryRoot, "src", "API"),
            "*.csproj",
            SearchOption.AllDirectories);
        var webHosts = apiProjects.Where(path => File.ReadAllText(path)
            .Contains("Microsoft.NET.Sdk.Web", StringComparison.Ordinal)).ToArray();
        Assert.Single(webHosts);
        Assert.EndsWith(
            Path.Combine("Auraly.Api", "Auraly.Api.csproj"),
            webHosts[0],
            StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(Path.Combine(repositoryRoot, "src", "API", "Auraly.Api", "Controllers")));
        var controllerFiles = Directory.GetFiles(
            Path.Combine(repositoryRoot, "src", "API"),
            "*Controller.cs",
            SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.NotEmpty(controllerFiles);
        Assert.All(
            controllerFiles,
            controller => Assert.StartsWith(
                Path.Combine(repositoryRoot, "src", "API", "Auraly.Api", "Controllers"),
                controller,
                StringComparison.OrdinalIgnoreCase));
        var unversionedControllerRoute = new System.Text.RegularExpressions.Regex(
            @"\[(?:Route|HttpGet|HttpPost|HttpPut|HttpPatch|HttpDelete)\(""api\/(?!v\d+\/)",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        Assert.All(controllerFiles, controller =>
        {
            var source = File.ReadAllText(controller);
            Assert.Contains("api/v1/", source, StringComparison.Ordinal);
            Assert.False(
                unversionedControllerRoute.IsMatch(source),
                $"Unversioned API route found in {Path.GetRelativePath(repositoryRoot, controller)}.");
        });
        Assert.False(Directory.Exists(Path.Combine(repositoryRoot, "src", "API", "Auraly.Platform.Composition")));
        Assert.False(Directory.Exists(Path.Combine(
            repositoryRoot,
            "src",
            "API",
            string.Concat("Mi", "mos", "BabySpa.WebAPI"))));
        Assert.DoesNotContain("AURALY_COMMERCE_API_URL", backendRouting, StringComparison.Ordinal);
        Assert.DoesNotContain("IdentityApiUrl", desktop, StringComparison.Ordinal);
        Assert.DoesNotContain("CommerceApiUrl", desktop, StringComparison.Ordinal);
        Assert.Contains("string ApiUrl", desktop, StringComparison.Ordinal);
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
