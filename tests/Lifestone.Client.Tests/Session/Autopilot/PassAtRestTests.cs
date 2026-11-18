using System.Globalization;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Messages;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 7 AW: a full pass in the ground with a creature near her walks home first, by name, and lies down there; with nothing near it stays. Stretch 12 stood 901 s in cell 0xA8B50034 with a Mite Scion in the quiet distance; stretch 13's pass in town lay
/// down at about 2 stamina a second.
/// </summary>
public sealed class PassAtRestTests(ITestOutputHelper output)
{
    private const uint Scion = 0x80002083;

    private static void Creature(ApWorld w, float metres)
    {
        var at = w.Trip.View.State.Self.Position!.Value;
        var r = w.Trip.Vendor.H.R;
        var basis = ReducerHarness.Create(Scion, ReducerHarness.Stamps(), ReducerHarness.Position(at.CellId, at.Frame.Origin.X + metres, at.Frame.Origin.Y, at.Frame.Origin.Z), name: "Mite Scion");
        r.Reducer.OnCreateObject(r.Context, basis with { Weenie = basis.Weenie with { ItemType = 0x10, ObjectDescriptionFlags = WorldObjectTable.AttackableFlag } }, isUpdate: false);
    }

    private static ApWorld Outskirts(ITestOutputHelper output)
    {
        var w = new ApWorld("holtburg-outskirts", output, atHome: false);
        w.Level(10);
        w.OwnKit();
        for (var i = 0u; i < 2; i++)
        {
            w.Trip.Own("Wand " + i.ToString(CultureInfo.InvariantCulture), 90_000 + i, KeepAsideRule.CasterType, 100);
        }

        var state = w.Trip.Vendor.State;
        state.Self.Spellbook.IsKnown = true;
        foreach (var spell in ScrivenerCatalog.Default.Spells)
        {
            state.Self.Spellbook.Learn(spell.SpellId);
        }

        state.Self.Qualities.Enchantments = [new(666, 1, 666, 0, 666, 0, 1800.0, 0x50000001, 0f, 0f, 0.0, EnchantmentRules.Multiplicative, 0, 1f, 0, EnchantmentBucket.Vitae)]; // one enchantment still up
        return w;
    }

    [Fact]
    public void Over_the_scripted_ACE_a_full_pass_with_a_creature_near_walks_home_then_runs()
    {
        var w = Outskirts(output);
        var walked = 0;
        w.Factory.Script = spec => spec.Kind == StationKinds.RungChange
            ? new ScriptedRun(spec.Name, _ =>
            {
                walked++;
                var home = w.Ground.Home;
                var r = w.Trip.Vendor.H.R;
                r.Reducer.OnDeleteObject(r.Context, new DeleteObjectMsg(Scion, 1));
                w.Trip.Vendor.StandAt(home.LifestoneCell, home.LifestonePoint);
                return ScenarioStatus.Succeeded;
            }, static () => string.Empty)
            : null;
        Creature(w, 22); // past the fight's reach, inside the quiet distance: stretch 12's Mite Scion
        var run = w.Autopilot(new AutopilotOptions { Job = AutopilotJob.LevelUp, Minutes = 30 });

        w.Run(run, _ => w.Factory.Made.Any(m => m.Kind == StationKinds.DenFight), 6000, _ => AnswerWieldsAndModes(w));

        Assert.Equal(1, walked);
        var place = w.Lines.First(l => l.Contains("ladder: pass: place: ", StringComparison.Ordinal));
        Assert.Contains("she walks home", place, StringComparison.Ordinal);
        Assert.Contains("blocker=0x80002083", place, StringComparison.Ordinal);
        var kinds = w.Factory.Made.Select(m => m.Kind).ToList();
        Assert.True(kinds.IndexOf(StationKinds.RungChange) < kinds.IndexOf(StationKinds.Buff), string.Join(",", kinds)); // home first, the pass there
        Assert.Contains(w.Lines, l => l.Contains("-> buff (cycle 1): home", StringComparison.Ordinal));
    }

    /// <summary>M6 16 E2b (the one world picture): the pass's place reads a creature where the session draws it. A Mite Scion the server last reported 40 m off (past the quiet 24 m) and drawn 22 m from her.</summary>
    [Fact]
    public void A_creature_drawn_22_m_from_her_and_last_reported_40_m_off_sends_the_full_pass_home()
    {
        // before (the report alone): nothing within 24 m: the pass ran where she stood
        var before = Outskirts(output);
        Creature(before, 40);
        before.Run(before.Autopilot(new AutopilotOptions { Job = AutopilotJob.LevelUp, Minutes = 30 }), _ => before.Factory.Made.Any(m => m.Kind == StationKinds.DenFight), 6000, _ => AnswerWieldsAndModes(before));
        Assert.Contains(before.Lines, l => l.Contains("ladder: pass: place: ", StringComparison.Ordinal) && l.EndsWith(": here", StringComparison.Ordinal));
        Assert.DoesNotContain(before.Factory.Made, m => m.Kind == StationKinds.RungChange);

        // after: its body stands 22 m from her: she walks home first, and the line names it
        var w = Outskirts(output);
        var walked = 0;
        w.Factory.Script = spec => spec.Kind == StationKinds.RungChange
            ? new ScriptedRun(spec.Name, _ =>
            {
                walked++;
                var home = w.Ground.Home;
                var r = w.Trip.Vendor.H.R;
                r.Reducer.OnDeleteObject(r.Context, new DeleteObjectMsg(Scion, 1));
                w.Trip.Vendor.StandAt(home.LifestoneCell, home.LifestonePoint);
                return ScenarioStatus.Succeeded;
            }, static () => string.Empty)
            : null;
        Creature(w, 40);
        w.Trip.View.Live = new Lifestone.Client.Tests.Session.Contracts.ScriptedBodies().StandEast(Scion, w.Trip.View.State.Self.Position!.Value, 22).Over(w.Trip.View.Clock);
        w.Run(w.Autopilot(new AutopilotOptions { Job = AutopilotJob.LevelUp, Minutes = 30 }), _ => w.Factory.Made.Any(m => m.Kind == StationKinds.DenFight), 6000, _ => AnswerWieldsAndModes(w));

        Assert.Equal(1, walked);
        var place = w.Lines.First(l => l.Contains("ladder: pass: place: ", StringComparison.Ordinal));
        Assert.Contains("she walks home", place, StringComparison.Ordinal);
        Assert.Contains("blocker=0x80002083", place, StringComparison.Ordinal);
    }

    [Fact]
    public void Over_the_scripted_ACE_a_full_pass_with_nothing_near_runs_where_she_stands()
    {
        var w = Outskirts(output);
        var run = w.Autopilot(new AutopilotOptions { Job = AutopilotJob.LevelUp, Minutes = 30 });

        w.Run(run, _ => w.Factory.Made.Any(m => m.Kind == StationKinds.DenFight), 6000, _ => AnswerWieldsAndModes(w));

        Assert.Contains(w.Lines, l => l.Contains("ladder: pass: place: ", StringComparison.Ordinal) && l.EndsWith(": here", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Factory.Made, m => m.Kind == StationKinds.RungChange);
    }
}
