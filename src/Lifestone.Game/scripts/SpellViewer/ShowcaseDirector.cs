using Godot;
using Lifestone.Client.Effects;
using Lifestone.Data.Magic;
using Lifestone.Game.Core;
using AcVector3 = System.Numerics.Vector3;

namespace Lifestone.Game.SpellViewer;

/// <summary>
/// <c>--showcase</c>: casts every showcase spell in turn (docs/spell-visuals.md §5 plus a fizzle) with a scripted,
/// smoothed camera, then quits. Time advances by exactly 1/30 s per frame, so a recording made with Godot's Movie Maker
/// (<c>--write-movie frames.png --fixed-fps 30</c>) is smooth and deterministic.
/// </summary>
/// <remarks>
/// <para>Shots (the caster stands at the west end facing east, the drudge at the east end facing west; the town is to
/// the north, a lamp post 3.7 m south-east of the drudge): war bolts start close on the caster's hands for the wind-up,
/// then follow the projectile from the south and settle on the drudge for the explosion; the ring is seen from above;
/// self spells and recalls from the caster's front; spells on the drudge start on the caster and swing to the drudge
/// when the effect lands.</para>
/// <para>A segment ends a fixed time after the effect (or the first impact) so the whole showcase stays under a minute;
/// the log line "showcase segment" gives each segment's first frame for editing.</para>
/// </remarks>
public sealed class ShowcaseDirector
{
    /// <summary>Camera smoothing time constant (seconds).</summary>
    public const double SmoothSeconds = 0.28;

    private readonly SpellViewer _viewer;
    private readonly IReadOnlyList<ShowcaseEntry> _entries;
    private int _index = -1;
    private double _elapsed;
    private int _frame;
    private bool _started;
    private Shot _current;

    private enum Kind
    {
        Bolt,
        Ring,
        Self,
        Other,
        Fizzle,
    }

    /// <summary>A camera set-up: compass heading looked along, pitch, distance, focus point (AC stage space).</summary>
    private readonly record struct Shot(float Yaw, float Pitch, float Distance, AcVector3 Focus);

    public ShowcaseDirector(SpellViewer viewer, SpellViewerOptions options)
    {
        _viewer = viewer;
        var all = SpellStage.Showcase;
        _entries = options.ShowcaseOnly is { } only ? only.Where(i => i >= 0 && i < all.Count).Select(i => all[i]).ToArray() : all;
    }

    public bool Finished { get; private set; }

    public int ExitCode { get; private set; }

    /// <summary>Called once per frame before the stage advances by <paramref name="step"/>.</summary>
    public void Process(double step)
    {
        _frame++;
        if (Finished)
        {
            return;
        }

        if (!_started)
        {
            // Wait for Holtburg's statics (so the recording starts with the town in place) and the particle meshes.
            if (!_viewer.StaticsReady || !_viewer.ParticlesReady || _frame < 3)
            {
                return;
            }

            _started = true;
            Log.Info("spellviewer", "showcase starts", ("frame", _frame), ("entries", _entries.Count));
            Next();
            return;
        }

        _elapsed += step;
        var entry = _entries[_index];
        if (_elapsed >= Duration(entry))
        {
            Next();
            return;
        }

        // Smoothly approach the desired shot.
        var want = Desired(entry);
        var k = (float)(1.0 - Math.Exp(-step / SmoothSeconds));
        _current = new Shot(
            _current.Yaw + (AngleDelta(_current.Yaw, want.Yaw) * k),
            _current.Pitch + ((want.Pitch - _current.Pitch) * k),
            _current.Distance + ((want.Distance - _current.Distance) * k),
            _current.Focus + ((want.Focus - _current.Focus) * k));
        Apply(_current);
    }

    private void Next()
    {
        _index++;
        if (_index >= _entries.Count)
        {
            Finished = true;
            ExitCode = _viewer.Exceptions == 0 ? 0 : 1;
            Log.Info("spellviewer", "showcase done", ("frame", _frame), ("exceptions", _viewer.Exceptions));
            return;
        }

        var entry = _entries[_index];
        _elapsed = 0;
        _viewer.Cast(entry.SpellId, entry.Fizzle);
        _current = Desired(entry); // cut to the first shot of the segment
        Apply(_current);
        Log.Info("spellviewer", "showcase segment", ("index", _index), ("label", entry.Label), ("frame", _frame), ("seconds", Duration(entry)));
    }

    private Kind KindOf(ShowcaseEntry entry)
    {
        var visual = _viewer.Stage.Visual!;
        if (entry.Fizzle)
        {
            return Kind.Fizzle;
        }

        if (visual.Projectile is { Kind: ProjectileKind.Ring })
        {
            return Kind.Ring;
        }

        if (visual.Projectile is not null)
        {
            return Kind.Bolt;
        }

        return _viewer.Stage.Simulation?.Target is null || visual.TargetEffect is null ? Kind.Self : Kind.Other;
    }

    /// <summary>Seconds since the cast at which the spell takes effect.</summary>
    private double EffectTime()
    {
        var stage = _viewer.Stage;
        var sim = stage.Simulation!;
        return sim.CastStartTime - stage.CastTime + sim.Visual.Plan.EffectTime;
    }

    /// <summary>Segment length: a fixed time after the effect or the first impact (computed from the plan, deterministic).</summary>
    private double Duration(ShowcaseEntry entry)
    {
        var effect = EffectTime();
        return KindOf(entry) switch
        {
            Kind.Bolt => Math.Max(2.6, FirstImpact() is { } hit ? hit + 1.15 : effect + 2.2),
            Kind.Ring => 6.2,
            Kind.Other => effect + 1.8,
            Kind.Fizzle => effect + 1.4,
            _ => effect + (entry.SpellId is 2645 or 1635 ? 2.0 : entry.SpellId == 24 ? 1.9 : 1.6),
        };
    }

    /// <summary>Seconds since the cast of the first projectile impact, once it happened.</summary>
    private double? FirstImpact()
    {
        var stage = _viewer.Stage;
        return stage.Simulation?.Projectiles.Select(p => p.ImpactTime).FirstOrDefault(t => t is not null) is { } t ? t - stage.CastTime : null;
    }

    private Shot Desired(ShowcaseEntry entry)
    {
        var stage = _viewer.Stage;
        var caster = stage.CasterFrame.Origin;
        var target = stage.TargetFrame.Origin;
        var east = AcVector3.Normalize(target - caster);
        var hands = caster + (east * 0.55f) + new AcVector3(0, 0, 1.15f);

        // Shots on the drudge aim 1.5 m short of it (towards the caster): the camera then stands west of the lamp post
        // (Setup 0x020005D8, 3.7 m south-east of the drudge) and keeps it out of frame (StageSite remarks).
        var nearTarget = target - (east * 1.5f);
        var effect = EffectTime();
        var t = _elapsed;
        switch (KindOf(entry))
        {
            case Kind.Bolt:
                if (t < effect - 0.05)
                {
                    return new Shot(332f, -4f, 3.9f, hands);
                }

                if (FirstImpact() is { } hit && t > hit + 0.1)
                {
                    return new Shot(10f, -4f, 5.6f, nearTarget + new AcVector3(0, 0, 1.0f));
                }

                // The follow cam stops 2.5 m short of the drudge so the lamp post south of it stays out of frame.
                var p = stage.Simulation!.Projectiles.Count > 0 ? stage.Simulation.Projectiles[0].Actor.Frame.Origin : hands;
                var along = Math.Clamp(AcVector3.Dot(p - caster, east), 1.0f, AcVector3.Distance(caster, target) - 2.5f);
                return new Shot(358f, -5f, 6.4f, caster + (east * along) + new AcVector3(0, 0, 1.1f));

            case Kind.Ring:
                var grow = (float)Math.Clamp((t - effect) / 3.0, 0.0, 1.0);
                return new Shot(20f + (float)(t * 5.0), -36f, 8.5f + (grow * 6f), caster + (east * (grow * 2.5f)) + new AcVector3(0, 0, 0.3f));

            case Kind.Other:
                return t < effect - 0.1
                    ? new Shot(332f, -4f, 3.9f, hands)
                    : new Shot(20f, -4f, 5.4f, nearTarget + new AcVector3(0, 0, 1.05f));

            case Kind.Fizzle:
                return new Shot(332f, -4f, 3.9f, hands);

            default:
                // Self spells and recalls: from the caster's front-right, the town behind him; slow orbit.
                return new Shot(296f + (float)(t * 6.0), -4f, 4.3f, caster + new AcVector3(0, 0, 1.0f));
        }
    }

    private void Apply(Shot shot) => _viewer.SetCamera(Mathf.PosMod(shot.Yaw, 360f), shot.Pitch, shot.Distance, shot.Focus);

    private static float AngleDelta(float from, float to)
    {
        var d = Mathf.PosMod(to - from, 360f);
        return d > 180f ? d - 360f : d;
    }
}
