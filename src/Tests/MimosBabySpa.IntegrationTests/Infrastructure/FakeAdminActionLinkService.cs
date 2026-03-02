using MimosBabySpa.Application.Services;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

/// <summary>
/// Fake para tests: no genera links, no valida tokens. Implementa ambas interfaces.
/// </summary>
internal class FakeAdminActionLinkService : IAdminActionLinkService, IReleaseLinkService
{
    public string? GenerateReleaseUrl(Guid conversationId) => null;
    public string? GeneratePaymentConfirmationUrl(string paymentReferenceId) => null;
    public bool ValidateReleaseToken(Guid conversationId, string token) => false;
    public bool ValidatePaymentConfirmationToken(string paymentReferenceId, string token) => false;
    public bool ValidateToken(Guid conversationId, string token) => false;
}
