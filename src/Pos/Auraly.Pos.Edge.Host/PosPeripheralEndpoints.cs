using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Sales;
using Auraly.Contracts.WorkSessions;
using Auraly.Pos.Edge.Infrastructure;
using Auraly.Pos.Printing;

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
        services.AddSingleton<CreditSaleAcknowledgementRenderer>();
        services.AddSingleton<IWindowsRawPrintJob>(SystemWindowsRawPrintJob.Instance);
        services.AddSingleton<IWindowsRenderedPrintJob, SystemWindowsRenderedPrintJob>();
        services.AddSingleton<IReceiptPreviewLauncher, ShellReceiptPreviewLauncher>();
        var dataDirectory = Path.GetDirectoryName(Path.GetFullPath(databasePath))!;
        var enrollmentPrinterMode = configuration["PosEdge:PrinterMode"]?.Trim();
        var enrollmentPrinterDefault = PosPrinterConfiguration.Default with
        {
            ReceiptMode = PosPrinterModes.IsValid(enrollmentPrinterMode ?? string.Empty)
                ? enrollmentPrinterMode!
                : PosPrinterConfiguration.Default.ReceiptMode
        };
        services.AddSingleton(new PosPrinterConfigurationStore(
            Path.Combine(dataDirectory, "printer-settings.json"),
            configuration["PosEdge:ReceiptOutputDirectory"]
                ?? Path.Combine(dataDirectory, "receipts"),
            enrollmentPrinterDefault,
            WindowsPrinterDiscovery.GetInstalledPrinters));
        services.AddSingleton<ConfigurableOrderDocumentPrinter>();
        services.AddSingleton<ConfigurablePosReceiptPrinter>();
        services.AddSingleton<IPosReceiptPrinter>(sp =>
            sp.GetRequiredService<ConfigurablePosReceiptPrinter>());
        services.AddSingleton<PosCashMovementTicketPrinter>();
        services.AddSingleton<PosCashDenominationCountTicketPrinter>();
        services.AddSingleton<PosWorkSessionClosurePrinter>();
        services.AddSingleton<IPosWorkSessionClosurePrinter>(sp =>
            sp.GetRequiredService<PosWorkSessionClosurePrinter>());
        services.AddSingleton<PosCashDrawer>();
        services.AddSingleton<PosScaleReader>();
        return services;
    }

    public static RouteGroupBuilder MapPosPeripheralEndpoints(
        this RouteGroupBuilder edge)
    {
        edge.MapGet("/configuration/printers", (
            PosPrinterConfigurationStore printers) =>
        {
            try
            {
                return Results.Ok(printers.GetView());
            }
            catch (InvalidDataException exception)
            {
                return Results.Problem(
                    exception.Message,
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (InvalidOperationException exception)
            {
                return Results.Problem(
                    exception.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (IOException exception)
            {
                return Results.Problem(
                    exception.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        edge.MapPut("/configuration/printers", (
            PosPrinterConfiguration request,
            PosPrinterConfigurationStore printers) =>
        {
            try
            {
                var view = printers.SaveView(request);
                return Results.Ok(view);
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]>
                    {
                        [nameof(PosPrinterConfiguration)] = [exception.Message]
                    });
            }
            catch (InvalidOperationException exception)
            {
                return Results.Problem(
                    exception.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (IOException exception)
            {
                return Results.Problem(
                    exception.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        edge.MapPost("/print/receipt", async (
            DirectPrintReceiptRequest request,
            string? workflow,
            ConfigurablePosReceiptPrinter printer,
            CancellationToken ct) =>
        {
            var orderTicketWorkflow = workflow == "order-tickets";
            if (request.DocumentId == Guid.Empty ||
                (orderTicketWorkflow
                    ? request.DocumentType != "Order"
                    : !PosSaleDocumentTypes.IsSupported(request.DocumentType)))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request)] = ["El documento para imprimir no es válido."]
                });
            if (workflow is not (null or "pos" or "order-tickets"))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(workflow)] = ["El flujo de impresión no es válido."]
                });
            try
            {
                var receipt = new PosReceipt(
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
                    80,
                    request.DocumentType,
                    request.CompanyName,
                    request.CompanyLogoSource,
                    CustomerName: request.CustomerName,
                    BusinessName: request.BusinessName,
                    WarehouseName: request.WarehouseName,
                    CreditAcknowledgement: request.CreditAcknowledgement,
                    InvoicePrintDetails: request.InvoicePrintDetails);
                if (orderTicketWorkflow)
                    await printer.PrintOrderAsync(receipt, ct);
                else
                    await printer.PrintAsync(receipt, ct);
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

        edge.MapPost("/print/cash-movement", async (
            PosCashMovementTicket request,
            PosCashMovementTicketPrinter printer,
            CancellationToken ct) =>
        {
            try
            {
                await printer.PrintAsync(request, ct);
                return Results.NoContent();
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request)] = [exception.Message]
                });
            }
            catch (Exception exception) when (
                exception is IOException or InvalidOperationException)
            {
                return Results.Problem(
                    exception.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        edge.MapPost("/print/work-session-closure", async (
            WorkSessionClosureView closure,
            IPosWorkSessionClosurePrinter printer,
            CancellationToken ct) =>
        {
            try
            {
                await printer.PrintAsync(closure, ct);
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

        edge.MapPost("/render/work-session-closure", (
            WorkSessionClosureView closure,
            PosWorkSessionClosurePrinter printer) =>
            Results.Ok(new WorkSessionClosureReceiptView(
                printer.RenderHtml(closure))));

        edge.MapPost("/print/cash-denomination-count", async (
            PosCashDenominationCountTicket request,
            PosCashDenominationCountTicketPrinter printer,
            CancellationToken ct) =>
        {
            try
            {
                await printer.PrintAsync(request, ct);
                return Results.NoContent();
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(request)] = [exception.Message]
                });
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
