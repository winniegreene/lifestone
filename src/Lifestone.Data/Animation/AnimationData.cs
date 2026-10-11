namespace Lifestone.Data.Animation;

/// <summary>One keyframe of an animation: a frame per Setup part plus the hooks that fire when it is crossed (retail <c>AnimFrame</c>).</summary>
public sealed class AnimationPartFrame(IReadOnlyList<AcFrame> frames, IReadOnlyList<AnimHook> hooks)
{
    /// <summary>Per-part frame, relative to the object frame (flat, not hierarchical: rendering.md §6).</summary>
    public IReadOnlyList<AcFrame> Frames { get; } = frames;

    public IReadOnlyList<AnimHook> Hooks { get; } = hooks;
}

/// <summary>
/// A decoded Animation (0x03) record, re-expressing retail <c>CAnimation::UnPack</c> / <c>AnimFrame::UnPack</c>
/// (acclient 11.4186 PORTAL/canim/CAnim.c): <c>u32 id, u32 flags, u32 numParts, u32 numFrames,
/// [AFrame posFrames[numFrames] if flags &amp; 1], numFrames × (AFrame[numParts], u32 numHooks, hooks)</c>.
/// </summary>
public sealed class AnimationData
{
    /// <summary><c>flags &amp; 1</c>: the animation carries per-frame root motion (<see cref="PosFrames"/>).</summary>
    public const uint FlagPosFrames = 1;

    /// <summary><c>(flags &gt;&gt; 1) &amp; 1</c>: retail stores this as <c>CAnimation::has_hooks</c>.</summary>
    public const uint FlagHasHooks = 2;

    private AnimationData(uint id, uint flags, int numParts, IReadOnlyList<AcFrame>? posFrames, IReadOnlyList<AnimationPartFrame> partFrames)
    {
        Id = id;
        Flags = flags;
        NumParts = numParts;
        PosFrames = posFrames;
        PartFrames = partFrames;
    }

    public uint Id { get; }

    public uint Flags { get; }

    public int NumParts { get; }

    public int NumFrames => PartFrames.Count;

    /// <summary>Root-motion delta per frame (retail <c>pos_frames</c>), or null when <c>flags &amp; 1</c> is clear.</summary>
    public IReadOnlyList<AcFrame>? PosFrames { get; }

    public IReadOnlyList<AnimationPartFrame> PartFrames { get; }

    /// <summary>Decodes one raw Animation record (the bytes of file <paramref name="id"/>, starting with the id).</summary>
    public static AnimationData Decode(uint id, byte[] raw)
    {
        var r = new DatByteReader(raw, id);
        var storedId = r.ReadUInt32();
        if (storedId != id)
        {
            throw new InvalidDataException($"Animation 0x{id:X8}: record starts with id 0x{storedId:X8}.");
        }

        var flags = r.ReadUInt32();
        var numParts = checked((int)r.ReadUInt32());
        var numFrames = checked((int)r.ReadUInt32());
        if (numParts > 256 || numFrames > 100_000)
        {
            throw new InvalidDataException($"Animation 0x{id:X8}: implausible counts parts={numParts} frames={numFrames}.");
        }

        AcFrame[]? posFrames = null;
        if ((flags & FlagPosFrames) != 0)
        {
            posFrames = new AcFrame[numFrames];
            for (var i = 0; i < numFrames; i++)
            {
                posFrames[i] = r.ReadFrame();
            }
        }

        var partFrames = new AnimationPartFrame[numFrames];
        for (var f = 0; f < numFrames; f++)
        {
            var frames = new AcFrame[numParts];
            for (var p = 0; p < numParts; p++)
            {
                frames[p] = r.ReadFrame();
            }

            var numHooks = checked((int)r.ReadUInt32());
            var hooks = numHooks == 0 ? Array.Empty<AnimHook>() : new AnimHook[numHooks];
            for (var h = 0; h < numHooks; h++)
            {
                hooks[h] = AnimHookDecoder.Read(r);
            }

            partFrames[f] = new AnimationPartFrame(frames, hooks);
        }

        r.RequireEnd("Animation");
        return new AnimationData(id, flags, numParts, posFrames, partFrames);
    }
}
