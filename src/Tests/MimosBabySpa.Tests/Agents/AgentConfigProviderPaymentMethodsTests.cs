using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Operations.Checkout;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using Moq;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class AgentConfigProviderPaymentMethodsTests
{
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
