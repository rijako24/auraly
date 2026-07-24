using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Operations.Checkout;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Moq;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class AgentConfigProviderPaymentMethodsTests
{
    [Theory]
    [InlineData(AgentBotType.Order, false, true)]
    [InlineData(AgentBotType.Order, true, true)]
    [InlineData(AgentBotType.Reservation, true, false)]
    [InlineData(AgentBotType.Delivery, true, false)]
    [InlineData(AgentBotType.PaymentValidator, true, false)]
    public async Task Commerce_IsDerivedOnlyFromBotType(
        AgentBotType botType,
        bool configuredCommerceEnabled,
        bool expectedCommerceEnabled)
    {
        var agentId = Guid.NewGuid();
        var repository = new Mock<IAgentRepository>();
        repository
            .Setup(value => value.GetByIdAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent
            {
                AgentId = agentId,
                BusinessId = Guid.NewGuid(),
                BotType = botType,
                Name = "Typed agent",
                IsActive = true,
                SettingsJson = $$"""
                    {
                      "flows": [
                        {
                          "id": "main",
                          "type": "primary",
                          "stages": [{ "id": "start" }]
                        }
                      ],
                      "commerce": {
                        "enabled": {{configuredCommerceEnabled.ToString().ToLowerInvariant()}},
                        "provider": "Local"
                      }
                    }
                    """
            });

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new AgentConfigProvider(
            repository.Object,
            cache,
            NullLogger<AgentConfigProvider>.Instance,
            new AgentConfigurationCompiler(new AgentOperationRegistry([])));

        var config = await provider.GetConfigAsync(agentId);

        config.Commerce.Enabled.Should().Be(expectedCommerceEnabled);
    }

    [Fact]
    public async Task ConfiguredCheckout_AutomaticallyCompilesPaymentMethodsCapability()
    {
        var agentId = Guid.NewGuid();
        var repository = new Mock<IAgentRepository>();
        repository
            .Setup(value => value.GetByIdAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent
            {
                AgentId = agentId,
                BusinessId = Guid.NewGuid(),
                Name = "Transactional agent",
                IsActive = true,
                SettingsJson = """
                    {
                      "flows": [
                        {
                          "id": "transaction",
                          "type": "primary",
                          "stages": [{ "id": "start" }]
                        }
                      ],
                      "checkout": {
                        "modes": {
                          "order": {
                            "paymentMethods": {
                              "cash": { "label": "efectivo al recibir" },
                              "card": { "label": "datafono al recibir" }
                            }
                          }
                        }
                      }
                    }
                    """
            });

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new AgentConfigProvider(
            repository.Object,
            cache,
            NullLogger<AgentConfigProvider>.Instance,
            new AgentConfigurationCompiler(
                new AgentOperationRegistry([new ListPaymentMethodsOperation()])));

        var config = await provider.GetConfigAsync(agentId);

        config.GlobalActions.Should().ContainSingle(action =>
            action.Id == BuiltInAgentCapabilities.PaymentMethodsActionId
            && action.Signal.Type == BuiltInAgentCapabilities.PaymentMethodsSignalType);
        config.Templates.Should().ContainKey(BuiltInAgentCapabilities.PaymentMethodsTemplateId);
    }
}
