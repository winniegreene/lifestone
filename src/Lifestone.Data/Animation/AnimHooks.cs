using System.Numerics;

namespace Lifestone.Data.Animation;

/// <summary>
/// Hook types of retail <c>CAnimHook::UnPackHook</c> (acclient 11.4186 PORTAL/canim/CAnimHook.c). The same hook
/// records appear in Animation (0x03) part frames and in PhysicsScript (0x33) timelines.
/// </summary>
public enum AnimHookType : uint
{
    NoOp = 0,
    Sound = 1,
    SoundTable = 2,
    Attack = 3,
    AnimationDone = 4,
    ReplaceObject = 5,
    Ethereal = 6,
    TransparentPart = 7,
    Luminous = 8,
    LuminousPart = 9,
    Diffuse = 10,
    DiffusePart = 11,
    Scale = 12,
    CreateParticle = 13,
    DestroyParticle = 14,
    StopParticle = 15,
    NoDraw = 16,
    DefaultScript = 17,
    DefaultScriptPart = 18,
    CallPES = 19,
    Transparent = 20,
    SoundTweaked = 21,
    SetOmega = 22,
    TextureVelocity = 23,
    TextureVelocityPart = 24,
    SetLight = 25,
    CreateBlockingParticle = 26,
}

/// <summary>
/// Retail <c>CAnimHook::direction_</c>: a hook fires when the animation crosses its frame in this direction
/// (<c>CSequence::execute_hooks</c>: fires when <c>direction_ == 0 || direction_ == dir</c>).
/// </summary>
public enum AnimHookDirection
{
    Both = 0,
    Forward = 1,
    Backward = -1,
}

/// <summary>Attack cone of an <see cref="AttackHook"/> (retail <c>AttackCone</c>, 28 bytes with the part index).</summary>
public readonly record struct AttackCone(int PartIndex, float LeftX, float LeftY, float RightX, float RightY, float Radius, float Height);

/// <summary>One decoded animation/physics-script hook. Field names follow the retail hook classes.</summary>
public abstract record AnimHook(AnimHookDirection Direction)
{
    public abstract AnimHookType Type { get; }
}

public sealed record NoOpHook(AnimHookDirection Direction) : AnimHook(Direction)
{
    public override AnimHookType Type => AnimHookType.NoOp;
}

/// <summary>Plays a Wave (0x0A) directly (retail <c>SoundHook::Execute</c> → <c>SoundManager::PlaySound(gid)</c>).</summary>
public sealed record SoundHook(AnimHookDirection Direction, uint SoundId) : AnimHook(Direction)
{
    public override AnimHookType Type => AnimHookType.Sound;
}

/// <summary>Plays a sound type from the object's SoundTable (0x20) (<c>SoundTableHook::Execute</c>).</summary>
public sealed record SoundTableHook(AnimHookDirection Direction, uint SoundType) : AnimHook(Direction)
{
    public override AnimHookType Type => AnimHookType.SoundTable;
}

public sealed record AttackHook(AnimHookDirection Direction, AttackCone Cone) : AnimHook(Direction)
{
    public override AnimHookType Type => AnimHookType.Attack;
}

/// <summary>
/// Animation-done notification. <c>CSequence::update_internal</c> queues a synthetic one (retail global
/// <c>anim_done_hook</c>) whenever a non-cyclic animation finishes; type 4 can also be stored in data.
/// </summary>
public sealed record AnimationDoneHook(AnimHookDirection Direction) : AnimHook(Direction)
{
    public override AnimHookType Type => AnimHookType.AnimationDone;
}

/// <summary>Swaps the GfxObj of one part (<c>ReplaceObjectHook</c>, an <c>AnimPartChange</c>).</summary>
public sealed record ReplaceObjectHook(AnimHookDirection Direction, int PartIndex, uint PartId) : AnimHook(Direction)
{
    public override AnimHookType Type => AnimHookType.ReplaceObject;
}

public sealed record EtherealHook(AnimHookDirection Direction, bool Ethereal) : AnimHook(Direction)
{
    public override AnimHookType Type => AnimHookType.Ethereal;
}

/// <summary>Interpolates one part's translucency from <see cref="Start"/> to <see cref="End"/> over <see cref="Time"/> seconds (<c>CPhysicsObj::SetPartTranslucency</c>).</summary>
public sealed record TransparentPartHook(AnimHookDirection Direction, int PartIndex, float Start, float End, float Time) : AnimHook(Direction)
{
    public override AnimHookType Type => AnimHookType.TransparentPart;
}

public sealed record LuminousHook(AnimHookDirection Direction, float Start, float End, float Time) : AnimHook(Direction)
{
    public override AnimHookType Type => AnimHookType.Luminous;
}

public sealed record LuminousPartHook(AnimHookDirection Direction, int PartIndex, float Start, float End, float Time) : AnimHook(Direction)
{
    public override AnimHookType Type => AnimHookType.LuminousPart;
}

public sealed record DiffuseHook(AnimHookDirection Direction, float Start, float End, float Time) : AnimHook(Direction)
{
    public override AnimHookType Type => AnimHookType.Diffuse;
}

public sealed record DiffusePartHook(AnimHookDirection Direction, int PartIndex, float Start, float End, float Time) : AnimHook(Direction)
{
    public override AnimHookType Type => AnimHookType.DiffusePart;
}

/// <summary>Scales the whole object to <see cref="End"/> over <see cref="Time"/> seconds (<c>CPhysicsObj::SetScale</c>).</summary>
public sealed record ScaleHook(AnimHookDirection Direction, float End, float Time) : AnimHook(Direction)
{
    public override AnimHookType Type => AnimHookType.Scale;
}

/// <summary>
/// Creates a particle emitter (<c>CPhysicsObj::create_particle_emitter</c>): ParticleEmitterInfo (0x32)
/// <see cref="EmitterInfoId"/>, attached to part <see cref="PartIndex"/> (−1 = the object frame) at <see cref="Offset"/>.
/// <see cref="EmitterId"/> 0 means "allocate a new id"; a non-zero id replaces an existing emitter with that id.
/// </summary>
public sealed record CreateParticleHook(AnimHookDirection Direction, uint EmitterInfoId, int PartIndex, AcFrame Offset, uint EmitterId) : AnimHook(Direction)
{
    public override AnimHookType Type => AnimHookType.CreateParticle;
}

/// <summary>Like <see cref="CreateParticleHook"/>, but does nothing while an emitter with the same non-zero id exists (<c>ParticleManager::CreateBlockingParticleEmitter</c>).</summary>
public sealed record CreateBlockingParticleHook(AnimHookDirection Direction, uint EmitterInfoId, int PartIndex, AcFrame Offset, uint EmitterId) : AnimHook(Direction)
{
    public override AnimHookType Type => AnimHookType.CreateBlockingParticle;
}

public sealed record DestroyParticleHook(AnimHookDirection Direction, uint EmitterId) : AnimHook(Direction)
{
    public override AnimHookType Type => AnimHookType.DestroyParticle;
}

public sealed record StopParticleHook(AnimHookDirection Direction, uint EmitterId) : AnimHook(Direction)
{
    public override AnimHookType Type => AnimHookType.StopParticle;
}

public sealed record NoDrawHook(AnimHookDirection Direction, bool NoDraw) : AnimHook(Direction)
{
    public override AnimHookType Type => AnimHookType.NoDraw;
}

/// <summary>Plays the object's default PlayScript through its PhysicsScriptTable (<c>CPhysicsObj::play_default_script</c>).</summary>
public sealed record DefaultScriptHook(AnimHookDirection Direction) : AnimHook(Direction)
{
    public override AnimHookType Type => AnimHookType.DefaultScript;
}

/// <summary>Plays the default script of the child object attached at <see cref="PartIndex"/>.</summary>
public sealed record DefaultScriptPartHook(AnimHookDirection Direction, int PartIndex) : AnimHook(Direction)
{
    public override AnimHookType Type => AnimHookType.DefaultScriptPart;
}

/// <summary>Plays PhysicsScript <see cref="ScriptId"/> after a random delay in [0, <see cref="Pause"/>] (<c>CPhysicsObj::CallPES</c>).</summary>
public sealed record CallPesHook(AnimHookDirection Direction, uint ScriptId, float Pause) : AnimHook(Direction)
{
    public override AnimHookType Type => AnimHookType.CallPES;
}

/// <summary>Interpolates the whole object's translucency (<c>CPhysicsObj::SetTranslucency2</c>).</summary>
public sealed record TransparentHook(AnimHookDirection Direction, float Start, float End, float Time) : AnimHook(Direction)
{
    public override AnimHookType Type => AnimHookType.Transparent;
}

public sealed record SoundTweakedHook(AnimHookDirection Direction, uint SoundId, float Priority, float Probability, float Volume) : AnimHook(Direction)
{
    public override AnimHookType Type => AnimHookType.SoundTweaked;
}

public sealed record SetOmegaHook(AnimHookDirection Direction, Vector3 Axis) : AnimHook(Direction)
{
    public override AnimHookType Type => AnimHookType.SetOmega;
}

public sealed record TextureVelocityHook(AnimHookDirection Direction, float USpeed, float VSpeed) : AnimHook(Direction)
{
    public override AnimHookType Type => AnimHookType.TextureVelocity;
}

public sealed record TextureVelocityPartHook(AnimHookDirection Direction, int PartIndex, float USpeed, float VSpeed) : AnimHook(Direction)
{
    public override AnimHookType Type => AnimHookType.TextureVelocityPart;
}

public sealed record SetLightHook(AnimHookDirection Direction, bool LightsOn) : AnimHook(Direction)
{
    public override AnimHookType Type => AnimHookType.SetLight;
}

/// <summary>
/// Raw decoder for one hook, re-expressing retail <c>CAnimHook::UnPackHook</c> and the per-class <c>UnPack</c>
/// methods (acclient 11.4186 PORTAL/canim/CAnimHook.c), cross-checked against ACE
/// <c>ACE.DatLoader/Entity/AnimationHooks/*.cs</c> (AGPL-3.0, @95ef426d).
/// </summary>
/// <remarks>
/// We do not use DatReaderWriter 2.1.9 for hooks: its <c>CreateBlockingParticleHook</c> reads no payload, while retail
/// allocates it as a <c>CreateParticleHook</c> subclass (same 0x58-byte object) and reads the same 40 bytes.
/// Header: <c>u32 type, i32 direction</c>, then the payload.
/// </remarks>
internal static class AnimHookDecoder
{
    public static AnimHook Read(DatByteReader r)
    {
        var start = r.Position;
        var type = r.ReadUInt32();
        var dir = (AnimHookDirection)r.ReadInt32();
        if (dir is not (AnimHookDirection.Both or AnimHookDirection.Forward or AnimHookDirection.Backward))
        {
            throw new InvalidDataException($"Record 0x{r.RecordId:X8}: hook at offset {start} has direction {(int)dir}.");
        }

        switch ((AnimHookType)type)
        {
            case AnimHookType.NoOp:
                return new NoOpHook(dir);
            case AnimHookType.Sound:
                return new SoundHook(dir, r.ReadUInt32());
            case AnimHookType.SoundTable:
                return new SoundTableHook(dir, r.ReadUInt32());
            case AnimHookType.Attack:
                return new AttackHook(dir, new AttackCone(
                    r.ReadInt32(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
            case AnimHookType.AnimationDone:
                return new AnimationDoneHook(dir);
            case AnimHookType.ReplaceObject:
            {
                // ReplaceObjectHook::UnPack → AnimPartChange::UnPack: u16 part index, then a DataID of known type
                // 0x01000000 packed as u16 (or 2×u16 when the high bit is set). ACE ReadAsDataIDOfKnownType.
                var part = r.ReadUInt16();
                return new ReplaceObjectHook(dir, part, ReadDataIdOfKnownType(r, 0x01000000u));
            }
            case AnimHookType.Ethereal:
                return new EtherealHook(dir, r.ReadInt32() != 0);
            case AnimHookType.TransparentPart:
                return new TransparentPartHook(dir, r.ReadInt32(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            case AnimHookType.Luminous:
                return new LuminousHook(dir, r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            case AnimHookType.LuminousPart:
                return new LuminousPartHook(dir, r.ReadInt32(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            case AnimHookType.Diffuse:
                return new DiffuseHook(dir, r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            case AnimHookType.DiffusePart:
                return new DiffusePartHook(dir, r.ReadInt32(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            case AnimHookType.Scale:
                return new ScaleHook(dir, r.ReadSingle(), r.ReadSingle());
            case AnimHookType.CreateParticle:
                return new CreateParticleHook(dir, r.ReadUInt32(), r.ReadInt32(), r.ReadFrame(), r.ReadUInt32());
            case AnimHookType.CreateBlockingParticle:
                return new CreateBlockingParticleHook(dir, r.ReadUInt32(), r.ReadInt32(), r.ReadFrame(), r.ReadUInt32());
            case AnimHookType.DestroyParticle:
                return new DestroyParticleHook(dir, r.ReadUInt32());
            case AnimHookType.StopParticle:
                return new StopParticleHook(dir, r.ReadUInt32());
            case AnimHookType.NoDraw:
                return new NoDrawHook(dir, r.ReadInt32() != 0);
            case AnimHookType.DefaultScript:
                return new DefaultScriptHook(dir);
            case AnimHookType.DefaultScriptPart:
                return new DefaultScriptPartHook(dir, r.ReadInt32());
            case AnimHookType.CallPES:
                return new CallPesHook(dir, r.ReadUInt32(), r.ReadSingle());
            case AnimHookType.Transparent:
                return new TransparentHook(dir, r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            case AnimHookType.SoundTweaked:
                return new SoundTweakedHook(dir, r.ReadUInt32(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            case AnimHookType.SetOmega:
                return new SetOmegaHook(dir, r.ReadVector3());
            case AnimHookType.TextureVelocity:
                return new TextureVelocityHook(dir, r.ReadSingle(), r.ReadSingle());
            case AnimHookType.TextureVelocityPart:
                return new TextureVelocityPartHook(dir, r.ReadInt32(), r.ReadSingle(), r.ReadSingle());
            case AnimHookType.SetLight:
                return new SetLightHook(dir, r.ReadInt32() != 0);
            default:
                // Retail UnPackHook has no case for other values and returns no hook; the record would be corrupt.
                throw new InvalidDataException($"Record 0x{r.RecordId:X8}: unknown hook type {type} at offset {start}.");
        }
    }

    /// <summary>ACE <c>BinaryReaderExtensions.ReadAsDataIDOfKnownType</c> (retail <c>UnPackPackedDataID</c>).</summary>
    private static uint ReadDataIdOfKnownType(DatByteReader r, uint knownType)
    {
        var value = r.ReadUInt16();
        if ((value & 0x8000) != 0)
        {
            var lower = r.ReadUInt16();
            var higher = (uint)(value & 0x3FFF) << 16;
            return knownType + (higher | lower);
        }

        return knownType + value;
    }
}
