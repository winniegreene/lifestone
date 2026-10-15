namespace Lifestone.Client.Movement;

/// <summary>
/// The jump bar: retail <c>ClientCombatSystem</c>'s jump half (11.4186 AC/accui_misc/CombatSystem.c; names and
/// addresses cited, nothing copied). Holding the jump key fills the bar in one second (0.8 s in the dual-wield stance);
/// releasing it jumps with the bar's level as the extent, never less than <see cref="MinJumpExtent"/>.
/// </summary>
/// <remarks>
/// <para>State only: <see cref="PlayerMovementController.CommenceJump"/> and <see cref="PlayerMovementController.DoJump"/>
/// drive it with the motion interpreter's charge and jump and send the Jump message. The bar's level for the UI is
/// <see cref="GetJumpPowerLevel"/> (retail's <c>ClientCombatSystem::UseTime</c>, 11.4186 0x0056C1F0, 11.6096 0x0056CF40,
/// only redraws the bar from it; a full bar does not jump by itself).</para>
/// <para>Addresses are 11.4186 / 11.6096 (docs/research/retail-addresses-m2.md).</para>
/// </remarks>
public sealed class JumpCharge
{
    /// <summary>
    /// Retail <c>MIN_JUMP_EXTENT</c>, the float 0.001 (bits 0x3A83126F) at 11.6096 VA 0x007D01C8 in <c>.rdata</c>, read under
    /// owner decision D4 (docs/research/retail-addresses-m2.md §1.2): the extent of a jump whose bar is below it.
    /// </summary>
    public const float MinJumpExtent = 0.001f;

    /// <summary>Seconds the bar takes to fill (<c>ClientCombatSystem::GetPowerBarLevel</c>'s divisor, 1.0).</summary>
    public const double FillSeconds = 1.0;

    /// <summary>Seconds the bar takes to fill in the dual-wield stance (the divisor 0.8).</summary>
    public const double DualWieldFillSeconds = 0.8;

    /// <summary>The dual-wield stance, DualWieldCombat 0x80000046, which fills the bar faster.</summary>
    public const uint DualWieldCombatStyle = 0x80000046;

    /// <summary>Retail <c>jump_pending</c>: the jump key is down and the charge was allowed.</summary>
    public bool Pending { get; private set; }

    /// <summary>Retail <c>buildInProgress</c>: the bar is filling.</summary>
    public bool Building { get; private set; }

    /// <summary>Retail <c>buildStartTime</c>: when the bar started filling (seconds), 0 when it is not.</summary>
    public double BuildStartTime { get; private set; }

    /// <summary>
    /// The success branch of retail <c>ClientCombatSystem::CommenceJump</c> (0x0056AF90 / 0x0056BCD0): the jump is
    /// pending and the bar starts filling at <paramref name="now"/> (<c>StartPowerBarBuild</c>, 0x0056ADB0 / 0x0056BAF0).
    /// </summary>
    public void Start(double now)
    {
        Pending = true;
        Building = true;
        BuildStartTime = now;
    }

    /// <summary>
    /// Retail <c>ClientCombatSystem::GetPowerBarLevel</c> (0x0056ADE0 / 0x0056BB20): while the bar fills, the seconds since
    /// it started over <see cref="FillSeconds"/> (<see cref="DualWieldFillSeconds"/> when the player's interpreted style is
    /// <see cref="DualWieldCombatStyle"/>), clamped to [0, 1]; 0 when it is not filling.
    /// </summary>
    /// <param name="now">The clock in seconds.</param>
    /// <param name="currentStyle">The player's interpreted motion style (retail reads <c>InqInterpretedMotionState()->current_style</c>).</param>
    public double GetPowerBarLevel(double now, uint currentStyle)
    {
        if (!Building)
        {
            return 0.0;
        }

        var level = (now - BuildStartTime) / (currentStyle == DualWieldCombatStyle ? DualWieldFillSeconds : FillSeconds);
        return level > 1.0 ? 1.0 : level < 0.0 ? 0.0 : level;
    }

    /// <summary>
    /// Retail <c>ClientCombatSystem::GetJumpPowerLevel</c> (0x0056B050 / 0x0056BD90): while a jump is pending, the extent a
    /// release would jump with (<see cref="ToExtent"/> of <see cref="GetPowerBarLevel"/>); 0 otherwise.
    /// </summary>
    public float GetJumpPowerLevel(double now, uint currentStyle) =>
        Pending ? ToExtent(GetPowerBarLevel(now, currentStyle)) : 0f;

    /// <summary>
    /// The extent of a bar at <paramref name="level"/>, as <c>GetJumpPowerLevel</c> and <c>ClientCombatSystem::DoJump</c>
    /// (0x0056B110 / 0x0056BE50) take it: the level as a float when it is at least <see cref="MinJumpExtent"/> (the
    /// comparison is made on the double), else <see cref="MinJumpExtent"/>.
    /// </summary>
    public static float ToExtent(double level) => level >= MinJumpExtent ? (float)level : MinJumpExtent;

    /// <summary>
    /// The bar's part of retail <c>ClientCombatSystem::FinishJump</c> (0x0056A9B0 / 0x0056B6F0): a pending jump's bar stops
    /// and is hidden, and the jump is no longer pending. The caller ends the standing long jump, as <c>FinishJump</c> does
    /// on the player's motion interpreter.
    /// </summary>
    public void Finish()
    {
        if (Pending)
        {
            Building = false;
            BuildStartTime = 0.0;
        }

        Pending = false;
    }
}
