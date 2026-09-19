using Amsterfam.Core.Entities;

namespace Amsterfam.Tests.Domain;

public class EventStateMachineTests
{
    private static readonly DateOnly Today = new(2030, 6, 15);

    private static Event MakeEvent(
        EventStatus status,
        DateOnly? start = null,
        DateOnly? end = null,
        bool paused = false
    ) =>
        new()
        {
            Name = "Test",
            Location = "Amsterdam",
            Status = status,
            StartDate = start ?? Today.AddDays(10),
            EndDate = end ?? Today.AddDays(17),
            AutoTransitionsPaused = paused,
        };

    public static TheoryData<EventStatus, EventStatus> OrganiserTransitions =>
        new()
        {
            { EventStatus.Draft, EventStatus.LookingForDate },
            { EventStatus.Draft, EventStatus.Open },
            { EventStatus.Draft, EventStatus.Archived },
            { EventStatus.LookingForDate, EventStatus.Draft },
            { EventStatus.LookingForDate, EventStatus.Open },
            { EventStatus.Open, EventStatus.Draft },
            { EventStatus.Open, EventStatus.LookingForDate },
            { EventStatus.Open, EventStatus.InProgress },
            { EventStatus.Open, EventStatus.Archived },
            { EventStatus.InProgress, EventStatus.Closed },
            { EventStatus.InProgress, EventStatus.Archived },
            { EventStatus.Closed, EventStatus.Archived },
        };

    public static TheoryData<EventStatus, EventStatus> OwnerOnlyTransitions =>
        new()
        {
            { EventStatus.Draft, EventStatus.Cancelled },
            { EventStatus.LookingForDate, EventStatus.Cancelled },
            { EventStatus.Open, EventStatus.Cancelled },
            { EventStatus.InProgress, EventStatus.Cancelled },
            { EventStatus.InProgress, EventStatus.Open },
            { EventStatus.Closed, EventStatus.Open },
        };

    public static TheoryData<EventStatus, EventStatus> InvalidTransitions =>
        new()
        {
            { EventStatus.Draft, EventStatus.InProgress },
            { EventStatus.Draft, EventStatus.Closed },
            { EventStatus.LookingForDate, EventStatus.Archived },
            { EventStatus.LookingForDate, EventStatus.InProgress },
            { EventStatus.Open, EventStatus.Closed },
            { EventStatus.InProgress, EventStatus.Draft },
            { EventStatus.InProgress, EventStatus.LookingForDate },
            { EventStatus.Closed, EventStatus.InProgress },
            { EventStatus.Closed, EventStatus.Cancelled },
            { EventStatus.Archived, EventStatus.Open },
            { EventStatus.Archived, EventStatus.Cancelled },
            { EventStatus.Cancelled, EventStatus.Draft },
            { EventStatus.Cancelled, EventStatus.Archived },
        };

    [Theory]
    [MemberData(nameof(OrganiserTransitions))]
    public void Organiser_CanPerformRegularTransitions(EventStatus from, EventStatus to)
    {
        var ev = MakeEvent(from);
        var result = EventStateMachine.TryTransition(ev, to, EventActor.Organiser, Today);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(to, ev.Status);
    }

    [Theory]
    [MemberData(nameof(OwnerOnlyTransitions))]
    public void OwnerOnlyTransitions_RejectOrganiser_AcceptOwner(EventStatus from, EventStatus to)
    {
        var ev = MakeEvent(from);

        var asOrganiser = EventStateMachine.TryTransition(ev, to, EventActor.Organiser, Today);
        Assert.Equal(TransitionOutcome.Forbidden, asOrganiser.Outcome);
        Assert.Equal(from, ev.Status);

        var asOwner = EventStateMachine.TryTransition(ev, to, EventActor.Owner, Today);
        Assert.True(asOwner.Succeeded, asOwner.Error);
        Assert.Equal(to, ev.Status);
    }

    [Theory]
    [MemberData(nameof(InvalidTransitions))]
    public void InvalidTransitions_AreNotAllowed_EvenForOwner(EventStatus from, EventStatus to)
    {
        var ev = MakeEvent(from);
        var result = EventStateMachine.TryTransition(ev, to, EventActor.Owner, Today);

        Assert.Equal(TransitionOutcome.NotAllowed, result.Outcome);
        Assert.Equal(from, ev.Status);
    }

    [Theory]
    [MemberData(nameof(OrganiserTransitions))]
    public void Members_CannotTransition(EventStatus from, EventStatus to)
    {
        var ev = MakeEvent(from);
        var result = EventStateMachine.TryTransition(ev, to, EventActor.Member, Today);

        Assert.Equal(TransitionOutcome.Forbidden, result.Outcome);
    }

    [Theory]
    [InlineData(null, "2030-06-20")]
    [InlineData("2030-06-20", null)]
    [InlineData("2030-06-20", "2030-06-19")]
    [InlineData("2030-06-14", "2030-06-20")]
    public void Opening_RequiresValidFutureDates(string? start, string? end)
    {
        var ev = MakeEvent(EventStatus.LookingForDate);
        ev.StartDate = start is null ? null : DateOnly.Parse(start);
        ev.EndDate = end is null ? null : DateOnly.Parse(end);

        var result = EventStateMachine.TryTransition(ev, EventStatus.Open, EventActor.Owner, Today);

        Assert.Equal(TransitionOutcome.GuardFailed, result.Outcome);
        Assert.Equal(EventStatus.LookingForDate, ev.Status);
        Assert.DoesNotContain(
            EventStatus.Open,
            EventStateMachine.GetAllowedTransitions(ev, EventActor.Owner, Today)
        );
    }

    [Fact]
    public void Opening_AllowsStartingToday()
    {
        var ev = MakeEvent(EventStatus.Draft, Today, Today);
        var result = EventStateMachine.TryTransition(
            ev,
            EventStatus.Open,
            EventActor.Organiser,
            Today
        );

        Assert.True(result.Succeeded, result.Error);
    }

    [Fact]
    public void Reset_BypassesDateGate_AndPausesAutoTransitions()
    {
        var ev = MakeEvent(EventStatus.Closed, Today.AddDays(-10), Today.AddDays(-3));

        var result = EventStateMachine.TryTransition(ev, EventStatus.Open, EventActor.Owner, Today);

        Assert.True(result.Succeeded, result.Error);
        Assert.True(ev.AutoTransitionsPaused);
        Assert.Null(EventStateMachine.GetDueAutoTransition(ev, Today));
    }

    [Fact]
    public void ManualTransition_AfterReset_ResumesAutoTransitions()
    {
        var ev = MakeEvent(EventStatus.Open, paused: true);

        EventStateMachine.TryTransition(ev, EventStatus.InProgress, EventActor.Organiser, Today);

        Assert.False(ev.AutoTransitionsPaused);
    }

    [Fact]
    public void AutoTransition_StartsOpenEventOnStartDate()
    {
        var ev = MakeEvent(EventStatus.Open, Today, Today.AddDays(3));
        Assert.Equal(EventStatus.InProgress, EventStateMachine.GetDueAutoTransition(ev, Today));
        Assert.Null(EventStateMachine.GetDueAutoTransition(ev, Today.AddDays(-1)));
    }

    [Fact]
    public void AutoTransition_ClosesInProgressEventAfterEndDate()
    {
        var ev = MakeEvent(EventStatus.InProgress, Today.AddDays(-3), Today);
        Assert.Null(EventStateMachine.GetDueAutoTransition(ev, Today));
        Assert.Equal(
            EventStatus.Closed,
            EventStateMachine.GetDueAutoTransition(ev, Today.AddDays(1))
        );
    }

    [Theory]
    [InlineData(EventStatus.Draft)]
    [InlineData(EventStatus.LookingForDate)]
    [InlineData(EventStatus.Closed)]
    [InlineData(EventStatus.Archived)]
    [InlineData(EventStatus.Cancelled)]
    public void AutoTransition_IgnoresOtherStates(EventStatus status)
    {
        var ev = MakeEvent(status, Today.AddDays(-10), Today.AddDays(-5));
        Assert.Null(EventStateMachine.GetDueAutoTransition(ev, Today));
    }

    [Fact]
    public void AllowedTransitions_ForOrganiser_ExcludeOwnerOnly()
    {
        var ev = MakeEvent(EventStatus.InProgress);
        Assert.Equal(
            [EventStatus.Closed, EventStatus.Archived],
            EventStateMachine.GetAllowedTransitions(ev, EventActor.Organiser, Today)
        );
        Assert.Equal(
            [EventStatus.Closed, EventStatus.Archived, EventStatus.Cancelled, EventStatus.Open],
            EventStateMachine.GetAllowedTransitions(ev, EventActor.Owner, Today)
        );
    }

    [Fact]
    public void AllowedTransitions_AreEmpty_ForFinalStates()
    {
        Assert.Empty(
            EventStateMachine.GetAllowedTransitions(
                MakeEvent(EventStatus.Archived),
                EventActor.Owner,
                Today
            )
        );
        Assert.Empty(
            EventStateMachine.GetAllowedTransitions(
                MakeEvent(EventStatus.Cancelled),
                EventActor.Owner,
                Today
            )
        );
    }
}
