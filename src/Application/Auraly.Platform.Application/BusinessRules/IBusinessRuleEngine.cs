namespace Auraly.Platform.Application.BusinessRules;

public interface IBusinessRuleEngine
{
    Task<BusinessRuleValidationResult> ValidateReservationAsync(
        Guid businessId,
        string serviceName,
        DateOnly desiredDate,
        TimeOnly desiredTime,
        CancellationToken cancellationToken = default);

    Task<BusinessRuleContext> GetBusinessContextAsync(
        Guid businessId,
        string phone,
        string? service,
        CancellationToken cancellationToken = default);
}
