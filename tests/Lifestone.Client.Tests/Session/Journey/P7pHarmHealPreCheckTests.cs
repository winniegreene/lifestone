using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Client.Tests.Session.Vitals;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M5 7 P7p (D141, D88), the harm-heal station's pre-check: the item-end gate's Archer began its harm-heal in the forest while a Gold Phyntos Wasp, a Shadow Child and a Brutish Monouga the hunt left standing attacked it, and a
/// 39-point hit in the potion's window failed the station. No harm is cast while a living attackable creature heard attacking the player (<see cref="AttackWatch"/>) stands within <see cref="HarmHealRun.HostileReachMetres"/>: the
/// station steps away from it (bounded) and, still attacked, fails named <c>precondition:hostile-in-reach:&lt;name&gt;</c> before any swap or cast. A creature that does not attack, or an NPC (a vendor: not attackable), never blocks it.
/// </summary>
public sealed class P7pHarmHealPreCheckTests
{
    private const uint Cell = 0x860201AD; // the rig player's cell (ReducerHarness.EnterWorld), at (10, 20)
    private const uint Wasp = 0x80001500;
    private const uint Vendor = 0x7A9B4021;

    private static readonly uint[] Spells = [.. VitalsMeans.HarmSelf, .. VitalsMeans.HealSelf, .. VitalsMeans.StaminaToManaSelf];

    [Fact]
    public void A_hostile_in_reach_keeps_the_harm_from_starting_and_after_the_bounded_steps_the_station_fails_named()
    {
        var vitals = new C2VitalsRig(new C2Character { Caster = false, CasterInPack = true, KitUses = 4, Potions = 5, LifeMagicRanks = 600, Spells = Spells });
        var rig = new J3aStationRig(vitals.R, vitals.Table);
        Creature(rig, Wasp, "Gold Phyntos Wasp", attackable: true, x: 14f); // 4 m from the player
        var run = (HarmHealRun)rig.Start(new HarmHealStation(), "Holtburg Archer");

        // The wasp keeps attacking: an evasion notice (0x01B4) and a resisted bolt every frame, as the gate's log heard them.
        var status = rig.Run(run, 120, pump: () =>
        {
            vitals.Pump();
            Attack(rig, "Gold Phyntos Wasp");
        });

        var lines = rig.R.H.Log.Lines;
        Assert.NotEqual(ScenarioStatus.Running, status); // ended, as a harm-heal script failure ends: a failed check, the station not Ok
        Assert.False(run.Ok);
        Assert.Contains(run.Problems, p => p.StartsWith("precondition:hostile-in-reach: 'Gold Phyntos Wasp'", StringComparison.Ordinal));
        Assert.Null(run.Harm); // no script: no swap, no cast, no kit, no potion
        Assert.Empty(vitals.Casts);
        Assert.DoesNotContain(rig.R.H.Net.Sends, s => s.Kind == FakeSendKind.GetAndWieldItem);
        Assert.Equal(HarmHealRun.MaxStepAways, run.StepsAway);
        Assert.Contains("why=precondition:hostile-in-reach:Gold_Phyntos_Wasp", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(lines, l => l.Contains("pre-check: heard 'Gold Phyntos Wasp' attack the player (evasion notice)", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("pre-check: a hostile in reach: 'Gold Phyntos Wasp' 0x80001500 at 4.0 m attacks the player", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("pre-check: no fight-back of 'Gold Phyntos Wasp' 0x80001500: no-caster:", StringComparison.Ordinal)); // M6 6 I: the fight-back is tried first; no wand wielded, so the step away is the named fallback
        Assert.Equal(0, run.FoughtBack);
        Assert.Contains(lines, l => l.Contains("no outdoor point to step to (cell 0x860201AD)", StringComparison.Ordinal)); // the rig's player stands in a room: the bounded listen, not a walk
        Assert.True(rig.R.H.R.Clock.Now < HarmHealRun.HostileWaitSeconds + 10, "the wait is bounded");
    }

    // The vitals rig answers a cast by its spell id and knows no war bolt: the bolt takes a Revitalize id (the rig then answers it with a stamina boost, harmless here) and the table says it is a War Magic projectile.
    private static readonly uint WarBolt = VitalsMeans.RevitalizeSelf[0];

    private static C2VitalsRig CasterRig(bool withWarSpell)
    {
        var vitals = new C2VitalsRig(new C2Character { Caster = true, KitUses = 4, Potions = 5, LifeMagicRanks = 600, Spells = withWarSpell ? [.. Spells, WarBolt] : Spells });
        if (withWarSpell)
        {
            vitals.Table[WarBolt] = WorldActionRig.Spell(WarBolt, DatReaderWriter.Enums.SpellType.Projectile, Lifestone.Data.Magic.SpellFlags.None) with
            {
                Name = "Flame Bolt I",
                School = DatReaderWriter.Enums.MagicSchool.WarMagic,
                Power = 1,
                BaseMana = 10,
                BaseRangeConstant = ScriptedCaster.WarBoltRangeConstant, // M6 16 Q8: a range, as the dat's row has (the skirmish casts within it)
            };
        }

        vitals.R.Spells([.. vitals.Table.Values]);
        return vitals;
    }

    [Fact]
    public void M6_6_I_a_caster_fights_the_attacker_back_with_a_war_bolt_then_the_harm_begins_without_a_step_away()
    {
        var vitals = CasterRig(withWarSpell: true);
        var rig = new J3aStationRig(vitals.R, vitals.Table);
        Creature(rig, Wasp, "Gold Phyntos Wasp", attackable: true, x: 14f); // 4 m away
        var run = (HarmHealRun)rig.Start(new HarmHealStation(), "Winnie");
        var dead = false;

        var status = rig.Run(run, 60, pump: () =>
        {
            vitals.Pump();
            if (!dead && vitals.Casts.Any(c => c.Spell == WarBolt))
            {
                rig.R.H.R.Reducer.OnDeleteObject(rig.R.H.R.Context, new DeleteObjectMsg(Wasp, 1)); // the bolt killed it and it rotted
                dead = true;
            }

            if (!dead)
            {
                Attack(rig, "Gold Phyntos Wasp");
            }
        });

        var lines = rig.R.H.Log.Lines;
        Assert.True(dead, "no war bolt was cast: " + string.Join(" | ", lines.Where(l => l.Contains("fight-back", StringComparison.Ordinal) || l.Contains("pre-check", StringComparison.Ordinal))));
        Assert.Equal(1, run.FoughtBack);
        Assert.Equal(1, run.FightBackKills);
        Assert.Equal(0, run.StepsAway);
        Assert.NotNull(run.Harm); // the harm began after the kill
        Assert.NotEqual(ScenarioStatus.Failed, status);
        Assert.Contains(lines, l => l.Contains("fight-back: cast Flame Bolt I (", StringComparison.Ordinal) && l.Contains(") at 'Gold Phyntos Wasp' 0x80001500", StringComparison.Ordinal)); // M6 16 Q8: the skirmish's lines
        Assert.Contains(lines, l => l.Contains("fight-back: 'Gold Phyntos Wasp' 0x80001500 is dead", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("it steps away", StringComparison.Ordinal));
    }

    /// <summary>
    /// M6 16 Q8 (the station's fight-back is the skirmish): the reach is the spell's own. State: a wasp attacks from 20 m, inside her bolt's 30 m and past the station's old 15 m. Before: the refusal <c>out-of-range</c> and the step
    /// away. After: the bolt from where she stands, the kill, and the harm begins with no step away.
    /// </summary>
    [Fact]
    public void M6_16_Q8_an_attacker_past_the_old_15_m_but_inside_the_bolts_range_is_fought_back_not_stepped_away_from()
    {
        var vitals = CasterRig(withWarSpell: true);
        var rig = new J3aStationRig(vitals.R, vitals.Table);
        Creature(rig, Wasp, "Gold Phyntos Wasp", attackable: true, x: 30f); // 20 m away
        var run = (HarmHealRun)rig.Start(new HarmHealStation(), "Winnie");
        var dead = false;
        rig.Run(run, 60, pump: () =>
        {
            vitals.Pump();
            if (!dead && vitals.Casts.Any(c => c.Spell == WarBolt))
            {
                rig.R.H.R.Reducer.OnDeleteObject(rig.R.H.R.Context, new DeleteObjectMsg(Wasp, 1));
                dead = true;
            }

            if (!dead)
            {
                Attack(rig, "Gold Phyntos Wasp");
            }
        });

        var lines = rig.R.H.Log.Lines;
        Assert.True(dead, string.Join(" | ", lines.Where(l => l.Contains("fight-back", StringComparison.Ordinal) || l.Contains("pre-check", StringComparison.Ordinal))));
        Assert.Equal(1, run.FightBackKills);
        Assert.Equal(0, run.StepsAway);
        Assert.DoesNotContain(lines, l => l.Contains("out-of-range", StringComparison.Ordinal));
        Assert.NotNull(run.Harm);
    }

    [Fact]
    public void M6_6_I_a_caster_that_knows_no_war_bolt_has_no_fight_back_and_the_step_away_stays_the_named_fallback()
    {
        var vitals = CasterRig(withWarSpell: false);
        var rig = new J3aStationRig(vitals.R, vitals.Table);
        Creature(rig, Wasp, "Gold Phyntos Wasp", attackable: true, x: 14f);
        var run = (HarmHealRun)rig.Start(new HarmHealStation(), "Winnie");

        rig.Run(run, 120, pump: () =>
        {
            vitals.Pump();
            Attack(rig, "Gold Phyntos Wasp");
        });

        var lines = rig.R.H.Log.Lines;
        Assert.Equal(0, run.FoughtBack);
        Assert.Contains(lines, l => l.Contains("pre-check: no fight-back of 'Gold Phyntos Wasp' 0x80001500: no-war-spell:", StringComparison.Ordinal));
        Assert.Equal(HarmHealRun.MaxStepAways, run.StepsAway);
        Assert.Contains(run.Problems, p => p.StartsWith("precondition:hostile-in-reach: 'Gold Phyntos Wasp'", StringComparison.Ordinal));
        Assert.Empty(vitals.Casts);
    }

    [Fact]
    public void A_vendor_a_creature_that_does_not_attack_or_an_attacker_out_of_reach_does_not_block_the_harm()
    {
        var vitals = new C2VitalsRig(new C2Character { Caster = false, CasterInPack = true, KitUses = 4, Potions = 5, LifeMagicRanks = 600, Spells = Spells });
        var rig = new J3aStationRig(vitals.R, vitals.Table);
        Creature(rig, Vendor, "Shopkeeper Renald the Elder", attackable: false, x: 12f); // an NPC: not attackable
        Creature(rig, Wasp + 1, "Shadow Child", attackable: true, x: 13f); // near, but it does not attack (a camp the hunt left alone)
        Creature(rig, Wasp, "Gold Phyntos Wasp", attackable: true, x: 10f + (float)HarmHealRun.HostileReachMetres + 5f); // attacks, but from past the reach
        var run = (HarmHealRun)rig.Start(new HarmHealStation(), "Holtburg Brawler");

        var status = rig.Run(run, 20, pump: () =>
        {
            vitals.Pump();
            Attack(rig, "Gold Phyntos Wasp");
        });

        Assert.True(status == ScenarioStatus.Running, string.Join(" ; ", run.Problems) + " | " + run.Failure + " | " + run.VerdictFields());
        Assert.NotNull(run.Harm); // the script began: the swap in is under way
        Assert.Equal(0, run.StepsAway);
        Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("pre-check: heard 'Gold Phyntos Wasp', none within 40 m in 3 s: the harm begins", StringComparison.Ordinal));
    }

    /// <summary>
    /// M6 16 Q1: the pre-check asks the session's threat board. A wasp that attacks from 4 m is found each time the pre-check looks, and the station ends as it did (the bounded steps, then the named failure); the shadow lane
    /// of the old read is gone, so no line of it is written.
    /// </summary>
    [Fact]
    public void M6_16_Q1_the_pre_check_finds_the_hostile_through_the_threat_board()
    {
        var vitals = new C2VitalsRig(new C2Character { Caster = false, CasterInPack = true, KitUses = 4, Potions = 5, LifeMagicRanks = 600, Spells = Spells });
        var rig = new J3aStationRig(vitals.R, vitals.Table);
        Creature(rig, Wasp, "Gold Phyntos Wasp", attackable: true, x: 14f); // 4 m from the player
        var run = (HarmHealRun)rig.Start(new HarmHealStation(), "Holtburg Archer");

        rig.Run(run, 120, pump: () =>
        {
            vitals.Pump();
            Attack(rig, "Gold Phyntos Wasp");
        });

        Assert.Equal(HarmHealRun.MaxStepAways, run.StepsAway); // the pre-check's own answer, as before
        Assert.DoesNotContain(rig.R.H.Log.Lines, l => l.Contains(Lifestone.Client.Session.Threat.ThreatShadowLane.Token, StringComparison.Ordinal));
    }

    /// <summary>M6 16 Q1: a pre-check that passes (the attacker stands past the reach) writes no shadow line and the harm begins.</summary>
    [Fact]
    public void M6_16_Q1_a_pre_check_that_passes_writes_no_shadow_line()
    {
        var vitals = new C2VitalsRig(new C2Character { Caster = false, CasterInPack = true, KitUses = 4, Potions = 5, LifeMagicRanks = 600, Spells = Spells });
        var rig = new J3aStationRig(vitals.R, vitals.Table);
        Creature(rig, Wasp, "Gold Phyntos Wasp", attackable: true, x: 10f + (float)HarmHealRun.HostileReachMetres + 5f); // attacks, but from past the reach
        var run = (HarmHealRun)rig.Start(new HarmHealStation(), "Holtburg Brawler");

        rig.Run(run, 20, pump: () =>
        {
            vitals.Pump();
            Attack(rig, "Gold Phyntos Wasp");
        });

        Assert.NotNull(run.Harm); // the harm began, as before
        Assert.DoesNotContain(rig.R.H.Log.Lines, l => l.Contains(Lifestone.Client.Session.Threat.ThreatShadowLane.Token, StringComparison.Ordinal));
    }

    [Fact]
    public void The_nearest_hostile_is_a_living_attackable_non_player_creature_heard_attacking_within_the_reach()
    {
        var rig = new J3aStationRig();
        HashSet<string> heard = ["Shopkeeper Renald the Elder", "Gold Phyntos Wasp", "Shadow Child"];
        Creature(rig, Vendor, "Shopkeeper Renald the Elder", attackable: false, x: 11f);
        Assert.False(HostileReach.TryNearest(rig.View, 20, heard, out _, out _, out _)); // an NPC is never one

        Creature(rig, Wasp, "Gold Phyntos Wasp", attackable: true, x: 16f);
        Creature(rig, Wasp + 1, "Shadow Child", attackable: true, x: 13f);
        Creature(rig, Wasp + 2, "Brutish Monouga", attackable: true, x: 11f); // nearest, but not heard
        Assert.True(HostileReach.TryNearest(rig.View, 20, heard, out var guid, out var name, out var metres));
        Assert.Equal((Wasp + 1, "Shadow Child"), (guid, name));
        Assert.Equal(3.0, metres, 3);
        Assert.False(HostileReach.TryNearest(rig.View, 2.5, heard, out _, out _, out _));
        Assert.False(HostileReach.TryNearest(rig.View, 20, new HashSet<string>(), out _, out _, out _)); // nothing heard: nothing hostile
    }

    /// <summary>
    /// M6 14 caught on the run (rule 3, closing <c>TODO(m6-live-position-reach)</c>; live ladder25 04:10:23 UTC: a Reedshark Ravager whose server report was 18.9 m off while its body was drawn 2.6 m from her): every hostile read takes
    /// the creature where the session draws it, so the one reported at 18.9 m and drawn at 2.6 m stands at 2.6 m, inside the 15 m cast range, for the nearest pick, the attackers present and the pack count; with no bodies drawn the
    /// report answers as before; her own place is the caller's when it gives one (her body as the client draws it).
    /// </summary>
    [Fact]
    public void A_creature_reported_at_18_9_m_but_drawn_at_2_6_m_is_read_at_2_6_m_and_in_cast_range()
    {
        var t = new RungRig(level: 10);
        const uint ravager = 0x80001970;
        t.Attacker(ravager, "Reedshark Ravager", 18.9f, wcid: 22746);
        HashSet<string> heard = ["Reedshark Ravager"];
        Assert.True(HostileReach.TryNearest(t.Session, HostileReach.DefaultMetres, heard, out _, out _, out var reported));
        Assert.Equal(18.9, reported, 1); // no body drawn: the server's report answers
        Assert.True(reported > AcademyRun.CastRange);

        t.DrawAt(ravager, 2.6f);
        Assert.True(HostileReach.TryNearest(t.Session, HostileReach.DefaultMetres, heard, out var guid, out var name, out var drawn));
        Assert.Equal((ravager, "Reedshark Ravager"), (guid, name));
        Assert.Equal(2.6, drawn, 1);
        Assert.True(drawn <= AcademyRun.CastRange); // in cast range: the fight-back bolts it, it does not wait for it to close
        var present = new List<PresentAttacker>();
        HostileReach.Present(t.Session, HostileReach.DefaultMetres, heard, present);
        Assert.Equal(2.6, Assert.Single(present).Metres, 1);
        Assert.Equal(1, HostileReach.CountWithin(t.Session, 5));
        Assert.Equal(2.6, Assert.Single(HostileReach.Distances(t.Session)), 1);

        var here = t.R.H.State.Self.Position!.Value;
        var body = new CellFrame(here.CellId, new AcFrame(here.Origin + new Vector3(2.6f, 0, 0), Quaternion.Identity)); // her body as the client draws it, beside the creature's
        Assert.True(HostileReach.TryNearest(t.Session, HostileReach.DefaultMetres, heard, out _, out _, out var fromHer, here: body));
        Assert.Equal(0, fromHer, 1);
    }

    [Fact]
    public void The_step_away_point_lies_on_the_line_from_the_hostile_through_the_player_and_none_is_named_indoors()
    {
        var here = new CellFrame(0xA9B30021, new AcFrame(new Vector3(100f, 40f, 60f), Quaternion.Identity));
        var hostile = new CellFrame(0xA9B30021, new AcFrame(new Vector3(96f, 40f, 60f), Quaternion.Identity)); // 4 m west of the player

        Assert.True(HostileReach.TryAwayPoint(here, hostile, 30, out var away));
        Assert.Equal(0xA9B30000u, away.CellId & 0xFFFF0000u);
        Assert.Equal(130f, away.Local.X, 2); // 30 m east, away from it
        Assert.Equal(40f, away.Local.Y, 2);

        var indoors = new CellFrame(0xA9B4013F, new AcFrame(new Vector3(133.9f, 15.7f, 94f), Quaternion.Identity));
        Assert.False(HostileReach.TryAwayPoint(indoors, hostile, 30, out _));
    }

    // ------------------------------------------------------------------------------------------------ M6 6 R1F: the run leg's rules in the station's fight-back (components, the magic mode)

    // A second borrowed bolt (the rig answers a Revitalize id harmlessly), a level higher.
    private static readonly uint WarBolt2 = VitalsMeans.RevitalizeSelf[1];

    /// <summary>
    /// A caster with two War Magic bolts: Flame Bolt I (power 1, Lead Scarab) and Flame Bolt III (power 100, Copper Scarab), and a pack that holds the War Magic focus and the components of <paramref name="paid"/> only (the foci formula). The War
    /// Magic skill is not read, so the plain pick is the lowest power.
    /// </summary>
    private static C2VitalsRig ComponentRig(params uint[] paid)
    {
        var bolts = new Dictionary<uint, Lifestone.Data.Magic.SpellInfo>
        {
            [WarBolt] = WorldActionRig.Spell(WarBolt, DatReaderWriter.Enums.SpellType.Projectile) with { Name = "Flame Bolt I", School = DatReaderWriter.Enums.MagicSchool.WarMagic, Power = 1, BaseMana = 10, Formula = [1, 0, 0, 0, 0, 0, 0, 0], BaseRangeConstant = ScriptedCaster.WarBoltRangeConstant },
            [WarBolt2] = WorldActionRig.Spell(WarBolt2, DatReaderWriter.Enums.SpellType.Projectile) with { Name = "Flame Bolt III", School = DatReaderWriter.Enums.MagicSchool.WarMagic, Power = 100, BaseMana = 15, Formula = [3, 0, 0, 0, 0, 0, 0, 0], BaseRangeConstant = ScriptedCaster.WarBoltRangeConstant },
        };
        var pack = new Dictionary<uint, int> { [LevelFloor.FocusOf(DatReaderWriter.Enums.MagicSchool.WarMagic)] = 1 };
        foreach (var id in paid)
        {
            foreach (var (component, n) in GearKit.FociFormulaCounts(bolts[id].Formula))
            {
                Assert.True(Lifestone.Data.Magic.ComponentWeenies.TryGetByComponent(component, out var weenie));
                pack[weenie.Wcid] = Math.Max(pack.GetValueOrDefault(weenie.Wcid), n);
            }
        }

        var vitals = new C2VitalsRig(new C2Character { Caster = true, KitUses = 4, Potions = 5, LifeMagicRanks = 600, Spells = [.. Spells, WarBolt, WarBolt2], Pack = [.. pack.Select(p => (p.Key, p.Value))] });
        foreach (var (id, info) in bolts)
        {
            vitals.Table[id] = info;
        }

        vitals.R.Spells([.. vitals.Table.Values]);
        return vitals;
    }

    /// <summary>
    /// M6 6 R1F (live round 1, the run leg's rule shared through <see cref="WarBoltPick.ChooseCastable"/>): the station's fight-back skips a bolt the pack is short of (Flame Bolt I: no Lead Scarab) and casts the one it can pay for; a mode lost after
    /// a bolt is entered again before the next (check, act, check), and the attacker dies.
    /// </summary>
    [Fact]
    public void M6_6_R1F_the_station_skips_a_bolt_the_pack_cannot_pay_for_and_enters_the_magic_mode_again_before_each_cast()
    {
        var vitals = ComponentRig(WarBolt2);
        var rig = new J3aStationRig(vitals.R, vitals.Table);
        Creature(rig, Wasp, "Gold Phyntos Wasp", attackable: true, x: 14f);
        var run = (HarmHealRun)rig.Start(new HarmHealStation(), "Winnie");
        var dead = false;
        var seen = 0;
        rig.Run(run, 60, pump: () =>
        {
            vitals.Pump();
            var bolts = vitals.Casts.Count(c => c.Spell == WarBolt2);
            if (bolts == 1 && seen == 0)
            {
                seen = 1;
                vitals.R.CombatModeIs(Lifestone.Client.Session.Contracts.CombatModes.NonCombat); // the mode is lost after the first bolt
            }
            else if (bolts == 2 && !dead)
            {
                rig.R.H.R.Reducer.OnDeleteObject(rig.R.H.R.Context, new DeleteObjectMsg(Wasp, 1));
                dead = true;
            }

            if (!dead)
            {
                Attack(rig, "Gold Phyntos Wasp");
            }
        });

        var lines = rig.R.H.Log.Lines;
        Assert.True(dead, string.Join(" | ", lines.Where(l => l.Contains("fight-back", StringComparison.Ordinal))));
        Assert.DoesNotContain(vitals.Casts, c => c.Spell == WarBolt); // never the bolt the pack is short of
        Assert.Contains(lines, l => l.Contains("'Gold Phyntos Wasp' 0x80001500 at 4.0 m attacks the player: Flame Bolt III (", StringComparison.Ordinal) && l.Contains("skipped: Flame Bolt I needs Lead Scarab 691, which the pack does not hold", StringComparison.Ordinal));
        var first = vitals.Used.IndexOf("cast:" + WarBolt2);
        var second = vitals.Used.LastIndexOf("cast:" + WarBolt2);
        Assert.True(second > first && vitals.Used.IndexOf("mode:8", first) is var magic && magic > first && magic < second, string.Join(" > ", vitals.Used));
        Assert.Contains(lines, l => l.Contains("fight-back: check before the cast: the combat mode is 1, not magic (8)", StringComparison.Ordinal));
        Assert.Equal(1, run.FightBackKills);
        Assert.Equal(0, run.StepsAway);
    }

    /// <summary>M6 6 R1F: a pack that pays for no bolt the book holds is no fight-back: the refusal <c>no-components</c> names each bolt's lack, no bolt is cast, and the step away stays the named fallback.</summary>
    [Fact]
    public void M6_6_R1F_a_pack_that_pays_for_no_bolt_refuses_the_fight_back_with_no_components_and_steps_away()
    {
        var vitals = ComponentRig();
        var rig = new J3aStationRig(vitals.R, vitals.Table);
        Creature(rig, Wasp, "Gold Phyntos Wasp", attackable: true, x: 14f);
        var run = (HarmHealRun)rig.Start(new HarmHealStation(), "Winnie");
        rig.Run(run, 120, pump: () =>
        {
            vitals.Pump();
            Attack(rig, "Gold Phyntos Wasp");
        });

        var lines = rig.R.H.Log.Lines;
        Assert.Equal(0, run.FoughtBack);
        Assert.Contains(lines, l => l.Contains("pre-check: no fight-back of 'Gold Phyntos Wasp' 0x80001500: no-components: Flame Bolt I needs Lead Scarab 691", StringComparison.Ordinal) && l.Contains("Flame Bolt III needs Copper Scarab 686", StringComparison.Ordinal));
        Assert.DoesNotContain(vitals.Casts, c => c.Spell == WarBolt || c.Spell == WarBolt2);
        Assert.Equal(HarmHealRun.MaxStepAways, run.StepsAway);
    }

    /// <summary>
    /// M6 6 FXD2 (6-D7): a 0x0400 that reaches the station's fight-back only as chat (after the UseDone ended the cast) drops the bolt for the station, as the run leg reads it: the next bolt is the other one and the first is never cast again.
    /// </summary>
    [Fact]
    public void M6_6_FXD2_a_component_refusal_that_arrives_only_as_chat_drops_the_bolt_for_the_station()
    {
        var vitals = ComponentRig(WarBolt, WarBolt2);
        var rig = new J3aStationRig(vitals.R, vitals.Table);
        Creature(rig, Wasp, "Gold Phyntos Wasp", attackable: true, x: 14f);
        var run = (HarmHealRun)rig.Start(new HarmHealStation(), "Winnie");
        var told = false;
        var dead = false;
        rig.Run(run, 60, pump: () =>
        {
            var before = vitals.Casts.Count;
            vitals.Pump();
            if (!told && vitals.Casts.Count > before)
            {
                told = true;
                vitals.R.WeenieError(0x0400); // after the UseDone: only the chat carries it
            }
            else if (told && !dead && vitals.Casts.Count > 1)
            {
                rig.R.H.R.Reducer.OnDeleteObject(rig.R.H.R.Context, new DeleteObjectMsg(Wasp, 1));
                dead = true;
            }

            if (!dead)
            {
                Attack(rig, "Gold Phyntos Wasp");
            }
        });

        var lines = rig.R.H.Log.Lines;
        Assert.True(dead, string.Join(" | ", lines.Where(l => l.Contains("fight-back", StringComparison.Ordinal))));
        var bolts = vitals.Casts.Where(c => c.Spell == WarBolt || c.Spell == WarBolt2).Select(c => c.Spell).ToList();
        Assert.Equal(2, bolts.Count);
        Assert.NotEqual(bolts[0], bolts[1]); // never the refused bolt again
        Assert.Contains(lines, l => l.Contains("was refused for a missing component (0x0400): dropped for this leg", StringComparison.Ordinal));
    }

    /// <summary>One frame of a creature's attacks as ACE tells the defender: an evasion notice (0x01B4, a <see cref="CombatEvent"/>) and a resisted spell's Magic line.</summary>
    private static void Attack(J3aStationRig rig, string who)
    {
        rig.R.H.R.Bus.Publish(new CombatEvent(CombatEventKind.DefenseEvaded, who, 0, 0, 0, false, 0, true, 0, $"You evaded {who}!"));
        rig.R.H.ServerMessage("You resist the spell cast by " + who, 7);
    }

    private static void Creature(J3aStationRig rig, uint guid, string name, bool attackable, float x)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), ReducerHarness.Position(Cell, x, 20), state: PhysicsState.Gravity, name: name, itemType: HostileReach.CreatureItemType);
        rig.R.H.R.Reducer.OnCreateObject(rig.R.H.R.Context, basis with { Weenie = basis.Weenie with { ObjectDescriptionFlags = attackable ? HostileReach.AttackableFlag : 0x200u } }, isUpdate: false);
    }
}
