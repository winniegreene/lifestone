using System.Globalization;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests;
using Lifestone.Data.Dat;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M4 3.1 s4, the heal against the bundled ACE: a Handy Healing Kit used on the player and a Potion of Healing drunk, through <see cref="IActionApi"/> (<c>UseWithTarget</c> and <c>Drink</c>), and the untrained refusal.
/// It replaces the capture test of this item (<c>HealCaptureLiveTests</c>, which recorded ACE's raw order before the trackers existed; its findings are in <c>cache/m4/3-1/capture.md</c>). WRITTEN AND COMPILED BY THE SLICE, RUN BY THE
/// LEAD: it needs the backend with the Lifestone server mod ready, Holtburg Hunter and BSD Mage built (<c>--scenario=fighter-build</c> and the mage's build), the hunter holding a Handy Healing Kit 628 with uses left and a stack of Potions
/// of Healing 377 (the lead tops them up). Neither character is reset, purged or levelled by anything here except the hunt's own kills; the hunter ends changed only by kit uses, a potion and the health it lost.
/// <para>Run it alone with <c>dotnet test tests/Lifestone.Loopback.Tests --filter "FullyQualifiedName~HealLiveTests" --logger "console;verbosity=detailed"</c>; each test records its session to <c>cache/m4/3-1/live</c> of the main checkout
/// (derived data, gitignored). Test one takes about 5 to 9 minutes (the forest hunt of up to 150 s with its bind and wind-down), test two about 1 minute.</para>
/// </summary>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
public sealed class HealLiveTests(ITestOutputHelper output)
{
    private const uint KitWcid = 628;
    private const uint PotionWcid = 377;
    private const uint NotTrainedInHealing = 0x04FC;
    private const int PotionBoost = 25;
    private const string Item = "3-1";

    /// <summary>
    /// <b>Test one, Holtburg Hunter, one session, all through <see cref="IActionApi"/>.</b> (a) At the start, with the health full, a kit use at the full vital: the outcome is RECORDED (the capture did not reach it; expected a
    /// <see cref="ActionOutcomeKind.ServerError"/> with 028B 0x04FF and its text, or the UseDone's code), printed as <c>heal_full=</c>, and not asserted; a health that is not full prints <c>heal_full=n/a</c>. (b) The hunt for damage
    /// (the forest, <c>--hunt-ground forest</c>, up to 3 kills in 150 s, the recovery rule off, the retreat at 30% ends it): no creature found, or no damage taken, is recorded, (c) and (d) are skipped with the reason, and the test FAILS with a message starting <c>INCONCLUSIVE:</c> (review R18: a run that proved neither heal is not a pass). (c) Back at Holtburg,
    /// <c>UseWithTarget(kit, self)</c> once, and again while the health is under 90%: each Confirmed with a <see cref="HealResult"/> of <see cref="HealResultKind.Healed"/> or <see cref="HealResultKind.FailedRoll"/>, the kit's Structure down by
    /// one per Confirmed use, a Healed amount at most the health missing. (d) After the first kit use, while the health is under the maximum, <c>Drink(potion)</c>: Confirmed, a <see cref="HealResultKind.Drank"/> of 1 to 25, the stack down by one.
    /// Prints <c>heal_live kit_uses= healed= failed_rolls= potion_points= wasted= health_before=&lt;%&gt; health_after=&lt;%&gt; heal_full='...'</c> (and <c>skipped='...'</c> when (c) and (d) did not run).
    /// </summary>
    [LoopbackFact]
    public void Holtburg_Hunter_records_a_kit_at_a_full_vital_then_heals_after_the_forest_with_a_kit_and_a_potion()
    {
        using var dats = DatSet.Open();
        var huntLog = new RecordingLogSink();
        var leg = new HunterLeg(output.WriteLine, dats, huntLog);
        var result = LiveRun.Run(output, dats, leg, "heal-hunter", HuntLiveRun.QuitAfterSeconds, item: Item);
        foreach (var line in huntLog.Lines.Where(l => l.Contains("[hunt]", StringComparison.Ordinal) || l.Contains("WARN", StringComparison.Ordinal) || l.Contains("ERROR", StringComparison.Ordinal)))
        {
            output.WriteLine(line);
        }

        foreach (var round in leg.HuntLines)
        {
            output.WriteLine("heal_live hunt " + round);
        }

        var verdict = leg.VerdictLine();
        output.WriteLine(verdict);
        Assert.True(leg.Problems.Count == 0, "the checks that failed: " + string.Join("; ", leg.Problems) + " | " + verdict);
        Assert.True(result.Ok, result.Failure + " | " + verdict);

        // M4 3.1 fix, review R18: a run that proved neither heal live is not a pass. (a), the kit at a full vital, stays a recorded value; (c) and (d) are the proof DoD 9 names ("the fighter after hits, a kit use and a potion"), so a
        // forest round that found no creature or dealt no damage, or a health that was full again before the potion, fails the test as INCONCLUSIVE with the reason (the lead reruns it).
        if (leg.Skipped is not null)
        {
            Assert.Fail("INCONCLUSIVE: the kit heal (c) and the potion (d) were not exercised: " + leg.Skipped + " | " + verdict);
        }

        Assert.True(leg.Tally.KitUses >= 1, "INCONCLUSIVE: no kit use was Confirmed: " + verdict);
        if (leg.PotionSkipped is not null)
        {
            Assert.Fail("INCONCLUSIVE: the potion (d) was not exercised: " + leg.PotionSkipped + " | " + verdict);
        }

        Assert.True(leg.Tally.Potions >= 1, "INCONCLUSIVE: no potion was Confirmed: " + verdict);
    }

    /// <summary>
    /// <b>Test two, BSD Mage</b> (Healing untrained, M3 D19; no other character): a Handy Healing Kit from the <c>item</c> verb, <c>UseWithTarget</c> on itself through <see cref="IActionApi"/>: a
    /// <see cref="ActionOutcomeKind.ServerError"/> with 0x04FC (YouAreNotTrainedInHealing) and the kit's uses unchanged; then the kit is dropped and the pack's item count (and the count with the side packs' contents and the worn items)
    /// is as before. Prints <c>heal_mage outcome= code= pack_before= pack_after= held_before= held_after= kit_uses=</c>.
    /// </summary>
    [LoopbackFact]
    public void BSD_Mage_is_refused_a_kit_use_for_an_untrained_Healing_and_the_pack_is_as_before()
    {
        using var dats = DatSet.Open();
        var leg = new MageLeg(output.WriteLine, dats);
        var result = LiveRun.Run(output, dats, leg, "heal-mage", 300, item: Item);
        var verdict = leg.VerdictLine();
        output.WriteLine(verdict);
        Assert.True(leg.Problems.Count == 0, "the checks that failed: " + string.Join("; ", leg.Problems) + " | " + verdict);
        Assert.True(result.Ok, result.Failure + " | " + verdict);
        Assert.Equal(NotTrainedInHealing, leg.Code);
        Assert.Equal(leg.PackBefore, leg.PackAfter);
        Assert.Equal(leg.HeldBefore, leg.HeldAfter);
    }

    // ------------------------------------------------------------------------------------------------ the legs

    /// <summary>What both legs share: the entry as a named character of the test account, the model reads and the end of the session.</summary>
    private abstract class HealLeg(string name, Action<string> log, DatSet dats) : LiveLeg(name, [], log, dats)
    {
        private const double ArrivalSeconds = 90;

        protected uint SelfGuid => S.State.Self.Guid;

        protected bool InWorld => S.State.Phase == SessionPhase.InWorld && S.State.Self.Qualities.IsKnown;

        /// <summary>Logs in, chooses the character by name (never creates, purges or resets one), enters the world and waits for the arrival with the qualities known.</summary>
        protected IEnumerable<ActionHandle?> EnterCharacter(string characterName)
        {
            Stage = "login";
            var login = S.Actions.Login(ActionSource.Host);
            yield return login;
            Require(login.Outcome is { Kind: ActionOutcomeKind.Confirmed }, "login: " + Describe(login));
            Follow ??= new HuntFollow(Dats, new RecordingLogSink());

            Stage = "choosing";
            CharacterSlot? slot = S.State.Characters.Entries.OrderBy(c => c.PendingDeletion).ThenBy(c => c.Guid).Cast<CharacterSlot?>().FirstOrDefault(c => string.Equals(c!.Value.Name, characterName, StringComparison.OrdinalIgnoreCase));
            Require(slot is { PendingDeletion: false }, $"'{characterName}' is not on the account (or is pending deletion): build it first; this test never creates or purges a character");
            Stage = "entering";
            var enter = S.Actions.EnterWorld(slot!.Value.Guid, ActionSource.Host);
            yield return enter;
            Require(enter.Outcome is { Kind: ActionOutcomeKind.Confirmed }, $"entering the world as {characterName}: " + Describe(enter));
            Stage = "arriving";
            var since = S.Clock.Now;
            while (S.State.Self.Travel != SelfTravel.Present || !S.State.Self.Qualities.IsKnown)
            {
                Require(S.Clock.Now - since <= ArrivalSeconds, "arriving: the player was not unhidden with its qualities within " + ArrivalSeconds + " s");
                yield return null;
            }

            Stage = "running";
            Log("arrived as " + characterName);
        }

        protected bool Health(out uint current, out uint max) => S.State.Self.Qualities.TryGetVitalLevels(1, out current, out max);

        protected bool HealthFull() => Health(out var current, out var max) && max > 0 && current >= max;

        protected double? HealthShare() => Health(out var current, out var max) && max > 0 ? (double)current / max : null;

        protected string HealthText() => Health(out var current, out var max) ? $"{current}/{max}" : "unknown";

        protected static string Pct(double? share) => share is { } s ? (s * 100).ToString("0.0", CultureInfo.InvariantCulture) : "n/a";

        /// <summary>The guids of the held items of that weenie class in the main pack, in the pack's order.</summary>
        protected List<uint> InPack(uint wcid)
        {
            var list = new List<uint>();
            foreach (var entry in S.State.Self.Inventory.Pack)
            {
                if (S.State.Objects.TryGet(entry.Guid, out var o) && o.Weenie.WeenieClassId == wcid)
                {
                    list.Add(entry.Guid);
                }
            }

            return list;
        }

        protected int? Structure(uint guid) => S.State.Objects.TryGet(guid, out var o) && o.Weenie.Structure is { } s ? s : null;

        /// <summary>The first Handy Healing Kit in the pack that has uses left, or 0.</summary>
        protected uint PickKit() => InPack(KitWcid).FirstOrDefault(k => Structure(k) is null or > 0);

        /// <summary>The held stack of Potions of Healing (the first in the pack), its guid and size, or 0 and -1.</summary>
        protected (uint Guid, int Size) PotionStack()
        {
            var guid = InPack(PotionWcid).FirstOrDefault();
            return guid == 0 ? (0u, -1) : (guid, StackOf(guid));
        }

        protected static string Outcome(ActionHandle handle) => handle.Outcome is { } o ? $"{o.Kind}/0x{o.Code:X4}/{o.Detail ?? "none"}" : "unsettled";

        protected IEnumerable<ActionHandle?> EndSession()
        {
            if (S.State.Phase != SessionPhase.InWorld)
            {
                Log("not in the world: nothing to log off");
                yield break;
            }

            foreach (var h in LogOffAndQuit())
            {
                yield return h;
            }
        }
    }

    /// <summary>Test one: the full-vital kit use, the forest hunt for damage, the kit and the potion at Holtburg.</summary>
    private sealed class HunterLeg(Action<string> log, DatSet dats, RecordingLogSink huntLog) : HealLeg("heal-hunter", log, dats)
    {
        private readonly List<string> _huntLines = [];
        private HuntScenario? _lastHunt;
        private int _potionPoints;
        private double? _before;
        private double? _after;
        private string _full = "n/a";

        /// <summary>The counters of the heals this leg Confirmed (<see cref="HealTally.Record"/>).</summary>
        public HealTally Tally { get; } = new();

        /// <summary>The hunt round's verdict and detail fields, printed after the session.</summary>
        public IReadOnlyList<string> HuntLines => _huntLines;

        /// <summary>Why (c) and (d) did not run (no creature found, or no damage taken), or null when they ran.</summary>
        public string? Skipped { get; private set; }

        /// <summary>Why (d) did not run after (c) did (the health was full after the kit use), or null.</summary>
        public string? PotionSkipped { get; private set; }

        public string VerdictLine() =>
            string.Create(
                CultureInfo.InvariantCulture,
                $"heal_live kit_uses={Tally.KitUses} healed={Tally.Heals} failed_rolls={Tally.FailedRolls} potion_points={_potionPoints} wasted={Tally.Wasted} health_before={Pct(_before)} health_after={Pct(_after)} heal_full='{_full}'{(Skipped is null ? string.Empty : $" skipped='{Skipped}'")}{(PotionSkipped is null ? string.Empty : $" potion_skipped='{PotionSkipped}'")}");

        protected override IEnumerable<ActionHandle?> Script()
        {
            foreach (var h in EnterCharacter(FighterTemplate.CharacterName))
            {
                yield return h;
            }

            foreach (var h in Pause(3)) // the arrival's own messages settle
            {
                yield return h;
            }

            Log($"health {HealthText()}; kits {InPack(KitWcid).Count}; potion stack {PotionStack().Size}");

            // (a) a kit use at the full vital: recorded, not asserted (the capture did not reach it).
            if (HealthFull())
            {
                var kit = PickKit();
                if (Check(kit != 0, "no Handy Healing Kit 628 with uses left in the pack (the lead tops the hunter's kit up)"))
                {
                    var full = S.Actions.UseWithTarget(kit, SelfGuid, ActionSource.Host);
                    yield return full;
                    Tally.Record(full);
                    _full = Outcome(full);
                    Log($"(a) a kit use at the full vital {HealthText()}: {_full}; result {full.Result?.Kind.ToString() ?? "none"}; kit uses {Structure(kit)?.ToString(CultureInfo.InvariantCulture) ?? "?"}");
                }
            }
            else
            {
                Log($"(a) skipped: the health is not full ({HealthText()}): heal_full=n/a");
            }

            // (b) the hunt for damage.
            foreach (var h in HuntForDamage())
            {
                yield return h;
            }

            if (!InWorld || _lastHunt is not { Fighter: "ok" })
            {
                Check(false, $"the hunter is not in the world as the fighter after the hunt (phase {S.State.Phase}, fighter {_lastHunt?.Fighter ?? "?"}, failure {_lastHunt?.Failure ?? "none"})");
                foreach (var h in EndSession())
                {
                    yield return h;
                }

                yield break;
            }

            foreach (var h in Pause(2)) // the return and the last vitals settle
            {
                yield return h;
            }

            var kills = _lastHunt.Tally.Kills;
            var taken = _lastHunt.Tally.DamageTaken;
            Log($"after the hunt: kills {kills}, damage taken {taken}, health {HealthText()}, returned {_lastHunt.Returned}, failure {_lastHunt.Failure ?? "none"}");
            if (HealthFull())
            {
                Skipped = _lastHunt.Failure is not null && kills == 0 && taken == 0
                    ? $"no creature was found or fought ({_lastHunt.Failure}); the health is full, so (c) and (d) are skipped"
                    : $"no damage came (damage taken {taken}, kills {kills}); the health is full, so (c) and (d) are skipped";
                Log("SKIPPED (c) and (d): " + Skipped);
            }
            else
            {
                foreach (var h in HealAtHoltburg())
                {
                    yield return h;
                }
            }

            _after = HealthShare();
            foreach (var h in EndSession())
            {
                yield return h;
            }
        }

        /// <summary>One forest round for damage, as a step (the recovery rule off so nothing heals it, the corpses not opened), through the hunt's own wind-down back to Holtburg; the session stays open.</summary>
        private IEnumerable<ActionHandle?> HuntForDamage()
        {
            var heights = new Dictionary<uint, float?>();
            var options = new HuntOptions
            {
                Kills = 3,
                Seconds = 150,
                Weapon = HuntWeapon.Melee,
                Ground = HuntGroundChoice.Forest,
                OpenCorpse = false,
                Recovery = new RecoveryOptions { Enabled = false },
            };
            Follow?.Dispose();
            Follow = new HuntFollow(Dats, huntLog);
            FollowIsTheHunts = true;
            var hunt = new HuntScenario(
                options,
                huntLog,
                cell =>
                {
                    if (!heights.TryGetValue(cell.CellId, out var height))
                    {
                        heights[cell.CellId] = height = HuntGround.TerrainHeight(Dats, cell);
                    }

                    return height;
                },
                options.GroundRow,
                endSession: false,
                follow: Follow);
            _lastHunt = hunt;
            Stage = "hunting";
            Log($"(b) the hunt for damage: {options}");
            for (;;)
            {
                if (hunt.Step(S, Now) != ScenarioStatus.Running)
                {
                    break;
                }

                yield return null;
            }

            Stage = "running";
            FollowIsTheHunts = false;
            Follow = new HuntFollow(Dats, new RecordingLogSink()); // the hunt disposed its own; the leg keeps the client's body following ACE for the rest of the session
            _huntLines.Add($"verdict {hunt.VerdictFields()} | detail {hunt.DetailFields()} | failure={hunt.Failure ?? "none"}");
        }

        /// <summary>(c) and (d): at Holtburg, the kit on the player once, the potion while the health is under the maximum, a second kit use while it is under 90%.</summary>
        private IEnumerable<ActionHandle?> HealAtHoltburg()
        {
            _before = HealthShare();
            Log($"(c) health before the heals: {HealthText()} ({Pct(_before)}%)");
            for (var use = 1; use <= 2; use++)
            {
                var kit = PickKit();
                if (!Check(kit != 0, $"kit use {use}: no Handy Healing Kit with uses left in the pack"))
                {
                    break;
                }

                Health(out var currentBefore, out var maxBefore);
                var missing = maxBefore - currentBefore;
                var usesBefore = Structure(kit);
                var healthBefore = HealthText();
                var heal = S.Actions.UseWithTarget(kit, SelfGuid, ActionSource.Host);
                yield return heal;
                Tally.Record(heal);
                var result = heal.Result;
                var usesAfter = S.State.Objects.TryGet(kit, out _) ? Structure(kit) : null;
                Log($"kit use {use}: {Describe(heal)}; result {result?.Kind.ToString() ?? "none"} amount {result?.Amount.ToString(CultureInfo.InvariantCulture) ?? "-"}; health {healthBefore} -> {HealthText()}; uses {usesBefore?.ToString(CultureInfo.InvariantCulture) ?? "?"} -> {usesAfter?.ToString(CultureInfo.InvariantCulture) ?? "gone"}");
                if (Confirmed(heal, $"kit use {use}"))
                {
                    Check(result is { Kind: HealResultKind.Healed or HealResultKind.FailedRoll }, $"kit use {use}: the result is {result?.Kind.ToString() ?? "none"}, not Healed or FailedRoll");
                    Check(usesBefore is null || usesAfter == usesBefore - 1, $"kit use {use}: the kit's Structure went {usesBefore?.ToString(CultureInfo.InvariantCulture) ?? "?"} -> {usesAfter?.ToString(CultureInfo.InvariantCulture) ?? "gone"}, not down by one");
                    if (result is { Kind: HealResultKind.Healed } healed)
                    {
                        Check(healed.Amount > 0 && healed.Amount <= missing, $"kit use {use}: healed {healed.Amount} of {missing} missing");
                    }
                }

                if (use == 1)
                {
                    foreach (var h in DrinkWhileUnderTheMaximum())
                    {
                        yield return h;
                    }
                }

                if (HealthShare() is not { } share || share >= 0.9)
                {
                    break;
                }
            }
        }

        private IEnumerable<ActionHandle?> DrinkWhileUnderTheMaximum()
        {
            if (HealthFull())
            {
                PotionSkipped = $"the health is full ({HealthText()}) after the kit use, so there is no drink while under the maximum";
                Log("(d) skipped: " + PotionSkipped);
                yield break;
            }

            var (potion, size) = PotionStack();
            if (!Check(potion != 0, "(d) no Potion of Healing 377 in the pack (the lead tops the hunter's potions up)"))
            {
                yield break;
            }

            var healthBefore = HealthText();
            var drink = S.Actions.Drink(potion, ActionSource.Host);
            yield return drink;
            Tally.Record(drink);
            var result = drink.Result;
            var stackAfter = S.State.Self.Inventory.Contains(potion) ? StackOf(potion) : 0;
            Log($"(d) Drink: {Describe(drink)}; result {result?.Kind.ToString() ?? "none"} amount {result?.Amount.ToString(CultureInfo.InvariantCulture) ?? "-"}; health {healthBefore} -> {HealthText()}; stack {size} -> {stackAfter}");
            if (Confirmed(drink, "Drink"))
            {
                Check(result is { Kind: HealResultKind.Drank, Amount: > 0 and <= PotionBoost }, $"Drink: the result is {result?.Kind.ToString() ?? "none"} {result?.Amount.ToString(CultureInfo.InvariantCulture) ?? "-"}, not a Drank of 1 to {PotionBoost}");
                Check(stackAfter == size - 1, $"Drink: the stack went {size} -> {stackAfter}, not down by one");
                if (result is { Kind: HealResultKind.Drank } drank)
                {
                    _potionPoints += drank.Amount;
                }
            }
        }
    }

    /// <summary>Test two: BSD Mage, a kit from the item verb, the refusal, the kit dropped.</summary>
    private sealed class MageLeg(Action<string> log, DatSet dats) : HealLeg("heal-mage", log, dats)
    {
        /// <summary>The UseDone code the refusal carried (0 when none).</summary>
        public uint Code { get; private set; }

        public int PackBefore { get; private set; } = -1;

        public int PackAfter { get; private set; } = -1;

        public int HeldBefore { get; private set; } = -1;

        public int HeldAfter { get; private set; } = -1;

        private string _outcome = "none";
        private string _uses = "n/a";

        public string VerdictLine() =>
            string.Create(CultureInfo.InvariantCulture, $"heal_mage outcome='{_outcome}' code=0x{Code:X4} pack_before={PackBefore} pack_after={PackAfter} held_before={HeldBefore} held_after={HeldAfter} kit_uses={_uses}");

        protected override IEnumerable<ActionHandle?> Script()
        {
            foreach (var h in EnterCharacter(MageTemplate.CharacterName))
            {
                yield return h;
            }

            foreach (var h in Pause(3))
            {
                yield return h;
            }

            PackBefore = S.State.Self.Inventory.Pack.Count;
            HeldBefore = S.State.Self.Inventory.Count;
            Log($"the mage's pack before: {PackBefore} items in the main pack, {HeldBefore} held in all");
            var spawn = S.Tools.SpawnItem(KitWcid, null, ActionSource.Host);
            yield return spawn;
            if (Check(spawn.Result is not null, "the Handy Healing Kit grant failed: " + Describe(spawn)))
            {
                var kit = spawn.Result!.Guid;
                foreach (var h in WaitHeld(kit, "the kit"))
                {
                    yield return h;
                }

                if (Check(S.State.Self.Inventory.Contains(kit), "the kit is not in the mage's pack after the grant"))
                {
                    foreach (var h in Pause(1)) // the grant's own messages settle before the use
                    {
                        yield return h;
                    }

                    var usesBefore = Structure(kit);
                    var use = S.Actions.UseWithTarget(kit, SelfGuid, ActionSource.Host);
                    yield return use;
                    _outcome = Outcome(use);
                    Code = use.Outcome?.Code ?? 0;
                    _uses = Structure(kit)?.ToString(CultureInfo.InvariantCulture) ?? "?";
                    Log($"UseWithTarget on itself with Healing untrained: {_outcome}; kit uses {usesBefore?.ToString(CultureInfo.InvariantCulture) ?? "?"} -> {_uses}");
                    Check(use.Outcome is { Kind: ActionOutcomeKind.ServerError, Code: NotTrainedInHealing }, $"the use was not refused with 0x{NotTrainedInHealing:X4} (YouAreNotTrainedInHealing): {Describe(use)}");
                    Check(use.Result is null, "an untrained use carried a HealResult: " + use.Result);
                    Check(usesBefore == Structure(kit), $"the kit's uses changed: {usesBefore?.ToString(CultureInfo.InvariantCulture) ?? "?"} -> {_uses}");

                    foreach (var h in Pause(1))
                    {
                        yield return h;
                    }

                    var drop = S.Actions.Drop(kit, ActionSource.Host);
                    yield return drop;
                    Log("Drop: " + Describe(drop));
                    foreach (var h in Until(() => !S.State.Self.Inventory.Contains(kit), 5))
                    {
                        yield return h;
                    }

                    Check(!S.State.Self.Inventory.Contains(kit), "the kit is still in the mage's pack after the drop");
                    foreach (var h in Pause(2)) // the 019A and the ground object settle
                    {
                        yield return h;
                    }

                    PackAfter = S.State.Self.Inventory.Pack.Count;
                    HeldAfter = S.State.Self.Inventory.Count;
                    Log($"the mage's pack after: {PackAfter} items in the main pack, {HeldAfter} held in all (before: {PackBefore} and {HeldBefore})");
                    Check(PackAfter == PackBefore && HeldAfter == HeldBefore, $"the pack is not as before: {PackBefore} -> {PackAfter} in the main pack, {HeldBefore} -> {HeldAfter} held in all");
                }
            }

            foreach (var h in EndSession())
            {
                yield return h;
            }
        }
    }
}
