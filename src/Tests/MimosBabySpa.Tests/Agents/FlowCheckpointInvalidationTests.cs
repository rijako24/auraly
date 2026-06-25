using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Configuration;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class FlowCheckpointInvalidationTests
{
    [Fact]
    public void GetDerivedAdvanceFactsToClear_WhenReentryFactChanged_ReturnsOnlyDerivedAdvanceFacts()
    {
        var ctx = new AgentToolContext
        {
            Config = new AgentConfig
            {
                FactSchema =
                [
                    new FactSchemaEntry { Key = "order_finalized", Source = "user" },
                    new FactSchemaEntry { Key = "customer_name", Source = "user" },
                    new FactSchemaEntry { Key = "order_checkout_presented", Source = "system" }
                ],
                Flow = new AgentFlowDefinition
                {
                    Stages =
                    [
                        new AgentFlowStage
                        {
                            Id = "summary",
                            AdvanceWhenFacts = ["order_checkout_presented", "customer_name"],
                            ReentryOnFactChanged = ["order_finalized"]
                        }
                    ]
                }
            }
        };

        var result = FlowCheckpointInvalidation.GetDerivedAdvanceFactsToClear(ctx, ["order_finalized"]);

        result.Should().Equal("order_checkout_presented");
    }

    [Fact]
    public void GetInvalidations_WhenReentryFactChanged_ReturnsDerivedFactsAndStageSnapshots()
    {
        var ctx = new AgentToolContext
        {
            Config = new AgentConfig
            {
                FactSchema =
                [
                    new FactSchemaEntry { Key = "payment_method", Source = "user" },
                    new FactSchemaEntry { Key = "order_checkout_presented", Source = "system" }
                ],
                Flow = new AgentFlowDefinition
                {
                    Stages =
                    [
                        new AgentFlowStage
                        {
                            Id = "summary",
                            AdvanceWhenFacts = ["order_checkout_presented"],
                            ReentryOnFactChanged = ["payment_method"]
                        }
                    ]
                }
            }
        };

        var result = FlowCheckpointInvalidation.GetInvalidations(ctx, ["payment_method"]);

        result.FactsToClear.Should().Equal("order_checkout_presented");
        result.StageSnapshotsToReset.Should().Equal("summary");
    }

    [Fact]
    public void GetDerivedAdvanceFactsToClear_WhenChangedFactIsNotAReentryDependency_ReturnsEmpty()
    {
        var ctx = new AgentToolContext
        {
            Config = new AgentConfig
            {
                FactSchema =
                [
                    new FactSchemaEntry { Key = "order_checkout_presented", Source = "system" }
                ],
                Flow = new AgentFlowDefinition
                {
                    Stages =
                    [
                        new AgentFlowStage
                        {
                            Id = "summary",
                            AdvanceWhenFacts = ["order_checkout_presented"],
                            ReentryOnFactChanged = ["order_finalized"]
                        }
                    ]
                }
            }
        };

        var result = FlowCheckpointInvalidation.GetDerivedAdvanceFactsToClear(ctx, ["payment_method"]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void GetDerivedAdvanceFactsToClear_MatchesChangedFactsIgnoringCase()
    {
        var ctx = new AgentToolContext
        {
            Config = new AgentConfig
            {
                FactSchema =
                [
                    new FactSchemaEntry { Key = "summary_presented", Source = "system" }
                ],
                Flow = new AgentFlowDefinition
                {
                    Stages =
                    [
                        new AgentFlowStage
                        {
                            Id = "summary",
                            AdvanceWhenFacts = ["summary_presented"],
                            ReentryOnFactChanged = ["Customer_Name"]
                        }
                    ]
                }
            }
        };

        var result = FlowCheckpointInvalidation.GetDerivedAdvanceFactsToClear(ctx, ["customer_name"]);

        result.Should().Equal("summary_presented");
    }
}
