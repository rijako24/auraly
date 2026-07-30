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
    string? Reference);

public sealed record CompleteDraftRequest(
    string? CustomerIdentification,
    IReadOnlyCollection<CompletePaymentRequest> Payments,
    PosSaleUblSnapshotContract? UblSnapshot = null);

internal sealed record PosSaleHostSettings(
    RegisterContext Register,
    UserId UserId,
    Guid DeviceId,
    string SupplierTaxId,
    string DefaultCustomerIdentification,
    FiscalTechnicalKey TechnicalKey,
    FiscalEnvironment Environment,
    string QrValidationUrl,
    int PaperWidthMillimeters,
    PosEdgeDocumentSeriesProvision DocumentSeries,
    PosEdgeSeriesProvision FiscalSeries,
    IReadOnlySet<string> Permissions);
internal static class PosSaleHostModule
{
    public static IServiceCollection AddPosSaleCompletion(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString,
        PosEdgeRuntimeContext runtime,
        PosDeviceCredentials device)
    {
        var tenantId = new TenantId(RequiredGuid(configuration, "PosEdge:TenantId"));
        var locationId = new LocationId(RequiredGuid(configuration, "PosEdge:LocationId"));
        var permissions = ReadPermissions(configuration).ToHashSet(StringComparer.Ordinal);
        var settings = new PosSaleHostSettings(
            new RegisterContext(
                tenantId,
                runtime.Scope.BusinessId,
                locationId,
                runtime.Scope.WarehouseId,
                runtime.Scope.RegisterId,
                runtime.WarehouseAllowsNegativeStock),
            runtime.Scope.UserId,
            device.DeviceId,
            Required(configuration, "PosEdge:SupplierTaxId"),
            configuration["PosEdge:DefaultCustomerIdentification"] ?? "222222222222",
            new FiscalTechnicalKey(
                PosEdgeProtectedSecret.UnprotectTechnicalKey(
                    Required(configuration, "PosEdge:SecretKeyDirectory"),
                    Required(configuration, "PosEdge:Fiscal:ProtectedTechnicalKey")),
                Required(configuration, "PosEdge:Fiscal:TechnicalKeyVersion")),
            Enum.Parse<FiscalEnvironment>(
                Required(configuration, "PosEdge:Fiscal:Environment"),
                ignoreCase: true),
            Required(configuration, "PosEdge:Fiscal:QrValidationUrl"),
            RequiredPaperWidth(configuration),
            new PosEdgeDocumentSeriesProvision(
                RequiredGuid(configuration, "PosEdge:Documents:SalesInvoice:SeriesId"),
                runtime.Scope.RegisterId,
                AuralyDocumentTypes.SalesInvoice,
                AuralyDocumentTypes.DefaultPrefix(AuralyDocumentTypes.SalesInvoice),
                Required(configuration, "PosEdge:RegisterCode"),
                RequiredInt(configuration, "PosEdge:Documents:SalesInvoice:Padding"),
                RequiredLong(configuration, "PosEdge:Documents:SalesInvoice:RangeStart"),
                RequiredLong(configuration, "PosEdge:Documents:SalesInvoice:RangeEnd")),
            new PosEdgeSeriesProvision(
                RequiredGuid(configuration, "PosEdge:Fiscal:SeriesId"),
                runtime.Scope.RegisterId,
                Required(configuration, "PosEdge:Fiscal:Prefix"),
                Required(configuration, "PosEdge:Fiscal:AuthorizationNumber"),
                RequiredLong(configuration, "PosEdge:Fiscal:RangeStart"),
                RequiredLong(configuration, "PosEdge:Fiscal:RangeEnd"),
                RequiredDate(configuration, "PosEdge:Fiscal:ValidUntil"),
                RequiredGuid(configuration, "PosEdge:Fiscal:FiscalAuthorizationId")),
            permissions);
        var permissionSet = new UserPermissionSet(
            tenantId,
            runtime.Scope.UserId,
            permissions);
        var confirmation = new ConfirmOfflineSaleService(
            new PermissionAuthorizer(new ConfiguredPermissionProvider(permissionSet)));
        services.AddSingleton(settings);
        services.AddSingleton(new PosEdgeSaleStore(connectionString, confirmation));
        services.AddSingleton(sp => new PosDraftIssuanceStore(
            connectionString,
            sp.GetRequiredService<IAuralyIdGenerator>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<EscPosReceiptRenderer>();
        services.AddSingleton<HtmlReceiptPreviewRenderer>();
        services.AddSingleton<IReceiptPreviewLauncher, ShellReceiptPreviewLauncher>();
        services.AddSingleton<IPosReceiptPrinter>(sp =>
        {
            var renderer = sp.GetRequiredService<EscPosReceiptRenderer>();
            var outputDirectory = configuration["PosEdge:ReceiptOutputDirectory"];
            var mode = configuration["PosEdge:PrinterMode"]?.Trim();
            return mode switch
            {
                "BrowserPreview" => new HtmlReceiptPreviewPrinter(
                    Required(configuration, "PosEdge:ReceiptOutputDirectory"),
                    sp.GetRequiredService<HtmlReceiptPreviewRenderer>(),
                    sp.GetRequiredService<IReceiptPreviewLauncher>()),
                "File" => new FileReceiptPrinter(
                    Required(configuration, "PosEdge:ReceiptOutputDirectory"),
                    renderer),
                "WindowsRaw" => new WindowsRawReceiptPrinter(
                    Required(configuration, "PosEdge:PrinterName"),
                    renderer),
                null or "" => string.IsNullOrWhiteSpace(outputDirectory)
                    ? new WindowsRawReceiptPrinter(
                        Required(configuration, "PosEdge:PrinterName"),
                        renderer)
                    : new FileReceiptPrinter(outputDirectory, renderer),
                _ => throw new InvalidOperationException(
                    "PosEdge:PrinterMode must be BrowserPreview, File or WindowsRaw.")
            };
        });
        services.AddSingleton<PosSaleCompletionService>();
        services.AddHostedService<PosSaleStorageInitializer>();
        return services;
    }

    public static RouteGroupBuilder MapPosSaleCompletion(this RouteGroupBuilder edge)
    {
        edge.MapGet("/sales/next-number", async (
            PosEdgeSaleStore sales,
            PosSaleHostSettings settings,
            CancellationToken ct) =>
        {
            var document = await sales.PreviewNextDocumentNumberAsync(
                settings.Register.RegisterId,
                AuralyDocumentTypes.SalesInvoice,
                ct);
            var fiscal = await sales.PreviewNextFiscalNumberAsync(
                settings.Register.RegisterId,
                DateTimeOffset.Now,
                ct);
            return Results.Ok(new { document, fiscal });
        });

        edge.MapPost("/drafts/{draftId:guid}/complete", async (
            Guid draftId,
            CompleteDraftRequest request,
            PosSaleCompletionService completion,
            PosSaleHostSettings settings,
            CancellationToken ct) =>
        {
            try
            {
                var payments = request.Payments
                    .Select(payment => new OfflineSalePayment(
                        payment.MethodCode,
                        payment.Amount,
                        payment.Reference))
                    .ToArray();
                var result = await completion.CompleteAsync(
                    new DraftId(draftId),
                    new CompletePosSaleCommand(
                        settings.UserId,
                        settings.Register,
                        DateTimeOffset.Now,
                        settings.SupplierTaxId,
                        string.IsNullOrWhiteSpace(request.CustomerIdentification)
                            ? settings.DefaultCustomerIdentification
                            : request.CustomerIdentification.Trim(),
                        settings.TechnicalKey,
                        settings.Environment,
                        settings.QrValidationUrl,
                        payments,
                        settings.DeviceId,
                        settings.PaperWidthMillimeters,
                        request.UblSnapshot),
                    ct);
                return Results.Ok(result);
            }
            catch (IOException error)
            {
                return Results.Problem(
                    error.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "La venta fue emitida, pero la tirilla no pudo imprimirse.");
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
            CancellationToken ct) =>
        {
            if (!settings.Permissions.Contains(CommercePermissionCodes.SalesReprint))
                return Results.Problem(
                    $"Permission '{CommercePermissionCodes.SalesReprint}' is required.",
                    statusCode: StatusCodes.Status403Forbidden);
            try
            {
                await completion.ReprintAsync(
                    new DocumentId(documentId),
                    settings.UserId,
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
    PosSaleHostSettings settings) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await sales.InitializeAsync(cancellationToken);
        await issuance.InitializeAsync(cancellationToken);
        await sales.ProvisionDocumentSeriesAsync(settings.DocumentSeries, cancellationToken);
        await sales.ProvisionSeriesAsync(settings.FiscalSeries, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
