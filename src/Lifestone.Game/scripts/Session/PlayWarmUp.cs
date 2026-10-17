using System.Diagnostics;
using Lifestone.Client.Audio;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.CharacterCreation;
using Lifestone.Data.Appearance;
using Lifestone.Data.Motion;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.World;
using Lifestone.Data.World;
using Lifestone.Game.Core;

namespace Lifestone.Game.Sessions;

/// <summary>
/// Pays play mode's first-use costs at start-up, so the frames between Enter and walking do not (DoD 15, no frame over 50 ms): a throw-away human is made in a
/// scratch avatar set (the Setup and its parts' LOD chains read from the dats and cached in the shared <see cref="AvatarSources"/>, the motion table, the object
/// clock stepped, the code jitted) and given a physics body on the human Setup (<see cref="PhysicsGeometryLibrary"/> shared with the player's rig, so the human's
/// collision shape is decoded here and kept). Nothing of it is drawn or kept but those caches.
/// </summary>
/// <remarks>
/// <para>What it cannot warm: a character whose Setup is not the male human's (a female or another heritage's) still reads its own parts from the dats the first
/// time. That is a smaller cost than the code's first run, which this does pay. A failure is logged loudly and never fatal (the arrival then pays the cost).</para>
/// </remarks>
public static class PlayWarmUp
{
    private const string LogCategory = "play";
    private const uint ScratchGuid = 0x7FFF0001;

    /// <summary>The human sound table, so the scratch bodies' hooks reach the router.</summary>
    private const uint WarmSoundTable = 0x20000001;

    /// <summary>A sound sink that plays nothing: the warm-up only pays the router's code.</summary>
    private sealed class WarmSoundSink : ISoundSink
    {
        public void PlayFromCenter(uint soundTableId, uint soundType)
        {
        }

        public void PlayWave(uint waveId, float priority, float probability, float volume, SoundOrigin origin)
        {
        }

        public void PlayFromTable(uint tableId, uint soundType, SoundOrigin origin, float volume)
        {
        }
    }

    private static CellFrame At(in CellFrame from, System.Numerics.Vector3 offset) => new(from.CellId, new AcFrame(from.Origin + offset, from.Orientation));

    /// <summary>
    /// A scratch Missile per distinct war-spell projectile Setup (the ACE world extract's table, the one <c>StandInVisit.ProjectileSetup</c> reads), in the order ACE sends a bolt's messages: the create with its
    /// velocity and PhysicsScriptTable, the Launch script, a few steps of flight, the hit's state change and zero vector, the Explode script, the delete. The Setups, parts, scripts and emitters are read from the dats and
    /// cached in the shared <see cref="AvatarSources"/>, and the avatar code of a bolt is jitted. Returns the Setups warmed.
    /// </summary>
    private static int WarmProjectiles(AvatarSet set, in CellFrame spawn, ref double now)
    {
        const uint hitState = AvatarStateBits.Missile | AvatarStateBits.IgnoreCollisions | AvatarStateBits.NoDraw;
        var warmed = 0;
        var guid = ScratchGuid + 16;
        foreach (var projectile in Lifestone.Data.Magic.AceWorldExtract.Load().Projectiles.DistinctBy(p => p.SetupId))
        {
            var bolt = new AvatarCreate
            {
                Guid = guid,
                SetupId = projectile.SetupId,
                PhysicsState = AvatarStateBits.Missile | AvatarStateBits.AlignPath | AvatarStateBits.ReportCollisions,
                Velocity = new System.Numerics.Vector3(0f, 15f, 0f),
                PhysicsScriptTableId = projectile.PhysicsEffectTableId ?? 0,
                Position = At(spawn, new System.Numerics.Vector3(0f, 2f, 1f)),
            };
            if (set.Create(bolt, now) != AvatarApply.Applied)
            {
                continue;
            }

            set.PlayScript(guid, (uint)DatReaderWriter.Enums.PlayScript.Launch, 1f);
            for (var i = 0; i < 3; i++)
            {
                now += 1.0 / 30.0;
                set.Advance(now, new AvatarViewer(spawn), long.MaxValue);
            }

            set.SetState(guid, hitState);
            set.SetVectors(guid, System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero);
            set.PlayScript(guid, (uint)DatReaderWriter.Enums.PlayScript.Explode, 1f);
            now += 1.0 / 30.0;
            set.Advance(now, new AvatarViewer(spawn), long.MaxValue);
            set.Delete(guid);
            warmed++;
            guid++;
        }

        return warmed;
    }

    /// <summary>Runs the warm-up on the main thread; returns the milliseconds it took.</summary>
    public static double Run(GameServices services, AvatarSources sources, PhysicsGeometryLibrary geometry)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(geometry);
        var timer = Stopwatch.StartNew();
        try
        {
            var dats = services.Dats ?? throw new InvalidOperationException("the warm-up needs the dats");
            var set = new AvatarSet(sources) { EffectEvents = new SoundHookRouter(new WarmSoundSink()) }; // M3 1.3 s1: the sound router's and the origin's code is JIT-compiled here too
            var spawn = new CellFrame(0xA9B40019, new AcFrame(new System.Numerics.Vector3(84f, 7.1f, 94f), System.Numerics.Quaternion.Identity));
            var create = new AvatarCreate
            {
                Guid = ScratchGuid,
                IsSelf = true,
                IsCreature = true,
                SetupId = OfflineWalker.HumanSetupId,
                MotionTableId = OfflineWalker.HumanMotionTableId,
                PhysicsState = AvatarStateBits.Gravity | AvatarStateBits.ReportCollisions,
                Position = spawn,
                SoundTableId = WarmSoundTable,
            };
            var now = 0.0;

            // The player the way the server describes one: a clothed, coloured body (CharGen's composition of the stand-in look, the same ObjDesc code paths).
            ObjDesc? desc = null;
            try
            {
                var data = Cg3DViewData.FromDats(dats, services.Textures, services.GfxObjMeshes);
                var composition = CharGenAppearance.Compose(data.Record, OfflinePlayerLook.Create(data.Record, data.Dats), data.Dats);
                create = OfflinePlayerLook.Describe(composition, spawn) with { Guid = ScratchGuid, PhysicsState = create.PhysicsState };
                desc = create.ObjDesc;
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException)
            {
                Log.Warn(LogCategory, "the warm-up could not compose a clothed human; it warms the plain body", ("error", ex.Message));
            }

            if (set.Create(create, now) != AvatarApply.Applied || set.Player is not { Parts: { } parts })
            {
                Log.Warn(LogCategory, "the warm-up could not make its scratch human; the arrival pays the first-use costs");
                return timer.Elapsed.TotalMilliseconds;
            }

            var physics = new PhysicsWorld();
            var walker = OfflineWalker.Create(dats, physics, NullLogSink.Instance, geometry: geometry, parts: parts, id: ScratchGuid, sink: new NullMovementSink());
            set.PlayerBody = walker.Body;
            for (var i = 0; i < 4; i++)
            {
                now += 1.0 / 30.0;
                set.Advance(now, new AvatarViewer(spawn), long.MaxValue);
            }

            // A creature the server places: created hidden, unhidden (the Hidden and UnHide scripts and their particles), walking, moved, redressed, deleted.
            var npc = create with
            {
                Guid = ScratchGuid + 1,
                IsSelf = false,
                IsCreature = true,
                PhysicsState = create.PhysicsState | AvatarStateBits.Hidden,
                Position = At(spawn, new System.Numerics.Vector3(3f, 0f, 0f)),
                SoundTableId = WarmSoundTable,
            };
            if (set.Create(npc, now) == AvatarApply.Applied)
            {
                var forward = WireMovement.Interpreted(
                    MotionCommandTable.ToWire(MotionCommands.NonCombat),
                    new WireInterpretedMotion(null, MotionCommandTable.ToWire(MotionCommands.WalkForward), null, null, null, null, null));
                set.SetState(npc.Guid, npc.PhysicsState & ~AvatarStateBits.Hidden);

                // M3 1.3 s0: what the server's F755 and F750 will use (the script's decode, its emitters' infos and the hook code), paid here and not on the frame the message lands in.
                set.PlayScript(npc.Guid, (uint)DatReaderWriter.Enums.PlayScript.LevelUp, 1f);
                set.PlaySound(npc.Guid, (uint)DatReaderWriter.Enums.Sound.EnchantUp, 0.75f);
                set.SetMovement(npc.Guid, isSelf: false, autonomous: false, forward, []);
                for (var i = 0; i < 6; i++)
                {
                    now += 1.0 / 30.0;
                    set.Advance(now, new AvatarViewer(spawn), long.MaxValue);
                }

                set.Move(npc.Guid, At(spawn, new System.Numerics.Vector3(4f, 1f, 0f)), teleportAdvanced: false, hasContact: true, placement: null, now);
                if (desc is not null)
                {
                    set.SetVisualDesc(npc.Guid, desc);
                }

                set.Advance(now + 0.1, new AvatarViewer(spawn), long.MaxValue);
                set.Delete(npc.Guid);
            }

            // M3 3.1 lane fc: the war spells' bolts, so the CreateObject of the first one reads no Setup, script table or emitter from the dats and runs no first-use code on the play path.
            var projectileTimer = Stopwatch.StartNew();
            var projectiles = WarmProjectiles(set, spawn, ref now);
            Log.Info(LogCategory, "projectile paths warmed (a scratch bolt per war-spell Setup: create, launch, flight, hit, explode, delete)", ("setups", projectiles), ("ms", projectileTimer.Elapsed.TotalMilliseconds));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Never fatal (this runs inside PlayMode.Install, called from Main._Ready): whatever the dat decoding or the motion set-up threw, the arrival pays the cost.
            Log.Exception(LogCategory, "the play-mode warm-up failed; the arrival pays the first-use costs", ex);
        }

        var ms = timer.Elapsed.TotalMilliseconds;
        Log.Info(LogCategory, "play mode warmed up (a scratch human, its physics shape and the avatar code paths)", ("ms", ms));
        return ms;
    }
}
