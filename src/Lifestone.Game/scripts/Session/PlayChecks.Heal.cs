using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Game.Sessions;

// M4 3.1 s4: the heal rows (a kit used on the player, a potion drunk, a potion drunk at a full vital). The only hook is one call in CheckAllAsync, in the second session of the run (the one ReplayVisit plays).
// ReplayVisit is the visit that announces the kit and the five potions (HealItems) and whose player STARTS with partial health (a placeholder, TODO(m4-standin-start-vitals): Visit's own player is made partial by
// a vital update after the unhide, and its recorded description, when the machine has one, may be a full-health character's). The other rows keep Visit: its pack is the one they count.
// THE OWNER'S RULE (m4-plan.md D23): no creature, monster or creature corpse is added to any stand-in or play-check world, so no row here has a kill; the hunt's heal-between-kills logic is proved by HuntScenarioTests
// and live by the lead. Each row judges the session's state (the Action API's outcome and result, the object table's Structure and stack, the player's vital), never the stand-in's counters or its sends.
public sealed partial class PlayChecks
{
    /// <summary>The weenie class of the Handy Healing Kit and of the Potion of Healing (the world database's; the stand-in announces both with <c>StandInVisit.HealItems</c>).</summary>
    private const uint HealKitWcid = 628, HealPotionWcid = 377;

    /// <summary>The health a Potion of Healing restores at most (its BoostValue; ACE's <c>Food.cs</c>, capture.md C3).</summary>
    private const int HealPotionBoost = 25;

    /// <summary>The slack, in seconds, on the lane's margin check (the clock's double arithmetic: a send exactly one margin after the UseDone reads 0.0999999999).</summary>
    private const double HealMarginEpsilon = 1e-6;

    /// <summary>The seconds a row may wait for one heal (a use is 1.5 s of session time, plus the lane's hold).</summary>
    private const double HealWaitSeconds = 25.0;

    /// <summary>The kit uses a top-up may spend to reach a full vital (the kit has 20; a lost roll still spends one).</summary>
    private const int HealTopUpUses = 10;

    /// <summary>
    /// M4 3.1 s4: the heal rows on the stand-in, in the order that keeps a partial vital for each: <c>heal-potion</c> (one drink at a partial vital: Confirmed, <see cref="HealResultKind.Drank"/> of at most 25 and at most the
    /// missing health, the vital up by that amount, the stack down by one), <c>heal-kit</c> (two kit uses queued at once: the first Confirmed as <see cref="HealResultKind.Healed"/> or <see cref="HealResultKind.FailedRoll"/>
    /// with the kit's Structure down by one and, for a won roll, the vital up by the line's amount; the second still waiting for the lane when the first completed, then sent after it and Confirmed, or refused as a full vital),
    /// <c>heal-potion-at-full-vital</c> (the vital topped up by kit uses, then a drink at the full vital: Confirmed, <see cref="HealResultKind.Wasted"/>, the stack down by one, the vital as it was). Offline they prove the
    /// client's trackers and the stand-in's answers in the live order of cache/m4/3-1/capture.md (C1 to C3), not ACE's acceptance (<c>HealLiveTests</c> does that).
    /// </summary>
    private async Task HealChecksAsync(IGameSessionView session)
    {
        await HealPotionRowAsync(session);
        await HealKitRowAsync(session);
        await HealPotionAtFullRowAsync(session);
    }

    private async Task HealPotionRowAsync(IGameSessionView session)
    {
        const string name = "heal-potion";
        var potion = HealPackItem(session, HealPotionWcid);
        var partial = potion != 0 && await WaitFor(() => HealHealth(session, out var current, out var max) && current < max, 10.0);
        if (!partial || !HealHealth(session, out var before, out var healthMax))
        {
            Check(name, false, $"setup: potion_stack={potion != 0} health={HealVitalText(session)} (the replay visit's player starts with partial health and holds the potions: HealItems, TODO(m4-standin-start-vitals))");
            return;
        }

        var stackBefore = HealStackOf(session, potion);
        var drink = session.Actions.Drink(potion, ActionSource.Host);
        var done = await WaitFor(() => drink.IsCompleted, HealWaitSeconds);
        HealHealth(session, out var after, out _);
        var stackAfter = HealStackOf(session, potion);
        var missing = healthMax - before;
        var result = drink.Result;
        var rose = (long)after - before;
        var ok = done
            && drink.Outcome is { Kind: ActionOutcomeKind.Confirmed }
            && result is { Kind: HealResultKind.Drank, Amount: > 0 and <= HealPotionBoost }
            && result.Amount <= missing
            && rose == result.Amount
            && stackAfter == stackBefore - 1;
        Check(
            name,
            ok,
            $"outcome={DescribeHeal(drink)} health {before}/{healthMax} -> {after} (rose {rose}, the line said {result?.Amount.ToString() ?? "none"}, at most min({HealPotionBoost}, missing {missing})) stack {stackBefore} -> {stackAfter}");
    }

    private async Task HealKitRowAsync(IGameSessionView session)
    {
        const string name = "heal-kit";
        var kit = HealPackItem(session, HealKitWcid);
        var self = session.State.Self.Guid;
        var usesBefore = kit == 0 ? (int?)null : HealStructureOf(session, kit);
        var partial = kit != 0 && usesBefore is > 1 && await WaitFor(() => HealHealth(session, out var current, out var max) && current < max, 10.0);
        if (!partial || !HealHealth(session, out var before, out var healthMax))
        {
            Check(name, false, $"setup: kit={kit != 0} uses={usesBefore?.ToString() ?? "?"} health={HealVitalText(session)} (the replay visit's player starts with partial health and holds a kit: HealItems, TODO(m4-standin-start-vitals))");
            return;
        }

        // Two uses queued at once: the Heal lane sends the second only after the first's UseDone, the player's Ready and a short margin.
        var first = session.Actions.UseWithTarget(kit, self, ActionSource.Host);
        var second = session.Actions.UseWithTarget(kit, self, ActionSource.Host);
        var firstDone = await WaitFor(() => first.IsCompleted, HealWaitSeconds);
        var secondWaited = !second.IsCompleted; // read in the frame the first completed: the second's own use takes 1.5 s more
        HealHealth(session, out var afterFirst, out _);
        var usesAfterFirst = HealStructureOf(session, kit);
        var secondDone = await WaitFor(() => second.IsCompleted, HealWaitSeconds);
        HealHealth(session, out var afterSecond, out _);
        var usesAfterSecond = HealStructureOf(session, kit);

        var firstResult = first.Result;
        var firstOk = firstDone
            && first.Outcome is { Kind: ActionOutcomeKind.Confirmed }
            && firstResult is { Kind: HealResultKind.Healed or HealResultKind.FailedRoll }
            && usesAfterFirst == usesBefore - 1
            && (firstResult.Kind == HealResultKind.FailedRoll
                ? afterFirst == before && firstResult.Amount == 0
                : firstResult.Amount > 0 && firstResult.Amount <= healthMax - before && (long)afterFirst - before == firstResult.Amount);

        var secondConfirmed = second.Outcome is { Kind: ActionOutcomeKind.Confirmed };
        var secondRefusedFull = second.Outcome is { Kind: ActionOutcomeKind.ServerError } && afterFirst >= healthMax && afterSecond == afterFirst && usesAfterSecond == usesAfterFirst;
        var secondResult = second.Result;
        var secondOk = secondDone
            && secondWaited
            && second.SentAt is { } sent && first.CompletedAt is { } finished && sent >= finished
            && sent - finished >= HealLaneTiming.MarginSeconds - HealMarginEpsilon // the lane's own margin (M4 3.1 fix R25), beside the order: ACE is busy until the Ready F74C, and a repeat at the UseDone is refused 0x001D
            && ((secondConfirmed
                    && secondResult is { Kind: HealResultKind.Healed or HealResultKind.FailedRoll }
                    && usesAfterSecond == usesAfterFirst - 1
                    && (secondResult.Kind == HealResultKind.FailedRoll ? afterSecond == afterFirst : (long)afterSecond - afterFirst == secondResult.Amount && afterSecond <= healthMax))
                || secondRefusedFull);
        Check(
            name,
            firstOk && secondOk,
            $"first={DescribeHeal(first)} health {before}/{healthMax} -> {afterFirst} uses {usesBefore} -> {usesAfterFirst} | second waited_for_the_lane={secondWaited} sent_{(second.SentAt is { } s2 && first.CompletedAt is { } c1 ? (s2 - c1).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "n/a")}_s_after_the_first_usedone "
            + $"second={DescribeHeal(second)} health -> {afterSecond} uses -> {usesAfterSecond}");
    }

    private async Task HealPotionAtFullRowAsync(IGameSessionView session)
    {
        const string name = "heal-potion-at-full-vital";
        var kit = HealPackItem(session, HealKitWcid);
        var potion = HealPackItem(session, HealPotionWcid);
        var self = session.State.Self.Guid;
        if (kit == 0 || potion == 0 || !HealHealth(session, out _, out _))
        {
            Check(name, false, $"setup: kit={kit != 0} potion_stack={potion != 0} health={HealVitalText(session)}");
            return;
        }

        // To a full vital by kit uses (a lost roll spends a use and heals nothing: the loop goes on), then the drink.
        var uses = 0;
        while (uses < HealTopUpUses && HealHealth(session, out var current, out var max) && current < max)
        {
            var top = session.Actions.UseWithTarget(kit, self, ActionSource.Host);
            await WaitFor(() => top.IsCompleted, HealWaitSeconds);
            uses++;
        }

        if (!HealHealth(session, out var before, out var healthMax) || before < healthMax)
        {
            Check(name, false, $"the vital was not full after {uses} kit uses: health={HealVitalText(session)}");
            return;
        }

        var stackBefore = HealStackOf(session, potion);
        var drink = session.Actions.Drink(potion, ActionSource.Host);
        var done = await WaitFor(() => drink.IsCompleted, HealWaitSeconds);
        HealHealth(session, out var after, out _);
        var stackAfter = HealStackOf(session, potion);
        var ok = done
            && drink.Outcome is { Kind: ActionOutcomeKind.Confirmed }
            && drink.Result is { Kind: HealResultKind.Wasted, Amount: 0 }
            && after == before
            && stackAfter == stackBefore - 1;
        Check(name, ok, $"after {uses} top-up kit uses: outcome={DescribeHeal(drink)} health {before}/{healthMax} -> {after} stack {stackBefore} -> {stackAfter} (a potion drunk at a full vital is spent and restores nothing: capture.md C3)");
    }

    // ------------------------------------------------------------------------------------------------ what the rows read from the session

    private static bool HealHealth(IGameSessionView session, out uint current, out uint max) => session.State.Self.Qualities.TryGetVitalLevels(1, out current, out max);

    private static string HealVitalText(IGameSessionView session) => HealHealth(session, out var current, out var max) ? $"{current}/{max}" : "unknown";

    /// <summary>The first item of that weenie class in the player's main pack (a kit with no uses left is skipped), or 0.</summary>
    private static uint HealPackItem(IGameSessionView session, uint wcid)
    {
        foreach (var entry in session.State.Self.Inventory.Pack)
        {
            if (session.State.Objects.TryGet(entry.Guid, out var o) && o.Weenie.WeenieClassId == wcid && (wcid != HealKitWcid || o.Weenie.Structure is null or > 0))
            {
                return entry.Guid;
            }
        }

        return 0;
    }

    private static int HealStructureOf(IGameSessionView session, uint kit) => session.State.Objects.TryGet(kit, out var o) && o.Weenie.Structure is { } uses ? uses : -1;

    private static int HealStackOf(IGameSessionView session, uint guid)
    {
        if (session.State.Self.Inventory.TryGetStack(guid, out var size, out _))
        {
            return (int)size;
        }

        return session.State.Objects.TryGet(guid, out var o) ? (int)(o.Weenie.StackSize ?? 1u) : -1;
    }

    private static string DescribeHeal(HealHandle handle) =>
        (handle.Outcome is { } o ? o.Kind + (o.Code == 0 ? string.Empty : $" (code 0x{o.Code:X})") : "pending")
        + (handle.Result is { } r ? $" {r.Kind} {r.Amount}" + (r.Left == HealResult.Unknown ? string.Empty : $" left {r.Left}") : string.Empty);
}
