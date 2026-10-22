using System.Globalization;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 3.1 s4: the offline heal rows of <c>--play-checks</c> (<c>PlayChecks.Heal.cs</c>: <c>heal-potion</c>, <c>heal-kit</c>, <c>heal-potion-at-full-vital</c>) proved here without Godot, so a row that cannot hold fails in the
/// fast tests and not only in the slow check. The rig is the replay visit's player (<c>PlayChecks.ReplayVisit</c>): the play checks' own synthetic description (their enchantments, spells and experience) with the health it STARTS with
/// (5, a placeholder: <c>TODO(m4-standin-start-vitals)</c>), a kit and five potions announced (<c>HealItems</c>), no creature (the owner's rule, m4-plan.md D23). The three rows run in the check's order on the one session, through
/// <see cref="IActionApi"/>, each judged on the session's state (the handle's outcome and result, the object table's Structure and stack, the player's vital), and the conditions are the rows' own. The game's rows wait in real time
/// where this runs the session's clock; nothing else differs.
/// </summary>
public sealed class HealPlayCheckRowsTests(ITestOutputHelper output)
{
    private const uint Kit = StandInWorld.HealKitGuid;
    private const uint Potions = StandInWorld.HealPotionGuid;
    private const int Boost = 25;

    /// <summary>The replay visit's player as <c>PlayChecks.ReplayVisit</c> builds it from <c>PlayChecks.Visit.Stats</c>: the same description, the health it starts with 5.</summary>
    private static StandInVisit ReplayLike() => new()
    {
        EnterAfter = 0.2,
        UnhideAfter = 1.0,
        HealItems = true,
        StartVitalsPlaceholder = true,
        Stats = new StandInPlayerStats
        {
            Enchantments = StandInPlayerStats.LiveShapedEnchantments(),
            Level = 1,
            TotalExperience = 2_000_000,
            AvailableExperience = 2_000_000,
            Spells = StandInPlayerStats.ProbeSpells(),
            SpellbookFilters = StandInPlayerStats.ProbeSpellbookFilters,
            ExtraEnchantments = StandInPlayerStats.ProbeEnchantments(),
            Health = 5,
        },
    };

    private static bool Health(CastRig rig, out uint current, out uint max) => rig.Q.TryGetVitalLevels(1, out current, out max);

    private static int Structure(CastRig rig, uint item) => rig.Session.State.Objects.TryGet(item, out var o) && o.Weenie.Structure is { } uses ? uses : -1;

    private static int Stack(CastRig rig, uint item) => rig.Session.State.Self.Inventory.TryGetStack(item, out var size, out _) ? (int)size : rig.Session.State.Objects.TryGet(item, out var o) ? (int)(o.Weenie.StackSize ?? 1u) : -1;

    private static void Wait(CastRig rig, ActionHandle handle, double seconds = 25)
    {
        for (var t = 0.0; t < seconds && !handle.IsCompleted; t += 0.05)
        {
            rig.Run(0.05);
        }
    }

    private static string Describe(HealHandle h) => (h.Outcome is { } o ? o.Kind + (o.Code == 0 ? string.Empty : $" 0x{o.Code:X}") : "pending") + (h.Result is { } r ? $" {r.Kind} {r.Amount} left {r.Left}" : string.Empty);

    [Fact]
    public void The_replay_visit_logs_its_start_health_placeholder_once_and_the_game_file_sets_the_flag_3_1_fix_R24()
    {
        using var rig = new CastRig(ReplayLike());
        rig.Begin();

        var lines = rig.Log.Lines.Where(l => l.Contains("TODO(m4-standin-start-vitals)", StringComparison.Ordinal)).ToList();
        var line = Assert.Single(lines);
        Assert.Contains("the player starts with 5 health, a placeholder of the heal rows", line, StringComparison.Ordinal);
        Assert.Contains("not on the machine's recorded character", line, StringComparison.Ordinal);

        // A visit that does not ask (the other rows' Visit, the hunt tests') logs nothing.
        using var plain = new CastRig(new StandInVisit { EnterAfter = 0.2, UnhideAfter = 1.0, HealItems = true });
        plain.Begin();
        Assert.DoesNotContain(plain.Log.Lines, l => l.Contains("m4-standin-start-vitals", StringComparison.Ordinal));

        // The game's replay visit sets the flag (the Game project is not referenced here: the source is read).
        var source = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "PlayChecks.cs"));
        Assert.Contains("StartVitalsPlaceholder = true,", source, StringComparison.Ordinal);
        Assert.Contains("Stats = Visit.Stats! with { Health = 5 },", source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_three_heal_rows_hold_in_the_checks_order_on_the_replay_visit_s_player()
    {
        using var rig = new CastRig(ReplayLike());
        rig.Begin();
        var self = rig.Session.State.Self.Guid;
        Assert.True(Health(rig, out var start, out var startMax));
        output.WriteLine($"the player starts at health {start}/{startMax}");
        Assert.True(start < startMax, "the replay visit's player starts with partial health");
        Assert.True(rig.Session.State.Self.Inventory.Find(Kit).Held);
        Assert.True(rig.Session.State.Self.Inventory.Find(Potions).Held);

        // heal-potion: one drink at a partial vital.
        var stackBefore = Stack(rig, Potions);
        var drink = rig.Session.Actions.Drink(Potions, ActionSource.Host);
        Wait(rig, drink);
        Health(rig, out var afterDrink, out _);
        var drank = drink.Result;
        output.WriteLine($"heal-potion: {Describe(drink)} health {start} -> {afterDrink} stack {stackBefore} -> {Stack(rig, Potions)}");
        Assert.True(drink.IsCompleted && drink.Outcome is { Kind: ActionOutcomeKind.Confirmed }, Describe(drink));
        Assert.True(drank is { Kind: HealResultKind.Drank, Amount: > 0 and <= Boost }, Describe(drink));
        Assert.True(drank!.Amount <= startMax - start, "the restore is at most the missing health");
        Assert.Equal(drank.Amount, (long)afterDrink - start);
        Assert.Equal(stackBefore - 1, Stack(rig, Potions));

        // heal-kit: two uses queued at once.
        Assert.True(Health(rig, out var before, out var healthMax));
        var usesBefore = Structure(rig, Kit);
        Assert.True(before < healthMax && usesBefore > 1);
        var first = rig.Session.Actions.UseWithTarget(Kit, self, ActionSource.Host);
        var second = rig.Session.Actions.UseWithTarget(Kit, self, ActionSource.Host);
        Wait(rig, first);
        var secondWaited = !second.IsCompleted;
        Health(rig, out var afterFirst, out _);
        var usesAfterFirst = Structure(rig, Kit);
        Wait(rig, second);
        Health(rig, out var afterSecond, out _);
        var usesAfterSecond = Structure(rig, Kit);
        output.WriteLine($"heal-kit: first {Describe(first)} health {before}/{healthMax} -> {afterFirst} uses {usesBefore} -> {usesAfterFirst}; second waited={secondWaited} sent {(second.SentAt - first.CompletedAt)?.ToString("0.00", CultureInfo.InvariantCulture)} s after the first's UseDone: {Describe(second)} health -> {afterSecond} uses -> {usesAfterSecond}");
        Assert.True(first.IsCompleted && first.Outcome is { Kind: ActionOutcomeKind.Confirmed }, Describe(first));
        Assert.True(first.Result is { Kind: HealResultKind.Healed or HealResultKind.FailedRoll }, Describe(first));
        Assert.Equal(usesBefore - 1, usesAfterFirst);
        if (first.Result!.Kind == HealResultKind.FailedRoll)
        {
            Assert.Equal(before, afterFirst);
        }
        else
        {
            Assert.True(first.Result.Amount > 0 && first.Result.Amount <= healthMax - before);
            Assert.Equal(first.Result.Amount, (long)afterFirst - before);
        }

        Assert.True(secondWaited, "the second heal is still waiting for the lane when the first completes");
        Assert.True(second.IsCompleted, Describe(second));
        Assert.True(second.SentAt is { } sent && first.CompletedAt is { } finished && sent >= finished, "the second was sent after the first's UseDone");
        Assert.True(second.SentAt - first.CompletedAt >= HealLaneTiming.MarginSeconds - 1e-6, $"the second was sent at least the lane's margin ({HealLaneTiming.MarginSeconds} s) after the first's UseDone, not at it (3.1 fix R25): {second.SentAt - first.CompletedAt}");
        if (second.Outcome is { Kind: ActionOutcomeKind.Confirmed })
        {
            Assert.True(second.Result is { Kind: HealResultKind.Healed or HealResultKind.FailedRoll }, Describe(second));
            Assert.Equal(usesAfterFirst - 1, usesAfterSecond);
            Assert.Equal(second.Result!.Kind == HealResultKind.FailedRoll ? afterFirst : afterFirst + (uint)second.Result.Amount, afterSecond);
            Assert.True(afterSecond <= healthMax);
        }
        else
        {
            Assert.True(second.Outcome is { Kind: ActionOutcomeKind.ServerError } && afterFirst >= healthMax && afterSecond == afterFirst && usesAfterSecond == usesAfterFirst, "a refused second heal is the full vital: " + Describe(second));
        }

        // heal-potion-at-full-vital: top up with kit uses, then drink at the full vital.
        var uses = 0;
        while (uses < 10 && Health(rig, out var current, out var max) && current < max)
        {
            Wait(rig, rig.Session.Actions.UseWithTarget(Kit, self, ActionSource.Host));
            uses++;
        }

        Assert.True(Health(rig, out var full, out var fullMax) && full >= fullMax, $"the vital is full after {uses} top-up uses: {full}/{fullMax}");
        var stackAtFull = Stack(rig, Potions);
        var waste = rig.Session.Actions.Drink(Potions, ActionSource.Host);
        Wait(rig, waste);
        Health(rig, out var afterWaste, out _);
        output.WriteLine($"heal-potion-at-full-vital: after {uses} top-up kit uses {Describe(waste)} health {full}/{fullMax} -> {afterWaste} stack {stackAtFull} -> {Stack(rig, Potions)}");
        Assert.True(waste.IsCompleted && waste.Outcome is { Kind: ActionOutcomeKind.Confirmed }, Describe(waste));
        Assert.True(waste.Result is { Kind: HealResultKind.Wasted, Amount: 0 }, Describe(waste));
        Assert.Equal(full, afterWaste);
        Assert.Equal(stackAtFull - 1, Stack(rig, Potions));
    }
}
