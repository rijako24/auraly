using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Templates;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class AgentTurnExecutionTests
{
    [Fact]
    public void RegisterFragment_ExclusiveSameTemplateId_ReplacesPreviousFragment()
    {
        var turn = new AgentTurnExecution(errorEscalationThreshold: 3);
        var data1 = new Dictionary<string, object?> { ["time"] = "08:00" };
        var data2 = new Dictionary<string, object?> { ["time"] = "09:00" };

        turn.RegisterFragment("CHECKOUT", "checkout_with_deposit", data1, FragmentRenderMode.Exclusive);
        turn.RegisterFragment("CHECKOUT", "checkout_with_deposit", data2, FragmentRenderMode.Exclusive);

        turn.FragmentEntries.Should().HaveCount(1);
        turn.FragmentEntries[0].Fragment.Data["time"].Should().Be("09:00");
    }

    [Fact]
    public void RegisterFragment_ExclusiveDifferentTemplateIds_KeepsBoth()
    {
        var turn = new AgentTurnExecution(errorEscalationThreshold: 3);

        turn.RegisterFragment(
            "CHECKOUT",
            "checkout_with_deposit",
            new Dictionary<string, object?>(),
            FragmentRenderMode.Exclusive);
        turn.RegisterFragment(
            "RESERVATION",
            "reservation_created",
            new Dictionary<string, object?>(),
            FragmentRenderMode.Exclusive);

        turn.FragmentEntries.Should().HaveCount(2);
    }
}
