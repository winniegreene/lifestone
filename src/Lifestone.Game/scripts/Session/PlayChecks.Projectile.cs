using Godot;
using Lifestone.Client.Avatars;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Tools;
using Lifestone.Data.World;
using Lifestone.Game.Core;
using Lifestone.Game.Diagnostics;
using Lifestone.Game.World.ServerObjects;

namespace Lifestone.Game.Sessions;

// M3 3.1 lane fc (the projectile on screen) fills this file and no other of PlayChecks. The hook is called once from CheckAllAsync, after the tools teleport and the panel checks (PlayChecks.cs): a long phase
// before the tools teleport makes the stand-in's fixed teleport time miss its tunnel (the 2.2 lesson), so nothing of the projectile checks runs earlier.
public sealed partial class PlayChecks
{
    /// <summary>Flame Bolt I: the war spell whose projectile the stand-in creates (the ACE world extract's Setup 0x0200040D; <c>StandInVisit.ProjectileSetup</c>).</summary>
    private const uint FlameBoltSpellId = 27;

    /// <summary>Shock Wave I: a war spell whose Setup (0x020003FA) has four parts, so the wait for its meshes is measurable (the Flame Bolt's one part draws nothing). Report-only.</summary>
    private const uint ShockWaveSpellId = 64;

    /// <summary>What one followed bolt showed.</summary>
    private sealed record BoltFollow(
        uint SetupId, long Created, long Shown, int Moves, int FramesAlive, int FirstShownAt, bool WindowSeen, bool WindowClosed, double Flown, int WaitFrames, long LayerShown, long WindowsOpened, long Launches, long Explodes, string Pipelines, string Outcome);

    /// <summary>
    /// M3 3.1 lane fc (item 4): a stand-in Flame Bolt cast, followed frame by frame from its CreateObject to its F747. Judged: <c>projectiles_created&gt;=1 projectiles_shown&gt;=1</c> and that the bolt moved (its position
    /// differs between two frames). Reported, not judged (the hit and the Launch/Explode scripts are fb's, and the stand-in sends the projectile no PhysicsScriptTable yet): the frames the bolt waited for its meshes
    /// (<c>wait_frames</c>, 0 when the warm-up had them ready), the metres it flew, <c>projectile_launches</c> and <c>projectile_explodes</c>, the window and the pipelines compiled while it lived. A Shock Wave cast after it
    /// (four parts) reports the wait of a bolt that has meshes to wait for.
    /// </summary>
    private async Task ProjectileChecksAsync()
    {
        const string name = "a-stand-in-flame-bolt-is-shown-and-flies";
        if (_host.Session is not { } session || _services.Dats is null || _mode.Avatars is not { } avatars || _mode.Layer is not { } layer)
        {
            Check(name, false, "no session, dats, avatar set or server-objects layer: the bolt cannot be followed");
            return;
        }

        // The stand-in knows the war spells' projectile Setups from the ACE world extract (the table the cast tracker's lookup reads too): set here as ActionChecksAsync does, which runs after this hook.
        Visit.SpellTable ??= Lifestone.Data.Magic.SpellTableData.Load(_services.Dats);
        Visit.ProjectileSetup ??= id => Lifestone.Data.Magic.AceWorldExtract.Load().ProjectilesBySpell.TryGetValue(id, out var projectile) ? projectile.SetupId : null;
        var learn = session.Tools.LearnSpells([FlameBoltSpellId, ShockWaveSpellId], ActionSource.Host);
        var learned = await WaitFor(() => learn.IsCompleted, 10.0);
        var caster = await HoldCasterAsync(session); // M4 3.1 g: the magic mode needs a caster in hand (the combat-mode guard); the wand goes back to the pack with the return to peace below
        var magic = session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Host);
        var inMagic = await WaitFor(() => magic.IsCompleted, 10.0) && session.State.Self.Qualities.CombatMode == CombatModes.Magic;

        var flame = await FollowBoltAsync(session, avatars, layer, FlameBoltSpellId);
        Check(
            name,
            learned && inMagic && flame.Created >= 1 && flame.Shown >= 1 && flame.Moves >= 1 && flame.WindowSeen && flame.WindowClosed,
            $"spell=Flame Bolt I learned={learned} mode_magic={inMagic} cast_outcome={flame.Outcome} setup=0x{flame.SetupId:X8} projectiles_created={flame.Created} projectiles_shown={flame.Shown} moved_frames={flame.Moves}/{flame.FramesAlive} "
            + $"flown_m={flame.Flown:0.0} first_shown_frame={flame.FirstShownAt} wait_frames={flame.WaitFrames} layer_shown={flame.LayerShown} window_opened={flame.WindowsOpened} window_seen={flame.WindowSeen} window_closed={flame.WindowClosed} "
            + $"projectile_launches={flame.Launches} projectile_explodes={flame.Explodes} warm={layer.ProjectileWarm is { Done: true }} pipelines_while_alive=[{flame.Pipelines}] "
            + "(judged: projectiles_created>=1 projectiles_shown>=1 moved; the rest is report-only, the hit is lane fb's)");

        // Report-only: a bolt whose parts have meshes, to show what the warm-up saves (the Flame Bolt waits for nothing).
        var shock = await FollowBoltAsync(session, avatars, layer, ShockWaveSpellId);
        Log.Info(
            "playcheck",
            "projectile with meshes (report-only)",
            ("spell", "Shock Wave I"),
            ("setup", $"0x{shock.SetupId:X8}"),
            ("created", shock.Created),
            ("shown", shock.Shown),
            ("wait_frames", shock.WaitFrames),
            ("first_shown_frame", shock.FirstShownAt),
            ("moved_frames", shock.Moves),
            ("flown_m", shock.Flown),
            ("warm", layer.ProjectileWarm is { Done: true }),
            ("pipelines_while_alive", shock.Pipelines));

        // Back to peace, so the checks after this one find the character as they did.
        var peace = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
        await WaitFor(() => peace.IsCompleted, 10.0);
        await StowAsync(session, caster);
    }

    /// <summary>Casts <paramref name="spellId"/> and follows its projectile frame by frame from the CreateObject to the F747 (20 s at most).</summary>
    private async Task<BoltFollow> FollowBoltAsync(IGameSessionView session, AvatarSet avatars, ServerObjectsLayer layer, uint spellId)
    {
        var stats = avatars.Stats;
        var createdBefore = stats.ProjectilesCreated;
        var shownBefore = stats.ProjectilesShown;
        var launchesBefore = stats.ProjectileLaunches;
        var explodesBefore = stats.ProjectileExplodes;
        var windowsBefore = stats.ProjectileWindows;
        var layerShownBefore = layer.ProjectilesShown;
        var pipelinesBefore = PipelineCompilations.Read();
        var cast = session.Actions.Cast(spellId, ActionSource.Host);

        uint guid = 0;
        uint setup = 0;
        CellFrame? first = null;
        CellFrame? previous = null;
        var moves = 0;
        var framesAlive = 0;
        var firstShownAt = -1;
        var windowSeen = false;
        var flown = 0.0;
        var deadline = Time.GetTicksMsec() + 20_000UL;
        while (Time.GetTicksMsec() < deadline)
        {
            await Frames(1);
            if (guid == 0)
            {
                if (stats.ProjectilesCreated <= createdBefore)
                {
                    continue;
                }

                guid = avatars.LastProjectileGuid;
            }

            if (!avatars.TryGet(guid, out var bolt))
            {
                break; // F747 removed it
            }

            windowSeen |= avatars.ProjectileWindowOpen;
            setup = bolt.Created.SetupId;
            var here = bolt.Body.Position;
            first ??= here;
            if (previous is { } before && PositionAgreement.DistanceMetres(before, here) > 0.001)
            {
                moves++;
            }

            previous = here;
            flown = PositionAgreement.DistanceMetres(first.Value, here);
            if (firstShownAt < 0 && layer.IsDrawn(guid))
            {
                firstShownAt = framesAlive;
            }

            framesAlive++;
        }

        var castDone = await WaitFor(() => cast.IsCompleted, 10.0);
        var windowClosed = await WaitFor(() => !avatars.ProjectileWindowOpen, 5.0);
        return new BoltFollow(
            setup,
            stats.ProjectilesCreated - createdBefore,
            stats.ProjectilesShown - shownBefore,
            moves,
            framesAlive,
            firstShownAt,
            windowSeen,
            windowClosed,
            flown,
            layer.LastProjectileWaitFrames,
            layer.ProjectilesShown - layerShownBefore,
            stats.ProjectileWindows - windowsBefore,
            stats.ProjectileLaunches - launchesBefore,
            stats.ProjectileExplodes - explodesBefore,
            PipelineCompilations.Read().Since(pipelinesBefore).Describe(),
            castDone ? cast.Outcome?.Kind.ToString() ?? "none" : "pending");
    }
}
