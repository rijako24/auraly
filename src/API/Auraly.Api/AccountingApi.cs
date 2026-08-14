using System.Security.Claims;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;

namespace Auraly.Api;

public static class AccountingApi
{
    public static IEndpointRouteBuilder MapAccountingApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/commerce/v1/accounting/accounts", async (HttpContext context, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.ListAccountsAsync(context.User.ToAccountingIdentity(), token), Results.Ok)).RequireAuthorization("accounting.user");
        endpoints.MapGet("/api/commerce/v1/accounting/cost-centers", async (HttpContext context, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.ListCostCentersAsync(context.User.ToAccountingIdentity(), token), Results.Ok)).RequireAuthorization("accounting.user");
        endpoints.MapGet("/api/commerce/v1/accounting/periods", async (HttpContext context, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.ListPeriodsAsync(context.User.ToAccountingIdentity(), token), Results.Ok)).RequireAuthorization("accounting.user");
        endpoints.MapGet("/api/commerce/v1/accounting/account-mappings", async (HttpContext context, AccountingService service, CancellationToken token) =>
            await ExecuteAsync(() => service.ListMappingsAsync(context.User.ToAccountingIdentity(), token), Results.Ok)).RequireAuthorization("accounting.user");
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
