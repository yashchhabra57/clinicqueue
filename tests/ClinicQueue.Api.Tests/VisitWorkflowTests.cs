using ClinicQueue.Api.Data;
using ClinicQueue.Api.Visits;

namespace ClinicQueue.Api.Tests;

public class VisitWorkflowTests
{
    private static readonly DateTime T0 = new(2026, 11, 18, 15, 0, 0, DateTimeKind.Utc);

    private static Visit NewVisit() => new() { TicketCode = "A-001", Initials = "JD", CheckedInAt = T0 };

    [Fact]
    public void Happy_path_stamps_call_and_end_times()
    {
        var v = NewVisit();
        Assert.True(VisitWorkflow.TryApply(v, VisitAction.Call, T0.AddMinutes(12)));
        Assert.Equal(VisitStatus.InRoom, v.Status);
        Assert.True(VisitWorkflow.TryApply(v, VisitAction.Complete, T0.AddMinutes(30)));
        Assert.Equal(VisitStatus.Completed, v.Status);
        Assert.Equal(18, v.VisitMinutes);
    }

    [Theory]
    [InlineData(VisitStatus.Waiting, VisitAction.Complete)]
    [InlineData(VisitStatus.Waiting, VisitAction.ReturnToQueue)]
    [InlineData(VisitStatus.InRoom, VisitAction.Call)]
    [InlineData(VisitStatus.InRoom, VisitAction.Leave)]
    [InlineData(VisitStatus.Completed, VisitAction.Call)]
    [InlineData(VisitStatus.LeftWithoutBeingSeen, VisitAction.Call)]
    public void Illegal_transitions_change_nothing(VisitStatus from, VisitAction action)
    {
        var v = NewVisit();
        v.Status = from;
        var version = v.Version;
        Assert.False(VisitWorkflow.TryApply(v, action, T0));
        Assert.Equal(from, v.Status);
        Assert.Equal(version, v.Version);
    }

    [Fact]
    public void Returning_to_queue_keeps_the_original_place_in_line()
    {
        var v = NewVisit();
        VisitWorkflow.TryApply(v, VisitAction.Call, T0.AddMinutes(5));
        Assert.True(VisitWorkflow.TryApply(v, VisitAction.ReturnToQueue, T0.AddMinutes(6)));
        Assert.Equal(VisitStatus.Waiting, v.Status);
        Assert.Null(v.CalledAt);
        Assert.Equal(T0, v.CheckedInAt);
    }

    [Fact]
    public void Every_transition_rotates_the_concurrency_token()
    {
        var v = NewVisit();
        var before = v.Version;
        VisitWorkflow.TryApply(v, VisitAction.Leave, T0);
        Assert.NotEqual(before, v.Version);
        Assert.Null(v.VisitMinutes); // walkouts don't count toward visit length
    }
}
