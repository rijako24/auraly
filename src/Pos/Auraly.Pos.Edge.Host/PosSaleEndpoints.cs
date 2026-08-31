using Auraly.Application.Authorization;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Organization;
using Auraly.Contracts.Sales;
using Auraly.Domain.Authorization;
using Auraly.Fiscal.Core;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Pos.Edge.Host;

public sealed record CompletePaymentRequest(
    string MethodCode,
    decimal Amount,
    string? Reference,
    string? CardFranchiseCode = null,
    string? ApprovalNumber = null);

public sealed record CompleteDraftRequest(
    string? CustomerIdentification,
    IReadOnlyCollection<CompletePaymentRequest> Payments,
    PosSaleUblSnapshotContract? UblSnapshot = null,
    string DocumentType = PosSaleDocumentTypes.Invoice);

internal sealed record PosFiscalHostSettings(
    string SupplierTaxId,
    FiscalTechnicalKey TechnicalKey,
    FiscalEnvironment Environment,
    string QrValidationUrl,
    PosEdgeSeriesProvision Series);

internal sealed class PosFiscalRuntimeSettings(PosFiscalHostSettings? initial)
{
    private PosFiscalHostSettings? current = initial;
    public PosFiscalHostSettings? Current => Volatile.Read(ref current);
    public void Replace(PosFiscalHostSettings value) =>
        Volatile.Write(ref current, value ?? throw new ArgumentNullException(nameof(value)));
    public void Clear() => Volatile.Write(ref current, null);
}

internal sealed record PosSaleHostSettings(
    TenantId TenantId,
    BusinessId BusinessId,
    WarehouseId WarehouseId,
    DeviceId DeviceId,
    bool WarehouseAllowsNegativeStock,
    string DefaultCustomerIdentification,
    int PaperWidthMillimeters,
    PosEdgeDocumentSeriesProvision DocumentSeries,
    PosEdgeDocumentSeriesProvision ReceiptDocumentSeries,
    PosFiscalHostSettings? Fiscal)
{
    public SalesExecutionContext ContextFor(PosLocalUserSession session) => new(
        TenantId,
        BusinessId,
        WarehouseId,
        new UserId(session.UserId),
        DeviceId,
        new WorkSessionId(session.WorkSessionId),
        WarehouseAllowsNegativeStock);
}
internal static class PosSaleHostModule
{
    public static IServiceCollection AddPosSaleCompletion(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString,
        string databasePath,
        PosEdgeRuntimeContext runtime,
        PosDeviceCredentials device)
    {
        services.AddPosPeripherals(configuration, databasePath);
        var tenantId = new TenantId(RequiredGuid(configuration, "PosEdge:TenantId"));
        _ = ReadPermissions(configuration);
        var fiscal = ReadFiscalSettings(configuration, runtime.DeviceId);
        var settings = new PosSaleHostSettings(
            tenantId,
            runtime.BusinessId,
            runtime.WarehouseId,
            runtime.DeviceId,
            runtime.WarehouseAllowsNegativeStock,
            configuration["PosEdge:DefaultCustomerIdentification"] ?? "222222222222",
            RequiredPaperWidth(configuration),
            new PosEdgeDocumentSeriesProvision(
                RequiredGuid(configuration, "PosEdge:Documents:SalesInvoice:SeriesId"),
                runtime.DeviceId,
                AuralyDocumentTypes.SalesInvoice,
                AuralyDocumentTypes.DefaultPrefix(AuralyDocumentTypes.SalesInvoice),
                Required(configuration, "PosEdge:Documents:SalesInvoice:SeriesCode"),
                RequiredInt(configuration, "PosEdge:Documents:SalesInvoice:Padding"),
                RequiredLong(configuration, "PosEdge:Documents:SalesInvoice:RangeStart"),
                RequiredLong(configuration, "PosEdge:Documents:SalesInvoice:RangeEnd")),
            new PosEdgeDocumentSeriesProvision(
                RequiredGuid(configuration, "PosEdge:Documents:SalesReceipt:SeriesId"),
                runtime.DeviceId,
                AuralyDocumentTypes.SalesReceipt,
                AuralyDocumentTypes.DefaultPrefix(AuralyDocumentTypes.SalesReceipt),
                Required(configuration, "PosEdge:Documents:SalesReceipt:SeriesCode"),
                RequiredInt(configuration, "PosEdge:Documents:SalesReceipt:Padding"),
                RequiredLong(configuration, "PosEdge:Documents:SalesReceipt:RangeStart"),
                RequiredLong(configuration, "PosEdge:Documents:SalesReceipt:RangeEnd")),
            fiscal);
        services.AddSingleton(settings);
        services.AddSingleton(new PosFiscalRuntimeSettings(fiscal));
        services.AddSingleton(sp => new PosEdgeSaleStore(
            connectionString,
            new ConfirmOfflineSaleService(
                new PermissionAuthorizer(
                    new PosLocalPermissionProvider(
                        sp.GetRequiredService<PosLocalSessionAccessor>())))));
        services.AddSingleton(sp => new PosDraftIssuanceStore(
            connectionString,
            sp.GetRequiredService<IAuralyIdGenerator>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<PosSaleCompletionService>();
        services.AddHostedService<PosSaleStorageInitializer>();
        return services;
    }

    public static RouteGroupBuilder MapPosSaleCompletion(this RouteGroupBuilder edge)
    {
        edge.MapGet("/sales/next-number", async (
            string? documentType,
            PosEdgeSaleStore sales,
            PosSaleHostSettings settings,
            PosLocalSessionAccessor sessions,
            CancellationToken ct) =>
        {
            var selectedType = PosSaleDocumentTypes.IsSupported(documentType ?? string.Empty)
                ? documentType!
                : PosSaleDocumentTypes.Invoice;
            var document = await sales.PreviewNextDocumentNumberAsync(
                settings.DeviceId,
                selectedType,
                ct);
            var fiscal = PosSaleDocumentTypes.IsFiscal(selectedType)
                ? await sales.PreviewNextFiscalNumberAsync(
                    settings.DeviceId, DateTimeOffset.Now, ct)
                : null;
            return Results.Ok(new { document, fiscal });
        });

        edge.MapGet("/drafts/{draftId:guid}/settlement", async (
            Guid draftId,
            PosSaleCompletionService completion,
            CancellationToken ct) =>
            Results.Ok(await completion.PreviewSettlementAsync(
                new DraftId(draftId), ct)));

        edge.MapPost("/drafts/{draftId:guid}/complete", async (
            Guid draftId,
            CompleteDraftRequest request,
            PosSaleCompletionService completion,
            PosSaleHostSettings settings,
            PosFiscalRuntimeSettings fiscalRuntime,
            PosCashDrawer cashDrawer,
            PosSynchronizationSignal synchronization,
            PosLocalSessionAccessor sessions,
            ILogger<PosSaleCompletionService> logger,
            CancellationToken ct) =>
        {
            try
            {
                var payments = request.Payments
                    .Select(payment => new OfflineSalePayment(
                        payment.MethodCode,
                        payment.Amount,
                        payment.Reference,
                        payment.CardFranchiseCode,
                        payment.ApprovalNumber))
                    .ToArray();
                var session = sessions.Required();
                var result = await completion.CompleteAsync(
                    new DraftId(draftId),
                    new CompletePosSaleCommand(
                        new UserId(session.UserId),
                        settings.ContextFor(session),
                        DateTimeOffset.Now,
                        fiscalRuntime.Current?.SupplierTaxId,
                        string.IsNullOrWhiteSpace(request.CustomerIdentification)
                            ? settings.DefaultCustomerIdentification
                            : request.CustomerIdentification.Trim(),
                        fiscalRuntime.Current?.TechnicalKey,
                        fiscalRuntime.Current?.Environment,
                        fiscalRuntime.Current?.QrValidationUrl,
                        payments,
                        settings.PaperWidthMillimeters,
                        request.UblSnapshot,
                        request.DocumentType),
                    ct);
                synchronization.Signal(PosSynchronizationTrigger.LocalOutbox);
                if (!result.PrintedDirectly && !string.IsNullOrWhiteSpace(result.PrintError))
                    logger.LogWarning(
                        "Sale {DocumentId} was issued but direct printing failed: {PrintError}",
                        result.IssuedSale.DocumentId.Value,
                        result.PrintError);
                // The sale is already durably issued at this point. Every completed
                // sale opens the local drawer, including offline sales. A disconnected
                // drawer must not turn a successful sale into a 409.
                cashDrawer.TryOpen();
                return Results.Ok(result);
            }
            catch (InvalidOperationException error)
            {
                return Results.Conflict(new { detail = error.Message });
            }
        });

        edge.MapGet("/sales/{documentId:guid}/fiscal-status", async (
            Guid documentId,
            PosEdgeSaleStore sales,
            CancellationToken ct) =>
        {
            var status = await sales.GetFiscalStatusAsync(new DocumentId(documentId), ct);
            return status is null ? Results.NotFound() : Results.Ok(status);
        });

        edge.MapPost("/sales/{documentId:guid}/reprint", async (
            Guid documentId,
            PosSaleCompletionService completion,
            PosSaleHostSettings settings,
            PosLocalSessionAccessor sessions,
            CancellationToken ct) =>
        {
            var user = sessions.Required();
            if (!user.Permissions.Contains(CommercePermissionCodes.SalesReprint))
                return Results.Problem(
                    $"Permission '{CommercePermissionCodes.SalesReprint}' is required.",
                    statusCode: StatusCodes.Status403Forbidden);
            try
            {
                await completion.ReprintAsync(
                    new DocumentId(documentId),
                    new UserId(user.UserId),
                    settings.PaperWidthMillimeters,
                    ct);
                return Results.NoContent();
            }
            catch (KeyNotFoundException error)
            {
                return Results.NotFound(new { detail = error.Message });
            }
        });
        return edge;
    }

    private static PosFiscalHostSettings? ReadFiscalSettings(
        IConfiguration configuration,
        DeviceId deviceId)
    {
        if (string.IsNullOrWhiteSpace(configuration["PosEdge:Fiscal:SeriesId"]))
            return null;
        return new PosFiscalHostSettings(
            Required(configuration, "PosEdge:SupplierTaxId"),
            new FiscalTechnicalKey(
                PosEdgeProtectedSecret.UnprotectTechnicalKey(
                    Required(configuration, "PosEdge:SecretKeyDirectory"),
                    Required(configuration, "PosEdge:Fiscal:ProtectedTechnicalKey")),
                Required(configuration, "PosEdge:Fiscal:TechnicalKeyVersion")),
            Enum.Parse<FiscalEnvironment>(
                Required(configuration, "PosEdge:Fiscal:Environment"),
                ignoreCase: true),
            Required(configuration, "PosEdge:Fiscal:QrValidationUrl"),
            new PosEdgeSeriesProvision(
                RequiredGuid(configuration, "PosEdge:Fiscal:SeriesId"),
                deviceId,
                Required(configuration, "PosEdge:Fiscal:Prefix"),
                Required(configuration, "PosEdge:Fiscal:AuthorizationNumber"),
                RequiredLong(configuration, "PosEdge:Fiscal:RangeStart"),
                RequiredLong(configuration, "PosEdge:Fiscal:RangeEnd"),
                RequiredDate(configuration, "PosEdge:Fiscal:ValidUntil"),
                RequiredGuid(configuration, "PosEdge:Fiscal:FiscalAuthorizationId"),
                RequiredDate(configuration, "PosEdge:Fiscal:ValidFrom"),
                OptionalLong(configuration, "PosEdge:Fiscal:AuthorizationRangeStart",
                    RequiredLong(configuration, "PosEdge:Fiscal:RangeStart")),
                OptionalLong(configuration, "PosEdge:Fiscal:AuthorizationRangeEnd",
                    RequiredLong(configuration, "PosEdge:Fiscal:RangeEnd")),
                OptionalInt(configuration, "PosEdge:Fiscal:ExpirationWarningDays", 3),
                OptionalLong(configuration,
                    "PosEdge:Fiscal:RemainingNumberWarningThreshold", 100),
                OptionalBool(configuration, "PosEdge:Fiscal:ProductionActive", true)));
    }

    private static string Required(IConfiguration configuration, string key) =>
        string.IsNullOrWhiteSpace(configuration[key])
            ? throw new InvalidOperationException($"{key} is required.")
            : configuration[key]!;

    private static Guid RequiredGuid(IConfiguration configuration, string key) =>
        Guid.TryParse(Required(configuration, key), out var value) && value != Guid.Empty
            ? value
            : throw new InvalidOperationException($"{key} must be a non-empty GUID.");

    private static long RequiredLong(IConfiguration configuration, string key) =>
        long.TryParse(Required(configuration, key), out var value)
            ? value
            : throw new InvalidOperationException($"{key} must be an integer.");

    private static long OptionalLong(
        IConfiguration configuration, string key, long fallback) =>
        long.TryParse(configuration[key], out var value) ? value : fallback;

    private static int OptionalInt(
        IConfiguration configuration, string key, int fallback) =>
        int.TryParse(configuration[key], out var value) ? value : fallback;

    private static bool OptionalBool(
        IConfiguration configuration, string key, bool fallback) =>
        bool.TryParse(configuration[key], out var value) ? value : fallback;

    private static int RequiredInt(IConfiguration configuration, string key) =>
        int.TryParse(Required(configuration, key), out var value)
            ? value
            : throw new InvalidOperationException($"{key} must be an integer.");

    private static DateOnly RequiredDate(IConfiguration configuration, string key) =>
        DateOnly.TryParse(Required(configuration, key), out var value)
            ? value
            : throw new InvalidOperationException($"{key} must be an ISO date.");

    private static int RequiredPaperWidth(IConfiguration configuration)
    {
        var value = configuration.GetValue<int>("PosEdge:PaperWidthMillimeters");
        return value is 58 or 80
            ? value
            : throw new InvalidOperationException(
                "PosEdge:PaperWidthMillimeters must be 58 or 80.");
    }

    private static IReadOnlyCollection<string> ReadPermissions(IConfiguration configuration)
    {
        var values = configuration
            .GetSection("PosEdge:Permissions")
            .Get<string[]>()?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];
        if (!values.Contains(CommercePermissionCodes.SalesCreate, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"PosEdge:Permissions must contain {CommercePermissionCodes.SalesCreate}.");
        return values;
    }

    private sealed class ConfiguredPermissionProvider(UserPermissionSet permissionSet)
        : IUserPermissionSetProvider
    {
        public UserPermissionSet Get(TenantId tenantId, UserId userId)
        {
            if (tenantId != permissionSet.TenantId || userId != permissionSet.UserId)
                throw new UnauthorizedAccessException("The POS permission snapshot does not match.");
            return permissionSet;
        }
    }
}

internal sealed class PosSaleStorageInitializer(
    PosEdgeSaleStore sales,
    PosDraftIssuanceStore issuance,
    PosSaleHostSettings settings,
    PosFiscalRuntimeSettings fiscalRuntime) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await sales.InitializeAsync(cancellationToken);
        await issuance.InitializeAsync(cancellationToken);
        await sales.ProvisionDocumentSeriesAsync(settings.DocumentSeries, cancellationToken);
        if (fiscalRuntime.Current is { } fiscal)
            await sales.ProvisionSeriesAsync(fiscal.Series, cancellationToken);
        await sales.ProvisionDocumentSeriesAsync(settings.ReceiptDocumentSeries, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
