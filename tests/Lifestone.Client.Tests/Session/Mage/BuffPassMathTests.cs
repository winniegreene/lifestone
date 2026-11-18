using System.Collections.Immutable;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 7 AN: a climber's buff pass cannot stall: it waits by name, by rest, press and potion, to a named limit, then goes on with what stands, and the lines it left are not due at once; the pass and the bootstrap under 13% vitae; the Lodge's gate with every condition met lets the move on through (the two left open by slice AK).
/// </summary>
public sealed class BuffPassMathTests
{
    private sealed class PassRig
    {
        public PassRig(uint mana, uint stamina, double shootFirst = 0, ILiveBodies? bodies = null)
        {
            R = new WorldActionRig();
            Caster = new ScriptedCaster(R, mana, stamina);
            Ledger = new ArmourBuffLedger();
            var book = R.H.State.Self.Spellbook;
            book.IsKnown = true;
            foreach (var spell in BuffProfiles.Climber.Lines.Where(l => l.IsBuff && ArenaProfile.LineNamed(l.Name) is not null).SelectMany(l => l.Spells).Where(s => s.Position <= 4)) // the lines the scripted caster's table holds; M6 14 (m6-banes): the six other banes have their own tests (ArmourBanesTests)
            {
                book.Learn(spell.Id);
            }

            // M6 16 E2b: with bodies given the cycle is built over a session that draws them (as every production site builds it: over the session); else over the read model alone, where the reports answer
            Cycle = bodies is null
                ? new BuffCycle(R.Api, R.H.State, Caster.Table, new BuffOptions { ShootFirstMetres = shootFirst }, R.H.Log, Ledger, null, BuffProfiles.Climber)
                : new BuffCycle(new FightManaTests.DrawnView(R, new LivePositions(bodies, R.H.R.Clock)), Caster.Table, new BuffOptions { ShootFirstMetres = shootFirst }, R.H.Log, Ledger, null, BuffProfiles.Climber);
        }

        public WorldActionRig R { get; }

        public ScriptedCaster Caster { get; }

        public ArmourBuffLedger Ledger { get; }

        public BuffCycle Cycle { get; }

        public double Now => R.H.R.Clock.Now;

        public BuffStatus Run(double maxSeconds)
        {
            var end = Now + maxSeconds;
            var status = BuffStatus.Running;
            while (status == BuffStatus.Running && Now < end)
            {
                R.Tick(1);
                Caster.Pump();
                R.Tick();
                status = Cycle.Step(Now);
            }

            return status;
        }

        public bool Logged(string text) => R.H.Log.Lines.Any(l => l.Contains(text, StringComparison.Ordinal));
    }

    private static ImmutableArray<EnchantmentDesc> Vitae(float share) =>
        [new(666, 1, 666, 0, 666, 0, 1800.0, 0x50000001, 0f, 0f, 0.0, EnchantmentRules.Multiplicative, 0, share, 0, EnchantmentBucket.Vitae)];

    [Fact]
    public void With_no_mana_coming_the_pass_ends_done_at_its_named_limit_never_failed_and_names_what_is_missing()
    {
        var t = new PassRig(mana: 0, stamina: 0);
        t.Caster.ManaRegenPerSecond = 0;
        t.Caster.StaminaRegenPerSecond = 0;

        Assert.True(t.Cycle.Start(t.Now));
        var started = t.Now;
        var status = t.Run(1200);

        Assert.Equal(BuffStatus.Done, status);
        Assert.Null(t.Cycle.Result!.Failure);
        Assert.True(t.Now - started > 800, string.Join(" | ", t.R.H.Log.Lines.TakeLast(12)));
        Assert.InRange(t.Now - started, LadderLoader.WarMage.Vitals.BuffPassWaitSeconds, LadderLoader.WarMage.Vitals.BuffPassWaitSeconds + 30);
        Assert.True(t.Logged("buff: pass: the mana limit of 900 s is up"), "the limit is named");
        Assert.NotEmpty(t.Ledger.PassLeft);
        Assert.DoesNotContain(t.Cycle.RebuffDue(t.Now + 1, 60), l => t.Ledger.PassLeft.Contains(l)); // the upkeep does not stop the fight for them at once
        Assert.Contains(t.Cycle.RebuffDue(t.Now + 901, 60), l => t.Ledger.PassLeft.Contains(l)); // and they come due again
    }

    [Fact]
    public void A_climbers_pass_ends_with_what_stands_so_the_station_judges_no_line_as_failed()
    {
        var t = new PassRig(mana: 0, stamina: 0);
        t.Caster.ManaRegenPerSecond = 0;
        t.Caster.StaminaRegenPerSecond = 0;
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run(1200));

        var pass = t.Cycle.Result!;
        Assert.True(pass.EndsWithWhatStands);
        Assert.True(pass.Active < pass.Wanted);
        var climber = new BuffLegScenarioResult(true, null, "gear=bare worn=0", pass, null, UnattendedEnd: true);
        Assert.DoesNotContain(BuffLegVerdict.Compose(climber, default).Problems, p => p.StartsWith("buffs=", StringComparison.Ordinal));
        var other = climber with { Cycle = pass with { EndsWithWhatStands = false } }; // the den's mage keeps her judge
        Assert.Contains(BuffLegVerdict.Compose(other, default).Problems, p => p.StartsWith("buffs=", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, "buff: pass: check after: the server's motion shows the posture 0x0014", "buff: pass: rest: posture lie")]
    [InlineData(false, "buff: pass: check after: failed=no-echo: she was asked to lie down", "buff: pass: rest: posture stand")]
    public void The_wait_of_a_pass_checks_the_servers_motion_after_the_key_and_ends_with_the_rate_reached(bool echo, string check, string rate)
    {
        var t = new PassRig(mana: 0, stamina: 0);
        t.Caster.ManaRegenPerSecond = 0; // no mana comes: she waits at rest to the pass's named limit
        t.Caster.StaminaRegenPerSecond = 0;
        var h = t.R.H.R;
        h.Reducer.OnPlayerTeleport(h.Context, new PlayerTeleportMsg(2));
        h.Reducer.OnUpdatePosition(h.Context, ReducerHarness.Move(WorldActionRig.Player, 1, 2, 2, 1, cell: 0xA9B30034, x: 100));
        t.R.H.Unhide(stateStamp: 5);
        var spy = new Lifestone.Client.Tests.Session.Vitals.C2VitalsRig.PostureSpy();
        if (echo)
        {
            ushort movement = 1;
            spy.Echo = command => h.Reducer.OnUpdateMotion(h.Context, new UpdateMotionMsg(WorldActionRig.Player, 1, ++movement, 1, false, new ServerMotionState { MovementType = MovementType.Interpreted, Stance = 0x003D, Forward = command == VitalsKeeper.LieCommand ? (ushort)0x0014 : null }));
        }

        t.R.Api.Move.Bind(spy, false);

        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run(3000));

        Assert.True(t.Logged("buff: pass: lying down for the wait: check before: peace"), string.Join(" | ", t.R.H.Log.Lines.TakeLast(12)));
        Assert.True(t.Logged(check), string.Join(" | ", t.R.H.Log.Lines.TakeLast(12)));
        Assert.True(t.Logged(rate), string.Join(" | ", t.R.H.Log.Lines.TakeLast(12)));
        Assert.Equal(0u, spy.Posture); // up before the cast
    }

    [Fact]
    public void A_short_pool_waits_by_the_press_and_the_pass_ends_with_one_line_of_its_cost()
    {
        var t = new PassRig(mana: 60, stamina: 60);
        t.Caster.ManaRegenPerSecond = 2;
        t.Caster.StaminaRegenPerSecond = 2;

        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run(3000));

        var line = Assert.Single(t.R.H.Log.Lines, l => l.Contains("buff: pass: casts=", StringComparison.Ordinal));
        foreach (var field in new[] { " seconds=", " potions=", " rests=", " presses=", " stands=" })
        {
            Assert.Contains(field, line, StringComparison.Ordinal);
        }

        Assert.False(t.Logged("FAILED"), "the pass never fails on mana");
    }

    [Fact]
    public void A_pass_at_full_vitals_under_13_percent_vitae_reads_the_live_skill_and_ends_done()
    {
        var t = new PassRig(mana: 400, stamina: 200);
        t.R.H.State.Self.Qualities.Enchantments = Vitae(0.87f);

        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run(3000));
        Assert.True(t.Logged("buff: pass: casts="), "the pass's line");
    }

    [Fact]
    public void Under_13_percent_vitae_the_bootstrap_stands_on_the_scaled_base_and_the_mastery_stays_unscaled()
    {
        PlayerQualities Winnie(float? vitae)
        {
            var q = new PlayerQualities { IsKnown = true, Enchantments = vitae is { } v ? Vitae(v) : [] };
            q.Attributes[MageTemplate.Focus] = new AttributeDesc(MageTemplate.Focus, 31, 100, 0);
            q.Attributes[MageTemplate.Self] = new AttributeDesc(MageTemplate.Self, 30, 100, 0);
            q.Skills[MageTemplate.CreatureEnchantment] = new SkillDesc(MageTemplate.CreatureEnchantment, 50, 1, 2u, 0, 0u, 0, 0);
            q.Skills[MageTemplate.LifeMagic] = new SkillDesc(MageTemplate.LifeMagic, 75, 1, 2u, 0, 0u, 0, 0);
            q.Skills[MageTemplate.WarMagic] = new SkillDesc(MageTemplate.WarMagic, 66, 1, 2u, 0, 0u, 0, 0);
            q.Skills[MageTemplate.ItemEnchantment] = new SkillDesc(MageTemplate.ItemEnchantment, 0, 1, 2u, 0, 0u, 0, 0);
            return q;
        }

        var bar = LadderLoader.WarMage.PlanBuffChance;
        var whole = BuffReach.Walk(Winnie(null), bar, static (_, _) => true)!;
        var under = BuffReach.Walk(Winnie(0.87f), bar, static (_, _) => true)!;

        Assert.True(under.Creature < whole.Creature, $"{under.Creature} against {whole.Creature}");
        Assert.True(under.Life < whole.Life);
        Assert.True(under.CreatureMastery <= whole.CreatureMastery);
        Assert.True(under.Life > whole.Life * 0.87, "the mastery is added after the vitae");
    }

    [Fact]
    public void With_every_condition_of_the_gate_met_the_rung_moves_on_to_the_Lodge()
    {
        var ladder = LadderLoader.WarMage;
        var q = new PlayerQualities { IsKnown = true };
        q.Skills[MageTemplate.ItemEnchantment] = new SkillDesc(MageTemplate.ItemEnchantment, 21, 1, 2u, 0, 0u, 0, 0);
        var conditions = RungGateRule.Check(ladder.GateOf("lodge")!, q, static (_, _) => true, static _ => 4, static _ => 4);

        Assert.All(conditions, c => Assert.True(c.Met, c.Name));
        var held = RungGateRule.HeldLevel(ladder, 17, conditions);
        Assert.Equal(17, held);
        Assert.Equal("rung-moved:lodge", AutopilotScenario.MoveOnDue(ladder, ladder.Rungs.Single(r => r.Key == "burrow"), held, "tusker-burrow")?.Stop);
    }

    private static void Creature(PassRig t, uint guid, float metres)
    {
        var h = t.R.H.R;
        var at = t.R.H.State.Self.Position!.Value;
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), ReducerHarness.Position(at.CellId, at.Frame.Origin.X + metres, at.Frame.Origin.Y, at.Frame.Origin.Z), name: "Mite Scion");
        h.Reducer.OnCreateObject(h.Context, basis with { Weenie = basis.Weenie with { ItemType = 0x10, ObjectDescriptionFlags = WorldObjectTable.AttackableFlag } }, isUpdate: false);
    }

    [Theory]
    [InlineData(5f)] // within the fight's reach: M6 7 AT's rule
    [InlineData(22f)] // past it, inside the quiet distance: stretch 12's Mite Scion
    public void A_pass_in_the_ground_ends_when_a_creature_keeps_her_on_her_feet_and_does_not_change_modes_to_its_limit(float metres)
    {
        // Stretch 12: 901 s, rests=730, 1,400 mode changes and no "cut at" line: the cut was read only inside the wait, and it leaves the wait first.
        var t = new PassRig(mana: 0, stamina: 0, shootFirst: 20);
        t.Caster.ManaRegenPerSecond = 0;
        t.Caster.StaminaRegenPerSecond = 0;
        var h = t.R.H.R;
        h.Reducer.OnPlayerTeleport(h.Context, new PlayerTeleportMsg(2));
        h.Reducer.OnUpdatePosition(h.Context, ReducerHarness.Move(WorldActionRig.Player, 1, 2, 2, 1, cell: 0xA8B50034, x: 100));
        t.R.H.Unhide(stateStamp: 5);
        Creature(t, 0x80002083, metres);

        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run(120));

        Assert.True(t.Logged("buff: pass: cut at "), string.Join(" | ", t.R.H.Log.Lines.TakeLast(12)));
        Assert.False(t.Logged("the mana limit of"));
        Assert.True(t.R.H.Log.Lines.Count(l => l.Contains("combat mode to ", StringComparison.Ordinal)) <= 3, "the cut ends the wait: no mode loop");
    }

    [Fact]
    public void A_pass_at_home_with_a_creature_past_any_cut_stands_and_says_its_posture_and_rate()
    {
        var t = new PassRig(mana: 0, stamina: 0); // no reach: a pass at home
        t.Caster.ManaRegenPerSecond = 0;
        t.Caster.StaminaRegenPerSecond = 0;
        var h = t.R.H.R;
        h.Reducer.OnPlayerTeleport(h.Context, new PlayerTeleportMsg(2));
        h.Reducer.OnUpdatePosition(h.Context, ReducerHarness.Move(WorldActionRig.Player, 1, 2, 2, 1, cell: 0xA9B30034, x: 100));
        t.R.H.Unhide(stateStamp: 5);
        Creature(t, 0x80002084, 10);

        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run(3000));

        Assert.True(t.Logged("the wait stands"));
        Assert.True(t.Logged("buff: pass: rest: posture stand"), string.Join(" | ", t.R.H.Log.Lines.TakeLast(12)));
    }

    /// <summary>M6 16 E2b: a pass at home (no reach to cut for) at no mana with a creature the server last reported <paramref name="reportMetres"/> east of her and, with <paramref name="bodyMetres"/> given, drawn that far east of her; true when the wait stood on her feet for it.</summary>
    private static bool PassStoodFor(float reportMetres, float? bodyMetres)
    {
        const uint scion = 0x80002084;
        var bodies = bodyMetres is null ? null : new Lifestone.Client.Tests.Session.Contracts.ScriptedBodies();
        var t = new PassRig(mana: 0, stamina: 0, bodies: bodies);
        t.Caster.ManaRegenPerSecond = 0;
        t.Caster.StaminaRegenPerSecond = 0;
        var h = t.R.H.R;
        h.Reducer.OnPlayerTeleport(h.Context, new PlayerTeleportMsg(2));
        h.Reducer.OnUpdatePosition(h.Context, ReducerHarness.Move(WorldActionRig.Player, 1, 2, 2, 1, cell: 0xA9B30034, x: 100)); // a land cell: a body answers within 100 m of the report
        t.R.H.Unhide(stateStamp: 5);
        Creature(t, scion, reportMetres);
        bodies?.StandEast(scion, t.R.H.State.Self.Position!.Value, bodyMetres!.Value);

        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run(3000));
        return t.Logged("0x80002084 is within 24 m: the wait stands");
    }

    [Fact]
    public void The_pass_reads_a_creature_where_it_is_drawn_for_her_lie_down()
    {
        // reported 30 m off (past the quiet 24 m), drawn 10 m off: before (the report alone) the pass lay her down beside it; after, the wait stands on her feet
        Assert.False(PassStoodFor(reportMetres: 30, bodyMetres: null));
        Assert.True(PassStoodFor(reportMetres: 30, bodyMetres: 10));

        // reported 10 m off, drawn 40 m off: before, the wait stood; after, nothing is awake within 24 m and she lies down
        Assert.True(PassStoodFor(reportMetres: 10, bodyMetres: null));
        Assert.False(PassStoodFor(reportMetres: 10, bodyMetres: 40));
    }

    [Fact]
    public void The_lines_kept_up_in_a_fight_are_data_and_by_default_all_of_them()
    {
        var vitals = LadderLoader.WarMage.Vitals;
        Assert.Empty(vitals.StartOnlyLines!);
        Assert.False(vitals.StartOnly("Armor Self"));
        Assert.True((vitals with { StartOnlyLines = ["Coordination Self"] }).StartOnly("Coordination Self"));
    }

    [Fact]
    public void The_regeneration_lines_stand_with_the_protective_lines_in_her_pass_after_the_owners_order()
    {
        var names = BuffProfiles.Climber.Lines.Select(l => l.Name).ToList();
        Assert.True(names.IndexOf("Invulnerability Self") < names.IndexOf("Armor Self") || names.Contains("Armor Self"));
        Assert.Contains("Regeneration Self", names);
        Assert.Contains("Rejuvenation Self", names);
        Assert.Contains("Mana Renewal Self", names);
        Assert.False(LadderLoader.WarMage.Vitals.StartOnly("Rejuvenation Self"));
        Assert.Equal(ManaLoopMove.None, ManaLoopRule.Decide(45, 57, 57, LadderLoader.WarMage.Vitals, 0));
    }
}
