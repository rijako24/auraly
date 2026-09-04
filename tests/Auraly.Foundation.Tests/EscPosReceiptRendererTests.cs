using System.Text;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Sales;
using Auraly.Commerce.Taxation.Contracts;
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
            CompanyName: "Comercializadora Uno",
            BusinessName: "Sede principal",
            WarehouseName: "Bodega de venta");

        var bytes = new EscPosReceiptRenderer().Render(receipt);
        var printable = Encoding.UTF8.GetString(bytes);

        Assert.Contains("FV100", printable);
        Assert.Contains("Comercializadora Uno", printable);
        Assert.Contains("Sede: Sede principal - Bodega", printable);
        Assert.True(ContainsSequence(bytes, [0x1D, 0x21, 0x10]));
        Assert.Contains("VTA03-00000100", printable);
        Assert.Contains("CUFE: abc123", printable);
        Assert.Contains(qr, printable);
        Assert.Contains("$ 23.205", printable);
        Assert.Contains("Impuestos por tarifa", printable);
        Assert.Contains("IVA 19%", printable);
        Assert.Contains("Medios de pago", printable);
        Assert.Contains("Efectivo", printable);
        Assert.Contains("Transferencia", printable);
        Assert.Contains("Factura emitida por Auraly", printable);
        Assert.Equal(new byte[] { 0x1B, 0x40, 0x1B, 0x61, 0x01 }, bytes.Take(5).ToArray());
        Assert.Contains("\u001bE\u0001Total", printable);
        Assert.Contains("www.auralyapp.co", printable);
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
        Assert.Contains("data-auraly-report=\"sales-invoice\"", html);
        Assert.Contains("data-auraly-report-version=\"1\"", html);
        Assert.Contains("font: 12px/1.35", html);
        Assert.Contains("font-size: 12px", html);
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
        Assert.Contains("Resumen e impuestos", html);
        Assert.Contains("IVA 19%", html);
        Assert.Contains("Medios de pago", html);
        Assert.Contains("Factura emitida por Auraly", html);
        Assert.Contains("www.auralyapp.co", html);
        Assert.DoesNotContain("body { text-transform: uppercase", html);
        Assert.Contains("text-align: center", html);
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

        Assert.Contains("Comprobante de venta", esc);
        Assert.Contains("CVI03-00000042", esc);
        Assert.DoesNotContain("Numero DIAN", esc);
        Assert.DoesNotContain("CUFE", esc);
        Assert.DoesNotContain("searchqr", esc);
        Assert.Contains("Comprobante emitido por Auraly", esc);
        Assert.DoesNotContain("Factura emitida por Auraly", esc);
        Assert.Contains("Comprobante de venta", html);
        Assert.Contains("CVI03-00000042", html);
        Assert.Contains("data-auraly-report=\"sales-receipt\"", html);
        Assert.Contains("data-auraly-report-version=\"1\"", html);
        Assert.Contains("font: 11px/1.35", html);
        Assert.Contains("font-size: 12px", html);
        Assert.DoesNotContain("Número DIAN", html);
        Assert.DoesNotContain("CUFE", html);
        Assert.DoesNotContain("<svg", html);
        Assert.Contains("Comprobante emitido por Auraly", html);
        Assert.DoesNotContain("Factura emitida por Auraly", html);
    }

    [Fact]
    public void Half_letter_renders_two_rotated_identical_copies_without_cut_marks()
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
        Assert.Contains("rotate(90deg)", html);
        Assert.DoesNotContain("CORTE MEDIA", html);
        Assert.DoesNotContain("dashed", html);
        Assert.Equal(2, html.Split("VTA01-00000999").Length - 1);
        Assert.Equal(2, html.Split("Cliente prueba").Length - 1);
        Assert.Equal(2, html.Split("data-auraly-report=\"sales-invoice\"").Length - 1);
        Assert.Equal(2, html.Split("data-auraly-report-version=\"1\"").Length - 1);
        Assert.Contains(".platform strong { color: #065f5b; font-size: 1.15em; }", html);
        Assert.Contains("data:image/svg+xml;base64", html);
        Assert.Contains("Impuestos por tarifa", html);
        Assert.Contains("IVA 19%", html);
        Assert.Contains("Medios de pago", html);
        Assert.Contains("Transferencia", html);
        Assert.Equal(2, html.Split("<h1>Comercializadora Uno</h1>").Length - 1);
        Assert.Equal(2, html.Split("data:image/png;base64,AA==").Length - 1);
        Assert.Equal(2, html.Split("Factura emitida por Auraly").Length - 1);
        Assert.Equal(2, html.Split("www.auralyapp.co").Length - 1);
        Assert.Equal(2, html.Split("Página 1 de 1").Length - 1);
        Assert.DoesNotContain("text-transform: uppercase", html);
        Assert.Contains("text-align: right", html);
    }

    [Fact]
    public void Receipt_discriminates_withholdings_and_net_amount_to_pay()
    {
        var receipt = new PosReceipt(
            Guid.NewGuid(), new DocumentId(Guid.NewGuid()), "TCK-42", null,
            DateTimeOffset.UtcNow, "900123456",
            [new PosReceiptLine("P-1", "Producto", 1m, 100_000m, 0m, 19_000m, 119_000m)],
            [new OfflineSalePayment("Cash", 116_500m)],
            100_000m, 19_000m, 119_000m, null, null, 80,
            PosSaleDocumentTypes.Receipt,
            WithholdingTotal: 2_500m,
            NetPayableAmount: 116_500m,
            Withholdings:
            [
                new WithholdingLineSnapshot(
                    Guid.NewGuid(), 1, "RETEFUENTE", "Retefuente", "IncomeTax",
                    "TaxExclusive", 100_000m, 2.5m, 2_500m, null)
            ]);

        var esc = Encoding.UTF8.GetString(new EscPosReceiptRenderer().Render(receipt));
        var html = new HtmlReceiptPreviewRenderer().Render(receipt);

        Assert.Contains("Total bruto", esc);
        Assert.Contains("Ret. Retefuente", esc);
        Assert.Contains("-$ 2.500", esc);
        Assert.Contains("Total", esc);
        Assert.Contains("$ 116.500", esc);
        Assert.Contains("Total bruto", html);
        Assert.Contains("Ret. Retefuente", html);
        Assert.Contains("Total retenciones", html);
        Assert.Contains("Total", html);
    }

    [Theory]
    [InlineData(HalfLetterDocumentRenderer.HalfLetter)]
    [InlineData(HalfLetterDocumentRenderer.HalfLegal)]
    [InlineData(HalfLetterDocumentRenderer.Letter)]
    public void Every_sheet_format_discriminates_withholdings_and_net_amount(string format)
    {
        var receipt = OnlineReceipt() with
        {
            Payments = [new OnlineSalesPayment("Cash", 21_300m, null)],
            WithholdingTotal = 2_500m,
            NetPayableAmount = 21_300m,
            Withholdings =
            [
                new WithholdingLineSnapshot(
                    Guid.NewGuid(), 1, "RETEFUENTE", "Retefuente", "IncomeTax",
                    "TaxExclusiveAmount", 20_000m, 12.5m, 2_500m, null)
            ]
        };

        var html = new HalfLetterDocumentRenderer().Render([receipt], format);

        Assert.Contains("Total bruto", html);
        Assert.Contains("Ret. Retefuente", html);
        Assert.Contains("Total retenciones", html);
        Assert.Contains("Total a pagar", html);
        Assert.Contains("21.300", html);
    }

    [Fact]
    public void Half_legal_uses_legal_paper_and_rotates_every_complete_copy()
    {
        var receipt = OnlineReceipt();

        var html = new HalfLetterDocumentRenderer().Render(
            [receipt], HalfLetterDocumentRenderer.HalfLegal);

        Assert.Contains("@page { size: 215.9mm 330.2mm;", html);
        Assert.Contains("class=\"sheet half half-oficio\"", html);
        Assert.Contains("rotate(90deg)", html);
        Assert.DoesNotContain("CORTE", html);
        Assert.Equal(2, html.Split(receipt.DocumentNumber).Length - 1);
        Assert.Equal(2, html.Split("Impuestos por tarifa").Length - 1);
        Assert.Equal(2, html.Split("Medios de pago").Length - 1);
    }

    [Fact]
    public void Letter_uses_one_full_page_copy_without_rotation()
    {
        var receipt = OnlineReceipt();

        var html = new HalfLetterDocumentRenderer().Render(
            [receipt], HalfLetterDocumentRenderer.Letter);

        Assert.Contains("@page { size: Letter portrait;", html);
        Assert.Contains("class=\"sheet letter\"", html);
        Assert.Equal(1, html.Split(receipt.DocumentNumber).Length - 1);
        Assert.Equal(1, html.Split("Factura emitida por Auraly").Length - 1);
    }

    [Fact]
    public void Commercial_sheet_keeps_the_layout_without_fiscal_artifacts()
    {
        var receipt = OnlineReceipt() with
        {
            DocumentType = PosSaleDocumentTypes.Receipt
        };

        var html = new HalfLetterDocumentRenderer().Render(
            [receipt], HalfLetterDocumentRenderer.Letter);

        Assert.Contains("Comprobante de venta", html);
        Assert.Contains("data-auraly-report=\"sales-receipt\"", html);
        Assert.Contains("data-auraly-report-version=\"1\"", html);
        Assert.Contains("Representación gráfica del comprobante de venta", html);
        Assert.Contains("Comprobante emitido por Auraly", html);
        Assert.DoesNotContain("Factura electrónica de venta", html);
        Assert.DoesNotContain("Número DIAN", html);
        Assert.DoesNotContain("CUFE", html);
        Assert.DoesNotContain("QR DIAN", html);
        Assert.Contains("Impuestos por tarifa", html);
        Assert.Contains("Medios de pago", html);
        Assert.Contains("Página 1 de 1", html);
    }

    [Fact]
    public void Sheet_formats_group_all_tax_rates_and_render_every_payment()
    {
        var receipt = OnlineReceipt() with
        {
            Lines =
            [
                new OnlineSalesReceiptLine(
                    "P-001", "Sin impuesto", 1m, 10_000m, 0m, 0m, 10_000m, "01", 0m),
                new OnlineSalesReceiptLine(
                    "P-002", "Con IVA", 1m, 10_000m, 0m, 1_900m, 11_900m, "01", 19m),
                new OnlineSalesReceiptLine(
                    "P-003", "Mismo IVA", 1m, 5_000m, 0m, 950m, 5_950m, "01", 19m)
            ],
            Payments =
            [
                new OnlineSalesPayment("Cash", 12_000m, null),
                new OnlineSalesPayment("Transfer", 15_850m, "TRX-1")
            ],
            UntaxedAmount = 25_000m,
            TaxAmount = 2_850m,
            PayableAmount = 27_850m
        };

        var html = new HalfLetterDocumentRenderer().Render(
            [receipt], HalfLetterDocumentRenderer.Letter);

        Assert.Equal(1, html.Split("IVA 0%").Length - 1);
        Assert.Equal(1, html.Split("IVA 19%").Length - 1);
        Assert.Contains("base", html);
        Assert.Contains("15.000", html);
        Assert.Contains("Efectivo", html);
        Assert.Contains("Transferencia", html);
    }

    private static OnlineSalesReceipt OnlineReceipt() =>
        new(
            Guid.NewGuid(),
            PosSaleDocumentTypes.Invoice,
            "VTA01-00000999",
            "FE999",
            new DateTimeOffset(2026, 8, 27, 15, 47, 24, TimeSpan.FromHours(-5)),
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

    private static bool ContainsSequence(byte[] source, byte[] expected)
    {
        for (var index = 0; index <= source.Length - expected.Length; index++)
        {
            if (source.AsSpan(index, expected.Length).SequenceEqual(expected))
                return true;
        }
        return false;
    }

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
