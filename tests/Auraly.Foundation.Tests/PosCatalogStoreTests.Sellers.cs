using Auraly.Contracts.Catalog;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Auraly.Foundation.Tests;

public sealed partial class PosCatalogStoreTests
{
    [Fact]
    public async Task Seller_snapshot_resumes_atomically_and_delta_is_scoped_paged_and_retires_inactive_sellers()
    {
        var path=Path.Combine(Path.GetTempPath(),$"auraly-sellers-{Guid.NewGuid():N}.db");
        try
        {
            var store=new PosCatalogStore($"Data Source={path};Pooling=False");await store.InitializeAsync();
            var first=new PosSeller(Guid.NewGuid(),Guid.NewGuid(),"V001","Vendedora uno","123",true);
            var second=new PosSeller(Guid.NewGuid(),Guid.NewGuid(),"V002","Vendedora dos","456",true);
            await store.BeginSellerBootstrapAsync();
            await store.ApplySellerBootstrapPageAsync(new(7,"next",true,[first]));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>store.SearchSellersAsync(null));
            store=new PosCatalogStore($"Data Source={path};Pooling=False");
            var state=await store.SellerSynchronizationStateAsync();Assert.True(state.Bootstrapping);Assert.Equal("next",state.Next);
            await store.ApplySellerBootstrapPageAsync(new(9,null,false,[second]));
            Assert.Equal(7,(await store.SellerSynchronizationStateAsync()).Cursor);
            var page=await store.SearchSellersAsync("Vendedora",0,1);Assert.Single(page.Items);Assert.True(page.HasMore);
            Assert.Single((await store.SearchSellersAsync("Vendedora",1,1)).Items);
            Assert.Equal(first,await store.GetSellerAsync(first.SellerId));
            await store.ApplySellerChangesAsync(new(7,10,false,[new(8,first.SellerId,first with { Name="Nombre actualizado" }),new(10,second.SellerId,null)]));
            Assert.Equal("Nombre actualizado",(await store.GetSellerAsync(first.SellerId))!.Name);
            Assert.False((await store.GetSellerAsync(second.SellerId))!.IsActive);
            Assert.Single((await store.SearchSellersAsync(null)).Items);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>store.ApplySellerChangesAsync(new(7,10,false,[])));
            Assert.Equal(10,(await store.SellerSynchronizationStateAsync()).Cursor);
            await store.ApplySellerChangesAsync(new(10,14,false,[new(11,first.SellerId,first),new(14,first.SellerId,null)]));
            Assert.False((await store.GetSellerAsync(first.SellerId))!.IsActive);
            Assert.Empty((await store.SearchSellersAsync(null)).Items);
            Assert.Equal(14,(await store.SellerSynchronizationStateAsync()).Cursor);
        }
        finally {SqliteConnection.ClearAllPools();File.Delete(path);}
    }
}
