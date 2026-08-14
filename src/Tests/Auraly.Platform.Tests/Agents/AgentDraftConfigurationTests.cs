using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Agents.Configuration;
using Auraly.Platform.Application.Agents.Operations;
using Auraly.Platform.Application.Identity.Services;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Agents;

public sealed class AgentDraftConfigurationTests
{
    [Fact]
    public async Task NewDraftSettings_DeserializeAndCompileForAdmin()
    {
        var agentId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var repository = new Mock<IAgentRepository>();
        repository
            .Setup(value => value.GetByIdForAdminAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent
            {
                AgentId = agentId,
                BusinessId = businessId,
                Name = "Sofia",
                IsActive = false,
                SettingsJson = AgentAdminService.CreateDraftSettingsJson("Sofia")
            });

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new AgentConfigProvider(
            repository.Object,
            cache,
            NullLogger<AgentConfigProvider>.Instance,
            new AgentConfigurationCompiler(new AgentOperationRegistry([])));

        var config = await provider.GetConfigForAdminAsync(agentId);

        config.AgentId.Should().Be(agentId);
        config.BusinessId.Should().Be(businessId);
        config.Persona.Should().Contain("Sofia");
        config.Flows.Should().ContainSingle(flow => flow.Type == FlowTypes.Primary);
        config.Flows.Single().Stages.Should().ContainSingle(stage => stage.Id == "start");
    }
}
