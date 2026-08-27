using System.Security.Claims;
using Auraly.Commerce.Payroll.Application;
using Auraly.Commerce.Payroll.Contracts;

namespace Auraly.Api;

public static class PayrollApi
{
    public static IEndpointRouteBuilder MapPayrollApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commerce/v1/payroll")
            .RequireAuthorization("payroll.user");

        group.MapGet("/options", (HttpContext context, PayrollService service,
            CancellationToken ct) => Execute(() => service.GetOptionsAsync(context.User.Identity(), ct)));

        group.MapGet("/reports/definitions", (HttpContext context,
            PayrollReportingService service, CancellationToken ct) =>
            Execute(() => service.ListDefinitionsAsync(context.User.Identity(), ct)));

        group.MapGet("/reports/{code}", (HttpContext context, string code,
            DateOnly from, DateOnly to, Guid? partyId,
            PayrollReportingService service, CancellationToken ct) =>
            Execute(() => service.RunAsync(context.User.Identity(), code,
                from, to, partyId, ct)));

        group.MapPut("/settings", (HttpContext context, SavePayrollSettingsRequest request,
            PayrollService service, CancellationToken ct) =>
            Execute(() => service.SaveSettingsAsync(context.User.Identity(), request, ct)));

        group.MapPut("/electronic-configuration", (HttpContext context,
            SaveElectronicPayrollConfigurationRequest request,
            PayrollService service, CancellationToken ct) =>
            Execute(() => service.SaveElectronicConfigurationAsync(
                context.User.Identity(), request, ct)));

        group.MapPut("/employments/{id:guid}", (HttpContext context, Guid id,
            SavePayrollEmploymentRequest request, PayrollService service, CancellationToken ct) =>
            id != request.EmploymentId ? Task.FromResult<IResult>(Results.BadRequest()) :
            Execute(() => service.SaveEmploymentAsync(context.User.Identity(), request, ct)));

        group.MapPut("/concepts/{id:guid}", (HttpContext context, Guid id,
            SavePayrollConceptRequest request, PayrollService service, CancellationToken ct) =>
            id != request.ConceptId ? Task.FromResult<IResult>(Results.BadRequest()) :
            Execute(() => service.SaveConceptAsync(context.User.Identity(), request, ct)));

        group.MapPut("/rule-sets/{id:guid}", (HttpContext context, Guid id,
            SavePayrollRuleSetRequest request, PayrollService service, CancellationToken ct) =>
            id != request.RuleSetId ? Task.FromResult<IResult>(Results.BadRequest()) :
            Execute(() => service.SaveRuleSetAsync(context.User.Identity(), request, ct)));

        group.MapPost("/rule-sets/{id:guid}/approve", (HttpContext context, Guid id,
            PayrollVersionRequest request, PayrollService service, CancellationToken ct) =>
            Execute(() => service.ApproveRuleSetAsync(context.User.Identity(), id, request.RowVersion, ct)));

        group.MapPut("/deduction-agreements/{id:guid}", (HttpContext context, Guid id,
            SavePayrollDeductionAgreementRequest request, PayrollService service, CancellationToken ct) =>
            id != request.DeductionAgreementId ? Task.FromResult<IResult>(Results.BadRequest()) :
            Execute(() => service.SaveDeductionAgreementAsync(context.User.Identity(), request, ct)));

        group.MapPost("/novelties", async (HttpContext context, SavePayrollNoveltyRequest request,
            PayrollService service, CancellationToken ct) =>
        {
            try
            {
                await service.SaveNoveltyAsync(context.User.Identity(), request, ct);
                return Results.Created($"/api/commerce/v1/payroll/novelties/{request.NoveltyId:D}", request);
            }
            catch (Exception error) { return Problem(error); }
        });

        group.MapPost("/payments", (HttpContext context,
            CreatePayrollPaymentBatchRequest request, PayrollService service,
            CancellationToken ct) =>
            ExecuteCreated(() => service.CreatePaymentBatchAsync(
                context.User.Identity(), request, ct),
                value => $"/api/commerce/v1/payroll/payments/{value.PaymentBatchId:D}"));

        group.MapPost("/runs", (HttpContext context, CreatePayrollRunRequest request,
            PayrollService service, CancellationToken ct) =>
            ExecuteCreated(() => service.CreateRunAsync(context.User.Identity(), request, ct),
                value => $"/api/commerce/v1/payroll/runs/{value.PayrollRunId:D}"));

        group.MapGet("/runs", (HttpContext context, PayrollService service,
            CancellationToken ct) => Execute(() => service.ListRunsAsync(context.User.Identity(), ct)));

        group.MapGet("/runs/{id:guid}", (HttpContext context, Guid id,
            PayrollService service, CancellationToken ct) =>
            Execute(() => service.GetRunAsync(context.User.Identity(), id, ct)));

        group.MapPost("/runs/{id:guid}/calculate", (HttpContext context, Guid id,
            PayrollService service, CancellationToken ct) =>
            Execute(() => service.CalculateRunAsync(context.User.Identity(), id, ct)));

        group.MapPost("/runs/{id:guid}/approve", (HttpContext context, Guid id,
            PayrollVersionRequest request, PayrollService service, CancellationToken ct) =>
            ExecuteAccepted(() => service.ApproveRunAsync(context.User.Identity(), id,
                context.Request.Headers["Idempotency-Key"].ToString(), request.RowVersion, ct),
                id));

        group.MapPost("/electronic-periods", (HttpContext context,
            GenerateElectronicPayrollPeriodRequest request,
            PayrollService service, CancellationToken ct) =>
            ExecuteCreated(
                () => service.GenerateElectronicPeriodAsync(
                    context.User.Identity(), request, ct),
                value => $"/api/commerce/v1/payroll/electronic-periods/{value.ElectronicPeriodId:D}"));

        return endpoints;
    }

    private static async Task<IResult> Execute<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (Exception error) { return Problem(error); }
    }

    private static async Task<IResult> ExecuteCreated<T>(Func<Task<T>> action, Func<T, string> location)
    {
        try { var value = await action(); return Results.Created(location(value), value); }
        catch (Exception error) { return Problem(error); }
    }

    private static async Task<IResult> ExecuteAccepted<T>(Func<Task<T>> action, Guid id)
    {
        try { return Results.Accepted($"/api/commerce/v1/payroll/runs/{id:D}", await action()); }
        catch (Exception error) { return Problem(error); }
    }

    private static IResult Problem(Exception error) => error switch
    {
        PayrollForbiddenException => Results.Problem(error.Message, statusCode: StatusCodes.Status403Forbidden),
        PayrollValidationException => Results.Problem(error.Message, statusCode: StatusCodes.Status400BadRequest),
        PayrollConflictException => Results.Problem(error.Message, statusCode: StatusCodes.Status409Conflict),
        PayrollNotFoundException => Results.Problem(error.Message, statusCode: StatusCodes.Status404NotFound),
        _ => throw error
    };

    private static PayrollUserIdentity Identity(this ClaimsPrincipal principal) => new(
        Required(principal, ClaimTypes.NameIdentifier),
        Required(principal, "tenant_id"),
        Required(principal, "business_id"),
        principal.FindAll("permission").Select(claim => claim.Value)
            .ToHashSet(StringComparer.Ordinal));

    private static Guid Required(ClaimsPrincipal principal, string type) =>
        Guid.TryParse(principal.FindFirstValue(type), out var value) ? value :
            throw new PayrollForbiddenException($"Falta el claim '{type}'.");
}

public sealed record PayrollVersionRequest(byte[] RowVersion);
