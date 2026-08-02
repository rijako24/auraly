using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.Contracts.Pricing;
using Auraly.Domain.Pricing;

namespace Auraly.Application.Pricing;

public sealed record PriceProposalSource(
    Guid ProposalId, Guid ProductId, decimal ObservedUnitCost,
    string Status, byte[] RowVersion);

public sealed record PreparedPricePublication(
    Guid ProposalId, Guid ProductId, decimal CostBasisAmount,
    string InputMode, decimal? TargetMarginPercent, decimal SalePrice,
    decimal? EffectiveMarginPercent, decimal RoundingIncrement,
    string RoundingMode, byte[] ExpectedRowVersion);

public interface IPricingStore
{
    Task<PriceRevisionPage> ListAsync(PricingUserIdentity user, PriceRevisionQuery query, CancellationToken ct);
    Task<PriceProposalSource?> GetProposalAsync(PricingUserIdentity user, Guid proposalId, CancellationToken ct);
    Task ReviewAsync(PricingUserIdentity user, Guid proposalId, PriceCalculationResult calculation, byte[] expectedRowVersion, CancellationToken ct);
    Task RejectAsync(PricingUserIdentity user, Guid proposalId, byte[] expectedRowVersion, string? reason, CancellationToken ct);
    Task<PublishPricesResult> PublishAsync(PricingUserIdentity user, IReadOnlyList<PreparedPricePublication> values, DateTimeOffset now, CancellationToken ct);
    Task<IReadOnlyList<ProductPriceHistoryItem>> HistoryAsync(PricingUserIdentity user, Guid productId, CancellationToken ct);
}

public sealed class PricingService(
    IPricingStore store,
    TimeProvider timeProvider,
    IPosSynchronizationOutboxDispatcher synchronization)
{
    public Task<PriceRevisionPage> ListAsync(PricingUserIdentity user, PriceRevisionQuery query, CancellationToken ct)
    {
        Require(user, PricingPermissionCodes.Read);
        Require(user, PricingPermissionCodes.ReadCostBasis);
        if (query.Page < 1 || query.PageSize is < 1 or > 100)
            throw new PricingValidationException("Page and PageSize are invalid.");
        if (query.Status is not null && query.Status is not ("PendingReview" or "Approved" or "Published" or "Rejected" or "Superseded"))
            throw new PricingValidationException("The proposal status is invalid.");
        return store.ListAsync(user, query with { Search = Normalize(query.Search, 120) }, ct);
    }

    public PriceCalculationResult Calculate(PricingUserIdentity user, PriceCalculationRequest request)
    {
        Require(user, PricingPermissionCodes.Read);
        Require(user, PricingPermissionCodes.ReadCostBasis);
        return CalculateCore(request);
    }

    public async Task ReviewAsync(PricingUserIdentity user, Guid proposalId, ReviewPriceProposalRequest request, CancellationToken ct)
    {
        Require(user, PricingPermissionCodes.ReviewProposals);
        Require(user, PricingPermissionCodes.ReadCostBasis);
        var source = await RequiredProposalAsync(user, proposalId, ct);
        EnsureReviewable(source);
        var calculation = CalculateCore(new(
            source.ObservedUnitCost, request.InputMode, request.TargetMarginPercent,
            request.SalePrice, request.RoundingIncrement, request.RoundingMode));
        await store.ReviewAsync(user, proposalId, calculation, DecodeToken(request.ConcurrencyToken), ct);
    }

    public async Task RejectAsync(PricingUserIdentity user, Guid proposalId, RejectPriceProposalRequest request, CancellationToken ct)
    {
        Require(user, PricingPermissionCodes.ReviewProposals);
        var source = await RequiredProposalAsync(user, proposalId, ct);
        EnsureReviewable(source);
        await store.RejectAsync(user, proposalId, DecodeToken(request.ConcurrencyToken), Normalize(request.Reason, 500), ct);
    }

    public async Task<PublishPricesResult> PublishAsync(PricingUserIdentity user, PublishPricesRequest request, CancellationToken ct)
    {
        Require(user, PricingPermissionCodes.PublishPrices);
        Require(user, PricingPermissionCodes.ReadCostBasis);
        if (request.Items is null || request.Items.Count == 0)
            throw new PricingValidationException("At least one proposal is required.");
        if (request.Items.Count > 1) Require(user, PricingPermissionCodes.BulkPublish);
        if (request.Items.Count > 100 || request.Items.Select(x => x.ProposalId).Distinct().Count() != request.Items.Count)
            throw new PricingValidationException("The publication batch is invalid.");

        var prepared = new List<PreparedPricePublication>(request.Items.Count);
        foreach (var item in request.Items)
        {
            var source = await RequiredProposalAsync(user, item.ProposalId, ct);
            EnsureReviewable(source);
            var expected = DecodeToken(item.ConcurrencyToken);
            if (!source.RowVersion.AsSpan().SequenceEqual(expected))
                throw new PricingConflictException("The proposal changed before publication.");
            var result = CalculateCore(new(
                source.ObservedUnitCost, item.InputMode, item.TargetMarginPercent,
                item.SalePrice, item.RoundingIncrement, item.RoundingMode));
            prepared.Add(new(
                source.ProposalId, source.ProductId, source.ObservedUnitCost,
                result.InputMode, result.TargetMarginPercent, result.RoundedSalePrice,
                result.EffectiveMarginPercent, result.RoundingIncrement,
                result.RoundingMode, expected));
        }

        var published = await store.PublishAsync(user, prepared, timeProvider.GetUtcNow(), ct);
        await synchronization.DispatchPendingAsync(user.TenantId, user.BusinessId, CancellationToken.None);
        return published;
    }

    public Task<IReadOnlyList<ProductPriceHistoryItem>> HistoryAsync(PricingUserIdentity user, Guid productId, CancellationToken ct)
    {
        Require(user, PricingPermissionCodes.ReadHistory);
        return store.HistoryAsync(user, productId, ct);
    }

    private static PriceCalculationResult CalculateCore(PriceCalculationRequest request)
    {
        if (request.CostBasisAmount < 0) throw new PricingValidationException("Cost basis cannot be negative.");
        if (!PriceInputModes.IsSupported(request.InputMode)) throw new PricingValidationException("InputMode is invalid.");
        if (!PricingRoundingModes.IsSupported(request.RoundingMode)) throw new PricingValidationException("RoundingMode is invalid.");
        if (request.RoundingIncrement <= 0) throw new PricingValidationException("RoundingIncrement must be positive.");
        decimal raw;
        decimal? target;
        try
        {
            if (request.InputMode == PriceInputModes.Margin)
            {
                if (request.TargetMarginPercent is null) throw new PricingValidationException("TargetMarginPercent is required.");
                target = request.TargetMarginPercent;
                raw = PriceMargin.CalculateSalePrice(request.CostBasisAmount, target.Value);
            }
            else
            {
                if (request.SalePrice is null || request.SalePrice < 0) throw new PricingValidationException("SalePrice is required.");
                raw = request.SalePrice.Value;
                target = PriceMargin.CalculateMarginPercent(request.CostBasisAmount, raw);
            }
            var rounded = PriceMargin.RoundPrice(raw, request.RoundingIncrement, request.RoundingMode);
            return new(request.CostBasisAmount, request.InputMode, target, raw, rounded,
                PriceMargin.CalculateMarginPercent(request.CostBasisAmount, rounded),
                request.RoundingIncrement, request.RoundingMode);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new PricingValidationException(exception.Message, exception);
        }
    }

    private async Task<PriceProposalSource> RequiredProposalAsync(PricingUserIdentity user, Guid proposalId, CancellationToken ct) =>
        await store.GetProposalAsync(user, proposalId, ct) ?? throw new PricingNotFoundException("Price proposal was not found.");

    private static void EnsureReviewable(PriceProposalSource source)
    {
        if (source.Status is not ("PendingReview" or "Approved"))
            throw new PricingConflictException("Only pending or approved proposals can be changed.");
    }

    private static byte[] DecodeToken(string token)
    {
        try
        {
            var value = Convert.FromBase64String(token);
            return value.Length == 8 ? value : throw new FormatException();
        }
        catch (FormatException) { throw new PricingValidationException("ConcurrencyToken is invalid."); }
    }

    private static string? Normalize(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maximum) throw new PricingValidationException($"The value exceeds {maximum} characters.");
        return normalized;
    }

    private static void Require(PricingUserIdentity user, string permission)
    {
        if (!user.Permissions.Contains(permission)) throw new PricingForbiddenException($"Permission '{permission}' is required.");
    }
}

public sealed class PricingForbiddenException(string message) : Exception(message);
public sealed class PricingConflictException(string message) : Exception(message);
public sealed class PricingNotFoundException(string message) : Exception(message);
public sealed class PricingValidationException : Exception
{
    public PricingValidationException(string message) : base(message) { }
    public PricingValidationException(string message, Exception inner) : base(message, inner) { }
}
