using System.Security.Cryptography;
using System.Text;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.BuildingBlocks.Infrastructure.Identifiers;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Pos.Edge.Host;

public sealed record PosEdgeRuntimeContext(
    PosDraftScope Scope,
    bool WarehouseAllowsNegativeStock);

public sealed record CaptureRequest(string Value, Guid? CustomerId);
public sealed record QuantityRequest(decimal Quantity);
public sealed record SaveTemporaryRequest(string Name, string? Reference, string? Observation);

public static class PosEdgeHostApplication
{
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
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
        var credentials = new PosDeviceCredentials(
            RequiredGuid(builder.Configuration, "PosEdge:DeviceId"),
            Required(builder.Configuration, "PosEdge:DeviceSecret"));
        var runtime = new PosEdgeRuntimeContext(
            new PosDraftScope(
                new BusinessId(RequiredGuid(builder.Configuration, "PosEdge:BusinessId")),
                new WarehouseId(RequiredGuid(builder.Configuration, "PosEdge:WarehouseId")),
                new RegisterId(RequiredGuid(builder.Configuration, "PosEdge:RegisterId")),
                new UserId(RequiredGuid(builder.Configuration, "PosEdge:UserId"))),
            builder.Configuration.GetValue<bool>("PosEdge:WarehouseAllowsNegativeStock"));

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IAuralyIdGenerator, Uuid7AuralyIdGenerator>();
        builder.Services.AddSingleton(runtime);
        builder.Services.AddSingleton(new PosCatalogStore(connectionString));
        builder.Services.AddSingleton(sp => new PosDraftStore(
            connectionString,
            sp.GetRequiredService<IAuralyIdGenerator>(),
            sp.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton(new HttpClient { BaseAddress = new Uri(serverUrl) });
        builder.Services.AddSingleton(credentials);
        builder.Services.AddSingleton<PosCatalogSynchronizer>();
        builder.Services.AddSingleton<IPosInventoryAvailabilityClient>(
            sp => sp.GetRequiredService<PosCatalogSynchronizer>());
        builder.Services.AddSingleton<PosCaptureService>();
        builder.Services.AddHostedService<PosEdgeStorageInitializer>();

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
                context.Response.Headers.AccessControlAllowHeaders = "Content-Type,X-Auraly-Edge-Session";
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
            await next(context);
        });

        var edge = app.MapGroup("/edge/v1");
        edge.MapGet("/health", () => Results.Ok(new { status = "Ready" }));
        edge.MapGet("/drafts/active", async (
            PosDraftStore drafts,
            PosEdgeRuntimeContext context,
            CancellationToken ct) =>
            Results.Ok(await drafts.GetOrCreateActiveAsync(context.Scope, ct)));
        edge.MapPost("/capture", async (
            CaptureRequest request,
            PosCaptureService capture,
            PosEdgeRuntimeContext context,
            IAuralyIdGenerator ids,
            CancellationToken ct) =>
        {
            var result = await capture.CaptureAsync(
                request.Value,
                context.Scope,
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
        edge.MapDelete("/drafts/{draftId:guid}/lines/{lineId:guid}", async (
            Guid draftId,
            Guid lineId,
            PosDraftStore drafts,
            CancellationToken ct) =>
            Results.Ok(await drafts.RemoveLineAsync(new DraftId(draftId), lineId, ct)));
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
                context.Scope.BusinessId,
                new PosTemporaryFilter(Search: search),
                ct)));
        edge.MapPost("/temporaries/{draftId:guid}/recover", async (
            Guid draftId,
            PosDraftStore drafts,
            PosEdgeRuntimeContext context,
            CancellationToken ct) =>
            Results.Ok(await drafts.RecoverTemporaryAsync(
                new DraftId(draftId),
                context.Scope,
                ct)));
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


    private static bool IsLoopback(System.Net.IPAddress? address) =>
        address is null || System.Net.IPAddress.IsLoopback(address);
}

internal sealed class PosEdgeStorageInitializer(
    PosCatalogStore catalog,
    PosDraftStore drafts) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await catalog.InitializeAsync(cancellationToken);
        await drafts.InitializeAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class Program
{
    public static async Task Main(string[] args)
    {
        var app = PosEdgeHostApplication.Build(args);
        await app.RunAsync();
    }
}
