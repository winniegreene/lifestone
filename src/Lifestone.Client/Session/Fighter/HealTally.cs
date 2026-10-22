using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Fighter;

/// <summary>
/// The hunt's heal counters (M4 3.1 s1): plain numbers a driver feeds with <see cref="Record"/>, one line a finished heal handle, so the hunt's verdict (<c>heals=</c>, the kit's uses, the potions) reads them. Only a handle that ended
/// <see cref="ActionOutcomeKind.Confirmed"/> counts; a refused, failed or timed-out heal is not counted here (the driver sees its outcome). Game thread only; nothing allocates.
/// </summary>
public sealed class HealTally
{
    /// <summary>Kit sends confirmed: a use of the kit, won or lost (ACE spends the use first, win or lose: capture.md C1).</summary>
    public int KitUses { get; private set; }

    /// <summary>Kit uses whose roll won (<see cref="HealResultKind.Healed"/>).</summary>
    public int Heals { get; private set; }

    /// <summary>Kit uses whose roll lost (<see cref="HealResultKind.FailedRoll"/>): a use spent and nothing healed.</summary>
    public int FailedRolls { get; private set; }

    /// <summary>Drinks confirmed: a potion or food spent, whatever it restored.</summary>
    public int Potions { get; private set; }

    /// <summary>Drinks that restored nothing (<see cref="HealResultKind.Wasted"/>): a potion spent at a full vital.</summary>
    public int Wasted { get; private set; }

    /// <summary>The points restored by every heal and drink counted (the lines' amounts).</summary>
    public int Points { get; private set; }

    /// <summary>
    /// Counts one finished heal. A handle that is not completed, or did not end <see cref="ActionOutcomeKind.Confirmed"/>, adds nothing; a confirmed one whose line was not read (<see cref="HealHandle.Result"/> null) counts its kit use or
    /// drink only.
    /// </summary>
    /// <param name="handle">The handle of <see cref="IActionApi.UseWithTarget"/> or <see cref="IActionApi.Drink"/>.</param>
    public void Record(HealHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (handle.Outcome is not { Kind: ActionOutcomeKind.Confirmed })
        {
            return;
        }

        switch (handle.Kind)
        {
            case ActionKind.UseWithTarget:
                KitUses++;
                break;
            case ActionKind.Drink:
                Potions++;
                break;
            default:
                return;
        }

        switch (handle.Result)
        {
            case { Kind: HealResultKind.Healed } healed:
                Heals++;
                Points += healed.Amount;
                break;
            case { Kind: HealResultKind.FailedRoll }:
                FailedRolls++;
                break;
            case { Kind: HealResultKind.Drank } drank:
                Points += drank.Amount;
                break;
            case { Kind: HealResultKind.Wasted }:
                Wasted++;
                break;
        }
    }
}
