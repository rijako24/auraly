using System.Security.Claims;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;

namespace Auraly.Api;

public static class AccountingApi
{
    public static IEndpointRouteBuilder MapAccountingApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/commerce/v1/accounting/readiness", async (HttpContext context, DateOnly? effectiveFrom, string? openingBalanceMode, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.GetReadinessAsync(context.User.ToAccountingIdentity(), effectiveFrom, openingBalanceMode, token), Results.Ok)).RequireAuthorization("accounting.user");
        endpoints.MapPost("/api/commerce/v1/accounting/activate", async (HttpContext context, ActivateAccountingRequest request, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.ActivateAsync(context.User.ToAccountingIdentity(), request, token), Results.Ok)).RequireAuthorization("accounting.user");
        endpoints.MapGet("/api/commerce/v1/accounting/opening-balances", async (HttpContext context, DateOnly effectiveOn, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.GetOpeningBalanceAsync(context.User.ToAccountingIdentity(), effectiveOn, token), Results.Ok)).RequireAuthorization("accounting.user");
        endpoints.MapPut("/api/commerce/v1/accounting/opening-balances", async (HttpContext context, SaveAccountingOpeningBalanceRequest request, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.SaveOpeningBalanceAsync(context.User.ToAccountingIdentity(), request, token), Results.Ok)).RequireAuthorization("accounting.user");
        endpoints.MapPost("/api/commerce/v1/accounting/opening-balances/{batchId:guid}/approve", async (HttpContext context, Guid batchId, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.ApproveOpeningBalanceAsync(context.User.ToAccountingIdentity(), batchId, token), Results.Ok)).RequireAuthorization("accounting.user");
        endpoints.MapPost("/api/commerce/v1/accounting/manual/account-adjustments", async (HttpContext context, ConfirmAccountAdjustmentRequest request, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.ConfirmAccountAdjustmentAsync(context.User.ToAccountingIdentity(), request, token), value => Results.Accepted($"/api/commerce/v1/accounting/entries/by-document/{value.DocumentId:D}", value))).RequireAuthorization("accounting.user");
        endpoints.MapPost("/api/commerce/v1/accounting/manual/vouchers", async (HttpContext context, ConfirmManualAccountingVoucherRequest request, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.ConfirmManualVoucherAsync(context.User.ToAccountingIdentity(), request, token), value => Results.Accepted($"/api/commerce/v1/accounting/entries/by-document/{value.DocumentId:D}", value))).RequireAuthorization("accounting.user");
        endpoints.MapGet("/api/commerce/v1/accounting/accounts", async (HttpContext context, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.ListAccountsAsync(context.User.ToAccountingIdentity(), token), Results.Ok)).RequireAuthorization("accounting.user");
        endpoints.MapGet("/api/commerce/v1/accounting/bank-accounts", async (HttpContext context, bool? includeInactive, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.ListBankAccountsAsync(context.User.ToAccountingIdentity(), includeInactive == true, token), Results.Ok)).RequireAuthorization("accounting.user");
        endpoints.MapGet("/api/pos/v1/accounting/settlement-configuration", async (HttpContext context, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.GetPosSettlementConfigurationAsync(
                context.User.ToPosDeviceIdentity().TenantId, token), Results.Ok)).RequireAuthorization("pos.enrolled");
        endpoints.MapGet("/api/commerce/v1/pos/settlement-configuration", async (HttpContext context, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.GetPosSettlementConfigurationAsync(
                context.User.ToAccountingIdentity().TenantId, token), Results.Ok)).RequireAuthorization();
        endpoints.MapPut("/api/commerce/v1/accounting/bank-accounts/{bankAccountId:guid}", async (HttpContext context, Guid bankAccountId, SaveBankAccountRequest request, AccountingService service, CancellationToken token) =>
            bankAccountId != request.BankAccountId
                ? Results.Problem("The route and payload bank account IDs differ.", statusCode: 400)
                : await ExecuteAsync(() => service.SaveBankAccountAsync(context.User.ToAccountingIdentity(), request, token), Results.Ok)).RequireAuthorization("accounting.user");
        endpoints.MapGet("/api/commerce/v1/accounting/cost-centers", async (HttpContext context, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.ListCostCentersAsync(context.User.ToAccountingIdentity(), token), Results.Ok)).RequireAuthorization("accounting.user");
        endpoints.MapGet("/api/commerce/v1/accounting/periods", async (HttpContext context, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.ListPeriodsAsync(context.User.ToAccountingIdentity(), token), Results.Ok)).RequireAuthorization("accounting.user");
        endpoints.MapGet("/api/commerce/v1/accounting/account-mappings", async (HttpContext context, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.ListMappingsAsync(context.User.ToAccountingIdentity(), token), Results.Ok)).RequireAuthorization("accounting.user");
        endpoints.MapGet("/api/commerce/v1/accounting/category-definitions", async (HttpContext context, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.ListCategoryDefinitionsAsync(context.User.ToAccountingIdentity(), token), Results.Ok)).RequireAuthorization("accounting.user");
        endpoints.MapPut("/api/commerce/v1/accounting/defaults", async (HttpContext context, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.EnsureDefaultsAsync(context.User.ToAccountingIdentity(), token), Results.Ok)).RequireAuthorization("accounting.user");
        endpoints.MapPost("/api/commerce/v1/accounting/accounts", async (HttpContext context, CreateAccountingAccountRequest request, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.CreateAccountAsync(context.User.ToAccountingIdentity(), request, token), value => Results.Created($"/api/commerce/v1/accounting/accounts/{value.AccountId:D}", value))).RequireAuthorization("accounting.user");
        endpoints.MapPost("/api/commerce/v1/accounting/cost-centers", async (HttpContext context, CreateCostCenterRequest request, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.CreateCostCenterAsync(context.User.ToAccountingIdentity(), request, token), value => Results.Created($"/api/commerce/v1/accounting/cost-centers/{value.CostCenterId:D}", value))).RequireAuthorization("accounting.user");
        endpoints.MapPost("/api/commerce/v1/accounting/periods", async (HttpContext context, CreateAccountingPeriodRequest request, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.CreatePeriodAsync(context.User.ToAccountingIdentity(), request, token), value => Results.Created($"/api/commerce/v1/accounting/periods/{value.PeriodId:D}", value))).RequireAuthorization("accounting.user");
        endpoints.MapPut("/api/commerce/v1/accounting/account-mappings", async (HttpContext context, SetAccountMappingRequest request, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(async () =>
            {
                await service.SetMappingAsync(context.User.ToAccountingIdentity(), request, token);
                return true;
            }, _ => Results.NoContent())).RequireAuthorization("accounting.user");
        endpoints.MapPost("/api/commerce/v1/accounting/periods/{periodId:guid}/close", async (HttpContext context, Guid periodId, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(async () =>
            {
                await service.ClosePeriodAsync(context.User.ToAccountingIdentity(), periodId, token);
                return true;
            }, _ => Results.NoContent())).RequireAuthorization("accounting.user");
        endpoints.MapPost("/api/commerce/v1/accounting/postings/{documentId:guid}/retry", async (HttpContext context, Guid documentId, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(async () =>
            {
                var value = await service.RetryPostingAsync(context.User.ToAccountingIdentity(), documentId, token);
                return value is null ? Results.NotFound() : Results.Ok(value);
            })).RequireAuthorization("accounting.user");
        endpoints.MapGet("/api/commerce/v1/accounting/entries/by-document/{documentId:guid}", async (HttpContext context, Guid documentId, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(async () =>
            {
                var value = await service.GetEntryAsync(context.User.ToAccountingIdentity(), documentId, token);
                return value is null ? Results.NotFound() : Results.Ok(value);
            })).RequireAuthorization("accounting.user");
        endpoints.MapGet("/api/commerce/v1/accounting/reports/trial-balance", async (HttpContext context, DateOnly from, DateOnly to, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.GetTrialBalanceAsync(context.User.ToAccountingIdentity(), from, to, token), Results.Ok)).RequireAuthorization("accounting.user");
        endpoints.MapGet("/api/commerce/v1/accounting/reports/account-movements", async (HttpContext context, string accountCode, DateOnly from, DateOnly to, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.GetAccountMovementsAsync(context.User.ToAccountingIdentity(), accountCode, from, to, token), Results.Ok)).RequireAuthorization("accounting.user");
        endpoints.MapGet("/api/commerce/v1/accounting/reports/journal", async (HttpContext context, DateOnly from, DateOnly to, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.GetJournalAsync(context.User.ToAccountingIdentity(), from, to, token), Results.Ok)).RequireAuthorization("accounting.user");
        endpoints.MapGet("/api/commerce/v1/accounting/reports/general-ledger", async (HttpContext context, DateOnly from, DateOnly to, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.GetGeneralLedgerAsync(context.User.ToAccountingIdentity(), from, to, token), Results.Ok)).RequireAuthorization("accounting.user");
        endpoints.MapGet("/api/commerce/v1/accounting/reports/balance-sheet", async (HttpContext context, DateOnly asOf, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.GetBalanceSheetAsync(context.User.ToAccountingIdentity(), asOf, token), Results.Ok)).RequireAuthorization("accounting.user");
        endpoints.MapGet("/api/commerce/v1/accounting/reports/income-statement", async (HttpContext context, DateOnly from, DateOnly to, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.GetIncomeStatementAsync(context.User.ToAccountingIdentity(), from, to, token), Results.Ok)).RequireAuthorization("accounting.user");
        endpoints.MapGet("/api/commerce/v1/accounting/reports/exceptions", async (HttpContext context, DateOnly from, DateOnly to, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.GetExceptionsAsync(context.User.ToAccountingIdentity(), from, to, token), Results.Ok)).RequireAuthorization("accounting.user");
        endpoints.MapGet("/api/commerce/v1/accounting/compliance/definitions", async (HttpContext context, short? taxYear, ComplianceReportingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.ListDefinitionsAsync(context.User.ToAccountingIdentity(), taxYear, token), Results.Ok)).RequireAuthorization("accounting.user");
        endpoints.MapGet("/api/commerce/v1/accounting/compliance/mappings", async (HttpContext context, short taxYear, string? formatCode, ComplianceReportingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.ListMappingsAsync(context.User.ToAccountingIdentity(), taxYear, formatCode, token), Results.Ok)).RequireAuthorization("accounting.user");
        endpoints.MapPut("/api/commerce/v1/accounting/compliance/mappings", async (HttpContext context, SetComplianceConceptMappingRequest request, ComplianceReportingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.SetMappingAsync(context.User.ToAccountingIdentity(), request, token), Results.Ok)).RequireAuthorization("accounting.user");
        endpoints.MapPost("/api/commerce/v1/accounting/compliance/runs", async (HttpContext context, GenerateComplianceReportRequest request, ComplianceReportingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.GenerateAsync(context.User.ToAccountingIdentity(), request, token), value => Results.Created($"/api/commerce/v1/accounting/compliance/runs/{value.RunId:D}", value))).RequireAuthorization("accounting.user");
        endpoints.MapGet("/api/commerce/v1/accounting/compliance/runs", async (HttpContext context, short? taxYear, ComplianceReportingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.ListRunsAsync(context.User.ToAccountingIdentity(), taxYear, token), Results.Ok)).RequireAuthorization("accounting.user");
        endpoints.MapGet("/api/commerce/v1/accounting/compliance/runs/{runId:guid}/artifact", async (HttpContext context, Guid runId, ComplianceReportingService service, CancellationToken token) =>
            await ExecuteAsync(async () =>
            {
                var artifact = await service.GetArtifactAsync(context.User.ToAccountingIdentity(), runId, token);
                return artifact is null ? Results.NotFound() : Results.File(artifact.Content, artifact.MediaType, artifact.FileName);
            })).RequireAuthorization("accounting.user");
        return endpoints;
    }

    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (AccountingForbiddenException exception) { return Results.Problem(exception.Message, statusCode: 403); }
        catch (AccountingValidationException exception) { return Results.Problem(exception.Message, statusCode: 400); }
        catch (AccountingConflictException exception) { return Results.Problem(exception.Message, statusCode: 409); }
    }
    private static async Task<IResult> ExecuteAsync<T>(Func<Task<T>> action, Func<T, IResult> success)
    {
        try { return success(await action()); }
        catch (AccountingForbiddenException exception) { return Results.Problem(exception.Message, statusCode: 403); }
        catch (AccountingValidationException exception) { return Results.Problem(exception.Message, statusCode: 400); }
        catch (AccountingConflictException exception) { return Results.Problem(exception.Message, statusCode: 409); }
    }
}

public static class AccountingClaimsPrincipalExtensions
{
    public static AccountingUserIdentity ToAccountingIdentity(this ClaimsPrincipal principal) => new(
        RequiredGuid(principal, ClaimTypes.NameIdentifier), RequiredGuid(principal, "tenant_id"), RequiredGuid(principal, "business_id"),
        principal.FindAll("permission").Select(claim => claim.Value).ToHashSet(StringComparer.Ordinal));
    private static Guid RequiredGuid(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value) ? value : throw new AccountingForbiddenException($"The authenticated identity lacks claim '{claimType}'.");
}
