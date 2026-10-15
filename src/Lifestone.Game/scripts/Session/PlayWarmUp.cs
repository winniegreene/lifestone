using System.Diagnostics;
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

    private static CellFrame At(in CellFrame from, System.Numerics.Vector3 offset) => new(from.CellId, new AcFrame(from.Origin + offset, from.Orientation));

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
            var set = new AvatarSet(sources);
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
            };
            if (set.Create(npc, now) == AvatarApply.Applied)
            {
                var forward = WireMovement.Interpreted(
                    MotionCommandTable.ToWire(MotionCommands.NonCombat),
                    new WireInterpretedMotion(null, MotionCommandTable.ToWire(MotionCommands.WalkForward), null, null, null, null, null));
                set.SetState(npc.Guid, npc.PhysicsState & ~AvatarStateBits.Hidden);
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
