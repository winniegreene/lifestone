using System.Numerics;
using DatReaderWriter.DBObjs;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Effects;

namespace Lifestone.Client.PortalSpace;

/// <summary>
/// The tunnel object of portal space (retail <c>gmSmartBoxUI::teleportObj</c>): the UIASSET tunnel Setup at the scene's
/// origin, spinning the UIASSET animation (<see cref="TunnelSpin"/>), with an effect object that turns the spin's frame-2
/// hook into the tunnel hum. The wave-2 tunnel view draws its <see cref="PartFrames"/>; the state machine reads its
/// <see cref="CurrentFrame"/>; the director drains its hum.
/// </summary>
/// <remarks>
/// <para><b>Stepping.</b> Retail steps creature-mode objects inside <c>CreatureMode::Render</c> (11.4186 0x004529D0,
/// 11.6096 0x00452A10), so only while the tunnel shows: the caller calls <see cref="AdvanceTo"/> only then. Each call
/// re-expresses <c>CPhysicsObj::update_position</c> (11.4186 0x00514520, 11.6096 0x00515020) through
/// <see cref="CreatureModeClock"/>: no step until 1/30 s has built up, then one step of the whole time, and a gap over
/// 2 s skipped. A step is <c>CPhysicsObj::UpdatePositionInternal</c> (11.4186 0x00512C30, 11.6096 0x00513730): the spin
/// advances and its hooks are queued, then <see cref="EffectObject.Update"/> runs them (<c>process_hooks</c>), poses the
/// parts (<c>CPhysicsObj::set_frame</c>), and updates the particles and scripts. The UIASSET animation has no root
/// motion (no position frames; the asset conformance tests pin it) and the object has no velocity, so the object stays
/// at the scene's origin.</para>
/// <para><b>Restarts.</b> <see cref="Restart"/> restarts the spin only, as retail's <c>set_sequence_animation</c> does:
/// the clock is the object's and carries over (a tunnel shown again within 2 s of its last step catches up on the hidden
/// time in its first step; one hidden longer skips it), and the parts keep their last pose until the next step.</para>
/// <para><b>The hum.</b> The spin's SoundTweaked hook reaches the effect object, which hands it on as an
/// <see cref="EffectEvent"/> (a sound is the engine's concern); <see cref="DrainEvents"/> moves those events out, one per
/// 2.975 s loop.</para>
/// <para>Frame thread only. A steady <see cref="AdvanceTo"/> allocates nothing once the event lists have grown;
/// <see cref="Restart"/> allocates the sequence node.</para>
/// </remarks>
public sealed class TunnelObject : IPortalTunnel
{
    private readonly AcFrame[] _partFrames;
    private readonly CreatureModeClock _clock;

    /// <summary>Creates a stopped tunnel object.</summary>
    /// <param name="animations">Where the spin's animation comes from.</param>
    /// <param name="effects">The effect definitions (the tunnel has no script table; its hooks are the spin's).</param>
    /// <param name="animationId">The UIASSET portal-space animation (<see cref="PortalSpaceAssets.TunnelAnimationId"/>).</param>
    /// <param name="partCount">The tunnel Setup's part count (2).</param>
    /// <param name="seed">The effect object's random seed (the tunnel's hooks draw nothing today).</param>
    /// <param name="createdAt">The host's frame time when the object is made (retail <c>InitObjectBegin</c> starts the
    /// object's clock then).</param>
    /// <exception cref="InvalidDataException">The animation does not exist.</exception>
    public TunnelObject(IAnimationSource animations, IEffectSource effects, uint animationId, int partCount, int seed = 0, double createdAt = 0.0)
    {
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentOutOfRangeException.ThrowIfLessThan(partCount, 1);
        Spin = new TunnelSpin(animations, animationId);
        PartCount = partCount;
        Effects = new EffectObject(effects, new RetailRandom(seed), physicsScriptTableId: 0);
        _partFrames = new AcFrame[partCount];
        _clock = new CreatureModeClock(createdAt);
        PoseParts();
        Effects.SetPlacement(createdAt, Frame, _partFrames);
    }

    /// <summary>The object's frame: the scene's origin (<see cref="PortalSpaceScene.TunnelPosition"/>), the identity orientation.</summary>
    public static AcFrame Frame => new(PortalSpaceScene.TunnelPosition, Quaternion.Identity);

    /// <summary>The spin.</summary>
    public TunnelSpin Spin { get; }

    /// <summary>The effect object (its pending events hold the hum until <see cref="DrainEvents"/>).</summary>
    public EffectObject Effects { get; }

    /// <summary>The tunnel Setup's part count.</summary>
    public int PartCount { get; }

    /// <summary>The parts' frames in the scene (AC coordinates), as of the last step.</summary>
    public ReadOnlySpan<AcFrame> PartFrames => _partFrames;

    /// <summary>The clock value of the last step or skip (retail <c>update_time</c>).</summary>
    public double UpdateTime => _clock.UpdateTime;

    /// <inheritdoc/>
    public int CurrentFrame => Spin.CurrentFrame;

    /// <summary>
    /// Creates the tunnel object of the user's dats: the spin's animation and the part count come through the UIASSET
    /// records in <paramref name="assets"/>.
    /// </summary>
    /// <param name="dats">The user's dats.</param>
    /// <param name="assets">The UIASSET records.</param>
    /// <param name="seed">The effect object's random seed.</param>
    /// <param name="createdAt">The host's frame time when the object is made.</param>
    /// <exception cref="InvalidDataException">The tunnel Setup or its animation does not exist.</exception>
    public static TunnelObject Create(DatSet dats, PortalSpaceAssets assets, int seed = 0, double createdAt = 0.0)
    {
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentNullException.ThrowIfNull(assets);
        if (!dats.TryGetPortal<Setup>(assets.TunnelSetupId, out var setup))
        {
            throw new InvalidDataException($"The portal-space tunnel Setup 0x{assets.TunnelSetupId:X8} does not exist.");
        }

        return new TunnelObject(new DatAnimationSource(dats), new DatEffectSource(dats), assets.TunnelAnimationId, setup.Parts.Count, seed, createdAt);
    }

    /// <inheritdoc/>
    public void Restart() => Spin.Restart();

    /// <inheritdoc/>
    public void Stop() => Spin.Stop();

    /// <inheritdoc/>
    public void AdvanceTo(double now)
    {
        if (!_clock.TryStep(now, out var quantum))
        {
            return;
        }

        var hooks = Spin.Update(quantum).Hooks;
        for (var i = 0; i < hooks.Count; i++)
        {
            Effects.QueueAnimHook(hooks[i]);
        }

        PoseParts();
        Effects.Update(now, Frame, _partFrames);
    }

    /// <inheritdoc/>
    public int DrainEvents(List<EffectEvent> into) => Effects.DrainEvents(into);

    /// <summary>Poses the parts from the spin's current pose (retail floor sampling); a stopped spin leaves them at the object.</summary>
    private void PoseParts()
    {
        var frame = Frame;
        if (Spin.Pose is { } pose)
        {
            PartPose.Update(frame, pose, Vector3.One, _partFrames);
            for (var i = pose.Count; i < _partFrames.Length; i++)
            {
                _partFrames[i] = frame;
            }
        }
        else
        {
            Array.Fill(_partFrames, frame);
        }
    }
}
