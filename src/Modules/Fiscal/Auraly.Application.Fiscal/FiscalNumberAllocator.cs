using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Fiscal;
using Auraly.Domain.Fiscal;

namespace Auraly.Application.Fiscal;

public sealed class FiscalNumberAllocator
{
    public FiscalNumberAssignment Allocate(
        DocumentSeries series,
        DeviceId deviceId,
        DateOnly issueDate,
        string authorizationNumber)
    {
        if (string.IsNullOrWhiteSpace(authorizationNumber))
        {
            throw new ArgumentException("An authorization number is required.", nameof(authorizationNumber));
        }

        var assigned = series.Consume(deviceId, issueDate);
        return new FiscalNumberAssignment(
            assigned.SeriesId,
            assigned.Prefix,
            assigned.Consecutive,
            assigned.FullNumber,
            authorizationNumber.Trim());
    }
}
