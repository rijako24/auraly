using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Purchasing;
using Auraly.Contracts.Purchasing;
using Auraly.Domain.Purchasing;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class GoodsReceiptWorkspaceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_cash_cost_document_replays_and_new_cash_receipts_record_payables(bool mainCreatesPayable)
    {
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.ReadGoodsReceipts,
            PurchasingPermissionCodes.CreateGoodsReceipts,PurchasingPermissionCodes.ConfirmGoodsReceipts);
        var draft = CreateDraft();
        var cost = new GoodsReceiptCostDocumentRequest(Guid.NewGuid(),fixture.SupplierId,
            PurchaseEvidenceTypes.SupplierElectronicInvoice,$"LEGACY-COST-{Guid.NewGuid():N}",
            draft.SupplierInvoiceDate!.Value,true,draft.DueDate,"COP",1m,
            DateOnly.FromDateTime(draft.SupplierInvoiceDate.Value.Date),"FunctionalCurrency",
            [new(1,PurchaseCostKinds.Freight,"Flete histórico",5m,0m,"00",0m,0m,
                PurchasingTaxTreatments.NotApplicable,PurchaseCostTreatments.Capitalize,PurchaseCostAllocationMethods.Value)]);
        var request = new ConfirmGoodsReceiptRequest(draft.DraftId,fixture.BusinessId,fixture.WarehouseId,
            fixture.SupplierId,draft.SupplierInvoiceNumber,draft.SupplierInvoiceDate,draft.ReceivedAt,
            true,draft.DueDate,"COP",draft.Notes,draft.Lines,AdditionalCostDocuments:[cost]);
        var key = $"legacy-cash-cost-{request.DocumentId:N}";
        var newId = Guid.Empty;
        fixture.PauseDocumentProcessing();
        try
        {
            using (var accepted = await ConfirmAsync(request,key))
                Assert.Equal(HttpStatusCode.Accepted,accepted.StatusCode);
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            GoodsReceiptDocumentPayload source;
            await using (var read = new SqlCommand("SELECT PayloadJson FROM dbo.DocumentProcessingPayloads WHERE DocumentId=@Id AND DocumentType=N'GoodsReceipt'",connection))
            {
                read.Parameters.AddWithValue("@Id",request.DocumentId);
                source = GoodsReceiptContractSerializer.Deserialize((string)(await read.ExecuteScalarAsync())!);
            }
            var legacyCost = cost with { CreatesPayable=false,DueDate=null };
            var legacyRequest = request with { CreatesPayable=mainCreatesPayable,
                DueDate=mainCreatesPayable?request.DueDate:null,AdditionalCostDocuments=[legacyCost] };
            var legacySource = source with { CreatesPayable=mainCreatesPayable,DueDate=legacyRequest.DueDate,
                AdditionalCostDocuments=[source.AdditionalCostDocuments!.Single() with { CreatesPayable=false,DueDate=null }] };
            // Simulate a pending intent accepted by the pre-cutover binary. No
            // inventory, subledger or journal has run. Only this disposable
            // fixture's intent/header data and frozen v1 signature are replaced.
            var json = GoodsReceiptContractSerializer.Serialize(legacySource);
            await using (var seed = new SqlCommand("""
                UPDATE dbo.GoodsReceipts SET CreatesPayable=@Creates,DueDate=@Due,PayloadHash=@RequestHash WHERE GoodsReceiptId=@Id;
                UPDATE purchasing.GoodsReceiptCostDocuments SET CreatesPayable=0,DueDate=NULL WHERE CostDocumentId=@CostId AND GoodsReceiptId=@Id;
                UPDATE dbo.DocumentProcessingPayloads SET PayloadJson=@Payload,PayloadHash=@SourceHash WHERE DocumentId=@Id AND DocumentType=N'GoodsReceipt';
                """,connection))
            {
                seed.Parameters.AddWithValue("@Id",request.DocumentId);
                seed.Parameters.AddWithValue("@CostId",cost.CostDocumentId);
                seed.Parameters.AddWithValue("@Creates",mainCreatesPayable);
                seed.Parameters.AddWithValue("@Due",(object?)legacyRequest.DueDate??DBNull.Value);
                seed.Parameters.AddWithValue("@RequestHash",LegacyReceiptFingerprint(legacyRequest,legacySource));
                seed.Parameters.AddWithValue("@Payload",json);
                seed.Parameters.AddWithValue("@SourceHash",SHA256.HashData(Encoding.UTF8.GetBytes(json)));
                Assert.Equal(3,await seed.ExecuteNonQueryAsync());
            }
            using (var replay = await ConfirmAsync(legacyRequest,key))
            {
                Assert.True(replay.StatusCode==HttpStatusCode.Accepted,await replay.Content.ReadAsStringAsync());
                Assert.True((await replay.Content.ReadFromJsonAsync<GoodsReceiptAcceptance>())!.IdempotentReplay);
            }
            using (var conflict = await ConfirmAsync(legacyRequest with { Notes="Cambio posterior" },key))
                Assert.Equal(HttpStatusCode.Conflict,conflict.StatusCode);
            newId = Guid.NewGuid();
            var newCost = legacyCost with { CostDocumentId=Guid.NewGuid(),DocumentNumber=$"NEW-{Guid.NewGuid():N}" };
            using (var acceptedCash = await ConfirmAsync(legacyRequest with { DocumentId=newId,
                SupplierInvoiceNumber=$"NEW-{newId:N}",AdditionalCostDocuments=[newCost] },$"new-cash-cost-{newId:N}"))
                Assert.Equal(HttpStatusCode.Accepted,acceptedCash.StatusCode);
            await using var count = new SqlCommand("""
                SELECT (SELECT COUNT(*) FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id)
                  +(SELECT COUNT(*) FROM dbo.DocumentProcessingJobs WHERE DocumentId=@Id)
                  +(SELECT COUNT(*) FROM dbo.DocumentProcessingPayloads WHERE DocumentId=@Id);
                """,connection);
            count.Parameters.AddWithValue("@Id",newId);
            Assert.Equal(3,(int)(await count.ExecuteScalarAsync())!);
        }
        finally
        {
            var pending = fixture.DrainDocumentSignals()
                .Where(signal=>signal.DocumentId==request.DocumentId || signal.DocumentId==newId)
                .DistinctBy(signal=>signal.MovementId).ToArray();
            fixture.ResumeDocumentProcessing();
            foreach (var signal in pending) await fixture.DocumentSignals.PublishAsync(signal);
        }

        async Task<HttpResponseMessage> ConfirmAsync(ConfirmGoodsReceiptRequest value,string idempotencyKey)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post,"/api/commerce/v1/goods-receipts/confirm") { Content=JsonContent.Create(value) };
            message.Headers.Add("Idempotency-Key",idempotencyKey);
            return await client.SendAsync(message);
        }
    }

    private static byte[] LegacyReceiptFingerprint(ConfirmGoodsReceiptRequest request,GoodsReceiptDocumentPayload source)
    {
        // Frozen pre-percentage request fingerprint, using immutable evidence.
        // This contract fixture performs no calculation or current catalog read.
        var costs = (source.AdditionalCostDocuments??[]).Select(document=>new CalculatedGoodsReceiptCostDocument(
            request.AdditionalCostDocuments!.Single(value=>value.CostDocumentId==document.CostDocumentId),
            document.NetAmount,document.TaxAmount,document.GrandTotal,document.FunctionalNetAmount,
            document.FunctionalTaxAmount,document.FunctionalGrandTotal,document.Lines)).ToArray();
        var v1 = new {
            request.DocumentId,request.BusinessId,request.WarehouseId,request.SupplierId,
            request.SupplierInvoiceNumber,request.SupplierInvoiceDate,request.ReceivedAt,
            request.CreatesPayable,request.DueDate,Currency="COP",request.Notes,
            request.PurchaseEvidenceType,request.PurchaseOrderId,source.NetAmount,source.TaxAmount,source.GrandTotal,
            Lines=source.Lines.Select(line=>new { line.LineNumber,line.ProductId,line.Description,line.Quantity,
                line.UnitCost,line.DiscountAmount,line.TaxCode,line.TaxRate,
                TaxTreatment=Enum.Parse<PurchaseTaxTreatment>(line.TaxTreatment),line.NetAmount,line.TaxAmount,line.LineTotal }).ToArray(),
            CostCalculation=new GoodsReceiptCostCalculation(source.ExchangeRate,source.ExchangeRateDate!.Value,
                source.ExchangeRateSource,source.FunctionalNetAmount,source.FunctionalTaxAmount,source.FunctionalGrandTotal,source.Lines,costs),
            Withholding=source.Withholding,
            AdditionalWithholdings=(source.AdditionalCostDocuments??[]).OrderBy(value=>value.CostDocumentId)
                .Select(value=>new KeyValuePair<Guid,Auraly.Commerce.Taxation.Contracts.WithholdingCalculationSnapshot>(value.CostDocumentId,value.Withholding)).ToArray()
        };
        return SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(v1));
    }
}
