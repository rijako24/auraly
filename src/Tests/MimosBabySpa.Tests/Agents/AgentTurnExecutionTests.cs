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
            "checkout_no_deposit",
            new Dictionary<string, object?>(),
            FragmentRenderMode.Exclusive);

        turn.FragmentEntries.Should().HaveCount(2);
    }

    [Fact]
    public void RecordToolOutcome_RecoverableErrors_DoNotCountTowardEscalation()
    {
        var turn = new AgentTurnExecution(errorEscalationThreshold: 3);
        var recoverable = ToolExecutionOutcome.Parse(
            """{"ok":false,"error":{"code":"unknown_fact_key","message":"x","recoverable":true}}""");

        turn.RecordToolOutcome(recoverable);
        turn.RecordToolOutcome(recoverable);
        turn.RecordToolOutcome(recoverable);

        turn.ShouldAutoEscalate.Should().BeFalse();
        turn.ConsecutiveToolErrors.Should().Be(0);
    }

    [Fact]
    public void RecordToolOutcome_FatalErrors_CountTowardEscalation()
    {
        var turn = new AgentTurnExecution(errorEscalationThreshold: 3);
        var fatal = ToolExecutionOutcome.Parse(
            """{"ok":false,"error":{"code":"tool_exception","message":"x"}}""");

        turn.RecordToolOutcome(fatal);
        turn.RecordToolOutcome(fatal);
        turn.ShouldAutoEscalate.Should().BeFalse();

        turn.RecordToolOutcome(fatal);
        turn.ShouldAutoEscalate.Should().BeTrue();
    }

    [Fact]
    public void RecordToolOutcome_SuccessResetsFatalErrorCounter()
    {
        var turn = new AgentTurnExecution(errorEscalationThreshold: 3);
        var fatal = ToolExecutionOutcome.Parse(
            """{"ok":false,"error":{"code":"tool_exception","message":"x"}}""");
        var ok = ToolExecutionOutcome.Parse("""{"ok":true,"data":{}}""");

        turn.RecordToolOutcome(fatal);
        turn.RecordToolOutcome(fatal);
        turn.RecordToolOutcome(ok);
        turn.RecordToolOutcome(fatal);

        turn.ShouldAutoEscalate.Should().BeFalse();
        turn.ConsecutiveToolErrors.Should().Be(1);
    }
}
