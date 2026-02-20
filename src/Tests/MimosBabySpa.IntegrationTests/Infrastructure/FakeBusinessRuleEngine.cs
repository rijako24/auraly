using MimosBabySpa.Application.BusinessRules;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

/// <summary>
/// Fake IBusinessRuleEngine. Always returns valid. 
/// Specific scenarios can inject their own override if needed.
/// </summary>
public class FakeBusinessRuleEngine : IBusinessRuleEngine
{
    public Task<BusinessRuleValidationResult> ValidateReservationAsync(
        Guid businessId, ConversationState state, CancellationToken cancellationToken = default) =>
        Task.FromResult(new BusinessRuleValidationResult { IsValid = true });

    public Task<BusinessRuleContext> GetBusinessContextAsync(
        Guid businessId, string phone, string? service, CancellationToken cancellationToken = default) =>
        Task.FromResult(new BusinessRuleContext
        {
            HasRestrictions = false,
            HasBenefits     = false
        });

    public BusinessRuleValidationResult ValidateBusinessAttribute(
        Guid businessId, string attributeName, string attributeValue) =>
        new() { IsValid = true };
}
