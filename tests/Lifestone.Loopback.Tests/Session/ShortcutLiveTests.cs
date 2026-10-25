using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests;
using Lifestone.Data.Dat;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M4 3.2 h1, the shortcut bar against the bundled ACE: Holtburg Archer (test-archer; M4 P4: the Hunter's test) puts a Handy Healing Kit on slot 0 and a Potion of Healing on slot 1 through <see cref="IActionApi.AddShortcut"/>, ends the session, enters again (the description's
/// list must show both: ACE keeps the list and returns it in the next login's player description), drinks the potion by <see cref="IActionApi.UseShortcut"/> (slot 1; the hunter is usually at full health, so the potion is spent as
/// <see cref="HealResultKind.Wasted"/>, which is fine and is printed), removes both through <see cref="IActionApi.RemoveShortcut"/>, ends the session, and enters a third time (the list must be empty). WRITTEN AND COMPILED BY THE SLICE,
/// RUN BY THE LEAD: it needs the backend with the Lifestone server mod ready, Holtburg Archer built (<c>--scenario=fighter-build --account=test-archer</c>) and holding a Handy Healing Kit 628 and a stack of Potions of Healing 377 in its main pack.
/// The character is never reset, purged or levelled: it ends changed only by one potion spent. Rows left on the bar by an earlier failed run are removed at the start of the first session.
/// <para>Run it alone with <c>dotnet test tests/Lifestone.Loopback.Tests --filter "FullyQualifiedName~ShortcutLiveTests" --logger "console;verbosity=detailed"</c>; each session is recorded to <c>cache/m4/3-2/live</c> of the main checkout
/// (derived data, gitignored). About 3 minutes (three sessions and two 6 s pauses).</para>
/// <para>What it proves: the 019C and 019D reach ACE and are stored (the relog lists exactly the two objects at their slots, not missing), the bar is the description's after the relog, <c>UseShortcut</c> routes a potion to a drink that
/// ACE confirms, and the removal is stored (the third entry's bar is empty). It does not prove #219 live (an item leaving the inventory): that is the unit tests' (<c>ShortcutInventoryTests</c>).</para>
/// </summary>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test-archer")]
[LoopbackCharacter("Holtburg Archer")]
[LiveSharedSession]
public sealed class ShortcutLiveTests(ITestOutputHelper output)
{
    private const uint KitWcid = 628;
    private const uint PotionWcid = 377;
    private const int KitSlot = 0;
    private const int PotionSlot = 1;
    private const string Item = "3-2";

    /// <summary>
    /// <b>Holtburg Hunter, three sessions, all through <see cref="IActionApi"/>.</b> Prints <c>shortcuts added=&lt;n&gt; relog_listed=&lt;n&gt; used=&lt;outcome&gt; removed=&lt;n&gt; relog_empty=&lt;yes|no&gt;</c> and asserts added 2,
    /// relog_listed 2, the use Confirmed (Drank or Wasted), removed 2, relog_empty yes.
    /// </summary>
    [LoopbackFact]
    public void Holtburg_Archer_adds_a_kit_and_a_potion_relogs_drinks_by_slot_and_removes_both()
    {
        using var dats = DatSet.Open();
        var facts = new Facts();
        var legs = new List<ShortcutLeg>();
        foreach (var (part, name) in new[] { (Part.Add, "shortcuts-add"), (Part.Use, "shortcuts-use"), (Part.Verify, "shortcuts-verify") })
        {
            var leg = new ShortcutLeg(name, part, output.WriteLine, dats, facts);
            legs.Add(leg);
            var result = LiveRun.Run(output, leg, name, 300, item: Item, character: FighterTemplate.Archer.Name, ownLogin: true); // a relog is the point: each session logs in and out on its own
            output.WriteLine(facts.Verdict());
            Assert.True(leg.Problems.Count == 0, name + ": the checks that failed: " + string.Join("; ", leg.Problems) + " | " + facts.Verdict());
            Assert.True(result.Ok, name + ": " + result.Failure + " | " + facts.Verdict());
        }

        var verdict = facts.Verdict();
        Assert.Equal(2, facts.Added);
        Assert.Equal(2, facts.RelogListed);
        Assert.True(facts.UsedConfirmed, "the potion by slot was not Confirmed: " + verdict);
        Assert.Equal(2, facts.Removed);
        Assert.True(facts.RelogEmpty, "the third entry's bar was not empty: " + verdict);
    }

    private enum Part
    {
        Add,
        Use,
        Verify,
    }

    /// <summary>What the three sessions found, for the verdict line.</summary>
    private sealed class Facts
    {
        public uint Kit { get; set; }

        public uint Potion { get; set; }

        public int Added { get; set; }

        public int RelogListed { get; set; } = -1;

        public string Used { get; set; } = "none";

        public bool UsedConfirmed { get; set; }

        public int Removed { get; set; } = -1;

        public bool RelogEmpty { get; set; }

        public bool RelogEmptyKnown { get; set; }

        public string Verdict() =>
            string.Create(
                CultureInfo.InvariantCulture,
                $"shortcuts added={Added} relog_listed={RelogListed} used={Used} removed={Removed} relog_empty={(RelogEmptyKnown ? (RelogEmpty ? "yes" : "no") : "n/a")}");
    }

    private sealed class ShortcutLeg(string name, Part part, Action<string> log, DatSet dats, Facts facts) : LiveLeg(name, [], log, dats)
    {
        private const double ArrivalSeconds = 90;

        private ShortcutBar Bar => S.State.Self.Shortcuts;

        protected override IEnumerable<ActionHandle?> Script()
        {
            foreach (var h in Enter())
            {
                yield return h;
            }

            foreach (var h in Pause(2))
            {
                yield return h;
            }

            var work = part switch
            {
                Part.Add => AddBoth(),
                Part.Use => UseAndRemove(),
                _ => VerifyEmpty(),
            };
            foreach (var h in work)
            {
                yield return h;
            }

            foreach (var h in Pause(2)) // ACE saves the list with the character when the session ends
            {
                yield return h;
            }

            if (S.State.Phase == SessionPhase.InWorld)
            {
                foreach (var h in LogOffAndQuit())
                {
                    yield return h;
                }
            }
        }

        private IEnumerable<ActionHandle?> Enter()
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
            CharacterSlot? slot = S.State.Characters.Entries.OrderBy(c => c.PendingDeletion).ThenBy(c => c.Guid).Cast<CharacterSlot?>().FirstOrDefault(c => string.Equals(c!.Value.Name, FighterTemplate.Archer.Name, StringComparison.OrdinalIgnoreCase));
            Require(slot is { PendingDeletion: false }, $"'{FighterTemplate.Archer.Name}' is not on the account (or is pending deletion): build it first; this test never creates or purges a character");
            Stage = "entering";
            var enter = S.Actions.EnterWorld(slot!.Value.Guid, ActionSource.Host);
            yield return enter;
            Require(enter.Outcome is { Kind: ActionOutcomeKind.Confirmed }, "entering the world: " + Describe(enter));
            Stage = "arriving";
            var since = S.Clock.Now;
            while (S.State.Self.Travel != SelfTravel.Present || !S.State.Self.Qualities.IsKnown)
            {
                Require(S.Clock.Now - since <= ArrivalSeconds, "arriving: the player was not unhidden with its qualities within " + ArrivalSeconds + " s");
                yield return null;
            }

            Stage = "running";
            Log($"arrived; the description's bar holds {Bar.Count} shortcut(s)");
        }

        private uint FirstInPack(uint wcid)
        {
            foreach (var entry in S.State.Self.Inventory.Pack)
            {
                if (S.State.Objects.TryGet(entry.Guid, out var o) && o.Weenie.WeenieClassId == wcid && (wcid != KitWcid || (o.Weenie.Structure ?? 1) > 0))
                {
                    return entry.Guid;
                }
            }

            return 0;
        }

        /// <summary>Session one: any row an earlier failed run left is removed, then the kit goes to slot 0 and the potion to slot 1.</summary>
        private IEnumerable<ActionHandle?> AddBoth()
        {
            for (var slot = 0; slot < ShortcutBar.SlotCount; slot++)
            {
                if (Bar[slot].IsEmpty)
                {
                    continue;
                }

                Log($"slot {slot} held {Bar[slot].ObjectId:X8} from an earlier run: removing it");
                var clean = S.Actions.RemoveShortcut(slot, ActionSource.Host);
                yield return clean;
                Check(clean.Outcome is { Kind: ActionOutcomeKind.Sent }, $"clean-up of slot {slot}: {Describe(clean)}");
            }

            // M4 P4 sfix2: the hunt tests before this one drink the Archer's potions, so the test grants what it needs with the mod's item verb (as VendorLiveTests grants its pyreals) when the pack holds none.
            uint kit = 0, potion = 0;
            foreach (var h in GrantIfMissing(() => FirstInPack(KitWcid), KitWcid, "Handy Healing Kit 628", g => kit = g)) { yield return h; }
            foreach (var h in GrantIfMissing(() => FirstInPack(PotionWcid), PotionWcid, "Potion of Healing 377", g => potion = g)) { yield return h; }
            Require(kit != 0, "the hunter holds no Handy Healing Kit 628 with uses left in its main pack (the grant with the item verb failed)");
            Require(potion != 0, "the hunter holds no Potion of Healing 377 in its main pack (the grant with the item verb failed)");
            facts.Kit = kit;
            facts.Potion = potion;

            foreach (var (slot, item, what) in new[] { (KitSlot, kit, "kit"), (PotionSlot, potion, "potion") })
            {
                var add = S.Actions.AddShortcut(slot, item, ActionSource.Host);
                yield return add;
                Log($"AddShortcut {what} {item:X8} to slot {slot}: {Describe(add)}");
                if (Check(add.Outcome is { Kind: ActionOutcomeKind.Sent }, $"AddShortcut {what}: {Describe(add)}") && Check(Bar[slot].ObjectId == item, $"the {what} is not on slot {slot} after the add"))
                {
                    facts.Added++;
                }
            }
        }

        /// <summary>Session two: the description lists both, the potion is drunk by slot, both rows are removed.</summary>
        private IEnumerable<ActionHandle?> UseAndRemove()
        {
            var listed = 0;
            foreach (var (slot, item, what) in new[] { (KitSlot, facts.Kit, "kit"), (PotionSlot, facts.Potion, "potion") })
            {
                var ok = item != 0 && Bar[slot].ObjectId == item && !Bar.IsMissing(slot);
                Check(ok, $"the relog's description does not list the {what} {item:X8} on slot {slot} (the slot holds {Bar[slot].ObjectId:X8}, missing {Bar.IsMissing(slot)})");
                listed += ok ? 1 : 0;
            }

            facts.RelogListed = listed;
            Log($"the relog lists {listed} of the 2 shortcuts");

            var use = S.Actions.UseShortcut(PotionSlot, ActionSource.Host);
            yield return use;
            var result = (use as HealHandle)?.Result;
            facts.Used = (use.Outcome?.Kind.ToString() ?? "unsettled") + "/" + (result?.Kind.ToString() ?? "no-result") + "/" + use.Kind;
            facts.UsedConfirmed = use.Outcome is { Kind: ActionOutcomeKind.Confirmed } && result is { Kind: HealResultKind.Drank or HealResultKind.Wasted };
            Log("UseShortcut(1): " + Describe(use) + "; result " + (result?.ToString() ?? "none"));
            Check(use.Kind == ActionKind.Drink, "UseShortcut(1) did not route to Drink: " + use.Kind);
            Check(facts.UsedConfirmed, "UseShortcut(1) was not a Confirmed Drank or Wasted: " + facts.Used);

            foreach (var h in Pause(1))
            {
                yield return h;
            }

            foreach (var slot in new[] { KitSlot, PotionSlot })
            {
                if (Bar[slot].IsEmpty)
                {
                    Log($"slot {slot} is empty already (an item that leaves the bar is removed by #219)");
                    continue;
                }

                var remove = S.Actions.RemoveShortcut(slot, ActionSource.Host);
                yield return remove;
                Log($"RemoveShortcut({slot}): {Describe(remove)}");
                Check(remove.Outcome is { Kind: ActionOutcomeKind.Sent }, $"RemoveShortcut({slot}): {Describe(remove)}");
            }

            facts.Removed = (Bar[KitSlot].IsEmpty ? 1 : 0) + (Bar[PotionSlot].IsEmpty ? 1 : 0);
            Check(Bar.Count == 0, $"the bar still holds {Bar.Count} shortcut(s) after the removals");
        }

        /// <summary>Session three: the description's list is empty.</summary>
        private IEnumerable<ActionHandle?> VerifyEmpty()
        {
            facts.RelogEmptyKnown = true;
            facts.RelogEmpty = Bar.IsKnown && Bar.Count == 0;
            Log($"the third entry's bar holds {Bar.Count} shortcut(s)");
            Check(facts.RelogEmpty, $"the third entry's bar is not empty: {Bar.Count} shortcut(s)");
            yield break;
        }
    }
}
