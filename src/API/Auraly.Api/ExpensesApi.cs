using System.Security.Claims;
using Auraly.Application.Expenses;
using Auraly.Contracts.Expenses;

namespace Auraly.Api;

public static class ExpensesApi
{
    public static IEndpointRouteBuilder MapExpensesApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commerce/v1/expenses")
            .RequireAuthorization("expenses.user");

        group.MapGet("/options", (
            HttpContext context,
            ExpenseService service,
            CancellationToken cancellationToken) =>
            Execute(() => service.GetOptionsAsync(
                context.User.ToExpenseIdentity(), cancellationToken)));

        group.MapGet("/concepts", (
            HttpContext context,
            bool? includeInactive,
            ExpenseService service,
            CancellationToken cancellationToken) =>
            Execute(() => service.ListConceptsAsync(
                context.User.ToExpenseIdentity(),
                includeInactive ?? false,
                cancellationToken)));

        group.MapPut("/concepts/{id:guid}", (
            HttpContext context,
            Guid id,
            SaveExpenseConceptRequest request,
            ExpenseService service,
            CancellationToken cancellationToken) =>
            id != request.ConceptId
                ? Task.FromResult<IResult>(Results.BadRequest())
                : Execute(() => service.SaveConceptAsync(
                    context.User.ToExpenseIdentity(), request, cancellationToken)));

        group.MapGet("", (
            HttpContext context,
            int? page,
            int? pageSize,
            string? search,
            Guid? conceptId,
            Guid? supplierId,
            DateOnly? from,
            DateOnly? to,
            ExpenseService service,
            CancellationToken cancellationToken) =>
            Execute(() => service.ListAsync(
                context.User.ToExpenseIdentity(),
                page ?? 1,
                pageSize ?? 25,
                search,
                conceptId,
                supplierId,
                from,
                to,
                cancellationToken)));

        group.MapPost("/confirm", async (
            HttpContext context,
            ConfirmExpenseRequest request,
            ExpenseService service,
            CancellationToken cancellationToken) =>
            await ExecuteResult(async () =>
            {
                var value = await service.ConfirmAsync(
                    context.User.ToExpenseIdentity(),
                    context.Request.Headers["Idempotency-Key"].ToString(),
                    request,
                    cancellationToken);
                return Results.Accepted(
                    $"/api/commerce/v1/expenses/{value.ExpenseId:D}", value);
            }));
        return endpoints;
    }

    private static async Task<IResult> Execute<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (ExpenseForbiddenException error)
        { return Results.Problem(error.Message, statusCode: StatusCodes.Status403Forbidden); }
        catch (ExpenseValidationException error)
        { return Results.Problem(error.Message, statusCode: StatusCodes.Status400BadRequest); }
        catch (ExpenseConflictException error)
        { return Results.Problem(error.Message, statusCode: StatusCodes.Status409Conflict); }
    }

    private static async Task<IResult> ExecuteResult(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (ExpenseForbiddenException error)
        { return Results.Problem(error.Message, statusCode: StatusCodes.Status403Forbidden); }
        catch (ExpenseValidationException error)
        { return Results.Problem(error.Message, statusCode: StatusCodes.Status400BadRequest); }
        catch (ExpenseConflictException error)
        { return Results.Problem(error.Message, statusCode: StatusCodes.Status409Conflict); }
    }

    private static ExpenseUserIdentity ToExpenseIdentity(this ClaimsPrincipal principal) =>
        new(
            Required(principal, ClaimTypes.NameIdentifier),
            Required(principal, "tenant_id"),
            Required(principal, "business_id"),
            principal.FindAll("permission")
                .Select(claim => claim.Value)
                .ToHashSet(StringComparer.Ordinal));

    private static Guid Required(ClaimsPrincipal principal, string type) =>
        Guid.TryParse(principal.FindFirstValue(type), out var value)
            ? value
            : throw new ExpenseForbiddenException($"Falta el claim '{type}'.");
}
