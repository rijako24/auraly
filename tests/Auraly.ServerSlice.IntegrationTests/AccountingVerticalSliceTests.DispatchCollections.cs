using System.Net.Http.Json;
using Auraly.Api;
using Auraly.Application.Dispatching;
using Auraly.Application.Returns;
using Auraly.Contracts.Returns;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Dispatching;
using Auraly.Contracts.Orders;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class AccountingVerticalSliceTests
{
    [Fact]
    public Task Dispatch_settlement_resumes_an_expired_claim_after_restart_without_another_signal()
        => Order_dispatch_collection_uses_one_session_and_reclassifies_only_changed_accounts(
            "SalesReceipt","Cash",true,false,restartDuringLease:true);
    [Fact]
    public Task Dispatch_uses_original_posted_account_when_the_same_native_form_changes_its_account()
        =>Order_dispatch_collection_uses_one_session_and_reclassifies_only_changed_accounts(
            "SalesReceipt","Cash",true,false,remapFormBeforeCollection:true);

    [Theory]
    [InlineData("SalesReceipt","Partial",0)]
    [InlineData("SalesReceipt","NotDelivered",0)]
    [InlineData("SalesInvoice","Partial",4000)]
    public Task Previously_collected_delivery_links_an_administrative_refund(string documentType,string returnCase,decimal credit)
        =>Order_dispatch_collection_uses_one_session_and_reclassifies_only_changed_accounts(
            documentType,"Cash",true,false,returnCase,credit,previouslyCollected:true);

    [Theory]
    [InlineData("SalesReceipt", "Cash", true, false)]
    [InlineData("SalesReceipt", "Cash", true, false, "None", 0, false, -1000)]
    [InlineData("SalesReceipt", "Cash", true, false, "None", 0, false, 1000)]
    [InlineData("SalesReceipt", "Cash", false, false, "None", 0, false, -1000)]
    [InlineData("SalesReceipt", "Cash", false, false)]
    [InlineData("SalesReceipt", "DebitCard", false, false)]
    [InlineData("SalesReceipt", "CreditCard", false, false)]
    [InlineData("SalesReceipt", "Transfer", false, true)]
    [InlineData("SalesInvoice", "Transfer", false, false)]
    [InlineData("SalesReceipt", "Transfer", false, false, "Partial")]
    [InlineData("SalesReceipt", "Cash", true, false, "NotDelivered")]
    [InlineData("SalesInvoice", "Transfer", false, false, "Partial")]
    [InlineData("SalesInvoice", "Transfer", false, false, "None", 4000)]
    [InlineData("SalesInvoice", "Transfer", false, false, "Partial", 4000)]
    [InlineData("SalesInvoice", "Transfer", false, false, "Partial", 8000)]
    [InlineData("SalesInvoice", "Transfer", false, false, "None", 11900)]
    [InlineData("SalesInvoice", "Transfer", false, false, "Partial", 11900)]
    [InlineData("SalesReceipt", "DebitCard", false, false, "None", 0, true)]
    [InlineData("SalesReceipt", "CreditCard", false, false, "None", 0, true)]
    [InlineData("SalesInvoice", "DebitCard", false, false, "None", 0, true)]
    [InlineData("SalesInvoice", "Cash", true, false, "None", 0, false, 0, true)]
    [InlineData("SalesInvoice", "Cash", true, false, "None", 4000, false, 0, true)]
    [InlineData("SalesReceipt", "Cash", false, false, "None", 0, false, -1000, false, true)]
    [InlineData("SalesReceipt", "Transfer", false, false, "None", 0, false, 1000)]
    [InlineData("SalesReceipt", "Cash", false, false, "None", 0, false, 0, false, false, "PendingCollection")]
    [InlineData("SalesReceipt", "DebitCard", false, false, "None", 0, false, 0, false, false, "PendingCollection")]
    [InlineData("SalesReceipt", "CreditCard", false, false, "None", 0, false, 0, false, false, "PendingCollection")]
    [InlineData("SalesReceipt", "Transfer", false, false, "None", 0, false, 0, false, false, "PendingCollection")]
    [InlineData("SalesReceipt", "Transfer", false, false, "Partial", 0, false, 0, false, false, "PendingCollection")]
    [InlineData("SalesReceipt", "Cash", false, false, "NotDelivered", 0, false, 0, false, false, "PendingCollection")]
    public async Task Order_dispatch_collection_uses_one_session_and_reclassifies_only_changed_accounts(
        string documentType, string method, bool sameForm, bool remap, string returnCase = "None", decimal creditPortion = 0, bool refundAfterCollection = false, decimal cashDifference = 0, bool previouslyCollected = false, bool splitCash = false, string originalMethod = "Cash", bool remapFormBeforeCollection = false, bool restartDuringLease = false)
    {
        using var admin = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate, CatalogPermissionCodes.Update);
        using (var defaults = await admin.PutAsync("/api/commerce/v1/accounting/defaults", null)) defaults.EnsureSuccessStatusCode();
        using (var activation = await admin.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026, 1, 1), "COP", "ZeroDeclared"))) activation.EnsureSuccessStatusCode();
        var original = await CreateClosureFormAsync(admin, originalMethod);
        if(originalMethod=="PendingCollection")
        {
            foreach(var usage in new[]{"Sales","Receipts","SupplierPayments","Payroll","VoucherReceipts","VoucherPayments"})
            {
                var hidden=(await admin.GetFromJsonAsync<PaymentMethodPage>($"/api/commerce/v1/payment-methods/?usage={usage}&paymentMethodId={original.PaymentMethodId}&page=1&pageSize=10"))!;
                Assert.Empty(hidden.Items);
            }
            var usable=(await admin.GetFromJsonAsync<PaymentMethodPage>($"/api/commerce/v1/payment-methods/?usage=OrderReceipts&paymentMethodId={original.PaymentMethodId}&page=1&pageSize=10"))!;
            Assert.Equal(original.PaymentMethodId,Assert.Single(usable.Items).PaymentMethodId);
            var save=new SavePaymentMethodRequest(original.PaymentMethodId,original.Code,original.Name,original.PaymentTypeId,original.AccountId,true,false,false,false,true,10,original.RowVersion);
            foreach(var invalid in new[]{save with{UseForReceipts=true},save with{UseForSupplierPayments=true},save with{UseForPayroll=true},save with{UseForVouchers=true}})
            {
                using var rejected=await admin.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{original.PaymentMethodId}",invalid);
                Assert.Equal(System.Net.HttpStatusCode.BadRequest,rejected.StatusCode);
            }
            var direct=WithUblSnapshot(fixture.CreateUniqueValidRequest()) with{Payments=[new(1,"PendingCollection",11900,null,
                PaymentMethodId:original.PaymentMethodId,PaymentMethodVersion:original.Version,PaymentMethodCode:original.Code,PaymentMethodName:original.Name,AccountId:original.AccountId)]};
            using var directClient=fixture.CreateClient();using var directUpload=fixture.CreateUploadMessage(direct);
            using var forbiddenDirect=await directClient.SendAsync(directUpload);
            Assert.Equal(System.Net.HttpStatusCode.BadRequest,forbiddenDirect.StatusCode);
            Assert.Equal(0,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SalesDocuments WHERE DocumentId=@Id",direct.DocumentId));
        }
        var target = sameForm ? original : await CreateClosureFormAsync(admin, method);
        var differenceCash=splitCash||cashDifference!=0&&method!="Cash"?await CreateClosureFormAsync(admin,"Cash"):null;
        var issuer = Guid.NewGuid(); var issuerSession = Guid.NewGuid(); var order = Guid.NewGuid(); var secondOrder = Guid.NewGuid();
        await fixture.SeedConfirmedOrdersForInvoiceAsync(issuer, issuerSession, order, secondOrder);
        using var cashier = fixture.CreateUserClient(issuer, "sales.create", "orders.read", "orders.recover", "orders.invoice");
        using var invoiceRequest = new HttpRequestMessage(HttpMethod.Post, "/api/commerce/v1/orders/invoice") {
            Content = JsonContent.Create(new InvoiceOrdersRequest(issuerSession, fixture.WarehouseId, issuer, creditPortion>0||previouslyCollected?[secondOrder]:[order, secondOrder], originalMethod, null,
                DocumentType: documentType, PaymentMethodId: original.PaymentMethodId, PaymentMethodVersion: original.Version,
                PaymentFormCode: original.Code, PaymentFormName: original.Name, AccountId: original.AccountId)) };
        invoiceRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var invoiceResponse = await cashier.SendAsync(invoiceRequest);
        Assert.True(invoiceResponse.IsSuccessStatusCode, await invoiceResponse.Content.ReadAsStringAsync());
        var results = (await invoiceResponse.Content.ReadFromJsonAsync<InvoiceOrdersResponse>())!.Results;
        var otherEmission = Assert.Single(results, item => item.OrderId == secondOrder);
        Assert.True(otherEmission.Status == "Invoiced", System.Text.Json.JsonSerializer.Serialize(otherEmission));
        await AssertBalancedAsync(otherEmission.DocumentId!.Value);
        Guid sale;
        if(creditPortion>0||previouslyCollected)
        {
            var(customer,site)=await CreateCustomerAsync();
            var upload=WithUblSnapshot(fixture.CreateUniqueValidRequest() with{
                SourceOrderId=previouslyCollected?null:order,CollectionMode=previouslyCollected?SaleCollectionMode.Session:SaleCollectionMode.Dispatch,CustomerId=customer,CustomerPartySiteId=site,
                Credit=creditPortion==0?null:new PosSaleCreditContract(customer,creditPortion,new DateTimeOffset(2026,8,31,0,0,0,TimeSpan.FromHours(-5)),PartySiteId:site),
                Payments=creditPortion==11900?[]:[new PosSalePaymentContract(1,"Cash",11900-creditPortion,null,PaymentMethodId:original.PaymentMethodId,
                    PaymentMethodVersion:original.Version,PaymentMethodCode:original.Code,PaymentMethodName:original.Name,AccountId:original.AccountId)]});
            var uploadConsecutive=upload.DocumentNumber.Consecutive;
            if(previouslyCollected&&documentType==PosSaleDocumentTypes.Receipt)
            {
                var series=Guid.NewGuid();var code=$"RR{uploadConsecutive}";
                await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
                await using var seed=new SqlCommand("""
                    INSERT dbo.DocumentSeries(DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
                    VALUES(@Id,@Business,@Device,N'SalesReceipt',N'CVI',@Code,8,1,99999999,1,1,SYSDATETIMEOFFSET());
                    """,connection);
                seed.Parameters.AddWithValue("@Id",series);seed.Parameters.AddWithValue("@Business",fixture.BusinessId);
                seed.Parameters.AddWithValue("@Device",fixture.DeviceId);seed.Parameters.AddWithValue("@Code",code);await seed.ExecuteNonQueryAsync();
                upload=upload with{DocumentNumber=new(series,PosSaleDocumentTypes.Receipt,"CVI",code,uploadConsecutive,8,$"CVI{code}-{uploadConsecutive:D8}"),
                    CommercialSnapshot=upload.CommercialSnapshot with{DocumentType=PosSaleDocumentTypes.Receipt},FiscalSnapshot=null,UblSnapshot=null};
            }
            if(creditPortion>0) upload=upload with{UblSnapshot=upload.UblSnapshot! with{PaymentFormCode="2",DueDate=new(2026,8,31)}};
            using var pos=fixture.CreateClient();using var message=fixture.CreateUploadMessage(upload);
            using var response=await pos.SendAsync(message);
            Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
            sale=upload.DocumentId;
        }
        else
        {
            var emitted=Assert.Single(results,item=>item.OrderId==order);
            Assert.Equal("Invoiced",emitted.Status);sale=emitted.DocumentId!.Value;
        }
        var gross=creditPortion>0||previouslyCollected?11900m:10000m;
        await AssertBalancedAsync(sale);
        var originalHash = await ScalarAsync<byte[]>("SELECT PayloadHash FROM dbo.SalesDocuments WHERE DocumentId=@Id", sale);
        // Creation checks the persisted role, independently of HTTP claims.
        await using (var authorizationConnection = new SqlConnection(fixture.ConnectionString))
        {
            await authorizationConnection.OpenAsync();
            await using var authorize = new SqlCommand("""
                DECLARE @Role uniqueidentifier=NEWID(),@Name nvarchar(128)=CONVERT(nvarchar(36),NEWID());
                INSERT dbo.AppRoles(RoleId,TenantId,Name,NormalizedName) VALUES(@Role,@Tenant,@Name,@Name);
                INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId)
                  SELECT NEWID(),@Role,PermissionId FROM dbo.Permissions WHERE Resource=N'dispatches.delivery.execute';
                IF @@ROWCOUNT<>1 THROW 51000,N'Falta el permiso nativo de entrega para la prueba.',1;
                INSERT dbo.UserRoles(UserRoleId,UserId,RoleId,BusinessId) VALUES(NEWID(),@User,@Role,@Business);
                """, authorizationConnection);
            authorize.Parameters.AddWithValue("@Tenant", fixture.TenantId); authorize.Parameters.AddWithValue("@Business", fixture.BusinessId);
            authorize.Parameters.AddWithValue("@User", fixture.UserId); await authorize.ExecuteNonQueryAsync();
        }
        using var scope = fixture.CreateScope();
        var logistics = scope.ServiceProvider.GetRequiredService<DispatchService>();
        var deliveries = scope.ServiceProvider.GetRequiredService<DispatchDeliveryService>();
        var actor = new DispatchActorIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string>{DispatchPermissionCodes.Create,DispatchPermissionCodes.Read,DispatchPermissionCodes.ReadAll,
                DispatchPermissionCodes.Prepare,DispatchPermissionCodes.Release,DispatchPermissionCodes.ExecuteDeliveries,DispatchPermissionCodes.Settle}, true);
        var draft = await logistics.CreateAsync(actor, new(fixture.BusinessId, new(2026,11,18), "Transportador de prueba", null,
            fixture.UserId, null, null, [sale]), default);
        var otherDraft = await logistics.CreateAsync(actor, new(fixture.BusinessId, new(2026,11,18), "Transportador de prueba", null,
            fixture.UserId, null, null, [otherEmission.DocumentId.Value]), default);
        Assert.NotEqual(draft.DispatchNumber, otherDraft.DispatchNumber);
        var prepared = await logistics.PrepareAsync(actor, draft.DispatchId, new(draft.RowVersion, Guid.NewGuid().ToString("N")), default);
        await logistics.ReleaseAsync(actor, draft.DispatchId, new(prepared.RowVersion, Guid.NewGuid().ToString("N")), default);
        var before = await deliveries.GetAsync(actor, draft.DispatchId, default);
        var document = Assert.Single(before.Documents);
        Assert.Equal(previouslyCollected?"Session":"Dispatch",document.CollectionMode);
        if(previouslyCollected)
            await Assert.ThrowsAsync<DispatchValidationException>(()=>deliveries.RecordAsync(actor,draft.DispatchId,
                new(document.DispatchSourceDocumentId,"Delivered",null,null,null,null,DateTimeOffset.UtcNow,
                    Guid.NewGuid().ToString("N"),[new("InvoicePayment","Cash",gross-creditPortion,null,null,original.PaymentMethodId,original.Version)],[]),default));
        if(remapFormBeforeCollection)
        {
            var replacement=await CreateClosureFormAsync(admin,"Cash");
            var fresh=Assert.Single((await admin.GetFromJsonAsync<PaymentMethodPage>(
                $"/api/commerce/v1/payment-methods/?usage=OrderReceipts&paymentMethodId={target.PaymentMethodId}&page=1&pageSize=10"))!.Items);
            using var changed=await admin.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{fresh.PaymentMethodId}",
                new SavePaymentMethodRequest(fresh.PaymentMethodId,fresh.Code,fresh.Name,fresh.PaymentTypeId,
                    replacement.AccountId,true,true,false,false,true,10,fresh.RowVersion));
            Assert.True(changed.IsSuccessStatusCode,await changed.Content.ReadAsStringAsync());
            target=(await changed.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method;
        }
        var sameAccount=original.AccountId==target.AccountId;
        var card = method is "DebitCard" or "CreditCard";
        var franchise = card ? (await admin.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/card-franchise"))!.First().Code : null;
        var notDelivered = returnCase == "NotDelivered";
        var hasReturn = returnCase != "None";
        var returnedAmount=hasReturn?notDelivered?gross:gross/2:0m;
        var collected=notDelivered||previouslyCollected?0m:decimal.Min(gross-creditPortion,gross-returnedAmount);
        var reversedCredit=previouslyCollected&&hasReturn?0:decimal.Min(creditPortion,returnedAmount);
        var reversedTender=returnedAmount-reversedCredit;
        var capture=new List<DispatchDeliveryPaymentInput>();
        var deliveryReturnReason=returnCase=="Partial"?(await deliveries.ReasonsAsync(actor,"DeliveryReturn",default)).First():null;
        if(!notDelivered)
        {
            if(collected>0)capture.Add(new("InvoicePayment",method,splitCash?collected/2:collected,method=="Transfer"?"TRF-CONFIRMED":null,null,
                target.PaymentMethodId,target.Version,franchise,card?"APPROVED":null));
            if(splitCash)capture.Add(new("InvoicePayment","Cash",collected/2,null,null,differenceCash!.PaymentMethodId,differenceCash.Version));
            if(creditPortion>0)capture.Add(new("CreditDocument",null,0,null,"https://qa.invalid/factura-firmada.png"));
        }
        var deliveryRequest = new RecordDispatchDeliveryRequest(document.DispatchSourceDocumentId, notDelivered ? "NotDelivered" : hasReturn ? "PartiallyDelivered" : "Delivered", notDelivered ? "Cliente ausente" : hasReturn ? "Devolución parcial" : null, null,
            null, null, DateTimeOffset.UtcNow, Guid.NewGuid().ToString("N"),
            capture,
            returnCase == "Partial" ? [new(1,.5m,"Sellable",deliveryReturnReason!.Code,deliveryReturnReason.Name)] : []);
        if(returnCase=="Partial"&&creditPortion==11900)
            await Assert.ThrowsAsync<DispatchConflictException>(()=>deliveries.RecordAsync(actor,draft.DispatchId,
                deliveryRequest with{Returns=[new(1,.5m,"Sellable","MISSING-CAPTURE-REASON","Motivo no configurado")]},default));
        var result=await deliveries.RecordAsync(actor,draft.DispatchId,deliveryRequest,default);
        var invoicePayments=Assert.Single(result.Documents).Payments.Where(item=>item.ApplicationType=="InvoicePayment").ToArray();
        Assert.Equal(notDelivered||collected==0?0:splitCash?2:1,invoicePayments.Length);
        var payment=invoicePayments.FirstOrDefault();
        await deliveries.CloseRouteAsync(actor, draft.DispatchId,
            new(method == "Cash" ? collected : 0, null, Guid.NewGuid().ToString("N"), card ? collected : 0, method == "Transfer" ? collected : 0), default);
        if (method != "Cash" && payment is not null) await deliveries.VerifyPaymentAsync(actor, draft.DispatchId, payment.PaymentId, new(true), default);
        async Task<PaymentMethodView> SaveTargetAsync(Guid account, bool active)
        {
            using var response = await admin.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{target.PaymentMethodId}",
                new SavePaymentMethodRequest(target.PaymentMethodId, target.Code, target.Name, target.PaymentTypeId, account,
                    true, true, false, false, active, 10, target.RowVersion));
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method;
        }
        target = Assert.Single((await admin.GetFromJsonAsync<PaymentMethodPage>(
            $"/api/commerce/v1/payment-methods/?usage=Receipts&paymentMethodId={target.PaymentMethodId}&page=1&pageSize=10"))!.Items);
        if (remap) target = await SaveTargetAsync(target.AccountId, false);
        var acceptance = new SettleDispatchRequest((method == "Cash" ? collected : 0)+cashDifference, cashDifference!=0?"Diferencia de prueba":null, Guid.NewGuid().ToString("N"),CashDifferencePaymentMethodId:differenceCash?.PaymentMethodId);
        Guid? administrativeReturn=null;
        if(previouslyCollected&&hasReturn)
        {
            await Assert.ThrowsAsync<DispatchConflictException>(()=>deliveries.SettleAsync(actor,draft.DispatchId,acceptance,default));
            Assert.Equal(0,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.DispatchSettlementOperations WHERE DispatchId=@Id",draft.DispatchId));
            await Assert.ThrowsAsync<DispatchConflictException>(()=>deliveries.SettleAsync(actor,draft.DispatchId,
                acceptance with{ReturnIds=[Guid.NewGuid()]},default));
            var reason=await ScalarAsync<string>("SELECT TOP(1) Code FROM dbo.BusinessReasons WHERE BusinessId=@Id AND ReasonType=N'SalesReturn' AND IsActive=1 ORDER BY DisplayOrder,Code",fixture.BusinessId);
            var returnRequest=new ConfirmSalesReturnRequest(Guid.NewGuid(),fixture.BusinessId,fixture.WarehouseId,sale,
                DateTimeOffset.UtcNow,ReturnEconomicResolutions.Refund,"Cash","Reembolso administrativo de la entrega",
                [new(1,notDelivered?1m:.5m,"Sellable")],WorkSessionId:await fixture.OpenWebWorkSessionAsync(),ReasonCode:reason);
            var returnsService=scope.ServiceProvider.GetRequiredService<SalesReturnService>();
            var returned=await returnsService.ConfirmAsync(new(fixture.UserId,fixture.TenantId,fixture.BusinessId,
                new HashSet<string>{SalesReturnPermissionCodes.Create}),$"administrative-refund:{draft.DispatchId:N}",returnRequest);
            administrativeReturn=returned.ReturnId;
            await AssertBalancedAsync(returned.ReturnId);
            var queries=scope.ServiceProvider.GetRequiredService<SalesReturnQueryService>();
            var queryUser=new SalesReturnUserIdentity(fixture.UserId,fixture.TenantId,fixture.BusinessId,
                new HashSet<string>{SalesReturnPermissionCodes.Read});
            var history=await queries.ListReturnsAsync(queryUser,new(1,10,null,"Processed",null,null,OriginalDocumentId:sale));
            Assert.Equal(returned.ReturnId,Assert.Single(history.Items).ReturnId);
            Assert.Empty((await queries.ListReturnsAsync(queryUser,new(1,10,null,"Processed",null,null,OriginalDocumentId:otherEmission.DocumentId))).Items);
            Assert.Empty((await queries.ListReturnsAsync(queryUser with{TenantId=Guid.NewGuid()},new(1,10,null,"Processed",null,null,OriginalDocumentId:sale))).Items);
            acceptance=acceptance with{ReturnIds=[returned.ReturnId]};
        }
        var worker = ActivatorUtilities.CreateInstance<DispatchSettlementHostedService>(fixture.Services);
        using var leaseReads=restartDuringLease?new InventoryBalanceProcessingTests.CommandCounter(
            new SqlConnection(fixture.ConnectionString).Database,commandFilter:command=>command.CommandText=="dbo.DispatchSettlementOperationClaimGet"):null;
        try
        {
            if(previouslyCollected)
            {
                var closed=await deliveries.GetAsync(actor,draft.DispatchId,default);
                Assert.Equal(decimal.Min(gross-creditPortion,gross-returnedAmount),closed.Settlement!.PreviouslyCollectedTotal);
                Assert.Equal(0,closed.Settlement.BalanceDifference);
            }
            if(differenceCash is not null)
                await Assert.ThrowsAsync<DispatchConflictException>(()=>deliveries.SettleAsync(actor,draft.DispatchId,
                    acceptance with{CashDifferencePaymentMethodId=null},default));
            if(administrativeReturn is not null)
            {
                using var commands=new InventoryBalanceProcessingTests.CommandCounter(new SqlConnection(fixture.ConnectionString).Database);
                var watch=System.Diagnostics.Stopwatch.StartNew();
                await deliveries.SettleAsync(actor,draft.DispatchId,acceptance,default);
                watch.Stop();
                output.WriteLine("Administrative refund link: {0} SQL commands, {1:F1} ms",commands.Count,watch.Elapsed.TotalMilliseconds);
                Assert.Equal(8,commands.Count);
                Assert.True(watch.Elapsed<TimeSpan.FromSeconds(2),watch.Elapsed.ToString());
            }
            else await deliveries.SettleAsync(actor, draft.DispatchId, acceptance, default);
            if(cashDifference==1000 && sameForm)
            {
                await using var dateConnection=new SqlConnection(fixture.ConnectionString);await dateConnection.OpenAsync();
                await using var date=new SqlCommand("UPDATE dbo.WorkSessionClosures SET ReceivedAt='2026-12-01T00:30:00+00:00' WHERE WorkSessionId=(SELECT WorkSessionId FROM dbo.WorkSessions WHERE DispatchId=@Id);",dateConnection);
                date.Parameters.AddWithValue("@Id",draft.DispatchId);await date.ExecuteNonQueryAsync();
            }
            if(restartDuringLease)
            {
                await using var leaseConnection=new SqlConnection(fixture.ConnectionString);await leaseConnection.OpenAsync();
                await using var lease=new SqlCommand("UPDATE dbo.DispatchSettlementOperations SET Status=N'Processing',NextAttemptAt=DATEADD(SECOND,2,SYSUTCDATETIME()) WHERE DispatchId=@Id",leaseConnection);
                lease.Parameters.AddWithValue("@Id",draft.DispatchId);Assert.Equal(1,await lease.ExecuteNonQueryAsync());
            }
            fixture.Services.GetRequiredService<DispatchSettlementCoordinator>().Signal();
            await worker.StartAsync(default);
            Guid closure;
            await using (var connection = new SqlConnection(fixture.ConnectionString))
            {
                await connection.OpenAsync();
                await using var query = new SqlCommand("SELECT DispatchSettlementId FROM dispatch.Closures WHERE DispatchId=@Id", connection);
                query.Parameters.AddWithValue("@Id", draft.DispatchId); closure = (Guid)(await query.ExecuteScalarAsync())!;
            }
            if (remap)
            {
                var pending = false;
                for (var attempt = 0; attempt < 100; attempt++)
                {
                    if (await PostingStatusAsync(closure, DispatchAccountingDocumentTypes.CashDifference) == "AccountingPendingConfiguration") { pending = true; break; }
                    await Task.Delay(50);
                }
                Assert.True(pending);
                Assert.Equal("SettlementAttention", await ScalarAsync<string>("SELECT Status FROM dbo.Dispatches WHERE DispatchId=@Id", draft.DispatchId));
                var replacement = await CreateClosureFormAsync(admin, "Transfer");
                target = await SaveTargetAsync(replacement.AccountId, true);
                await scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>().ProcessAsync(
                    closure, DispatchAccountingDocumentTypes.CashDifference, fixture.BusinessId, default);
            }
            for (var attempt = 0; attempt < 100 && await ScalarAsync<string>("SELECT Status FROM dbo.Dispatches WHERE DispatchId=@Id", draft.DispatchId) != "Closed"; attempt++)
                await Task.Delay(50);
            Assert.True(await ScalarAsync<string>("SELECT Status FROM dbo.Dispatches WHERE DispatchId=@Id", draft.DispatchId) == "Closed",
                await ScalarAsync<string>("SELECT CONCAT(Status,N': ',LastErrorCode,N' ',LastErrorMessage) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id", closure));
            if(restartDuringLease)Assert.InRange(leaseReads!.Count,2,3);
            await deliveries.SettleAsync(actor, draft.DispatchId, acceptance, default);
            await scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>().ProcessAsync(
                closure, DispatchAccountingDocumentTypes.CashDifference, fixture.BusinessId, default);
            await using var checkConnection = new SqlConnection(fixture.ConnectionString); await checkConnection.OpenAsync();
            await using var check = new SqlCommand("""
                SELECT (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Sale),
                  (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Closure),
                  (SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Sale AND MovementType=N'Sale'),
                  (SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE DocumentId=@Sale OR SourceKey=CONCAT(N'dispatch-settlement:',CONVERT(nvarchar(36),@Dispatch))),
                  (SELECT COUNT(*) FROM dbo.Receivables WHERE SourceDocumentId=@Sale),
                  (SELECT COUNT(*) FROM dbo.WorkSessions WHERE DispatchId=@Dispatch AND Kind=N'Dispatch' AND Status=N'Closed'),
                  (SELECT COUNT(*) FROM dbo.WorkSessionClosures header JOIN dbo.WorkSessions session ON session.WorkSessionId=header.WorkSessionId WHERE session.DispatchId=@Dispatch),
                  (SELECT SUM(line.Debit) FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId WHERE entry.SourceDocumentId=@Closure AND line.AccountId=@Target),
                  (SELECT SUM(line.Credit) FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId WHERE entry.SourceDocumentId=@Closure AND line.AccountId=@Original);
                """, checkConnection);
            check.Parameters.AddWithValue("@Sale", sale); check.Parameters.AddWithValue("@Closure", closure);
            check.Parameters.AddWithValue("@Dispatch", draft.DispatchId); check.Parameters.AddWithValue("@Target", target.AccountId);
            check.Parameters.AddWithValue("@Original", original.AccountId);
            await using var reader = await check.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt32(0)); Assert.Equal((sameAccount || collected==0) && cashDifference==0 ? 0 : 1, reader.GetInt32(1));
            Assert.Equal(1, reader.GetInt32(2)); Assert.Equal(previouslyCollected?1:0, reader.GetInt32(3)); Assert.Equal(creditPortion>0?1:0, reader.GetInt32(4));
            Assert.Equal(1, reader.GetInt32(5)); Assert.Equal(1, reader.GetInt32(6));
            if (!sameAccount && collected>0) { Assert.Equal(splitCash?collected/2:collected+(method=="Cash"?cashDifference:0), reader.GetDecimal(7)); Assert.Equal(collected, reader.GetDecimal(8)); }
            if(cashDifference!=0)
            {
                await reader.CloseAsync();
                await AssertBalancedAsync(closure);
                var differenceAccount=differenceCash?.AccountId??target.AccountId;
                await using var attribution=new SqlCommand("SELECT COALESCE(SUM(line.Debit-line.Credit),0) FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId WHERE entry.SourceDocumentId=@Id AND line.AccountId=@Account;",checkConnection);
                attribution.Parameters.AddWithValue("@Id",closure);attribution.Parameters.AddWithValue("@Account",differenceAccount);
                var expectedNet=cashDifference+(splitCash?collected/2:method=="Cash"&&!sameAccount?collected:0);
                Assert.Equal(expectedNet,Convert.ToDecimal(await attribution.ExecuteScalarAsync()));
            }
            await reader.CloseAsync();
            if(cashDifference==1000 && sameForm)
            {
                var recorded=System.Text.Json.JsonSerializer.Deserialize<DispatchCashDifferencePayload>(await ScalarAsync<string>("SELECT PayloadJson FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id",closure),new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
                Assert.Equal(new DateTime(2026,11,30),recorded.OccurredAt.Date);
                Assert.Equal(TimeSpan.FromHours(-5),recorded.OccurredAt.Offset);
            }
            if(creditPortion>0)
            {
                Assert.Equal(creditPortion-reversedCredit,await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Receivables WHERE SourceDocumentId=@Id",sale));
                Assert.Equal(0,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.CustomerPaymentApplications payment JOIN dbo.Receivables debt ON debt.ReceivableId=payment.ReceivableId WHERE debt.SourceDocumentId=@Id",sale));
            }
            if(administrativeReturn is {} refundId)
            {
                var complete=await deliveries.GetAsync(actor,draft.DispatchId,default);
                Assert.Equal(refundId,Assert.Single(complete.LinkedReturns!).ReturnId);
                Assert.Equal(1,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SalesReturns WHERE OriginalDocumentId=@Id",sale));
                Assert.Equal(returnedAmount,await ScalarAsync<decimal>("SELECT SUM(Amount) FROM dbo.SalesReturnSettlements WHERE ReturnId=@Id AND SettlementType=N'Refund'",refundId));
                Assert.Equal(1,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id",refundId));
                Assert.Equal(1,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Id AND DocumentType=N'SalesReturn'",refundId));
                Assert.Equal(1,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE SourceKey=CONCAT(N'sales-return:',LOWER(REPLACE(CONVERT(nvarchar(36),@Id),N'-',N'')))",refundId));
                Assert.Equal(0,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.CustomerCredits WHERE SourceReturnId=@Id",refundId));
                await Assert.ThrowsAsync<DispatchConflictException>(()=>deliveries.SettleAsync(actor,draft.DispatchId,
                    acceptance with{ReturnIds=null},default));
            }
            if(hasReturn&&!previouslyCollected)
            {
                var expectedResolution=creditPortion==gross?ReturnEconomicResolutions.CustomerCredit:ReturnEconomicResolutions.ReverseUncollected;
                Assert.Equal(1,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SalesReturns WHERE OriginalDocumentId=@Id AND WorkSessionId IS NULL",sale));
                var returnedPayload=SalesReturnContractSerializer.Deserialize(await ScalarAsync<string>(
                    "SELECT payload.PayloadJson FROM dbo.SalesReturns returned JOIN dbo.DocumentProcessingPayloads payload ON payload.DocumentId=returned.ReturnId AND payload.DocumentType=N'SalesReturn' WHERE returned.OriginalDocumentId=@Id",sale));
                Assert.Equal(expectedResolution,returnedPayload.EconomicResolution);
                await AssertBalancedAsync(returnedPayload.ReturnId);
                Assert.Equal(-reversedCredit,await ScalarAsync<decimal>("SELECT COALESCE(SUM(Amount),0) FROM reporting.SalesReportPaymentFacts WHERE SourceDocumentId=@Id AND MovementType=N'CreditApplication'",returnedPayload.ReturnId));
                Assert.Equal(0,await ScalarAsync<int>("SELECT COUNT(*) FROM reporting.SalesReportPaymentFacts WHERE SourceDocumentId=@Id AND MovementType=N'Refund'",returnedPayload.ReturnId));
                Assert.Equal(reversedTender,await ScalarAsync<decimal>("SELECT COALESCE(SUM(line.Credit),0) FROM dbo.SalesReturns returned JOIN dbo.AccountingEntries entry ON entry.SourceDocumentId=returned.ReturnId AND entry.SourceDocumentType=N'SalesReturn' JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId JOIN dbo.SalesPayments payment ON payment.DocumentId=returned.OriginalDocumentId AND payment.PaymentNumber=1 AND payment.AccountId=line.AccountId WHERE returned.OriginalDocumentId=@Id",sale));
                var returnRequest=new ConfirmSalesReturnRequest(returnedPayload.ReturnId,fixture.BusinessId,returnedPayload.WarehouseId,
                    sale,returnedPayload.ReturnedAt,returnedPayload.EconomicResolution,null,returnedPayload.ReasonDescription,
                    returnedPayload.Lines.Select(line=>new ConfirmSalesReturnLineRequest(line.OriginalLineNumber,line.Quantity,line.InventoryDisposition)).ToArray(),
                    ReasonCode:returnedPayload.ReasonCode,Notes:returnedPayload.Notes,ReturnScopeCode:returnedPayload.ReturnScopeCode,
                    ReturnedChargeIds:returnedPayload.Charges?.Select(charge=>charge.AppliedChargeId).ToArray()??[],
                    DispatchId:returnedPayload.DispatchId);
                var returnUser=new SalesReturnUserIdentity(fixture.UserId,fixture.TenantId,fixture.BusinessId,
                    new HashSet<string>{SalesReturnPermissionCodes.Create,DispatchPermissionCodes.Settle});
                var returnsService=scope.ServiceProvider.GetRequiredService<SalesReturnService>();
                var replayed=await returnsService.ConfirmAsync(returnUser,
                    $"dispatch-settlement:{draft.DispatchId:N}:return:{sale:N}",returnRequest);
                Assert.True(replayed.IdempotentReplay);
                if(expectedResolution==ReturnEconomicResolutions.ReverseUncollected)
                {
                await Assert.ThrowsAsync<SalesReturnForbiddenException>(()=>returnsService.ConfirmAsync(returnUser with{
                    Permissions=new HashSet<string>{SalesReturnPermissionCodes.Create}},Guid.NewGuid().ToString("N"),returnRequest));
                await Assert.ThrowsAsync<SalesReturnConflictException>(()=>returnsService.ConfirmAsync(returnUser,
                    Guid.NewGuid().ToString("N"),returnRequest with{ReturnId=Guid.NewGuid()}));
                }

                Assert.Equal(reversedTender,await ScalarAsync<decimal>("SELECT COALESCE(SUM(settlement.Amount),0) FROM dbo.SalesReturnSettlements settlement WHERE OriginalDocumentId=@Id AND SettlementType=N'ReverseUncollected'",sale));
                Assert.Equal(0,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.CustomerCredits credit JOIN dbo.SalesReturns returned ON returned.ReturnId=credit.SourceReturnId WHERE returned.OriginalDocumentId=@Id",sale));
                Assert.Equal(1,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.InventoryMovements movement JOIN dbo.SalesReturns returned ON returned.ReturnId=movement.DocumentId WHERE returned.OriginalDocumentId=@Id AND movement.DocumentType=N'SalesReturn'",sale));
                Assert.Equal(0,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.WorkSessionMovements movement JOIN dbo.SalesReturns returned ON movement.SourceKey=CONCAT(N'sales-return:',LOWER(REPLACE(CONVERT(nvarchar(36),returned.ReturnId),N'-',N''))) WHERE returned.OriginalDocumentId=@Id",sale));
            }
            if(refundAfterCollection)
            {
                var returnUser=new SalesReturnUserIdentity(fixture.UserId,fixture.TenantId,fixture.BusinessId,
                    new HashSet<string>{SalesReturnPermissionCodes.Create});
                var query=scope.ServiceProvider.GetRequiredService<SalesReturnQueryService>();
                var currentSale=(await query.GetReturnableSaleAsync(returnUser,sale))!;
                var actualCard=Assert.Single(currentSale.Payments);
                Assert.Equal(method,actualCard.MethodCode);Assert.Equal(payment!.PaymentId,actualCard.OriginalDispatchPaymentId);
                var reason=await ScalarAsync<string>("SELECT TOP(1) Code FROM dbo.BusinessReasons WHERE BusinessId=@Id AND ReasonType=N'SalesReturn' AND IsActive=1 ORDER BY DisplayOrder,Code",fixture.BusinessId);
                var refundRequest=new ConfirmSalesReturnRequest(Guid.NewGuid(),fixture.BusinessId,fixture.WarehouseId,sale,
                    new DateTimeOffset(2026,11,18,18,0,0,TimeSpan.Zero),ReturnEconomicResolutions.Refund,method,
                    "Reversión de tarjeta recaudada",[new(1,.5m,ReturnInventoryDispositions.Sellable)],
                    WorkSessionId:await fixture.OpenWebWorkSessionAsync(),ReasonCode:reason,
                    OriginalDispatchPaymentId:actualCard.OriginalDispatchPaymentId);
                var returnsService=scope.ServiceProvider.GetRequiredService<SalesReturnService>();
                var otherPrepared=await logistics.PrepareAsync(actor,otherDraft.DispatchId,
                    new(otherDraft.RowVersion,Guid.NewGuid().ToString("N")),default);
                await logistics.ReleaseAsync(actor,otherDraft.DispatchId,
                    new(otherPrepared.RowVersion,Guid.NewGuid().ToString("N")),default);
                var otherDocument=Assert.Single((await deliveries.GetAsync(actor,otherDraft.DispatchId,default)).Documents);
                await deliveries.RecordAsync(actor,otherDraft.DispatchId,new(otherDocument.DispatchSourceDocumentId,
                    "Delivered",null,null,null,null,DateTimeOffset.UtcNow,Guid.NewGuid().ToString("N"),
                    [new("InvoicePayment","Cash",otherDocument.DocumentTotal,null,null,original.PaymentMethodId,original.Version)],[]),default);
                var unrelatedDispatchSession=await ScalarAsync<Guid>("SELECT WorkSessionId FROM dbo.WorkSessions WHERE DispatchId=@Id AND Kind=N'Dispatch' AND Status=N'Open'",otherDraft.DispatchId);
                Assert.Equal(0,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.WorkSessions session CROSS APPLY worksessions.OpenSessionForUser(session.TenantId,session.BusinessId,session.UserId,session.WorkSessionId) accepted WHERE session.WorkSessionId=@Id",unrelatedDispatchSession));
                await Assert.ThrowsAsync<SalesReturnValidationException>(()=>returnsService.ConfirmAsync(returnUser,
                    Guid.NewGuid().ToString("N"),refundRequest with{ReturnId=Guid.NewGuid(),WorkSessionId=unrelatedDispatchSession}));
                Assert.Equal(0,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SalesReturns WHERE OriginalDocumentId=@Id",sale));
                await Assert.ThrowsAsync<SalesReturnValidationException>(()=>returnsService.ConfirmAsync(returnUser,
                    Guid.NewGuid().ToString("N"),refundRequest with{OriginalPaymentNumber=1,OriginalDispatchPaymentId=null}));
                var refunded=await returnsService.ConfirmAsync(returnUser,Guid.NewGuid().ToString("N"),refundRequest);
                await AssertBalancedAsync(refunded.ReturnId);
                Assert.Equal(gross/2,await ScalarAsync<decimal>("SELECT SUM(line.Credit) FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId JOIN dbo.SalesReturns returned ON returned.ReturnId=entry.SourceDocumentId JOIN dbo.DispatchDeliveryPayments payment ON payment.DispatchDeliveryPaymentId=returned.OriginalDispatchPaymentId JOIN dbo.WorkSessions session ON session.DispatchId=payment.DispatchId AND session.Kind=N'Dispatch' JOIN dbo.WorkSessionClosures closure ON closure.WorkSessionId=session.WorkSessionId JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=closure.WorkSessionClosureId AND job.SourceDocumentType=N'DispatchCashDifference' CROSS APPLY OPENJSON(job.AppliedPaymentMethodsJson) WITH(SourceLineId uniqueidentifier,AccountId uniqueidentifier) binding WHERE entry.SourceDocumentId=@Id AND binding.SourceLineId=payment.DispatchDeliveryPaymentId AND line.AccountId=binding.AccountId",refunded.ReturnId));
                Assert.Equal("APPROVED",await ScalarAsync<string>("SELECT ApprovalNumber FROM dbo.SalesReturns WHERE ReturnId=@Id",refunded.ReturnId));
                currentSale=(await query.GetReturnableSaleAsync(returnUser,sale))!;
                Assert.Equal(gross/2,Assert.Single(currentSale.Payments).AvailableAmount);
                async Task<SalesReturnAcceptance?> TryAnotherRefundAsync()
                {
                    try{return await returnsService.ConfirmAsync(returnUser,Guid.NewGuid().ToString("N"),refundRequest with{ReturnId=Guid.NewGuid()});}
                    catch(SalesReturnConflictException){return null;}
                }
                var outcomes=await Task.WhenAll(TryAnotherRefundAsync(),TryAnotherRefundAsync());
                var last=Assert.Single(outcomes.Where(value=>value is not null))!;
                await AssertBalancedAsync(last.ReturnId);
                Assert.Equal(2,await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SalesReturns WHERE OriginalDocumentId=@Id",sale));
                Assert.Equal(0m,Assert.Single((await query.GetReturnableSaleAsync(returnUser,sale))!.Payments).AvailableAmount);
                Assert.Equal(gross,await ScalarAsync<decimal>("SELECT SUM(Amount) FROM dbo.SalesReturnSettlements WHERE OriginalDispatchPaymentId=@Id",payment.PaymentId));
                Assert.Equal(1,await CountAsync("AccountingEntries","SourceDocumentId",sale));
            }
            Assert.Equal(originalHash, await ScalarAsync<byte[]>("SELECT PayloadHash FROM dbo.SalesDocuments WHERE DocumentId=@Id", sale));
        }
        finally { await worker.StopAsync(default); worker.Dispose(); }
    }
}
