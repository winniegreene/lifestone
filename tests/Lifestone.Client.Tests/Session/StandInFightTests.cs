using System.Globalization;
using System.Numerics;
using DatReaderWriter.Enums;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M3 3.1 fb: the stand-in's war bolt (<c>StandInWorld.Combat.cs</c>). A war bolt is cast at a placed object and the stand-in answers in the order ACE's code sends: the projectile (F745) and its Launch (F755), the flight for the distance
/// over the weenie's speed, the impact (SetState F74B, the Explode script F755, the zero velocity F74E); the projectile goes five seconds after the impact (F747). A projectile with no target, or whose target is gone, flies on to the
/// spell's range; a cast at a guid the stand-in does not hold ends as ACE ends it (UseDone TargetNotAcquired). M4 4.1 t2 (D23, D26) retired the stand-in's creature: the tests of its health, its damage lines, its kill and its experience were
/// deleted with it (they are proved live by the bsd-fight leg and by the scripted scenario tests, which draw nothing). Nothing here is ACE: the offline stand-in is proved against ACE's code, and the live loops prove ACE.
/// </summary>
public sealed class StandInFightTests
{
    private const uint Bolt = 85; // Flame Bolt VI: in the ACE world extract
    private const uint Level7 = 6_001; // a Flame Bolt level the extract lacks (it wears its line's projectile)
    private const uint Statue = 0x80000B01; // a non-creature placeholder object: the bolt's target
    private const uint FlameBoltSetup = 0x0200040D;
    private const uint FlameBoltEffects = 0x34000005;
    private const float FlameBoltSpeed = 15f;

    private static readonly SpellInfo BoltSpell = WorldActionRig.Spell(Bolt, SpellType.Projectile) with { Name = "Flame Bolt VI", Power = 350 };
    private static readonly SpellInfo Level7Spell = WorldActionRig.Spell(Level7, SpellType.Projectile) with { Name = "Flame Bolt VII", Power = 300 };

    private static StandInVisit Visit(StandInPlayerStats? stats = null) => new()
    {
        EnterAfter = 0.2,
        UnhideAfter = 1.0,
        Stats = stats ?? new StandInPlayerStats(),
        ProjectileSetup = id => id == Bolt ? FlameBoltSetup : null,
    };

    private sealed class Fight : IDisposable
    {
        public Fight(StandInVisit? visit = null)
        {
            Rig = new CastRig(visit ?? Visit(), [BoltSpell, Level7Spell]);
            Rig.Learn(Bolt, Level7);
            Rig.Mode(CombatMode.Magic);
            Server = StandInSessions.Server(Rig.Session.Reducer);
            At = Server.PlaceNear(new StandInObject(Statue, "Stand-in Statue", default, MotionTable: 0), new Vector3(0f, 15f, 0f)) ?? throw new InvalidOperationException("the player has no position");
            Rig.Run(0.5);
            Assert.True(Rig.Session.State.Objects.Contains(Statue));
            Rig.Begin();
        }

        public CastRig Rig { get; }

        public StandInServer Server { get; }

        public AcPosition At { get; }

        public StandInWorld World => Rig.World;

        public ActionHandle Cast(uint spell = Bolt, uint target = Statue)
        {
            var cast = Rig.Session.Actions.Cast(spell, target, ActionSource.Host);
            Rig.Run(1.6); // the flight (0.9 s) and the recoil (1 s)
            return cast;
        }

        /// <summary>The tags since <see cref="CastRig.Begin"/> without UseDone (its moment depends on the recoil, not on the fight).</summary>
        public string[] Tags => [.. Rig.Tags.Where(t => t != "01C7")];

        public void Dispose()
        {
            Rig.Dispose();
        }
    }

    private static readonly string[] OneBolt = ["02E9", "F745", "F755", "F74B", "F755", "F74E"]; // M5 1 K2: the cast's mana (02E9) comes with the effect, before the projectile

    private static IEnumerable<string> MagicLines(CastRig rig) => rig.Chat.Where(l => l.ChatType == FightTally.MagicChatType).Select(l => l.Text);

    [Fact]
    public void A_placed_object_stands_where_it_is_told_and_is_neither_attackable_nor_a_creature()
    {
        using var fight = new Fight();
        Assert.True(fight.Rig.Session.State.Objects.TryGet(Statue, out var statue));
        Assert.Equal(("Stand-in Statue", StandInVisit.HumanSetup), (statue.Name, statue.Physics.SetupId));
        Assert.Equal(0u, statue.Weenie.ObjectDescriptionFlags & 0x10); // not attackable
        Assert.Equal(0u, statue.Weenie.ItemType & 0x10); // not a creature (M4 4.1 t2: only a person is)
        Assert.Equal(fight.At.CellId, statue.Position!.Value.CellId);
        var player = fight.Rig.Session.State.Self.Position!.Value.ToGlobal();
        var there = statue.Position.Value.ToGlobal();
        Assert.Equal(15.0, Math.Sqrt(Math.Pow(there.X - player.X, 2) + Math.Pow(there.Y - player.Y, 2)), 0.05);
    }

    [Fact]
    public void A_bolt_is_created_in_the_missile_state_on_the_spells_setup_and_flies_at_the_weenies_speed_toward_the_target()
    {
        using var fight = new Fight();
        fight.Rig.Session.Actions.Cast(Bolt, Statue, ActionSource.Host);
        fight.Rig.Run(0.5); // the stand-in answers 0.3 s after the cast is sent

        Assert.True(fight.Rig.Session.State.Objects.TryGet(StandInWorld.FirstProjectileGuid, out var bolt));
        Assert.Equal(("Flame Bolt", FlameBoltSetup, FlameBoltEffects), (bolt.Name, bolt.Physics.SetupId, bolt.Physics.PhysicsEffectTableId ?? 0u));
        Assert.NotEqual(PhysicsState.None, bolt.State & PhysicsState.Missile);
        Assert.Equal(PhysicsState.None, bolt.State & (PhysicsState.Ethereal | PhysicsState.IgnoreCollisions));
        Assert.Equal(FlameBoltSpeed, bolt.Velocity.Length(), 0.01f);
        var start = bolt.Position!.Value.ToGlobal();
        var target = fight.Rig.Session.State.Objects.TryGet(Statue, out var statue) ? statue.Position!.Value.ToGlobal() : default;
        var toward = Vector3.Normalize(new Vector3((float)(target.X - start.X), (float)(target.Y - start.Y), (float)(target.Z - start.Z)));
        Assert.True(Vector3.Dot(toward, Vector3.Normalize(bolt.Velocity)) > 0.99f, "the bolt flies at the target");
        Assert.Equal(1, fight.World.BoltsLaunched);
        Assert.Equal(["02E9", "F745", "F755"], fight.Tags.Take(3));
    }

    [Fact]
    public void A_hit_comes_in_aces_order_and_the_flight_takes_the_distance_over_the_speed_with_no_damage_line()
    {
        using var fight = new Fight();
        var cast = fight.Rig.Session.Actions.Cast(Bolt, Statue, ActionSource.Host);
        fight.Rig.Run(0.5);

        // The flight: from the projectile's origin to two thirds of the body's height, less the two radii, at 15 m/s.
        Assert.True(fight.Rig.Session.State.Objects.TryGet(StandInWorld.FirstProjectileGuid, out var bolt));
        var from = bolt.Position!.Value.ToGlobal();
        var chest = fight.Rig.Session.State.Objects.TryGet(Statue, out var body) ? body.Position!.Value.ToGlobal() : default;
        var aim = new Vector3((float)(chest.X - from.X), (float)(chest.Y - from.Y), (float)(chest.Z + (StandInWorld.PlaceholderBodyHeight * StandInWorld.ProjHeight) - from.Z));
        var flight = (aim.Length() - StandInWorld.PlaceholderBodyRadius - StandInWorld.PlaceholderProjectileRadius) / FlameBoltSpeed;
        fight.Rig.Run(6.3);

        Assert.Equal(ActionOutcomeKind.Confirmed, cast.Outcome!.Value.Kind);
        Assert.Equal([.. OneBolt, "F747"], fight.Tags);
        var launch = fight.Rig.First("F745");
        var impact = fight.Rig.First("F74B");
        Assert.Equal(StandInWorld.ImpactLingerSeconds, fight.Rig.Timeline.Last(e => e.Tag == "F747").At - impact, 0.03);
        Assert.Equal(flight, impact - launch, 0.03);
        Assert.InRange(impact - launch, 0.8, 1.0);

        // The stand-in holds no creature (M4 4.1 t2): the hit does no damage, so there is no attacker's line, and the bolt's own object is gone by now.
        Assert.Empty(MagicLines(fight.Rig));
        Assert.False(fight.Rig.Session.State.Objects.Contains(StandInWorld.FirstProjectileGuid));
        Assert.Equal((1, 0), (fight.World.BoltsHit, fight.World.BoltsMissed));
    }

    [Fact]
    public void A_bolt_with_no_target_flies_on_along_the_heading_and_is_deleted_at_the_spells_range()
    {
        using var fight = new Fight();
        fight.Rig.Session.Actions.Cast(Bolt, ActionSource.Host);
        fight.Rig.Run(6.0);

        Assert.Equal(["02E9", "F745", "F755", "F747"], fight.Tags);
        Assert.Equal(StandInWorld.MaxMissileRange / FlameBoltSpeed, fight.Rig.First("F747") - fight.Rig.First("F745"), 0.03);
        Assert.Equal((0, 1), (fight.World.BoltsHit, fight.World.BoltsMissed));
    }


    [Fact]
    public void A_level_the_extract_lacks_wears_its_lines_projectile_and_hits_the_same()
    {
        using var fight = new Fight();
        fight.Cast(Level7);
        fight.Rig.Run(5.0);

        Assert.True(fight.Rig.Session.State.Objects.Contains(Statue));
        Assert.Equal(OneBolt, fight.Tags.Take(OneBolt.Length));
        Assert.Contains(fight.Rig.Log.Entries, e => e.Line.Contains("TODO(m3-standin-projectile-weenies)", StringComparison.Ordinal));
    }
}

/// <summary>
/// M3 3.1 fb: the cast planner plans any spell on demand (<c>TODO(m3-standin-cast-plan-lazy)</c> closed). The visit has no planner, only the factory; the first cast makes it, and Flame Bolt, a spell no host planned ahead, plays
/// ACE's words and gestures offline. Skipped without the dats.
/// </summary>
public sealed class StandInLazyPlannerTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [RequiresDatsFact]
    public void The_first_cast_makes_the_planner_and_a_spell_nobody_planned_plays_its_words_and_gestures()
    {
        var table = SpellTableData.Load(fixture.Dats);
        var bolt = table.Spells.Values.Where(s => s.Name == "Flame Bolt I").OrderBy(s => s.Id).First();
        var visit = new StandInVisit
        {
            EnterAfter = 0.2,
            UnhideAfter = 1.0,
            Stats = new StandInPlayerStats(),
            SpellTable = table,
            CastPlannerFactory = () => StandInCastPlanner.FromDats(fixture.Dats),
            ProjectileSetup = id => AceWorldExtract.Load().ProjectilesBySpell.TryGetValue(id, out var projectile) ? projectile.SetupId : null,
        };
        Assert.Null(visit.CastPlanner);
        using var rig = new CastRig(visit, null, fixture.Dats);
        rig.Learn(bolt.Id);
        rig.Mode(CombatMode.Magic);
        rig.Begin();

        var cast = rig.Session.Actions.Cast(bolt.Id, ActionSource.Host); // no target: a bolt along the heading (a guid the stand-in does not hold ends the cast before its words)
        rig.Run(6.0);

        Assert.Equal(ActionOutcomeKind.Confirmed, cast.Outcome!.Value.Kind);
        Assert.NotNull(visit.CastPlanner);
        Assert.Equal("02BB", rig.Tags[0]); // the words first, as ACE speaks them
        Assert.True(rig.Tags.Count(t => t == "F74C") >= 2, "a wind-up gesture and the talisman's, then Ready"); // the gestures
        Assert.Contains(rig.Log.Entries, e => e.Line.Contains("cast planner was made on demand", StringComparison.Ordinal));
        Assert.Equal(visit.CastPlanner!(bolt)!.Words, rig.Chat.Last(l => l.Kind == ChatKind.Speech && l.ChatType == StandInWorld.SpellcastingChat).Text);

        // Any other spell is planned when it is asked for, by the same planner, and kept.
        var other = table.Spells.Values.Where(s => s.Name == "Flame Bolt II").OrderBy(s => s.Id).First();
        Assert.Same(visit.CastPlanner!(other), visit.CastPlanner!(other));
    }
}
