using DatReaderWriter.Enums;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 14 the Zefir (live ladder42, the Crater Pathway 0x01A7, 2026-11-16 20:17:14-20:18:30 UTC): 25 Ilservian's Flames at a Kilif Zefir 0x80006D61 (weenie 2610) each struck the world (the projectile created 1.8 m from her, deleted 5.1 s later
/// 3.2 to 3.4 m from her: ACE's <c>ProjectileImpact</c> destroys it 5.0 s after it impacts), no damage line, until the 180 s timeout recalled her; the owner's ruling: she should have used Life Magic and harmed it. What these prove: after
/// <see cref="FightScenario.WallHitsBeforeBlock"/> bolts with no line the travel fight-back casts Harm Other (no projectile), which the scripted server lets kill it, and the walk goes on; Harm Other's numbers on the Zefir are the
/// corpus's; the fight-back's bolt is the line that hits the attacker hardest (a Nubilous Golem, fire 0, gets Lightning Bolt VI); a failed walk step of a ladder errand is named for the retry rule. Not proved: the live server's
/// answers.
/// </summary>
public sealed partial class RungChangeTests
{
    private const uint Zefir = 0x80006D61, ZefirWcid = 2610, HarmOtherVI = 1176;

    private static SpellInfo HarmSpell(uint id = HarmOtherVI) =>
        WorldActionRig.Spell(id, SpellType.Boost) with { Name = "Harm Other VI", School = MagicSchool.LifeMagic, Power = 275, BaseMana = 30, BaseRangeConstant = 20f, BaseRangeMod = 0f };

    [Fact]
    public void Bolts_that_strike_the_world_switch_the_fight_back_to_harm_other_and_the_walk_goes_on()
    {
        var bolts = ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells.Select(b => (ushort)b.Id).ToArray();
        var t = new RungRig(level: 78, spells: [.. bolts, (ushort)HarmOtherVI]);
        t.Caster.Table[HarmOtherVI] = HarmSpell();
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.Entry(BurrowEntry, Burrow);
        var first = true;
        t.Travel.WalkingOn = r =>
        {
            if (r.Destination.Name != "entry-front" || !first)
            {
                return false;
            }

            first = false;
            t.Attacker(Zefir, "Kilif Zefir", 3f, ZefirWcid);
            return true;
        };
        t.Projectile = spell => spell != HarmOtherVI; // live: each bolt was "projectile created", then struck the world
        t.CastAt = (spell, target) =>
        {
            if (spell == HarmOtherVI)
            {
                t.Kill(target); // no projectile: no wall stops it
            }
            else
            {
                t.Attacker(target, "Kilif Zefir", 3f, ZefirWcid); // the bolt strikes the world, no line; it attacks again
            }
        };

        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow), maxSeconds: 600);

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(14)));
        Assert.Single(t.Lines($"{FightScenario.WallHitsBeforeBlock} bolts in a row came to nothing"));
        Assert.Contains(t.Lines("fight-back: Life Magic at 'Kilif Zefir' 0x80006D61"), l => l.Contains("no projectile, so no wall stops it", StringComparison.Ordinal));
        Assert.Equal(FightScenario.WallHitsBeforeBlock, t.Events.Count(e => e.EndsWith("@80006D61", StringComparison.Ordinal) && !e.StartsWith($"cast:{HarmOtherVI}@", StringComparison.Ordinal)));
        Assert.Contains($"cast:{HarmOtherVI}@80006D61", t.Events);
        Assert.Single(t.Lines("fight-back: 'Kilif Zefir' 0x80006D61 is dead"));
        Assert.DoesNotContain(t.Lines("still attacks after"), _ => true);
        Assert.Contains(" fail=none ", run.VerdictFields(), StringComparison.Ordinal);
    }

    /// <summary>
    /// M6 16 E1e: the travel fight-back's life cast took a creature with no maximum health as 10,000 and an absent Magic Defense as 0 without a word. Here an attacker of a weenie class the bestiary does not hold: the numbers taken
    /// are the same, and one warning on the leg names the class and what is taken (<see cref="LegKindRead"/>), however many life casts follow.
    /// </summary>
    [Fact]
    public void An_attacker_of_a_class_the_bestiary_does_not_hold_is_named_once_on_the_leg_and_harmed_on_the_old_fallbacks()
    {
        const uint odd = 0x80006D62, oddWcid = 999_999_999;
        Assert.Null(Bestiary.Find(oddWcid));
        var bolts = ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells.Select(b => (ushort)b.Id).ToArray();
        var t = new RungRig(level: 78, spells: [.. bolts, (ushort)HarmOtherVI]);
        t.Caster.Table[HarmOtherVI] = HarmSpell();
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.Entry(BurrowEntry, Burrow);
        var first = true;
        t.Travel.WalkingOn = r =>
        {
            if (r.Destination.Name != "entry-front" || !first)
            {
                return false;
            }

            first = false;
            t.Attacker(odd, "Odd Thing", 3f, oddWcid);
            return true;
        };
        t.Projectile = spell => spell != HarmOtherVI;
        var harms = 0;
        t.CastAt = (spell, target) =>
        {
            if (spell == HarmOtherVI && ++harms == 2)
            {
                t.Kill(target); // the second Harm Other kills it: two life casts read its kind
            }
            else
            {
                t.Attacker(target, "Odd Thing", 3f, oddWcid);
            }
        };

        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow), maxSeconds: 600);

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(14)));
        Assert.Equal(2, harms);
        var named = Assert.Single(t.Lines("TODO(engine-bestiary-unknown-kind)"));
        Assert.Contains("fight-back: TODO(engine-bestiary-unknown-kind): 'Odd Thing' (class 999999999) is no kind of the bestiary (no creature weenie of the pinned world database's table): a life cast at it takes a large target, 10000 health", named, StringComparison.Ordinal);
        Assert.Contains("its Magic Defense is taken as 0, rolled as ACE rolls a Magic Defense of 0", named, StringComparison.Ordinal);
        Assert.Contains("named once on this leg", named, StringComparison.Ordinal);
        Assert.Contains(t.Lines("fight-back: Life Magic at 'Odd Thing' 0x80006D62"), l => l.Contains("against Magic Defense 0)", StringComparison.Ordinal) && l.Contains("of its 10000 health", StringComparison.Ordinal));
        Assert.Contains(t.Lines("fight-back: target 'Odd Thing' 0x80006D62"), l => l.Contains("its health is not known: TODO(m6-creature-health): weenie 999999999", StringComparison.Ordinal)); // the publish line named the health already, as before
        Assert.Contains(" fail=none ", run.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_harm_other_an_attacker_its_bolts_cannot_reach_is_left_and_the_walk_goes_on()
    {
        var bolts = ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells.Select(b => (ushort)b.Id).ToArray();
        var t = new RungRig(level: 78, spells: bolts);
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.Entry(BurrowEntry, Burrow);
        var first = true;
        t.Travel.WalkingOn = r =>
        {
            if (r.Destination.Name != "entry-front" || !first)
            {
                return false;
            }

            first = false;
            t.Attacker(Zefir, "Kilif Zefir", 3f, ZefirWcid);
            return true;
        };
        t.Projectile = _ => true;
        t.CastAt = (_, target) => t.Attacker(target, "Kilif Zefir", 3f, ZefirWcid);

        var (_, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow), maxSeconds: 600);

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(14)));
        Assert.Contains(t.Lines("fight-back: 'Kilif Zefir' 0x80006D61: its bolts came to nothing"), l => l.Contains("her book holds no Harm Other", StringComparison.Ordinal) && l.Contains("left; the walk goes on", StringComparison.Ordinal));
        Assert.DoesNotContain(t.Lines("still attacks after"), _ => true);
    }

    [Fact]
    public void The_road_fight_back_casts_the_line_that_hits_the_attacker_hardest()
    {
        var fire = ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells.Select(b => (ushort)b.Id).ToArray();
        var lightning = WarSpellData.BoltLineIds["electric"][5]; // Lightning Bolt VI 80
        var t = new RungRig(level: 78, spells: [.. fire, (ushort)lightning]);
        t.Caster.Table[lightning] = t.Caster.Table[fire[5]] with { Id = lightning, Name = "Lightning Bolt VI" };
        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.Entry(BurrowEntry, Burrow);
        var first = true;
        t.Travel.WalkingOn = r =>
        {
            if (r.Destination.Name != "entry-front" || !first)
            {
                return false;
            }

            first = false;
            t.Attacker(0x80001960, "Nubilous Golem", 3f, 23082); // ResistFire 0, ResistElectric 1 (creature-resists.json)
            return true;
        };
        t.CastAt = (_, target) => t.Kill(target);

        var (_, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow));

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", t.R.H.Log.Lines.TakeLast(14)));
        Assert.Contains($"cast:{lightning}@80001960", t.Events);
        Assert.Contains(t.Lines("fight-back: cast Lightning Bolt VI"), l => l.Contains("the line that hits weenie 23082 hardest", StringComparison.Ordinal));
    }

    [Fact]
    public void Harm_other_on_the_zefir_is_the_corpuss_numbers()
    {
        var table = new Dictionary<uint, SpellInfo> { [HarmOtherVI] = HarmSpell() };
        var pick = LifeStrike.Choose(table, id => id == HarmOtherVI, new HashSet<uint>(), 390, 300, 0.5, KindNumbers.Of(ZefirWcid), 8.7, 50, out var why)!;
        Assert.Equal(HarmOtherVI, pick.Spell.Id);
        Assert.True(pick.LandChance > 0.99, why); // her Life Magic 390 against the Zefir's Magic Defense 46 (the creature table)
        Assert.Equal(48.0 * pick.LandChance, pick.Takes, 6); // Harm Other VI takes 33 to 63 (HarmOtherRows), mean 48, under its 50 health

        Assert.Null(LifeStrike.Choose(table, _ => false, new HashSet<uint>(), 390, 300, 0.5, KindNumbers.Of(ZefirWcid), 8.7, 50, out var none));
        Assert.Equal("her book holds no Harm Other", none);
        Assert.Null(LifeStrike.Choose(table, id => id == HarmOtherVI, new HashSet<uint>(), 390, 300, 0.5, KindNumbers.Of(ZefirWcid), 25, 50, out var far));
        Assert.Contains("reaches 20 m", far, StringComparison.Ordinal);
        Assert.Null(LifeStrike.Choose(table, id => id == HarmOtherVI, new HashSet<uint> { HarmOtherVI }, 390, 300, 0.5, KindNumbers.Of(ZefirWcid), 8.7, 50, out var refused));
        Assert.Contains("refused on this leg", refused, StringComparison.Ordinal);
    }

    /// <summary>The hovering Kilif Zefir of live ladder51 (2026-11-17 02:55:47 UTC, the Crater Pathway 0x01A7): guid 0x8000802D, drawn 38.9 m from her.</summary>
    private const uint HoveringZefir = 0x8000802D;

    private const float HoveringMetres = 38.9f;

    /// <summary>
    /// The rung change at level 79 with the war line and Harm Other VI, the run to the entry's front 2 s long; as it reaches the door the hovering Zefir attacks her from 38.9 m. Her bolts reach <paramref name="boltReach"/> m: the live
    /// fight's dat terms (base 30 + 0.2 a point of War Magic init+ranks; 68.2 m at her 191 live) with the base set so the rig's own init+ranks gives that reach. Harm Other VI reaches 33 m (the live fights' "Harm Other VI reaches 33 m").
    /// </summary>
    private static RungRig HoveringZefirRig(float boltReach, Action<RungRig, uint, uint> castAt)
    {
        var bolts = ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells.Select(b => (ushort)b.Id).ToArray();
        var t = new RungRig(level: 79, spells: [.. bolts, (ushort)HarmOtherVI]);
        t.Caster.Table[HarmOtherVI] = HarmSpell() with { BaseRangeConstant = 33f };
        Assert.True(t.R.H.State.Self.Qualities.TryGetSkill(MageTemplate.WarMagic, out var war));
        foreach (var id in bolts)
        {
            t.Caster.Table[id] = t.Caster.Table[id] with { BaseRangeConstant = boltReach - (ScriptedCaster.WarBoltRangeMod * (war.Init + war.Ranks)), BaseRangeMod = ScriptedCaster.WarBoltRangeMod };
            Assert.Equal(boltReach, FightScenario.CastRange(t.Caster.Table[id], war.Init + war.Ranks), 3);
        }

        t.Portals(Routes, "holtburg-to-network", "network-to-refuge");
        t.Entry(BurrowEntry, Burrow);
        var first = true;
        t.Travel.RunsFor = r =>
        {
            if (r.Destination.Name != "entry-front" || !first)
            {
                return null;
            }

            first = false;
            return 2;
        };
        t.Travel.OnRun = (_, _, arrives) =>
        {
            if (arrives)
            {
                t.Attacker(HoveringZefir, "Kilif Zefir", HoveringMetres, ZefirWcid);
            }
        };
        t.CastAt = (spell, target) => castAt(t, spell, target);
        return t;
    }

    [Fact]
    public void The_live_bolt_range_is_68_2_m_at_war_magic_init_plus_ranks_191()
    {
        var bolt = WorldActionRig.Spell(2128, SpellType.Projectile, SpellFlags.Projectile) with { BaseRangeConstant = ScriptedCaster.WarBoltRangeConstant, BaseRangeMod = ScriptedCaster.WarBoltRangeMod };
        Assert.Equal(68.2f, FightScenario.CastRange(bolt, 191), 3); // live ladder51: her fight's range_m=68.2
        Assert.Equal(FightScenario.MaxRange, FightScenario.CastRange(bolt, 400)); // ACE caps it at MaxRadarRange_Outdoors, 75 m
    }

    /// <summary>
    /// M6 14 the hovering Zefir (live ladder51 02:55:47 to 03:00:13 UTC: a Kilif Zefir hovering 38.9 m off was "beyond the cast range 15 m", waited for, left and taken back for 180 s with no cast, and the timeout recalled her at
    /// health 207 of 219; her bolt reached 68.2 m): at 38.9 m, within her bolt's 68.2 m, the fight-back bolts it at once and the walk goes on.
    /// </summary>
    [Fact]
    public void A_hovering_zefir_at_38_9_m_within_her_68_2_m_bolt_is_bolted_at_once()
    {
        var t = HoveringZefirRig(68.2f, (rig, _, target) => rig.Kill(target));

        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow), maxSeconds: 600);

        var trace = string.Join(" || ", t.Lines("fight-back").Concat(t.Lines("run past")).Take(20));
        Assert.True(status == ScenarioStatus.Succeeded, trace);
        Assert.Contains(" fail=none ", run.VerdictFields(), StringComparison.Ordinal);
        var cast = Assert.Single(t.Events, e => e.EndsWith("@8000802D", StringComparison.Ordinal));
        Assert.NotEqual($"cast:{HarmOtherVI}@8000802D", cast); // a bolt, not Life Magic
        Assert.Single(t.Lines("fight-back: 'Kilif Zefir' 0x8000802D is dead"));
        Assert.Empty(t.Lines("beyond"));
        Assert.Empty(t.Lines("runs past it"));
        Assert.Empty(t.Lines("still attacks after"));
        Assert.Contains("use:" + BurrowEntry.Guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), t.Events);
    }

    /// <summary>
    /// M6 14 the hovering Zefir, rules 2 and 3: at 38.9 m its bolts come to nothing (each projectile strikes the world, no line, as the Zefir of ladder42): after <see cref="FightScenario.WallHitsBeforeBlock"/> Life Magic would take it,
    /// but Harm Other VI reaches 33 m, so she runs past it and the walk goes on; its next attacks from 38.9 m take nothing back, and the door's portal is used. Never the 180 s timeout.
    /// </summary>
    [Fact]
    public void A_hovering_zefir_whose_bolts_come_to_nothing_beyond_harm_others_33_m_is_run_past_and_the_walk_goes_on()
    {
        var t = HoveringZefirRig(68.2f, (rig, _, target) => rig.Attacker(target, "Kilif Zefir", HoveringMetres, ZefirWcid)); // the bolt strikes the world, no line; it attacks again from where it hovers
        t.Projectile = spell => spell != HarmOtherVI;
        var hits = 0;

        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow), maxSeconds: 600, each: () =>
        {
            if (hits < 2 && t.Lines("fight-back: 'Kilif Zefir' 0x8000802D: its bolts came to nothing").Count > 0)
            {
                hits++;
                t.Hit("Kilif Zefir", 6); // Harm Other II from where it hovers
            }
        });

        var trace = string.Join(" || ", t.Lines("fight-back").Concat(t.Lines("run past")).Take(20));
        Assert.True(status == ScenarioStatus.Succeeded, trace);
        Assert.Contains(" fail=none ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Single(t.Lines($"{FightScenario.WallHitsBeforeBlock} bolts in a row came to nothing"));
        Assert.Equal(FightScenario.WallHitsBeforeBlock, t.Events.Count(e => e.EndsWith("@8000802D", StringComparison.Ordinal)));
        Assert.DoesNotContain($"cast:{HarmOtherVI}@8000802D", t.Events);
        var passed = Assert.Single(t.Lines("fight-back: 'Kilif Zefir' 0x8000802D: its bolts came to nothing"));
        Assert.Contains("Harm Other VI reaches 33 m, the target stands 38.9 m off", passed, StringComparison.Ordinal);
        Assert.Contains("she runs past it and the walk goes on; it is fought again only if it attacks her from within 33 m", passed, StringComparison.Ordinal);
        Assert.True(hits > 0, trace);
        Assert.Empty(t.Lines("fight-back: 'Kilif Zefir' 0x8000802D was left ")); // its attacks from 38.9 m took nothing back
        Assert.Empty(t.Lines("still attacks after"));
        Assert.Empty(t.Lines("retreat due"));
        Assert.Contains("use:" + BurrowEntry.Guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), t.Events);
    }

    /// <summary>
    /// M6 14 the hovering Zefir, rule 3: her bolts reach 30 m (the dat's base with no skill points) and Harm Other VI 33 m, so at 38.9 m no spell of hers reaches it: she runs past it at once, with no cast and no wait, and the walk goes
    /// on; it attacks her again from 38.9 m and stays left, and the door's portal is used.
    /// </summary>
    [Fact]
    public void A_zefir_beyond_every_spells_range_is_run_past_at_once_with_no_cast()
    {
        var t = HoveringZefirRig(30f, (rig, _, target) => rig.Kill(target));
        var hits = 0;

        var (run, status) = t.RunStation(new RungChangeStation(), t.Context(Burrow), maxSeconds: 600, each: () =>
        {
            if (hits < 2 && t.Lines("she runs past it").Count > 0)
            {
                hits++;
                t.Hit("Kilif Zefir", 6);
            }
        });

        var trace = string.Join(" || ", t.Lines("fight-back").Concat(t.Lines("run past")).Take(20));
        Assert.True(status == ScenarioStatus.Succeeded, trace);
        Assert.Contains(" fail=none ", run.VerdictFields(), StringComparison.Ordinal);
        Assert.DoesNotContain(t.Events, e => e.EndsWith("@8000802D", StringComparison.Ordinal)); // no cast at it
        var passed = Assert.Single(t.Lines("fight-back: 'Kilif Zefir' 0x8000802D: it stands 38.9 m off (planar, as ACE measures), beyond "));
        Assert.Contains("'s range 30 m, and no life spell answers it (", passed, StringComparison.Ordinal);
        Assert.Contains("Harm Other VI reaches 33 m, the target stands 38.9 m off", passed, StringComparison.Ordinal);
        Assert.Contains("she runs past it and the walk goes on; it is fought again only if it attacks her from within 33 m", passed, StringComparison.Ordinal);
        Assert.True(hits > 0, trace);
        Assert.Empty(t.Lines("fight-back: 'Kilif Zefir' 0x8000802D was left "));
        Assert.Empty(t.Lines("no cast at it in"));
        Assert.Empty(t.Lines("still attacks after"));
        Assert.Contains("use:" + BurrowEntry.Guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), t.Events);
    }

    [Fact]
    public void A_failed_walk_step_is_named_for_the_errands_retry_and_nothing_else_is()
    {
        Assert.True(AutopilotScenario.ErrandWalkFailed("go:hop:retreat:hop:recall")); // live ladder42 20:20:15 UTC
        Assert.True(AutopilotScenario.ErrandWalkFailed("go:off-place"));
        Assert.False(AutopilotScenario.ErrandWalkFailed("combine:failed-roll"));
        Assert.False(AutopilotScenario.ErrandWalkFailed("hunt-until:short:3/16"));
        Assert.False(AutopilotScenario.ErrandWalkFailed(null));
        Assert.Equal(3, AutopilotScenario.LadderErrandWalkTries);
    }
}
