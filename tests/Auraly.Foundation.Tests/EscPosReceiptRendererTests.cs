using System.Text;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Sales;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Foundation.Tests;

public sealed class EscPosReceiptRendererTests
{
    [Theory]
    [InlineData(58)]
    [InlineData(80)]
    public void Receipt_contains_company_fiscal_numbers_cufe_exact_qr_and_cut_command(int width)
    {
        var qr = """
            NumFac: FV100
            FecFac: 2026-07-28
            CUFE: abc123
            https://dian.example?documentkey=abc123
            """;
        var receipt = new PosReceipt(
            Guid.NewGuid(),
            new DocumentId(Guid.NewGuid()),
            "FV100",
            "VTA03-00000100",
            new DateTimeOffset(2026, 7, 28, 14, 30, 0, TimeSpan.FromHours(-5)),
            "222222222",
            [new PosReceiptLine("P-001", "Café molido", 2m, 10_000m, 500m, 3_705m, 23_205m, "01", 19m)],
            [new OfflineSalePayment("Cash", 10_000m), new OfflineSalePayment("Transfer", 13_205m)],
            19_500m,
            3_705m,
            23_205m,
            "abc123",
            qr,
            width,
            CompanyName: "Comercializadora Uno");

        var bytes = new EscPosReceiptRenderer().Render(receipt);
        var printable = Encoding.UTF8.GetString(bytes);

        Assert.Contains("FV100", printable);
        Assert.Contains("COMERCIALIZADORA UNO", printable);
        Assert.Contains("VTA03-00000100", printable);
        Assert.Contains("CUFE: abc123", printable);
        Assert.Contains(qr, printable);
        Assert.Contains("23205.00", printable);
        Assert.Contains("IMPUESTOS POR TARIFA", printable);
        Assert.Contains("IVA 19%", printable);
        Assert.Contains("MEDIOS DE PAGO", printable);
        Assert.Contains("EFECTIVO", printable);
        Assert.Contains("TRANSFERENCIA", printable);
        Assert.Equal(new byte[] { 0x1D, 0x56, 0x41, 0x03 }, bytes.TakeLast(4).ToArray());
    }

    [Fact]
    public void Unsupported_paper_width_is_rejected()
    {
        var receipt = new PosReceipt(
            Guid.NewGuid(),
            new DocumentId(Guid.NewGuid()),
            "FV100",
            "VTA03-00000100",
            DateTimeOffset.UtcNow,
            "222222222",
            [],
            [],
            0m,
            0m,
            0m,
            "cufe",
            "qr",
            76);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new EscPosReceiptRenderer().Render(receipt));
    }

    [Fact]
    public async Task File_printer_writes_the_exact_ESC_POS_receipt_atomically()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "auraly-receipt-" + Guid.NewGuid().ToString("N"));
        var receipt = new PosReceipt(
            Guid.NewGuid(),
            new DocumentId(Guid.NewGuid()),
            "VTA03-00000042",
            "FV42",
            DateTimeOffset.UtcNow,
            "222222222",
            [new PosReceiptLine("P-001", "Producto", 1m, 12_500m, 0m, 0m, 12_500m)],
            [new OfflineSalePayment("Cash", 12_500m)],
            12_500m,
            0m,
            12_500m,
            "cufe",
            "qr",
            80);
        var renderer = new EscPosReceiptRenderer();

        try
        {
            await new FileReceiptPrinter(directory, renderer).PrintAsync(receipt);

            var path = Path.Combine(directory, $"{receipt.PrintJobId:N}.escpos");
            Assert.True(File.Exists(path));
            Assert.Equal(renderer.Render(receipt), await File.ReadAllBytesAsync(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Html_preview_contains_printable_receipt_qr_and_fiscal_snapshot()
    {
        var receipt = Receipt();

        var html = new HtmlReceiptPreviewRenderer().Render(receipt);

        Assert.Contains("<title>Comercializadora Uno VTA01-00000042</title>", html);
        Assert.Contains("Comercializadora Uno", html);
        Assert.Contains("data:image/png;base64,AA==", html);
        Assert.Contains("@page { size: 80mm auto;", html);
        Assert.Contains("window.print()", html);
        Assert.Contains("Producto &amp; prueba", html);
        Assert.DoesNotContain("P-001", html);
        Assert.Contains("CUFE", html);
        Assert.Contains("abc123", html);
        Assert.Contains("<svg", html);
        Assert.Contains("Efectivo", html);
        Assert.Contains("Impuestos por tarifa", html);
        Assert.Contains("IVA 19%", html);
        Assert.Contains("Medios de pago", html);
    }

    [Fact]
    public async Task Html_preview_printer_writes_atomically_and_opens_the_exact_file()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "auraly-preview-" + Guid.NewGuid().ToString("N"));
        var launcher = new RecordingReceiptPreviewLauncher();
        var receipt = Receipt();

        try
        {
            await new HtmlReceiptPreviewPrinter(
                directory,
                new HtmlReceiptPreviewRenderer(),
                launcher).PrintAsync(receipt);

            var expected = Path.GetFullPath(
                Path.Combine(directory, $"{receipt.PrintJobId:N}.html"));
            Assert.Equal(expected, launcher.OpenedPath);
            Assert.True(File.Exists(expected));
            Assert.NotNull(receipt.Cufe);
            Assert.Contains(receipt.Cufe, await File.ReadAllTextAsync(expected));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Commercial_receipt_omits_all_fiscal_artifacts()
    {
        var receipt = new PosReceipt(
            Guid.NewGuid(),
            new DocumentId(Guid.NewGuid()),
            "CVI03-00000042",
            null,
            new DateTimeOffset(2026, 8, 10, 14, 30, 0, TimeSpan.FromHours(-5)),
            "222222222",
            [new PosReceiptLine("P-001", "Producto", 1m, 10_000m, 0m, 1_900m, 11_900m)],
            [new OfflineSalePayment("Cash", 11_900m)],
            10_000m,
            1_900m,
            11_900m,
            null,
            null,
            80,
            PosSaleDocumentTypes.Receipt);

        var esc = Encoding.UTF8.GetString(new EscPosReceiptRenderer().Render(receipt));
        var html = new HtmlReceiptPreviewRenderer().Render(receipt);

        Assert.Contains("COMPROBANTE DE VENTA", esc);
        Assert.Contains("CVI03-00000042", esc);
        Assert.DoesNotContain("NUMERO DIAN", esc);
        Assert.DoesNotContain("CUFE", esc);
        Assert.DoesNotContain("searchqr", esc);
        Assert.Contains("Comprobante de venta", html);
        Assert.Contains("CVI03-00000042", html);
        Assert.DoesNotContain("Número DIAN", html);
        Assert.DoesNotContain("CUFE", html);
        Assert.DoesNotContain("<svg", html);
    }

    [Fact]
    public void Half_letter_renders_two_identical_copies_on_each_letter_sheet()
    {
        var receipt = new OnlineSalesReceipt(
            Guid.NewGuid(),
            PosSaleDocumentTypes.Invoice,
            "VTA01-00000999",
            "FE999",
            DateTimeOffset.UtcNow,
            "222222222",
            [new OnlineSalesReceiptLine(
                "P-001", "Producto", 2m, 10_000m, 0m, 3_800m, 23_800m, "01", 19m)],
            [new OnlineSalesPayment("Cash", 10_000m, null), new OnlineSalesPayment("Transfer", 13_800m, "TRX-1")],
            20_000m,
            3_800m,
            23_800m,
            "cufe-999",
            "https://catalogo-vpfe.dian.gov.co/document/searchqr?documentkey=cufe-999",
            "Accepted",
            "Cliente prueba",
            "Comercializadora Uno",
            "data:image/png;base64,AA==");

        var html = new HalfLetterDocumentRenderer().Render([receipt]);

        Assert.Contains("@page { size: Letter portrait;", html);
        Assert.Contains("CORTE MEDIA CARTA", html);
        Assert.Equal(2, html.Split("VTA01-00000999").Length - 1);
        Assert.Equal(2, html.Split("Cliente prueba").Length - 1);
        Assert.Contains("data:image/svg+xml;base64", html);
        Assert.Contains("Impuestos por tarifa", html);
        Assert.Contains("IVA 19%", html);
        Assert.Contains("Medios de pago", html);
        Assert.Contains("Transferencia", html);
        Assert.Equal(2, html.Split("<h1>Comercializadora Uno</h1>").Length - 1);
        Assert.Equal(2, html.Split("data:image/png;base64,AA==").Length - 1);
    }

    private static PosReceipt Receipt() =>
        new(
            Guid.NewGuid(),
            new DocumentId(Guid.NewGuid()),
            "VTA01-00000042",
            "FE42",
            new DateTimeOffset(2026, 7, 29, 14, 30, 0, TimeSpan.FromHours(-5)),
            "222222222",
            [new PosReceiptLine(
                "P-001",
                "Producto & prueba",
                1m,
                12_500m,
                0m,
                2_375m,
                14_875m,
                "01",
                19m)],
            [new OfflineSalePayment("Cash", 14_875m)],
            12_500m,
            2_375m,
            14_875m,
            "abc123",
            "https://catalogo-vpfe.dian.gov.co/document/searchqr?documentkey=abc123",
            80,
            CompanyName: "Comercializadora Uno",
            CompanyLogoSource: "data:image/png;base64,AA==");

    private sealed class RecordingReceiptPreviewLauncher : IReceiptPreviewLauncher
    {
        public string? OpenedPath { get; private set; }

        public Task OpenAsync(
            string absolutePath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenedPath = absolutePath;
            return Task.CompletedTask;
        }
    }
}
