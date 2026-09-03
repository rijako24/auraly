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
    public void Online_pos_rebuilds_its_client_from_a_fresh_workspace_bootstrap()
    {
        var repositoryRoot = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(
            repositoryRoot, "admin", "src", "app", "(pos)", "pos", "page.tsx"));
        var bootstrapStart = page.IndexOf(
            "const bootstrap = async () =>", StringComparison.Ordinal);
        var bootstrapCall = page.IndexOf(
            "const serverBootstrap = await loadSalesWorkspaceBootstrap();",
            bootstrapStart,
            StringComparison.Ordinal);
        var onlineClientCreation = page.IndexOf(
            "const onlineClient = new OnlinePosClient(",
            bootstrapCall,
            StringComparison.Ordinal);

        Assert.True(bootstrapStart >= 0, "The POS bootstrap was not found.");
        Assert.True(bootstrapCall > bootstrapStart, "The fresh workspace bootstrap call was not found.");
        Assert.True(onlineClientCreation > bootstrapCall, "The online client was not rebuilt from the fresh bootstrap.");
        Assert.DoesNotContain("cachedOnlineClient", page, StringComparison.Ordinal);
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
    public void Rejected_pos_scan_finishes_with_persistent_visual_and_audible_feedback()
    {
        var repositoryRoot = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(
            repositoryRoot, "admin", "src", "app", "(pos)", "pos", "page.tsx"));

        Assert.Contains("playRejectedScanTone();", page, StringComparison.Ordinal);
        Assert.Contains("phase: \"latched\"", page, StringComparison.Ordinal);
        Assert.Contains("Lectura rechazada", page, StringComparison.Ordinal);
        Assert.Contains("Este producto no pasó", page, StringComparison.Ordinal);
        Assert.Contains("clearScanRejection();", page, StringComparison.Ordinal);
        Assert.DoesNotContain("window.setTimeout(() => setScanRejected(false)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Pos_shortcuts_cycle_between_search_results_scanner_and_sale_quantities()
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
        var functionShortcut = File.ReadAllText(
            Path.Combine(posDirectory, "pos-function-shortcut.ts"));

        Assert.Contains("shortcut === POS_ACTION_SHORTCUTS.productSearch", page, StringComparison.Ordinal);
        Assert.Contains("shortcut === POS_ACTION_SHORTCUTS.editLines", page, StringComparison.Ordinal);
        Assert.Contains("capturePosFunctionShortcut(event", page, StringComparison.Ordinal);
        Assert.Contains("LaunchApplication1: \"F2\"", functionShortcut, StringComparison.Ordinal);
        Assert.Contains("LaunchApp1: \"F2\"", functionShortcut, StringComparison.Ordinal);
        var shortcutResolution = functionShortcut.IndexOf(
            "const shortcut = resolvePosFunctionShortcut(event.key, event.code, event.keyCode);",
            StringComparison.Ordinal);
        var shortcutReservation = functionShortcut.IndexOf(
            "event.preventDefault();",
            shortcutResolution,
            StringComparison.Ordinal);
        var shortcutPropagation = functionShortcut.IndexOf(
            "event.stopImmediatePropagation();",
            shortcutResolution,
            StringComparison.Ordinal);
        var shortcutDispatch = functionShortcut.IndexOf(
            "onShortcut(shortcut);",
            shortcutResolution,
            StringComparison.Ordinal);
        Assert.True(shortcutResolution >= 0, "The POS shortcut resolver was not found.");
        Assert.True(
            shortcutReservation > shortcutResolution &&
            shortcutPropagation > shortcutReservation &&
            shortcutPropagation < shortcutDispatch,
            "Function keys must be reserved before dispatching the POS action.");
        Assert.Contains(
            "if (paymentOpen) return;",
            page.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);
        Assert.Contains("Buscar <span", page, StringComparison.Ordinal);
        Assert.Contains("POS_ACTION_SHORTCUTS.editLines", page, StringComparison.Ordinal);
        Assert.Contains("POS_ACTION_SHORTCUTS.productSearch", page, StringComparison.Ordinal);
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
            "usePosReferenceOptions(client, \"payment-method\")",
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
        Assert.Contains("target < 0 || target >= results.length", productSearchDialog, StringComparison.Ordinal);
        Assert.Contains("input.current?.focus()", productSearchDialog, StringComparison.Ordinal);
        Assert.Contains(
            "Flechas recorren; Tab entra al listado; Enter agrega; Esc vuelve al lector.",
            productSearchDialog,
            StringComparison.Ordinal);
        Assert.Contains("data-pos-focus-surface=\"modal\"", productSearchDialog, StringComparison.Ordinal);
        Assert.Contains("input.current?.focus({ preventScroll: true })", productSearchDialog, StringComparison.Ordinal);
        Assert.Contains("(event.key === \"ArrowDown\" || event.key === \"ArrowUp\")", page, StringComparison.Ordinal);
        Assert.Contains("focusLastQuantity()", page, StringComparison.Ordinal);
        Assert.Contains("revealLine(quantityToFocus)", page, StringComparison.Ordinal);
        Assert.Contains("focusScanner()", page, StringComparison.Ordinal);
        Assert.Contains("canEnrollOffline={canEnrollOffline}", page, StringComparison.Ordinal);
        Assert.Contains("onEnroll={prepareInstalledPos}", page, StringComparison.Ordinal);
        Assert.DoesNotContain("setStartupMode", page, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "No tienes permiso para preparar este equipo para trabajar sin conexión.",
            page,
            StringComparison.Ordinal);

        var desktopHost = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "Desktop",
            "Auraly.Desktop",
            "AuralyDesktopApplicationContext.cs"));
        Assert.Contains(
            "AreBrowserAcceleratorKeysEnabled = false",
            desktopHost,
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

        Assert.Contains("shortcut === POS_ACTION_SHORTCUTS.removeLine", page, StringComparison.Ordinal);
        Assert.Contains("protectedActionHandlers.current.removeLine(selectedLineId)", page, StringComparison.Ordinal);
        Assert.Contains("shortcut === POS_ACTION_SHORTCUTS.editLines", page, StringComparison.Ordinal);
        Assert.Contains("shortcut === POS_ACTION_SHORTCUTS.restartSale", page, StringComparison.Ordinal);
        Assert.Contains("protectedActionHandlers.current.restartSale()", page, StringComparison.Ordinal);
        Assert.Contains("restartSale: requestCancelSale", page, StringComparison.Ordinal);
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
    public void Supervisor_approval_is_realtime_on_register_bell_and_mobile_sheet()
    {
        var repositoryRoot = FindRepositoryRoot();
        var posDirectory = Path.Combine(repositoryRoot, "admin", "src", "app", "(pos)", "pos");
        var dialog = File.ReadAllText(Path.Combine(posDirectory, "pos-supervisor-approval-dialog.tsx"));
        var notifications = File.ReadAllText(Path.Combine(
            repositoryRoot, "admin", "src", "components", "layout", "notifications-dropdown.tsx"));
        var client = File.ReadAllText(Path.Combine(
            repositoryRoot, "admin", "src", "services", "pos", "pos-approval-client.ts"));
        var messageParser = File.ReadAllText(Path.Combine(
            repositoryRoot, "admin", "src", "services", "pos", "pos-approval-synchronization.ts"));
        var synchronization = File.ReadAllText(Path.Combine(
            repositoryRoot, "src", "Pos", "Auraly.Pos.Edge.Host", "PosSynchronization.cs"));
        var serviceWorker = File.ReadAllText(Path.Combine(
            repositoryRoot, "admin", "public", "app-sw.js"));

        Assert.Contains("Credencial del supervisor", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("animate-spin", dialog, StringComparison.Ordinal);
        Assert.Contains("subscribeApprovals", dialog, StringComparison.Ordinal);
        Assert.Contains("md:hidden", notifications, StringComparison.Ordinal);
        Assert.Contains(" Denegar", notifications, StringComparison.Ordinal);
        Assert.Contains(" Aprobar", notifications, StringComparison.Ordinal);
        Assert.Contains("setRequests(pending)", notifications, StringComparison.Ordinal);
        Assert.Contains("reconnectTimer", client, StringComparison.Ordinal);
        Assert.Contains("data?.stream ?? data?.Stream", messageParser, StringComparison.Ordinal);
        Assert.Contains("void connect();", client, StringComparison.Ordinal);
        Assert.Contains("createPortal(", notifications, StringComparison.Ordinal);
        Assert.Contains("Recibe aprobaciones con Auraly cerrada", notifications, StringComparison.Ordinal);
        Assert.Contains("auraly:pos-approvals-changed", notifications, StringComparison.Ordinal);
        Assert.DoesNotContain("PosSynchronizationStreams.Approvals =>", synchronization, StringComparison.Ordinal);
        Assert.Contains("self.addEventListener(\"push\"", serviceWorker, StringComparison.Ordinal);
        Assert.Contains("client.postMessage({ type: \"auraly:pos-approvals-changed\" })", serviceWorker, StringComparison.Ordinal);
    }

    [Fact]
    public void Workspace_configuration_refreshes_without_cache_and_keeps_current_values_read_only_offline()
    {
        var repositoryRoot = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(
            repositoryRoot, "admin", "src", "app", "(pos)", "pos", "page.tsx"));
        var setup = File.ReadAllText(Path.Combine(
            repositoryRoot, "admin", "src", "app", "(pos)", "pos", "pos-online-setup.tsx"));
        var bootstrap = File.ReadAllText(Path.Combine(
            repositoryRoot, "admin", "src", "services", "pos", "online-pos-bootstrap.ts"));

        var changeWorkspaceStart = page.IndexOf(
            "async function changeOnlineWorkspace()", StringComparison.Ordinal);
        var changeWorkspaceEnd = page.IndexOf(
            "if (client instanceof PosEdgeClient", changeWorkspaceStart, StringComparison.Ordinal);
        Assert.True(changeWorkspaceStart >= 0 && changeWorkspaceEnd > changeWorkspaceStart);
        var changeWorkspace = page[changeWorkspaceStart..changeWorkspaceEnd];

        Assert.Contains("await loadSalesWorkspaceBootstrap()", changeWorkspace, StringComparison.Ordinal);
        Assert.DoesNotContain("if (client.mode === \"edge\")", changeWorkspace, StringComparison.Ordinal);
        Assert.Contains("setWorkspaceConfigurationOffline(true)", changeWorkspace, StringComparison.Ordinal);
        Assert.Contains("workstation.businessId", changeWorkspace, StringComparison.Ordinal);
        Assert.Contains("workstation.warehouseId", changeWorkspace, StringComparison.Ordinal);
        Assert.Contains("{ cache: \"no-store\" }", bootstrap, StringComparison.Ordinal);
        Assert.Contains("disabled={configurationOffline}", setup, StringComparison.Ordinal);
        Assert.Contains("configurationOffline || !canEnrollOffline", setup, StringComparison.Ordinal);
        Assert.Contains("if (configurationOffline)", setup, StringComparison.Ordinal);
        Assert.Contains("onCancel?.()", setup, StringComparison.Ordinal);
    }

    [Fact]
    public void Enrolled_warehouse_policy_and_remote_unenrollment_use_the_canonical_push_outbox()
    {
        var repositoryRoot = FindRepositoryRoot();
        var streams = File.ReadAllText(Path.Combine(repositoryRoot,
            "src", "BuildingBlocks", "Auraly.BuildingBlocks.Application", "PosSynchronization.cs"));
        var warehouseStore = File.ReadAllText(Path.Combine(repositoryRoot,
            "src", "Infrastructure", "Auraly.Infrastructure.Persistence", "SqlInventoryQueryStore.cs"));
        var pricingSnapshot = File.ReadAllText(Path.Combine(repositoryRoot,
            "database", "Auraly.Database", "StoredProcedures", "PosPricingSnapshotGet.sql"));
        var edgeSynchronization = File.ReadAllText(Path.Combine(repositoryRoot,
            "src", "Pos", "Auraly.Pos.Edge.Host", "PosSynchronization.cs"));
        var deviceAdministration = File.ReadAllText(Path.Combine(repositoryRoot,
            "src", "Infrastructure", "Auraly.Platform.Infrastructure", "Identity", "SqlTenantDeviceAdminStore.cs"));
        var setup = File.ReadAllText(Path.Combine(repositoryRoot,
            "admin", "src", "app", "(pos)", "pos", "pos-online-setup.tsx"));

        Assert.Contains("const string Configuration", streams, StringComparison.Ordinal);
        Assert.Contains("N'Configuration'", warehouseStore, StringComparison.Ordinal);
        Assert.Contains("AllowNegativeStockSales", pricingSnapshot, StringComparison.Ordinal);
        Assert.Contains("PosSynchronizationStreams.Configuration => PosSynchronizationTrigger.Catalog", edgeSynchronization, StringComparison.Ordinal);
        Assert.Contains("PosSynchronizationStreams.DeviceEnrollment", edgeSynchronization, StringComparison.Ordinal);
        Assert.Contains("TargetDeviceId", deviceAdministration, StringComparison.Ordinal);
        Assert.Contains("Equipo enrolado", setup, StringComparison.Ordinal);
        Assert.Contains("Preparar este equipo para trabajar sin conexión", setup, StringComparison.Ordinal);
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
    public void Pos_product_search_keeps_local_results_independent_from_online_warehouse_availability()
    {
        var repositoryRoot = FindRepositoryRoot();
        var dialog = File.ReadAllText(Path.Combine(
            repositoryRoot, "admin", "src", "app", "(pos)", "pos",
            "pos-product-search-dialog.tsx"));
        var page = File.ReadAllText(Path.Combine(
            repositoryRoot, "admin", "src", "app", "(pos)", "pos", "page.tsx"));
        var edgeHost = File.ReadAllText(Path.Combine(
            repositoryRoot, "src", "Pos", "Auraly.Pos.Edge.Host", "Program.cs"));
        var enrollment = File.ReadAllText(Path.Combine(
            repositoryRoot, "src", "Modules", "Organization",
            "Auraly.Application.Organization", "PosEnrollmentService.cs"));
        var compatibilityTable = File.ReadAllText(Path.Combine(
            repositoryRoot, "database", "Auraly.Database", "Tables",
            "PosDevicePermissions.sql"));
        var preDeployment = File.ReadAllText(Path.Combine(
            repositoryRoot, "database", "Auraly.Database", "Scripts",
            "PreDeployment.sql"));

        Assert.Contains("const availabilityVersion = useRef(0)", dialog, StringComparison.Ordinal);
        Assert.Contains("El producto local sigue disponible", dialog, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Existencias por sede y bodega\"", dialog, StringComparison.Ordinal);
        Assert.Contains("connected={serverConnected}", page, StringComparison.Ordinal);
        Assert.Contains("client.productWarehouseAvailability(productId)", page, StringComparison.Ordinal);
        Assert.Contains("/catalog/products/{productId:guid}/warehouse-availability", edgeHost, StringComparison.Ordinal);
        Assert.Contains("const string inventoryAvailabilityRead = \"pos.inventory.availability.read\"", edgeHost, StringComparison.Ordinal);
        Assert.Contains(".includes(\"pos.inventory.availability.read\")", page, StringComparison.Ordinal);
        Assert.DoesNotContain("DevicePermissions", enrollment, StringComparison.Ordinal);
        Assert.Contains("Compatibilidad de despliegue", compatibilityTable, StringComparison.Ordinal);
        Assert.Contains("La API actual no", compatibilityTable, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoveLegacyPosDevicePermissions", preDeployment, StringComparison.Ordinal);
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
        Assert.Contains("title=\"Facturas desde pedidos\"", peripheralDialog, StringComparison.Ordinal);
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
            "/login#edgeToken=",
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
