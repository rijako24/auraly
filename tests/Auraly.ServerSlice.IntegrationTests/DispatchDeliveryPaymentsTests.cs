using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Auraly.Application.Dispatching;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Dispatching;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed partial class DispatchDeliveryPaymentsTests(ServerSliceFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task Retired_return_reason_preserves_accepted_goods_but_cannot_authorize_changed_quantities()
    {
        var form=await CreateFormAsync("Cash");
        var (dispatch,source,_)=await SeedDeliveryAsync();
        var reasonId=Guid.NewGuid();var code=reasonId.ToString("N");
        await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
        await using(var seed=new SqlCommand("""
            INSERT dbo.BusinessReasons(ReasonId,BusinessId,ReasonType,Code,Name,CreatedAt,UpdatedAt)
            VALUES(@Id,@Business,N'DeliveryReturn',@Code,N'Motivo temporal de prueba',SYSUTCDATETIME(),SYSUTCDATETIME());
            """,connection))
        {seed.Parameters.AddWithValue("@Id",reasonId);seed.Parameters.AddWithValue("@Business",fixture.BusinessId);
            seed.Parameters.AddWithValue("@Code",code);await seed.ExecuteNonQueryAsync();}
        using var driver=fixture.CreateUserClient(fixture.UserId,DispatchPermissionCodes.ExecuteDeliveries);
        var url=$"/api/commerce/v1/dispatches/{dispatch}/delivery-results";
        var returned=new DispatchDeliveryReturnInput(1,.25m,"Sellable",code,"Motivo temporal de prueba");
        var tender=new DispatchDeliveryPaymentInput("InvoicePayment","Cash",37500m,null,null,form.PaymentMethodId,form.Version);
        var request=new RecordDispatchDeliveryRequest(source,"PartiallyDelivered","Devolución parcial",null,null,null,
            DateTimeOffset.Parse("2026-11-17T16:00:00Z"),Guid.NewGuid().ToString("N"),[tender],[returned]);
        using(var accepted=await driver.PutAsJsonAsync(url,request))
            Assert.True(accepted.IsSuccessStatusCode,await accepted.Content.ReadAsStringAsync());
        await using(var retire=new SqlCommand("UPDATE dbo.BusinessReasons SET IsActive=0 WHERE ReasonId=@Id",connection))
        {retire.Parameters.AddWithValue("@Id",reasonId);await retire.ExecuteNonQueryAsync();}
        // A payment-only edit preserves the previously accepted goods/reason.
        using(var edit=await driver.PutAsJsonAsync(url,request with{IdempotencyKey=Guid.NewGuid().ToString("N"),Notes="Editar sólo el soporte"}))
            Assert.True(edit.IsSuccessStatusCode,await edit.Content.ReadAsStringAsync());
        // The inactive code must not authorize new goods or a different disposition.
        foreach(var change in new[]{returned with{Quantity=.5m},returned with{InventoryDisposition="NotReturned"}})
        {
            var payment=tender with{Amount=50000m-change.Quantity*50000m};
            using var rejected=await driver.PutAsJsonAsync(url,request with{IdempotencyKey=Guid.NewGuid().ToString("N"),
                Payments=[payment],Returns=[change]});
            Assert.Equal(HttpStatusCode.Conflict,rejected.StatusCode);
        }
        var current=(await driver.GetFromJsonAsync<DispatchExecutionDetail>($"/api/commerce/v1/dispatches/{dispatch}/execution"))!;
        Assert.Equal(.25m,Assert.Single(Assert.Single(current.Documents).Returns).Quantity);
        Assert.Equal(37500m,Assert.Single(Assert.Single(current.Documents).Payments).Amount);
    }

    [Theory]
    [InlineData("Cash")]
    [InlineData("DebitCard")]
    [InlineData("CreditCard")]
    [InlineData("Transfer")]
    public async Task Native_delivery_persists_its_form_and_support_once_and_rejects_stale_or_foreign_requests(string method)
    {
        var form = await CreateFormAsync(method);
        var (dispatch, source, sale) = await SeedDeliveryAsync();
        using var driver = fixture.CreateUserClient(fixture.UserId, DispatchPermissionCodes.ExecuteDeliveries);
        var choices = await driver.GetFromJsonAsync<PaymentMethodPage>(
            $"/api/commerce/v1/payment-methods/?usage=Receipts&methodCode={method}&paymentMethodId={form.PaymentMethodId}&page=1&pageSize=10");
        Assert.Equal(form.PaymentMethodId, Assert.Single(choices!.Items).PaymentMethodId);
        var card = method is "DebitCard" or "CreditCard";
        var franchise = card ? (await driver.GetFromJsonAsync<ReferenceOption[]>(
            "/api/commerce/v1/reference-options/card-franchise"))!.First().Code : null;
        var tender = new DispatchDeliveryPaymentInput("InvoicePayment", method, 50_000m,
            method == "Transfer" ? new string(' ', 140) + "TRANSFER-100  " : null, null, form.PaymentMethodId, form.Version,
            card ? $" {franchise} " : null, card ? new string(' ', 90) + "APPROVAL-100  " : null);
        var request = new RecordDispatchDeliveryRequest(source, "Delivered", null, "Entrega real de prueba", null, null,
            DateTimeOffset.Parse("2026-11-17T16:00:00Z"), Guid.NewGuid().ToString("N"), [tender], []);
        var url = $"/api/commerce/v1/dispatches/{dispatch}/delivery-results";
        if (method != "Cash")
        {
            using var missingSupport = await driver.PutAsJsonAsync(url, request with {
                Payments = [tender with { Reference = null, ApprovalNumber = null }] });
            Assert.Equal(HttpStatusCode.BadRequest, missingSupport.StatusCode);
        }
        using (var stale = await driver.PutAsJsonAsync(url, request with {
            Payments = [tender with { PaymentMethodVersion = form.Version + 1 }] }))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var wrongAmount = await driver.PutAsJsonAsync(url, request with { Payments = [tender with { Amount = 50_001m }] }))
            Assert.Equal(HttpStatusCode.BadRequest, wrongAmount.StatusCode);
        using (var foreignForm = await driver.PutAsJsonAsync(url, request with {
            Payments = [tender with { PaymentMethodId = Guid.NewGuid() }] }))
            Assert.Equal(HttpStatusCode.Conflict, foreignForm.StatusCode);
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<DispatchDeliveryService>();
        var actor = new DispatchActorIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { DispatchPermissionCodes.ExecuteDeliveries }, false);
        if(method=="Cash")
        {
            var reason=Guid.NewGuid().ToString("N");
            await using var reasonConnection=new SqlConnection(fixture.ConnectionString);await reasonConnection.OpenAsync();
            await using var seed=new SqlCommand("""
                INSERT dbo.BusinessReasons(ReasonId,BusinessId,ReasonType,Code,Name,CreatedAt,UpdatedAt)
                VALUES(NEWID(),@Business,N'DeliveryReturn',@Code,N'Devolución de entrega',SYSUTCDATETIME(),SYSUTCDATETIME());
                """,reasonConnection);
            seed.Parameters.AddWithValue("@Business",fixture.BusinessId);seed.Parameters.AddWithValue("@Code",reason);
            await seed.ExecuteNonQueryAsync();
            using var excessiveReturn=await driver.PutAsJsonAsync(url,request with{
                DeliveryStatus="PartiallyDelivered",Reason="Devolución parcial",Returns=[new(1,2m,"Sellable",reason,"Cantidad excesiva")]
            });
            Assert.Equal(HttpStatusCode.BadRequest,excessiveReturn.StatusCode);
            Assert.Contains("excede",await excessiveReturn.Content.ReadAsStringAsync(),StringComparison.OrdinalIgnoreCase);
        }
        await Assert.ThrowsAsync<DispatchNotFoundException>(() => service.GetAsync(
            actor with { TenantId = Guid.NewGuid() }, dispatch, CancellationToken.None));
        await Assert.ThrowsAsync<DispatchConflictException>(() => service.RecordAsync(
            actor with { UserId = Guid.NewGuid() }, dispatch, request, CancellationToken.None));
        var watch = Stopwatch.StartNew();
        using var response = await driver.PutAsJsonAsync(url, request);
        watch.Stop();
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var saved = (await response.Content.ReadFromJsonAsync<DispatchExecutionDetail>())!;
        var payment = Assert.Single(Assert.Single(saved.Documents).Payments);
        Assert.Equal("PendingSettlement", saved.Status);
        Assert.Equal(method, payment.PaymentMethod); Assert.Equal(50_000m, payment.Amount);
        Assert.Equal(form.PaymentMethodId, payment.PaymentMethodId); Assert.Equal(form.Version, payment.PaymentMethodVersion);
        Assert.Equal(tender.Reference?.Trim(), payment.Reference); Assert.Equal(franchise, payment.CardFranchiseCode);
        Assert.Equal(tender.ApprovalNumber?.Trim(), payment.ApprovalNumber);
        using var replay = await driver.PutAsJsonAsync(url, request);
        Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
        var replayed = (await replay.Content.ReadFromJsonAsync<DispatchExecutionDetail>())!;
        Assert.Equal(payment.PaymentId, Assert.Single(Assert.Single(replayed.Documents).Payments).PaymentId);
        // A newer edit must survive an old offline retry: the old receipt of
        // acceptance remains, but replay returns the current aggregate unchanged.
        using var edited = await driver.PutAsJsonAsync(url, request with {
            Notes = "Entrega corregida", IdempotencyKey = Guid.NewGuid().ToString("N") });
        Assert.True(edited.IsSuccessStatusCode, await edited.Content.ReadAsStringAsync());
        var editedState = (await edited.Content.ReadFromJsonAsync<DispatchExecutionDetail>())!;
        payment = Assert.Single(Assert.Single(editedState.Documents).Payments);
        using var oldRetry = await driver.PutAsJsonAsync(url, request);
        Assert.True(oldRetry.IsSuccessStatusCode, await oldRetry.Content.ReadAsStringAsync());
        var current = (await oldRetry.Content.ReadFromJsonAsync<DispatchExecutionDetail>())!;
        Assert.Equal("Entrega corregida", Assert.Single(current.Documents).Notes);
        Assert.Equal(payment.PaymentId, Assert.Single(Assert.Single(current.Documents).Payments).PaymentId);
        using var altered = await driver.PutAsJsonAsync(url, request with { Notes = "Otro contenido con la misma clave" });
        Assert.Equal(HttpStatusCode.Conflict, altered.StatusCode);
        using var noPermission = fixture.CreateUserClient(fixture.UserId, DispatchPermissionCodes.Read);
        using var denied = await noPermission.PutAsJsonAsync(url, request);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var file = new MultipartFormDataContent();
        var image = new ByteArrayContent([1, 2, 3]); image.Headers.ContentType = new("image/png"); file.Add(image, "file", "soporte.png");
        using var uploadDenied = await noPermission.PostAsync($"/api/commerce/v1/dispatches/{dispatch}/evidence", file);
        Assert.Equal(HttpStatusCode.Forbidden, uploadDenied.StatusCode);
        using var forbiddenReview = await driver.PutAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch}/payments/{payment.PaymentId}/verification",
            new VerifyDispatchPaymentRequest(true));
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenReview.StatusCode);
        await using var connection = new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        await using var verify = new SqlCommand("""
            SELECT (SELECT COUNT(*) FROM dbo.DispatchDeliveryPayments WHERE DispatchId=@Dispatch),
              (SELECT COUNT(*) FROM dbo.DispatchDeliveryEvents WHERE DispatchId=@Dispatch),
              (SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Sale),
              (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Sale),
              (SELECT COUNT(*) FROM dbo.WorkSessions WHERE DispatchId=@Dispatch AND Kind=N'Dispatch' AND Status=N'Open'),
              (SELECT COUNT(*) FROM dbo.DispatchDeliveryEvents WHERE DispatchId=@Dispatch AND IsCurrent=1);
            """, connection);
        verify.Parameters.AddWithValue("@Dispatch", dispatch); verify.Parameters.AddWithValue("@Sale", sale);
        await using var reader = await verify.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0)); Assert.Equal(2, reader.GetInt32(1));
        Assert.Equal(0, reader.GetInt32(2)); Assert.Equal(0, reader.GetInt32(3));
        Assert.Equal(1, reader.GetInt32(4)); Assert.Equal(1, reader.GetInt32(5));
        await reader.CloseAsync();
        output.WriteLine("Capture {0}: form/evidence persisted, replay unchanged, {1:F2} ms", method, watch.Elapsed.TotalMilliseconds);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3));

        var closeUrl = $"/api/commerce/v1/dispatches/{dispatch}/close-route";
        var declaration = new CloseDispatchRouteRequest(19_000m, null, Guid.NewGuid().ToString("N"), 3_000m, 4_000m);
        using var settleOnly = fixture.CreateUserClient(fixture.UserId, DispatchPermissionCodes.Settle);
        using (var deniedClose = await settleOnly.PostAsJsonAsync(closeUrl, declaration))
            Assert.Equal(HttpStatusCode.Forbidden, deniedClose.StatusCode);
        using (var incomplete = await driver.PostAsJsonAsync(closeUrl, declaration with { DeclaredCard = null }))
            Assert.Equal(HttpStatusCode.BadRequest, incomplete.StatusCode);
        // A blind declaration accepts real counted values even when they differ
        // from the expected collection. Double click keeps the same actor and row.
        var closedResponses = await Task.WhenAll(driver.PostAsJsonAsync(closeUrl, declaration), driver.PostAsJsonAsync(closeUrl, declaration));
        foreach (var closedResponse in closedResponses)
        {
            using (closedResponse)
            {
                Assert.True(closedResponse.IsSuccessStatusCode, await closedResponse.Content.ReadAsStringAsync());
                var closed = (await closedResponse.Content.ReadFromJsonAsync<DispatchExecutionDetail>())!.Settlement!;
                Assert.Equal(19_000m, closed.DeclaredCash); Assert.Equal(3_000m, closed.DeclaredCard);
                Assert.Equal(4_000m, closed.DeclaredTransfer); Assert.Equal(fixture.UserId, closed.TransporterClosedBy);
                Assert.NotNull(closed.TransporterClosedAt); Assert.Equal("PendingReview", closed.Status);
            }
        }
        using (var alteredClose = await driver.PostAsJsonAsync(closeUrl, declaration with { DeclaredCash = 18_000m }))
            Assert.Equal(HttpStatusCode.Conflict, alteredClose.StatusCode);
        using (var newClose = await driver.PostAsJsonAsync(closeUrl, declaration with { IdempotencyKey = Guid.NewGuid().ToString("N") }))
            Assert.Equal(HttpStatusCode.Conflict, newClose.StatusCode);
        using (var afterClose = await driver.PutAsJsonAsync(url, request with { IdempotencyKey = Guid.NewGuid().ToString("N") }))
            Assert.Equal(HttpStatusCode.Conflict, afterClose.StatusCode);
        await Assert.ThrowsAsync<DispatchConflictException>(() => service.CloseRouteAsync(actor with { UserId = Guid.NewGuid() }, dispatch, declaration, CancellationToken.None));
        await using var count = new SqlCommand("SELECT COUNT(*) FROM dispatch.Closures WHERE DispatchId=@Dispatch", connection);
        count.Parameters.AddWithValue("@Dispatch", dispatch); Assert.Equal(1, Convert.ToInt32(await count.ExecuteScalarAsync()));
        using var receipt = await driver.PostAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch}/closure-receipt", new DispatchClosureReceiptRequest("Empresa de prueba"));
        Assert.True(receipt.IsSuccessStatusCode, await receipt.Content.ReadAsStringAsync());
        var html = (await receipt.Content.ReadFromJsonAsync<DispatchClosureReceiptView>())!.Html;
        Assert.Contains("data-auraly-report=\"dispatch-closure\"", html);
        Assert.Contains("Valores declarados", html); Assert.DoesNotContain("Faltante", html);
        Assert.DoesNotContain("Efectivo esperado", html); Assert.Contains("America/Bogota", html);
        using var reviewer=fixture.CreateAdminClient(DispatchPermissionCodes.Settle,DispatchPermissionCodes.Read,DispatchPermissionCodes.ReadAll);
        var filtered=(await reviewer.GetFromJsonAsync<DispatchPage>($"/api/commerce/v1/dispatches?page=1&pageSize=25&status=PendingSettlement&search={dispatch}"))!;
        Assert.Equal(dispatch,Assert.Single(filtered.Items).DispatchId);
        var correction=new CorrectDispatchPaymentRequest(form.PaymentMethodId,form.Version,method,
            method=="Transfer"?"TRF-CORRECTED":null,franchise,card?"APPROVAL-CORRECTED":null,"Corrección verificada del soporte",null);
        using var corrected=await reviewer.PutAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch}/payments/{payment.PaymentId}/correction",correction);
        Assert.True(corrected.IsSuccessStatusCode,await corrected.Content.ReadAsStringAsync());
        var correctedPayment=(await corrected.Content.ReadFromJsonAsync<DispatchPaymentReviewResult>())!.Payment;
        Assert.Equal(50000m,correctedPayment.Amount);Assert.NotNull(correctedPayment.CorrectedAt);
        using(var repeated=await reviewer.PutAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch}/payments/{payment.PaymentId}/correction",correction))
        {Assert.True(repeated.IsSuccessStatusCode,await repeated.Content.ReadAsStringAsync());Assert.Equal(correctedPayment.CorrectedAt,(await repeated.Content.ReadFromJsonAsync<DispatchPaymentReviewResult>())!.Payment.CorrectedAt);}
        if(method!="Cash")
        {
            using(var staleReview=await reviewer.PutAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch}/payments/{payment.PaymentId}/verification",new VerifyDispatchPaymentRequest(true)))Assert.Equal(HttpStatusCode.Conflict,staleReview.StatusCode);
            using var checkedPayment=await reviewer.PutAsJsonAsync($"/api/commerce/v1/dispatches/{dispatch}/payments/{payment.PaymentId}/verification",new VerifyDispatchPaymentRequest(true,correctedPayment.CorrectedAt));
            Assert.True(checkedPayment.IsSuccessStatusCode,await checkedPayment.Content.ReadAsStringAsync());Assert.NotNull((await checkedPayment.Content.ReadFromJsonAsync<DispatchPaymentReviewResult>())!.Payment.VerifiedAt);
        }
    }

    private async Task<PaymentMethodView> CreateFormAsync(string method)
    {
        using var admin = fixture.CreateAdminClient(CatalogPermissionCodes.Read, CatalogPermissionCodes.Update,
            AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure);
        Guid? bankId = null;
        if (method == "Transfer")
        {
            bankId = Guid.NewGuid();
            var bankTypes = (await admin.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/bank-account-type"))!;
            using var bank = await admin.PutAsJsonAsync($"/api/commerce/v1/accounting/bank-accounts/{bankId}",
                new SaveBankAccountRequest(bankId.Value, null, bankTypes.Single(type => type.Code == "Checking").Id,
                    "Banco de entrega", bankId.Value.ToString("N"), "Banco de entrega", false, true, null));
            Assert.True(bank.IsSuccessStatusCode, await bank.Content.ReadAsStringAsync());
        }
        var account = method == "Cash" ? await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture, admin)
            : await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, admin,
                method == "Transfer" ? "1119" : "1399", "Asset", method == "Transfer" ? "Bank" : "CardClearing", "Debit", bankId);
        var types = (await admin.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/payment-method"))!;
        var save = new SavePaymentMethodRequest(Guid.NewGuid(), $"DELIVERY-{Guid.NewGuid():N}", $"Recaudo {method}",
            types.Single(type => type.Code == method).Id, account.AccountId, true, true, false, false, true, 10);
        using var created = await admin.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{save.PaymentMethodId}", save);
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        return (await created.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method;
    }

    private async Task<(Guid Dispatch, Guid Source, Guid Sale)> SeedDeliveryAsync()
    {
        var dispatch = Guid.NewGuid(); var source = Guid.NewGuid(); var sale = Guid.NewGuid();
        await using var connection = new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        await using var command = new SqlCommand("""
            INSERT dbo.SalesDocuments(DocumentId,BusinessId,WarehouseId,SourceMode,DocumentSeriesId,
              DocumentNumber,DocumentPrefix,DocumentSeriesCode,DocumentConsecutive,DocumentType,IdempotencyKey,
              PayloadHash,IssuedAt,CustomerIdentification,UntaxedAmount,TaxAmount,PayableAmount,ProcessingStatus,ReceivedAt,CollectionMode)
              VALUES(@Sale,@Business,@Warehouse,N'Online',@Series,CONVERT(nvarchar(36),@Sale),@Prefix,N'QA',1,N'SalesReceipt',
                CONVERT(nvarchar(36),@Sale),HASHBYTES('SHA2_256',CONVERT(nvarchar(36),@Sale)),SYSUTCDATETIME(),
                N'222222222222',50000,0,50000,N'Completed',SYSUTCDATETIME(),N'Dispatch');
            INSERT dbo.SalesDocumentLines(DocumentId,LineNumber,ProductId,Description,TaxCode,TaxRate,
              Quantity,UnitPrice,DiscountAmount,TaxAmount,UntaxedAmount,LineTotal)
              VALUES(@Sale,1,@Product,N'Producto entregado',N'01',0,1,50000,0,0,50000,50000);
            INSERT dbo.Dispatches(DispatchId,TenantId,BusinessId,DispatchNumber,ScheduledDate,DriverUserId,DriverName,
              Status,CreatedBy,CreatedAt,UpdatedAt)
              VALUES(@Dispatch,@Tenant,@Business,CONVERT(nvarchar(36),@Dispatch),'20261117',@User,N'Conductor de prueba',
                N'InDelivery',@User,SYSUTCDATETIME(),SYSUTCDATETIME());
            INSERT dbo.DispatchSourceDocuments(DispatchSourceDocumentId,DispatchId,SourceDocumentId,SourceDocumentType,
              DocumentNumberSnapshot,CustomerNameSnapshot,SellerNameSnapshot,DocumentTotalSnapshot,CreditAmountSnapshot,Status,CreatedAt)
              VALUES(@Source,@Dispatch,@Sale,N'SalesReceipt',N'CV-ENTREGA',N'Cliente de entrega',N'Vendedor de prueba',50000,0,N'Released',SYSUTCDATETIME());
            """, connection);
        command.Parameters.AddWithValue("@Sale", sale); command.Parameters.AddWithValue("@Source", source);
        command.Parameters.AddWithValue("@Prefix", sale.ToString("N")[..8]);
        command.Parameters.AddWithValue("@Dispatch", dispatch); command.Parameters.AddWithValue("@Business", fixture.BusinessId);
        command.Parameters.AddWithValue("@Tenant", fixture.TenantId); command.Parameters.AddWithValue("@Warehouse", fixture.WarehouseId);
        command.Parameters.AddWithValue("@Series", fixture.OnlineSalesReceiptSeriesId); command.Parameters.AddWithValue("@Product", fixture.ProductId);
        command.Parameters.AddWithValue("@User", fixture.UserId); await command.ExecuteNonQueryAsync();
        return (dispatch, source, sale);
    }
}
