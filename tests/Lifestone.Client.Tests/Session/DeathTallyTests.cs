using System.Collections.Immutable;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// <see cref="DeathTally"/> (M3 3.2 s0) over ACE's order (<c>Player_Death.cs</c> <c>OnDeath</c> 34-93, <c>Die</c> 150-242, <c>ThreadSafeTeleportOnDeath</c> 247-292): a tusker's kill (the victim notice first, then the Dead
/// motion, health 0x02E9 to 0, NumDeaths 0x02CD, the vitae 0x02C2, the purge 0x02C6, the corpse F745, the teleport F751 and the arrival, the lifestone protection, the vitals at 75%), a suicide (no victim notice) and the
/// respawn. The messages go through the real reducer, so the tally reads what the reducer publishes.
/// </summary>
public sealed class DeathTallyTests
{
    private const uint Player = ReducerHarness.PlayerGuid;
    private const uint Den = 0x01050375;
    private const uint Holtburg = 0xA9B40019;
    private const uint VitaeType = 0x800000 | 0x4000 | 0x2000 | 0x2;
    private const uint HealthCurrent = 2;
    private const uint StaminaCurrent = 4;
    private const uint ManaCurrent = 6;
    private const uint WeenieYouKilledYourself = 0x004A;

    private uint _event = 100;
    private byte _private = 10;
    private ushort _movement = 1;
    private ushort _position = 1;
    private ushort _teleport = 1;

    private readonly ReducerHarness _h = new();
    private readonly DeathTally _tally = new();

    private void Started(int numDeaths = 0)
    {
        _h.EnterWorld();
        _h.Reducer.OnPlayerDescription(
            _h.Context,
            new GameEventHeader(Player, 1, GameEventTypes.PlayerDescription),
            new PlayerDescriptionMsg
            {
                IntProperties = ImmutableDictionary<uint, int>.Empty.Add(DeathTally.NumDeathsProperty, numDeaths),
                Attributes = [.. Enumerable.Range(1, 6).Select(i => new AttributeDesc((uint)i, 0, 55, 0))],
                Vitals = [new VitalDesc(1, 0, 0, 0, 28), new VitalDesc(3, 0, 0, 0, 55), new VitalDesc(5, 0, 0, 0, 55)],
            });
        _tally.Start(new View(_h));
    }

    // --- ACE's messages, in the order Player_Death.cs sends them ---

    private void VictimNotice(string text = "You have been killed by Tusker Guard!") => Event(AceM3Writers.VictimNotification(Player, ++_event, text));

    private void Broadcast() => Send(AceM3Writers.PlayerKilled("Tester has been killed by Tusker Guard!", Player, 0x80000999));

    private void DeadMotion() =>
        _h.Reducer.OnUpdateMotion(_h.Context, new UpdateMotionMsg(Player, 1, ++_movement, 1, false, new ServerMotionState { MovementType = MovementType.Interpreted, Stance = 0x003D, Forward = WorldObjectTable.DeadMotionLowWord }));

    private void Vital(uint key, uint value) => Send(AceWireWriters.PrivateUpdateAttribute2ndLevel(++_private, key, value));

    private void NumDeaths(int value) => Send(AceWireWriters.PrivateUpdatePropertyInt(++_private, DeathTally.NumDeathsProperty, value));

    private void Vitae(float multiplier) =>
        Event(AceM3Writers.MagicUpdateEnchantment(Player, ++_event, new AceEnchantment(666, 1, 666, 0, 0, 0, -1, Player, 0f, 0f, 0.0, VitaeType, 0, multiplier, 0)));

    private void Purge() => Event(AceM3Writers.MagicPurgeEnchantments(Player, ++_event));

    // A timed beneficial enchantment of the player (additive, health): what a purge takes. The reducer publishes a purge only when it removed something.
    private void Buff() =>
        Event(AceM3Writers.MagicUpdateEnchantment(Player, ++_event, new AceEnchantment(5101, 1, 311, 0, 100, 0, 1800, Player, 0f, 0f, 0.0, 0x2000000 | 0x8000 | 0x1000 | 0x2, 1, 5f, 0)));

    private void Corpse(uint guid, string name, uint flags = WorldObjectTable.CorpseFlag)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), ReducerHarness.Position(Den, 12, 30));
        _h.Reducer.OnCreateObject(_h.Context, basis with { Weenie = basis.Weenie with { Name = name, ObjectDescriptionFlags = flags } }, isUpdate: false);
    }

    private void Teleport()
    {
        _h.Reducer.OnPlayerTeleport(_h.Context, new PlayerTeleportMsg(++_teleport));
    }

    private void Arrive(uint cell = Holtburg, float x = 80)
    {
        _h.Reducer.OnUpdatePosition(_h.Context, ReducerHarness.Move(Player, instance: 1, position: ++_position, teleport: _teleport, forcePosition: 1, cell: cell, x: x));
    }

    private void MagicLine(string text) => _h.Reducer.OnServerMessage(_h.Context, new ServerMessageMsg(text, (int)DeathTally.MagicChatType));

    private void Protected() => Event(AceWireWriters.WeenieError(Player, ++_event, DeathTally.LifestoneProtectsYou));

    private void Hit(uint damage = 35) => Event(AceM3Writers.DefenderNotification(Player, ++_event, "Tusker Guard", 4, 0.1, damage, 4, false, 0));

    private void Event(byte[] message) => Send(message);

    private void Send(byte[] message) => Assert.Equal(DispatchOutcome.Decoded, InboundDispatcher.Dispatch(_h.Context, message, _h.Reducer).Outcome);

    // --- the tests ---

    [Fact]
    public void A_tuskers_kill_in_ACE_s_order_is_counted_message_by_message()
    {
        Started();
        Buff();
        Hit(40);
        Hit(35);
        Vital(HealthCurrent, 14);
        Vital(HealthCurrent, 3);

        VictimNotice();
        Broadcast(); // the same death, named again by 0x019E: retail shows one line, the tally counts one notice
        DeadMotion();
        Vital(HealthCurrent, 0);
        NumDeaths(1);
        Send(AceWireWriters.PrivateUpdatePropertyInt(++_private, 139, 126)); // DeathLevel
        Send(AceWireWriters.PrivateUpdatePropertyInt(++_private, 129, 0)); // VitaeCpPool
        Vitae(0.95f);
        Purge();

        Assert.Equal(1, _tally.VictimNotices);
        Assert.Equal(1, _tally.DeadMotions);
        Assert.Equal(1, _tally.Deaths);
        Assert.Equal(5, _tally.VitaePercent);
        Assert.Equal(1, _tally.Purges);
        Assert.Equal(2, _tally.HitsTaken);
        Assert.Equal(75, _tally.DamageTaken);
        Assert.Equal(0.0, _tally.HealthMinPercent);
        Assert.Equal(0, _tally.DeathTeleports);
        Assert.False(_tally.HasArrival);

        Corpse(0x80000500, "Corpse of Tester");
        Teleport();
        Arrive();

        Assert.Equal(1, _tally.CorpsesSeen);
        Assert.Equal(0x80000500u, _tally.LastCorpseGuid);
        Assert.Equal(1, _tally.DeathTeleports);
        Assert.True(_tally.HasArrival);
        Assert.Equal(Holtburg, _tally.ArrivalCell);
        Assert.Equal(80f, _tally.ArrivalFrame.Origin.X);
    }

    [Fact]
    public void A_suicide_has_no_victim_notice_but_is_a_death_with_a_motion_a_purge_and_a_teleport()
    {
        Started();
        Buff();
        Send(AceWireWriters.HearSpeech("Oh cruel, cruel world!", "Tester", Player, 0)); // the five speech lines are not deaths
        DeadMotion();
        Vital(HealthCurrent, 0);
        NumDeaths(1);
        Event(AceWireWriters.WeenieError(Player, ++_event, WeenieYouKilledYourself));
        Vitae(0.95f);
        Purge();
        Corpse(0x80000501, "Corpse of Tester");
        Teleport();
        Arrive();

        Assert.Equal(0, _tally.VictimNotices);
        Assert.Equal(1, _tally.Deaths);
        Assert.Equal(1, _tally.DeadMotions);
        Assert.Equal(1, _tally.Purges);
        Assert.Equal(1, _tally.DeathTeleports);
        Assert.Equal(0, _tally.ProtectedHits); // YouKilledYourself is not the lifestone's word
        Assert.Equal(1, _tally.CorpsesSeen);
    }

    [Fact]
    public void A_suicide_then_a_tusker_death_is_two_deaths_and_one_victim_notice()
    {
        Started();
        DeadMotion();
        Vital(HealthCurrent, 0);
        NumDeaths(1);
        Teleport();
        Arrive();
        Vital(HealthCurrent, 21);

        VictimNotice();
        DeadMotion();
        Vital(HealthCurrent, 0);
        NumDeaths(2);
        Teleport();
        Arrive(Holtburg, 81);

        Assert.Equal(2, _tally.Deaths);
        Assert.Equal(1, _tally.VictimNotices);
        Assert.Equal(2, _tally.DeadMotions);
        Assert.Equal(2, _tally.DeathTeleports);
        Assert.Equal(81f, _tally.ArrivalFrame.Origin.X); // the last arrival
    }

    [Fact]
    public void The_respawn_counts_the_lifestone_s_protection_and_reads_the_vitals_after_the_arrival_as_shares_of_their_maxima()
    {
        Started();
        VictimNotice();
        DeadMotion();
        Vital(HealthCurrent, 0);
        NumDeaths(1);
        Teleport();

        // 75% sent before the arrival is not the arrival's (the tally waits for the position after the teleport).
        Vital(HealthCurrent, 28);
        Assert.True(double.IsNaN(_tally.VitalsAfterArrivalPercent));
        Assert.False(_tally.SampleVitalsAfterArrival()); // not arrived yet: nothing is read
        Arrive();
        Assert.True(double.IsNaN(_tally.FirstHealthAfterArrivalPercent));

        // ACE: 75% of each maximum, rounded (28 -> 21, 55 -> 41), one 0x02E9 each, 3 s after the arrival.
        Vital(HealthCurrent, 21);
        Assert.Equal(75.0, _tally.FirstHealthAfterArrivalPercent, 3);
        Assert.False(_tally.FirstVitalsSeen); // not all three yet
        Vital(StaminaCurrent, 41);
        Vital(ManaCurrent, 41);
        Assert.True(_tally.FirstVitalsSeen);
        Assert.Equal(41 * 100.0 / 55, _tally.FirstStaminaAfterArrivalPercent, 3);
        Assert.Equal(41 * 100.0 / 55, _tally.FirstManaAfterArrivalPercent, 3);
        Assert.True(double.IsNaN(_tally.VitalsAfterArrivalPercent)); // the judged share is read at the end of the vitals wait, not from the first updates
        Assert.True(_tally.SampleVitalsAfterArrival());
        Assert.Equal(75.0, _tally.HealthAfterArrivalPercent, 3);
        Assert.Equal(41 * 100.0 / 55, _tally.StaminaAfterArrivalPercent, 3);
        Assert.Equal(41 * 100.0 / 55, _tally.ManaAfterArrivalPercent, 3);
        Assert.InRange(_tally.VitalsAfterArrivalPercent, 74.0, 76.0);

        // Blows during the protection get ACE's 028A 0x0502 and no damage.
        Protected();
        Protected();
        Assert.Equal(2, _tally.ProtectedHits);
        Assert.Equal(0, _tally.HitsTaken);
        Assert.Equal(0, _tally.DamageTaken);
    }

    [Fact]
    public void The_lowest_health_share_is_kept_from_the_baseline()
    {
        Started();
        Assert.Equal(100.0, _tally.HealthMinPercent, 3); // the description's 28 of 28
        Vital(HealthCurrent, 14);
        Assert.Equal(50.0, _tally.HealthMinPercent, 3);
        Vital(HealthCurrent, 28); // healed: the minimum stays
        Assert.Equal(50.0, _tally.HealthMinPercent, 3);
        Vital(HealthCurrent, 7);
        Assert.Equal(25.0, _tally.HealthMinPercent, 3);
        Vital(StaminaCurrent, 1); // another vital does not move it
        Assert.Equal(25.0, _tally.HealthMinPercent, 3);
    }

    [Fact]
    public void A_corpse_counts_only_between_a_death_signal_and_that_deaths_arrival_the_dens_old_ones_are_stale()
    {
        Started();
        Corpse(0x80000701, "Corpse of Tester"); // an earlier run's corpse streams in before any death
        Assert.Equal(0, _tally.CorpsesSeen);
        Assert.Equal(1, _tally.StaleCorpses);

        DeadMotion();
        Corpse(0x80000702, "Corpse of Tester"); // this death's own corpse (ACE: after the Dead motion, before the teleport)
        Assert.Equal(1, _tally.CorpsesSeen);
        Assert.Equal(0x80000702u, _tally.LastCorpseGuid);
        Teleport();
        Corpse(0x80000703, "Corpse of Tester"); // between the F751 and the arrival is still this death's window
        Assert.Equal(2, _tally.CorpsesSeen);
        Arrive();

        Corpse(0x80000705, "Corpse of Tester"); // the arrival brings the den's old corpses into view: stale
        Corpse(0x80000704, "Corpse of Tester");
        Assert.Equal(2, _tally.CorpsesSeen);
        Assert.Equal(3, _tally.StaleCorpses);

        _tally.Reset();
        Assert.Equal(0, _tally.StaleCorpses);
    }

    [Fact]
    public void Without_a_death_the_dens_corpses_never_make_a_corpse_seen_so_the_live_assertion_cannot_pass_on_an_old_one()
    {
        Started();
        Corpse(0x80000711, "Corpse of Tester");
        Corpse(0x80000712, "Corpse of Tester");
        Assert.Equal(0, _tally.CorpsesSeen); // DeathLiveTests (d) asserts CorpsesSeen >= 1: the death's own corpse
        Assert.Equal(2, _tally.StaleCorpses);
    }

    [Fact]
    public void The_two_lifestone_magic_lines_end_the_protection_dispelled_or_expired_and_none_before_either()
    {
        Started();
        Assert.Equal("none", _tally.Protection);

        MagicLine("You're no longer protected by the Lifestone's magic!");
        Assert.Equal("expired", _tally.Protection);
        Assert.Equal((0, 1), (_tally.ProtectionDispels, _tally.ProtectionExpiries));

        MagicLine("Your actions have dispelled the Lifestone's magic!"); // the first teleport after a respawn (Player_Location.cs 702-703)
        Assert.Equal("dispelled", _tally.Protection); // the last line wins
        Assert.Equal((1, 1), (_tally.ProtectionDispels, _tally.ProtectionExpiries));
        Assert.Equal(0, _tally.ProtectedHits); // these are Magic chat lines, not the 028A 0x0502

        _h.Reducer.OnServerMessage(_h.Context, new ServerMessageMsg("Your actions have dispelled the Lifestone's magic!", 5)); // another chat type is not ACE's line
        _h.Reducer.OnServerMessage(_h.Context, new ServerMessageMsg("Your actions have dispelled the Lifestone's magic.", (int)DeathTally.MagicChatType));
        Assert.Equal((1, 1), (_tally.ProtectionDispels, _tally.ProtectionExpiries));

        _tally.Reset();
        Assert.Equal("none", _tally.Protection);
    }

    [Fact]
    public void The_judged_vitals_are_read_at_the_end_of_the_wait_the_first_updates_are_report_only_and_the_regeneration_that_follows_never_moves_them()
    {
        Started();
        VictimNotice();
        DeadMotion();
        NumDeaths(1);
        Teleport();
        Arrive();
        Vital(HealthCurrent, 21);
        Vital(StaminaCurrent, 41);
        Vital(ManaCurrent, 41);
        Assert.True(_tally.SampleVitalsAfterArrival());
        Assert.InRange(_tally.VitalsAfterArrivalPercent, 74.0, 76.0);

        Vital(HealthCurrent, 28); // a minute of regeneration during the regear
        Vital(StaminaCurrent, 55);
        Vital(ManaCurrent, 55);
        Assert.Equal(75.0, _tally.FirstHealthAfterArrivalPercent, 3);
        Assert.Equal(41 * 100.0 / 55, _tally.FirstStaminaAfterArrivalPercent, 3);
        Assert.Equal(41 * 100.0 / 55, _tally.FirstManaAfterArrivalPercent, 3);
        Assert.Equal(75.0, _tally.HealthAfterArrivalPercent, 3); // the wait-end values stay as they were read
        Assert.InRange(_tally.VitalsAfterArrivalPercent, 74.0, 76.0);

        // The next death's arrival is read on its own (a suicide, then the tuskers).
        DeadMotion();
        NumDeaths(2);
        Teleport();
        Assert.True(double.IsNaN(_tally.VitalsAfterArrivalPercent));
        Assert.True(double.IsNaN(_tally.FirstHealthAfterArrivalPercent));
        Arrive();
        Vital(HealthCurrent, 14);
        Assert.Equal(50.0, _tally.FirstHealthAfterArrivalPercent, 3);
        Assert.True(double.IsNaN(_tally.VitalsAfterArrivalPercent)); // not read until the scenario's wait ends
    }

    [Fact]
    public void A_stamina_or_mana_update_of_zero_before_ACE_s_restore_is_the_first_value_but_not_the_judged_one_M4_2_2_fix_R1()
    {
        // Live round 1 (BSD Mage, 30 evades, no suicide): vitals_pct=0.0. A vital's regeneration or drain tick reached the client after the arrival and before ACE's 75%, which comes 3 s after the teleport.
        Started();
        VictimNotice();
        DeadMotion();
        NumDeaths(1);
        Teleport();
        Arrive();
        Vital(StaminaCurrent, 0);
        Vital(ManaCurrent, 0);
        Vital(HealthCurrent, 21);
        Assert.True(_tally.FirstVitalsSeen);
        Assert.Equal(0.0, _tally.FirstStaminaAfterArrivalPercent, 3);
        Assert.Equal(0.0, _tally.FirstManaAfterArrivalPercent, 3);

        Vital(HealthCurrent, 21); // ACE's restore, three seconds after the teleport
        Vital(StaminaCurrent, 41);
        Vital(ManaCurrent, 41);
        Assert.True(_tally.SampleVitalsAfterArrival());
        Assert.InRange(_tally.VitalsAfterArrivalPercent, 74.0, 76.0);
        Assert.Equal(0.0, _tally.FirstStaminaAfterArrivalPercent, 3); // the first values stay report-only
    }

    [Fact]
    public void Only_the_players_own_corpse_counts()
    {
        Started();
        Corpse(0x80000600, "Corpse of Tusker Guard"); // another name
        Corpse(0x80000601, "Corpse of Tester", flags: 0); // the name without the corpse bit
        Corpse(0x80000602, "Corpse of Tester Two"); // a longer name
        Corpse(0x80000603, "corpse of tester"); // ordinal: the case matters
        Assert.Equal(0, _tally.CorpsesSeen);
        Assert.Equal(0u, _tally.LastCorpseGuid);

        DeadMotion(); // only a death's own corpse counts (R1); the death signal opens the window
        Corpse(0x80000604, "Corpse of Tester");
        Assert.Equal(1, _tally.CorpsesSeen);
        Assert.Equal(0x80000604u, _tally.LastCorpseGuid);

        Corpse(0x80000604, "Corpse of Tester"); // the same object created again (a refresh) is not a second corpse
        Assert.Equal(1, _tally.CorpsesSeen);
    }

    [Fact]
    public void The_NumDeaths_delta_starts_from_the_description_s_value()
    {
        Started(numDeaths: 3);
        Assert.Equal(0, _tally.Deaths);
        NumDeaths(4);
        Assert.Equal(1, _tally.Deaths);
    }

    [Fact]
    public void A_baseline_taken_before_the_description_is_the_description_s_value()
    {
        _h.EnterWorld();
        _tally.Start(new View(_h));
        _h.Reducer.OnPlayerDescription(
            _h.Context,
            new GameEventHeader(Player, 1, GameEventTypes.PlayerDescription),
            new PlayerDescriptionMsg { IntProperties = ImmutableDictionary<uint, int>.Empty.Add(DeathTally.NumDeathsProperty, 5) });
        Assert.Equal(0, _tally.Deaths);
        NumDeaths(6);
        Assert.Equal(1, _tally.Deaths);
    }

    [Fact]
    public void A_NumDeaths_update_before_the_description_is_the_baseline_not_a_death()
    {
        _h.EnterWorld();
        _tally.Start(new View(_h));
        NumDeaths(57); // the character's lifetime count, arriving before the PlayerDescription: the baseline, as FightTally takes its first total experience
        Assert.Equal(0, _tally.Deaths);
        Corpse(0x80000730, "Corpse of Tester"); // no death signal was noted: a corpse coming into view is stale, not this run's
        Assert.Equal(0, _tally.CorpsesSeen);
        Assert.Equal(1, _tally.StaleCorpses);

        NumDeaths(58);
        Assert.Equal(1, _tally.Deaths);
        Corpse(0x80000731, "Corpse of Tester"); // the death's own
        Assert.Equal(1, _tally.CorpsesSeen);
    }

    [Fact]
    public void The_vitae_percentage_follows_the_multiplier_and_is_zero_without_one()
    {
        Started();
        Assert.Equal(0, _tally.VitaePercent);
        Vitae(0.95f);
        Assert.Equal(5, _tally.VitaePercent);
        Vitae(0.60f); // ACE's cap: 40%
        Assert.Equal(40, _tally.VitaePercent);
        Purge(); // the purge keeps the vitae
        Assert.Equal(40, _tally.VitaePercent);
    }

    [Fact]
    public void A_purge_that_removes_nothing_is_counted_because_the_reducer_publishes_it_empty()
    {
        // ACE sends 0x02C6 at every player death (Player_Death.cs 211-216) even when the unbuffed player holds nothing a purge takes; the reducer changes nothing and publishes an empty
        // EnchantmentChanged (TODO(m3-purge-empty) closed), so the tally counts the unbuffed death's purge too. The bad-only purge (0x0312) is ACE-never and still not published when empty.
        Started();
        Vitae(0.95f);
        Purge();
        Assert.Equal(1, _tally.Purges);

        Buff();
        Purge();
        Assert.Equal(2, _tally.Purges);

        Buff();
        Event(AceM3Writers.MagicPurgeBadEnchantments(Player, ++_event)); // a beneficial buff survives the bad-only purge: nothing removed, nothing counted
        Assert.Equal(2, _tally.Purges);
    }

    [Fact]
    public void The_embedded_fight_tally_counts_the_blows_taken_and_resets_with_it()
    {
        Started();
        Hit(40);
        Event(AceM3Writers.EvasionDefenderNotification(Player, ++_event, "Tusker Guard"));
        Assert.Equal((1, 40L, 1), (_tally.HitsTaken, _tally.DamageTaken, _tally.Evades));
        Assert.Equal((1, 40L, 1), (_tally.Fight.HitsTaken, _tally.Fight.DamageTaken, _tally.Fight.Evades));

        _tally.Reset();

        Assert.Equal((0, 0L, 0), (_tally.HitsTaken, _tally.DamageTaken, _tally.Evades));
    }

    [Fact]
    public void Reset_zeroes_every_counter_and_Dispose_unsubscribes()
    {
        Started();
        Buff();
        VictimNotice();
        DeadMotion();
        Vital(HealthCurrent, 0);
        NumDeaths(1);
        Vitae(0.95f);
        Purge();
        Corpse(0x80000700, "Corpse of Tester");
        Teleport();
        Arrive();
        Protected();
        Vital(HealthCurrent, 21);

        _tally.Reset();

        Assert.Equal(0, _tally.VictimNotices + _tally.DeadMotions + _tally.Deaths + _tally.Purges + _tally.CorpsesSeen + _tally.DeathTeleports + _tally.ProtectedHits);
        Assert.Equal(5, _tally.VitaePercent); // the baseline is re-read: the registry still holds the vitae
        Assert.Equal(0u, _tally.LastCorpseGuid);
        Assert.False(_tally.HasArrival);
        Assert.Equal(0u, _tally.ArrivalCell);
        Assert.Equal(default, _tally.ArrivalFrame);
        Assert.True(double.IsNaN(_tally.VitalsAfterArrivalPercent));
        Assert.Equal(21 * 100.0 / 27, _tally.HealthMinPercent, 3); // the baseline: 21 of 27 (the vitae takes 5% off the maximum of 28)

        NumDeaths(2);
        Assert.Equal(1, _tally.Deaths);

        _tally.Dispose();
        Assert.False(_tally.Running);
        NumDeaths(3);
        VictimNotice();
        Assert.Equal(1, _tally.Deaths);
        Assert.Equal(0, _tally.VictimNotices);
    }

    private sealed class View(ReducerHarness h) : IGameSessionView
    {
        public GameState State => h.State;

        public IEventBus Events => h.Bus;

        public IActionApi Actions => null!;

        public PlayerTools Tools => null!;

        public SessionClock Clock => h.Clock;

        public ActionLog ActionLog { get; } = new();

        public LinkStats Link => default;
    }
}
