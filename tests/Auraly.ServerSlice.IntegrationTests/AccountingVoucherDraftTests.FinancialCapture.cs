using System.Net;
using System.Net.Http.Json;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class AccountingVoucherDraftTests
{
    [Fact]
    public async Task Financial_voucher_requires_a_main_party_to_post_but_can_be_saved_incomplete()
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend);
        using (var defaults = await client.PutAsJsonAsync("/api/commerce/v1/accounting/defaults", new { }))
            Assert.True(defaults.IsSuccessStatusCode, await defaults.Content.ReadAsStringAsync());
        using (var activation = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026, 1, 1), "COP", "ZeroDeclared")))
            Assert.True(activation.IsSuccessStatusCode, await activation.Content.ReadAsStringAsync());
        var accounts = (await client.GetFromJsonAsync<AccountingAccountView[]>("/api/commerce/v1/accounting/accounts"))!;
        var expense = accounts.Single(account => account.Code == "519595");
        var cash = accounts.Single(account => account.Code == "110505");
        var id = Guid.NewGuid();
        var request = new SaveVoucherDraftRequest(id, AccountingManualDocumentTypes.ManualVoucher,
            new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero), "MANUAL_VOUCHER", "Comprobante incompleto", null,
            [new(expense.AccountId, null, null, "Gasto", 100, 0, LineId: Guid.NewGuid()),
             new(cash.AccountId, null, null, "Caja", 0, 100, LineId: Guid.NewGuid())],
            null, null, PayloadVersion: 4, VoucherTypeCode: "Accounting");
        var path = $"/api/commerce/v1/accounting/manual/drafts/{id:D}";
        using var save = await client.PutAsJsonAsync(path, request);
        Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());
        var saved = (await save.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        using var send = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion));
        Assert.Equal(HttpStatusCode.BadRequest, send.StatusCode);
        Assert.Contains("tercero principal", await send.Content.ReadAsStringAsync());
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var effects = new SqlCommand("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id", connection);
        effects.Parameters.AddWithValue("@Id", id);
        Assert.Equal(0, (int)(await effects.ExecuteScalarAsync())!);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(2)]
    public async Task Legacy_capture_cannot_bypass_the_financial_engine_for_a_classified_cash_account(int? payloadVersion)
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend);
        using (var defaults = await client.PutAsJsonAsync("/api/commerce/v1/accounting/defaults", new { }))
            Assert.True(defaults.IsSuccessStatusCode, await defaults.Content.ReadAsStringAsync());
        using (var activation = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026, 1, 1), "COP", "ZeroDeclared")))
            Assert.True(activation.IsSuccessStatusCode, await activation.Content.ReadAsStringAsync());
        var cash = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture, client);
        var accounts = (await client.GetFromJsonAsync<AccountingAccountView[]>("/api/commerce/v1/accounting/accounts"))!;
        var expense = accounts.Single(account => account.Code == "519595");
        var id = Guid.NewGuid();
        var request = new SaveVoucherDraftRequest(id, AccountingManualDocumentTypes.ManualVoucher,
            new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero), "MANUAL_VOUCHER", "Caja sin efecto vinculado", null,
            [new(expense.AccountId, fixture.SupplierPartyId, null, "Gasto", 100, 0,
                LineId: payloadVersion == 2 ? Guid.NewGuid() : null),
             new(cash.AccountId, null, null, "Salida de caja", 0, 100,
                LineId: payloadVersion == 2 ? Guid.NewGuid() : null)], null, null, PayloadVersion: payloadVersion);
        var path = $"/api/commerce/v1/accounting/manual/drafts/{id}";
        using var saving = await client.PutAsJsonAsync(path, request);
        Assert.True(saving.IsSuccessStatusCode, await saving.Content.ReadAsStringAsync());
        var saved = (await saving.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        using var sending = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion));
        Assert.Equal(HttpStatusCode.BadRequest, sending.StatusCode);
        var rejection = await sending.Content.ReadAsStringAsync();
        Assert.Contains("captura financiera", rejection);
        Assert.Contains("no se contabilizó ni se registró un pago", rejection);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var effects = new SqlCommand("""
            SELECT (SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id)
              +(SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id)
              +(SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id)
              +(SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE DocumentId=@Id);
            """, connection);
        effects.Parameters.AddWithValue("@Id", id);
        Assert.Equal(0, (int)(await effects.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Financial_capture_reopens_its_links_and_evidence_without_reserving_and_clone_clears_consumable_references()
    {
        using var client=fixture.CreateAdminClient(AccountingPermissionCodes.Read,AccountingPermissionCodes.ManualCreate);
        var accounts=(await client.GetFromJsonAsync<AccountingAccountView[]>("/api/commerce/v1/accounting/accounts"))!;
        var control=accounts.Single(account=>account.Code=="220505");
        var bank=accounts.Single(account=>account.Code=="111005");
        var id=Guid.NewGuid();var copyId=Guid.NewGuid();var positionId=Guid.NewGuid();var originId=Guid.NewGuid();
        await using var connection=new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_",connection.Database);
        Guid siteId;
        await using(var site=new SqlCommand("SELECT PartySiteId FROM dbo.PartySites WHERE PartyId=@PartyId AND IsPrimary=1 AND IsActive=1;",connection))
        {
            site.Parameters.AddWithValue("@PartyId",fixture.SupplierPartyId);
            siteId=(Guid)(await site.ExecuteScalarAsync()??throw new InvalidOperationException("The supplier has no primary site."));
        }
        try
        {
            // A real shape of a commercial-only historical obligation. Saving
            // its reference must not reconcile, settle or invent its opening GL.
            await using(var seed=new SqlCommand("""
                SET XACT_ABORT ON; BEGIN TRANSACTION;
                DECLARE @Hash binary(32)=HASHBYTES('SHA2_256',CONVERT(varbinary(max),'{}'));
                INSERT dbo.AccountingSourceDocuments(SourceDocumentId,SourceDocumentType,TenantId,BusinessId,PayloadJson,
                  PayloadHash,OccurredAt,AcceptedAt,AccountingEntryRequired)
                VALUES(@Origin,N'FinancialCaptureTest',@TenantId,@BusinessId,N'{}',@Hash,'2025-12-31T12:00:00+00:00',SYSUTCDATETIME(),0);
                INSERT dbo.AccountingPostingJobs(AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,
                  SourcePayloadHash,OccurredAt,AccountingEntryRequired,Status,AttemptCount,CreatedAt,CompletedAt)
                VALUES(NEWID(),@TenantId,@BusinessId,@Origin,N'FinancialCaptureTest',@Hash,'2025-12-31T12:00:00+00:00',0,
                  N'CommercialEffectsApplied',1,SYSUTCDATETIME(),SYSUTCDATETIME());
                INSERT dbo.Payables(PayableId,BusinessId,SupplierId,PartySiteId,SourceDocumentId,SourceDocumentType,
                  DocumentNumber,CurrencyCode,OriginalAmount,OutstandingAmount,DueDate,Status,CreatedAt)
                VALUES(@Position,@BusinessId,@SupplierId,@Site,@Origin,N'FinancialCaptureTest',N'CAP-2025',N'COP',100,100,
                  '2026-09-30T12:00:00+00:00',N'Open',SYSUTCDATETIME());
                INSERT dbo.PayableTransactions(PayableTransactionId,PayableId,TransactionType,Amount,SourceDocumentId,OccurredAt,CreatedAt)
                VALUES(NEWID(),@Position,N'Opening',100,@Origin,'2025-12-31T12:00:00+00:00',SYSUTCDATETIME());
                COMMIT TRANSACTION;
                """,connection))
            {
                seed.Parameters.AddWithValue("@Origin",originId);seed.Parameters.AddWithValue("@Position",positionId);
                seed.Parameters.AddWithValue("@TenantId",fixture.TenantId);seed.Parameters.AddWithValue("@BusinessId",fixture.BusinessId);
                seed.Parameters.AddWithValue("@SupplierId",fixture.SupplierId);seed.Parameters.AddWithValue("@Site",siteId);
                await seed.ExecuteNonQueryAsync();
            }
            var groupId=Guid.NewGuid();
            var evidence=new VoucherPaymentEvidence("VISA","APROBACION-1",100m,"Evidencia de la captura");
            var input=new SaveVoucherDraftRequest(id,AccountingManualDocumentTypes.ManualVoucher,
                new(2026,10,23,12,0,0,TimeSpan.Zero),"MANUAL_VOUCHER","Captura financiera","REF-CAPTURA",[
                    new(control.AccountId,fixture.SupplierPartyId,null,"Abono por preparar",100,0,"CAP-2025",Guid.NewGuid(),
                        ExistingPosition:new("Payable",positionId),FinancialGroupId:groupId,DueDate:new(2026,9,30,12,0,0,TimeSpan.Zero),PartySiteId:siteId),
                    new(bank.AccountId,fixture.SupplierPartyId,null,"Tesorería por preparar",0,100,"REF-MEDIO",Guid.NewGuid(),
                        FinancialGroupId:groupId,PaymentEvidence:evidence)],null,null,
                MainPartyId:fixture.SupplierPartyId,PayloadVersion:4,VoucherTypeCode:"Disbursement");
            var path=$"/api/commerce/v1/accounting/manual/drafts/{id:D}";
            using(var rejectedLegacy=await client.PutAsJsonAsync(path,input with{PayloadVersion=2,VoucherTypeCode=null}))
                Assert.Equal(HttpStatusCode.BadRequest,rejectedLegacy.StatusCode);
            using var savedResponse=await client.PutAsJsonAsync(path,input);
            Assert.True(savedResponse.IsSuccessStatusCode,await savedResponse.Content.ReadAsStringAsync());
            var saved=(await savedResponse.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            var reopened=(await client.GetFromJsonAsync<VoucherDraftView>(path))!;
            Assert.Equal("Disbursement",reopened.VoucherTypeCode);
            Assert.Equal(4,reopened.PayloadVersion);
            Assert.Null(reopened.SentAt);
            Assert.Null(reopened.FinancialEffects);
            Assert.Equal(input.Lines,reopened.Lines.Select(line=>line.Value));
            using(var replayResponse=await client.PutAsJsonAsync(path,input))
            {
                Assert.True(replayResponse.IsSuccessStatusCode,await replayResponse.Content.ReadAsStringAsync());
                Assert.Equal(saved.RowVersion,(await replayResponse.Content.ReadFromJsonAsync<VoucherDraftView>())!.RowVersion);
            }
            using(var wrongTarget=await client.PutAsJsonAsync(path,input with{RowVersion=saved.RowVersion,
                Lines=[input.Lines[0] with{ExistingPosition=new("Payable",Guid.NewGuid())},input.Lines[1]]}))
                Assert.Equal(HttpStatusCode.BadRequest,wrongTarget.StatusCode);
            using(var copied=await client.PostAsJsonAsync($"/api/commerce/v1/accounting/documents/{id:D}/clone",
                new CloneVoucherDraftRequest(copyId,new(2026,11,1,12,0,0,TimeSpan.Zero))))
            {
                Assert.True(copied.IsSuccessStatusCode,await copied.Content.ReadAsStringAsync());
                var copy=(await copied.Content.ReadFromJsonAsync<VoucherDraftView>())!;
                Assert.Equal(4,copy.PayloadVersion);Assert.Equal("Disbursement",copy.VoucherTypeCode);
                Assert.Empty(copy.Lines.Select(line=>line.Value.LineId).Intersect(saved.Lines.Select(line=>line.Value.LineId)));
                Assert.All(copy.Lines,line=>{Assert.Null(line.Value.ExistingPosition);Assert.Null(line.Value.FinancialGroupId);
                    Assert.Null(line.Value.PaymentEvidence);Assert.Null(line.Value.DueDate);Assert.Null(line.Value.Reference);});
            }
            await using var unchanged=new SqlCommand("""
                SELECT (SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId IN(@Id,@Copy)),
                  (SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId IN(@Id,@Copy)),
                  (SELECT COUNT(*) FROM dbo.SupplierPaymentApplications WHERE PayableId=@Position),
                  OutstandingAmount FROM dbo.Payables WHERE PayableId=@Position;
                """,connection);
            unchanged.Parameters.AddWithValue("@Id",id);unchanged.Parameters.AddWithValue("@Copy",copyId);unchanged.Parameters.AddWithValue("@Position",positionId);
            await using var reader=await unchanged.ExecuteReaderAsync();await reader.ReadAsync();
            Assert.Equal(0,reader.GetInt32(0));Assert.Equal(0,reader.GetInt32(1));Assert.Equal(0,reader.GetInt32(2));Assert.Equal(100m,reader.GetDecimal(3));
        }
        finally
        {
            await using var cleanup=new SqlCommand("""
                DELETE accounting.VoucherDraftLines WHERE DocumentId IN(@Id,@Copy);
                DELETE accounting.VoucherDrafts WHERE DocumentId IN(@Id,@Copy) AND BusinessId=@BusinessId;
                DELETE dbo.PayableTransactions WHERE PayableId=@Position;
                DELETE dbo.Payables WHERE PayableId=@Position AND BusinessId=@BusinessId;
                DELETE dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Origin AND TenantId=@TenantId;
                DELETE dbo.AccountingPostingJobs WHERE SourceDocumentId=@Origin AND TenantId=@TenantId;
                """,connection);
            cleanup.Parameters.AddWithValue("@Id",id);cleanup.Parameters.AddWithValue("@Copy",copyId);cleanup.Parameters.AddWithValue("@Position",positionId);
            cleanup.Parameters.AddWithValue("@Origin",originId);cleanup.Parameters.AddWithValue("@BusinessId",fixture.BusinessId);cleanup.Parameters.AddWithValue("@TenantId",fixture.TenantId);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
