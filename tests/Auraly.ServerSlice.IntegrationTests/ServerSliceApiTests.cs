using System.Net;
using System.Net.Http.Json;
using Auraly.Application.DocumentProcessing;
using Auraly.Application.Fiscal;
using Auraly.Application.Sales;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Sales;
using Auraly.Fiscal.Core;
using Auraly.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class ServerSliceApiTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task First_offline_sale_preserves_its_existing_work_session_idempotently()
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var documentSeriesId = Guid.NewGuid();
        var workSessionId = Guid.NewGuid();
        const string deviceSecret = "offline-device-secret-for-integration-test";
        var deviceCredential = PosDeviceCredentialHasher.Create(deviceSecret);
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT dbo.AppUsers
                  (UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
                   FirstName,LastName,IsActive,CreatedAt)
                VALUES
                  (@UserId,@TenantId,CONCAT(N'offline-',@UserId),UPPER(CONCAT(N'offline-',@UserId)),
                   CONCAT(@UserId,N'@test.local'),UPPER(CONCAT(@UserId,N'@test.local')),
                   N'Offline',N'Cashier',1,SYSUTCDATETIME());

                INSERT dbo.EnrolledDevices
                  (DeviceId,TenantId,Name,CredentialSalt,CredentialHash,
                   CredentialIterations,IsActive,CreatedAt)
                VALUES
                  (@DeviceId,@TenantId,CONCAT(N'Offline device ',@DeviceId),
                   @CredentialSalt,@CredentialHash,@CredentialIterations,1,SYSUTCDATETIME());

                INSERT dbo.DocumentSeries
                  (DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,
                   Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
                VALUES
                  (@DocumentSeriesId,@BusinessId,@DeviceId,N'SalesReceipt',N'CVI',N'91',
                   8,1,99999999,1,1,SYSUTCDATETIME());

                INSERT dbo.WorkSessions
                  (WorkSessionId,TenantId,BusinessId,WarehouseId,UserId,DeviceId,
                   OpenedAt,LastActivityAt,Status)
                VALUES
                  (@WorkSessionId,@TenantId,@BusinessId,@WarehouseId,@UserId,@DeviceId,
                   SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET(),N'Open');
                """;
            command.Parameters.AddWithValue("@UserId", userId);
            command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            command.Parameters.AddWithValue("@DeviceId", deviceId);
            command.Parameters.AddWithValue("@DocumentSeriesId", documentSeriesId);
            command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            command.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
            command.Parameters.AddWithValue("@WorkSessionId", workSessionId);
            command.Parameters.Add("@CredentialSalt", System.Data.SqlDbType.VarBinary, 32)
                .Value = deviceCredential.Salt;
            command.Parameters.Add("@CredentialHash", System.Data.SqlDbType.VarBinary, 32)
                .Value = deviceCredential.Hash;
            command.Parameters.AddWithValue(
                "@CredentialIterations", deviceCredential.Iterations);
            await command.ExecuteNonQueryAsync();
        }
        var baseRequest = fixture.CreateValidRequest(100);
        var request = baseRequest with
        {
            DeviceId = deviceId,
            SoldByUserId = userId,
            WorkSessionId = workSessionId,
            DocumentNumber = new PosSaleDocumentNumberContract(
                documentSeriesId,
                PosSaleDocumentTypes.Receipt,
                "CVI",
                "91",
                100,
                8,
                "CVI91-00000100"),
            CommercialSnapshot = baseRequest.CommercialSnapshot with
            {
                DocumentType = PosSaleDocumentTypes.Receipt
            },
            FiscalSnapshot = null
        };
        using var client = fixture.CreateClient();
        using var upload = fixture.CreateUploadMessage(request, deviceSecret);
        using var response = await client.SendAsync(upload);

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
        var responsibility = await fixture.GetSalesWorkResponsibilityAsync(request.DocumentId);
        Assert.Equal(userId, responsibility.SoldByUserId);
        Assert.Equal(workSessionId, responsibility.WorkSessionId);

        using var duplicate = fixture.CreateUploadMessage(request, deviceSecret);
        using var duplicateResponse = await client.SendAsync(duplicate);
        Assert.Equal(HttpStatusCode.OK, duplicateResponse.StatusCode);
        Assert.Equal(1, await fixture.CountAsync("SalesDocuments", request.DocumentId));
    }

    [Fact]
    public async Task Authenticated_concurrent_duplicate_is_processed_exactly_once()
    {
        var request = fixture.CreateValidRequest(101);
        using var client = fixture.CreateClient();
        using var firstMessage = fixture.CreateUploadMessage(request);
        using var secondMessage = fixture.CreateUploadMessage(request);

        var responses = await Task.WhenAll(
            client.SendAsync(firstMessage),
            client.SendAsync(secondMessage));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var receipts = await Task.WhenAll(
            responses.Select(response =>
                response.Content.ReadFromJsonAsync<PosSaleUploadResponse>()));
        Assert.All(receipts, receipt => Assert.NotNull(receipt));
        Assert.All(receipts, receipt =>
            Assert.Contains(receipt!.Status,
                new[] { PosSaleRemoteStatuses.FiscalVerified, PosSaleRemoteStatuses.AlreadyProcessed }));
        Assert.Single(receipts.Select(receipt => receipt!.ReceiptId).Distinct());
        Assert.Single(receipts.Select(receipt => receipt!.DocumentId).Distinct());
        Assert.Equal(1, await fixture.CountAsync("SalesDocuments", request.DocumentId));
        Assert.Equal(1, await fixture.CountAsync("SalesDocumentLines", request.DocumentId));
        Assert.Equal(1, await fixture.CountAsync("SalesPayments", request.DocumentId));
        Assert.Equal(1, await fixture.CountAsync("WorkSessionMovements", request.DocumentId));
        Assert.Equal(1, await fixture.CountAsync("InventoryMovements", request.DocumentId));
        Assert.Equal(1, await fixture.CountAsync("ServerOutboxMessages", request.DocumentId));
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*) FROM dbo.ServerOutboxMessages
                WHERE DocumentId=@DocumentId AND ProcessedAt IS NOT NULL
                  AND AttemptCount>0 AND LastError IS NULL;
                """;
            command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
            Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));

            command.CommandText = """
                UPDATE dbo.ServerOutboxMessages
                SET ProcessedAt=NULL,AttemptCount=0,LastError=N'forced recovery'
                WHERE DocumentId=@DocumentId
                  AND Type<>N'FiscalDocument.DianAccepted';
                """;
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        using (var scope = fixture.CreateScope())
        {
            var source = scope.ServiceProvider
                .GetRequiredService<IDocumentProcessingWorkSource>();
            var recovery = (await source.ListReadySignalsAsync(500, default))
                .Single(signal => signal.DocumentId == request.DocumentId);
            await fixture.DocumentSignals.PublishAsync(recovery);
        }

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*) FROM dbo.ServerOutboxMessages
                WHERE DocumentId=@DocumentId AND ProcessedAt IS NOT NULL
                  AND AttemptCount=1 AND LastError IS NULL;
                """;
            command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
            Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
        }
        Assert.Equal(1, await fixture.CountAsync("DocumentProcessingJobs", request.DocumentId));
        Assert.Equal(1, await fixture.CountAsync("FiscalDocumentProcesses", request.DocumentId));
        var responsibility = await fixture.GetSalesWorkResponsibilityAsync(request.DocumentId);
        Assert.Equal(fixture.UserId, responsibility.SoldByUserId);
        Assert.NotEqual(Guid.Empty, responsibility.WorkSessionId);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Multiple_tax_rates_remain_queryable_from_the_canonical_line_snapshot()
    {
        var request = fixture.CreateMultiRateRequest(151);
        using var client = fixture.CreateClient();
        using var upload = fixture.CreateUploadMessage(request);
        using var response = await client.SendAsync(upload);

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
        var summaries = await fixture.GetLineTaxBreakdownAsync(request.DocumentId);
        Assert.Collection(
            summaries,
            fivePercent =>
            {
                Assert.Equal("01", fivePercent.TaxCode);
                Assert.Equal(5m, fivePercent.TaxRate);
                Assert.Equal(10_000m, fivePercent.TaxableAmount);
                Assert.Equal(500m, fivePercent.TaxAmount);
                Assert.Equal(10_500m, fivePercent.TotalAmount);
            },
            nineteenPercent =>
            {
                Assert.Equal("01", nineteenPercent.TaxCode);
                Assert.Equal(19m, nineteenPercent.TaxRate);
                Assert.Equal(20_000m, nineteenPercent.TaxableAmount);
                Assert.Equal(3_800m, nineteenPercent.TaxAmount);
                Assert.Equal(23_800m, nineteenPercent.TotalAmount);
            });

        using var duplicate = fixture.CreateUploadMessage(request);
        using var duplicateResponse = await client.SendAsync(duplicate);
        Assert.Equal(HttpStatusCode.OK, duplicateResponse.StatusCode);
        Assert.Equal(2, await fixture.CountAsync("SalesDocumentLines", request.DocumentId));
    }

    [Fact]
    public async Task Fiscal_documents_are_business_scoped_authorized_and_paged()
    {
        fixture.DrainFiscalSignals();
        var request = fixture.CreateValidRequest(150);
        using (var pos = fixture.CreateClient())
        using (var upload = fixture.CreateUploadMessage(request))
        using (var response = await pos.SendAsync(upload))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var allowed = fixture.CreateAdminClient(FiscalPermissionCodes.DocumentsRead);
        using var get = await allowed.GetAsync($"/api/commerce/v1/fiscal/documents/{request.DocumentId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var document = await get.Content.ReadFromJsonAsync<FiscalDocumentView>();
        Assert.NotNull(document);
        Assert.Equal(fixture.BusinessId, document.BusinessId);
        Assert.Equal(FiscalDocumentStatusCodes.PendingGeneration, document.Status);

        using var pageResponse = await allowed.GetAsync(
            $"/api/commerce/v1/fiscal/documents?page=1&pageSize=10&status={FiscalDocumentStatusCodes.PendingGeneration}" +
            $"&auralyNumber={Uri.EscapeDataString(request.DocumentNumber.FullNumber)}");
        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
        var page = await pageResponse.Content.ReadFromJsonAsync<FiscalDocumentPage>();
        Assert.NotNull(page);
        Assert.Contains(page.Items, item => item.DocumentId == request.DocumentId);
        fixture.DrainFiscalSignals();

        using var denied = fixture.CreateAdminClient();
        using var deniedResponse = await denied.GetAsync($"/api/commerce/v1/fiscal/documents/{request.DocumentId}");
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        using var retry = fixture.CreateAdminClient(FiscalPermissionCodes.Retry);
        using var retryResponse = await retry.PostAsync(
            $"/api/commerce/v1/fiscal/documents/{request.DocumentId}/retry", null);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        var signal = Assert.Single(fixture.DrainFiscalSignals()).Signal;
        Assert.Equal(fixture.BusinessId, signal.BusinessId);
        Assert.Equal(request.DocumentId, signal.DocumentId);
        Assert.Equal(FiscalProcessingStage.Generation, signal.Stage);
        Assert.NotEqual(Guid.Empty, signal.SignalId);
    }

    [Fact]
    public async Task Dian_rejected_invoice_retry_regenerates_the_same_fiscal_document()
    {
        fixture.DrainFiscalSignals();
        var request = fixture.CreateValidRequest(152);
        using (var pos = fixture.CreateClient())
        using (var upload = fixture.CreateUploadMessage(request))
        using (var response = await pos.SendAsync(upload))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        fixture.DrainFiscalSignals();

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE dbo.FiscalDocumentProcesses
                SET Status=N'DianRejected',TrackId=N'rejected-track',
                    CorrelationId=N'rejected-correlation',CompletedAt=SYSDATETIMEOFFSET()
                WHERE DocumentId=@DocumentId;
                UPDATE dbo.FiscalDocuments SET FiscalStatus=N'DianRejected'
                WHERE DocumentId=@DocumentId;
                UPDATE dbo.SalesDocuments SET FiscalStatus=N'DianRejected'
                WHERE DocumentId=@DocumentId;
                """;
            command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
            Assert.Equal(3, await command.ExecuteNonQueryAsync());
        }

        using var retry = fixture.CreateAdminClient(FiscalPermissionCodes.Retry);
        using var retryResponse = await retry.PostAsync(
            $"/api/commerce/v1/fiscal/documents/{request.DocumentId}/retry", null);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        var retried = await retryResponse.Content.ReadFromJsonAsync<FiscalDocumentView>();
        Assert.NotNull(retried);
        Assert.Equal(FiscalDocumentStatusCodes.PendingGeneration, retried.Status);
        Assert.Equal(request.FiscalSnapshot!.FiscalNumber, retried.DianNumber);

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*)
                FROM dbo.FiscalDocumentProcesses process
                JOIN dbo.FiscalDocuments fiscal ON fiscal.DocumentId=process.DocumentId
                WHERE process.DocumentId=@DocumentId
                  AND process.Status=N'PendingGeneration'
                  AND process.TrackId IS NULL
                  AND process.CorrelationId IS NULL
                  AND process.CompletedAt IS NULL
                  AND fiscal.FiscalNumber=@FiscalNumber;
                """;
            command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
            command.Parameters.AddWithValue("@FiscalNumber", request.FiscalSnapshot.FiscalNumber);
            Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
        }
        Assert.Equal(1, await fixture.CountAsync("SalesDocuments", request.DocumentId));
        Assert.Equal(FiscalProcessingStage.Generation,
            Assert.Single(fixture.DrainFiscalSignals()).Signal.Stage);
    }

    [Fact]
    public async Task Device_authentication_and_authenticated_context_are_enforced()
    {
        using var client = fixture.CreateClient();
        var unauthenticated = fixture.CreateValidRequest(102);
        using var noCredentials = fixture.CreateUploadMessage(unauthenticated, secret: null);
        using var noCredentialsResponse = await client.SendAsync(noCredentials);
        Assert.Equal(HttpStatusCode.Unauthorized, noCredentialsResponse.StatusCode);

        var wrongTenant = fixture.CreateValidRequest(104) with { TenantId = Guid.NewGuid() };
        using var wrongTenantMessage = fixture.CreateUploadMessage(wrongTenant);
        using var wrongTenantResponse = await client.SendAsync(wrongTenantMessage);
        Assert.Equal(HttpStatusCode.Forbidden, wrongTenantResponse.StatusCode);
    }

    [Fact]
    public async Task Altered_Auraly_document_number_is_rejected_before_persistence()
    {
        var original = fixture.CreateValidRequest(105);
        var changed = original with
        {
            DocumentNumber = original.DocumentNumber with
            {
                FullNumber = $"{original.DocumentNumber.FullNumber}X"
            }
        };
        using var client = fixture.CreateClient();
        using var message = fixture.CreateUploadMessage(changed);
        using var response = await client.SendAsync(message);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await fixture.CountAsync("SalesDocuments", original.DocumentId));
    }

    public static TheoryData<string> FiscalMutations => new()
    {
        "number",
        "date",
        "customer",
        "tax",
        "total",
        "prefix",
        "authorization"
    };

    [Theory]
    [MemberData(nameof(FiscalMutations))]
    public async Task Every_fiscal_mutation_creates_conflict_without_effects(string mutation)
    {
        var consecutive = 200 + mutation switch
        {
            "number" => 1, "date" => 2, "customer" => 3,
            "tax" => 4, "total" => 5, "prefix" => 6, "authorization" => 7,
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        var original = fixture.CreateValidRequest(consecutive);
        var changed = Mutate(original, mutation);
        using var client = fixture.CreateClient();
        using var message = fixture.CreateUploadMessage(changed);
        using var response = await client.SendAsync(message);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var receipt = await response.Content.ReadFromJsonAsync<PosSaleUploadResponse>();
        Assert.NotNull(receipt);
        Assert.Equal(PosSaleRemoteStatuses.FiscalIntegrityConflict, receipt.Status);
        Assert.Equal(original.FiscalSnapshot!.Cufe, receipt.CufeReceived);
        Assert.Equal(1, await fixture.CountAsync("SalesDocuments", original.DocumentId));
        Assert.Equal(1, await fixture.CountAsync("FiscalSnapshots", original.DocumentId));
        Assert.Equal(0, await fixture.CountAsync("SalesPayments", original.DocumentId));
        Assert.Equal(0, await fixture.CountAsync("InventoryMovements", original.DocumentId));
        Assert.Equal(0, await fixture.CountAsync("ServerOutboxMessages", original.DocumentId));
        Assert.Equal(1, await fixture.CountAsync("FiscalDocumentProcesses", original.DocumentId));
    }

    [Fact]
    public async Task A_previously_blocked_valid_snapshot_is_reverified_and_enqueued_idempotently()
    {
        var request = fixture.CreateValidRequest(7_123);
        var idempotencyKey = $"recover-integrity-{request.DocumentId:N}";
        using var scope = fixture.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IPosSaleServerStore>();
        var verifier = scope.ServiceProvider.GetRequiredService<IFiscalSnapshotVerifier>();
        var receiver = scope.ServiceProvider.GetRequiredService<ReceivePosSaleService>();
        var validVerification = await verifier.VerifyAsync(request, CancellationToken.None);
        Assert.True(validVerification.IsVerified, validVerification.ConflictReason);

        var snapshotJson = PosSaleContractSerializer.Serialize(request);
        var payloadHash = PosSaleContractSerializer.Hash(request);
        var blocked = await store.StoreReceptionAsync(
            new StorePosSaleReceptionCommand(
                request,
                idempotencyKey,
                snapshotJson,
                payloadHash,
                validVerification with
                {
                    IsVerified = false,
                    ConflictReason = "Simulated verifier defect"
                },
                DateTimeOffset.UtcNow),
            CancellationToken.None);
        Assert.Equal(PosSaleRemoteStatuses.FiscalIntegrityConflict, blocked.FiscalStatus);
        Assert.Equal("Blocked", blocked.ProcessingStatus);

        var recovered = await receiver.ReceiveAsync(
            new PosDeviceIdentity(fixture.DeviceId, fixture.TenantId),
            idempotencyKey,
            request,
            CancellationToken.None);

        Assert.NotEqual(PosSaleRemoteStatuses.FiscalIntegrityConflict, recovered.Status);
        Assert.Equal(1, await fixture.CountAsync("DocumentProcessingJobs", request.DocumentId));
        Assert.Equal(1, await fixture.CountAsync("DocumentProcessingPayloads", request.DocumentId));
        Assert.Equal(1, await fixture.CountAsync("FiscalSnapshots", request.DocumentId));

        await receiver.ReceiveAsync(
            new PosDeviceIdentity(fixture.DeviceId, fixture.TenantId),
            idempotencyKey,
            request,
            CancellationToken.None);
        Assert.Equal(1, await fixture.CountAsync("DocumentProcessingJobs", request.DocumentId));
        Assert.Equal(1, await fixture.CountAsync("DocumentProcessingPayloads", request.DocumentId));
    }

    [Fact]
    public async Task Fiscal_retry_recovers_a_previously_blocked_valid_snapshot_through_the_canonical_ingress()
    {
        fixture.DrainDocumentSignals();
        var request = fixture.CreateValidRequest(7_125) with
        {
            DeviceId = Guid.Empty,
            SourceMode = SaleSourceModes.Online
        };
        var idempotencyKey = $"recover-integrity-{request.DocumentId:N}";
        using (var scope = fixture.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IPosSaleServerStore>();
            var verifier = scope.ServiceProvider.GetRequiredService<IFiscalSnapshotVerifier>();
            var validVerification = await verifier.VerifyAsync(request, CancellationToken.None);
            Assert.True(validVerification.IsVerified, validVerification.ConflictReason);
            await store.StoreReceptionAsync(
                new StorePosSaleReceptionCommand(
                    request,
                    idempotencyKey,
                    PosSaleContractSerializer.Serialize(request),
                    PosSaleContractSerializer.Hash(request),
                    validVerification with
                    {
                        IsVerified = false,
                        ConflictReason = "Simulated verifier defect"
                    },
                    DateTimeOffset.UtcNow),
                CancellationToken.None);
        }

        using var client = fixture.CreateAdminClient(FiscalPermissionCodes.Retry);
        using var response = await client.PostAsync(
            $"/api/commerce/v1/fiscal/documents/{request.DocumentId}/retry", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var recovered = await response.Content.ReadFromJsonAsync<FiscalDocumentView>();
        Assert.NotNull(recovered);
        Assert.Equal(FiscalDocumentStatusCodes.PendingGeneration, recovered.Status);
        Assert.Equal(1, await fixture.CountAsync("DocumentProcessingJobs", request.DocumentId));
        Assert.Equal(1, await fixture.CountAsync("DocumentProcessingPayloads", request.DocumentId));
        var signal = Assert.Single(fixture.DrainDocumentSignals());
        Assert.Equal(request.DocumentId, signal.DocumentId);
        Assert.Equal(request.BusinessId, signal.BusinessId);
    }

    [Fact]
    public async Task Fiscal_verifier_accepts_a_frozen_line_when_document_totals_are_consistent()
    {
        var request = fixture.CreateValidRequest(7_124);
        var snapshot = request.FiscalSnapshot
            ?? throw new InvalidOperationException("The test requires a fiscal snapshot.");
        const decimal untaxed = .02m;
        const decimal tax = 0m;
        const decimal payable = .02m;
        var line = request.Lines.Single() with
        {
            Quantity = 1m,
            UnitPrice = .025m,
            DiscountAmount = 0m,
            TaxAmount = tax,
            UntaxedAmount = untaxed,
            LineTotal = payable,
            TaxRate = 0m
        };
        var calculated = CufeCalculator.Calculate(
            new CufeInput(
                snapshot.FiscalNumber,
                snapshot.IssuedAt,
                untaxed,
                payable,
                ServerSliceFixture.SupplierTaxId,
                snapshot.CustomerIdentification,
                new FiscalTechnicalKey(
                    ServerSliceFixture.TechnicalKeyValue,
                    ServerSliceFixture.TechnicalKeyVersion),
                FiscalEnvironment.Test,
                [new FiscalTaxAmount(line.TaxCode, tax)]),
            ServerSliceFixture.QrValidationUrl);
        request = request with
        {
            Lines = [line],
            Payments = [new PosSalePaymentContract(1, "Cash", payable, null)],
            CommercialSnapshot = request.CommercialSnapshot with
            {
                Taxes = [new PosSaleTaxContract(line.TaxCode, tax)],
                UntaxedAmount = untaxed,
                TaxAmount = tax,
                PayableAmount = payable
            },
            FiscalSnapshot = snapshot with
            {
                Taxes = [new PosSaleTaxContract(line.TaxCode, tax)],
                UntaxedAmount = untaxed,
                TaxAmount = tax,
                PayableAmount = payable,
                Cufe = calculated.Cufe,
                QrPayload = calculated.QrPayload
            }
        };

        using var scope = fixture.CreateScope();
        var verifier = scope.ServiceProvider.GetRequiredService<IFiscalSnapshotVerifier>();

        var verification = await verifier.VerifyAsync(request, CancellationToken.None);

        Assert.True(verification.IsVerified, verification.ConflictReason);
        Assert.Equal(calculated.Cufe, verification.CufeCalculated);
    }

    private static PosSaleUploadRequest Mutate(PosSaleUploadRequest request, string mutation)
    {
        var snapshot = request.FiscalSnapshot ?? throw new InvalidOperationException("The mutation requires a fiscal invoice snapshot.");
        var line = request.Lines.Single();
        return mutation switch
        {
            "number" => request with
            {
                FiscalSnapshot = snapshot with { FiscalNumber = $"{snapshot.FiscalNumber}X" }
            },
            "date" => request with
            {
                FiscalSnapshot = snapshot with { IssuedAt = snapshot.IssuedAt.AddMinutes(1) }
            },
            "customer" => request with
            {
                FiscalSnapshot = snapshot with { CustomerIdentification = "999999999" }
            },
            "tax" => request with
            {
                Lines = [line with { TaxAmount = line.TaxAmount + 1 }]
            },
            "total" => request with
            {
                FiscalSnapshot = snapshot with { PayableAmount = snapshot.PayableAmount + 1 }
            },
            "prefix" => request with
            {
                FiscalSnapshot = snapshot with { Prefix = "ZZ" }
            },
            "authorization" => request with
            {
                FiscalSnapshot = snapshot with { AuthorizationNumber = "18760000999" }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
    }
}

