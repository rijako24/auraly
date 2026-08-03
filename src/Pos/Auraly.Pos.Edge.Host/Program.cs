using System.Security.Cryptography;
using System.Text;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.BuildingBlocks.Infrastructure.Identifiers;
using Auraly.Contracts.Catalog;
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
        var enrollmentStore = new PosEdgeEnrollmentStore(packagePath, keyDirectory);
        var enrollment = enrollmentStore.Load();
        if (enrollment is null &&
            string.IsNullOrWhiteSpace(builder.Configuration["PosEdge:DeviceId"]))
            return BuildEnrollmentRequired(
                builder, sessionToken, allowedOrigin, serverUrl, enrollmentStore);
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
            Required(builder.Configuration, "PosEdge:BusinessName"),
            Required(builder.Configuration, "PosEdge:WarehouseName"),
            Required(builder.Configuration, "PosEdge:UserDisplayName")));
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
        builder.Services.AddSingleton<PosOrderServerClient>();
        builder.Services.AddSingleton<PosOrderRecoveryService>();
        builder.Services.AddSingleton<IPosInventoryAvailabilityClient>(
            sp => sp.GetRequiredService<PosCatalogSynchronizer>());
        builder.Services.AddSingleton<PosCaptureService>();
        builder.Services.AddSingleton<PosCustomerSelectionService>();
        builder.Services.AddPosSaleCompletion(
            builder.Configuration,
            connectionString,
            runtime,
            credentials);
        builder.Services.AddSingleton<IPosSaleUploadClient>(sp =>
            new HttpPosSaleUploadClient(
                sp.GetRequiredService<HttpClient>(),
                credentials.Secret));
        builder.Services.AddSingleton<PosEdgeOutboxUploader>();
        builder.Services.AddSingleton<IPosFiscalStatusClient, HttpPosFiscalStatusClient>();
        builder.Services.AddSingleton<PosFiscalStatusSynchronizer>();
        builder.Services.AddHostedService<PosEdgeStorageInitializer>();
        builder.Services.AddSingleton<PosSynchronizationSignal>();
        builder.Services.AddSingleton<PosSynchronizationWork>();
        builder.Services.AddSingleton(sp => new PosWebPubSubConnection(
            sp.GetRequiredService<HttpClient>(),
            credentials,
            sp.GetRequiredService<PosSynchronizationSignal>(),
            sp.GetRequiredService<PosServerConnectionState>(),
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
                context.Response.Headers.AccessControlAllowHeaders = "Content-Type,X-Auraly-Edge-Session,X-Auraly-User-Session";
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
            CancellationToken ct) =>
        {
            var catalogStatus = await catalog.StatusAsync(ct);
            var identityReady = await identities.HasValidSnapshotAsync(ct);
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
                businessName = workstation.BusinessName,
                warehouseName = workstation.WarehouseName,
                userDisplayName = user?.DisplayName ?? string.Empty,
                userId = user?.UserId,
                permissions = user?.Permissions ?? Array.Empty<string>(),
                catalogStatus = catalogStatus.Status,
                catalogCursor = catalogStatus.Cursor
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
            return Results.Ok(new
            {
                items = values.Take(pageSize),
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
                request.CustomerId,
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
            PosDraftStore drafts,
            PosLocalSessionAccessor sessions,
            CancellationToken ct) =>
        {
            if (!sessions.Required().Permissions.Contains(
                    Auraly.Contracts.Authorization.CommercePermissionCodes.SalesDiscount))
                return Results.Problem(
                    "Permission 'sales.discount' is required.",
                    statusCode: StatusCodes.Status403Forbidden);
            return Results.Ok(await drafts.SetDiscountAsync(
                new DraftId(draftId), lineId, request.Discount, ct));
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
            PosDraftStore drafts,
            PosLocalSessionAccessor sessions,
            CancellationToken ct) =>
        {
            if (!sessions.Required().Permissions.Contains(
                    Auraly.Contracts.Authorization.CommercePermissionCodes.SalesVoid))
                return Results.Problem(
                    "Permission 'sales.void' is required.",
                    statusCode: StatusCodes.Status403Forbidden);
            return Results.Ok(await drafts.RemoveLineAsync(
                new DraftId(draftId), lineId, ct));
        });
        edge.MapDelete("/drafts/{draftId:guid}", async (
            Guid draftId,
            PosDraftStore drafts,
            PosEdgeRuntimeContext context,
            PosLocalSessionAccessor sessions,
            CancellationToken ct) =>
        {
            var user = sessions.Required();
            if (!user.Permissions.Contains(
                    Auraly.Contracts.Authorization.CommercePermissionCodes.SalesVoid))
            {
                return Results.Problem(
                    "Permission 'sales.void' is required.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            await drafts.CancelAsync(new DraftId(draftId), ct);
            return Results.Ok(await drafts.GetOrCreateActiveAsync(
                context.ScopeFor(user), ct));
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
        return app;
    }

    private static WebApplication BuildEnrollmentRequired(
        WebApplicationBuilder builder,
        string sessionToken,
        string allowedOrigin,
        string serverUrl,
        PosEdgeEnrollmentStore store)
    {
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var serverUri) ||
            (serverUri.Scheme != Uri.UriSchemeHttps && !serverUri.IsLoopback))
            throw new InvalidOperationException(
                "PosEdge:ServerUrl must use HTTPS except for a loopback development server.");
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton(new HttpClient { BaseAddress = serverUri });
        builder.Services.AddSingleton<PosEdgeEnrollmentClient>();
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
        edge.MapGet("/health", () => Results.Ok(new
        {
            status = "EnrollmentRequired",
            serverConnected = false,
            deviceSeriesCode = "",
            businessName = "",
            warehouseName = "",
            userDisplayName = ""
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
        !path.Equals("/edge/v1/auth/login");

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
}
