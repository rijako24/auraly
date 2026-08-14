using FluentAssertions;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Agents.Composition;
using Auraly.Platform.Application.Agents.Configuration;
using Xunit;

namespace Auraly.Platform.Tests.Agents;

public sealed class FlowCheckpointInvalidationTests
{
    [Fact]
    public void GetDerivedAdvanceFactsToClear_WhenReentryFactChanged_ReturnsOnlyDerivedAdvanceFacts()
    {
        var ctx = new AgentConversationContext
        {
            Config = new AgentConfig
            {
                FactSchema =
                [
                    new FactSchemaEntry { Key = "order_finalized", Source = "user" },
                    new FactSchemaEntry { Key = "customer_name", Source = "user" },
                    new FactSchemaEntry { Key = "order_checkout_presented", Source = "system" }
                ],
                Flows = [new AgentFlowDefinition
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
                }]
            }
        };

        var result = FlowCheckpointInvalidation.GetDerivedAdvanceFactsToClear(ctx, ["order_finalized"]);

        result.Should().Equal("order_checkout_presented");
    }

    [Fact]
    public void GetInvalidations_WhenReentryFactChanged_ReturnsDerivedFactsAndStageSnapshots()
    {
        var ctx = new AgentConversationContext
        {
            Config = new AgentConfig
            {
                FactSchema =
                [
                    new FactSchemaEntry { Key = "payment_method", Source = "user" },
                    new FactSchemaEntry { Key = "order_checkout_presented", Source = "system" }
                ],
                Flows = [new AgentFlowDefinition
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
                }]
            }
        };

        var result = FlowCheckpointInvalidation.GetInvalidations(ctx, ["payment_method"]);

        result.FactsToClear.Should().Equal("order_checkout_presented");
        result.StageSnapshotsToReset.Should().Equal("summary");
    }

    [Fact]
    public void GetDerivedAdvanceFactsToClear_WhenChangedFactIsNotAReentryDependency_ReturnsEmpty()
    {
        var ctx = new AgentConversationContext
        {
            Config = new AgentConfig
            {
                FactSchema =
                [
                    new FactSchemaEntry { Key = "order_checkout_presented", Source = "system" }
                ],
                Flows = [new AgentFlowDefinition
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
                }]
            }
        };

        var result = FlowCheckpointInvalidation.GetDerivedAdvanceFactsToClear(ctx, ["payment_method"]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void GetDerivedAdvanceFactsToClear_MatchesChangedFactsIgnoringCase()
    {
        var ctx = new AgentConversationContext
        {
            Config = new AgentConfig
            {
                FactSchema =
                [
                    new FactSchemaEntry { Key = "summary_presented", Source = "system" }
                ],
                Flows = [new AgentFlowDefinition
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
                }]
            }
        };

        var result = FlowCheckpointInvalidation.GetDerivedAdvanceFactsToClear(ctx, ["customer_name"]);

        result.Should().Equal("summary_presented");
    }

    [Fact]
    public void GetInvalidations_WhenDependencyChanges_ClearsDependentRequestFactsTransitivelyAndResetsAffectedStages()
    {
        var ctx = new AgentConversationContext
        {
            Config = new AgentConfig
            {
                FactSchema =
                [
                    new FactSchemaEntry { Key = "desired_date", Source = "user", Scope = FactScopes.Request },
                    new FactSchemaEntry
                    {
                        Key = "desired_time",
                        Source = "user",
                        Scope = FactScopes.Request,
                        DependsOn = ["desired_date"]
                    },
                    new FactSchemaEntry
                    {
                        Key = "availability_checked",
                        Source = "system",
                        Scope = FactScopes.Ephemeral,
                        DependsOn = ["desired_time"]
                    }
                ],
                Flows = [new AgentFlowDefinition
                {
                    Stages =
                    [
                        new AgentFlowStage
                        {
                            Id = "scheduling",
                            AdvanceWhenFacts = ["availability_checked"],
                            ReentryOnFactChanged = ["desired_time"]
                        }
                    ]
                }]
            }
        };

        var result = FlowCheckpointInvalidation.GetInvalidations(ctx, ["desired_date"]);

        result.FactsToClear.Should().Equal("desired_time", "availability_checked");
        result.StageSnapshotsToReset.Should().Equal("scheduling");
    }

    [Fact]
    public void GetInvalidations_WhenDependencyChanges_DoesNotClearCustomerScopedFacts()
    {
        var ctx = new AgentConversationContext
        {
            Config = new AgentConfig
            {
                FactSchema =
                [
                    new FactSchemaEntry { Key = "service", Source = "user", Scope = FactScopes.Request },
                    new FactSchemaEntry
                    {
                        Key = "customer_name",
                        Source = "user",
                        Scope = FactScopes.Customer,
                        DependsOn = ["service"]
                    }
                ]
            }
        };

        var result = FlowCheckpointInvalidation.GetInvalidations(ctx, ["service"]);

        result.FactsToClear.Should().BeEmpty();
        result.StageSnapshotsToReset.Should().BeEmpty();
    }

    [Fact]
    public void GetInvalidations_WhenAdvanceFactIsAnAuthoritativeSystemDefault_PreservesIt()
    {
        var ctx = new AgentConversationContext
        {
            Config = new AgentConfig
            {
                FactSchema =
                [
                    new FactSchemaEntry { Key = "delivery_address", Source = "user" },
                    new FactSchemaEntry
                    {
                        Key = "city",
                        Source = "system",
                        DefaultValue = "Valledupar"
                    }
                ],
                Flows =
                [
                    new AgentFlowDefinition
                    {
                        Stages =
                        [
                            new AgentFlowStage
                            {
                                Id = "order_data",
                                AdvanceWhenFacts = ["delivery_address", "city"],
                                ReentryOnFactChanged = ["delivery_address", "city"]
                            }
                        ]
                    }
                ]
            }
        };

        var result = FlowCheckpointInvalidation.GetInvalidations(ctx, ["delivery_address"]);

        result.FactsToClear.Should().NotContain("city");
    }
}
