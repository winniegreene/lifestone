using System.Globalization;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;
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
/// (derived data, gitignored). Test two takes about 1 minute; the harm-heal fact (M4 P5a C3: the Brawler's and the Archer's are in their own classes below) about 2 to 4 minutes. The Brawler's belt hunt of M4 P4 is gone: Harm Self gives the damage.</para>
/// </summary>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test")]
[LoopbackCharacter("BSD Mage")]
[LiveSharedSession]
public sealed class HealLiveTests(ITestOutputHelper output)
{
    private const uint KitWcid = 628;
    private const uint PotionWcid = 377;
    private const uint NotTrainedInHealing = 0x04FC;

    internal const string Item = "3-1";

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
        var result = LiveRun.Run(output, leg, "heal-mage", 300, item: Item, character: MageTemplate.CharacterName);
        var verdict = leg.VerdictLine();
        output.WriteLine(verdict);
        Assert.True(leg.Problems.Count == 0, "the checks that failed: " + string.Join("; ", leg.Problems) + " | " + verdict);
        Assert.True(result.Ok, result.Failure + " | " + verdict);
        Assert.Equal(NotTrainedInHealing, leg.Code);
        Assert.Equal(leg.PackBefore, leg.PackAfter);
        Assert.Equal(leg.HeldBefore, leg.HeldAfter);
    }

    /// <summary>
    /// <b>BSD Mage (test), the caster path of the harm-heal script, no swap.</b> The mage wields its wand, so the script harms it to about half with Harm Self (<see cref="HarmSelfPlan"/>), casts Heal Self (the highest known level at the chance, VII for the mage) and
    /// lets the vitals keeper finish. Proves Harm Self and Heal Self VII on the mage (the build must have learned Harm Self: a rerun of the mage build does it), that no kit or potion is used (<c>kits=0 potions=0</c>, the mage's Healing is untrained), that no swap runs
    /// (<c>swapped=no</c>). Printed, not asserted, in this first gate (P5a-R9): that the health went to about half and never to 0 (<c>health_min_pct</c>), the harms landed, and that it ends at or over the keeper's start share (<c>health_end_pct</c>).
    /// </summary>
    [LoopbackFact]
    public void BSD_Mage_harms_itself_to_half_then_casts_Heal_Self_and_the_keeper_restores_it()
    {
        var leg = HarmHealFacts.Run(output, "harm-heal-mage", MageTemplate.CharacterName);
        var s = leg.Harm!;
        var verdict = leg.VerdictLine();
        Assert.True(s.OwnHealCasts >= 1, "no Heal Self raised the health: " + verdict);
        Assert.False(s.Swapped, "the mage swaps nothing: " + verdict);
        Assert.Equal(0, s.Kits);
        Assert.Equal(0, s.Potions);
        HarmHealFacts.Report(output, s); // report-only in this gate (M4 P5a fix F-abil, P5a-R9): harms, health_min_pct, health_end_pct, transfers, resist
    }

    // ------------------------------------------------------------------------------------------------ the legs

    /// <summary>What both legs share: the entry as a named character of the test account, the model reads and the end of the session.</summary>
    internal abstract class HealLeg(string name, Action<string> log, DatSet dats) : LiveLeg(name, [], log, dats)
    {
        private const double ArrivalSeconds = 90;

        protected uint SelfGuid => S.State.Self.Guid;

        protected bool InWorld => S.State.Phase == SessionPhase.InWorld && S.State.Self.Qualities.IsKnown;

        /// <summary>Logs in, chooses the character by name (never creates, purges or resets one), enters the world and waits for the arrival with the qualities known.</summary>
        protected IEnumerable<ActionHandle?> EnterCharacter(string characterName)
        {
            if (AlreadyInWorld)
            {
                Stage = "running";
                Follow ??= new HuntFollow(Dats, new RecordingLogSink());
                Log("already in the world: the shared session's entry logged the character in");
                yield break;
            }

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

    /// <summary>
    /// The harm-heal leg (M4 P5a lane C3, design section 3.3): the station script P5b moves into the client as a <c>ScriptLeg</c>. It enters the character (a shared session's entry, no teleport: the leg runs wherever the character stands, in peace),
    /// tops up what its means need with the <c>item</c> verb (a fighter's kit and potion, reported <c>granted=</c>; an item, D23), builds the keeper and the caster swap the character's name asks for and steps one
    /// <see cref="HarmHealScript"/> until it ends: harm to half (<see cref="HarmSelfPlan"/>: never a cast that could reach 0; fizzles counted, a 60 s cap), a kit, harm, a potion, <see cref="VitalsKeeper"/> restoring the health, one swap in and one out for a
    /// fighter; the mage (no swap) harms, casts Heal Self and lets the keeper finish. The engine is in <c>Lifestone.Client</c> so the default tier proves its order; this class only hosts it, so moving it is a rename.
    /// </summary>
    internal sealed class HarmHealLeg(Action<string> log, DatSet dats, string characterName, RecordingLogSink scriptLog) : HealLeg("harm-heal", log, dats)
    {
        private HarmHealScript? _harm;
        private int _granted;

        /// <summary>The script (null before the leg reached it).</summary>
        public HarmHealScript? Harm => _harm;

        /// <summary>The items the leg granted with the item verb (a kit or a potion an earlier test used up).</summary>
        public int Granted => _granted;

        public string VerdictLine() => (_harm?.VerdictLine() ?? "harm-heal done ok=false why=not-started") + " granted=" + _granted.ToString(CultureInfo.InvariantCulture);

        protected override IEnumerable<ActionHandle?> Script()
        {
            foreach (var h in EnterCharacter(characterName))
            {
                yield return h;
            }

            foreach (var h in Pause(3)) // the arrival's own messages settle
            {
                yield return h;
            }

            var swap = CasterSwap.ForCharacter(characterName, S.Actions, S.State, scriptLog);
            if (swap is not null)
            {
                // The fighters' kit holds 2 Handy Healing Kits and 5 Potions of Healing; the hunts and heals of earlier tests use them, so the leg grants what it needs (an item, D23) and says so.
                if (PickKit() == 0)
                {
                    _granted++;
                }

                foreach (var h in GrantIfMissing(PickKit, KitWcid, "Handy Healing Kit 628 with uses left", _ => { })) { yield return h; }

                if (PotionStack().Guid == 0)
                {
                    _granted++;
                }

                foreach (var h in GrantIfMissing(() => PotionStack().Guid, PotionWcid, "Potion of Healing 377", _ => { })) { yield return h; }
            }

            var spells = SpellTableData.Load(Dats).Spells;
            var keeper = new VitalsKeeper(S, spells, swap is null ? VitalsOptions.Default : VitalsOptions.Default.ForFighter(), scriptLog); // M4 P5a C7: a caster swap is a fighter: its mana order is a Mana Potion, then a rest
            _harm = new HarmHealScript(S, spells, keeper, swap, new HarmHealOptions(), scriptLog);
            Stage = "harm-heal";
            Log($"health {HealthText()}; kits {InPack(KitWcid).Count}; potion stack {PotionStack().Size}; {(swap is null ? "no swap (the caster wields its wand)" : "the caster swap runs once around the casts")}");
            while (_harm.Step(Now) == HarmHealState.Running)
            {
                yield return null;
            }

            Stage = "running";
            Log(_harm.VerdictLine());
            Check(_harm.State == HarmHealState.Done, "the harm-heal script ended " + _harm.State + " why=" + (_harm.Why ?? "none"));
            foreach (var h in EndSession())
            {
                yield return h;
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

/// <summary>What the harm-heal facts share (M4 P5a lane C3): runs the leg, prints the script's log and the verdict, and asserts the common facts.</summary>
internal static class HarmHealFacts
{
    /// <summary>Runs <see cref="HealLiveTests.HarmHealLeg"/> for <paramref name="character"/>, prints the script's log lines and the verdict line, and asserts that the leg and the session ended well.</summary>
    internal static HealLiveTests.HarmHealLeg Run(ITestOutputHelper output, string name, string character)
    {
        using var dats = DatSet.Open();
        var scriptLog = new RecordingLogSink();
        var leg = new HealLiveTests.HarmHealLeg(output.WriteLine, dats, character, scriptLog);
        var result = LiveRun.Run(output, leg, name, 900, item: HealLiveTests.Item, character: character);
        foreach (var line in scriptLog.Lines.Where(l => l.Contains("harm-heal", StringComparison.Ordinal) || l.Contains("vitals", StringComparison.Ordinal) || l.Contains("swap", StringComparison.Ordinal) || l.Contains("WARN", StringComparison.Ordinal) || l.Contains("ERROR", StringComparison.Ordinal)))
        {
            output.WriteLine(line);
        }

        var verdict = leg.VerdictLine();
        output.WriteLine(verdict);
        Assert.True(leg.Problems.Count == 0, "the checks that failed: " + string.Join("; ", leg.Problems) + " | " + verdict);
        Assert.True(result.Ok, result.Failure + " | " + verdict);
        Assert.NotNull(leg.Harm);
        Assert.True(leg.Harm.State == HarmHealState.Done, "harm-heal did not end Done: " + verdict);
        return leg;
    }

    /// <summary>
    /// The facts a fighter's leg proves (M4 P5a fix F-abil): the slice table's <c>heals&gt;=1 potions&gt;=1 swapped=yes</c> and the heal checks the old Brawler fact had and the harm-heal leg lost (P5a-R6, D28), now made by <see cref="HarmHealScript"/> itself and read here as
    /// <see cref="HarmHealScript.Anomalies"/> being empty: each kit use's Healed amount is over 0 and at most the health missing and its Structure goes down by one, the potion's Drank amount is 1 to 25 and its stack goes down by one. Everything else the verdict
    /// says is printed by <see cref="Report"/> and not asserted in this first gate (P5a-R9).
    /// </summary>
    internal static void AssertFighter(ITestOutputHelper output, HealLiveTests.HarmHealLeg leg)
    {
        var s = leg.Harm!;
        var verdict = leg.VerdictLine();
        Assert.True(s.Heals >= 1, "heals>=1: " + verdict);
        Assert.True(s.Potions >= 1, "potions>=1: no potion raised the health: " + verdict);
        Assert.True(s.Swapped, "swapped=yes: the caster swap ran in and out: " + verdict);
        Assert.True(s.Anomalies.Count == 0, "the heal checks that broke: " + string.Join("; ", s.Anomalies) + " | " + verdict);
        Report(output, s);
    }

    /// <summary>Prints the script's fields that no fact asserts yet (report-only in their first gate): the harms, the health's lowest and last share, the kit uses, the transfers, the resisted harms, the fizzles and the heal amounts.</summary>
    internal static void Report(ITestOutputHelper output, HarmHealScript s) =>
        output.WriteLine($"report-only: harms={s.Harms} health_min_pct={s.HealthMinPercent:0.0} health_end_pct={s.HealthEndPercent:0.0} kits={s.Kits} fizzles={s.Fizzles} transfers={s.Transfers} resist={s.ResistExceeded} kit_points={s.KitPoints} potion_points={s.PotionPoints} potion_stack={s.PotionStackBefore}->{s.PotionStackAfter} checks={(s.Anomalies.Count == 0 ? "ok" : string.Join("; ", s.Anomalies))}");
}

/// <summary>
/// The fighter's leg of the heal test (M4 P4; M4 P5a lane C3: the harm-heal script replaced the belt hunt). Holtburg Brawler, <c>test-melee</c>: the luck of the belt is gone, no hunt is needed for the kit-and-potion facts.
/// </summary>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test-melee")]
[LoopbackCharacter("Holtburg Brawler")]
[LiveSharedSession]
public sealed class HealBrawlerLiveTests(ITestOutputHelper output)
{
    /// <summary>
    /// <b>Holtburg Brawler (test-melee), one session, no hunt, no teleport.</b> In peace where it stands, the Brawler swaps its Mace for the caster once, lowers its own health to about half with Harm Self (levels chosen by <see cref="HarmSelfPlan"/>, a fizzle
    /// retried and counted, a 60 s cap), uses a Handy Healing Kit and the health RISES (judged by the server's vital update, a lost roll counted and retried), harms again, drinks a Potion of Healing and the health rises, the vitals keeper restores the health to its
    /// until share, and the caster swaps out once. Judges (the slice table's three): <c>heals&gt;=1 potions&gt;=1</c> and exactly one swap around all the casts (<c>swapped=yes</c>), and (P5a-R6, D28)
    /// the heal checks the old Brawler fact made: each kit use's Healed amount at most the health missing and its Structure down by one, the potion's Drank amount 1 to 25 and its stack down by one. Printed, not asserted, in this first gate (P5a-R9): that Harm Self
    /// took the health to about half and never to 0 (<c>health_min_pct</c>), the harms, the kit uses and the end health (<c>health_end_pct</c>). Prints <c>harm-heal done ok= heals= kits= potions= harms= fizzles= health_min_pct= swapped= ... s= granted=</c>.
    /// </summary>
    [LoopbackFact]
    public void Holtburg_Brawler_harms_itself_to_half_then_heals_by_kit_and_by_potion_and_the_keeper_restores_it()
    {
        var leg = HarmHealFacts.Run(output, "heal-brawler", FighterTemplate.Melee.Name);
        HarmHealFacts.AssertFighter(output, leg);
    }
}

/// <summary>The Archer's harm-heal fact (M4 P5a lane C3): the same leg on Holtburg Archer, <c>test-archer</c> (its launcher and arrows swap out for the caster and back). The gate's fighter and actions areas should name this class.</summary>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test-archer")]
[LoopbackCharacter("Holtburg Archer")]
[LiveSharedSession]
public sealed class HealArcherLiveTests(ITestOutputHelper output)
{
    /// <summary>
    /// <b>Holtburg Archer (test-archer), one session, no hunt, no teleport.</b> The same script as the Brawler's, with the Archer's Longbow and its Greater Frost Arrows unwielded for the caster and wielded again afterwards (<see cref="CasterSwap"/> Out restores what In recorded).
    /// Proves the swap works for a launcher (the CheckWeaponCollision rule for a missile weapon), Harm Self and the heals on the Archer, and the same end facts as the Brawler's fact.
    /// </summary>
    [LoopbackFact]
    public void Holtburg_Archer_harms_itself_to_half_then_heals_by_kit_and_by_potion_and_the_keeper_restores_it()
    {
        var leg = HarmHealFacts.Run(output, "heal-archer", FighterTemplate.Archer.Name);
        HarmHealFacts.AssertFighter(output, leg);
    }
}
