using Auraly.Application.Catalog;
using Auraly.Application.Expenses;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.Contracts.Sales;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Services;

public sealed class InvoiceChargeHistoryTests
{
    [Theory]
    [InlineData(5000, false)]
    [InlineData(5001, true)]
    public async Task Full_report_has_an_explicit_limit_and_preserves_authoritative_totals(int count, bool exceedsLimit)
    {
        var store = new Mock<IInvoiceChargeStore>(MockBehavior.Strict);
        var actor = new InvoiceChargeActor(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new HashSet<string> { InvoiceChargePermissions.Read });
        var from = new DateOnly(2026, 11, 1); var to = from.AddDays(25);
        var user = Guid.NewGuid(); var customer = Guid.NewGuid();
        var page = new InvoiceChargeHistoryPage([], 1, 5000, count, 15000, 2000)
            { GeneratedExpenseTotal = 17000, TotalDocuments = 1000 };
        store.Setup(value => value.HistoryAsync(actor, from, to, 1, 100, "FVL100",
            It.IsAny<CancellationToken>(), user, customer, true)).ReturnsAsync(page);
        var service = Create(store.Object);
        if (exceedsLimit)
        {
            var error = await Assert.ThrowsAsync<InvoiceChargeValidationException>(() =>
                service.ExportHistoryAsync(actor, from, to, " FVL100 ", user, customer));
            Assert.Contains("5.000", error.Message);
        }
        else
        {
            var result = await service.ExportHistoryAsync(actor, from, to, " FVL100 ", user, customer);
            Assert.Same(page, result);
        }
        store.VerifyAll(); store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Full_report_does_not_read_data_without_permission_or_with_invalid_filters()
    {
        var store = new Mock<IInvoiceChargeStore>(MockBehavior.Strict);
        var actor = new InvoiceChargeActor(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new HashSet<string>());
        var date = new DateOnly(2026, 11, 1); var service = Create(store.Object);
        await Assert.ThrowsAsync<InvoiceChargeForbiddenException>(() =>
            service.ExportHistoryAsync(actor, date, date, null, null, null));
        actor = actor with { Permissions = new HashSet<string> { InvoiceChargePermissions.Read } };
        await Assert.ThrowsAsync<InvoiceChargeValidationException>(() =>
            service.ExportHistoryAsync(actor, date, date.AddDays(31), null, null, null));
        await Assert.ThrowsAsync<InvoiceChargeValidationException>(() =>
            service.ExportHistoryAsync(actor, date, date, null, Guid.Empty, null));
        store.VerifyNoOtherCalls();
    }

    private static InvoiceChargeService Create(IInvoiceChargeStore store) => new(store,
        Mock.Of<IPosSynchronizationOutboxDispatcher>(), Mock.Of<IExpenseStore>(), Mock.Of<ICatalogStore>());
}
