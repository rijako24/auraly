using Auraly.Application.Catalog;
using Auraly.Contracts.Catalog;

namespace Auraly.Foundation.Tests;

public sealed class PaymentMethodServiceTests
{
    [Theory]
    [InlineData("VoucherReceipts")]
    [InlineData("VoucherPayments")]
    [InlineData("Receipts")]
    [InlineData("SupplierPayments")]
    public async Task Voucher_forms_use_accounting_read_access_without_granting_access_to_supplier_configuration(string usage)
    {
        var store = new Store();
        var service = new PaymentMethodService(store);
        await service.ListAsync(User("accounting.read"),new(Usage:usage));
        Assert.Equal(1,store.Calls);
        await Assert.ThrowsAsync<CatalogForbiddenException>(() => service.ListAsync(User("sales.create"),new(Usage:usage)));
        Assert.Equal(1,store.Calls);
    }

    [Fact]
    public async Task A_sales_operator_cannot_read_supplier_payment_configuration()
    {
        var store = new Store();
        await Assert.ThrowsAsync<CatalogForbiddenException>(() => new PaymentMethodService(store)
            .ListAsync(User("sales.create"),new(Usage:"SupplierPayments")));
        Assert.Equal(0,store.Calls);
    }

    [Theory]
    [InlineData(0,50)]
    [InlineData(1,101)]
    [InlineData(int.MaxValue,100)]
    public async Task Invalid_or_overflowing_pages_are_rejected_before_reading(int page,int size)
    {
        var store = new Store();
        await Assert.ThrowsAsync<CatalogValidationException>(() => new PaymentMethodService(store)
            .ListAsync(User(CatalogPermissionCodes.Read),new(page,size)));
        Assert.Equal(0,store.Calls);
    }

    [Fact]
    public async Task Save_preserves_tenant_and_normalizes_the_form_in_the_catalog_owner()
    {
        var store = new Store();
        var user = User(CatalogPermissionCodes.Update);
        var request = new SavePaymentMethodRequest(Guid.NewGuid()," BANK "," Banco ",Guid.NewGuid(),
            Guid.NewGuid(),true,true,true,true,true,10);
        await new PaymentMethodService(store).SaveAsync(user,request);
        Assert.Equal(user.TenantId,store.User?.TenantId);
        Assert.Equal("BANK",store.Saved?.Code);
        Assert.Equal("Banco",store.Saved?.Name);
    }

    private static CatalogUserIdentity User(string permission) => new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
        new HashSet<string>{permission});

    private sealed class Store : IPaymentMethodStore
    {
        public Task<PaymentMethodCatalogSnapshot> GetSnapshotAsync(Guid tenantId,CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public int Calls {get;private set;}
        public CatalogUserIdentity? User {get;private set;}
        public SavePaymentMethodRequest? Saved {get;private set;}
        public Task<PaymentMethodPage> ListAsync(CatalogUserIdentity user,PaymentMethodQuery query,CancellationToken cancellationToken)
        { Calls++;return Task.FromResult(new PaymentMethodPage([],query.Page,query.PageSize,0)); }
        public Task<PaymentMethodView> SaveAsync(CatalogUserIdentity user,SavePaymentMethodRequest request,CancellationToken cancellationToken)
        {
            Calls++;User=user;Saved=request;
            return Task.FromResult(new PaymentMethodView(request.PaymentMethodId,request.Code,request.Name,
                request.PaymentTypeId,"Transfer","Transferencia",request.AccountId,"1","Banco",null,
                request.UseForSales,request.UseForReceipts,request.UseForSupplierPayments,request.UseForPayroll,
                request.IsActive,request.SortOrder,1,new byte[8]));
        }
    }
}
