using System.Collections.Immutable;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M5 7 P7o, the item-end gate's scripted death that never came (<c>--autopilot-test-death</c>): F23's vitals guard healed during the scripted death, its magic mode cancelled the D37 lie-down, ACE's non-combat defence factor stopped and the
/// mage evaded 691 of 718 swings in 600 s. The guard now stands down during a SCRIPTED death until the death is seen; an unscripted death's station stays guarded. The scripted ACE's mage is given its vitals, its Life Magic and Heal Self
/// here, so the guard would really heal (the rig's own description has none).
/// </summary>
public sealed class AutopilotP7oTests(ITestOutputHelper output)
{
    private const uint Tusker = 0x80000F70;

    /// <summary>The spell table with the Heal Self line (the scripted caster's, in the real table's shape).</summary>
    internal static readonly Dictionary<uint, Lifestone.Data.Magic.SpellInfo> Table = new ScriptedCaster(new WorldActionRig(), 400, 200, withDrain: true).Table;

    /// <summary>The autopilot over the world's context with the Heal Self table (the guard's spells).</summary>
    internal static AutopilotScenario Autopilot(ApWorld w, AutopilotOptions options, IAutopilotSession? host = null)
    {
        var c = w.Trip.Context();
        return new AutopilotScenario(options, w.Factory, new JourneyContext { Character = c.Character, Account = c.Account, Log = c.Log, Travel = c.Travel, Spells = Table, Dungeon = c.Dungeon }, null, host);
    }

    /// <summary>
    /// A PlayerDescription that keeps the pack and the worn set and gives the mage its attributes, its magic skills, its vitals (health <paramref name="healthPercent"/> of its maximum, mana 400) and Heal Self I to VII, then non-combat
    /// (the description replaces the integer properties).
    /// </summary>
    internal static void Wounded(ApWorld w, uint healthPercent)
    {
        var h = w.Trip.Vendor.H;
        var inventory = h.State.Self.Inventory;
        var attributes = new[] { MageTemplate.Strength, MageTemplate.Endurance, MageTemplate.Quickness, MageTemplate.Coordination, MageTemplate.Focus, MageTemplate.Self }
            .Select(id => new AttributeDesc(id, (uint)(MageTemplate.TargetOf(MageStatKind.Attribute, id)?.Ranks ?? 0), MageTemplate.CreationValue(id), 0)).ToImmutableArray();
        var skills = new[] { MageTemplate.CreatureEnchantment, MageTemplate.ItemEnchantment, MageTemplate.LifeMagic, MageTemplate.WarMagic, MageTemplate.ManaConversion }
            .Select(id => (Id: id, T: MageTemplate.TargetOf(MageStatKind.Skill, id)!))
            .Select(s => new SkillDesc(s.Id, (ushort)s.T.Ranks, 1, s.T.Specialized ? 3u : 2u, 0, s.T.Specialized ? 10u : 0u, 0, 0)).ToImmutableArray();
        var description = new PlayerDescriptionMsg
        {
            Attributes = attributes,
            Skills = skills,
            Vitals = [new VitalDesc(MageTemplate.MaxHealth, 100, 0, 0, 200), new VitalDesc(MageTemplate.MaxStamina, (uint)MageTemplate.TargetOf(MageStatKind.Vital, MageTemplate.MaxStamina)!.Ranks, 0, 0, 200), new VitalDesc(MageTemplate.MaxMana, (uint)MageTemplate.TargetOf(MageStatKind.Vital, MageTemplate.MaxMana)!.Ranks, 0, 0, 400)],
            Inventory = [.. inventory.Pack.Select(e => new ContentDesc(e.Guid, e.ContainerType))],
            Equipped = [.. inventory.Equipped.Select(e => new EquippedDesc(e.Guid, e.Location, e.Priority))],
            Spells = VitalsMeans.HealSelf.ToImmutableDictionary(id => id, _ => 2f),
        };
        h.Describe(description);
        w.Trip.Vendor.R.CombatModeIs(CombatModes.NonCombat);
        h.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max);
        w.Trip.Vendor.R.Raw(AceWireWriters.PrivateUpdateAttribute2ndLevel(90, MageTemplate.MaxHealth + 1, max * healthPercent / 100));
        w.Trip.Vendor.R.Tick();
    }

    /// <summary>A living target creature of the ground <paramref name="metres"/> from the mage (a tusker in reach).</summary>
    internal static void Hostile(ApWorld w, float metres)
    {
        var at = w.Trip.View.State.Self.Position!.Value;
        var r = w.Trip.Vendor.H.R;
        var basis = ReducerHarness.Create(Tusker, ReducerHarness.Stamps(), ReducerHarness.Position(at.CellId, at.Frame.Origin.X + metres, at.Frame.Origin.Y, at.Frame.Origin.Z), name: "Tusker Guard");
        r.Reducer.OnCreateObject(r.Context, basis with { Weenie = basis.Weenie with { ItemType = 0x10, WeenieClassId = w.Ground.TargetWcids()[0] } }, isUpdate: false);
    }

    /// <summary>The mode changes and casts the Action API was asked for (sent, or refused by the client's own guards: the scripted mage holds no caster).</summary>
    internal static int Asked(ApWorld w)
    {
        var log = w.Trip.View.ActionLog;
        return Enumerable.Range(0, log.Count).Count(i => log[i].Kind is ActionKind.SetCombatMode or ActionKind.CastSpell);
    }

    /// <summary>A death station that waits (the wait among the tuskers) until <paramref name="dies"/> says so, then dies once (the victim notice), stands at the lifestone and waits there (the corpse trip still to come).</summary>
    private static IStationRun WaitingDeath(ApWorld w, Func<bool> dies)
    {
        var died = false;
        return new ScriptedRun(
            "death",
            _ =>
            {
                if (!died && dies())
                {
                    died = true;
                    w.Trip.Vendor.H.R.Bus.Publish(new PlayerDied("You have been slain by a Tusker Guard!", 0, DeathNoticeSource.VictimNotification));
                    w.Trip.Vendor.StandAt(w.Ground.Home.LifestoneCell, w.Ground.Home.LifestonePoint);
                }

                return null;
            },
            () => "deaths=1 corpse_recovered=no");
    }

    private void Dump(ApWorld w)
    {
        foreach (var line in w.Lines.Where(l => l.Contains("guard", StringComparison.Ordinal)).Take(40))
        {
            output.WriteLine(line);
        }
    }

    [Fact]
    public void P7o_the_guard_stands_down_in_a_scripted_deaths_wait_under_the_line_with_a_tusker_in_reach_and_is_back_once_the_death_is_seen()
    {
        var w = new ApWorld(Den, output, atHome: false);
        var die = false;
        w.Factory.Script = spec => spec.Kind == StationKinds.Death && spec.NeedsOverride is null ? WaitingDeath(w, () => die) : null;
        var autopilot = Autopilot(w, new AutopilotOptions { TestDeath = true });
        w.Run(autopilot, a => a.State == AutopilotState.Death && a.CurrentRun is not null);
        Assert.Equal(AutopilotState.Death, autopilot.State);

        Wounded(w, 20); // under the emergency line
        Hostile(w, 5); // a Tusker Guard 5 m away
        var before = Asked(w);
        var until = w.Now + 30;
        w.Run(autopilot, a => w.Now >= until);
        Dump(w);

        Assert.Equal(AutopilotState.Death, autopilot.State);
        Assert.Equal(before, Asked(w)); // no SetCombatMode, no cast: the lie-down's non-combat holds
        Assert.Single(w.Lines, l => l.Contains(AutopilotScenario.GuardStandsDownLine, StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.Contains("guard: death: ", StringComparison.Ordinal) && !l.Contains(AutopilotScenario.GuardStandsDownLine, StringComparison.Ordinal));

        // the death is seen (the victim notice): the respawn, still under the line, is guarded again
        die = true;
        var after = w.Now + 10;
        w.Run(autopilot, a => w.Now >= after);
        Dump(w);

        Assert.Contains(w.Lines, l => l.Contains("guard: death: the scripted death is seen: the guard is back", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("guard: death: entering the magic mode for Heal Self", StringComparison.Ordinal));
        Assert.True(Asked(w) > before, "the guard asked for the magic mode to heal");
    }

    [Fact]
    public void P7o_an_unscripted_deaths_corpse_trip_under_the_line_is_guarded()
    {
        var died = false;
        var w = new ApWorld(Den, output, atHome: false, onKill: (world, kills) =>
        {
            if (kills == 2 && !died)
            {
                died = true;
                world.Trip.Vendor.H.R.Bus.Publish(new PlayerDied("You have been slain by a Tusker Guard!", 0, DeathNoticeSource.VictimNotification));
            }
        });
        w.Factory.Script = spec => spec.Kind == StationKinds.Death && spec.NeedsOverride is not null ? new ScriptedRun("death", _ => null, () => "deaths=1 corpse_recovered=no") : null; // the corpse trip in progress
        var autopilot = Autopilot(w, new AutopilotOptions { Minutes = 10 });
        w.Run(autopilot, a => a.State == AutopilotState.Death && a.CurrentRun is not null);
        Assert.Equal(AutopilotState.Death, autopilot.State);

        Wounded(w, 20);
        Hostile(w, 5);
        var before = Asked(w);
        var until = w.Now + 10;
        w.Run(autopilot, a => w.Now >= until);
        Dump(w);

        Assert.DoesNotContain(w.Lines, l => l.Contains(AutopilotScenario.GuardStandsDownLine, StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("guard: death: entering the magic mode for Heal Self", StringComparison.Ordinal));
        Assert.True(Asked(w) > before, "the guard asked for the magic mode to heal");
    }

    [Theory]
    [InlineData(AutopilotState.Death, false, false, true)] // the scripted death's wait: stands down
    [InlineData(AutopilotState.Death, false, true, false)] // the scripted death, seen: the respawn, the corpse trip, the handler's fight
    [InlineData(AutopilotState.Death, true, false, false)] // an unscripted death's station
    [InlineData(AutopilotState.Trip, false, false, false)]
    [InlineData(AutopilotState.Upkeep, false, false, false)]
    public void P7o_the_guard_stands_down_only_for_a_scripted_death_not_yet_seen(AutopilotState state, bool unscripted, bool seen, bool down) =>
        Assert.Equal(down, AutopilotScenario.GuardStandsDown(state, unscripted, seen));
}
