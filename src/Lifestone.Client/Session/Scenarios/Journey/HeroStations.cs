using System.Globalization;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// Which of the main pack's items go on the empty visible slots of the shortcut bar (the M4 hero video, the owner's direction: a filled lower-right bar). Engine-free. The order is a
/// player's: potions and food first (ItemType Food), then kits and other usables (Misc), mana stones, ammunition and missile weapons, the caster kept in the pack, gems, then melee weapons; one
/// item per weenie class; never a container, a spell component, money, armour, clothing, jewellery or a key. Within a kind, the pack's order. Lifestone's own choice (retail leaves the bar to the player).
/// </summary>
public static class HotbarPicks
{
    /// <summary>The ItemType bits in the order the bar is filled (AC's ItemType: Food 0x20, Misc 0x80, ManaStone 0x80000, MissileWeapon 0x100, Caster 0x8000, Gem 0x800, MeleeWeapon 0x1).</summary>
    public static IReadOnlyList<uint> Order { get; } = [0x20, 0x80, 0x80000, 0x100, 0x8000, 0x800, 0x1];

    /// <summary>The ItemType bits never put on the bar: Container 0x200, SpellComponents 0x1000, Money 0x40, Armor 0x2, Clothing 0x4, Jewelry 0x8, Key 0x4000, PromissoryNote 0x40000.</summary>
    public const uint Never = 0x200 | 0x1000 | 0x40 | 0x2 | 0x4 | 0x8 | 0x4000 | 0x40000;

    /// <summary>The items to add, at most <paramref name="free"/>, in the order they go on the empty slots.</summary>
    /// <param name="items">The main pack's items in the pack's order: guid, weenie class, ItemType.</param>
    /// <param name="onBar">The weenie classes the bar holds already (not picked again).</param>
    /// <param name="free">The empty visible slots.</param>
    public static IReadOnlyList<uint> Choose(IEnumerable<(uint Guid, uint Wcid, uint ItemType)> items, IEnumerable<uint> onBar, int free)
    {
        ArgumentNullException.ThrowIfNull(items);
        var taken = new HashSet<uint>(onBar ?? []);
        var ranked = new List<(int Rank, int Index, uint Guid, uint Wcid)>();
        var index = 0;
        foreach (var (guid, wcid, type) in items)
        {
            index++;
            if (guid == 0 || (type & Never) != 0)
            {
                continue;
            }

            var rank = -1;
            for (var i = 0; i < Order.Count; i++)
            {
                if ((type & Order[i]) != 0)
                {
                    rank = i;
                    break;
                }
            }

            if (rank >= 0)
            {
                ranked.Add((rank, index, guid, wcid));
            }
        }

        var picks = new List<uint>();
        foreach (var (_, _, guid, wcid) in ranked.OrderBy(r => r.Rank).ThenBy(r => r.Index))
        {
            if (picks.Count >= free)
            {
                break;
            }

            if (taken.Add(wcid))
            {
                picks.Add(guid);
            }
        }

        return picks;
    }
}

/// <summary>
/// <c>hotbar</c> (the M4 hero video; the hero plans only): fills each empty visible slot of the shortcut bar (slots 0 to 8, the lower-right bar) with an item from the main pack chosen by
/// <see cref="HotbarPicks"/> (<see cref="IActionApi.AddShortcut"/>, GameAction 0x019C), and keeps what the bar holds. The bar is the character's own and stays (the live gate re-creates the
/// journey characters, D44). Fields: <c>hotbar=&lt;slots filled&gt;/9 added= kept=</c>; an add that is not on its slot after the answer is a problem, not a failure (the bar is the video's dressing).
/// </summary>
public sealed class HotbarStation : IStation
{
    /// <summary>The station's name.</summary>
    public const string StationName = "hotbar";

    /// <summary>The visible slots (retail's lower-right bar shows nine; <c>GameHotbar.VisibleSlots</c>).</summary>
    public const int VisibleSlots = 9;

    /// <inheritdoc />
    public string Name => StationName;

    /// <inheritdoc />
    public StationNeeds Needs { get; } = new();

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context) => new HotbarRun(context);

    private sealed class HotbarRun(JourneyContext context) : StationLeg(StationName, context)
    {
        private int _added;
        private int _kept;
        private int _filled;

        private ShortcutBar Bar => S.State.Self.Shortcuts;

        // The bar is dressing: a slot that did not take its item is a problem in the fields, never a failure that skips the journey's showpieces.
        public override bool Ok => Stage == "done" && Failure is null;

        public override string VerdictFields() =>
            string.Create(CultureInfo.InvariantCulture, $"hotbar={_filled}/{VisibleSlots} added={_added} kept={_kept} problems={Problems.Count}") + (Failure is null ? string.Empty : " why=" + Failure.Replace(' ', '_'));

        protected override IEnumerable<ActionHandle?> Script()
        {
            Stage = "pick";
            var empty = new List<int>();
            var onBar = new List<uint>();
            for (var slot = 0; slot < VisibleSlots; slot++)
            {
                if (Bar[slot].IsEmpty)
                {
                    empty.Add(slot);
                }
                else
                {
                    _kept++;
                    if (S.State.Objects.TryGet(Bar[slot].ObjectId, out var held))
                    {
                        onBar.Add(held.Weenie.WeenieClassId);
                    }
                }
            }

            var items = new List<(uint, uint, uint)>();
            foreach (var entry in S.State.Self.Inventory.Pack)
            {
                if (entry.ContainerType == 0 && S.State.Objects.TryGet(entry.Guid, out var o))
                {
                    items.Add((entry.Guid, o.Weenie.WeenieClassId, o.ItemType));
                }
            }

            var picks = HotbarPicks.Choose(items, onBar, empty.Count);
            Log($"the bar holds {_kept} of {VisibleSlots}; {picks.Count} of {items.Count} main-pack items go on the {empty.Count} empty slots");

            Stage = "add";
            for (var i = 0; i < picks.Count; i++)
            {
                var (slot, item) = (empty[i], picks[i]);
                var add = S.Actions.AddShortcut(slot, item, ActionSource.Host);
                yield return add;
                var name = S.State.Objects.TryGet(item, out var o) ? o.Weenie.Name : "?";
                Log($"AddShortcut '{name}' {item:X8} to slot {slot}: {Describe(add)}");
                if (Check(add.Outcome is { Kind: ActionOutcomeKind.Sent }, $"AddShortcut '{name}' to slot {slot}: {Describe(add)}") && Check(Bar[slot].ObjectId == item, $"'{name}' is not on slot {slot} after the add"))
                {
                    _added++;
                }
            }

            _filled = Enumerable.Range(0, VisibleSlots).Count(slot => !Bar[slot].IsEmpty);
        }
    }
}

/// <summary>
/// <c>corpses</c> (the M4 hero video; the hero mage's plan, in the den after den-travel): <c>@lifestone corpses rot</c> (<see cref="PlayerTools.RotCorpses"/>) sets the character's own player corpses in its landblock
/// to ACE's empty-corpse rot time: the corpses earlier generations of BSD Mage left in the Black Spawn Den (16 at the hero step, from the gates before 4.2's corpse trip), which the character cannot loot. ACE's decay
/// removes each corpse within about 20 s and puts its contents on the ground, where they rot after ACE's default 300 s (WorldObject_Decay.cs:111-134 and :18 at 95ef426d); so when it rotted any, the station waits
/// for the corpses to leave the view and then <see cref="ItemRotSeconds"/>, standing at the den's arrival, before the fight is filmed. A second run finds none and goes on at once. Fields:
/// <c>corpses_found= corpses_rotted= corpses_left= corpses_wait_s=</c>; a refused or failed tool call is a problem, not a failure (the den is the video's set, not a check).
/// </summary>
public sealed class CorpsesStation(bool noItemWait = false) : IStation
{
    /// <summary>The station's name.</summary>
    public const string StationName = "corpses";

    /// <summary>The seconds the dropped contents need to rot after their corpse went: ACE's default 300 s, its 5 s decay heartbeat and a margin (Lifestone's own).</summary>
    public const double ItemRotSeconds = 320;

    /// <summary>The seconds the rotted corpses get to leave the view: ACE's 15 s, the heartbeat and a margin.</summary>
    public const double CorpseGoneSeconds = 40;

    /// <inheritdoc />
    public string Name => StationName;

    /// <inheritdoc />
    public StationNeeds Needs { get; } = new();

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context) => new CorpsesRun(context, noItemWait);

    /// <summary>True when the station does not wait for the rotted corpses' contents (<see cref="JourneyPlans.NoItemWaitArgument"/>, the one-off clean-up).</summary>
    public bool NoItemWait => noItemWait;

    private sealed class CorpsesRun(JourneyContext context, bool noItemWait) : StationLeg(StationName, context)
    {
        private int _found = -1;
        private int _rotted = -1;
        private int _left = -1;
        private double _waited;

        public override bool Ok => Stage == "done" && Failure is null;

        public override string VerdictFields() =>
            string.Create(CultureInfo.InvariantCulture, $"corpses_found={_found} corpses_rotted={_rotted} corpses_left={_left} corpses_wait_s={_waited:0} problems={Problems.Count}") + (Failure is null ? string.Empty : " why=" + Failure.Replace(' ', '_'));

        protected override IEnumerable<ActionHandle?> Script()
        {
            Stage = "rot";
            var own = "Corpse of " + Context.Character;
            var rot = S.Tools.RotCorpses(ActionSource.Host);
            yield return rot;
            if (!Check(rot.Result is not null, "@lifestone corpses rot: " + Describe(rot)))
            {
                yield break;
            }

            (_found, _rotted) = (rot.Result!.Found, rot.Result.Rotted);
            Log($"@lifestone corpses rot: {_found} '{own}' found in landblock 0x{rot.Result.Landblock:X8}, {_rotted} set to rot");
            var started = S.Clock.Now;
            if (_rotted > 0)
            {
                Stage = "corpses-gone";
                foreach (var h in Until(() => InView(own) == 0, CorpseGoneSeconds)) { yield return h; }

                if (noItemWait)
                {
                    Log($"the corpses left the view ({InView(own)} still in view); their contents rot on the ground by ACE's own decay (no wait: {JourneyPlans.NoItemWaitArgument})");
                }
                else
                {
                    Stage = "items-rot";
                    Log($"the corpses left the view ({InView(own)} still in view); waiting {ItemRotSeconds:0} s for their contents on the ground to rot");
                    foreach (var h in Pause(ItemRotSeconds)) { yield return h; }
                }
            }

            _waited = S.Clock.Now - started;
            _left = InView(own);
            Check(_left == 0, $"{_left} '{own}' still in view after the wait");
        }

        private int InView(string name) => S.State.Objects.All.Count(o => string.Equals(o.Weenie.Name, name, StringComparison.Ordinal));
    }
}
