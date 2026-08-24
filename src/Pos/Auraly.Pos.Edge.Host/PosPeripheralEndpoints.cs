using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Sales;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Pos.Edge.Host;

internal static class PosPeripheralModule
{
    public static IServiceCollection AddPosPeripherals(
        this IServiceCollection services,
        IConfiguration configuration,
        string databasePath)
    {
        services.AddSingleton<EscPosReceiptRenderer>();
        services.AddSingleton<HtmlReceiptPreviewRenderer>();
        services.AddSingleton<HalfLetterDocumentRenderer>();
        services.AddSingleton<IWindowsRawPrintJob>(SystemWindowsRawPrintJob.Instance);
        services.AddSingleton<IWindowsRenderedPrintJob, SystemWindowsRenderedPrintJob>();
        services.AddSingleton<IReceiptPreviewLauncher, ShellReceiptPreviewLauncher>();
        var dataDirectory = Path.GetDirectoryName(Path.GetFullPath(databasePath))!;
        services.AddSingleton(new PosPrinterConfigurationStore(
            Path.Combine(dataDirectory, "printer-settings.json"),
            configuration["PosEdge:ReceiptOutputDirectory"]
                ?? Path.Combine(dataDirectory, "receipts")));
        services.AddSingleton<ConfigurableOrderDocumentPrinter>();
        services.AddSingleton<ConfigurablePosReceiptPrinter>();
        services.AddSingleton<IPosReceiptPrinter>(sp =>
            sp.GetRequiredService<ConfigurablePosReceiptPrinter>());
        services.AddSingleton<PosCashDrawer>();
        services.AddSingleton<PosScaleReader>();
        return services;
    }

    public static RouteGroupBuilder MapPosPeripheralEndpoints(
        this RouteGroupBuilder edge)
    {
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
                string.IsNullOrWhiteSpace(settings.PosPrinterName))
                return Results.Problem(
                    "Configura una impresora para impresión directa.",
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
            catch (Exception exception) when (
                exception is IOException or InvalidOperationException)
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
            catch (Exception exception) when (
                exception is IOException or InvalidOperationException or ArgumentException)
            {
                return Results.Problem(
                    exception.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        return edge;
    }
}
