using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 3.2 s2: <c>Identify</c> over the order ACE really answers (cache/m4/3-2/capture.md C3): one 0x00C9 for the guid 1 to 10 ms after the send, for a kit, a wielded mace and an NPC; the repeat on the current target answered
/// again at +1 ms; a missing guid answered with flags 0 and success 0; guid 0 and no answer. A failed roll (not seen live: <c>TODO(m4-appraise-fail-live)</c>) is Confirmed with the roll said to have failed and ACE's 5 s cooldown
/// said when it applies.
/// </summary>
public sealed class IdentifyTrackerTests
{
    private const uint Kit = 0x8000033F;
    private const uint Mace = 0x80000A01;
    private const uint Npc = 0x7A9B4027;
    private const uint Other = 0x80000A55;

    private static void Answer(WorldActionRig r, uint guid, AceAppraisal info) => r.Server((p, s) => AceVendorWriters.IdentifyResponse(p, s, guid, info));

    private static AceAppraisal KitInfo() => new()
    {
        Success = true,
        Ints = new Dictionary<uint, int>
        {
            [AppraisalIds.ItemType] = 0x1000,
            [AppraisalIds.Burden] = 5,
            [AppraisalIds.Value] = 10,
            [AppraisalIds.BoosterVital] = 2,
            [AppraisalIds.HealingBoost] = 20,
            [AppraisalIds.MaxStructure] = 25,
            [AppraisalIds.Structure] = 24,
        },
        Floats = new Dictionary<uint, double> { [100u] = 1.0 },
    };

    private static AceAppraisal MaceInfo() => new()
    {
        Success = true,
        Ints = new Dictionary<uint, int> { [AppraisalIds.Value] = 1200, [AppraisalIds.Burden] = 90 },
        Floats = new Dictionary<uint, double> { [100u] = 1.0 },
        Weapon = new AceWeaponProfile(2, 40, 0x29, 18, 0.4, 1.1, 0.8, 1.0, 1.15, 0),
    };

    private static AceAppraisal NpcInfo(bool success) => new()
    {
        Success = success,
        Ints = new Dictionary<uint, int> { [AppraisalIds.Level] = 12 },
        Strings = new Dictionary<uint, string> { [AppraisalIds.Name] = "Thelnoth Cort the Healer" },
        Creature = success ? new AceCreatureProfile(90, 100) : null,
        ArmorLevels = success ? [1, 2, 3, 4, 5, 6, 7, 8, 9] : null,
    };

    [Fact]
    public void A_kit_is_sent_once_and_completes_on_its_answer_with_the_appraisal()
    {
        var r = new WorldActionRig();
        var identify = r.Api.Identify(Kit, ActionSource.Ui);
        Assert.Equal(ActionKind.Identify, identify.Kind);
        r.Tick();

        Assert.Equal(Kit, Assert.Single(r.H.Net.Of(FakeSendKind.Identify)).Guid);
        Assert.False(identify.IsCompleted);
        r.Tick(0.005); // capture C3: every reply came 1 to 10 ms after the send
        Assert.False(identify.IsCompleted);

        Answer(r, Kit, KitInfo());
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, identify.Outcome!.Value.Kind);
        Assert.Null(identify.Outcome.Value.Detail);
        Assert.Equal(IdentifyResultKind.Appraised, identify.Result!.Kind);
        Assert.False(identify.Result.Cooldown);
        Assert.Equal(24, identify.Result.Appraisal.UsesLeft);
        Assert.Same(identify.Result.Appraisal, r.H.State.Appraisals.TryGet(Kit, out var held) ? held : null);
        Assert.Contains(r.H.ActionLog.All(), e => e.Kind == ActionKind.Identify && e.Outcome.Kind == ActionOutcomeKind.Confirmed);
    }

    [Fact]
    public void A_wielded_weapon_answers_with_its_weapon_profile()
    {
        var r = new WorldActionRig();
        var identify = r.Api.Identify(Mace, ActionSource.Ui);
        r.Tick();
        Answer(r, Mace, MaceInfo());
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, identify.Outcome!.Value.Kind);
        Assert.Equal(IdentifyResultKind.Appraised, identify.Result!.Kind);
        Assert.Equal(18, identify.Result.Appraisal.WeaponDamage);
        Assert.Equal(40, identify.Result.Appraisal.WeaponSpeed);
    }

    [Fact]
    public void A_creature_answers_with_its_profile_and_the_repeat_on_it_is_answered_again_with_no_cooldown()
    {
        var r = new WorldActionRig();
        var first = r.Api.Identify(Npc, ActionSource.Ui);
        r.Tick();
        Answer(r, Npc, NpcInfo(success: true));
        r.Tick();
        Assert.Equal(IdentifyResultKind.Appraised, first.Result!.Kind);
        Assert.True(first.Result.Appraisal.HasProfile);

        r.Tick(0.266); // capture C3: the repeat 266 ms later was answered at +1 ms, success 1, no roll
        var again = r.Api.Identify(Npc, ActionSource.Ui);
        r.Tick();
        Assert.Equal(2, r.H.Net.Of(FakeSendKind.Identify).Count);
        Assert.False(again.IsCompleted);
        Answer(r, Npc, NpcInfo(success: true));
        r.Tick(0.001);

        Assert.Equal(ActionOutcomeKind.Confirmed, again.Outcome!.Value.Kind);
        Assert.Equal(IdentifyResultKind.Appraised, again.Result!.Kind);
        Assert.False(again.Result.Cooldown);
        Assert.True(again.Result.Appraisal.Sequence > first.Result.Appraisal.Sequence);
    }

    [Fact]
    public void A_missing_guid_is_answered_with_flags_0_and_success_0_and_ends_Confirmed_as_not_found()
    {
        var r = new WorldActionRig();
        var identify = r.Api.Identify(0x80009999, ActionSource.Ui);
        r.Tick();
        Answer(r, 0x80009999, new AceAppraisal());
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, identify.Outcome!.Value.Kind);
        Assert.Contains("no such object", identify.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Equal(IdentifyResultKind.NotFound, identify.Result!.Kind);
        Assert.True(identify.Result.Appraisal.IsNotFound);
    }

    [Fact]
    public void Guid_0_is_refused_at_once_and_nothing_is_sent()
    {
        var r = new WorldActionRig();
        var identify = r.Api.Identify(0, ActionSource.Ui);

        Assert.Equal(ActionOutcomeKind.Refused, identify.Outcome!.Value.Kind);
        Assert.Contains("guid 0", identify.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Null(identify.Result);
        r.Tick(1);
        Assert.Empty(r.H.Net.Of(FakeSendKind.Identify));
    }

    [Fact]
    public void No_answer_in_5_seconds_ends_timed_out_with_no_result()
    {
        var r = new WorldActionRig();
        var identify = r.Api.Identify(Kit, ActionSource.Ui);
        r.Tick();
        r.Tick(4.9);
        Assert.False(identify.IsCompleted);

        r.Tick(0.2);
        Assert.Equal(ActionOutcomeKind.TimedOut, identify.Outcome!.Value.Kind);
        Assert.Null(identify.Result);
    }

    [Fact]
    public void An_answer_for_another_guid_or_one_from_before_the_send_does_not_complete_it()
    {
        var r = new WorldActionRig();
        Answer(r, Kit, KitInfo()); // an answer already held: not this request's
        var identify = r.Api.Identify(Kit, ActionSource.Ui);
        r.Tick();
        Answer(r, Other, KitInfo());
        r.Tick();
        Assert.False(identify.IsCompleted);

        Answer(r, Kit, KitInfo());
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, identify.Outcome!.Value.Kind);
    }

    [Fact]
    public void Two_guids_in_flight_each_take_their_own_answer_in_either_order()
    {
        var r = new WorldActionRig();
        var kit = r.Api.Identify(Kit, ActionSource.Ui);
        var mace = r.Api.Identify(Mace, ActionSource.Ui);
        r.Tick();
        Assert.Equal(2, r.H.Net.Of(FakeSendKind.Identify).Count);

        Answer(r, Mace, MaceInfo());
        r.Tick();
        Assert.False(kit.IsCompleted);
        Assert.Equal(IdentifyResultKind.Appraised, mace.Result!.Kind);
        Assert.NotNull(mace.Result.Appraisal.Weapon);

        Answer(r, Kit, KitInfo());
        r.Tick();
        Assert.Equal(24, kit.Result!.Appraisal.UsesLeft);
    }

    [Fact]
    public void Two_identifies_of_one_guid_leave_one_after_the_other_so_each_takes_its_own_answer()
    {
        var r = new WorldActionRig();
        var first = r.Api.Identify(Kit, ActionSource.Ui);
        var second = r.Api.Identify(Kit, ActionSource.Ui);
        r.Tick();
        Assert.Single(r.H.Net.Of(FakeSendKind.Identify));

        Answer(r, Kit, KitInfo());
        r.Tick();
        Assert.True(first.IsCompleted);
        Assert.False(second.IsCompleted);
        Assert.Equal(2, r.H.Net.Of(FakeSendKind.Identify).Count); // the second left only once the first had its answer

        Answer(r, Kit, KitInfo());
        r.Tick();
        Assert.True(second.IsCompleted);
        Assert.True(second.Result!.Appraisal.Sequence > first.Result!.Appraisal.Sequence);
    }

    [Fact]
    public void A_failed_roll_is_Confirmed_with_the_roll_said_to_have_failed_and_the_tables_kept()
    {
        var r = new WorldActionRig();
        var identify = r.Api.Identify(Npc, ActionSource.Ui);
        r.Tick();
        Answer(r, Npc, NpcInfo(success: false));
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, identify.Outcome!.Value.Kind);
        Assert.True(identify.Outcome.Value.Succeeded);
        Assert.Equal("the roll failed", identify.Outcome.Value.Detail);
        Assert.Equal(IdentifyResultKind.Failed, identify.Result!.Kind);
        Assert.False(identify.Result.Cooldown);
        Assert.False(identify.Result.Appraisal.Success);
        Assert.Equal("Thelnoth Cort the Healer", identify.Result.Appraisal.Name);
        Assert.False(identify.Result.Appraisal.HasProfile);
    }

    [Fact]
    public void A_repeat_within_5_seconds_of_a_failure_is_the_cooldown_s_answer_and_one_after_it_is_a_new_roll()
    {
        var r = new WorldActionRig();
        var failed = r.Api.Identify(Npc, ActionSource.Ui);
        r.Tick();
        Answer(r, Npc, NpcInfo(success: false));
        r.Tick();
        Assert.False(failed.Result!.Cooldown);

        r.Tick(2);
        var repeat = r.Api.Identify(Npc, ActionSource.Ui);
        r.Tick();
        Answer(r, Npc, NpcInfo(success: false)); // ACE answers failed with no roll
        r.Tick();
        Assert.Equal(IdentifyResultKind.Failed, repeat.Result!.Kind);
        Assert.True(repeat.Result.Cooldown);
        Assert.Contains("within 5 s", repeat.Outcome!.Value.Detail, StringComparison.Ordinal);
        Assert.Contains("no new roll", repeat.Outcome.Value.Detail, StringComparison.Ordinal);

        r.Tick(5.5);
        var later = r.Api.Identify(Npc, ActionSource.Ui);
        r.Tick();
        Answer(r, Npc, NpcInfo(success: false)); // rolled again, lost again
        r.Tick();
        Assert.Equal(IdentifyResultKind.Failed, later.Result!.Kind);
        Assert.False(later.Result.Cooldown);
        Assert.Equal("the roll failed", later.Outcome!.Value.Detail);

        var won = r.Api.Identify(Npc, ActionSource.Ui);
        r.Tick();
        Answer(r, Npc, NpcInfo(success: true));
        r.Tick();
        Assert.Equal(IdentifyResultKind.Appraised, won.Result!.Kind);
        Assert.False(won.Result.Cooldown);
    }

    [Fact]
    public void It_is_refused_when_the_player_is_not_in_the_world()
    {
        var r = new WorldActionRig(present: false);
        var identify = r.Api.Identify(Kit, ActionSource.Ui);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Refused, identify.Outcome!.Value.Kind);
        Assert.Empty(r.H.Net.Of(FakeSendKind.Identify));
    }

    [Fact]
    public void The_tally_counts_the_appraised_the_failed_and_the_missing_and_ignores_the_rest()
    {
        var r = new WorldActionRig();
        var tally = new AppraisalTally();
        var kit = r.Api.Identify(Kit, ActionSource.Ui);
        r.Tick();
        Answer(r, Kit, KitInfo());
        r.Tick();
        tally.Record(kit);

        var npc = r.Api.Identify(Npc, ActionSource.Ui);
        r.Tick();
        Answer(r, Npc, NpcInfo(success: false));
        r.Tick();
        tally.Record(npc);

        var repeat = r.Api.Identify(Npc, ActionSource.Ui);
        r.Tick();
        Answer(r, Npc, NpcInfo(success: false));
        r.Tick();
        tally.Record(repeat);

        var missing = r.Api.Identify(0x80009999, ActionSource.Ui);
        r.Tick();
        Answer(r, 0x80009999, new AceAppraisal());
        r.Tick();
        tally.Record(missing);

        tally.Record(r.Api.Identify(0, ActionSource.Ui)); // refused
        var silent = r.Api.Identify(Mace, ActionSource.Ui);
        r.Tick();
        r.Tick(6);
        tally.Record(silent); // timed out

        Assert.Equal((1, 2, 1, 1, 4), (tally.Appraised, tally.Failed, tally.Cooldowns, tally.NotFound, tally.Answered));
    }
}
