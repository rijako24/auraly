using Auraly.Application.Parties;
using Auraly.Contracts.Parties;

namespace Auraly.Api;

public static class PartyUserAccountApi
{
    public static IEndpointRouteBuilder MapPartyUserAccountApi(
        this IEndpointRouteBuilder endpoints)
    {
        var parties = endpoints.MapGroup("/api/commerce/v1/parties")
            .RequireAuthorization("parties.user");

        parties.MapGet("/{partyId:guid}/user-account", async (
            HttpContext context,
            PartyService service,
            Guid partyId,
            CancellationToken ct) =>
            await Handle(async () =>
            {
                var account = await service.GetUserAccountAsync(
                    context.User.ToPartyUserIdentity(),
                    partyId,
                    ct);
                return account is null ? Results.NotFound() : Results.Ok(account);
            }));

        parties.MapPut("/{partyId:guid}/user-account", async (
            HttpContext context,
            PartyService service,
            Guid partyId,
            LinkPartyUserAccountRequest request,
            CancellationToken ct) =>
            await Handle(async () => Results.Ok(
                await service.LinkUserAccountAsync(
                    context.User.ToPartyUserIdentity(),
                    partyId,
                    request,
                    ct))));

        parties.MapDelete("/{partyId:guid}/user-account", async (
            HttpContext context,
            PartyService service,
            Guid partyId,
            CancellationToken ct) =>
            await Handle(async () =>
            {
                await service.UnlinkUserAccountAsync(
                    context.User.ToPartyUserIdentity(),
                    partyId,
                    ct);
                return Results.NoContent();
            }));

        return endpoints;
    }

    private static async Task<IResult> Handle(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (PartyForbiddenException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status403Forbidden);
        }
        catch (PartyValidationException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (PartyConflictException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "PartyConflict");
        }
    }
}
