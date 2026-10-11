// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/WorldObject_Magic.cs (DoSpellEffects,
// CreateSpellProjectiles, CalculatePreOffset, CalculateProjectileOrigins, CalculateProjectileVelocity,
// LaunchSpellProjectiles), Source/ACE.Server/WorldObjects/SpellProjectile.cs (Setup, ProjectileImpact,
// OnCollideObject), Source/ACE.Server/Physics/Trajectory.cs (solve_ballistic_arc_lateral) and
// Source/ACE.Server/Physics/Common/Position.cs (CylinderDistance) (AGPL-3.0) [with changes: stationary target only (no
// lead or tracking); Setup radius/height read from the dats via ObjectDimensions; projectiles are EffectActors driven by
// Lifestone's retail client-side effect and motion code; impact is a geometric test with a MaxFlightSeconds timeout
// instead of ACE's physics collision; server random draws use a seeded RetailRandom].
using System.Numerics;
using DatReaderWriter.Enums;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Effects;
using DatSetup = DatReaderWriter.DBObjs.Setup;

namespace Lifestone.Data.Magic;

/// <summary>Physical size of an object as ACE's spell code reads it from the Setup (<c>PartArray.GetRadius/GetHeight</c>, <c>GetPhysicsRadius</c>).</summary>
public readonly record struct ObjectDimensions(int PartCount, float Radius, float Height, float PhysicsRadius)
{
    /// <summary>Setup radius/height times the scale; physics radius = first CylSphere, else first Sphere, radius × scale.</summary>
    public static ObjectDimensions FromSetup(DatSet dats, uint setupId, float scale)
    {
        if (!dats.TryGetPortal<DatSetup>(setupId, out var setup))
        {
            throw new KeyNotFoundException($"Setup 0x{setupId:X8} not found.");
        }

        var physics = setup.CylSpheres.Count > 0 ? setup.CylSpheres[0].Radius : setup.Spheres.Count > 0 ? setup.Spheres[0].Radius : 0f;
        return new ObjectDimensions(setup.Parts.Count, setup.Radius * scale, setup.Height * scale, physics * scale);
    }
}

/// <summary>A projectile in flight (or after impact) in a <see cref="SpellCastSimulation"/>.</summary>
public sealed class SimulatedProjectile(EffectActor actor, Vector3 aimPoint, float radius, double launchTime)
{
    public EffectActor Actor { get; } = actor;

    /// <summary>Where it is headed (target origin + its height × 2/3, or × 5/6 for arcs; ACE <c>ProjHeight</c>).</summary>
    public Vector3 AimPoint { get; } = aimPoint;

    public float Radius { get; } = radius;

    public double LaunchTime { get; } = launchTime;

    /// <summary>Set at impact: ACE <c>ProjectileImpact</c> makes the object NoDraw (its particles keep playing).</summary>
    public bool Hidden { get; internal set; }

    public double? ImpactTime { get; internal set; }
}

/// <summary>One line of the simulation log.</summary>
public readonly record struct SimulationEvent(double Time, string Text);

/// <summary>
/// Plays one spell from a caster at a target the way the server and client would: the caster enters the Magic stance,
/// says the words, plays the <see cref="CastPlan"/> motions, the effects land (caster/target PlayScripts), projectiles
/// fly and explode. Engine-agnostic: call <see cref="AdvanceTo"/> every frame, then draw <see cref="Caster"/>,
/// <see cref="Target"/> and <see cref="Projectiles"/> (part frames + particles).
/// </summary>
/// <remarks>
/// Server-side rules come from ACE (@95ef426d, AGPL-3.0): <c>Player.CreatePlayerSpell</c> (sequence),
/// <c>WorldObject.DoSpellEffects</c> (who gets which effect), <c>CalculatePreOffset</c>,
/// <c>CalculateProjectileOrigins</c>, <c>CalculateProjectileVelocity</c>, <c>LaunchSpellProjectiles</c>,
/// <c>SpellProjectile.Setup</c>/<c>ProjectileImpact</c>, <c>Trajectory.solve_ballistic_arc_lateral</c> (stationary
/// target). Client-side rules are retail (motion table, sequence, scripts, particles, <c>CPhysicsObj::InitDefaults</c>
/// for the projectile's Setup default script/animation, <c>ACCWeenieObject::DoCollision</c> playing the default script
/// of objects with ScriptedCollision). Impact is a simple geometric test (no collision system yet): the projectile hits
/// when it comes within its radius plus the target's physics radius of the aim point, or passes it; after
/// <see cref="MaxFlightSeconds"/> it impacts without hitting.
/// Server-side random draws (projectile peturbation) use this simulation's seeded generator.
/// </remarks>
public sealed class SpellCastSimulation
{
    /// <summary>ACE <c>WorldObject.ProjHeight</c>.</summary>
    public const float ProjHeight = 2.0f / 3.0f;

    /// <summary>ACE <c>WorldObject.ProjHeightArc</c>.</summary>
    public const float ProjHeightArc = 5.0f / 6.0f;

    /// <summary>ACE <c>PhysicsGlobals.Gravity</c> (retail <c>CPhysicsObj</c> uses the same −9.8 m/s²).</summary>
    public const float Gravity = -9.8f;

    /// <summary>
    /// Simulation-only stand-in for hitting the environment (there is no collision system yet): a projectile that has
    /// not reached the target after this many seconds impacts where it is, without the target effect.
    /// </summary>
    public const double MaxFlightSeconds = 10.0;

    private readonly SpellVisualResolver _resolver;
    private readonly Queue<CastStep> _steps;
    private readonly List<SimulatedProjectile> _projectiles = [];
    private readonly List<SimulationEvent> _log = [];
    private readonly RetailRandom _serverRandom;
    private readonly ObjectDimensions _casterSize;
    private readonly ObjectDimensions _targetSize;
    private readonly int _seed;
    private readonly bool _selfTargeted;
    private bool _spoke;

    public SpellCastSimulation(SpellVisualResolver resolver, SpellVisual visual, AcFrame casterFrame, AcFrame targetFrame, int seed, double startTime = 0)
    {
        _resolver = resolver;
        Visual = visual;
        _seed = seed;
        _serverRandom = new RetailRandom(seed ^ 0x5EED);
        var dats = resolver.Animations.Dats;
        _selfTargeted = (visual.Spell.Flags & SpellFlags.SelfTargeted) != 0;

        _casterSize = ObjectDimensions.FromSetup(dats, visual.Caster.SetupId, visual.Caster.Scale);
        Caster = CreateActor(visual.Caster, _casterSize, casterFrame, seed, startTime);
        var target = _selfTargeted ? null : visual.Target;
        _targetSize = target is null ? _casterSize : ObjectDimensions.FromSetup(dats, target.SetupId, target.Scale);
        Target = target is null ? null : CreateActor(target, _targetSize, targetFrame, seed + 1, startTime);

        // The caster must be in Magic combat mode before ACE accepts a cast: enter it, wait for the style link.
        var player = (MotionPlayer)Caster.Motion!;
        player.ApplyInterpretedMotion(MotionCommands.Magic, MotionCommands.Ready, 1f);
        var settle = player.Sequence.Nodes.TakeWhile(n => n != player.Sequence.FirstCyclic?.Value)
            .Sum(n => (Math.Abs(n.HighFrame - n.LowFrame) + 1) / Math.Max(Math.Abs((double)n.Framerate), 1e-3));
        CastStartTime = startTime + settle;
        Time = startTime;
        _steps = new Queue<CastStep>(visual.Plan.Steps);
        Log(startTime, $"{visual.Caster.Name} enters the Magic stance (cast starts at {CastStartTime:F3} s)");
    }

    public SpellVisual Visual { get; }

    public EffectActor Caster { get; }

    /// <summary>The target object; null for self-targeted spells (their effects land on <see cref="Caster"/>).</summary>
    public EffectActor? Target { get; }

    public IReadOnlyList<SimulatedProjectile> Projectiles => _projectiles;

    /// <summary>When the cast plan's t = 0 happens (after the stance change).</summary>
    public double CastStartTime { get; }

    public double Time { get; private set; }

    /// <summary>What happened so far (chant, motions, effects, launches, impacts).</summary>
    public IReadOnlyList<SimulationEvent> Events => _log;

    /// <summary>True once the plan has run, every projectile has hit, and the recoil time is over.</summary>
    public bool CastFinished => _steps.Count == 0 && _projectiles.All(p => p.ImpactTime is not null) && Time >= CastStartTime + Visual.Plan.EndTime;

    /// <summary>Advances everything to <paramref name="now"/> (seconds, caller's clock; call once per frame).</summary>
    public void AdvanceTo(double now)
    {
        while (_steps.Count > 0 && CastStartTime + _steps.Peek().StartTime <= now)
        {
            var step = _steps.Dequeue();
            var at = CastStartTime + step.StartTime;
            Caster.AdvanceTo(at);
            Target?.AdvanceTo(at);
            AdvanceProjectiles(at);
            Execute(step, at);
        }

        Caster.AdvanceTo(now);
        Target?.AdvanceTo(now);
        AdvanceProjectiles(now);
        Time = now;
    }

    private void Execute(CastStep step, double at)
    {
        if (!_spoke)
        {
            // Player.DoSpellWords: the chant is broadcast before the first gesture.
            _spoke = true;
            Log(at, $"{Visual.Caster.Name} says \"{Visual.Plan.Words}\"");
        }

        if (step.Kind != CastStepKind.SpellEffects)
        {
            ((MotionPlayer)Caster.Motion!).ApplyInterpretedMotion(step.Style, step.Motion, step.Speed);
            Log(at, $"motion {MotionCommands.Name(step.Motion)} ×{step.Speed}");
            return;
        }

        // WorldObject.DoSpellEffects at cast time (projectileHit = false): the caster effect always, the target effect
        // only for non-projectiles (`!spell.IsProjectile || projectileHit`). Projectile spells launch their projectiles;
        // their target effect waits for a hit, and never plays when the extract lacks the projectile.
        var intensity = Visual.Plan.EffectIntensity;
        if (Visual.CasterEffect is { } ce)
        {
            Log(at, $"caster {ce.Type}@{intensity} → script 0x{Caster.Effects.PlayScript(ce.Type, intensity):X8}");
        }

        if (Visual.Projectile is { } projectile)
        {
            Launch(projectile, at);
        }
        else if (Visual.IsProjectileSpell)
        {
            Log(at, $"no projectile: spell {Visual.Spell.Id} is not in {AceWorldExtract.FileName}; its target effect needs a hit");
        }

        if (!Visual.IsProjectileSpell && Visual.TargetEffect is { } te)
        {
            var receiver = Target ?? Caster;
            Log(at, $"target {te.Type}@{intensity} → script 0x{receiver.Effects.PlayScript(te.Type, intensity):X8}");
        }
    }

    private EffectActor CreateActor(VisualObject obj, ObjectDimensions size, AcFrame frame, int seed, double now)
    {
        var table = _resolver.Animations.GetMotionTable(obj.MotionTableId)
            ?? throw new KeyNotFoundException($"MotionTable 0x{obj.MotionTableId:X8} of '{obj.Name}' not found.");
        var fx = new EffectObject(_resolver.Effects, new RetailRandom(seed), obj.PhysicsScriptTableId);
        return new EffectActor(fx, new MotionPlayer(table, _resolver.Animations), size.PartCount, frame, obj.Scale, now);
    }

    /// <summary>ACE <c>CreateSpellProjectiles</c> → <c>LaunchSpellProjectiles</c> for a stationary target.</summary>
    private void Launch(ProjectileVisual visual, double at)
    {
        var entry = visual.Source;
        var dats = _resolver.Animations.Dats;
        var scale = entry.DefaultScale ?? 1f;
        var projSize = ObjectDimensions.FromSetup(dats, entry.SetupId, scale);
        var radius = dats.TryGetPortal<DatSetup>(entry.SetupId, out var setup) && setup.Spheres.Count > 0 ? setup.Spheres[0].Radius * scale : 0f;
        var target = Target ?? Caster;
        var targetSize = Target is null ? _casterSize : _targetSize;
        var origins = CalculateProjectileOrigins(visual, radius, target);
        var aimPoint = target.Frame.Origin + new Vector3(0, 0, targetSize.Height * (visual.Kind == ProjectileKind.Arc ? ProjHeightArc : ProjHeight));
        var velocity = CalculateProjectileVelocity(visual, origins[0], aimPoint);
        var toTarget = target.Frame.Origin - Caster.Frame.Origin;
        var rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.Atan2(-toTarget.X, toTarget.Y));

        for (var i = 0; i < origins.Count; i++)
        {
            var origin = origins[i];
            var v = velocity;
            if ((entry.SpreadAngle ?? 0f) > 0f)
            {
                var n = Vector3.Normalize(origin);
                v = Vector3.Transform(velocity, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.Atan2(-n.X, n.Y)));
            }

            var position = Caster.Frame.Origin + Vector3.Transform(origin, rotate);
            var frame = EffectActor.SetVectorHeading(new AcFrame(position, Quaternion.Identity), v);
            var fx = new EffectObject(_resolver.Effects, new RetailRandom(_seed + 100 + i), entry.PhysicsEffectTableId ?? 0)
            {
                // SpellProjectile.Setup: bolts/streaks/arcs/volleys/blasts (and four special wcids) collide with
                // PlayScript.ProjectileCollision at intensity 1; ring 3818 uses Explode; other rings have no collision script.
                DefaultScript = DefaultCollisionScript(visual),
                DefaultScriptIntensity = 1f,
            };
            IPoseDriver? pose = visual.SetupDefaultAnimationId != 0 ? new DefaultAnimationPlayer(_resolver.Animations, visual.SetupDefaultAnimationId) : null;
            var actor = new EffectActor(fx, pose, projSize.PartCount, frame, scale, at)
            {
                Velocity = v,
                Acceleration = visual.UsesGravity ? new Vector3(0, 0, Gravity) : Vector3.Zero,
                OnGround = false,
                AlignPath = (entry.RotationSpeed ?? 0f) == 0f,
                Omega = (entry.RotationSpeed ?? 0f) != 0f ? new Vector3(MathF.PI * 2 * entry.RotationSpeed!.Value, 0, 0) : Vector3.Zero,
            };

            // CPhysicsObj::InitDefaults plays the Setup's default script at creation; then the server's Launch PlayScript.
            if (visual.SetupDefaultScriptId != 0)
            {
                fx.PlayScriptId(visual.SetupDefaultScriptId);
            }

            var launch = fx.PlayScript(PlayScript.Launch, visual.ScriptIntensity);
            _projectiles.Add(new SimulatedProjectile(actor, aimPoint, radius, at));
            Log(at, $"projectile {i} ({visual.Kind}) launched at {position} v={v} launch script 0x{launch:X8}");
        }
    }

    private static PlayScript DefaultCollisionScript(ProjectileVisual visual)
    {
        var wcid = visual.Source.Wcid;
        if (visual.Kind is ProjectileKind.Bolt or ProjectileKind.Streak or ProjectileKind.Arc or ProjectileKind.Volley or ProjectileKind.Blast
            || wcid is 7276 or 7277 or 7279 or 7280)
        {
            return PlayScript.ProjectileCollision;
        }

        return visual.Kind == ProjectileKind.Ring && visual.Source.SpellId == 3818 ? PlayScript.Explode : PlayScript.Invalid;
    }

    private void AdvanceProjectiles(double now)
    {
        foreach (var p in _projectiles)
        {
            var before = p.Actor.Frame.Origin;
            p.Actor.AdvanceTo(now);
            if (p.ImpactTime is not null)
            {
                continue;
            }

            var after = p.Actor.Frame.Origin;
            var reach = p.Radius + (Target is null ? _casterSize.PhysicsRadius : _targetSize.PhysicsRadius);
            var passed = Vector3.Dot(p.AimPoint - after, p.AimPoint - before) < 0f;
            if (Vector3.Distance(after, p.AimPoint) <= reach || passed)
            {
                Impact(p, now, hitTarget: true);
            }
            else if (now - p.LaunchTime > MaxFlightSeconds)
            {
                Impact(p, now, hitTarget: false);
            }
        }
    }

    /// <summary>
    /// Client collision (<c>ACCWeenieObject::DoCollision</c>: ScriptedCollision objects play their default script), then
    /// ACE <c>ProjectileImpact</c>: NoDraw, stop, PlayScript.Explode at the projectile intensity. Only a projectile that
    /// reached the target runs the <c>DoSpellEffects(projectileHit: true)</c> of ACE <c>OnCollideObject</c> (the target
    /// effect); one that flew <see cref="MaxFlightSeconds"/> without reaching it ends like ACE <c>OnCollideEnvironment</c>
    /// (impact only).
    /// </summary>
    private void Impact(SimulatedProjectile p, double now, bool hitTarget)
    {
        p.ImpactTime = now;
        p.Hidden = true;
        p.Actor.Velocity = Vector3.Zero;
        p.Actor.Acceleration = Vector3.Zero;
        var fx = p.Actor.Effects;
        var collision = fx.DefaultScript != PlayScript.Invalid ? fx.PlayDefaultScript() : 0;
        var explode = fx.PlayScript(PlayScript.Explode, Visual.Projectile!.ScriptIntensity);
        var outcome = hitTarget ? "hit" : "timed out";
        Log(now, $"projectile {outcome}: collision script 0x{collision:X8}, explode script 0x{explode:X8}");
        if (hitTarget && Visual.TargetEffect is { } te)
        {
            var receiver = Target ?? Caster;
            Log(now, $"target {te.Type} → script 0x{receiver.Effects.PlayScript(te.Type, Visual.Plan.EffectIntensity):X8}");
        }
    }

    /// <summary>ACE <c>CalculateProjectileOrigins</c> (with <c>CalculatePreOffset</c>), in caster-local space facing the target.</summary>
    private List<Vector3> CalculateProjectileOrigins(ProjectileVisual visual, float radius, EffectActor target)
    {
        var entry = visual.Source;
        var count = Math.Max(1, entry.NumProjectiles ?? 1);
        var spread = entry.SpreadAngle ?? 0f;
        var padding = entry.Padding ?? Vector3.Zero;
        var vRadius = Vector3.One * radius;
        var baseOffset = entry.CreateOffset ?? Vector3.Zero;
        var radsum = _casterSize.PhysicsRadius * 2f + radius * 2f;
        var heightOffset = CalculatePreOffset(visual.Kind, radius, target);
        if (Target is not null)
        {
            var cylDist = CylinderDistance(Caster.Frame.Origin, _casterSize, target.Frame.Origin, _targetSize);
            if (cylDist < 0.6f)
            {
                radsum = _casterSize.PhysicsRadius + radius;
            }
        }

        if (spread == 360f)
        {
            radsum *= 0.6f;
        }

        baseOffset.Y += radsum;
        baseOffset += heightOffset;
        var anglePerStep = spread == 0f || count == 1 ? 0f : spread / (count % 2 == 1 ? count - 1 : count);
        var dims = entry.Dims ?? new Vector3(count, 1, 1);
        var peturbation = entry.Peturbation ?? Vector3.Zero;
        var origins = new List<Vector3>();
        var i = 0;
        for (var z = 0; z < dims.Z && i < count; z++)
        {
            for (var y = 0; y < dims.Y && i < count; y++)
            {
                var oddRow = (int)Math.Min(dims.X, count - i) % 2 == 1;
                for (var x = 0; x < dims.X && i < count; x++)
                {
                    var cur = baseOffset;
                    if (peturbation != Vector3.Zero)
                    {
                        var rng = new Vector3((float)_serverRandom.RollDice(-1f, 1f), (float)_serverRandom.RollDice(-1f, 1f), (float)_serverRandom.RollDice(-1f, 1f));
                        cur += rng * peturbation * padding;
                    }

                    if (!oddRow && spread == 0f)
                    {
                        cur.X += padding.X * 0.5f + radius;
                    }

                    var xFactor = spread == 0f ? (oddRow ? MathF.Ceiling(x * 0.5f) : MathF.Floor(x * 0.5f)) : 0f;
                    var origin = cur + (vRadius * 2f + padding) * new Vector3(xFactor, y, z);
                    if (spread == 0f)
                    {
                        if (x % 2 == (oddRow ? 1 : 0))
                        {
                            origin.X *= -1f;
                        }
                    }
                    else
                    {
                        var steps = (x + 1) / 2;
                        if (x % 2 == 0)
                        {
                            steps *= -1;
                        }

                        origin = Vector3.Transform(origin, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, anglePerStep * steps * MathF.PI / 180f));
                    }

                    origins.Add(origin);
                    i++;
                }
            }
        }

        return origins;
    }

    /// <summary>ACE <c>CalculatePreOffset</c>.</summary>
    private Vector3 CalculatePreOffset(ProjectileKind kind, float radius, EffectActor target)
    {
        var startFactor = kind == ProjectileKind.Arc ? 1.0f : ProjHeight;
        var preOffset = new Vector3(0, 0, _casterSize.Height * startFactor);
        if (Target is null)
        {
            return preOffset;
        }

        var startPos = Caster.Frame.Origin + new Vector3(0, 0, _casterSize.Height * startFactor);
        var endFactor = kind == ProjectileKind.Arc ? ProjHeightArc : ProjHeight;
        var endPos = target.Frame.Origin + new Vector3(0, 0, _targetSize.Height * endFactor);
        var globOffset = endPos - startPos;
        var rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.Atan2(globOffset.X, globOffset.Y));
        var localDir = Vector3.Normalize(Vector3.Transform(globOffset, rotate));
        var radsum = _casterSize.PhysicsRadius + radius;
        return preOffset + (localDir * radsum - Vector3.UnitY * radsum);
    }

    /// <summary>ACE <c>CalculateProjectileVelocity</c> for a stationary target.</summary>
    private Vector3 CalculateProjectileVelocity(ProjectileVisual visual, Vector3 origin, Vector3 aimPoint)
    {
        var speed = visual.Speed;
        var target = Target ?? Caster;
        var toTarget = target.Frame.Origin - Caster.Frame.Origin;
        var rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.Atan2(-toTarget.X, toTarget.Y));
        var startPos = Caster.Frame.Origin + Vector3.Transform(origin, rotate);
        if (visual.UsesGravity)
        {
            // Trajectory.solve_ballistic_arc_lateral with target_velocity = 0: t = |Δxy| / speed.
            var diffXy = new Vector3(aimPoint.X - startPos.X, aimPoint.Y - startPos.Y, 0f);
            var lateral = diffXy.Length();
            if (lateral > 0f && speed > 0f)
            {
                var t = lateral / speed;
                var v = Vector3.Normalize(diffXy) * speed;
                v.Z = -(2 * startPos.Z - 2 * aimPoint.Z + Gravity * t * t) / (t * 2);
                return v;
            }
        }

        var dir = aimPoint - startPos;
        return dir.LengthSquared() > 0f ? Vector3.Normalize(dir) * speed : Vector3.UnitY * speed;
    }

    /// <summary>ACE <c>Position.CylinderDistance</c>.</summary>
    private static float CylinderDistance(Vector3 pos, ObjectDimensions size, Vector3 other, ObjectDimensions otherSize)
    {
        var reach = Vector3.Distance(pos, other) - (size.Radius + otherSize.Radius);
        var diffZ = pos.Z <= other.Z ? other.Z - (pos.Z + size.Height) : pos.Z - (other.Z + otherSize.Height);
        if (diffZ > 0 && reach > 0)
        {
            return MathF.Sqrt(diffZ * diffZ + reach * reach);
        }

        if (diffZ < 0 && reach < 0)
        {
            return -MathF.Sqrt(diffZ * diffZ + reach * reach);
        }

        return reach;
    }

    private void Log(double time, string text) => _log.Add(new SimulationEvent(time, text));
}
