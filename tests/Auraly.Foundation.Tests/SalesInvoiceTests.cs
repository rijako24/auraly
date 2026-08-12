using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Domain.Sales;

namespace Auraly.Foundation.Tests;

public sealed class SalesInvoiceTests
{
    [Fact]
    public void Confirmation_freezes_matching_fiscal_snapshot_and_prevents_line_changes()
    {
        var invoice = CreateInvoice();
        invoice.AddLine(new SalesInvoiceLine(
            new ProductId(Guid.NewGuid()),
            "Producto",
            2m,
            10_000m,
            1_000m,
            3_610m));
        var snapshot = new ImmutableFiscalSnapshot(
            "FV011",
            "FV01",
            1,
            "18760000001",
            DateTimeOffset.UtcNow,
            "222222222",
            19_000m,
            3_610m,
            22_610m,
            new string('a', 96),
            "QR");

        var documentNumber = DocumentNumber();
        invoice.ConfirmOffline(documentNumber, snapshot);

        Assert.Equal(SalesInvoiceStatus.LocallyIssuedPendingSync, invoice.Status);
        Assert.Same(documentNumber, invoice.DocumentNumber);
        Assert.Same(snapshot, invoice.FiscalSnapshot);
        Assert.Throws<InvalidOperationException>(
            () => invoice.AddLine(new SalesInvoiceLine(
                new ProductId(Guid.NewGuid()),
                "Otro",
                1m,
                1m,
                0m,
                0m)));
    }

    [Fact]
    public void Confirmation_rejects_a_snapshot_with_changed_totals()
    {
        var invoice = CreateInvoice();
        invoice.AddLine(new SalesInvoiceLine(
            new ProductId(Guid.NewGuid()),
            "Producto",
            1m,
            10_000m,
            0m,
            1_900m));
        var changed = new ImmutableFiscalSnapshot(
            "FV011",
            "FV01",
            1,
            "18760000001",
            DateTimeOffset.UtcNow,
            "222222222",
            10_001m,
            1_900m,
            11_901m,
            new string('a', 96),
            "QR");

        Assert.Throws<InvalidOperationException>(() => invoice.ConfirmOffline(DocumentNumber(), changed));
        Assert.Equal(SalesInvoiceStatus.Draft, invoice.Status);
    }

    private static SalesInvoice CreateInvoice() =>
        new(
            new DocumentId(Guid.NewGuid()),
            new TenantId(Guid.NewGuid()),
            new BusinessId(Guid.NewGuid()),
            new WarehouseId(Guid.NewGuid()),
            new UserId(Guid.NewGuid()),
            new DeviceId(Guid.NewGuid()),
            new WorkSessionId(Guid.NewGuid()));

    private static AuralyDocumentNumberAssignment DocumentNumber() =>
        AuralyDocumentNumberAssignment.Create(
            Guid.NewGuid(),
            AuralyDocumentTypes.SalesInvoice,
            "VTA",
            "03",
            1,
            8);
}
