using MimosBabySpa.Application.Services;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

/// <summary>
/// Fake para tests: no genera links, no valida tokens.
/// </summary>
internal class FakeReleaseLinkService : IReleaseLinkService
{
    public string? GenerateReleaseUrl(Guid conversationId) => null;

    public bool ValidateToken(Guid conversationId, string token) => false;
}
