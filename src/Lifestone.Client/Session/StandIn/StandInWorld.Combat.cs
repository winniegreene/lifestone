// Re-expresses ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/WorldObject_Magic.cs (CreateSpellProjectiles, CalculateProjectileOrigins, CalculateProjectileVelocity, LaunchSpellProjectiles, GetProjectileSpeed, DoSpellEffects),
// SpellProjectile.cs (Setup, GetProjectileScriptIntensity, ProjectileImpact, OnCollideObject) (AGPL-3.0) [with changes: the stand-in's player is the only caster; the target stands still, so there is no intercept solver; the
// sizes are placeholders, named and logged where they are used. M4 4.1 t2 (D23, D26): the stand-in holds no creature, so a bolt does no damage and kills nothing; the damage, the death and the experience are the live server's].
using System.Numerics;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Magic;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.StandIn;

/// <summary>
/// The stand-in's war bolt (M3 3.1 fb; <c>TODO(m3-standin-projectile-model)</c> closed): a war bolt as ACE sends it. For a spell whose projectile the host knows (<see cref="StandInVisit.ProjectileSetup"/> set, and the ACE world
/// extract holds the spell or its line) the cast creates the projectile with the spell's Setup, effect and sound tables and velocity at the weenie's speed (CreateObject F745 in the Missile state, then the Launch script F755), and the
/// projectile flies for the distance over the speed. At a target the stand-in holds it hits in <c>ProjectileImpact</c>'s order (SetState F74B, the Explode script F755, the zero velocity F74E) and plays the spell's own target effect (F755);
/// the projectile is deleted five seconds after the impact (F747). A projectile with no target flies on and is deleted at the spell's range. A spell the host does not know a projectile for keeps the old placeholder object
/// (<see cref="StandInWorld.CreateProjectile"/>). The stand-in holds no creature (M4 4.1 t2, D23, D26): a war spell cast at a guid it does not hold is ended as ACE ends it (<c>TargetNotAcquired</c>, <c>StandInWorld.Actions.cs</c>), and no
/// bolt does damage; a creature's health, its death and the experience of a kill are proved by the live legs and by the scripted scenario tests, which draw nothing.
/// </summary>
internal sealed partial class StandInWorld
{
    /// <summary>ACE's <c>ProjHeight</c> (<c>WorldObject_Magic.cs</c> 1526): the share of a body's height a bolt leaves from and aims at.</summary>
    internal const float ProjHeight = 2f / 3f;

    /// <summary>How long after the impact ACE deletes the projectile (<c>ProjectileImpact</c>'s self-destruct chain).</summary>
    internal const double ImpactLingerSeconds = 5.0;

    /// <summary>The farthest a missed projectile flies (ACE's radar range outdoors, <c>Player.MaxRadarRange_Outdoors</c>: the stand-in's stand-in for the physics that ends a miss; <c>TODO(m3-standin-projectile-range)</c>).</summary>
    internal const float MaxMissileRange = 75f;

    /// <summary>The caster's and a target's height, radius and the projectile's radius when no Setup is at hand (placeholders: the stand-in has no dats; <c>TODO(m3-standin-object-sizes)</c>).</summary>
    internal const float PlaceholderBodyHeight = 1.8f;

    /// <summary>See <see cref="PlaceholderBodyHeight"/>.</summary>
    internal const float PlaceholderBodyRadius = 0.48f;

    /// <summary>See <see cref="PlaceholderBodyHeight"/>.</summary>
    internal const float PlaceholderProjectileRadius = 0.1f;

    private static readonly Lazy<AceWorldExtract?> ExtractOnce = new(() =>
    {
        try
        {
            return AceWorldExtract.Load();
        }
        catch (Exception e) when (e is FileNotFoundException or InvalidDataException)
        {
            return null;
        }
    });

    /// <summary>What the stand-in knows of a spell's projectile weenie (the ACE world extract's row, or its line's).</summary>
    private readonly record struct ProjectileModel(uint Wcid, string Name, uint Setup, float Speed, float Scale, uint? EffectTable, uint? SoundTable, Vector3 CreateOffset, bool FromLine, bool SpeedKnown, int Count);

    /// <summary>Spell projectiles launched with the model (the old placeholder counts in <see cref="Projectiles"/> only).</summary>
    internal int BoltsLaunched { get; private set; }

    /// <summary>Projectiles that reached their target (a hit, with or without damage).</summary>
    internal int BoltsHit { get; private set; }

    /// <summary>Projectiles that flew on past a missing or dead target, or had none.</summary>
    internal int BoltsMissed { get; private set; }

    /// <summary>Places <paramref name="kind"/> at <paramref name="at"/>: the stand-in keeps it from now (a chest, a vendor, a lifestone: <see cref="_placed"/>), and sends its CreateObject the next time the session ticks.</summary>
    internal void PlaceObject(StandInObject kind, AcPosition at)
    {
        _placed[kind.Guid] = kind;
        Later(sink =>
        {
            AnswerLog.Add("F745");
            sink.OnCreateObject(Context(sink), StandInSessions.ObjectMessage(kind, at, _visit.LookFor(kind.Name)), isUpdate: false);
        });
    }

    // --- the projectile -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>The projectile weenie of <paramref name="info"/> when the host opted in (<see cref="StandInVisit.ProjectileSetup"/> set) and the stand-in knows enough of it; null: the old placeholder.</summary>
    private ProjectileModel? ModelFor(SpellInfo info)
    {
        if (_visit.ProjectileSetup is not { } setupOf)
        {
            return null;
        }

        WarProjectileEntry? entry = null;
        var fromLine = false;
        if (ExtractOnce.Value is { } extract && !extract.ProjectilesBySpell.TryGetValue(info.Id, out entry))
        {
            // A level the extract lacks (the war line's VII, the Incantation) wears its line's projectile: in ACE the levels of a line share one projectile weenie (Flame Bolt I to VI are wcid 1499).
            var stem = Stem(info.Name);
            foreach (var candidate in extract.Projectiles)
            {
                if (Stem(candidate.SpellName) == stem)
                {
                    entry = candidate;
                    fromLine = true;
                    break;
                }
            }
        }

        var known = setupOf(info.Id);
        if (known is null && entry is null)
        {
            return null;
        }

        return new ProjectileModel(
            entry?.Wcid ?? 2,
            entry?.WeenieName ?? info.Name,
            known ?? entry!.SetupId,
            entry?.MaximumVelocity ?? ProjectileSpeed,
            entry?.DefaultScale ?? 1f,
            entry?.PhysicsEffectTableId,
            entry?.SoundTableId is { } sound && sound != 0 ? sound : null,
            entry?.CreateOffset ?? Vector3.Zero,
            fromLine,
            entry?.MaximumVelocity is not null,
            entry?.NumProjectiles ?? 1);
    }

    /// <summary>The spell's name without a "Incantation of " prefix or a trailing level numeral ("Flame Bolt VI" and "Incantation of Flame Bolt" are the line "Flame Bolt").</summary>
    private static string Stem(string name)
    {
        const string incantation = "Incantation of ";
        if (name.StartsWith(incantation, StringComparison.Ordinal))
        {
            name = name[incantation.Length..];
        }

        var space = name.LastIndexOf(' ');
        if (space > 0 && name[(space + 1)..] is "I" or "II" or "III" or "IV" or "V" or "VI" or "VII" or "VIII")
        {
            name = name[..space];
        }

        return name;
    }

    /// <summary>ACE's <c>SpellProjectile.GetProjectileScriptIntensity</c> for a single bolt, streak or arc (<c>SpellProjectile.cs</c> 191-223): the Launch and Explode scripts' intensity by the spell's level.</summary>
    private static float ScriptIntensity(SpellInfo info) => SpellFormulaRules.AceSpellLevel(info.Power) switch
    {
        2 => 0.2f,
        3 => 0.4f,
        4 => 0.6f,
        5 => 0.8f,
        >= 6 => 1.0f,
        _ => 0f,
    };

    /// <summary>How far the spell's projectile flies before it is deleted: ACE's range formula (<c>Monster_Magic.cs</c> <c>GetSpellMaxRange</c>: the base range plus the skill times its modifier, up to the radar range; none means no limit).</summary>
    private static float RangeOf(SpellInfo info)
    {
        var range = Math.Min(info.BaseRangeConstant + (PlaceholderMagicSkill * info.BaseRangeMod), MaxMissileRange);
        return range <= 0f ? MaxMissileRange : range;
    }

    private static Quaternion HeadingOf(Vector3 direction)
    {
        var yaw = MathF.Atan2(-direction.X, direction.Y);
        var pitch = MathF.Asin(Math.Clamp(direction.Z, -1f, 1f));
        return Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw));
    }

    /// <summary>
    /// ACE's <c>CreateSpellProjectiles</c> for one bolt: the origin (the weenie's create offset, the radii's sum in front of the caster and two thirds of the caster's height up), the velocity (toward two thirds of the target's height, at the
    /// weenie's <c>MaximumVelocity</c>) and the CreateObject and Launch it broadcasts; then the arrival, timed by the distance over the speed (a still target: no intercept solver).
    /// </summary>
    private void LaunchBolt(IAcSessionSink sink, SpellInfo info, uint target, ProjectileModel model)
    {
        var self = _reducer.State.Self;
        var guid = _nextProjectileGuid++;
        Projectiles++;
        BoltsLaunched++;
        Placeholder("m3-standin-object-sizes", "the caster's and the target's height and radius and the projectile's radius are placeholders (" + PlaceholderBodyHeight + ", " + PlaceholderBodyRadius + ", " + PlaceholderProjectileRadius + " m): the stand-in has no Setups, so the bolt's origin, aim and arrival are close to ACE's, not equal");
        if (model.FromLine)
        {
            Placeholder("m3-standin-projectile-weenies", "the extract holds the projectile of some levels only: a level it lacks (" + info.Name + ") is launched as its line's projectile (the levels of a line share one weenie in ACE)");
        }

        if (!model.SpeedKnown)
        {
            Placeholder("m3-standin-projectile-weenies", "no MaximumVelocity is known for the projectile of " + info.Name + ": it flies at " + ProjectileSpeed + " m/s");
        }

        if (model.Count > 1)
        {
            Placeholder("m3-standin-projectile-weenies", info.Name + " launches " + model.Count + " projectiles in ACE: the stand-in launches one, along the aim");
        }

        var cell = self.Position?.CellId ?? _visit.Cell;
        var here = self.Position?.Origin ?? _visit.Origin;
        var facing = Quaternion.Normalize(self.Position?.Orientation ?? Quaternion.Identity);
        var shooter = AcCoordinates.ToGlobal(new CellPosition(cell, here));
        GlobalPosition? goal = null;
        const float height = PlaceholderBodyHeight;
        var aimed = false;
        if (target != 0 && target != self.Guid && _reducer.State.Objects.TryGet(target, out var victim) && victim.Position is { } at)
        {
            goal = at.ToGlobal();
            aimed = true;
        }

        // ACE turns the projectile's frame to face the target before it adds the origin (WorldObject_Magic.cs 1782-1786).
        var rotate = facing;
        if (goal is { } g)
        {
            rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)Math.Atan2(-(g.X - shooter.X), g.Y - shooter.Y));
        }

        var origin = new Vector3(model.CreateOffset.X, model.CreateOffset.Y + (PlaceholderBodyRadius * 2f) + (PlaceholderProjectileRadius * 2f), model.CreateOffset.Z + (PlaceholderBodyHeight * ProjHeight));
        var offset = Vector3.Transform(origin, rotate);
        var start = shooter + offset;
        var toGoal = 0.0;
        Vector3 direction;
        if (goal is { } end)
        {
            var aim = new Vector3((float)(end.X - start.X), (float)(end.Y - start.Y), (float)((end.Z + (height * ProjHeight)) - start.Z));
            toGoal = aim.Length();
            direction = toGoal > 1e-4 ? aim / (float)toGoal : Vector3.Transform(Vector3.UnitY, rotate);
        }
        else
        {
            direction = Vector3.Transform(Vector3.UnitY, facing);
        }

        var speed = model.Speed;
        var localStart = here + offset;
        var startCell = cell;
        if (AcCoordinates.IsOutdoorCell(startCell))
        {
            AcCoordinates.AdjustToOutside(ref startCell, ref localStart);
        }

        var flags = PhysicsDescFlags.CSetup | PhysicsDescFlags.Position | PhysicsDescFlags.Velocity | PhysicsDescFlags.DefaultScript | PhysicsDescFlags.DefaultScriptIntensity;
        flags |= model.Scale != 1f ? PhysicsDescFlags.ObjScale : PhysicsDescFlags.None;
        flags |= model.EffectTable is not null ? PhysicsDescFlags.PeTable : PhysicsDescFlags.None;
        flags |= model.SoundTable is not null ? PhysicsDescFlags.STable : PhysicsDescFlags.None;
        var physics = new PhysicsDesc
        {
            Flags = flags,
            Velocity = direction * speed,
            // SpellProjectile.Setup (SpellProjectile.cs 76-90): ReportCollisions, Missile, AlignPath, PathClipped; not Ethereal, not IgnoreCollisions. The weenie's own base state is not known (TODO(m3-standin-projectile-weenies)).
            State = PhysicsState.ReportCollisions | PhysicsState.Missile | PhysicsState.AlignPath | PhysicsState.PathClipped | PhysicsState.LightingOn, // ACE sets LightingOn on a spell projectile (the bolt lights its surroundings: M3 4.1); ProjectileImpact clears it (the impact SetState below)
            Position = new AcPosition(startCell, localStart, HeadingOf(direction)),
            SetupId = model.Setup,
            Scale = model.Scale != 1f ? model.Scale : null,
            PhysicsEffectTableId = model.EffectTable,
            SoundTableId = model.SoundTable,
            DefaultScript = (uint)PlayScript.ProjectileCollision, // SpellProjectile.cs 98-102: bolts, streaks, arcs, volleys and blasts
            DefaultScriptIntensity = 1f,
            Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1),
        };
        var weenie = new WeenieDesc
        {
            Header = WeenieHeaderFlags.None,
            Name = model.Name,
            WeenieClassId = model.Wcid,
            IconId = 0x06000001,
            ItemType = 0,
            ObjectDescriptionFlags = 0,
        };
        AnswerLog.Add("F745");
        sink.OnCreateObject(Context(sink), new CreateObjectMsg(guid, ModelData.Empty, physics, weenie), isUpdate: false);
        PlayEffect(sink, guid, (uint)PlayScript.Launch, ScriptIntensity(info)); // LaunchSpellProjectiles: sp.EnqueueBroadcast(Launch) (WorldObject_Magic.cs 1823)

        var schedule = _reducer.StandIn!;
        var range = RangeOf(info);
        if (!aimed)
        {
            // No target: the projectile flies along the caster's heading until its range ends (a miss).
            BoltsMissed++;
            schedule.At(_reducer.Clock.Now + (range / speed), later => DeleteObject(later, guid));
            return;
        }

        // The projectile hits when it touches the target's body: the distance to its centre less the two radii.
        var flight = Math.Max(0.0, toGoal - PlaceholderBodyRadius - PlaceholderProjectileRadius);
        schedule.At(_reducer.Clock.Now + (flight / speed), later => ArriveBolt(later, guid, info, target, speed, flight, range));
    }

    private void DeleteObject(IAcSessionSink sink, uint guid)
    {
        AnswerLog.Add("F747");
        sink.OnDeleteObject(Context(sink), new DeleteObjectMsg(guid, 1));
    }

    /// <summary>The projectile reaches the place its target stood: a hit, or, when the target is gone, a miss that flies on to the spell's range.</summary>
    private void ArriveBolt(IAcSessionSink sink, uint guid, SpellInfo info, uint target, float speed, double flown, float range)
    {
        if (!_reducer.State.Objects.Contains(target))
        {
            BoltsMissed++;
            var remaining = Math.Max(0.0, range - flown);
            _reducer.StandIn!.At(_reducer.Clock.Now + (remaining / speed), later => DeleteObject(later, guid));
            return;
        }

        BoltsHit++;
        Impact(sink, guid, info);

        // DoSpellEffects(spell, caster, target, projectileHit: true): the spell's own target effect plays on the target (WorldObject_Magic.cs 358-365).
        if (info.TargetEffect != PlayScript.Invalid)
        {
            PlayEffect(sink, target, (uint)info.TargetEffect, SpellFormulaRules.AceEffectIntensity(info.Formula) ?? 1f);
        }

        _reducer.StandIn!.At(_reducer.Clock.Now + ImpactLingerSeconds, later => DeleteObject(later, guid));
    }

    /// <summary>ACE's <c>ProjectileImpact</c> (<c>SpellProjectile.cs</c> 225-263): the new state (no collision reports, ethereal, not drawn, cloaked), the Explode script, then the zero velocity.</summary>
    private void Impact(IAcSessionSink sink, uint guid, SpellInfo info)
    {
        AnswerLog.Add("F74B");
        sink.OnSetState(Context(sink), new SetStateMsg(guid, PhysicsState.Missile | PhysicsState.AlignPath | PhysicsState.PathClipped | PhysicsState.Ethereal | PhysicsState.IgnoreCollisions | PhysicsState.NoDraw | PhysicsState.Cloaked, 1, 2));
        PlayEffect(sink, guid, (uint)PlayScript.Explode, ScriptIntensity(info));
        AnswerLog.Add("F74E");
        sink.OnVectorUpdate(Context(sink), new VectorUpdateMsg(guid, Vector3.Zero, Vector3.Zero, 1, 2));
    }
}
