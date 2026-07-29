using System.Text;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Foundation.Tests;

public sealed class EscPosReceiptRendererTests
{
    [Theory]
    [InlineData(58)]
    [InlineData(80)]
    public void Receipt_contains_Auraly_and_fiscal_numbers_cufe_exact_qr_and_cut_command(int width)
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
            [new PosReceiptLine("P-001", "Café molido", 2m, 10_000m, 500m, 3_705m, 23_205m)],
            [new OfflineSalePayment("Cash", 23_205m)],
            19_500m,
            3_705m,
            23_205m,
            "abc123",
            qr,
            width);

        var bytes = new EscPosReceiptRenderer().Render(receipt);
        var printable = Encoding.UTF8.GetString(bytes);

        Assert.Contains("FV100", printable);
        Assert.Contains("VTA03-00000100", printable);
        Assert.Contains("CUFE: abc123", printable);
        Assert.Contains(qr, printable);
        Assert.Contains("23205.00", printable);
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
}
