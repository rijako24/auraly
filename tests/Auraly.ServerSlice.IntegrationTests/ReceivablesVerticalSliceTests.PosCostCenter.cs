using Auraly.Contracts.Authorization;
using Auraly.Contracts.Sales;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class ReceivablesVerticalSliceTests
{
    [Fact]
    public async Task Pos_cost_center_does_not_enable_accounting_and_replay_keeps_the_original_commercial_mode()
    {
        using var client=fixture.CreateAdminClient(CommercePermissionCodes.SalesCreate);
        var center=await ScalarAsync<Guid>("SELECT CostCenterId FROM dbo.AccountingCostCenters WHERE TenantId=@Id AND IsDefault=1 AND IsActive=1",fixture.TenantId);
        var original=await DisableAccountingAsync();
        var restored=false;
        try
        {
            var session=await fixture.OpenWorkSessionAsync(client);
            var draft=await CaptureAsync(client,await OpenDraftAsync(client,session.WorkSessionId));
            var request=new CompleteOnlineSalesDraftRequest(draft.Version,
                [new("Cash",draft.PayableAmount,null)],DocumentType:PosSaleDocumentTypes.Receipt,CostCenterId:center);
            var key="commercial-cost-center-"+Guid.NewGuid().ToString("N");
            var completed=await CompleteAsync(client,draft,request,key);
            await AssertCommercialOnlyProcessingAsync(completed.Receipt.DocumentId);
            Assert.Equal(center,await ScalarAsync<Guid>("SELECT CostCenterId FROM dbo.SalesDocuments WHERE DocumentId=@Id",completed.Receipt.DocumentId));
            await RestoreAccountingAsync(original);restored=true;
            var replay=await CompleteAsync(client,draft,request,key);
            Assert.Equal(completed.Receipt.DocumentId,replay.Receipt.DocumentId);
            await AssertCommercialOnlyProcessingAsync(completed.Receipt.DocumentId);
        }
        finally { if(!restored) await RestoreAccountingAsync(original); }
    }
}
