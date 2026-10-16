using System.Numerics;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using Lifestone.Client.Avatars;
using Lifestone.Data.Animation;
using Lifestone.Data.Appearance;
using Lifestone.Data.Effects;
using Lifestone.Data.Models;
using Lifestone.Data.Motion;
using Lifestone.Data.Textures;
using Lifestone.Data.World;
using DatAnimData = DatReaderWriter.Types.AnimData;
using DatFrame = DatReaderWriter.Types.Frame;
using DatMotionCommandData = DatReaderWriter.Types.MotionCommandData;
using DatMotionData = DatReaderWriter.Types.MotionData;
using DatMotionTable = DatReaderWriter.DBObjs.MotionTable;
using LandDefs = Lifestone.Data.World.LandDefs;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>
/// An in-memory world for the avatar tests (no dats): a 3-part "human" Setup with a right-hand holding location on part
/// 2 and a walking motion table whose cycles move by their pos frames (walk 3.12 m/s, run 4 m/s at 30 fps), a 1-part
/// "sword" Setup, a 2-part Setup with a default animation (neither has spheres), sources that load every GfxObj, and one
/// PhysicsScriptTable whose Hidden and UnHide entries are empty scripts.
/// </summary>
internal static class AvatarRig
{
    public const uint Human = 0x02FF0001;
    public const uint Sword = 0x02FF0002;
    public const uint Lamp = 0x02FF0003;
    public const uint Table = 0x09FF0001;

    public const uint ReadyAnim = 0x03FF0001;
    public const uint WalkAnim = 0x03FF0002;
    public const uint RunAnim = 0x03FF0003;
    public const uint FallAnim = 0x03FF0004;
    public const uint ReadyToWalk = 0x03FF0005;
    public const uint WalkToReady = 0x03FF0006;
    public const uint TurnAnim = 0x03FF0007;
    public const uint LampAnim = 0x03FF0008;

    /// <summary>The one PhysicsScriptTable: Hidden → <see cref="HiddenScript"/>, UnHide → <see cref="UnHideScript"/>.</summary>
    public const uint ScriptTable = 0x34FF0001;

    public const uint HiddenScript = 0x33FF0001;
    public const uint UnHideScript = 0x33FF0002;

    /// <summary>The script the table plays for <c>PlayScript.LevelUp</c> (M3 1.3 s0): one SoundTable hook (<see cref="LevelUpHookSoundType"/>) at time 0.</summary>
    public const uint LevelUpScript = 0x33FF0003;

    /// <summary>The sound type the <see cref="LevelUpScript"/>'s hook asks for.</summary>
    public const uint LevelUpHookSoundType = 90;

    /// <summary>Holtburg's landblock, outdoor cell 0x19.</summary>
    public const uint HoltburgCell = 0xA9B40019;

    /// <summary>The right-hand location of the human Setup: part 2, 0.1 m up its local Z.</summary>
    public static readonly Vector3 HandOffset = new(0f, 0f, 0.1f);

    /// <summary>The start, end and time of the Transparent hook the retail UnHide script 0x3300032F holds (AvatarHiddenTests pins them from the dats).</summary>
    public static readonly (float Start, float End, float Time) RetailUnHideFade = (1f, 0f, 0.75f);

    /// <summary>The Hidden script 0x33000331's Transparent hook: 1 to 1 over 0 s, an immediate translucency of 1.</summary>
    public static readonly (float Start, float End, float Time) RetailHiddenFade = (1f, 1f, 0f);

    /// <param name="materialise">
    /// True: the Hidden and UnHide scripts each hold the one Transparent hook of the retail scripts (at time 0, no particles),
    /// so the materialise fade runs; false (the default): they are empty.
    /// </param>
    public static AvatarSources Sources(bool materialise = false)
    {
        var animations = new RigAnimations()
            .Add(ReadyAnim, 2, parts: 3)
            .Add(WalkAnim, 4, parts: 3, posStepY: 3.12f / 30f, partStepX: 0.05f)
            .Add(RunAnim, 4, parts: 3, posStepY: 4f / 30f, partStepX: 0.05f)
            .Add(FallAnim, 2, parts: 3)
            .Add(ReadyToWalk, 3, parts: 3)
            .Add(WalkToReady, 3, parts: 3)
            .Add(TurnAnim, 4, parts: 3)
            .Add(LampAnim, 6, parts: 2, partStepX: 0.1f);
        var setups = new Dictionary<uint, ModelAssembly>
        {
            [Human] = HumanSetup(),
            [Sword] = SimpleSetup(Sword, 1, 0x01FF0010),
            [Lamp] = SimpleSetup(Lamp, 2, 0x01FF0020, defaultAnimation: LampAnim),
        };
        var table = MotionTable();
        return new AvatarSources(
            id => setups.GetValueOrDefault(id),
            new RigAppearance(),
            animations,
            id => id == Table ? table : null,
            new RigEffects(materialise));
    }

    /// <summary>A create for a human at <paramref name="origin"/> in Holtburg facing <paramref name="heading"/>.</summary>
    public static AvatarCreate HumanAt(uint guid, Vector3 origin, float heading = 0f, uint cell = HoltburgCell) => new()
    {
        Guid = guid,
        IsCreature = true,
        SetupId = Human,
        MotionTableId = Table,
        PhysicsState = AvatarStateBits.Gravity | AvatarStateBits.ReportCollisions,
        Position = At(origin, heading, cell),
    };

    public static CellFrame At(Vector3 origin, float heading = 0f, uint cell = HoltburgCell) =>
        new(cell, new AcFrame(origin, PositionFrames.WithHeading(Quaternion.Identity, heading)));

    /// <summary>A type-0 movement: NonCombat with the given forward command (and turn).</summary>
    public static WireMovement Moving(uint forward, float forwardSpeed = 1f, uint turn = 0) =>
        WireMovement.Interpreted(
            MotionCommandTable.ToWire(MotionCommands.NonCombat),
            new WireInterpretedMotion(
                null,
                forward == MotionCommands.Ready ? null : MotionCommandTable.ToWire(forward),
                null,
                turn == 0 ? null : MotionCommandTable.ToWire(turn),
                forwardSpeed == 1f ? null : forwardSpeed,
                null,
                null));

    private static ModelAssembly HumanSetup()
    {
        var setup = new Setup
        {
            Parts = [0x01FF0001, 0x01FF0002, 0x01FF0003],
            Height = 1.8f,
            Radius = 0.4f,
        };
        setup.Spheres.Add(new Sphere { Origin = new Vector3(0, 0, 0.4f), Radius = 0.4f });
        setup.PlacementFrames.Add(Placement.Resting, Frames(3));
        setup.HoldingLocations.Add(ParentLocation.RightHand, new LocationType
        {
            PartId = 2,
            Frame = new DatFrame { Origin = HandOffset, Orientation = Quaternion.Identity },
        });
        return ModelAssembly.FromSetup(Human, setup);
    }

    private static ModelAssembly SimpleSetup(uint id, int parts, uint firstGfxObj, uint defaultAnimation = 0)
    {
        var setup = new Setup { Height = 1f, Radius = 0.2f };
        for (var i = 0; i < parts; i++)
        {
            setup.Parts.Add(firstGfxObj + (uint)i);
        }

        setup.PlacementFrames.Add(Placement.Default, Frames(parts));
        if (defaultAnimation != 0)
        {
            setup.DefaultAnimation = defaultAnimation;
        }

        return ModelAssembly.FromSetup(id, setup);
    }

    /// <summary>A placement: part i at (0, 0, i) with no rotation.</summary>
    private static AnimationFrame Frames(int parts)
    {
        var frame = new AnimationFrame((uint)parts);
        for (var i = 0; i < parts; i++)
        {
            frame.Frames.Add(new DatFrame { Origin = new Vector3(0, 0, i), Orientation = Quaternion.Identity });
        }

        return frame;
    }

    private static MotionTableData MotionTable()
    {
        var style = MotionCommands.NonCombat;
        var table = new DatMotionTable { Id = Table, DefaultStyle = (MotionCommand)style };
        table.StyleDefaults[(MotionCommand)style] = (MotionCommand)MotionCommands.Ready;
        table.Cycles[Key(style, MotionCommands.Ready)] = Data(ReadyAnim);
        table.Cycles[Key(style, MotionCommands.WalkForward)] = Data(WalkAnim);
        table.Cycles[Key(style, MotionCommands.RunForward)] = Data(RunAnim);
        table.Cycles[Key(style, MotionCommands.Falling)] = Data(FallAnim);
        table.Cycles[Key(style, MotionCommands.TurnRight)] = Data(TurnAnim, omega: new Vector3(0, 0, -MathF.PI / 2f));
        table.Links[Key(style, MotionCommands.Ready)] = new DatMotionCommandData
        {
            MotionData = { [unchecked((int)MotionCommands.WalkForward)] = Data(ReadyToWalk) },
        };
        table.Links[Key(style, MotionCommands.WalkForward)] = new DatMotionCommandData
        {
            MotionData = { [unchecked((int)MotionCommands.Ready)] = Data(WalkToReady) },
        };
        return MotionTableData.FromDat(table);
    }

    private static int Key(uint style, uint motion) => unchecked((int)MotionTableData.Key(style, motion));

    private static DatMotionData Data(uint anim, Vector3 omega = default) => new()
    {
        Anims = { new DatAnimData { AnimId = anim, LowFrame = 0, HighFrame = -1, Framerate = 30f } },
        Omega = omega,
    };

    /// <summary>Animations built as raw records: every part i at (i·step·frame, 0, i), optional root motion.</summary>
    internal sealed class RigAnimations : IAnimationSource
    {
        private readonly Dictionary<uint, AnimationData> _anims = [];

        public RigAnimations Add(uint id, int frames, int parts, float posStepY = 0f, float partStepX = 0f)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            var withPos = posStepY != 0f;
            w.Write(id);
            w.Write(withPos ? 1u : 0u);
            w.Write((uint)parts);
            w.Write((uint)frames);
            if (withPos)
            {
                for (var f = 0; f < frames; f++)
                {
                    WriteFrame(w, new Vector3(0, posStepY, 0));
                }
            }

            for (var f = 0; f < frames; f++)
            {
                for (var p = 0; p < parts; p++)
                {
                    WriteFrame(w, new Vector3(partStepX * f, 0, p));
                }

                w.Write(0u); // no hooks
            }

            _anims[id] = AnimationData.Decode(id, ms.ToArray());
            return this;
        }

        public AnimationData? GetAnimation(uint animId) => _anims.GetValueOrDefault(animId);

        private static void WriteFrame(BinaryWriter w, Vector3 o)
        {
            w.Write(o.X);
            w.Write(o.Y);
            w.Write(o.Z);
            w.Write(1f);
            w.Write(0f);
            w.Write(0f);
            w.Write(0f);
        }
    }

    /// <summary>Every GfxObj loads with no surfaces.</summary>
    private sealed class RigAppearance : IAppearanceSource
    {
        public LodChain GetChain(uint gfxObjId) => LodChain.Create(gfxObjId, new GfxObj(), null, _ => true);

        public IReadOnlyList<uint> SurfaceIds(uint gfxObjId) => [];

        public uint OriginalTextureId(uint surfaceId) => 0;

        public uint OriginalPaletteId(uint surfaceId) => 0;

        public PaletteData? GetPalette(uint paletteId) => null;
    }

    /// <summary><see cref="ScriptTable"/> and its two empty scripts; no emitters.</summary>
    private sealed class RigEffects(bool materialise) : IEffectSource
    {
        private readonly Data.Effects.PhysicsScriptTableData _table = Lifestone.Data.Effects.PhysicsScriptTableData.FromDat(new PhysicsScriptTable
        {
            Id = ScriptTable,
            ScriptTable =
            {
                [PlayScript.Hidden] = new DatReaderWriter.Types.PhysicsScriptTableData { Scripts = { new ScriptAndModData { Mod = 1f, ScriptId = HiddenScript } } },
                [PlayScript.UnHide] = new DatReaderWriter.Types.PhysicsScriptTableData { Scripts = { new ScriptAndModData { Mod = 1f, ScriptId = UnHideScript } } },
                [PlayScript.LevelUp] = new DatReaderWriter.Types.PhysicsScriptTableData { Scripts = { new ScriptAndModData { Mod = 1f, ScriptId = LevelUpScript } } },
            },
        });

        public Data.Effects.PhysicsScriptData? GetScript(uint scriptId) =>
            scriptId switch
            {
                HiddenScript => materialise ? FadeScript(scriptId, RetailHiddenFade) : EmptyScript(scriptId),
                UnHideScript => materialise ? FadeScript(scriptId, RetailUnHideFade) : EmptyScript(scriptId),
                LevelUpScript => SoundTableScript(scriptId, LevelUpHookSoundType),
                _ => null,
            };

        public Data.Effects.PhysicsScriptTableData? GetScriptTable(uint tableId) => tableId == ScriptTable ? _table : null;

        public ParticleEmitterInfoData? GetEmitterInfo(uint emitterInfoId) => null;

        /// <summary>A script of one Transparent hook at time 0 (type 20, direction Both, start, end, time).</summary>
        private static Data.Effects.PhysicsScriptData FadeScript(uint id, (float Start, float End, float Time) fade)
        {
            var raw = new byte[8 + 8 + 4 + 4 + 12];
            BitConverter.TryWriteBytes(raw.AsSpan(0), id);
            BitConverter.TryWriteBytes(raw.AsSpan(4), 1u);
            BitConverter.TryWriteBytes(raw.AsSpan(8), 0.0);
            BitConverter.TryWriteBytes(raw.AsSpan(16), (uint)AnimHookType.Transparent);
            BitConverter.TryWriteBytes(raw.AsSpan(20), (int)AnimHookDirection.Both);
            BitConverter.TryWriteBytes(raw.AsSpan(24), fade.Start);
            BitConverter.TryWriteBytes(raw.AsSpan(28), fade.End);
            BitConverter.TryWriteBytes(raw.AsSpan(32), fade.Time);
            return Lifestone.Data.Effects.PhysicsScriptData.Decode(id, raw);
        }

        /// <summary>A script of one SoundTable hook at time 0 (type 2, direction Both, the sound type).</summary>
        private static Data.Effects.PhysicsScriptData SoundTableScript(uint id, uint soundType)
        {
            var raw = new byte[8 + 8 + 4 + 4 + 4];
            BitConverter.TryWriteBytes(raw.AsSpan(0), id);
            BitConverter.TryWriteBytes(raw.AsSpan(4), 1u);
            BitConverter.TryWriteBytes(raw.AsSpan(8), 0.0);
            BitConverter.TryWriteBytes(raw.AsSpan(16), (uint)AnimHookType.SoundTable);
            BitConverter.TryWriteBytes(raw.AsSpan(20), (int)AnimHookDirection.Both);
            BitConverter.TryWriteBytes(raw.AsSpan(24), soundType);
            return Lifestone.Data.Effects.PhysicsScriptData.Decode(id, raw);
        }

        /// <summary>A script with no hooks: its id and a count of 0.</summary>
        private static Data.Effects.PhysicsScriptData EmptyScript(uint id)
        {
            var raw = new byte[8];
            BitConverter.TryWriteBytes(raw, id);
            return Lifestone.Data.Effects.PhysicsScriptData.Decode(id, raw);
        }
    }
}

/// <summary>A ground probe that answers a fixed height everywhere, and counts its calls.</summary>
internal sealed class FlatGround(float z) : Lifestone.Data.Physics.IGroundProbe
{
    public int Calls { get; private set; }

    public bool TryGroundZ(in CellFrame at, float radius, out float groundZ, out Plane contact)
    {
        Calls++;
        groundZ = z;
        contact = new Plane(Vector3.UnitZ, -z);
        return true;
    }
}

/// <summary>Helpers shared by the avatar tests.</summary>
internal static class AvatarAssert
{
    public static void Near(Vector3 expected, Vector3 actual, float tolerance = 1e-4f)
    {
        Assert.True(Vector3.Distance(expected, actual) <= tolerance, $"expected {expected}, got {actual}");
    }

    /// <summary>The landblock origin difference between two cells, for comparing frames across a seam.</summary>
    public static Vector3 Block(uint from, uint to) => LandDefs.GetBlockOffset(from, to);
}
