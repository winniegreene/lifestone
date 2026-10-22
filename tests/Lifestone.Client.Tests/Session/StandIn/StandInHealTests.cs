using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Net.Session;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 3.1 s2: the stand-in's heal in the order and with the lengths of the live capture (cache/m4/3-1/capture.md C1 to C4 and capture1.log): a UseWithTarget 0x0035 of the Handy Healing Kit on the player's own guid (the motion, then at
/// +1.47 s the heal or the failed roll and the UseDone, the Ready 30 ms later) and a Use 0x0036 of a Potion of Healing (the drink, the effect at +0.73 s, the UseDone at +1.47 s), with ACE's roll from a seeded draw, the refusals and the
/// busy window. Everything goes through the responder's session, the face the Action API sends through. Nothing here is ACE: the offline stand-in is proved against the capture, the live loops prove ACE.
/// </summary>
public sealed class StandInHealTests
{
    private const double Tolerance = 0.015;
    private const uint Kit = StandInWorld.HealKitGuid;
    private const uint Potions = StandInWorld.HealPotionGuid;

    /// <summary>The first draws of <see cref="StandInWorld.HealSeed"/> (the roll, the amount, the critical): pinned, so a change of the seed or of the order of the draws shows.</summary>
    private static readonly double[] PinnedDraws = [0.898049, 0.803774, 0.471556];

    /// <summary>A generator that answers the draws a test names (the roll, the amount, the critical), then repeats them.</summary>
    private sealed class Fixed(params double[] draws) : Random
    {
        private int _next;

        public override double NextDouble() => draws[_next++ % draws.Length];
    }

    /// <summary>A stand-in with the kit and the potions announced; the hunter's numbers: 188 health (Endurance 240 and 68 ranks), 240 stamina, experience to spend.</summary>
    private sealed class Rig : IDisposable
    {
        public Rig(uint health, uint stamina = 240, bool combat = false, bool healItems = true)
        {
            var visit = new StandInVisit
            {
                EnterAfter = 0.2,
                UnhideAfter = 1.0,
                HealItems = healItems,
                Stats = new StandInPlayerStats { Endurance = 240, HealthRanks = 68, Health = health, Stamina = stamina, TotalExperience = 1000, AvailableExperience = 1000 },
            };
            Cast = new CastRig(visit);
            Cast.Session.Events.Subscribe((in UseDone e) => Done.Add((Cast.Session.Clock.Now, e.WeenieError)));
            if (combat)
            {
                Cast.Mode(CombatMode.Melee);
            }

            Cast.Begin();
            Done.Clear();
        }

        public CastRig Cast { get; }

        public List<(double At, uint Error)> Done { get; } = [];

        public IAcSession Net => Cast.Session.Reducer.LocalResponder!.Session;

        public StandInWorld World => Cast.World;

        public uint Self => Cast.Session.State.Self.Guid;

        public string[] Tags => Cast.Tags;

        public List<(double At, string Tag)> Timeline => Cast.Timeline;

        public uint Health
        {
            get
            {
                Assert.True(Cast.Q.TryGetVitalLevels(1, out var current, out _));
                return current;
            }
        }

        public uint Stamina
        {
            get
            {
                Assert.True(Cast.Q.TryGetVitalLevels(3, out var current, out _));
                return current;
            }
        }

        public string LastLine => Cast.Chat[^1].Text;

        public void Run(double seconds) => Cast.Run(seconds);

        public int? Structure(uint item) => Cast.Session.State.Objects.TryGet(item, out var o) ? o.Weenie.Structure : null;

        public int? Stack(uint item) => Cast.Session.State.Objects.TryGet(item, out var o) ? o.Weenie.StackSize : null;

        public bool Held(uint item) => Cast.Session.State.Self.Inventory.Find(item).Held;

        public void Dispose() => Cast.Dispose();
    }

    // --- the items ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_player_is_announced_with_a_kit_of_20_uses_and_five_potions_and_the_placeholder_is_logged()
    {
        using var rig = new Rig(188);

        Assert.True(rig.Held(Kit));
        Assert.True(rig.Held(Potions));
        Assert.True(rig.Cast.Session.State.Objects.TryGet(Kit, out var kit));
        Assert.Equal((628u, "Handy Healing Kit", 0x80u, (ushort?)20, (ushort?)20, rig.Self), (kit.Weenie.WeenieClassId, kit.Name, kit.Weenie.ItemType, kit.Weenie.Structure, kit.Weenie.MaxStructure, kit.Weenie.ContainerId ?? 0));
        Assert.True(rig.Cast.Session.State.Objects.TryGet(Potions, out var potions));
        Assert.Equal((377u, "Potion of Healing", 0x80u, (ushort?)5, (ushort?)100, (uint?)850), (potions.Weenie.WeenieClassId, potions.Name, potions.Weenie.ItemType, potions.Weenie.StackSize, potions.Weenie.MaxStackSize, potions.Weenie.Value));
        Assert.Contains("TODO(m4-standin-heal-items)", string.Join('\n', rig.Cast.Log.Entries.Select(e => e.Line)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_recorded_description_carries_the_kit_and_the_potions_in_its_pack_beside_the_added_side_pack()
    {
        var recorded = RecordedDescription.Of("x.lsrec", new Lifestone.Net.Messages.PlayerDescriptionMsg { Inventory = [] }, 0x50000077, []);
        using var rig = new CastRig(new StandInVisit { EnterAfter = 0.2, UnhideAfter = 1.0, HealItems = true, Recorded = recorded });

        Assert.True(rig.Session.State.Self.Inventory.Find(Kit).Held);
        Assert.True(rig.Session.State.Self.Inventory.Find(Potions).Held);
        Assert.True(rig.Session.State.Self.Inventory.Find(RecordedDescription.PlaceholderPackItemGuid).Held);
    }

    [Fact]
    public void A_visit_that_does_not_ask_holds_no_kit_and_no_potions()
    {
        using var rig = new Rig(188, healItems: false);

        Assert.False(rig.Held(Kit));
        Assert.False(rig.Held(Potions));
    }

    [Fact]
    public void A_potion_made_by_the_item_verb_is_drunk_when_the_visit_asks_for_the_heals()
    {
        using var rig = new CastRig(PotionVisit(heals: true));
        var potion = rig.Session.Tools.SpawnItem(StandInWorld.HealingPotionWcid, 3, ActionSource.Host);
        rig.Run(1.5);
        Assert.True(potion.IsCompleted, potion.Summary);
        var guid = potion.Result!.Guid;
        rig.Begin();

        rig.Session.Reducer.LocalResponder!.Session.SendUse(guid);
        rig.Run(3.0);

        Assert.Equal(["F74C", "02E9", "F7E0", "0197", "02CD", "F750", "F74C", "01C7"], rig.Tags);
        Assert.Equal(2, rig.World.Pack.Single(e => e.Guid == guid).Stack);
        Assert.True(rig.Session.State.Objects.TryGet(guid, out var o));
        Assert.Equal((ushort?)2, o.Weenie.StackSize);
    }

    [Fact]
    public void Without_the_heals_a_potion_use_is_a_bare_UseDone_and_a_kit_use_is_a_loud_stub()
    {
        using var rig = new CastRig(PotionVisit(heals: false));
        var potion = rig.Session.Tools.SpawnItem(StandInWorld.HealingPotionWcid, 3, ActionSource.Host);
        rig.Run(1.5);
        var guid = potion.Result!.Guid;
        var net = rig.Session.Reducer.LocalResponder!.Session;
        rig.Begin();

        net.SendUse(guid);
        rig.Run(3.0);

        Assert.Empty(rig.Tags);
        Assert.Equal(3, rig.World.Pack.Single(e => e.Guid == guid).Stack);
        Assert.Contains("TODO(m4-standin-heal)", Assert.Throws<NotSupportedException>(() => net.SendUseWithTarget(guid, rig.Session.State.Self.Guid)).Message, StringComparison.Ordinal);
    }

    private static StandInVisit PotionVisit(bool heals) => new()
    {
        EnterAfter = 0.2,
        UnhideAfter = 1.0,
        Heals = heals,
        Stats = new StandInPlayerStats { Endurance = 240, HealthRanks = 68, Health = 100, Stamina = 240 },
        Items = new Dictionary<uint, StandInItemKind> { [StandInWorld.HealingPotionWcid] = new(StandInWorld.HealingPotionName, StandInWorld.PotionMaxStack) },
    };

    // --- the rules ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_seed_is_pinned_and_the_rules_are_the_ones_the_capture_and_ACE_give()
    {
        var draws = new Random(StandInWorld.HealSeed);
        Assert.Equal(PinnedDraws, PinnedDraws.Select(_ => Math.Round(draws.NextDouble(), 6)).ToArray());
        using var rig = new Rig(188);
        Assert.Equal(PinnedDraws[0], Math.Round(rig.World.HealRandom.NextDouble(), 6));

        // capture.md C1: (Healing + 50) x 1.1 = 264 for the hunter, and 145 missing in a combat mode is a difficulty of 319 (a chance of about 17%).
        Assert.Equal(264, StandInWorld.HealEffective(StandInWorld.PlaceholderHealingSkill, StandInWorld.HandyKitBoost, specialized: false));
        Assert.Equal(319, StandInWorld.HealDifficulty(145, combat: true));
        Assert.Equal(290, StandInWorld.HealDifficulty(145, combat: false));
        Assert.Equal(0.1612, StandInWorld.HealChance(264, 319), 3);
        Assert.Equal(0.5, StandInWorld.HealChance(300, 300), 6);
        Assert.Equal(0.9734, StandInWorld.HealChance(264, 144), 3);
    }

    [Fact]
    public void The_stand_in_answers_the_kit_action_once_the_action_kind_exists()
    {
        // s1's lane adds ActionKind.UseWithTarget; the merge must then add it to StandInWorld.Answers (a one-line follow-up). Until the member exists the test has nothing to ask.
        if (Enum.TryParse<ActionKind>("UseWithTarget", out var kind))
        {
            Assert.True(StandInWorld.Answers(kind), "add ActionKind.UseWithTarget to StandInWorld.Answers");
        }
    }

    // --- the kit ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_won_roll_is_the_motion_then_at_1_47_seconds_stamina_health_experience_the_line_the_structure_and_the_UseDone_and_the_Ready_30_ms_later()
    {
        using var rig = new Rig(116);

        rig.Net.SendUseWithTarget(Kit, rig.Self);
        rig.Run(3.0);

        Assert.Equal(["F74C", "02E9", "02E9", "02CF", "F7E0", "02CE", "01C7", "F74C"], rig.Tags);
        var t = rig.Timeline;
        Assert.Equal(StandInWorld.HealMotionSeconds, t[1].At - t[0].At, Tolerance);
        Assert.All(t.Skip(1).Take(6), e => Assert.Equal(t[1].At, e.At, 0.001)); // the effect is ONE tick
        Assert.Equal(StandInWorld.HealReadyLagSeconds, t[7].At - t[6].At, Tolerance);
        Assert.Equal([(t[6].At, 0u)], rig.Done);

        // The motion: the Ready forward and the SkillHealSelf command; then the Ready with no command.
        Assert.Equal(2, rig.Cast.Motions.Count);
        var heal = rig.Cast.Motions[0].Motion;
        Assert.Null(heal.Forward);
        Assert.Equal((ushort)0x010E, Assert.Single(heal.Commands).Command);
        Assert.Empty(rig.Cast.Motions[1].Motion.Commands);

        // The heal: 72 missing, the draw's 83.8 capped at it; the stamina cost a fifth (14.4 rounds to 14); 1.1 x the difficulty (144) of experience; one use spent.
        Assert.Equal((188u, 226u), (rig.Health, rig.Stamina));
        Assert.Equal("You heal yourself for 72 Health points. Your Handy Healing Kit has 19 uses left.", rig.LastLine);
        Assert.Equal(1000 + 158, rig.Cast.Q.AvailableExperience);
        Assert.Equal(19, rig.Structure(Kit));
        Assert.Equal(19, rig.World.KitUsesLeft(Kit));
        Assert.True(rig.Held(Kit));
    }

    [Fact]
    public void A_uncapped_heal_is_the_draws_amount_and_a_fifth_of_it_in_stamina()
    {
        using var rig = new Rig(98); // 90 missing out of combat: a difficulty of 180, a chance of 0.926, and the draw's 83.8 is under the missing health

        rig.Net.SendUseWithTarget(Kit, rig.Self);
        rig.Run(3.0);

        Assert.Equal("You heal yourself for 84 Health points. Your Handy Healing Kit has 19 uses left.", rig.LastLine);
        Assert.Equal((98u + 84u, 240u - 17u), (rig.Health, rig.Stamina));
    }

    [Fact]
    public void A_lost_roll_by_the_pinned_draw_is_the_line_and_the_structure_with_no_vital_and_a_use_spent()
    {
        using var rig = new Rig(43, combat: true); // capture.md C1: 43 of 188 in a combat mode, a chance of 0.161 against the pinned first draw of 0.898

        rig.Net.SendUseWithTarget(Kit, rig.Self);
        rig.Run(3.0);

        Assert.Equal(["F74C", "F7E0", "02CE", "01C7", "F74C"], rig.Tags);
        Assert.Equal("You fail to heal yourself. Your Handy Healing Kit has 19 uses left.", rig.LastLine);
        Assert.Equal((43u, 240u), (rig.Health, rig.Stamina));
        Assert.Equal(19, rig.Structure(Kit));
        Assert.Equal(1000, rig.Cast.Q.AvailableExperience);
        Assert.Equal([0u], rig.Done.Select(d => d.Error));
        Assert.Equal(StandInWorld.HealMotionSeconds, rig.Timeline[1].At - rig.Timeline[0].At, Tolerance);
    }

    [Fact]
    public void A_critical_draw_doubles_the_heal_and_the_line_says_expertly()
    {
        using var rig = new Rig(38);
        rig.World.HealRandom = new Fixed(0.0, 0.5, 0.05); // a roll that wins even at 150 missing, the band's middle (66.5), a critical

        rig.Net.SendUseWithTarget(Kit, rig.Self);
        rig.Run(3.0);

        Assert.Equal("You expertly heal yourself for 133 Health points. Your Handy Healing Kit has 19 uses left.", rig.LastLine);
        Assert.Equal((38u + 133u, 240u - 27u), (rig.Health, rig.Stamina));
    }

    [Fact]
    public void Too_little_stamina_caps_the_heal_at_five_times_the_stamina()
    {
        using var rig = new Rig(116, stamina: 10);
        rig.World.HealRandom = new Fixed(0.0, 0.5, 0.9); // 66.5 would cost 13

        rig.Net.SendUseWithTarget(Kit, rig.Self);
        rig.Run(3.0);

        Assert.Equal("You heal yourself for 50 Health points. Your Handy Healing Kit has 19 uses left.", rig.LastLine);
        Assert.Equal((166u, 0u), (rig.Health, rig.Stamina));
    }

    [Fact]
    public void The_last_use_spends_the_kit_after_the_line_and_the_structure_with_0024_and_the_burden()
    {
        using var rig = new Rig(116);
        rig.World.SetKitUses(Kit, 1);

        rig.Net.SendUseWithTarget(Kit, rig.Self);
        rig.Run(3.0);

        Assert.Equal(["F74C", "02E9", "02E9", "02CF", "F7E0", "02CE", "0024", "02CD", "01C7", "F74C"], rig.Tags);
        Assert.Equal("You heal yourself for 72 Health points. Your Handy Healing Kit is used up.", rig.LastLine);
        Assert.False(rig.Held(Kit));
        Assert.True(rig.Cast.Session.State.Objects.Contains(Kit)); // ACE sends no delete for a consumed item: the object stays in the table, out of the pack
    }

    [Fact]
    public void The_last_use_of_a_failed_roll_spends_the_kit_too()
    {
        using var rig = new Rig(43, combat: true);
        rig.World.SetKitUses(Kit, 1);

        rig.Net.SendUseWithTarget(Kit, rig.Self);
        rig.Run(3.0);

        Assert.Equal(["F74C", "F7E0", "02CE", "0024", "02CD", "01C7", "F74C"], rig.Tags);
        Assert.Equal("You fail to heal yourself. Your Handy Healing Kit is used up.", rig.LastLine);
        Assert.False(rig.Held(Kit));
    }

    [Fact]
    public void A_second_to_last_use_says_one_use_left()
    {
        using var rig = new Rig(116);
        rig.World.SetKitUses(Kit, 2);

        rig.Net.SendUseWithTarget(Kit, rig.Self);
        rig.Run(3.0);

        Assert.EndsWith("Your Handy Healing Kit has 1 use left.", rig.LastLine, StringComparison.Ordinal);
    }

    // --- the potion ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_potion_at_partial_health_is_the_drink_then_at_0_73_seconds_the_health_the_line_the_stack_and_the_burden_the_sound_the_Ready_and_the_UseDone_at_1_47()
    {
        using var rig = new Rig(47);

        rig.Net.SendUse(Potions);
        rig.Run(3.0);

        Assert.Equal(["F74C", "02E9", "F7E0", "0197", "02CD", "F750", "F74C", "01C7"], rig.Tags);
        var t = rig.Timeline;
        Assert.Equal(StandInWorld.DrinkEffectSeconds, t[1].At - t[0].At, Tolerance);
        Assert.All(t.Skip(1).Take(4), e => Assert.Equal(t[1].At, e.At, 0.001));
        Assert.Equal(StandInWorld.DrinkSoundLagSeconds, t[5].At - t[1].At, Tolerance);
        Assert.Equal(StandInWorld.DrinkReadyLagSeconds, t[6].At - t[1].At, Tolerance);
        Assert.Equal(StandInWorld.DrinkDoneSeconds, t[7].At - t[0].At, Tolerance);
        Assert.Equal([(t[7].At, 0u)], rig.Done);

        // The drink is the forward command with no queued command; the Ready after it has neither.
        var drink = rig.Cast.Motions[0].Motion;
        Assert.Equal((ushort)0x001B, drink.Forward);
        Assert.Empty(drink.Commands);
        Assert.Null(rig.Cast.Motions[1].Motion.Forward);

        Assert.Equal(47u + 25u, rig.Health);
        Assert.Equal("The Potion of Healing restores 25 points of your Health.", rig.LastLine);
        Assert.Equal(4, rig.Stack(Potions));
    }

    [Fact]
    public void A_potion_drunk_at_a_full_health_is_spent_with_no_02E9_and_restores_0_points()
    {
        using var rig = new Rig(188);

        rig.Net.SendUse(Potions);
        rig.Run(3.0);

        Assert.Equal(["F74C", "F7E0", "0197", "02CD", "F750", "F74C", "01C7"], rig.Tags);
        Assert.Equal("The Potion of Healing restores 0 points of your Health.", rig.LastLine);
        Assert.Equal(188u, rig.Health);
        Assert.Equal(4, rig.Stack(Potions));
    }

    [Fact]
    public void A_potion_near_the_top_restores_only_what_is_missing()
    {
        using var rig = new Rig(180);

        rig.Net.SendUse(Potions);
        rig.Run(3.0);

        Assert.Equal("The Potion of Healing restores 8 points of your Health.", rig.LastLine);
        Assert.Equal(188u, rig.Health);
    }

    [Fact]
    public void The_last_potion_is_0024_and_the_burden_instead_of_the_stack_size_and_a_use_after_it_is_a_bare_UseDone()
    {
        using var rig = new Rig(188);
        for (var i = 0; i < 4; i++)
        {
            rig.Net.SendUse(Potions);
            rig.Run(2.0);
        }

        rig.Cast.Begin();
        rig.Done.Clear();
        rig.Net.SendUse(Potions);
        rig.Run(3.0);

        Assert.Equal(["F74C", "F7E0", "0024", "02CD", "F750", "F74C", "01C7"], rig.Tags);
        Assert.False(rig.Held(Potions));

        rig.Cast.Begin();
        rig.Done.Clear();
        rig.Net.SendUse(Potions);
        rig.Run(1.0);
        Assert.Equal([0u], rig.Done.Select(d => d.Error));
        Assert.Empty(rig.Tags); // the generic Use answer is a bare UseDone 0 (the stand-in's answer log does not list it)
    }

    // --- the refusals and the busy window -----------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_kit_used_while_a_heal_is_in_progress_is_refused_0x001D_with_no_028A_and_no_028B_and_the_first_heal_still_ends()
    {
        using var rig = new Rig(116);

        rig.Net.SendUseWithTarget(Kit, rig.Self);
        rig.Run(0.5);
        var sent = rig.Cast.Session.Clock.Now;
        rig.Net.SendUseWithTarget(Kit, rig.Self);
        rig.Run(3.0);

        Assert.Equal(["F74C", "01C7", "02E9", "02E9", "02CF", "F7E0", "02CE", "01C7", "F74C"], rig.Tags);
        Assert.Equal([0x001Du, 0u], rig.Done.Select(d => d.Error));
        Assert.Equal(sent + rig.World.DelaySeconds, rig.Done[0].At, Tolerance); // the refusal is the stand-in's delay after the second send
        Assert.DoesNotContain("028A", rig.Tags);
        Assert.DoesNotContain("028B", rig.Tags);
        Assert.Equal(19, rig.Structure(Kit));
    }

    [Fact]
    public void The_busy_window_lasts_until_the_Ready_F74C_a_repeat_at_the_UseDone_is_refused_and_one_after_the_Ready_is_answered()
    {
        using var rig = new Rig(100);
        rig.Net.SendUseWithTarget(Kit, rig.Self);
        rig.Run(StandInWorld.HealMotionSeconds + rig.World.DelaySeconds - 0.01); // just before the UseDone

        // A repeat sent as the first use ends (capture.md C2: 8 ms before the Ready) is refused.
        rig.Net.SendUseWithTarget(Kit, rig.Self);
        rig.Run(2.0);
        Assert.Equal([0u, 0x001Du], rig.Done.Select(d => d.Error));

        // One sent after the Ready is a use: the second F74C of the heal.
        rig.Cast.Begin();
        rig.Net.SendUseWithTarget(Kit, rig.Self);
        rig.Run(3.0);
        Assert.Equal("F74C", rig.Tags[0]);
        Assert.Equal(2, rig.Done.Count(d => d.Error == 0));
        Assert.Equal(18, rig.Structure(Kit));
    }

    [Fact]
    public void A_potion_used_while_a_heal_is_in_progress_is_the_028A_0x001D_and_a_UseDone_0_and_the_potion_is_kept()
    {
        using var rig = new Rig(116);

        rig.Net.SendUseWithTarget(Kit, rig.Self);
        rig.Run(0.5);
        rig.Net.SendUse(Potions);
        rig.Run(3.0);

        Assert.Equal(["F74C", "028A", "01C7", "02E9", "02E9", "02CF", "F7E0", "02CE", "01C7", "F74C"], rig.Tags);
        Assert.Equal(5, rig.Stack(Potions));
    }

    [Fact]
    public void A_kit_used_while_a_drink_is_in_progress_is_refused_0x001D()
    {
        using var rig = new Rig(116);

        rig.Net.SendUse(Potions);
        rig.Run(0.5);
        rig.Net.SendUseWithTarget(Kit, rig.Self);
        rig.Run(3.0);

        Assert.Equal([0x001Du, 0u], rig.Done.Select(d => d.Error));
        Assert.Equal(20, rig.World.KitUsesLeft(Kit));
    }

    [Fact]
    public void A_kit_used_by_a_character_whose_healing_is_untrained_is_UseDone_0x04FC_and_nothing_else()
    {
        using var rig = new Rig(116);
        rig.World.HealingTrained = false;

        rig.Net.SendUseWithTarget(Kit, rig.Self);
        rig.Run(3.0);

        Assert.Equal(["01C7"], rig.Tags);
        Assert.Equal([0x04FCu], rig.Done.Select(d => d.Error));
        Assert.Equal(20, rig.World.KitUsesLeft(Kit));
        Assert.Equal(116u, rig.Health);
    }

    [Fact]
    public void A_kit_used_at_a_full_health_is_028B_0x04FF_then_UseDone_0_and_no_use_is_spent()
    {
        using var rig = new Rig(188);

        rig.Net.SendUseWithTarget(Kit, rig.Self);
        rig.Run(3.0);

        Assert.Equal(["028B", "01C7"], rig.Tags);
        var line = rig.Cast.Chat[^1];
        Assert.Equal((ChatKind.WeenieError, 0x04FFu), (line.Kind, line.ChatType));
        Assert.Equal([0u], rig.Done.Select(d => d.Error));
        Assert.Equal(20, rig.World.KitUsesLeft(Kit));
        Assert.Contains("TODO(m4-heal-full-vital-live)", string.Join('\n', rig.Cast.Log.Entries.Select(e => e.Line)), StringComparison.Ordinal);
    }

    [Fact]
    public void A_kit_that_is_not_held_an_item_that_is_not_a_kit_and_a_missing_target_are_a_silent_UseDone_0()
    {
        using var rig = new Rig(116);

        rig.Net.SendUseWithTarget(0x80008888, rig.Self); // not held
        rig.Run(1.0);
        rig.Net.SendUseWithTarget(Potions, rig.Self); // held, not a kit
        rig.Run(1.0);
        rig.Net.SendUseWithTarget(Kit, 0x80009999); // a target the world does not know
        rig.Run(1.0);
        rig.Net.SendUseWithTarget(Kit, 0); // no target
        rig.Run(1.0);
        rig.Net.SendUse(Kit); // a Use of the kit alone is answered as any other item's
        rig.Run(1.0);

        Assert.Equal(["01C7", "01C7", "01C7", "01C7"], rig.Tags); // the Use of the kit alone is the generic answer, which the answer log does not list
        Assert.Equal(5, rig.Done.Count);
        Assert.All(rig.Done, d => Assert.Equal(0u, d.Error));
        Assert.Equal(20, rig.World.KitUsesLeft(Kit));
    }

    [Fact]
    public void A_kit_used_on_a_target_that_is_not_the_player_is_UseDone_0x04FE()
    {
        using var rig = new Rig(116);

        rig.Net.SendUseWithTarget(Kit, Potions); // an object the world knows that is not a player
        rig.Run(2.0);

        Assert.Equal(["01C7"], rig.Tags);
        Assert.Equal([StandInWorld.CantHealThat], rig.Done.Select(d => d.Error));
        Assert.Equal(20, rig.World.KitUsesLeft(Kit));
    }

    [Fact]
    public void The_stand_in_stays_busy_for_an_item_move_during_a_heal_and_the_refusal_is_the_moves_own()
    {
        using var rig = new Rig(116);

        rig.Net.SendUseWithTarget(Kit, rig.Self);
        rig.Run(0.5);
        rig.Net.SendDropItem(Potions);
        rig.Run(0.5);

        Assert.Contains("028A", rig.Tags);
        Assert.True(rig.Held(Potions));
    }
}
