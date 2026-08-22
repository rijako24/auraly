using System.Security.Cryptography;
using System.Text;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.BuildingBlocks.Infrastructure.Identifiers;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Sales;
using Auraly.Contracts.WorkSessions;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Pos.Edge.Host;

public sealed record PosEdgeRuntimeContext(
    BusinessId BusinessId,
    WarehouseId WarehouseId,
    DeviceId DeviceId,
    bool WarehouseAllowsNegativeStock)
{
    public PosDraftScope ScopeFor(PosLocalUserSession session) => new(
        BusinessId,
        WarehouseId,
        DeviceId,
        new WorkSessionId(session.WorkSessionId),
        new UserId(session.UserId));
}

public sealed record PosWorkstationIdentity(
    string DeviceSeriesCode,
    string BusinessName,
    string WarehouseName,
    string UserDisplayName);

public sealed record CaptureRequest(string Value, Guid? CustomerId);
public sealed record QuantityRequest(decimal Quantity);
public sealed record DiscountRequest(decimal Discount);
public sealed record SelectCustomerRequest(Guid? CustomerId);
public sealed record SaveTemporaryRequest(string Name, string? Reference, string? Observation);
public sealed record DirectPrintReceiptRequest(
    Guid DocumentId,
    string DocumentType,
    string DocumentNumber,
    string? FiscalNumber,
    DateTimeOffset IssuedAt,
    string CustomerIdentification,
    IReadOnlyCollection<PosReceiptLine> Lines,
    IReadOnlyCollection<OfflineSalePayment> Payments,
    decimal UntaxedAmount,
    decimal TaxAmount,
    decimal PayableAmount,
    string? Cufe,
    string? QrPayload);

public static class PosEdgeHostApplication
{
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseWindowsService(options =>
        {
            options.ServiceName = "Auraly POS Edge";
        });
        builder.WebHost.UseUrls(
            builder.Configuration["PosEdge:Url"] ?? "http://127.0.0.1:47831");

        var databasePath = builder.Configuration["PosEdge:DatabasePath"];
        if (string.IsNullOrWhiteSpace(databasePath))
            databasePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Auraly",
                "PosEdge",
                "auraly-pos.db");
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var connectionString = $"Data Source={databasePath}";

        var sessionToken = Required(builder.Configuration, "PosEdge:SessionToken");
        if (Encoding.UTF8.GetByteCount(sessionToken) < 32)
            throw new InvalidOperationException("PosEdge:SessionToken must contain at least 32 bytes.");
        var allowedOrigin = Required(builder.Configuration, "PosEdge:AllowedOrigin");
        var serverUrl = Required(builder.Configuration, "PosEdge:ServerUrl");
        var keyDirectory = builder.Configuration["PosEdge:SecretKeyDirectory"];
        if (string.IsNullOrWhiteSpace(keyDirectory))
            keyDirectory = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(databasePath))!, "keys");
        var packagePath = builder.Configuration["PosEdge:EnrollmentPackagePath"];
        if (string.IsNullOrWhiteSpace(packagePath))
            packagePath = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(databasePath))!,
                "enrollment.protected");
        var startupModePath = builder.Configuration["PosEdge:StartupModePath"];
        if (string.IsNullOrWhiteSpace(startupModePath))
            startupModePath = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(databasePath))!,
                "startup-mode");
        var startupModeStore = new PosStartupModeStore(startupModePath);
        var enrollmentStore = new PosEdgeEnrollmentStore(packagePath, keyDirectory);
        var enrollment = enrollmentStore.Load();
        if (enrollment is null &&
            string.IsNullOrWhiteSpace(builder.Configuration["PosEdge:DeviceId"]))
            return BuildEnrollmentRequired(
                builder,
                sessionToken,
                allowedOrigin,
                serverUrl,
                enrollmentStore,
                startupModeStore);
        if (enrollment is not null)
            builder.Configuration.AddInMemoryCollection(
                PosEdgeEnrollmentStore.ToConfiguration(
                    enrollment,
                    keyDirectory,
                    databasePath));
        var credentials = new PosDeviceCredentials(
            RequiredGuid(builder.Configuration, "PosEdge:DeviceId"),
            Required(builder.Configuration, "PosEdge:DeviceSecret"));
        var tenantId = RequiredGuid(builder.Configuration, "PosEdge:TenantId");
        var runtime = new PosEdgeRuntimeContext(
            new BusinessId(RequiredGuid(builder.Configuration, "PosEdge:BusinessId")),
            new WarehouseId(RequiredGuid(builder.Configuration, "PosEdge:WarehouseId")),
            new DeviceId(RequiredGuid(builder.Configuration, "PosEdge:DeviceId")),
            builder.Configuration.GetValue<bool>("PosEdge:WarehouseAllowsNegativeStock"));

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IAuralyIdGenerator, Uuid7AuralyIdGenerator>();
        builder.Services.AddSingleton(runtime);
        builder.Services.AddSingleton(new PosOperationalScope(
            runtime.BusinessId.Value,
            runtime.WarehouseId.Value));
        builder.Services.AddSingleton<PosLocalSessionAccessor>();
        builder.Services.AddSingleton(enrollmentStore);
        builder.Services.AddSingleton(startupModeStore);
        builder.Services.AddSingleton<PosEdgeEnrollmentClient>();
        builder.Services.AddSingleton(sp => new PosLocalIdentityStore(
            connectionString,
            keyDirectory,
            sp.GetRequiredService<IAuralyIdGenerator>(),
            sp.GetRequiredService<TimeProvider>()));
        builder.Services.Configure<PosOfflineLeaseTrustOptions>(
            builder.Configuration.GetSection(PosOfflineLeaseTrustOptions.SectionName));
        builder.Services.AddSingleton<PosOfflineLeaseVerifier>();
        builder.Services.AddSingleton(sp => new PosOfflineLeaseStore(
            connectionString,
            tenantId,
            credentials.DeviceId,
            sp.GetRequiredService<PosOfflineLeaseVerifier>(),
            sp.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<PosOfflineLeaseClient>();
        builder.Services.AddSingleton<PosEdgeAuthenticationService>();
        builder.Services.AddSingleton(new PosWorkstationIdentity(
            Required(builder.Configuration, "PosEdge:Documents:SalesInvoice:SeriesCode"),
            OptionalLabel(builder.Configuration, "PosEdge:BusinessName", "Negocio sin nombre"),
            OptionalLabel(builder.Configuration, "PosEdge:WarehouseName", "Bodega sin nombre"),
            OptionalLabel(builder.Configuration, "PosEdge:UserDisplayName", "Usuario sin nombre")));
        builder.Services.AddSingleton(new PosCatalogStore(connectionString));
        builder.Services.AddSingleton(sp => new PosDraftStore(
            connectionString,
            sp.GetRequiredService<IAuralyIdGenerator>(),
            sp.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<PosServerConnectionState>();
        builder.Services.AddSingleton(sp => new HttpClient(
            new PosServerConnectionHandler(
                new HttpClientHandler(),
                sp.GetRequiredService<PosServerConnectionState>()))
        {
            BaseAddress = new Uri(serverUrl)
        });
        builder.Services.AddSingleton(credentials);
        builder.Services.AddSingleton<PosCatalogSynchronizer>();
        builder.Services.AddSingleton<PosIdentitySynchronizer>();
        builder.Services.AddSingleton<PosCustomerServerClient>();
        builder.Services.AddSingleton<PosRemoteApprovalClient>();
        builder.Services.AddSingleton<PosSensitiveActionAuthorizer>();
        builder.Services.AddSingleton<PosOrderServerClient>();
        builder.Services.AddSingleton<PosOrderRecoveryService>();
        builder.Services.AddSingleton<IPosInventoryAvailabilityClient>(
            sp => sp.GetRequiredService<PosCatalogSynchronizer>());
        builder.Services.AddSingleton<PosCaptureService>();
        builder.Services.AddSingleton<PosCustomerSelectionService>();
        builder.Services.AddPosSaleCompletion(
            builder.Configuration,
            connectionString,
            databasePath,
            runtime,
            credentials);
        builder.Services.AddSingleton(sp => new PosCashMovementStore(
            connectionString,
            sp.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<PosCashMovementServerClient>();
        builder.Services.AddSingleton<PosWorkSessionClosureServerClient>();
        builder.Services.AddSingleton<PosWorkSessionClosurePrinter>();
        builder.Services.AddSingleton<PosCashDrawer>();
        builder.Services.AddSingleton<PosScaleReader>();
        builder.Services.AddSingleton<PosPendingClosureAuthorizationStore>();
        builder.Services.AddHostedService<PosCashMovementStorageInitializer>();
        builder.Services.AddSingleton<IPosSaleUploadClient>(sp =>
            new HttpPosSaleUploadClient(
                sp.GetRequiredService<HttpClient>(),
                credentials.Secret));
        builder.Services.AddSingleton<PosEdgeOutboxUploader>();
        builder.Services.AddSingleton<IPosFiscalStatusClient, HttpPosFiscalStatusClient>();
        builder.Services.AddSingleton<PosFiscalStatusSynchronizer>();
        builder.Services.AddSingleton<PosFiscalProvisioningSynchronizer>();
        builder.Services.AddHostedService<PosEdgeStorageInitializer>();
        builder.Services.AddSingleton<PosSynchronizationSignal>();
        builder.Services.AddSingleton<PosUiStateSignal>();
        builder.Services.AddSingleton<PosSynchronizationState>();
        builder.Services.AddSingleton<PosSynchronizationWork>();
        builder.Services.AddSingleton(sp => new PosWebPubSubConnection(
            sp.GetRequiredService<HttpClient>(),
            credentials,
            sp.GetRequiredService<PosSynchronizationSignal>(),
            sp.GetRequiredService<PosServerConnectionState>(),
            sp.GetRequiredService<PosUiStateSignal>(),
            tenantId,
            runtime.BusinessId.Value));
        builder.Services.AddHostedService<PosEventDrivenSynchronizationHostedService>();
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (!IsLoopback(context.Connection.RemoteIpAddress))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            var origin = context.Request.Headers.Origin.ToString();
            if (!string.IsNullOrEmpty(origin) &&
                !string.Equals(origin, allowedOrigin, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            if (HttpMethods.IsOptions(context.Request.Method))
            {
                if (string.IsNullOrEmpty(origin))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }
                SetCorsHeaders(context.Response, allowedOrigin);
                context.Response.Headers.AccessControlAllowMethods = "GET,POST,PUT,DELETE,OPTIONS";
                context.Response.Headers.AccessControlAllowHeaders = "Content-Type,X-Auraly-Edge-Session,X-Auraly-User-Session,X-Auraly-Supervisor-Secret,X-Auraly-Approval-Id,X-Auraly-Operation-Id";
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }
            var presented = context.Request.Headers["X-Auraly-Edge-Session"].ToString();
            if (!FixedEquals(sessionToken, presented))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            if (!string.IsNullOrEmpty(origin))
            {
                SetCorsHeaders(context.Response, allowedOrigin);
            }
            PosLocalSessionAccessor? localSessions = null;
            if (RequiresLocalUserSession(context.Request.Path))
            {
                var userToken = context.Request.Headers["X-Auraly-User-Session"].ToString();
                var identities = context.RequestServices
                    .GetRequiredService<PosLocalIdentityStore>();
                var userSession = await identities.ResolveAsync(
                    userToken, context.RequestAborted);
                if (userSession is null)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        code = "LocalLoginRequired",
                        detail = "Inicia sesiÃ³n en este dispositivo para continuar."
                    });
                    return;
                }
                localSessions = context.RequestServices
                    .GetRequiredService<PosLocalSessionAccessor>();
                localSessions.Current = userSession;
            }
            try
            {
                await next(context);
            }
            catch (PosLocalApprovalException error)
            {
                context.Response.StatusCode = error.Code == "ApprovalRequired"
                    ? StatusCodes.Status428PreconditionRequired
                    : StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = error.Code,
                    detail = error.Message
                });
            }
            catch (PosOrderServerException error)
            {
                context.Response.StatusCode = error.StatusCode is >= 400 and <= 599
                    ? error.StatusCode
                    : StatusCodes.Status502BadGateway;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "OrderServerRejected",
                    detail = "El servidor rechaz\u00F3 la operaci\u00F3n de pedidos."
                });
            }            catch (InvalidOperationException error)
                when (string.Equals(
                    error.Message,
                    "The sale was already issued and is locked until its receipt is printed.",
                    StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "IssuedPendingPrint",
                    detail = "La factura ya fue emitida y estÃ¡ pendiente de imprimir la tirilla. Presiona F1 para reintentar la impresiÃ³n."
        });
            }
            finally
            {
                if (localSessions is not null) localSessions.Current = null;
            }
        });

        var edge = app.MapGroup("/edge/v1");
        edge.MapGet("/configuration/startup-mode", (
            PosStartupModeStore startupMode) =>
            Results.Ok(new { mode = startupMode.Load(hasEnrollment: true) }));
        edge.MapPut("/configuration/startup-mode", (
            PosStartupModeRequest request,
            PosStartupModeStore startupMode) =>
        {
            if (!PosStartupModes.IsValid(request.Mode))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.Mode)] = ["El modo debe ser online o enrolled."]
                });
            startupMode.Save(request.Mode);
            return Results.NoContent();
        });
        edge.MapGet("/configuration/printers", (
            PosPrinterConfigurationStore printers) =>
            Results.Ok(new PosPrinterConfigurationView(
                printers.Load(), printers.InstalledPrinters(), printers.SerialPorts())));
        edge.MapPut("/configuration/printers", (
            PosPrinterConfiguration request,
            PosPrinterConfigurationStore printers) =>
        {
            try
            {
                return Results.Ok(new PosPrinterConfigurationView(
                    printers.Save(request), printers.InstalledPrinters(), printers.SerialPorts()));
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        [nameof(PosPrinterConfiguration)] = [exception.Message]
                    });
            }
        });
        edge.MapPost("/print/receipt", async (
            DirectPrintReceiptRequest request,
            IPosReceiptPrinter printer,
            PosPrinterConfigurationStore configuration,
            CancellationToken ct) =>
        {
            if (request.DocumentId == Guid.Empty ||
                !PosSaleDocumentTypes.IsSupported(request.DocumentType))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request)] = ["El documento para imprimir no es válido."]
                });
            var settings = configuration.Load();
            if (settings.ReceiptMode != PosPrinterModes.WindowsRaw ||
                string.IsNullOrWhiteSpace(settings.ReceiptPrinterName))
                return Results.Problem(
                    "Configura una impresora de tirilla para impresión directa.",
                    statusCode: StatusCodes.Status409Conflict);
            try
            {
                await printer.PrintAsync(new PosReceipt(
                    Guid.NewGuid(),
                    new DocumentId(request.DocumentId),
                    request.DocumentNumber,
                    request.FiscalNumber,
                    request.IssuedAt,
                    request.CustomerIdentification,
                    request.Lines,
                    request.Payments,
                    request.UntaxedAmount,
                    request.TaxAmount,
                    request.PayableAmount,
                    request.Cufe,
                    request.QrPayload,
                    settings.ReceiptPaperWidthMillimeters,
                    request.DocumentType), ct);
                return Results.NoContent();
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                return Results.Problem(
                    exception.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
        edge.MapPost("/cash-drawer/open", (
            PosCashDrawer cashDrawer) =>
        {
            try
            {
                cashDrawer.Open();
                return Results.NoContent();
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                return Results.Problem(
                    exception.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
        edge.MapPost("/scale/read", async (
            PosScaleReader scale,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await scale.ReadAsync(ct));
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
            {
                return Results.Problem(
                    exception.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
        edge.MapPost("/print/work-session-closure", async (
            WorkSessionClosureView closure,
            PosWorkSessionClosurePrinter printer,
            CancellationToken ct) =>
        {
            try
            {
                await printer.PrintAsync(closure, ct);
                return Results.NoContent();
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                return Results.Problem(
                    exception.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        edge.MapPost("/enrollment/redeem", async (
            LocalPosEnrollmentRequest request,
            PosEdgeEnrollmentClient client,
            IHostApplicationLifetime lifetime,
            CancellationToken ct) =>
        {
            try
            {
                var result = await client.RedeemAsync(request, ct);
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    lifetime.StopApplication();
                });
                return Results.Ok(result);
            }
            catch (HttpRequestException exception)
            {
                return Results.Problem(
                    exception.Message,
                    statusCode: StatusCodes.Status502BadGateway,
                    title: "EnrollmentServerUnavailable");
            }
        });
        edge.MapGet("/events", async (
            HttpContext context,
            PosUiStateSignal uiState,
            CancellationToken ct) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";
            var (subscriptionId, reader) = uiState.Subscribe();
            try
            {
                await context.Response.WriteAsync("event: state\ndata: ready\n\n", ct);
                await context.Response.Body.FlushAsync(ct);
                await foreach (var _ in reader.ReadAllAsync(ct))
                {
                    await context.Response.WriteAsync("event: state\ndata: changed\n\n", ct);
                    await context.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            finally
            {
                uiState.Unsubscribe(subscriptionId);
            }
        });
        edge.MapPost("/auth/login", async (
            PosLocalLoginRequest request,
            PosEdgeAuthenticationService authentication,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await authentication.LoginAsync(request, ct));
            }
            catch (PosLocalLoginException error)
            {
                var status = error.Code == "Locked"
                    ? StatusCodes.Status423Locked
                    : error.Code == "IdentityUnavailable"
                        ? StatusCodes.Status503ServiceUnavailable
                        : error.Code == "OfflineLeaseConflict"
                            ? StatusCodes.Status409Conflict
                        : StatusCodes.Status401Unauthorized;
                return Results.Json(
                    new { code = error.Code, detail = error.Message },
                    statusCode: status);
            }
        });
        edge.MapGet("/cash-movement-reasons", async (
            string? direction,
            PosCashMovementStore store,
            PosCashMovementServerClient server,
            PosEdgeRuntimeContext context,
            CancellationToken ct) =>
        {
            if (!CashMovementDirections.IsSupported(direction ?? string.Empty))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(direction)] = ["La direccion debe ser In u Out."]
                });
            try
            {
                await server.RefreshReasonsAsync(context.BusinessId.Value, ct);
            }
            catch (HttpRequestException)
            {
                // The last durable catalog remains authoritative while offline.
            }
            return Results.Ok(await store.ListReasonsAsync(
                context.BusinessId.Value, direction!, ct));
        });
        edge.MapPost("/cash-movements", async (
            QueueLocalCashMovementRequest request,
            PosCashMovementStore store,
            PosSynchronizationSignal synchronization,
            PosEdgeRuntimeContext context,
            PosLocalSessionAccessor sessions,
            CancellationToken ct) =>
        {
            try
            {
                var user = sessions.Required();
                var acceptance = await store.QueueAsync(
                    context.BusinessId.Value,
                    user.WorkSessionId,
                    user.UserId,
                    request,
                    ct);
                synchronization.Signal(PosSynchronizationTrigger.LocalOutbox);
                return Results.Accepted(
                    "/edge/v1/cash-movements/" + request.DocumentId.ToString("D"),
                    acceptance);
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request)] = [exception.Message]
                });
            }
        });

        edge.MapPost("/approvals", async (
            CreatePosApprovalRequest request,
            PosRemoteApprovalClient approvals,
            PosLocalSessionAccessor sessions,
            CancellationToken ct) =>
        {
            var user=sessions.Required();
            if(request.BusinessId!=runtime.BusinessId.Value || request.DeviceId!=runtime.DeviceId.Value ||
               request.WorkSessionId!=user.WorkSessionId)
                return Results.BadRequest(new { code="InvalidScope", detail="La solicitud no coincide con el contexto local." });
            return Results.Ok(await approvals.CreateAsync(
                user, request.DraftId, request.LineId, request.PermissionResource, request.ContextJson, ct));
        });
        edge.MapGet("/auth/session", (
            PosLocalSessionAccessor sessions) =>
            Results.Ok(sessions.Required()));
        edge.MapPost("/auth/logout", async (
            HttpContext http,
            PosEdgeAuthenticationService authentication,
            PosSynchronizationSignal synchronization,
            CancellationToken ct) =>
        {
            await authentication.LogoutAsync(
                http.Request.Headers["X-Auraly-User-Session"].ToString(), ct);
            synchronization.Signal(PosSynchronizationTrigger.Authentication);
            return Results.NoContent();
        });
        edge.MapPost("/synchronization/refresh", (PosSynchronizationSignal synchronization) => { synchronization.Signal(PosSynchronizationTrigger.All); return Results.Accepted(); });
        edge.MapGet("/health", async (
            HttpContext http,
            PosServerConnectionState server,
            PosWorkstationIdentity workstation,
            PosCatalogStore catalog,
            PosLocalIdentityStore identities,
            PosSaleHostSettings saleSettings,
            PosStartupModeStore startupMode,
            PosSynchronizationState synchronizationState,
            CancellationToken ct) =>
        {
            var catalogStatus = await catalog.StatusAsync(ct);
            var identityReady = await identities.HasValidSnapshotAsync(ct);
            var syncStatus = synchronizationState.Current;
            var user = await identities.ResolveAsync(
                http.Request.Headers["X-Auraly-User-Session"].ToString(), ct);
            var status = !identityReady
                ? "IdentitySynchronizing"
                : user is null
                    ? "LoginRequired"
                    : catalogStatus.Status == "Ready"
                        ? "Ready"
                        : "Synchronizing";
            return Results.Ok(new
            {
                status,
                serverConnected = server.IsConnected,
                deviceSeriesCode = workstation.DeviceSeriesCode,
                businessId = runtime.BusinessId.Value,
                businessName = workstation.BusinessName,
                warehouseName = workstation.WarehouseName,
                userDisplayName = user?.DisplayName ?? string.Empty,
                userId = user?.UserId,
                workSessionId = user?.WorkSessionId,
                deviceId = runtime.DeviceId.Value,
                fiscalReady = saleSettings.Fiscal is not null,
                permissions = user?.Permissions ?? Array.Empty<string>(),
                catalogStatus = catalogStatus.Status,
                catalogCursor = catalogStatus.Cursor,
                catalogUpdatedAt = catalogStatus.UpdatedAt,
                synchronizationInProgress = syncStatus.IsSynchronizing,
                lastSynchronizationAt = syncStatus.LastSuccessfulAt ?? catalogStatus.UpdatedAt,
                lastSynchronizationFailed = syncStatus.LastAttemptFailed,
                startupMode = startupMode.Load(hasEnrollment: true)
            });
        });
        edge.MapGet("/drafts/active", async (
            PosDraftStore drafts,
            PosEdgeRuntimeContext context,
            PosLocalSessionAccessor sessions,
            CancellationToken ct) =>
            Results.Ok(await drafts.GetOrCreateActiveAsync(
                context.ScopeFor(sessions.Required()), ct)));
        edge.MapGet("/catalog/products", async (
            string? search,
            int? skip,
            int? take,
            Guid? customerId,
            PosCatalogStore catalog,
            CancellationToken ct) =>
        {
            var pageSize = Math.Clamp(take ?? 50, 1, 50);
            var offset = Math.Max(skip ?? 0, 0);
            var values = (await catalog.SearchAsync(
                search ?? string.Empty,
                offset,
                pageSize + 1,
                ct)).ToArray();
            var hasMore = values.Length > pageSize;
            var priced = new List<object>(Math.Min(values.Length, pageSize));
            foreach (var value in values.Take(pageSize))
            {
                var resolved = await catalog.ResolvePriceAsync(value.ProductId, customerId, 1m, ct);
                priced.Add(new {
                    value.ProductId,value.ProductCode,value.Reference,value.Name,value.BaseUnitCode,
                    value.TaxCode,value.TaxRate,unitPrice=resolved.Amount,resolved.CurrencyCode,
                    value.IsActive,value.IsWeighable,priceSource=resolved.Source
                });
            }
            return Results.Ok(new
            {
                items = priced,
                hasMore,
                nextOffset = hasMore ? offset + pageSize : (int?)null
            });
        });
        edge.MapGet("/customers", async (
            string? search,
            int? skip,
            int? take,
            PosCatalogStore catalog,
            CancellationToken ct) =>
        {
            var pageSize = Math.Clamp(take ?? 50, 1, 50);
            var offset = Math.Max(skip ?? 0, 0);
            var values = (await catalog.SearchCustomersAsync(
                search ?? string.Empty,
                offset,
                pageSize + 1,
                ct)).ToArray();
            var hasMore = values.Length > pageSize;
            return Results.Ok(new
            {
                items = values.Take(pageSize),
                hasMore,
                nextOffset = hasMore ? offset + pageSize : (int?)null
            });
        });
        edge.MapGet("/customers/geography/countries", async (
            PosCustomerServerClient server,
            CancellationToken ct) => Results.Ok(await server.CountriesAsync(ct)));
        edge.MapGet("/customers/geography/countries/{countryId:guid}/divisions", async (
            Guid countryId,
            PosCustomerServerClient server,
            CancellationToken ct) => Results.Ok(await server.DivisionsAsync(countryId, ct)));
        edge.MapGet("/customers/geography/divisions/{divisionId:guid}/cities", async (
            Guid divisionId,
            PosCustomerServerClient server,
            CancellationToken ct) => Results.Ok(await server.CitiesAsync(divisionId, ct)));
        edge.MapPost("/customers", async (
            PosCreateCustomerInput request,
            PosCustomerServerClient server,
            CancellationToken ct) => Results.Ok(await server.CreateAsync(request, ct)));
        edge.MapGet("/customers/{customerId:guid}", async (
            Guid customerId,
            PosCatalogStore catalog,
            CancellationToken ct) =>
        {
            var customer = await catalog.GetCustomerAsync(customerId, ct);
            return customer is null ? Results.NotFound() : Results.Ok(customer);
        });
        edge.MapGet("/sales", async (
            string? search,
            int? skip,
            int? take,
            PosEdgeSaleStore sales,
            CancellationToken ct) =>
        {
            var pageSize = Math.Clamp(take ?? 50, 1, 50);
            var offset = Math.Max(skip ?? 0, 0);
            var values = (await sales.SearchIssuedSalesAsync(
                search ?? string.Empty,
                offset,
                pageSize + 1,
                ct)).ToArray();
            var hasMore = values.Length > pageSize;
            return Results.Ok(new
            {
                items = values.Take(pageSize),
                hasMore,
                nextOffset = hasMore ? offset + pageSize : (int?)null
            });
        });

        edge.MapPost("/capture", async (
            CaptureRequest request,
            PosCaptureService capture,
            PosEdgeRuntimeContext context,
            IAuralyIdGenerator ids,
            PosLocalSessionAccessor sessions,
            CancellationToken ct) =>
        {
            var result = await capture.CaptureAsync(
                request.Value,
                context.ScopeFor(sessions.Required()),
                null,
                context.WarehouseAllowsNegativeStock,
                ids.NewId(),
                ct);
            return result.Status switch
            {
                PosCaptureStatus.Added => Results.Ok(result),
                PosCaptureStatus.NotFound => Results.NotFound(result),
                PosCaptureStatus.InsufficientInventory => Results.Conflict(result),
                PosCaptureStatus.OfflineValidationRequired =>
                    Results.Json(result, statusCode: StatusCodes.Status503ServiceUnavailable),
                _ => Results.Problem("Unknown POS capture result.")
            };
        });
        edge.MapPut("/drafts/{draftId:guid}/lines/{lineId:guid}/quantity", async (
            Guid draftId,
            Guid lineId,
            QuantityRequest request,
            PosCaptureService capture,
            PosEdgeRuntimeContext context,
            IAuralyIdGenerator ids,
            CancellationToken ct) =>
        {
            var result = await capture.ChangeQuantityAsync(
                new DraftId(draftId),
                lineId,
                request.Quantity,
                context.WarehouseAllowsNegativeStock,
                ids.NewId(),
                ct);
            return result.Status == PosCaptureStatus.Added
                ? Results.Ok(result)
                : Results.Conflict(result);
        });
        edge.MapPut("/drafts/{draftId:guid}/lines/{lineId:guid}/discount", async (
            Guid draftId,
            Guid lineId,
            DiscountRequest request,
            HttpContext http,
            PosDraftStore drafts,
            PosSensitiveActionAuthorizer authorizer,
            PosLocalSessionAccessor sessions,
            CancellationToken ct) =>
        {
            var authorization = await authorizer.AuthorizeAsync(
                sessions.Required(), CommercePermissionCodes.SalesDiscount, draftId, lineId,
                http.Request.Headers["X-Auraly-Approval-Id"],
                http.Request.Headers["X-Auraly-Operation-Id"],
                http.Request.Headers["X-Auraly-Supervisor-Secret"], ct);
            var result = await drafts.SetDiscountAsync(
                new DraftId(draftId), lineId, request.Discount, ct);
            await authorizer.CompleteAsync(authorization, ct);
            return Results.Ok(result);
        });
        edge.MapPut("/drafts/{draftId:guid}/customer", async (
            Guid draftId,
            SelectCustomerRequest request,
            PosCustomerSelectionService customers,
            CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await customers.SelectAsync(
                    new DraftId(draftId),
                    request.CustomerId,
                    ct));
            }
            catch (KeyNotFoundException error)
            {
                return Results.NotFound(new { detail = error.Message });
            }
        });
        edge.MapDelete("/drafts/{draftId:guid}/lines/{lineId:guid}", async (
            Guid draftId,
            Guid lineId,
            HttpContext http,
            PosDraftStore drafts,
            PosSensitiveActionAuthorizer authorizer,
            PosLocalSessionAccessor sessions,
            CancellationToken ct) =>
        {
            var authorization = await authorizer.AuthorizeAsync(
                sessions.Required(), CommercePermissionCodes.SalesRemoveLine, draftId, lineId,
                http.Request.Headers["X-Auraly-Approval-Id"],
                http.Request.Headers["X-Auraly-Operation-Id"],
                http.Request.Headers["X-Auraly-Supervisor-Secret"], ct);
            var result = await drafts.RemoveLineAsync(new DraftId(draftId), lineId, ct);
            await authorizer.CompleteAsync(authorization, ct);
            return Results.Ok(result);
        });
        edge.MapDelete("/drafts/{draftId:guid}", async (
            Guid draftId,
            HttpContext http,
            PosDraftStore drafts,
            PosEdgeRuntimeContext context,
            PosSensitiveActionAuthorizer authorizer,
            PosLocalSessionAccessor sessions,
            CancellationToken ct) =>
        {
            var user = sessions.Required();
            var authorization = await authorizer.AuthorizeAsync(
                user, CommercePermissionCodes.SalesRestartDraft, draftId, null,
                http.Request.Headers["X-Auraly-Approval-Id"],
                http.Request.Headers["X-Auraly-Operation-Id"],
                http.Request.Headers["X-Auraly-Supervisor-Secret"], ct);
            await drafts.CancelAsync(new DraftId(draftId), ct);
            var result = await drafts.GetOrCreateActiveAsync(context.ScopeFor(user), ct);
            await authorizer.CompleteAsync(authorization, ct);
            return Results.Ok(result);
        });
        edge.MapPost("/drafts/{draftId:guid}/temporary", async (
            Guid draftId,
            SaveTemporaryRequest request,
            PosDraftStore drafts,
            CancellationToken ct) =>
            Results.Ok(await drafts.SaveTemporaryAsync(
                new DraftId(draftId),
                request.Name,
                request.Reference,
                request.Observation,
                ct)));
        edge.MapGet("/temporaries", async (
            string? search,
            PosDraftStore drafts,
            PosEdgeRuntimeContext context,
            CancellationToken ct) =>
            Results.Ok(await drafts.ListTemporariesAsync(
                context.BusinessId,
                new PosTemporaryFilter(Search: search),
                ct)));
        edge.MapPost("/temporaries/{draftId:guid}/recover", async (
            Guid draftId,
            PosDraftStore drafts,
            PosEdgeRuntimeContext context,
            PosLocalSessionAccessor sessions,
            CancellationToken ct) =>
            Results.Ok(await drafts.RecoverTemporaryAsync(
                new DraftId(draftId),
                context.ScopeFor(sessions.Required()),
                ct)));
        edge.MapDelete("/temporaries/{draftId:guid}", async (
            Guid draftId,
            PosDraftStore drafts,
            PosEdgeRuntimeContext context,
            CancellationToken ct) =>
        {
            await drafts.DeleteTemporaryAsync(
                new DraftId(draftId),
                context.BusinessId,
                ct);
            return Results.NoContent();
        });
        edge.MapPosSaleCompletion();
        edge.MapPosOrders();
        edge.MapPosWorkSessionClosure();
        return app;
    }

    private static WebApplication BuildEnrollmentRequired(
        WebApplicationBuilder builder,
        string sessionToken,
        string allowedOrigin,
        string serverUrl,
        PosEdgeEnrollmentStore store,
        PosStartupModeStore startupModeStore)
    {
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var serverUri) ||
            (serverUri.Scheme != Uri.UriSchemeHttps && !serverUri.IsLoopback))
            throw new InvalidOperationException(
                "PosEdge:ServerUrl must use HTTPS except for a loopback development server.");
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton(startupModeStore);
        builder.Services.AddSingleton(new HttpClient { BaseAddress = serverUri });
        builder.Services.AddSingleton<PosEdgeEnrollmentClient>();
        builder.Services.AddSingleton<PosUiStateSignal>();
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (!IsLoopback(context.Connection.RemoteIpAddress))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            var origin = context.Request.Headers.Origin.ToString();
            if (!string.IsNullOrEmpty(origin) &&
                !string.Equals(origin, allowedOrigin, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            if (HttpMethods.IsOptions(context.Request.Method))
            {
                if (string.IsNullOrEmpty(origin))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }
                SetCorsHeaders(context.Response, allowedOrigin);
                context.Response.Headers.AccessControlAllowMethods = "GET,POST,OPTIONS";
                context.Response.Headers.AccessControlAllowHeaders =
                    "Content-Type,X-Auraly-Edge-Session,X-Auraly-User-Session";
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }
            if (!FixedEquals(
                    sessionToken,
                    context.Request.Headers["X-Auraly-Edge-Session"].ToString()))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            if (!string.IsNullOrEmpty(origin)) SetCorsHeaders(context.Response, allowedOrigin);
            await next(context);
        });
        var edge = app.MapGroup("/edge/v1");
        edge.MapGet("/configuration/startup-mode", () =>
            Results.Ok(new
            {
                mode = startupModeStore.Load(hasEnrollment: false)
            }));
        edge.MapPut("/configuration/startup-mode", (
            PosStartupModeRequest request) =>
        {
            if (!PosStartupModes.IsValid(request.Mode))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request.Mode)] = ["El modo debe ser online o enrolled."]
                });
            startupModeStore.Save(request.Mode);
            return Results.NoContent();
        });
        edge.MapGet("/events", async (
            HttpContext context,
            PosUiStateSignal uiState,
            CancellationToken ct) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";
            var (subscriptionId, reader) = uiState.Subscribe();
            try
            {
                await context.Response.WriteAsync("event: state\ndata: ready\n\n", ct);
                await context.Response.Body.FlushAsync(ct);
                await foreach (var _ in reader.ReadAllAsync(ct))
                {
                    await context.Response.WriteAsync("event: state\ndata: changed\n\n", ct);
                    await context.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            finally
            {
                uiState.Unsubscribe(subscriptionId);
            }
        });
        edge.MapGet("/health", () => Results.Ok(new
        {
            status = "EnrollmentRequired",
            serverConnected = false,
            deviceSeriesCode = "",
            businessId = "",
            businessName = "",
            warehouseName = "",
            userDisplayName = "",
            fiscalReady = false,
            startupMode = startupModeStore.Load(hasEnrollment: false)
        }));
        edge.MapPost("/enrollment/redeem", async (
            LocalPosEnrollmentRequest request,
            PosEdgeEnrollmentClient client,
            IHostApplicationLifetime lifetime,
            CancellationToken ct) =>
        {
            try
            {
                var result = await client.RedeemAsync(request, ct);
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    lifetime.StopApplication();
                });
                return Results.Ok(result);
            }
            catch (HttpRequestException exception)
            {
                return Results.Problem(
                    exception.Message,
                    statusCode: StatusCodes.Status502BadGateway,
                    title: "EnrollmentServerUnavailable");
            }
        });
        return app;
    }

    private static string OptionalLabel(IConfiguration configuration, string key, string fallback)
    {
        var value = configuration[key];
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string Required(IConfiguration configuration, string key) =>
        string.IsNullOrWhiteSpace(configuration[key])
            ? throw new InvalidOperationException($"{key} is required.")
            : configuration[key]!;

    private static Guid RequiredGuid(IConfiguration configuration, string key) =>
        Guid.TryParse(Required(configuration, key), out var value) && value != Guid.Empty
            ? value
            : throw new InvalidOperationException($"{key} must be a non-empty GUID.");

    private static bool FixedEquals(string expected, string actual)
    {
        var left = Encoding.UTF8.GetBytes(expected);
        var right = Encoding.UTF8.GetBytes(actual);
        return left.Length == right.Length &&
               CryptographicOperations.FixedTimeEquals(left, right);
    }
    private static void SetCorsHeaders(HttpResponse response, string allowedOrigin)
    {
        response.Headers.AccessControlAllowOrigin = allowedOrigin;
        response.Headers.Vary = "Origin";
    }


    private static bool RequiresLocalUserSession(PathString path) =>
        path.StartsWithSegments("/edge/v1") &&
        !path.Equals("/edge/v1/health") &&
        !path.Equals("/edge/v1/auth/login") &&
        !path.Equals("/edge/v1/enrollment/redeem") &&
        !path.Equals("/edge/v1/configuration/startup-mode") &&
        !path.StartsWithSegments("/edge/v1/configuration/printers") &&
        !path.StartsWithSegments("/edge/v1/print") &&
        !path.Equals("/edge/v1/cash-drawer/open") &&
        !path.Equals("/edge/v1/scale/read");

    private static bool IsLoopback(System.Net.IPAddress? address) =>
        address is null || System.Net.IPAddress.IsLoopback(address);
}

internal sealed class PosEdgeStorageInitializer(
    PosLocalIdentityStore identities,
    PosOfflineLeaseStore leases,
    PosCatalogStore catalog,
    PosDraftStore drafts) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await identities.InitializeAsync(cancellationToken);
        await leases.InitializeAsync(cancellationToken);
        await catalog.InitializeAsync(cancellationToken);
        await drafts.InitializeAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Contains("--initialize-storage", StringComparer.Ordinal))
        {
            var databasePath = ReadArgument(args, "--database-path") ??
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Auraly",
                    "PosEdge",
                    "auraly-pos.db");
            await PosStorageBootstrap.InitializeAsync(databasePath);
            return;
        }

        if (args.Contains("--protect-fiscal-key", StringComparer.Ordinal))
        {
            var hostArgs = args
                .Where(argument => !string.Equals(
                    argument,
                    "--protect-fiscal-key",
                    StringComparison.Ordinal))
                .ToArray();
            var builder = WebApplication.CreateBuilder(hostArgs);
            var keyDirectory = builder.Configuration["PosEdge:SecretKeyDirectory"];
            if (string.IsNullOrWhiteSpace(keyDirectory))
                throw new InvalidOperationException("PosEdge:SecretKeyDirectory is required.");
            var technicalKey = await Console.In.ReadLineAsync()
                ?? throw new InvalidOperationException("The technical key must be provided through standard input.");
            Console.Out.WriteLine(
                PosEdgeProtectedSecret.ProtectTechnicalKey(keyDirectory, technicalKey));
            return;
        }

        var app = PosEdgeHostApplication.Build(args);
        await app.RunAsync();
    }

    private static string? ReadArgument(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.Ordinal))
                return args[index + 1];
        }

        return null;
    }
}
