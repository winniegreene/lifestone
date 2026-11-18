using DatReaderWriter.Enums;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M6 14 recall unblock (live 2026-11-14 17:24:49 to 17:32:21 UTC, the climber's death in the Tusker Quarters). The chain: the loot closed a corpse whose 0196 and UseDone had come but not its contents, so the open stayed in flight
/// 20 s and its timeout owed a UseDone it had already taken; that debt dropped the next cast's own UseDone, the cast timed out and owed one in turn, and 42 UseDones were dropped in seven minutes while every cast, the corpse open's hold,
/// the recall and the last Health Tonic waited behind the casualties. Over <see cref="WorldActionRig"/> (the real Action API, ACE's own writers): an open whose view closed ends at once and owes nothing; an action the server visibly
/// answered claims its UseDone over an older debt; a debt paid while an action waited is that action's last (the cascade guard); a busy refusal never pays a debt; an opened container's hold is bounded; a stopped attempt's queued
/// try is withdrawn.
/// </summary>
public sealed class OwedUseDoneTests
{
    private const uint Player = WorldActionRig.Player;
    private const uint Heal = 103;
    private const uint Corpse = 0x80000201;
    private const uint Item1 = 0x80000211;
    private uint _event = 100;

    private static WorldActionRig Rig()
    {
        var r = new WorldActionRig();
        r.CombatModeIs(CombatModes.Magic);
        r.Spells(WorldActionRig.Spell(Heal, SpellType.Boost, SpellFlags.Beneficial)); // a spell with no proof on the wire: only its UseDone ends it (as Drain Health Other and Harm Other)
        var basis = ReducerHarness.Create(Corpse, ReducerHarness.Stamps(), null, name: "Corpse of Tusker Slave");
        r.H.R.Reducer.OnCreateObject(r.H.R.Context, basis, isUpdate: false);
        return r;
    }

    private void View(WorldActionRig r, params uint[] entries) =>
        r.H.R.Reducer.OnViewContents(r.H.R.Context, new GameEventHeader(Player, ++_event, GameEventTypes.ViewContents), new ViewContentsEvent(Corpse, [.. entries.Select(e => new ViewContentsEntry(e, 0))]));

    private void Close(WorldActionRig r) =>
        r.H.R.Reducer.OnCloseGroundContainer(r.H.R.Context, new GameEventHeader(Player, ++_event, GameEventTypes.CloseGroundContainer), new CloseGroundContainerEvent(Corpse));

    /// <summary>A cast ACE never answers: it times out in flight and owes its UseDone.</summary>
    private static ActionHandle OweOne(WorldActionRig r)
    {
        var lost = r.Api.Cast(Heal, ActionSource.Automation);
        r.Tick();
        r.Tick(ActionPolicy.CastSeconds + 0.1);
        Assert.Equal(ActionOutcomeKind.TimedOut, lost.Outcome!.Value.Kind);
        Assert.Contains(r.H.Log.Lines, l => l.Contains($"#{lost.Id} CastSpell ended (it timed out) without its UseDone: one is owed", StringComparison.Ordinal));
        return lost;
    }

    private static bool Logged(WorldActionRig r, string text) => r.H.Log.Lines.Any(l => l.Contains(text, StringComparison.Ordinal));

    [Fact]
    public void An_open_whose_view_closed_before_its_contents_ends_at_once_owes_nothing_and_the_next_cast_takes_its_own_UseDone()
    {
        // The chain's origin (17:24:49): the 0196 and the UseDone came, the loot closed the corpse before its F745s, and the open waited out its 20 s with every cast behind it.
        var r = Rig();
        var open = r.Api.OpenContainer(Corpse, ActionSource.Automation);
        r.Tick();
        View(r, Item1);
        r.UseDone();
        r.Tick();
        Assert.False(open.IsCompleted); // the contents have not come

        var close = r.Api.CloseContainer(Corpse, ActionSource.Automation);
        r.Tick();
        Close(r);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, close.Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Cancelled, open.Outcome!.Value.Kind);
        Assert.Contains("closed before its contents arrived", open.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Null(r.Api.BusyReason);

        var cast = r.Api.Cast(Heal, ActionSource.Automation);
        r.Tick();
        Assert.Single(r.H.Net.SelfCasts(Player)); // at once, not 20 s later
        r.Tick(1);
        r.UseDone();
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Sent, cast.Outcome!.Value.Kind); // no debt dropped it
        Assert.False(Logged(r, "a late UseDone"));
    }

    [Fact]
    public void An_open_whose_view_came_claims_its_UseDone_over_an_older_debt_and_holds_no_lane()
    {
        // 17:27:19.748: a bolt's hold let go at its timeout owed a UseDone; the corpse open sent in the same tick got its 0196 and its UseDone 70 ms later, and the debt took the UseDone, so the open held the Use lane 20 s.
        var r = Rig();
        var lost = OweOne(r);
        var open = r.Api.OpenContainer(Corpse, ActionSource.Automation);
        r.Tick();
        Assert.Single(r.H.Net.Of(FakeSendKind.Use));

        View(r, Item1);
        r.UseDone(); // capture C2's order: the 0196, the UseDone, the F745s
        r.Item(Item1, Corpse);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, open.Outcome!.Value.Kind);
        Assert.Null(r.Api.BusyReason); // its own UseDone came: nothing holds the Use lane
        Assert.True(Logged(r, $"is #{open.Id} OpenContainer's own: the server answered it since its send, after the debt of #{lost.Id} CastSpell began; ACE answers a player's actions in order, so that debt is void"));
        Assert.False(Logged(r, "a late UseDone"));
    }

    [Fact]
    public void A_debt_paid_while_a_cast_waited_is_its_last_the_cast_owes_none_and_the_next_cast_takes_its_own_UseDone()
    {
        // 17:27:49 to 17:32:21: each drain's own UseDone went to the last one's debt, each drain timed out and owed one in turn. The cascade guard stops it after one.
        var r = Rig();
        var lost = OweOne(r);
        var second = r.Api.Cast(Heal, ActionSource.Automation);
        r.Tick();
        r.Tick(1);
        r.UseDone(); // the second cast's own, but nothing proves it: the older debt takes it
        r.Tick();
        Assert.False(second.IsCompleted);
        Assert.True(Logged(r, $"#{lost.Id} CastSpell's, owed since"));
        Assert.True(Logged(r, $"#{second.Id} CastSpell waits for its own and, ending without one, owes none (the cascade guard)"));

        r.Tick(ActionPolicy.CastSeconds);
        Assert.Equal(ActionOutcomeKind.TimedOut, second.Outcome!.Value.Kind);
        Assert.True(Logged(r, $"#{second.Id} CastSpell ended (it timed out) without its UseDone, but a UseDone that came while it waited paid an older action's debt"));

        var third = r.Api.Cast(Heal, ActionSource.Automation);
        r.Tick();
        r.Tick(1);
        r.UseDone();
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Sent, third.Outcome!.Value.Kind); // before the guard, this one was dropped too, and so on to her death
    }

    [Fact]
    public void A_busy_refusal_never_pays_a_debt_it_is_the_refusal_of_the_action_just_sent()
    {
        var r = Rig();
        OweOne(r);
        var next = r.Api.Cast(Heal, ActionSource.Automation);
        r.Tick();
        r.UseDone(ActionApi.BusyUseDoneCode);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.ServerError, next.Outcome!.Value.Kind);
        Assert.Equal(ActionApi.BusyUseDoneCode, next.Outcome.Value.Code);
        Assert.True(Logged(r, "a UseDone 0x001D (You're too busy) is the refusal of the action just sent, never a late end"));
    }

    [Fact]
    public void An_opened_containers_hold_without_its_UseDone_is_let_go_at_its_bound_and_owes_none()
    {
        var r = Rig();
        OweOne(r);
        var open = r.Api.OpenContainer(Corpse, ActionSource.Automation);
        r.Tick();
        r.UseDone(); // before the 0196: nothing proves it is the open's, so the debt takes it
        View(r, Item1);
        r.Item(Item1, Corpse);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, open.Outcome!.Value.Kind);
        Assert.Contains("the Use lane holds until ACE's UseDone", r.Api.BusyReason, StringComparison.Ordinal);

        r.Tick(ActionPolicy.OpenContainerUseDoneSeconds + 0.05);
        Assert.Null(r.Api.BusyReason); // 2 s, not the open's 20 s timeout
        Assert.True(Logged(r, $"#{open.Id} OpenContainer ended (its hold was let go at its own bound) without its UseDone, but a UseDone that came while it waited paid an older action's debt"));
    }

    [Fact]
    public void A_stopped_attempts_try_still_waiting_its_turn_is_withdrawn_and_never_sent()
    {
        // 17:27:27: the fight stopped its Harm Other try for the trip home, but the cast stayed queued behind the held Use lane and went out 12 s later, the busy reason the recall failed on.
        var r = Rig();
        r.Api.OpenContainer(Corpse, ActionSource.Automation); // ACE never answers: the Use is out, and a cast waits behind it (one UseDone in the air names no request)
        r.Tick();
        var attempt = new CastAttempt(r.Api, r.H.State, r.H.Log);
        attempt.Start(new CastRequest { Levels = [Heal], Rules = CastRules.Travel(0), Purpose = "fight harm", Source = ActionSource.Automation }, r.H.R.Clock.Now);
        attempt.Step(r.H.R.Clock.Now);
        r.Tick();
        Assert.Empty(r.H.Net.SelfCasts(Player));
        var queued = attempt.Handle;
        Assert.NotNull(queued);
        Assert.Null(queued.SentAt);

        attempt.Stop("the fight ended", r.H.R.Clock.Now);
        Assert.Equal(ActionOutcomeKind.Cancelled, queued.Outcome!.Value.Kind);
        Assert.Contains("withdrawn before it was sent", queued.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.True(Logged(r, $"fight harm: the try #{queued.Id} had not been sent (it waited its turn in the Action API): withdrawn, nothing goes out"));

        r.Tick(ActionPolicy.OpenContainerSeconds + 1); // the Use times out: the lane frees
        r.Tick();
        Assert.Empty(r.H.Net.SelfCasts(Player));
        Assert.False(r.Api.Withdraw(queued, "again")); // complete: nothing to withdraw
    }
}
