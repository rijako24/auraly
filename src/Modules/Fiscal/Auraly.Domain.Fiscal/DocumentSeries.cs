using Auraly.BuildingBlocks.Domain.Identifiers;

namespace Auraly.Domain.Fiscal;

public enum DocumentSeriesStatus
{
    Draft,
    Active,
    Exhausted,
    Expired,
    Revoked
}

public sealed record AssignedDocumentNumber(
    Guid SeriesId,
    string Prefix,
    long Consecutive,
    string FullNumber);

public sealed class DocumentSeries
{
    private readonly object _gate = new();
    private long _nextConsecutive;

    public DocumentSeries(
        Guid id,
        BusinessId businessId,
        RegisterId exclusiveRegisterId,
        string prefix,
        long rangeStart,
        long rangeEnd,
        DateOnly validFrom,
        DateOnly validUntil)
    {
        if (id == Guid.Empty) throw new ArgumentException("A series ID is required.", nameof(id));
        if (businessId.Value == Guid.Empty) throw new ArgumentException("A business ID is required.", nameof(businessId));
        if (exclusiveRegisterId.Value == Guid.Empty) throw new ArgumentException("An exclusive register is required.", nameof(exclusiveRegisterId));
        if (string.IsNullOrWhiteSpace(prefix)) throw new ArgumentException("A prefix is required.", nameof(prefix));
        if (rangeStart <= 0 || rangeEnd < rangeStart) throw new ArgumentOutOfRangeException(nameof(rangeStart));
        if (validUntil < validFrom) throw new ArgumentOutOfRangeException(nameof(validUntil));

        Id = id;
        BusinessId = businessId;
        ExclusiveRegisterId = exclusiveRegisterId;
        Prefix = prefix.Trim().ToUpperInvariant();
        RangeStart = rangeStart;
        RangeEnd = rangeEnd;
        ValidFrom = validFrom;
        ValidUntil = validUntil;
        _nextConsecutive = rangeStart;
        Status = DocumentSeriesStatus.Draft;
    }

    public Guid Id { get; }
    public BusinessId BusinessId { get; }
    public RegisterId ExclusiveRegisterId { get; }
    public string Prefix { get; }
    public long RangeStart { get; }
    public long RangeEnd { get; }
    public DateOnly ValidFrom { get; }
    public DateOnly ValidUntil { get; }
    public DocumentSeriesStatus Status { get; private set; }
    public long NextConsecutive => Interlocked.Read(ref _nextConsecutive);

    public void Activate(DateOnly today)
    {
        if (today < ValidFrom || today > ValidUntil)
        {
            throw new InvalidOperationException("The fiscal series is outside its validity period.");
        }

        if (Status is DocumentSeriesStatus.Revoked or DocumentSeriesStatus.Exhausted)
        {
            throw new InvalidOperationException($"A {Status} series cannot be activated.");
        }

        Status = DocumentSeriesStatus.Active;
    }

    public AssignedDocumentNumber Consume(RegisterId registerId, DateOnly today)
    {
        lock (_gate)
        {
            if (registerId != ExclusiveRegisterId)
            {
                throw new InvalidOperationException("This fiscal series belongs to a different register.");
            }

            if (today > ValidUntil)
            {
                Status = DocumentSeriesStatus.Expired;
                throw new InvalidOperationException("The fiscal series has expired.");
            }

            if (Status != DocumentSeriesStatus.Active)
            {
                throw new InvalidOperationException("The fiscal series is not active.");
            }

            if (_nextConsecutive > RangeEnd)
            {
                Status = DocumentSeriesStatus.Exhausted;
                throw new InvalidOperationException("The fiscal series is exhausted.");
            }

            var value = _nextConsecutive++;
            if (_nextConsecutive > RangeEnd)
            {
                Status = DocumentSeriesStatus.Exhausted;
            }

            return new AssignedDocumentNumber(Id, Prefix, value, $"{Prefix}{value}");
        }
    }

    public void Revoke() => Status = DocumentSeriesStatus.Revoked;
}
