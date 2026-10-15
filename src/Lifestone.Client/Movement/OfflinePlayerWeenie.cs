using Lifestone.Data.Motion;
using Lifestone.Data.Physics.Objects;

namespace Lifestone.Client.Movement;

/// <summary>
/// The weenie of the explore-mode player (<see cref="OfflineWalker"/>), who has no character: a creature and the
/// client's own player, never a player killer or impenetrable, carrying nothing, with Run and Jump skills that are
/// <b>placeholders</b> (<see cref="PlaceholderRunSkill"/>, <see cref="PlaceholderJumpSkill"/>) in place of the
/// qualities a logged-in character's weenie reads (retail <c>CACQualities::InqRunRate</c> and <c>InqJumpVelocity</c>,
/// 11.4186 0x00592800 and 0x00592980, TODO(physics): 11.6096 not in the symbol table; the state track's player weenie,
/// owner decision D16).
/// </summary>
/// <remarks>
/// The answers follow the retail functions with a load of 0 and stamina left: the run rate is
/// <see cref="MovementSystem.GetRunRate"/>, the jump speed <c>sqrt(</c><see cref="MovementSystem.GetJumpHeight"/><c> × 19.6)</c>,
/// a jump is allowed below a load of 2 (<c>CACQualities::CanJump</c>, 11.4186 0x00591B50, 11.6096 0x00592850), and its
/// stamina cost is <see cref="MovementSystem.JumpStaminaCost"/> (<c>CACQualities::JumpStaminaCost</c>, 0x00591B90,
/// 0x00592890).
/// </remarks>
public sealed class OfflinePlayerWeenie : IPhysicsWeenie
{
    /// <summary>The placeholder Run skill (no character in explore mode).</summary>
    public const int PlaceholderRunSkill = 100;

    /// <summary>The placeholder Jump skill (no character in explore mode).</summary>
    public const int PlaceholderJumpSkill = 100;

    /// <summary>A weenie with the given (placeholder) skills.</summary>
    public OfflinePlayerWeenie(int runSkill = PlaceholderRunSkill, int jumpSkill = PlaceholderJumpSkill)
    {
        RunSkill = runSkill;
        JumpSkill = jumpSkill;
    }

    /// <summary>The Run skill used for the run rate.</summary>
    public int RunSkill { get; }

    /// <summary>The Jump skill used for the jump speed.</summary>
    public int JumpSkill { get; }

    /// <inheritdoc/>
    public bool IsCreature => true;

    /// <inheritdoc/>
    public bool IsThePlayer => true;

    /// <inheritdoc/>
    public bool IsPlayer => true;

    /// <inheritdoc/>
    public bool IsPK => false;

    /// <inheritdoc/>
    public bool IsPKLite => false;

    /// <inheritdoc/>
    public bool IsImpenetrable => false;

    /// <inheritdoc/>
    public bool TryGetRunRate(out float rate)
    {
        rate = (float)MovementSystem.GetRunRate(0f, RunSkill, 1f);
        return true;
    }

    /// <inheritdoc/>
    public bool TryGetJumpVelocity(float extent, out float velocityZ)
    {
        velocityZ = (float)Math.Sqrt(MovementSystem.GetJumpHeight(0f, JumpSkill, extent, 1f) * 19.6);
        return true;
    }

    /// <inheritdoc/>
    public bool CanJump(float extent) => true;

    /// <inheritdoc/>
    public bool TryGetJumpStaminaCost(float extent, out int staminaCost)
    {
        staminaCost = (int)MovementSystem.JumpStaminaCost(extent, 0f, playerKiller: false);
        return true;
    }
}
