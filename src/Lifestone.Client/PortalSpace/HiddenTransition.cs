using DatReaderWriter.Enums;
using Lifestone.Data.Effects;

namespace Lifestone.Client.PortalSpace;

/// <summary>A change of an object's PhysicsState Hidden bit (0x4000).</summary>
public enum HiddenChange : byte
{
    /// <summary>The Hidden bit did not change: nothing happens.</summary>
    None,

    /// <summary>Hidden was set: the object dematerialises.</summary>
    Hide,

    /// <summary>Hidden was cleared: the object materialises (the pink bubbles after LoginComplete).</summary>
    Unhide,
}

/// <summary>
/// The pure rule for hiding and materialising objects (m2-log-in.md §4.9): retail <c>CPhysicsObj::set_state</c> (11.4186
/// 0x00514DD0, 11.6096 0x005158D0), which acts only when the Hidden bit changes, and <c>CPhysicsObj::set_hidden</c>
/// (11.4186 0x00514C60, 11.6096 0x00515760), which plays the object's Hidden or UnHide script and changes its drawing and
/// collision bits. The avatars track applies it to server objects (AV9); the rule itself lives here, once.
/// </summary>
/// <remarks>
/// <para><b>Hide</b> (<see cref="HiddenChange.Hide"/>): the object's PhysicsScriptTable entry for
/// <see cref="PlayScript.Hidden"/> (0x76) plays at intensity 1.0 (<c>PhysicsScriptTable::GetScript</c>, 11.4186 0x00521E40,
/// 11.6096 0x00522990, then <c>CPhysicsObj::play_script_internal</c>, 11.4186 0x00510CD0, 11.6096 0x005117A0); every child
/// (a wielded item) gets <see cref="NoDraw"/> and stops drawing (<c>CPartArray::SetNoDrawInternal</c>, 11.4186
/// 0x00518300, 11.6096 0x00518E30); a set
/// <see cref="ReportCollisions"/> is cleared, ending its collisions (<c>CPhysicsObj::report_collision_end</c>, 11.4186
/// 0x00514620, 11.6096 0x00515120); <see cref="IgnoreCollisions"/> is set; the cell stops drawing the object.</para>
/// <para><b>Unhide</b> (<see cref="HiddenChange.Unhide"/>): <see cref="PlayScript.UnHide"/> (0x75) plays at 1.0; the
/// children lose <see cref="NoDraw"/> and draw again; <see cref="IgnoreCollisions"/> is cleared;
/// <see cref="ReportCollisions"/> is set if it was clear, starting its collisions (<c>CPhysicsObj::report_collision_start</c>,
/// 11.4186 0x00513FD0, 11.6096 0x00514AD0); the cell draws the object again.</para>
/// <para><b>At creation.</b> A new object's state is <see cref="ConstructedState"/>, with Hidden clear
/// (<c>CPhysicsObj::CPhysicsObj</c>, 11.4186 0x005123F0, 11.6096 0x00512EF0). <c>CPhysicsObj::set_description</c>
/// (11.4186 0x00514F40, 11.6096 0x00515A40) loads the physics script table first and then applies the CreateObject's
/// state through <c>set_state</c>, so an object created Hidden plays its Hidden script at creation
/// (<see cref="ClassifyCreation"/>); the logging-in player does, unseen, inside the tunnel.</para>
/// <para><c>set_state</c> also reacts to changes of LightingOn (0x800) and NoDraw (0x20) on the object itself; those are
/// not this rule's.</para>
/// <para>Static and allocation-free, apart from what <see cref="EffectObject.PlayScript"/> allocates.</para>
/// </remarks>
public static class HiddenTransition
{
    /// <summary>PhysicsState <c>HIDDEN_PS</c>.</summary>
    public const uint Hidden = 0x4000;

    /// <summary>PhysicsState <c>NODRAW_PS</c>: set on the children of a hidden object.</summary>
    public const uint NoDraw = 0x20;

    /// <summary>PhysicsState <c>IGNORE_COLLISIONS_PS</c>: set while hidden.</summary>
    public const uint IgnoreCollisions = 0x10;

    /// <summary>PhysicsState <c>REPORT_COLLISIONS_PS</c>: cleared while hidden, set again when shown.</summary>
    public const uint ReportCollisions = 0x8;

    /// <summary>
    /// The state <c>CPhysicsObj::CPhysicsObj</c> gives a new object, before its description applies: EdgeSlide (0x400000),
    /// LightingOn, Gravity and ReportCollisions, with Hidden clear.
    /// </summary>
    public const uint ConstructedState = 0x00400C08;

    /// <summary>The intensity both scripts play at (<c>set_hidden</c> passes 1.0 to <c>GetScript</c>).</summary>
    public const float ScriptIntensity = 1f;

    /// <summary>Classifies a state change: <see cref="HiddenChange.None"/> unless the Hidden bit differs.</summary>
    public static HiddenChange Classify(uint oldState, uint newState) =>
        ((oldState ^ newState) & Hidden) == 0
            ? HiddenChange.None
            : (newState & Hidden) != 0 ? HiddenChange.Hide : HiddenChange.Unhide;

    /// <summary>
    /// Classifies a new object's state (the CreateObject's PhysicsDesc state) against <see cref="ConstructedState"/>: an
    /// object created Hidden is a <see cref="HiddenChange.Hide"/>, and plays the Hidden script.
    /// </summary>
    public static HiddenChange ClassifyCreation(uint createState) => Classify(ConstructedState, createState);

    /// <summary>
    /// The script type a change plays: <see cref="PlayScript.Hidden"/> (0x76) or <see cref="PlayScript.UnHide"/> (0x75);
    /// <see cref="PlayScript.Invalid"/> for <see cref="HiddenChange.None"/>.
    /// </summary>
    public static PlayScript Script(HiddenChange change) => change switch
    {
        HiddenChange.Hide => PlayScript.Hidden,
        HiddenChange.Unhide => PlayScript.UnHide,
        _ => PlayScript.Invalid,
    };

    /// <summary>
    /// The object's PhysicsState after <c>set_hidden</c>, from the state <c>set_state</c> stored (the new state, Hidden
    /// bit included): hiding sets Hidden and <see cref="IgnoreCollisions"/> and clears <see cref="ReportCollisions"/>;
    /// showing clears Hidden and <see cref="IgnoreCollisions"/> and sets <see cref="ReportCollisions"/>.
    /// </summary>
    public static uint ApplyToObjectState(uint state, HiddenChange change) => change switch
    {
        HiddenChange.Hide => (state | Hidden | IgnoreCollisions) & ~ReportCollisions,
        HiddenChange.Unhide => (state & ~(Hidden | IgnoreCollisions)) | ReportCollisions,
        _ => state,
    };

    /// <summary>A child's PhysicsState after its parent's change: <see cref="NoDraw"/> set on hiding, cleared on showing.</summary>
    public static uint ApplyToChildState(uint childState, HiddenChange change) => change switch
    {
        HiddenChange.Hide => childState | NoDraw,
        HiddenChange.Unhide => childState & ~NoDraw,
        _ => childState,
    };

    /// <summary>
    /// Plays the change's script on <paramref name="effects"/> through its PhysicsScriptTable at
    /// <see cref="ScriptIntensity"/>; returns the script id played, or 0 (no change, no table, or no entry).
    /// </summary>
    public static uint Play(EffectObject effects, HiddenChange change)
    {
        ArgumentNullException.ThrowIfNull(effects);
        return change == HiddenChange.None ? 0 : effects.PlayScript(Script(change), ScriptIntensity);
    }
}
