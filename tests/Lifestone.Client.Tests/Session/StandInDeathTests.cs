using System.Collections.Immutable;
using System.Numerics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.StandIn;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M3 3.2 db: the stand-in's death (<c>StandInWorld.Death.cs</c>). A tusker that attacks hits or is evaded within reach and the stand-in sends ACE's messages in ACE's order (the health, the defender notice, the hit sound and the splatter);
/// the player's death comes in the order of <c>Player_Death.cs</c> (the victim notice, the Dead motion, the health and NumDeaths, DeathLevel and VitaeCpPool, the vitae, the purge, then the corpse and the teleport to the lifestone after
/// the Dead animation plus a second, the stance, the protection and the vitals at 75% three seconds later); a suicide is five lines three seconds apart and then the same death without the victim notice; a lifestone Use binds the
/// player. s0's <see cref="DeathTally"/> reads the same messages, so the sentences are checked against it. Nothing here is ACE: the offline stand-in is proved against ACE's code, the live legs prove ACE.
/// </summary>
public sealed class StandInDeathTests
{
    private const uint Tusker = 0x80000C02;
    private const uint Lifestone = 0x80000C01;

    private static StandInVisit Visit(uint health = 52, int level = 30, StandInPlayerStats? stats = null) => new()
    {
        EnterAfter = 0.2,
        UnhideAfter = 1.0,
        Stats = stats ?? new StandInPlayerStats { Health = health, Level = level },
    };

    private sealed class Rig : IDisposable
    {
        public Rig(uint health = 52, int level = 30, StandInPlayerStats? stats = null)
        {
            Cast = new CastRig(Visit(health, level, stats));
            Tally = new DeathTally();
            Tally.Start(Cast.Session);
            Server = StandInSessions.Server(Cast.Session.Reducer);
            Cast.Begin();
        }

        public CastRig Cast { get; }

        public DeathTally Tally { get; }

        public StandInServer Server { get; }

        public StandInWorld World => Cast.World;

        public GameSession Session => Cast.Session;

        public void Run(double seconds) => Cast.Run(seconds);

        public string[] Tags => Cast.Tags;

        public void PlaceTusker(Vector3 offset, bool attacks = true)
        {
            Assert.NotNull(Server.PlaceNear(StandInTusker.Make(Tusker, default, attacks: attacks), offset));
            Run(0.5);
            Assert.True(Session.State.Objects.Contains(Tusker));
        }

        public void PlaceLifestone(Vector3 offset)
        {
            Assert.NotNull(Server.PlaceNear(StandInLifestone.Make(Lifestone), offset));
            Run(0.5);
            Assert.True(Session.State.Objects.Contains(Lifestone));
        }

        public void Bind()
        {
            var use = Session.Actions.Use(Lifestone, ActionSource.Host);
            Run(5.0);
            Assert.True(use.IsCompleted, use.Summary);
            Assert.Equal(ActionOutcomeKind.Confirmed, use.Outcome?.Kind);
            Assert.True(Server.Bound);
        }

        public IEnumerable<string> Lines(uint chatType) => Cast.Chat.Where(l => l.ChatType == chatType).Select(l => l.Text);

        public void Dispose()
        {
            Tally.Dispose();
            Cast.Dispose();
        }
    }

    private static int IndexOf(string[] tags, string tag, int from = 0) => Array.IndexOf(tags, tag, from);

    // --- the tusker's attack ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_tusker_that_attacks_takes_the_stance_swings_and_strikes_every_two_to_three_seconds_within_reach()
    {
        using var rig = new Rig();
        var motions = new List<(double At, ushort Stance, ushort? Forward, int Commands)>();
        rig.Session.Events.Subscribe((in ObjectMotionChanged e) =>
        {
            if (e.Guid == Tusker)
            {
                motions.Add((rig.Session.Clock.Now, e.Motion.Stance, e.Motion.Forward, e.Motion.Commands.IsDefault ? 0 : e.Motion.Commands.Length));
            }
        });
        rig.PlaceTusker(new Vector3(0f, 1f, 0f));
        rig.Cast.Begin();
        rig.Run(12.0);

        Assert.True(motions.Count >= 3, "the stance and at least two swings");
        Assert.Equal((ushort)0x3C, motions[0].Stance); // HandCombat: DoAttackStance
        Assert.Equal(0, motions[0].Commands);
        var swings = motions.Where(m => m.Commands == 1).ToList();
        Assert.True(swings.Count >= 2);
        for (var i = 1; i < swings.Count; i++)
        {
            Assert.InRange(swings[i].At - swings[i - 1].At, StandInWorld.AttackIntervalMin - 0.15, StandInWorld.AttackIntervalMax + 0.15);
        }

        Assert.True(rig.Server.TuskerHits + rig.Server.TuskerEvades >= 2);
        Assert.Equal(rig.Server.TuskerHits - rig.World.PlayerDeaths, rig.Tally.HitsTaken); // the killing blow is the victim notice, not a defender notice
        Assert.Equal(rig.Server.TuskerEvades, rig.Tally.Evades);
    }

    [Fact]
    public void A_tusker_that_does_not_attack_or_stands_out_of_reach_never_does_and_the_players_health_stays()
    {
        using var still = new Rig();
        still.PlaceTusker(new Vector3(0f, 1f, 0f), attacks: false);
        still.Cast.Begin();
        still.Run(20.0);
        Assert.DoesNotContain("01B2", still.Tags);
        Assert.DoesNotContain("01B4", still.Tags);
        Assert.Equal(0, still.Server.TuskerHits + still.Server.TuskerEvades);

        using var far = new Rig();
        far.PlaceTusker(new Vector3(0f, StandInWorld.AttackReachMetres + 3f, 0f));
        far.Cast.Begin();
        far.Run(20.0);
        Assert.DoesNotContain("01B2", far.Tags);
        Assert.DoesNotContain("01B4", far.Tags);
        Assert.DoesNotContain("F74C", far.Tags); // no swing, no stance
        Assert.Equal(0, far.Server.TuskerHits + far.Server.TuskerEvades);
    }

    [Fact]
    public void A_hit_is_the_health_the_defender_notice_the_hit_sound_the_splatter_and_the_wound_sound_in_ACEs_order_and_an_evade_is_the_evasion_notice()
    {
        using var rig = new Rig();
        rig.PlaceTusker(new Vector3(0f, 1f, 0f));
        rig.Cast.Begin();
        rig.Run(20.0);

        var tags = rig.Tags;
        var hit = IndexOf(tags, "01B2");
        Assert.True(hit > 0, "a hit in 20 s");
        Assert.Equal("02E9", tags[hit - 1]); // UpdateVitalDelta(Health, -amount) comes first
        Assert.Equal(["01B2", "F750", "F755", "F750"], tags[hit..(hit + 4)]); // the notice, the hit sound, the splatter, the wound sound (a tenth of the health or more)
        Assert.True(rig.Tally.HitsTaken >= 1);
        Assert.InRange(rig.Tally.DamageTaken, StandInWorld.DamageMin, StandInWorld.DamageMax * rig.Tally.HitsTaken);

        var combat = rig.Cast.Chat.Where(l => l.ChatType == 0x15).Select(l => l.Text).ToList();
        Assert.Contains(combat, t => t.StartsWith("Tusker Slave ", StringComparison.Ordinal) && t.Contains("points of piercing damage", StringComparison.Ordinal) || t.StartsWith("You evaded Tusker Slave", StringComparison.Ordinal));
    }

    // --- the player's death -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_death_by_a_blow_plays_ACEs_order_the_notice_the_dead_motion_the_updates_the_vitae_the_purge_then_the_corpse_the_teleport_and_the_vitals()
    {
        using var rig = new Rig(health: 5);
        rig.PlaceTusker(new Vector3(0f, 1f, 0f));
        rig.Cast.Begin();
        rig.Run(40.0);

        Assert.Equal(1, rig.World.PlayerDeaths);
        var tags = rig.Tags;
        var notice = IndexOf(tags, "01AC");
        Assert.True(notice > 0);
        Assert.Equal("02E9", tags[notice - 1]);
        Assert.Equal(["01AC", "019E", "F74C", "02E9", "02CD", "02CD", "02CD", "02C2", "02C6"], tags[notice..(notice + 9)]);
        Assert.Equal(1, rig.Tally.VictimNotices);
        Assert.Equal(1, rig.Tally.Deaths);
        Assert.Equal(1, rig.Tally.DeadMotions);
        Assert.Equal(5, rig.Tally.VitaePercent);
        Assert.Equal(1, rig.Tally.CorpsesSeen);
        Assert.Equal(1, rig.Tally.DeathTeleports);
        Assert.True(rig.Tally.HasArrival);
        Assert.Equal(30, rig.Cast.Q.TryGetInt(139, out var deathLevel) ? deathLevel : -1); // DeathLevel
        Assert.Equal(0, rig.Cast.Q.TryGetInt(129, out var pool) ? pool : -1); // VitaeCpPool
        Assert.True(rig.Cast.Q.TryGetVitae(out var vitae));
        Assert.Equal(0.95f, vitae.Enchantment.StatModValue, 3);

        // The timeline after the notice: the corpse a second after the Dead animation, the vitals three seconds after the arrival.
        var death = rig.Cast.Timeline.First(e => e.Tag == "01AC").At;
        var corpse = rig.Cast.Timeline.First(e => e.Tag == "F745" && e.At > death).At;
        Assert.Equal(StandInWorld.PlayerDeathAnimationSeconds + 1.0, corpse - death, 0.1);
        var vitals = rig.Cast.Timeline.Where(e => e.Tag == "02E9" && e.At > corpse + 1.0).ToList();
        Assert.Equal(3, vitals.Count);
        Assert.Equal(StandInWorld.RevivalSeconds, vitals[0].At - corpse, 0.1);

        // 75% of each maximum (the maximum has the vitae applied), and the player stands up in NonCombat.
        foreach (var maxId in new uint[] { 1, 3, 5 })
        {
            Assert.True(rig.Cast.Q.TryGetVitalLevels(maxId, out var current, out var max));
            Assert.Equal((uint)Math.Round(max * 0.75f, MidpointRounding.ToEven), current);
        }

        Assert.InRange(rig.Tally.VitalsAfterArrivalPercent, 70.0, 80.0);
        Assert.Equal(CombatModes.NonCombat, rig.Cast.Q.CombatMode);
        Assert.False(rig.World.Dying);
        Assert.Contains(rig.Lines(0), l => l.Contains("Tusker Slave", StringComparison.Ordinal)); // the notice's line (chat type 0)
        Assert.Contains("You have retained all your items. You do not need to recover your corpse!", rig.Lines(0));

        // The corpse: the bit, ACE's name, the player's Setup, in the Dead pose.
        Assert.True(rig.Session.State.Objects.TryGet(rig.Tally.LastCorpseGuid, out var body));
        Assert.Equal("Corpse of " + rig.Session.State.Self.Name, body.Name);
        Assert.NotEqual(0u, body.Weenie.ObjectDescriptionFlags & WorldObjectTable.CorpseFlag);
        Assert.Equal(StandInVisit.HumanSetup, body.Physics.SetupId);
        Assert.Equal(WorldObjectTable.DeadMotionLowWord, body.Motion?.Forward);
    }

    [Fact]
    public void The_teleport_goes_to_the_lifestone_the_player_used_the_protection_turns_blows_away_and_ends_after_a_minute()
    {
        using var rig = new Rig(health: 5);
        rig.PlaceLifestone(new Vector3(1f, 0f, 0f));
        var boundAt = rig.Session.State.Self.Position!.Value;
        rig.Bind();
        Assert.Equal(StandInWorld.PlaceholderUseMessage, rig.Lines(FightTally.MagicChatType).Last(l => l.StartsWith("You have bound", StringComparison.Ordinal)));
        rig.PlaceTusker(new Vector3(0f, 1f, 0f));
        rig.Run(25.0);

        Assert.Equal(1, rig.Tally.VictimNotices);
        Assert.True(rig.Tally.HasArrival);
        Assert.Equal(boundAt.CellId, rig.Tally.ArrivalCell);
        Assert.Equal(boundAt.Origin.X, rig.Tally.ArrivalFrame.Origin.X, 0.01);
        Assert.Equal(DeathRespawn.Lifestone, DeathLegVerdict.ClassifyRespawn(rig.Tally, rig.Session.State.Objects));

        // The tusker is still in reach: while the protection holds every strike is the 0x028A 0x0502 with the blue shield, and no health goes.
        var hitsBefore = rig.Server.TuskerHits;
        var healthBefore = rig.Cast.Q.TryGetVital(1, out var health) ? health.Current : 0;
        Assert.True(rig.Server.UnderProtection);
        rig.Cast.Begin();
        rig.Run(20.0);
        Assert.True(rig.Tally.ProtectedHits >= 2);
        Assert.Equal(rig.Server.ProtectedBlows, rig.Tally.ProtectedHits);
        Assert.Equal(hitsBefore, rig.Server.TuskerHits);
        Assert.Equal(healthBefore, rig.Cast.Q.TryGetVital(1, out var after) ? after.Current : 0);
        Assert.Contains(rig.Cast.Scripts, s => s.Script.ScriptType == 0x33);

        // A minute after the arrival the protection ends with the Magic line; the next strikes are real.
        for (var second = 0; second < 70 && !rig.Lines(FightTally.MagicChatType).Contains("You're no longer protected by the Lifestone's magic!"); second++)
        {
            rig.Run(1.0);
        }

        Assert.False(rig.Server.UnderProtection);
        Assert.Contains("You're no longer protected by the Lifestone's magic!", rig.Lines(FightTally.MagicChatType));
        rig.Run(30.0);
        Assert.True(rig.Server.TuskerHits > hitsBefore); // with the protection gone the strikes are real again (and, at 5 health, kill)
        Assert.Equal(2, rig.World.PlayerDeaths);
    }

    [Fact]
    public void The_check_hook_dispels_the_protection_as_a_teleport_does_and_the_line_is_the_same()
    {
        using var rig = new Rig(health: 5);
        rig.PlaceLifestone(new Vector3(1f, 0f, 0f));
        rig.Bind();
        var suicide = rig.Session.Actions.Suicide(ActionSource.Host);
        rig.Run(35.0);
        Assert.True(suicide.IsCompleted, suicide.Summary);
        Assert.True(rig.Server.UnderProtection);

        rig.Server.DispelProtectionAsATeleportDoes();
        rig.Run(2.0);
        Assert.False(rig.Server.UnderProtection);
        Assert.Contains("Your actions have dispelled the Lifestone's magic!", rig.Lines(FightTally.MagicChatType));
    }

    [Fact]
    public void A_teleport_dispels_the_protection_with_ACEs_line()
    {
        using var rig = new Rig(health: 5);
        rig.PlaceLifestone(new Vector3(1f, 0f, 0f));
        rig.Bind();
        var suicide = rig.Session.Actions.Suicide(ActionSource.Host);
        rig.Run(35.0);
        Assert.True(suicide.IsCompleted, suicide.Summary);
        Assert.True(rig.Server.UnderProtection);

        var place = rig.Session.Tools.Place(TeleportCatalog.Default().Find("Holtburg")!, ActionSource.Host);
        rig.Run(2.0);
        Assert.True(place.IsCompleted, place.Summary);
        Assert.False(rig.Server.UnderProtection);
        Assert.Contains("Your actions have dispelled the Lifestone's magic!", rig.Lines(FightTally.MagicChatType));
    }

    [Fact]
    public void Vitae_starts_at_95_percent_loses_five_percent_a_death_and_never_goes_below_the_levels_floor()
    {
        Assert.Equal(0.99f, StandInWorld.MinimumVitae(1), 3);
        Assert.Equal(0.94f, StandInWorld.MinimumVitae(3), 3);
        Assert.Equal(0.60f, StandInWorld.MinimumVitae(30), 3);
        Assert.Equal(0.60f, StandInWorld.MinimumVitae(275), 3);

        using var rig = new Rig();
        for (var death = 1; death <= 3; death++)
        {
            var suicide = rig.Session.Actions.Suicide(ActionSource.Host);
            rig.Run(30.0);
            Assert.True(suicide.IsCompleted, suicide.Summary);
            Assert.Equal(death, rig.Tally.Deaths);
            Assert.Equal(5 * death, rig.Tally.VitaePercent);
        }
    }

    // --- the suicide --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_suicide_is_five_lines_three_seconds_apart_then_the_death_without_the_notice_and_a_second_one_is_too_busy()
    {
        using var rig = new Rig();
        var speech = new List<(double At, string Text)>();
        rig.Session.Events.Subscribe((in ChatReceived e) =>
        {
            if (e.Line.Kind == ChatKind.Speech)
            {
                speech.Add((rig.Session.Clock.Now, e.Line.Text));
            }
        });
        var suicide = rig.Session.Actions.Suicide(ActionSource.Host);
        rig.Run(20.0);

        Assert.Equal(StandInWorld.SuicideLines, speech.Select(s => s.Text));
        for (var i = 1; i < speech.Count; i++)
        {
            Assert.Equal(3.0, speech[i].At - speech[i - 1].At, 0.05);
        }

        Assert.True(suicide.IsCompleted, suicide.Summary);
        Assert.Equal(ActionOutcomeKind.Confirmed, suicide.Outcome?.Kind);
        var tags = rig.Tags;
        Assert.DoesNotContain("01AC", tags);
        Assert.DoesNotContain("019E", tags);
        var dead = IndexOf(tags, "F74C");
        Assert.Equal(["02E9", "F74C", "02E9", "02CD", "028A", "02CD", "02CD", "02C2", "02C6"], tags[(dead - 1)..(dead + 8)]); // the health to 0, the motion, the update, NumDeaths, YouKilledYourself, DeathLevel, VitaeCpPool, the vitae, the purge
        Assert.Equal(0, rig.Tally.VictimNotices);
        Assert.Equal(1, rig.Tally.Deaths);
        Assert.Equal(1, rig.Tally.DeadMotions);
        Assert.Contains(rig.Cast.Chat, l => l.Kind == ChatKind.WeenieError && l.ChatType == StandInWorld.YouKilledYourself);

        // While dead or teleporting: YoureTooBusy, and nothing else.
        var again = rig.Session.Actions.Suicide(ActionSource.Host);
        rig.Run(2.0);
        Assert.Equal(ActionOutcomeKind.Refused, again.Outcome?.Kind);
        Assert.Equal(1, rig.World.PlayerDeaths);
    }

    [Fact]
    public void A_suicide_in_progress_is_dropped_when_asked_again_and_the_buffs_go_with_the_death()
    {
        // Two finite buffs (the live-shaped set is all lasting, which a purge keeps).
        var buffed = new StandInPlayerStats
        {
            Level = 30,
            Enchantments =
            [
                new EnchantmentDesc(1344, 1, 11, 0, 50, 0.0, 1800.0, 0x50000001, 0f, 0f, 0.0, 0x2009001, 2, 5f, 0, EnchantmentBucket.Additive),
                new EnchantmentDesc(1360, 1, 12, 0, 50, 0.0, 1800.0, 0x50000001, 0f, 0f, 0.0, 0x2009002, 3, 4f, 0, EnchantmentBucket.Additive),
            ],
        };
        using var rig = new Rig(stats: buffed);
        Assert.True(rig.Cast.Q.Enchantments.Length > 0);
        rig.Session.Actions.Suicide(ActionSource.Host);
        rig.Run(5.0);
        rig.Session.Actions.Suicide(ActionSource.Host);
        rig.Run(20.0);

        Assert.Equal(1, rig.World.PlayerDeaths);
        Assert.Equal(1, rig.Tally.Purges); // 0x02C6 removed something
        Assert.Equal(1, rig.Cast.Q.Enchantments.Count(e => e.SpellId == EnchantmentRules.VitaeSpellId)); // only the vitae is left
        Assert.DoesNotContain(rig.Cast.Q.Enchantments, e => e.SpellId != EnchantmentRules.VitaeSpellId);
    }

    // --- the lifestone ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_lifestone_use_is_the_sound_the_sanctuary_motion_then_the_line_and_half_the_stamina_and_a_combat_mode_goes_to_peace_first()
    {
        using var rig = new Rig();
        rig.PlaceLifestone(new Vector3(1f, 0f, 0f));
        var stone = rig.Session.State.Objects.TryGet(Lifestone, out var obj) ? obj : throw new InvalidOperationException();
        Assert.NotEqual(0u, stone.Weenie.ObjectDescriptionFlags & WorldObjectTable.LifestoneFlag);
        Assert.Equal(StandInLifestone.Name, stone.Name);
        Assert.Equal(0u, stone.Weenie.ItemType & 0x10); // not a creature
        rig.Cast.Mode(CombatMode.Magic);
        Assert.True(rig.Cast.Q.TryGetVitalLevels(3, out var staminaBefore, out _));
        rig.Cast.Begin();
        var use = rig.Session.Actions.Use(Lifestone, ActionSource.Host);
        rig.Run(6.0);

        Assert.True(use.IsCompleted, use.Summary);
        Assert.Equal(["F74C", "02CD", "F750", "F74C"], rig.Tags[..4].Length == 4 ? rig.Tags[..4] : []); // peace first (the motion, the combat mode), the lifestone sound, the Sanctuary motion
        var tags = rig.Tags;
        Assert.Equal(["F7E0", "02E9", "01C7"], tags[^3..]); // the line, the stamina, UseDone
        Assert.Equal(CombatModes.NonCombat, rig.Cast.Q.CombatMode);
        Assert.True(rig.Server.Bound);
        Assert.True(rig.Cast.Q.TryGetVitalLevels(3, out var staminaAfter, out _));
        Assert.Equal((uint)Math.Round(staminaBefore / 2f), staminaAfter);
    }

    [Fact]
    public void A_lifestone_too_far_away_answers_you_have_moved_too_far_and_binds_nothing()
    {
        using var rig = new Rig();
        rig.PlaceLifestone(new Vector3(StandInWorld.LifestoneUseRadiusMetres + 5f, 0f, 0f));
        rig.Cast.Begin();
        rig.Session.Actions.Use(Lifestone, ActionSource.Host);
        rig.Run(6.0);

        Assert.False(rig.Server.Bound);
        Assert.Contains(rig.Cast.Chat, l => l.Kind == ChatKind.WeenieError && l.ChatType == StandInWorld.YouHaveMovedTooFar);
    }

    // --- the items ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_death_at_a_high_level_takes_items_of_the_pack_with_ACEs_messages_and_the_line_that_says_so()
    {
        using var rig = new Rig(health: 5, level: 60);
        var wand = rig.Session.Tools.SpawnItem(GearKit.Wand.Wcid, null, ActionSource.Host);
        var comps = rig.Session.Tools.SpawnItem(774, 3, ActionSource.Host);
        rig.Run(2.0);
        Assert.Equal(2, rig.World.Pack.Count);
        rig.PlaceLifestone(new Vector3(1f, 0f, 0f));
        rig.Bind();
        rig.PlaceTusker(new Vector3(0f, 1f, 0f));
        rig.Cast.Begin();
        rig.Run(40.0);

        Assert.Equal(1, rig.World.PlayerDeaths);
        var lost = rig.World.LastDrops;
        Assert.InRange(lost.Count, 1, 2);
        var line = rig.Lines(0).Single(l => l.StartsWith("You've lost ", StringComparison.Ordinal));
        Assert.EndsWith("!", line);
        foreach (var (name, _) in lost)
        {
            Assert.Contains("your " + name, line, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("You have retained all your items. You do not need to recover your corpse!", rig.Lines(0));
        Assert.True(rig.Tags.Contains("0024") || rig.Tags.Contains("0197")); // an item left the pack, or a stack lost one unit
        Assert.Equal(1, rig.Tally.CorpsesSeen);
    }

    // --- the bars -----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void AddSpellFavorite_keeps_the_bars_for_the_next_description_as_ACE_adds_them_known_spells_only_once_at_a_clamped_index()
    {
        using var rig = new Rig();
        rig.Cast.Learn(27, 81, 82);
        var empty = new PlayerDescriptionMsg();
        Assert.Same(empty, rig.World.WithBars(empty)); // nothing added: the description is as it was

        rig.Session.Actions.AddSpellFavorite(27, 0, 1, ActionSource.Host);
        rig.Session.Actions.AddSpellFavorite(81, 5, 1, ActionSource.Host); // past the end: appended
        rig.Session.Actions.AddSpellFavorite(82, 0, 1, ActionSource.Host); // inserted first
        rig.Session.Actions.AddSpellFavorite(27, 2, 1, ActionSource.Host); // already on the bar: stays where it is
        rig.Session.Actions.AddSpellFavorite(999, 0, 1, ActionSource.Host); // not known: ignored
        rig.Session.Actions.AddSpellFavorite(27, 0, 0, ActionSource.Host);
        rig.Run(1.0);

        var bars = rig.World.WithBars(empty).SpellBars;
        Assert.Equal(2, bars.Length);
        Assert.Equal<uint>([27], bars[0]);
        Assert.Equal<uint>([82, 27, 81], bars[1]);
    }

    [Fact]
    public void RemoveSpellFavorite_takes_a_spell_off_the_bar_as_ACE_does_and_ignores_one_that_is_not_there()
    {
        using var rig = new Rig();
        rig.Cast.Learn(27, 81, 82);
        rig.Session.Actions.AddSpellFavorite(27, 0, 1, ActionSource.Host);
        rig.Session.Actions.AddSpellFavorite(81, 1, 1, ActionSource.Host);
        rig.Session.Actions.AddSpellFavorite(82, 2, 1, ActionSource.Host);
        rig.Session.Actions.RemoveSpellFavorite(81, 1, ActionSource.Host);
        rig.Session.Actions.RemoveSpellFavorite(999, 1, ActionSource.Host); // not on the bar
        rig.Session.Actions.RemoveSpellFavorite(27, 0, ActionSource.Host); // not on that bar
        rig.Run(1.0);

        var bars = rig.World.WithBars(new PlayerDescriptionMsg()).SpellBars;
        Assert.Equal<uint>([27, 82], bars[1]);
    }
}
