using Auraly.Application.Returns;
using Auraly.Contracts.Returns;
using Auraly.Pos.Edge.Infrastructure;
using Auraly.Pos.Printing;

namespace Auraly.Foundation.Tests;

public sealed class SalesReturnHistoryTests
{
    [Theory]
    [InlineData(58)]
    [InlineData(80)]
    public void Return_copy_preserves_accepted_lines_charges_and_rounding_in_both_formats(int width)
    {
        var detail = Detail();
        var receipt = SalesReturnReceipt.Create(detail);
        Assert.Equal(detail.TotalAmount, receipt.PayableAmount);
        Assert.Equal(.4m, receipt.PayableRoundingAmount);
        Assert.Equal(2, receipt.Lines.Count);
        Assert.Equal(8m, receipt.Lines[0].Total);
        Assert.Equal(2m, receipt.Lines[0].Discount);
        Assert.Equal(2m, receipt.Lines[1].Total);
        Assert.Empty(receipt.Payments);
        Assert.Null(receipt.CreditAcknowledgement);
        Assert.Null(receipt.Cufe);
        Assert.Null(receipt.CreditNotePrintDetails);
        Assert.Equal(1, receipt.SalesReturnPrintDetails!.TemplateVersion);

        var thermal = new SalesReceiptHtmlRenderer().Render(receipt, width, autoPrint: false);
        var halfLetter = new HalfLetterDocumentRenderer().Render([receipt], "HalfLetter", autoPrint: false);
        foreach (var html in new[] { thermal, halfLetter })
        {
            Assert.Contains("Devoluci", html);
            Assert.Contains("FV-original", html);
            Assert.Contains("Flete", html);
            Assert.Contains("Ajuste al peso", html);
            Assert.Contains("sales-return", html);
            Assert.DoesNotContain("Total a pagar", html);
            Assert.DoesNotContain("Medios de pago", html);
            Assert.DoesNotContain("Nota crédito electrónica", html);
            Assert.Contains("&lt;motivo&gt;", html);
        }
        var local = receipt.ToPosReceipt(Guid.NewGuid(), width);
        Assert.Equal(receipt.SalesReturnPrintDetails, local.ToPrintDocument().SalesReturnPrintDetails);
        Assert.Equal(receipt.PayableRoundingAmount, local.ToPrintDocument().PayableRoundingAmount);
        Assert.Equal(receipt.FiscalStatus, local.ToPrintDocument().FiscalStatus);
        Assert.NotEmpty(new EscPosReceiptRenderer().Render(local));
    }

    [Fact]
    public async Task Reading_history_requires_read_permission_and_performs_no_mutations()
    {
        var store = new ReadStore();
        var service = new SalesReturnQueryService(store);
        var actor = new SalesReturnUserIdentity(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
            new HashSet<string> { SalesReturnPermissionCodes.Create });
        await Assert.ThrowsAsync<SalesReturnForbiddenException>(() => service.GetReturnAsync(actor,Guid.NewGuid()));
        Assert.Equal(0, store.Reads);
        var reader = actor with { Permissions = new HashSet<string> { SalesReturnPermissionCodes.Read } };
        var detail = await service.GetReturnAsync(reader,Guid.NewGuid());
        Assert.NotNull(detail!.Receipt);
        Assert.Equal(1, store.Reads);
        await Assert.ThrowsAsync<SalesReturnValidationException>(() => service.ListReturnsAsync(reader,new(0,25,null,null,null,null)));
    }

    private static SalesReturnDetail Detail() => new(Guid.NewGuid(),"DVT-1",Guid.NewGuid(),"FV-original",
        "Cliente histórico","123",Guid.NewGuid(),"Bodega",DateTimeOffset.UtcNow,"Refund","Cash",
        10m,0m,10.4m,"Accepted","Pending","Return","<motivo>",null,
        [new(1,1,Guid.NewGuid(),"Genérico",1,10,2,"01",0,8,0,8,3,"Sellable")],
        [new(Guid.NewGuid(),Guid.NewGuid(),"Freight","Flete",2,2,0,2,0,"01",0,2,0,Guid.NewGuid(),Guid.NewGuid(),null)],
        "Sede", "Empresa");

    private sealed class ReadStore : ISalesReturnQueryStore
    {
        public int Reads { get; private set; }
        public Task<SalesReturnDetail?> GetReturnAsync(SalesReturnUserIdentity user,Guid id,CancellationToken token)
        { Reads++; return Task.FromResult<SalesReturnDetail?>(Detail()); }
        public Task<SalesReturnPage> ListReturnsAsync(SalesReturnUserIdentity user,SalesReturnQuery query,CancellationToken token) => throw new NotImplementedException();
        public Task<ReturnableSalePage> ListReturnableSalesAsync(SalesReturnUserIdentity user,ReturnableSalesQuery query,CancellationToken token) => throw new NotImplementedException();
        public Task<ReturnableSale?> GetReturnableSaleAsync(SalesReturnUserIdentity user,Guid id,CancellationToken token) => throw new NotImplementedException();
    }
}
