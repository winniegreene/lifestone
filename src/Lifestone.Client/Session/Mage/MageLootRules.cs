using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Session.Mage;

/// <summary>Why the mage's loot rule kept an item of a corpse, or left it (<see cref="MageLootPlan.VerdictOf"/>).</summary>
public enum MageLootVerdict
{
    /// <summary>Kept: the item's value per burden is at or over the floor and it fits the load line and the pack's free slots.</summary>
    Keep,

    /// <summary>Kept: pyreals are always taken, whatever the floor and the bounds.</summary>
    Pyreals,

    /// <summary>Left: no Value in the description or in an appraisal (<c>TODO(m5-loot-value-unknown)</c>); the item counts as worth nothing.</summary>
    ValueUnknown,

    /// <summary>Left: the value (or the value per burden) is under the floor.</summary>
    ValuePerBurden,

    /// <summary>Left: taking it would carry the burden over the load line.</summary>
    Load,

    /// <summary>Left (M5 7 F28): the value per burden reaches the floor, but the item's type is one no vendor of the ground buys, so it cannot be sold (<see cref="MageLootThresholds.SellableItemTypes"/>).</summary>
    NotSold,

    /// <summary>Left: the main pack has no free slot for it (counting the slots already planned and the ones kept free).</summary>
    Slots,

    /// <summary>Left: the player already holds the item.</summary>
    AlreadyHeld,

    /// <summary>Left: the corpse is the player's own, which is the death handler's.</summary>
    OwnCorpse,

    /// <summary>Left (M5 7 F29, D96, the owner's point that weight is bad): the item alone is heavier than the per-item burden cap (<see cref="MageLootThresholds.MaxItemBurden"/>), whatever its value.</summary>
    TooHeavy,

    /// <summary>Left (M5 7 F29, D96): the value per burden reaches the floor, but the payout (the Value times the best buyer's BuyPrice) is under <see cref="MageLootThresholds.MinPayout"/>.</summary>
    Payout,

    /// <summary>Kept (M5 7 F30, the owner): a caster with a War Magic bonus at the profile's floor or a piece with a cantrip the mage wants (<see cref="KeepAsideRule"/>), whatever the value, burden and payout floors say; "worth a look", and never sold.</summary>
    KeepAside,

    /// <summary>Kept (M5 7 S2b, D125): a mana stone, empty or charged, while the kit holds fewer than its stone target (<see cref="ItemManaKeeper.StoneTarget"/>), whatever the value, burden and payout floors say; it goes to the kit pack.</summary>
    KitStone,

    /// <summary>
    /// Left (M5 7 D149, the owner's rule: only wearable upgrades; amends F30): the keep-aside rule matched the item, but it is heavier than <see cref="MageLootThresholds.MaxItemBurden"/> and the self-equip judge
    /// (<see cref="SelfEquipJudge"/>) does not rate it a wearable upgrade (<see cref="SelfEquipChoice.IsUpgrade"/>): a requirement unmet, no free slot (the robe's coverage counted, D129), its cantrips worn already, or no gain.
    /// </summary>
    HeavyNotUpgrade,
}

/// <summary>
/// The numbers of the mage's loot rule (M5 item 3 m2, D46): options of the run, never a dungeon's constants. The floor and the load line start at Lifestone's own values and are set from the measured loot value of a ground
/// (<c>m5-den-loot-value</c>) and, for the load line, from the ground's supplies section (the burden line, m5-plan.md section 4.5 m0) when the autopilot sets them.
/// </summary>
public sealed record MageLootThresholds
{
    /// <summary>The default floor: one pyreal of value for each unit of burden. Lifestone's own start value; <c>TODO(m5-loot-floor-measure)</c>: item 3's measured den loot value sets it.</summary>
    public const double DefaultMinValuePerBurden = 1.0;

    /// <summary>The default load line: the whole capacity the inventory page reads (the share of it the burden may reach with the item taken, in percent; 0 is no limit).</summary>
    public const int DefaultLoadLinePercent = 100;

    /// <summary>The default for the slots kept free in the main pack: none.</summary>
    public const int DefaultSlotsKeptFree = 0;

    /// <summary>The least value per burden unit an item must have to be kept (pyreals excepted).</summary>
    public double MinValuePerBurden { get; init; } = DefaultMinValuePerBurden;

    /// <summary>
    /// M5 7 F28 (D95): the item types the ground's vendors buy (<see cref="Lifestone.Client.Session.Grounds.HuntingGround.SellableItemTypes"/>); an item whose type has no bit in it is left (<see cref="MageLootVerdict.NotSold"/>). 0, the default, is
    /// unrestricted: nothing is known of what sells.
    /// </summary>
    public uint SellableItemTypes { get; init; }

    /// <summary>M5 7 F29 (D96): the least payout (<c>max(1, floor(rate x Value + 0.1))</c> at the best buyer of the item's type) an item must fetch; 0 is off.</summary>
    public long MinPayout { get; init; }

    /// <summary>M5 7 F29 (D96): the heaviest single item kept, whatever its value; 0 is off.</summary>
    public int MaxItemBurden { get; init; }

    /// <summary>M5 7 F29 (D96): the ground's vendors as (mask, rate) pairs, which price an item at its best buyer; empty reads a rate of 1 (the whole Value).</summary>
    public IReadOnlyList<LootBuyer> Buyers { get; init; } = [];

    /// <summary>The best buyer's rate for an item of type <paramref name="itemType"/>: the highest BuyPrice among the buyers whose mask has a bit of it; 1 when no buyer is listed; 0 when some are listed and none buys the type.</summary>
    /// <param name="itemType">The item's ItemType.</param>
    public double BestRate(uint itemType)
    {
        if (Buyers.Count == 0)
        {
            return 1.0;
        }

        var best = 0.0;
        for (var i = 0; i < Buyers.Count; i++)
        {
            if ((Buyers[i].ItemTypes & itemType) != 0 && Buyers[i].Rate > best)
            {
                best = Buyers[i].Rate;
            }
        }

        return best;
    }

    /// <summary>What the best buyer pays for an item of type <paramref name="itemType"/> whose whole stack is worth <paramref name="value"/>: ACE's <c>max(1, floor(rate x Value + 0.1))</c> (Vendor.cs 591-598 @95ef426d); 0 when no buyer takes the type.</summary>
    /// <param name="itemType">The item's ItemType.</param>
    /// <param name="value">The stack's Value.</param>
    public long PayoutOf(uint itemType, long value)
    {
        var rate = BestRate(itemType);
        return rate <= 0 ? 0 : Math.Max(1, (long)Math.Floor((rate * value) + 0.1));
    }

    /// <summary>The share of the capacity the burden may reach with an item taken, in percent (the load line); 0 for no limit.</summary>
    public int LoadLinePercent { get; init; } = DefaultLoadLinePercent;

    /// <summary>The main pack's slots the rule leaves free for what the mage buys and carries.</summary>
    public int SlotsKeptFree { get; init; } = DefaultSlotsKeptFree;

    /// <summary>M5 7 F30: the keep-aside rule (the ground's <c>loot.keep_aside</c> block), or null for none.</summary>
    public KeepAsideRule? KeepAside { get; init; }

    /// <summary>The thresholds in one phrase, for the log.</summary>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"value per burden at least {MinValuePerBurden:0.##}{(MinPayout > 0 ? $", payout at least {MinPayout}" : string.Empty)}{(MaxItemBurden > 0 ? $", no item over {MaxItemBurden} burden" : string.Empty)}{(SellableItemTypes == 0 ? string.Empty : $", types sold here 0x{SellableItemTypes:X}")}, load line {(LoadLinePercent == 0 ? "unlimited" : LoadLinePercent + "% of the capacity")}, {SlotsKeptFree} slot(s) kept free");
}

/// <summary>One item of a corpse as the rule sees it: what the description (or an appraisal) said of it.</summary>
/// <param name="Guid">The item's object id.</param>
/// <param name="Name">The item's name, for the log.</param>
/// <param name="ItemType">The item type bits.</param>
/// <param name="Value">The pyreal value of the whole stack, or null when neither the description nor an appraisal gave one.</param>
/// <param name="Burden">The burden of the whole stack (0 when none was sent).</param>
/// <param name="Held">True when the player already holds the item.</param>
/// <param name="KeepAside">M5 7 F30: why the keep-aside rule keeps the item (its bonus or cantrips), or null when it does not.</param>
/// <param name="Wcid">M5 7 D121: the item's weenie class id, which the pack layout's rules read (a stock line, a kit line); 0 when not known.</param>
/// <param name="Gear">M5 7 D149: the self-equip judge's decision on a heavy find worth a look (<see cref="MageLootRules.IsHeavy"/>), made on the corpse with the loot's appraisal; null when not judged (a light find, or none asked).</param>
public readonly record struct MageLootItem(uint Guid, string Name, uint ItemType, long? Value, long Burden, bool Held = false, string? KeepAside = null, uint Wcid = 0, SelfEquipChoice? Gear = null)
{
    /// <summary>True for money (pyreals): the rule always takes it.</summary>
    public bool IsMoney => (ItemType & SellRules.ItemTypeMoney) != 0;

    /// <summary>The value per burden unit: infinite for a weightless item with a value, 0 for one without.</summary>
    public double ValuePerBurden => Value is not > 0 ? 0 : Burden <= 0 ? double.PositiveInfinity : (double)Value.Value / Burden;
}

/// <summary>
/// The mage's loot rule (M5 item 3 m2; DoD 6): which items of a creature's corpse the mage keeps, ranked by value per burden and bounded by the load line (the capacity the inventory page reads, as
/// <see cref="LootRules"/> reads it) and the main pack's free slots. Pure: it decides from a list of <see cref="MageLootItem"/> and the numbers of the load, touches no session and allocates once per plan.
/// </summary>
/// <remarks>
/// <para><b>The order.</b> Pyreals first, always taken (no floor, no bound: money is weightless to the rule). Then the items whose value per burden reaches the floor (<see cref="MageLootThresholds.MinValuePerBurden"/>), the
/// highest ratio first (a tie goes to the greater value, then to the corpse's order); each is kept while it fits under the load line and a free slot remains, else it is left with the reason, and a lighter item further down the list
/// may still fit. A weightless item with a value is the best ratio.</para>
/// <para><b>The value.</b> The description's <c>Value</c> when it has one, else an appraisal's (session.md section 38, <see cref="ValueOf"/>); with neither the item is worth nothing to the rule and is left as
/// <see cref="MageLootVerdict.ValueUnknown"/> (<c>TODO(m5-loot-value-unknown)</c>, named once by the caller). ACE's own <c>wo.Value ?? 0</c> reads the same.</para>
/// <para><b>The own corpse.</b> A corpse named for the player itself (<c>Corpse of &lt;name&gt;</c>) is never looted by this rule: it is the death handler's, which takes everything and walks the trip (m5-plan.md section 4.5 d1).</para>
/// <para><b>Retail and ACE.</b> Nothing here is retail's: the choice of what to pick up is the player's. The load is the inventory page's (<c>gmBackpackUI::SetLoadLevel</c> 11.6096 0x004A7210, <c>CACQualities::InqLoad</c> 0x0058FF60) and the
/// main pack's 102 slots are ACE's human weenie (<c>Container.cs</c> 527 @95ef426d, <see cref="GearKit.PackCapacity"/>).</para>
/// </remarks>
public sealed class MageLootRules
{
    /// <summary>The name <c>TODO(m5-loot-value-unknown)</c> carries in the log: an item whose Value is in neither the description nor an appraisal.</summary>
    public const string ValueUnknownTodo = "TODO(m5-loot-value-unknown)";

    private readonly IReadOnlyDictionary<uint, Lifestone.Data.Magic.SpellInfo>? _spells;

    /// <summary>The thresholds in force.</summary>
    public MageLootThresholds Thresholds { get; }

    /// <summary>The name of a spell id from the SpellTable, or null.</summary>
    /// <param name="id">The spell id.</param>
    public string? SpellName(uint id) => _spells is not null && _spells.TryGetValue(id, out var info) ? info.Name : null;

    /// <summary>True when the keep-aside rule wants an appraisal of an item of this type (it casts or has a bonus only an appraisal tells).</summary>
    /// <param name="itemType">The item's ItemType.</param>
    public bool WantsKeepAsideAppraisal(uint itemType) => Thresholds.KeepAside is not null && KeepAsideRule.NeedsAppraisal(itemType);

    /// <summary>A rule.</summary>
    /// <param name="thresholds">The thresholds, or null for the defaults.</param>
    /// <param name="spells">The SpellTable's spells, which name the spells of an appraisal for the keep-aside rule (M5 7 F30); null names none, so no cantrip matches.</param>
    public MageLootRules(MageLootThresholds? thresholds = null, IReadOnlyDictionary<uint, Lifestone.Data.Magic.SpellInfo>? spells = null)
    {
        _spells = spells;
        Thresholds = thresholds ?? new MageLootThresholds();
        ArgumentOutOfRangeException.ThrowIfNegative(Thresholds.MinValuePerBurden);
        ArgumentOutOfRangeException.ThrowIfNegative(Thresholds.LoadLinePercent);
        ArgumentOutOfRangeException.ThrowIfNegative(Thresholds.SlotsKeptFree);
        ArgumentOutOfRangeException.ThrowIfNegative(Thresholds.MinPayout);
        ArgumentOutOfRangeException.ThrowIfNegative(Thresholds.MaxItemBurden);
    }

    /// <summary>True when some vendor of the ground buys the item's type (or the ground's sellable types are not known, mask 0). Pyreals are not asked.</summary>
    /// <param name="item">The corpse's item.</param>
    public bool IsSoldHere(in MageLootItem item) => Thresholds.SellableItemTypes == 0 || (item.ItemType & Thresholds.SellableItemTypes) != 0;

    /// <summary>The name ACE gives a creature's corpse: <c>Corpse of &lt;creature&gt;</c>.</summary>
    /// <param name="creatureName">The creature's name.</param>
    public static string CorpseNameOf(string creatureName) => "Corpse of " + creatureName;

    /// <summary>True when the corpse is the player's own (named for the player): the death handler's, never this rule's.</summary>
    /// <param name="corpseName">The corpse's name.</param>
    /// <param name="playerName">The player's name.</param>
    public static bool IsOwnCorpse(string? corpseName, string? playerName) =>
        !string.IsNullOrEmpty(playerName) && string.Equals(corpseName, CorpseNameOf(playerName), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The value the rule reads: the description's <c>Value</c>, else the appraisal's (PropertyInt 19, session.md section 38; a not-found answer has none), else null.
    /// </summary>
    /// <param name="item">The item's world object.</param>
    /// <param name="appraisal">The appraisal the session holds of it, or null.</param>
    public static long? ValueOf(WorldObject item, Appraisal? appraisal)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Weenie.Value is { } described)
        {
            return described;
        }

        return appraisal is { IsNotFound: false, Value: { } appraised } ? appraised : null;
    }

    /// <summary>The rule's view of one item of a corpse.</summary>
    /// <param name="item">The item's world object.</param>
    /// <param name="appraisal">The appraisal the session holds of it, or null.</param>
    /// <param name="held">True when the player already holds it.</param>
    public static MageLootItem ItemOf(WorldObject item, Appraisal? appraisal, bool held)
    {
        ArgumentNullException.ThrowIfNull(item);
        var burden = item.Weenie.Burden is { } described ? described : appraisal is { IsNotFound: false, Burden: { } appraised } ? appraised : 0;
        return new MageLootItem(item.Guid, item.Name, item.ItemType, ValueOf(item, appraisal), burden, held, Wcid: item.Weenie.WeenieClassId);
    }

    /// <summary>
    /// The rule's view of one item of a corpse, with the keep-aside check (M5 7 F30) made on the appraisal. M5 7 D149: a find worth a look heavier than <see cref="MageLootThresholds.MaxItemBurden"/> is judged here by
    /// <paramref name="judgeWear"/> (the self-equip judge against what the mage wears now, <see cref="SelfEquipJudge.Judge"/>) on the same appraisal the keep-aside match read: the wield and activation requirements, the slots and the
    /// cantrips all come from it, and the loot step appraised every caster and wearable before planning, so no second appraisal is needed. The plan keeps it only when the judge rates it an upgrade.
    /// </summary>
    /// <param name="item">The item's world object.</param>
    /// <param name="appraisal">The appraisal the session holds of it, or null.</param>
    /// <param name="held">True when the player already holds it.</param>
    /// <param name="judgeWear">M5 7 D149: the self-equip judge for a heavy find worth a look; null judges none (such a find is then left, named "not judged").</param>
    public MageLootItem ItemFor(WorldObject item, Appraisal? appraisal, bool held, Func<GearCandidate, SelfEquipChoice>? judgeWear = null)
    {
        var plain = ItemOf(item, appraisal, held);
        if (Thresholds.KeepAside is not { } keep || plain.IsMoney)
        {
            return plain;
        }

        var aside = plain with { KeepAside = keep.Match(item.ItemType, appraisal, SpellName) };
        return aside.KeepAside is not null && IsHeavy(aside) && judgeWear is not null ? aside with { Gear = judgeWear(SelfEquipJudge.CandidateOf(item, appraisal)) } : aside;
    }

    /// <summary>M5 7 F29 (D96), D149: true when the item alone is heavier than <see cref="MageLootThresholds.MaxItemBurden"/> (never with the cap off, 0).</summary>
    /// <param name="item">The corpse's item.</param>
    public bool IsHeavy(in MageLootItem item) => Thresholds.MaxItemBurden > 0 && item.Burden > Thresholds.MaxItemBurden;

    /// <summary>The main pack's free slots now: its capacity (the player weenie's <c>ItemsCapacity</c> when it was sent, else <see cref="GearKit.PackCapacity"/>) less the ordinary items it holds (a side pack and what is in it are not main-pack slots).</summary>
    /// <param name="state">The session's read model.</param>
    public static int FreeSlots(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var capacity = state.Objects.TryGet(state.Self.Guid, out var self) && self.Weenie.ItemsCapacity is > 0 ? (int)self.Weenie.ItemsCapacity.Value : GearKit.PackCapacity;
        return Math.Max(0, capacity - state.Self.Inventory.Pack.Count);
    }

    /// <summary>Plans one corpse: which of its items the mage keeps, in the order they are taken.</summary>
    /// <param name="corpseName">The corpse's name.</param>
    /// <param name="playerName">The player's name (the own corpse is refused).</param>
    /// <param name="items">The corpse's items, in its order.</param>
    /// <param name="burden">The burden now.</param>
    /// <param name="capacity">The capacity (<see cref="LootContext.Capacity"/>); 0 with a load line is a load refusal for everything but pyreals, as <see cref="LootRules"/>'s.</param>
    /// <param name="freeSlots">The main pack's free slots now (<see cref="FreeSlots"/>).</param>
    /// <param name="layout">M5 7 D121: the packs' jobs and free slots, or null for the main pack alone: a kept item whose job has a side pack with a free slot goes there (counted against that pack, not the main pack's slots); a full side pack falls back to the main pack.</param>
    /// <param name="packRules">M5 7 D121: the rules that give a kept item its job (with <paramref name="layout"/>).</param>
    /// <param name="stonesWanted">M5 7 S2b: the mana stones the kit still wants (<see cref="ItemManaKeeper.StonesWanted"/>): that many of the corpse's stones are kept whatever the floors say (<see cref="MageLootVerdict.KitStone"/>); the excess goes by the value rules.</param>
    public MageLootPlan Plan(string? corpseName, string? playerName, IReadOnlyList<MageLootItem> items, int burden, int capacity, int freeSlots, PackLayout? layout = null, PackRules? packRules = null, int stonesWanted = 0)
    {
        ArgumentNullException.ThrowIfNull(items);
        var verdicts = new MageLootVerdict[items.Count];
        var order = new List<int>(items.Count);
        if (IsOwnCorpse(corpseName, playerName))
        {
            Array.Fill(verdicts, MageLootVerdict.OwnCorpse);
            return new MageLootPlan(this, items, verdicts, order, burden, capacity, freeSlots, ownCorpse: true, []);
        }

        var ranked = new List<int>(items.Count);
        var aside = new List<int>();
        var kitStones = new List<int>(); // M5 7 S2b: the mana stones kept for the kit
        var slots = Math.Max(0, freeSlots - Thresholds.SlotsKeptFree);
        var carried = (long)burden;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.Held)
            {
                verdicts[i] = MageLootVerdict.AlreadyHeld;
            }
            else if (item.IsMoney)
            {
                verdicts[i] = MageLootVerdict.Pyreals;
                order.Add(i);
                slots--;
            }
            else if (stonesWanted > 0 && (item.ItemType & PackRules.ManaStoneType) != 0)
            {
                stonesWanted--; // M5 7 S2b: round 12 left 38 mana stones under the value floors (22 Moderate, 12 Mana Stone, 3 Lesser, 1 Minor) while the kit wanted them
                kitStones.Add(i);
            }
            else if (item.KeepAside is not null && IsHeavy(item) && item.Gear is not { IsUpgrade: true })
            {
                verdicts[i] = MageLootVerdict.HeavyNotUpgrade; // M5 7 D149 (the owner): a heavy find is kept aside only as a wearable upgrade; the long run of 2026-10-30 pinned its load over the trip line with heavy armour it could not wear
            }
            else if (item.KeepAside is not null)
            {
                aside.Add(i); // M5 7 F30 (the owner): kept whatever the value, burden and payout floors say; the slots and the load line still bound it (D149: a heavy one only as an upgrade)
            }
            else if (item.Value is null)
            {
                verdicts[i] = MageLootVerdict.ValueUnknown;
            }
            else if (Thresholds.MaxItemBurden > 0 && item.Burden > Thresholds.MaxItemBurden)
            {
                verdicts[i] = MageLootVerdict.TooHeavy; // M5 7 F29 (D96): weight is bad, whatever the value
            }
            else if (item.Value < 1 || item.ValuePerBurden < Thresholds.MinValuePerBurden)
            {
                verdicts[i] = MageLootVerdict.ValuePerBurden;
            }
            else if (!IsSoldHere(item))
            {
                verdicts[i] = MageLootVerdict.NotSold;
            }
            else if (Thresholds.MinPayout > 0 && Thresholds.PayoutOf(item.ItemType, item.Value ?? 0) < Thresholds.MinPayout)
            {
                verdicts[i] = MageLootVerdict.Payout;
            }
            else
            {
                ranked.Add(i);
            }
        }

        ranked.Sort((a, b) => Compare(items, a, b));
        aside.Sort((a, b) => Compare(items, a, b));
        ranked.InsertRange(0, aside); // the keep-aside items are taken first, while the pack has room
        ranked.InsertRange(0, kitStones); // M5 7 S2b: the kit's stones before them
        var sideFree = new int[4];
        if (layout is not null)
        {
            for (var j = 1; j < sideFree.Length; j++)
            {
                sideFree[j] = layout.PackOf((PackJob)j) is { } pack ? pack.Free : 0;
            }
        }

        var into = new Dictionary<uint, (PackJob Job, uint Container)>();
        foreach (var i in ranked)
        {
            var item = items[i];
            var job = PackJob.Main;
            if (layout is not null && packRules is not null)
            {
                var wanted = packRules.JobOf(new PackItem(item.Guid, item.Name, item.Wcid, item.ItemType, 0), item.KeepAside is not null, rotation: false);
                job = wanted != PackJob.Main && sideFree[(int)wanted] > 0 ? wanted : PackJob.Main; // M5 7 D121: a full side pack falls back to the main pack
            }

            if (job == PackJob.Main && slots <= 0)
            {
                verdicts[i] = MageLootVerdict.Slots;
            }
            else if (Thresholds.LoadLinePercent > 0 && (capacity <= 0 || (carried + item.Burden) * 100 > (long)capacity * Thresholds.LoadLinePercent))
            {
                verdicts[i] = MageLootVerdict.Load;
            }
            else
            {
                verdicts[i] = kitStones.Contains(i) ? MageLootVerdict.KitStone : items[i].KeepAside is not null ? MageLootVerdict.KeepAside : MageLootVerdict.Keep;
                order.Add(i);
                if (job == PackJob.Main)
                {
                    slots--;
                }
                else
                {
                    sideFree[(int)job]--;
                    into[item.Guid] = (job, layout!.ContainerOf(job));
                }

                carried += item.Burden;
            }
        }

        // The pyreals' burden counts toward nothing the rule checks (money is weightless to it); the plan's burden is what the kept items add.
        return new MageLootPlan(this, items, verdicts, order, burden, capacity, freeSlots, ownCorpse: false, into);
    }

    private static int Compare(IReadOnlyList<MageLootItem> items, int a, int b)
    {
        var byRatio = items[b].ValuePerBurden.CompareTo(items[a].ValuePerBurden);
        if (byRatio != 0)
        {
            return byRatio;
        }

        var byValue = (items[b].Value ?? 0).CompareTo(items[a].Value ?? 0);
        return byValue != 0 ? byValue : a.CompareTo(b);
    }
}

/// <summary>
/// What <see cref="MageLootRules.Plan"/> decided for one corpse. It is a <see cref="LootRules"/> for the shared <see cref="CorpseLooter"/> (M5 3 m2): the looter asks it item by item and it answers from the plan, so the same loop
/// that takes the fighters' loot takes the mage's, one pick-up at a time, in the corpse's order. An item that arrived after the plan was made is not in it and is left.
/// </summary>
public sealed class MageLootPlan : LootRules
{
    private readonly MageLootRules _rules;
    private readonly Dictionary<uint, (PackJob Job, uint Container)> _into;
    private readonly Dictionary<uint, MageLootVerdict> _byGuid;
    private readonly int[] _counts = new int[Enum.GetValues<MageLootVerdict>().Length];

    internal MageLootPlan(MageLootRules rules, IReadOnlyList<MageLootItem> items, MageLootVerdict[] verdicts, List<int> order, int burden, int capacity, int freeSlots, bool ownCorpse, Dictionary<uint, (PackJob Job, uint Container)> into)
        : base(LootOptions.AllTypes, 0, long.MaxValue, 0)
    {
        _rules = rules;
        _into = into;
        OwnCorpse = ownCorpse;
        Burden = burden;
        Capacity = capacity;
        FreeSlots = freeSlots;
        Items = items;
        Verdicts = verdicts;
        _byGuid = new Dictionary<uint, MageLootVerdict>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            _byGuid[items[i].Guid] = verdicts[i];
            _counts[(int)verdicts[i]]++;
        }

        var kept = new List<uint>(order.Count);
        long value = 0;
        long weight = 0;
        foreach (var i in order)
        {
            kept.Add(items[i].Guid);
            value += items[i].Value ?? 0;
            weight += items[i].Burden;
        }

        Kept = kept;
        KeptValue = value;
        KeptBurden = weight;
    }

    /// <summary>The rule that made the plan.</summary>
    public MageLootRules Rules => _rules;

    /// <summary>True when the corpse is the player's own: nothing is kept (the death handler's).</summary>
    public bool OwnCorpse { get; }

    /// <summary>The burden the plan started from.</summary>
    public int Burden { get; }

    /// <summary>The capacity the plan used.</summary>
    public int Capacity { get; }

    /// <summary>The main pack's free slots the plan started from.</summary>
    public int FreeSlots { get; }

    /// <summary>The corpse's items, in its order.</summary>
    public IReadOnlyList<MageLootItem> Items { get; }

    /// <summary>The verdict of each item, in the corpse's order.</summary>
    public IReadOnlyList<MageLootVerdict> Verdicts { get; }

    /// <summary>The guids to keep, in the order the rule ranked them (pyreals first).</summary>
    public IReadOnlyList<uint> Kept { get; }

    /// <summary>The sum of the kept items' Values.</summary>
    public long KeptValue { get; }

    /// <summary>The sum of the kept items' burdens.</summary>
    public long KeptBurden { get; }

    /// <summary>The items left for <paramref name="verdict"/>.</summary>
    public int Count(MageLootVerdict verdict) => _counts[(int)verdict];

    /// <summary>The Value the plan read for <paramref name="guid"/>, or 0 for an item it does not know or one with none.</summary>
    public long ValueOf(uint guid)
    {
        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i].Guid == guid)
            {
                return Items[i].Value ?? 0;
            }
        }

        return 0;
    }

    /// <summary>The verdict for <paramref name="guid"/>, or null when the plan does not know the item.</summary>
    public MageLootVerdict? VerdictOf(uint guid) => _byGuid.TryGetValue(guid, out var verdict) ? verdict : null;

    /// <summary>M5 7 D121: the job of the pack a kept item goes into (<see cref="PackJob.Main"/> for the main pack, and for an item the plan does not route).</summary>
    public PackJob JobOf(uint guid) => _into.TryGetValue(guid, out var to) ? to.Job : PackJob.Main;

    /// <inheritdoc/>
    public override uint DestinationOf(WorldObject item, GameState state)
    {
        ArgumentNullException.ThrowIfNull(item);
        return _into.TryGetValue(item.Guid, out var to) ? to.Container : 0;
    }

    /// <summary>M5 7 D121: the side packs the plan routes kept items into, as the log prints it (<c>; to the components pack 2, the review pack 1</c>), or empty when everything goes to the main pack.</summary>
    public string RouteSummary()
    {
        if (_into.Count == 0)
        {
            return string.Empty;
        }

        var parts = _into.Values.GroupBy(v => v.Job).OrderBy(g => g.Key).Select(g => string.Create(CultureInfo.InvariantCulture, $"the {PackLayout.Word(g.Key)} pack {g.Count()}"));
        return "; to " + string.Join(", ", parts);
    }

    /// <inheritdoc/>
    public override LootDecision Decide(WorldObject? item, in LootContext context)
    {
        if (item is null)
        {
            return LootDecision.NotResolved;
        }

        if (context.Inventory is { } inventory && inventory.Find(item.Guid).Held)
        {
            return LootDecision.AlreadyHeld;
        }

        return VerdictOf(item.Guid) switch
        {
            MageLootVerdict.Keep or MageLootVerdict.Pyreals or MageLootVerdict.KeepAside or MageLootVerdict.KitStone => LootDecision.Take,
            MageLootVerdict.ValueUnknown or MageLootVerdict.ValuePerBurden or MageLootVerdict.Payout => LootDecision.Value,
            MageLootVerdict.NotSold => LootDecision.Type,
            MageLootVerdict.Load or MageLootVerdict.Slots or MageLootVerdict.TooHeavy or MageLootVerdict.HeavyNotUpgrade => LootDecision.Burden,
            MageLootVerdict.AlreadyHeld => LootDecision.AlreadyHeld,
            MageLootVerdict.OwnCorpse => LootDecision.Type,
            _ => LootDecision.NotResolved, // an item that arrived after the plan was made
        };
    }

    /// <inheritdoc/>
    public override string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"mage rule ({_rules.Thresholds}): keeps {Kept.Count} of {Items.Count} (value {KeptValue}, burden {KeptBurden}){(OwnCorpse ? "; the corpse is the player's own: left to the death handler" : string.Empty)}");

    /// <summary>M5 7 F30: the items the plan keeps aside (<see cref="MageLootVerdict.KeepAside"/>): the wands and cantrip pieces worth a look.</summary>
    public int WorthALookCount => Count(MageLootVerdict.KeepAside);

    /// <summary>The guids of the items the plan keeps aside, in the order they are taken.</summary>
    public IEnumerable<uint> WorthALookGuids => Kept.Where(g => VerdictOf(g) == MageLootVerdict.KeepAside);

    /// <summary>
    /// M5 7 F30: one line per item the keep-aside rule matched, whether the plan takes it or not: <c>loot: worth a look: '&lt;name&gt;' 0x&lt;guid&gt; (&lt;bonus or cantrips&gt;)</c>, with <c>; left: &lt;why&gt;</c> when a full pack or the load line stops it.
    /// M5 7 D149: a heavy find the self-equip does not rate an upgrade has its own line instead, <c>loot: worth a look but heavy and not an upgrade: left '&lt;name&gt;' 0x&lt;guid&gt; burden &lt;b&gt; (&lt;bonus or cantrips&gt;; &lt;the judge's verb&gt;: &lt;why&gt;)</c>;
    /// a heavy one it does rate an upgrade has <c>; heavy (burden &lt;b&gt; over &lt;cap&gt;) but an upgrade: &lt;verb&gt;: &lt;why&gt;</c> on its worth-a-look line.
    /// </summary>
    public IEnumerable<string> WorthALookLines()
    {
        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i].KeepAside is not { } why)
            {
                continue;
            }

            var gear = Items[i].Gear is { } judged ? judged.Verb + ": " + judged.Why : "not judged: the loot step gave no self-equip judge";
            if (Verdicts[i] == MageLootVerdict.HeavyNotUpgrade)
            {
                yield return string.Create(CultureInfo.InvariantCulture, $"loot: worth a look but heavy and not an upgrade: left '{Items[i].Name}' 0x{Items[i].Guid:X8} burden {Items[i].Burden} ({why}; {gear})");
                continue;
            }

            var heavy = _rules.IsHeavy(Items[i]) ? string.Create(CultureInfo.InvariantCulture, $"; heavy (burden {Items[i].Burden} over {_rules.Thresholds.MaxItemBurden}) but an upgrade: {gear}") : string.Empty;
            var left = Verdicts[i] switch
            {
                MageLootVerdict.Slots => "; left: no free slot",
                MageLootVerdict.Load => "; left: over the load line",
                MageLootVerdict.AlreadyHeld => "; already held",
                _ => string.Empty,
            };
            yield return string.Create(CultureInfo.InvariantCulture, $"loot: worth a look: '{Items[i].Name}' 0x{Items[i].Guid:X8} ({why}){heavy}{left}");
        }
    }

    /// <summary>The items the plan keeps (pyreals included).</summary>
    public int KeptCount => Kept.Count;

    /// <summary>The items the plan leaves, whatever the reason.</summary>
    public int LeftCount => Items.Count - Kept.Count;

    /// <summary>
    /// The decision lines (M5 7 F28, D95), one per item in the corpse's order:
    /// <c>loot: kept|left &lt;name&gt; value &lt;v&gt; burden &lt;b&gt; v/b &lt;x&gt; (floor &lt;f&gt;[, type not sold here][, &lt;why left&gt;])</c>. The v/b is "-" for a weightless item. "type not sold here" says that no vendor of the ground buys the item's type (whatever the
    /// verdict); the why-left is named when the floor is not the reason (unknown value, the load line, no free slot, already held, the own corpse).
    /// </summary>
    public IEnumerable<string> DecisionLines()
    {
        for (var i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            var verdict = Verdicts[i];
            var kept = verdict is MageLootVerdict.Keep or MageLootVerdict.Pyreals or MageLootVerdict.KeepAside or MageLootVerdict.KitStone;
            var ratio = item.Burden <= 0 ? "-" : item.ValuePerBurden.ToString("0.##", CultureInfo.InvariantCulture);
            var why = verdict switch
            {
                MageLootVerdict.ValueUnknown => ", value unknown",
                MageLootVerdict.Load => ", over the load line",
                MageLootVerdict.Slots => ", no free slot",
                MageLootVerdict.AlreadyHeld => ", already held",
                MageLootVerdict.OwnCorpse => ", the own corpse",
                MageLootVerdict.TooHeavy => ", too heavy",
                MageLootVerdict.Payout => ", under the payout floor",
                MageLootVerdict.KeepAside => ", worth a look",
                MageLootVerdict.KitStone => ", for the kit's mana stones",
                MageLootVerdict.HeavyNotUpgrade => ", worth a look but heavy and not an upgrade",
                _ => string.Empty,
            };
            var thresholds = _rules.Thresholds;
            var payout = thresholds.MinPayout > 0 || thresholds.Buyers.Count > 0 ? string.Create(CultureInfo.InvariantCulture, $" payout {(item.IsMoney ? item.Value ?? 0 : thresholds.PayoutOf(item.ItemType, item.Value ?? 0))}") : string.Empty;
            var limits = (thresholds.MinPayout > 0 ? string.Create(CultureInfo.InvariantCulture, $", min payout {thresholds.MinPayout}") : string.Empty) + (thresholds.MaxItemBurden > 0 ? string.Create(CultureInfo.InvariantCulture, $", max burden {thresholds.MaxItemBurden}") : string.Empty);
            var unsold = !item.IsMoney && !_rules.IsSoldHere(item) ? ", type not sold here" : string.Empty;
            yield return string.Create(
                CultureInfo.InvariantCulture,
                $"loot: {(kept ? "kept" : "left")} {item.Name} value {item.Value?.ToString(CultureInfo.InvariantCulture) ?? "unknown"} burden {item.Burden} v/b {ratio}{payout} (floor {thresholds.MinValuePerBurden:0.##}{limits}{unsold}{why})");
        }
    }

    /// <summary>The plan as the log prints it: the counts by verdict and the kept items' names in the order they are taken.</summary>
    public string Summary()
    {
        var list = new System.Text.StringBuilder();
        foreach (var guid in Kept)
        {
            for (var i = 0; i < Items.Count; i++)
            {
                if (Items[i].Guid == guid)
                {
                    list.Append(list.Length == 0 ? string.Empty : ", ").Append(CultureInfo.InvariantCulture, $"'{Items[i].Name}' (value {Items[i].Value ?? 0}, burden {Items[i].Burden})");
                }
            }
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"keep {Kept.Count} of {Items.Count} (pyreals={Count(MageLootVerdict.Pyreals)} ranked={Count(MageLootVerdict.Keep)} value_per_burden={Count(MageLootVerdict.ValuePerBurden)} unknown_value={Count(MageLootVerdict.ValueUnknown)} not_sold={Count(MageLootVerdict.NotSold)} load={Count(MageLootVerdict.Load)} slots={Count(MageLootVerdict.Slots)} held={Count(MageLootVerdict.AlreadyHeld)} own_corpse={Count(MageLootVerdict.OwnCorpse)} too_heavy={Count(MageLootVerdict.TooHeavy)} payout_floor={Count(MageLootVerdict.Payout)} worth_a_look={WorthALookCount}{(Count(MageLootVerdict.HeavyNotUpgrade) > 0 ? $" heavy_not_upgrade={Count(MageLootVerdict.HeavyNotUpgrade)}" : string.Empty)}{(Count(MageLootVerdict.KitStone) > 0 ? $" kit_stones={Count(MageLootVerdict.KitStone)}" : string.Empty)}); value {KeptValue}, burden {KeptBurden}{(list.Length == 0 ? string.Empty : ": " + list)}");
    }
}
