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
/// M3 3.1 fb: the stand-in's fight (<c>StandInWorld.Combat.cs</c>). A war bolt is cast at a placed tusker and the stand-in answers in the order ACE's code sends: the projectile (F745) and its Launch (F755), the flight for the distance
/// over the weenie's speed, the impact (SetState F74B, the Explode script F755, the zero velocity F74E), the attacker's line (F7E0, ACE's sentence and verb) while the tusker lives, and at zero the killer notification (0x01AD), the Dead
/// motion (F74C) and the experience (0x02CF twice); the projectile goes five seconds after the impact (F747), the tusker once its animation has played. A projectile with no target, or whose target is dead, flies on to the spell's
/// range. The health falls and a QueryHealth answers the real fraction. The tally of s0 reads the same lines, so the sentences are checked against it too. Nothing here is ACE: the offline stand-in is proved against ACE's code, the
/// live loops prove ACE.
/// </summary>
public sealed class StandInFightTests
{
    private const uint Bolt = 85; // Flame Bolt VI: in the ACE world extract
    private const uint Level7 = 6_001; // a Flame Bolt level the extract lacks (it wears its line's projectile)
    private const uint Tusker = 0x80000B01;
    private const uint FlameBoltSetup = 0x0200040D;
    private const uint FlameBoltEffects = 0x34000005;
    private const float FlameBoltSpeed = 15f;
    private const long StartTotalXp = 1_000;
    private const long StartAvailableXp = 500;

    private static readonly SpellInfo BoltSpell = WorldActionRig.Spell(Bolt, SpellType.Projectile) with { Name = "Flame Bolt VI", Power = 350 };
    private static readonly SpellInfo Level7Spell = WorldActionRig.Spell(Level7, SpellType.Projectile) with { Name = "Flame Bolt VII", Power = 300 };

    private static StandInVisit Visit(StandInPlayerStats? stats = null) => new()
    {
        EnterAfter = 0.2,
        UnhideAfter = 1.0,
        Stats = stats ?? new StandInPlayerStats { TotalExperience = StartTotalXp, AvailableExperience = StartAvailableXp },
        ProjectileSetup = id => id == Bolt ? FlameBoltSetup : null,
    };

    private sealed class Fight : IDisposable
    {
        public Fight(uint maxHealth = StandInTusker.PlaceholderMaxHealth, StandInVisit? visit = null)
        {
            Rig = new CastRig(visit ?? Visit(), [BoltSpell, Level7Spell]);
            Rig.Learn(Bolt, Level7);
            Rig.Mode(CombatMode.Magic);
            Tally = new FightTally();
            Tally.Start(Rig.Session);
            Server = StandInSessions.Server(Rig.Session.Reducer);
            At = Server.PlaceNear(StandInTusker.Make(Tusker, default, maxHealth: maxHealth), new Vector3(0f, 15f, 0f)) ?? throw new InvalidOperationException("the player has no position");
            Rig.Run(0.5);
            Assert.True(Rig.Session.State.Objects.Contains(Tusker));
            Rig.Begin();
            Rig.Session.Events.Subscribe((in ObjectMotionChanged e) =>
            {
                if (e.Guid == Tusker && e.Motion.Forward is { } word)
                {
                    TuskerMotions.Add(word);
                }
            });
        }

        public CastRig Rig { get; }

        public FightTally Tally { get; }

        public StandInServer Server { get; }

        public AcPosition At { get; }

        public List<ushort> TuskerMotions { get; } = [];

        public StandInWorld World => Rig.World;

        public ActionHandle Cast(uint spell = Bolt, uint target = Tusker)
        {
            var cast = Rig.Session.Actions.Cast(spell, target, ActionSource.Host);
            Rig.Run(1.6); // the flight (0.9 s) and the recoil (1 s)
            return cast;
        }

        /// <summary>The tags since <see cref="CastRig.Begin"/> without UseDone (its moment depends on the recoil, not on the fight).</summary>
        public string[] Tags => [.. Rig.Tags.Where(t => t != "01C7")];

        public void Dispose()
        {
            Tally.Dispose();
            Rig.Dispose();
        }
    }

    private static readonly string[] OneBolt = ["F745", "F755", "F74B", "F755", "F74E", "F7E0"];

    private static int PointsOf(string line)
    {
        const string marker = " points with ";
        var end = line.IndexOf(marker, StringComparison.Ordinal);
        var start = line.LastIndexOf(" for ", end, StringComparison.Ordinal) + " for ".Length;
        return int.Parse(line[start..end], CultureInfo.InvariantCulture);
    }

    private static IEnumerable<string> MagicLines(CastRig rig) => rig.Chat.Where(l => l.ChatType == FightTally.MagicChatType).Select(l => l.Text);

    [Fact]
    public void A_placed_tusker_stands_where_it_is_told_is_attackable_and_answers_query_health_with_a_full_bar()
    {
        using var fight = new Fight();
        Assert.True(fight.Rig.Session.State.Objects.TryGet(Tusker, out var tusker));
        Assert.Equal((StandInTusker.Name, StandInTusker.Wcid, StandInTusker.Setup), (tusker.Name, tusker.Weenie.WeenieClassId, tusker.Physics.SetupId));
        Assert.NotEqual(0u, tusker.Weenie.ObjectDescriptionFlags & StandInTusker.AttackableFlag);
        Assert.Equal(fight.At.CellId, tusker.Position!.Value.CellId);
        var player = fight.Rig.Session.State.Self.Position!.Value.ToGlobal();
        var there = tusker.Position.Value.ToGlobal();
        Assert.Equal(15.0, Math.Sqrt(Math.Pow(there.X - player.X, 2) + Math.Pow(there.Y - player.Y, 2)), 0.05);

        var query = fight.Rig.Session.Actions.QueryHealth(Tusker, ActionSource.Host);
        fight.Rig.Run(1.0);

        Assert.True(query.IsCompleted, query.Summary);
        Assert.Equal(1f, fight.Rig.Session.State.Objects.TryGet(Tusker, out var asked) ? asked.Health!.Value : -1f, 3);
        Assert.Equal(1f, fight.Server.HealthOf(Tusker)!.Value, 3);
    }

    [Fact]
    public void A_bolt_is_created_in_the_missile_state_on_the_spells_setup_and_flies_at_the_weenies_speed_toward_the_target()
    {
        using var fight = new Fight();
        fight.Rig.Session.Actions.Cast(Bolt, Tusker, ActionSource.Host);
        fight.Rig.Run(0.5); // the stand-in answers 0.3 s after the cast is sent

        Assert.True(fight.Rig.Session.State.Objects.TryGet(StandInWorld.FirstProjectileGuid, out var bolt));
        Assert.Equal(("Flame Bolt", FlameBoltSetup, FlameBoltEffects), (bolt.Name, bolt.Physics.SetupId, bolt.Physics.PhysicsEffectTableId ?? 0u));
        Assert.NotEqual(PhysicsState.None, bolt.State & PhysicsState.Missile);
        Assert.Equal(PhysicsState.None, bolt.State & (PhysicsState.Ethereal | PhysicsState.IgnoreCollisions));
        Assert.Equal(FlameBoltSpeed, bolt.Velocity.Length(), 0.01f);
        var start = bolt.Position!.Value.ToGlobal();
        var target = fight.Rig.Session.State.Objects.TryGet(Tusker, out var tusker) ? tusker.Position!.Value.ToGlobal() : default;
        var toward = Vector3.Normalize(new Vector3((float)(target.X - start.X), (float)(target.Y - start.Y), (float)(target.Z - start.Z)));
        Assert.True(Vector3.Dot(toward, Vector3.Normalize(bolt.Velocity)) > 0.99f, "the bolt flies at the target");
        Assert.Equal(1, fight.World.BoltsLaunched);
        Assert.Equal(["F745", "F755"], fight.Tags.Take(2));
    }

    [Fact]
    public void A_hit_comes_in_aces_order_the_flight_takes_the_distance_over_the_speed_and_the_health_falls_by_the_damage_in_the_line()
    {
        using var fight = new Fight();
        var cast = fight.Rig.Session.Actions.Cast(Bolt, Tusker, ActionSource.Host);
        fight.Rig.Run(0.5);

        // The flight: from the projectile's origin to two thirds of the tusker's height, less the two radii, at 15 m/s.
        Assert.True(fight.Rig.Session.State.Objects.TryGet(StandInWorld.FirstProjectileGuid, out var bolt));
        var from = bolt.Position!.Value.ToGlobal();
        var chest = fight.Rig.Session.State.Objects.TryGet(Tusker, out var body) ? body.Position!.Value.ToGlobal() : default;
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

        // The line: ACE's sentence with the fire verb for the share of the maximum health the blow took, and the health that is left.
        var line = Assert.Single(MagicLines(fight.Rig));
        var damage = PointsOf(line);
        Assert.Equal($"You {StandInDamageText.Verb(StandInDamageType.Fire, damage / (float)StandInTusker.PlaceholderMaxHealth)} Tusker Slave for {damage} points with Flame Bolt VI.", line);
        Assert.InRange(damage, (int)Math.Round(350 * StandInWorld.SpellDamageMinOfPower), (int)Math.Round(350 * StandInWorld.SpellDamageMaxOfPower));
        var expected = (StandInTusker.PlaceholderMaxHealth - damage) / (float)StandInTusker.PlaceholderMaxHealth;
        Assert.Equal(expected, fight.Server.HealthOf(Tusker)!.Value, 4);
        var query = fight.Rig.Session.Actions.QueryHealth(Tusker, ActionSource.Host);
        fight.Rig.Run(1.0);
        Assert.True(query.IsCompleted, query.Summary);
        Assert.Equal(expected, fight.Rig.Session.State.Objects.TryGet(Tusker, out var tusker) ? tusker.Health!.Value : -1f, 4);

        // The tally reads the same line; the bolt's own object is gone by now.
        Assert.Equal((1, damage, 0), (fight.Tally.DamageLinesDealt, (int)fight.Tally.DamageDealt, fight.Tally.Kills));
        Assert.Equal(0, fight.Tally.UnparsedMagicLines);
        Assert.False(fight.Rig.Session.State.Objects.Contains(StandInWorld.FirstProjectileGuid));
        Assert.Equal((1, 0, 0), (fight.World.BoltsHit, fight.World.BoltsMissed, fight.World.Kills));
    }

    [Fact]
    public void The_kill_is_the_notice_the_dead_motion_and_the_experience_in_aces_order_with_no_damage_line_for_the_killing_blow()
    {
        using var fight = new Fight();
        var casts = 0;
        while (fight.World.Kills == 0 && casts < 10)
        {
            fight.Cast();
            casts++;
        }

        fight.Rig.Run(6.0);
        Assert.Equal(1, fight.World.Kills);
        Assert.InRange(casts, 3, 5); // 400 health, 96 to 158 a blow

        // The last bolt: impact, then the notice (OnDeath), the Dead motion (Die), then the two experience updates (the player's queued action). The corpse and the tusker's removal follow once its animation has played; every bolt is deleted.
        var tags = fight.Tags;
        var notice = Array.IndexOf(tags, "01AD");
        Assert.Equal(["F74B", "F755", "F74E", "01AD", "F74C", "02CF", "02CF"], tags[(notice - 3)..(notice + 4)]);
        Assert.Equal(casts + 1, tags.Count(t => t == "F745")); // every bolt, and the creature's corpse (M4 2.1 s3: the corpse comes with the tusker's removal, one tick)
        Assert.Equal(casts - 1, tags.Count(t => t == "F7E0")); // no line for the killing blow (SpellProjectile.cs 857-861)
        Assert.Equal(casts + 1, tags.Count(t => t == "F747"));
        var noticeAt = fight.Rig.First("01AD");
        Assert.Contains(fight.Rig.Timeline, e => e.Tag == "F747" && Math.Abs(e.At - noticeAt - StandInWorld.DeathAnimationSeconds) < 0.03);

        // The lines: one per blow that did not kill, each ACE's sentence; the tally adds them up and recognises every one.
        var lines = MagicLines(fight.Rig).ToList();
        Assert.Equal(casts - 1, lines.Count);
        Assert.All(lines, l => Assert.Matches(@"^You (burn|scorch|incinerate|singe) Tusker Slave for \d+ points with Flame Bolt VI\.$", l));
        Assert.Equal((casts - 1, lines.Sum(PointsOf), 1, 0), (fight.Tally.DamageLinesDealt, (int)fight.Tally.DamageDealt, fight.Tally.Kills, fight.Tally.UnparsedMagicLines));

        // The notice: one of the fire sentences with the tusker's name; the tusker is on the floor (the Dead motion), at no health.
        Assert.Contains(fight.Tally.LastKillText, StandInDamageText.KillerMessages(StandInDamageType.Fire).Select(m => string.Format(CultureInfo.InvariantCulture, m, "Tusker Slave")));
        Assert.Equal([MotionCommandTable.ToWire(MotionCommands.Dead)], fight.TuskerMotions);
        Assert.Equal(0f, fight.Server.HealthOf(Tusker)!.Value);

        // The experience: the tusker's 30,000 on top of what the character had, the total first and then the available.
        var q = fight.Rig.Q;
        Assert.Equal((StartTotalXp + StandInTusker.XpOverride, StartAvailableXp + StandInTusker.XpOverride), (q.TotalExperience, q.AvailableExperience));
        Assert.Equal(StandInTusker.XpOverride, fight.Tally.XpGained);
        Assert.False(fight.Rig.Session.State.Objects.Contains(Tusker));
        Assert.False(fight.Rig.Session.State.Objects.Contains(StandInWorld.FirstProjectileGuid + (uint)casts - 1));
    }

    [Fact]
    public void A_bolt_at_a_target_that_is_not_there_flies_on_and_is_deleted_at_the_spells_range()
    {
        using var fight = new Fight();
        fight.Rig.Session.Actions.Cast(Bolt, 0x7A000001u, ActionSource.Host);
        fight.Rig.Run(6.0);

        Assert.Equal(["F745", "F755", "F747"], fight.Tags);
        Assert.Equal(StandInWorld.MaxMissileRange / FlameBoltSpeed, fight.Rig.First("F747") - fight.Rig.First("F745"), 0.03);
        Assert.Empty(MagicLines(fight.Rig));
        Assert.Equal((0, 1, 0), (fight.World.BoltsHit, fight.World.BoltsMissed, fight.World.Kills));
        Assert.Equal(1f, fight.Server.HealthOf(Tusker)!.Value);
    }

    [Fact]
    public void A_bolt_that_arrives_after_the_target_died_flies_on_and_is_deleted_at_the_same_range()
    {
        using var fight = new Fight(maxHealth: 50); // the first blow (96 or more) kills
        fight.Cast();
        Assert.Equal(1, fight.World.Kills);

        fight.Rig.Begin();
        fight.Rig.Session.Actions.Cast(Bolt, Tusker, ActionSource.Host);
        fight.Rig.Run(7.0);

        // The second bolt arrives while the dead tusker still stands (it goes two seconds after its Dead motion): no impact, no notice, and its own deletion at the range.
        Assert.Equal(2, fight.Tags.Count(t => t == "F745")); // the second bolt, and the first kill's corpse two seconds after its Dead motion
        Assert.DoesNotContain("F74B", fight.Tags);
        Assert.DoesNotContain("01AD", fight.Tags);
        Assert.Equal(StandInWorld.MaxMissileRange / FlameBoltSpeed, fight.Rig.Timeline.Last(e => e.Tag == "F747").At - fight.Rig.First("F745"), 0.03);
        Assert.Equal((1, 1, 1), (fight.World.BoltsHit, fight.World.BoltsMissed, fight.World.Kills));
        Assert.Equal(1, fight.Tally.Kills);
    }

    [Fact]
    public void A_level_the_extract_lacks_wears_its_lines_projectile_and_hits_the_same()
    {
        using var fight = new Fight();
        fight.Cast(Level7);
        fight.Rig.Run(5.0);

        Assert.True(fight.Rig.Session.State.Objects.Contains(Tusker));
        Assert.Equal(OneBolt, fight.Tags.Take(OneBolt.Length));
        var line = Assert.Single(MagicLines(fight.Rig));
        Assert.EndsWith(" points with Flame Bolt VII.", line);
        Assert.Contains(fight.Rig.Log.Entries, e => e.Line.Contains("TODO(m3-standin-projectile-weenies)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_spell_the_host_has_no_projectile_for_keeps_the_old_placeholder_and_a_visit_object_without_health_keeps_the_placeholder_fraction()
    {
        var visit = Visit() with { ProjectileSetup = null, Objects = [new StandInObject(0x80000B02, "Stand-in Guard", new Vector3(0f, 6f, 0f), DescriptionFlags: 0x10)] };
        using var rig = new CastRig(visit, [BoltSpell]);
        rig.Learn(Bolt);
        rig.Mode(CombatMode.Magic);
        rig.Begin();

        rig.Session.Actions.Cast(Bolt, 0x80000B02u, ActionSource.Host);
        rig.Run(3.0);
        var query = rig.Session.Actions.QueryHealth(0x80000B02u, ActionSource.Host);
        rig.Run(1.0);

        Assert.Equal(["F745", "F747"], rig.Tags.Where(t => t is "F745" or "F747"));
        Assert.DoesNotContain("F74B", rig.Tags);
        Assert.Equal(StandInWorld.PlaceholderHealth, rig.Session.State.Objects.TryGet(0x80000B02u, out var guard) ? guard.Health!.Value : -1f, 3);
        Assert.True(query.IsCompleted, query.Summary);
    }

    [Fact]
    public void A_creature_of_the_visit_with_health_is_held_without_being_placed()
    {
        var visit = Visit() with { Objects = [StandInTusker.Make(0x80000B03, new Vector3(0f, 8f, 0f), maxHealth: 200)] };
        using var rig = new CastRig(visit, [BoltSpell]);
        Assert.Equal(1f, rig.World.HealthFraction(0x80000B03)!.Value);
        Assert.Null(rig.World.HealthFraction(0x80000B09));
        Assert.True(rig.Session.State.Objects.TryGet(0x80000B03, out var tusker));
        Assert.Equal(StandInTusker.Wcid, tusker.Weenie.WeenieClassId);
    }

    [Fact]
    public void The_damage_words_are_aces_the_fire_verbs_by_tier_and_every_damage_type_has_death_messages()
    {
        Assert.Equal(["incinerate", "burn", "scorch", "singe"], new[] { 0.51f, 0.26f, 0.11f, 0.1f }.Select(p => StandInDamageText.Verb(StandInDamageType.Fire, p)));
        Assert.Equal(["mangle", "gore", "crush", "freeze", "dissolve", "blast", "eradicate", "hit"], new[]
        {
            StandInDamageType.Slash, StandInDamageType.Pierce, StandInDamageType.Bludgeon, StandInDamageType.Cold, StandInDamageType.Acid, StandInDamageType.Electric, StandInDamageType.Nether, StandInDamageType.Undef,
        }.Select(t => StandInDamageText.Verb(t, 0.9f)));
        foreach (var type in Enum.GetValues<StandInDamageType>())
        {
            Assert.All(StandInDamageText.KillerMessages(type), m => Assert.Contains("{0}", m, StringComparison.Ordinal));
        }

        Assert.Equal(StandInDamageType.Fire, StandInDamageText.TypeOf(BoltSpell));
        Assert.Equal(StandInDamageType.Cold, StandInDamageText.TypeOf(BoltSpell with { Name = "Frost Bolt III" }));
        Assert.Equal(StandInDamageType.Undef, StandInDamageText.TypeOf(BoltSpell with { Name = "Mystery" }));
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

        var cast = rig.Session.Actions.Cast(bolt.Id, 0x7A000001u, ActionSource.Host);
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
