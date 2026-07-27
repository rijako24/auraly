namespace Auraly.Contracts.Fiscal;

public sealed record FiscalNumberAssignment(
    Guid SeriesId,
    string Prefix,
    long Consecutive,
    string FullNumber,
    string AuthorizationNumber);
