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
    /// <summary>The default floor: one pyreal of value for each unit of burden. Lifestone's own start value; <c>m5-loot-floor-measure (closed)</c>: item 3's measured den loot value sets it.</summary>
    public const double DefaultMinValuePerBurden = 1.0;

    /// <summary>The default load line: the whole capacity the inventory page reads (the share of it the burden may reach with the item taken, in percent; 0 is no limit).</summary>
    public const int DefaultLoadLinePercent = 100;

    /// <summary>
    /// M6 14 pit-hold: the highest load line on a ground with jumps (<see cref="Lifestone.Client.Session.Grounds.HuntingGround.Jumps"/>): 100%. Retail's load modifier is 1 up to a load of 1.0 and falls as 2 - load past it
    /// (<c>EncumbranceSystem::LoadMod</c>, 11.6096 0x004FD810), and it scales both the run rate and the jump's height (<c>MovementSystem::GetRunRate</c> 0x006B1820, <c>GetJumpHeight</c> 0x006B1880), so up to 100% the flight the
    /// runner checks against a jump's need is her whole one, and past it the flight falls about as the load's 1.5th power: Qwinnie's buffed 11.54 m (Run 103, Jump 178) falls under the Abode's need of 10.75 m at about 107%
    /// (<see cref="Lifestone.Client.Movement.Routes.JumpReach.FlightMetres(int, int, float)"/>). The loot never carries her past it there.
    /// </summary>
    public const int JumpLoadLinePercent = 100;

    /// <summary>The default for the slots kept free in the main pack: none.</summary>
    public const int DefaultSlotsKeptFree = 0;

    /// <summary>
    /// M6 14 pit-hold: <paramref name="loadLinePercent"/> held to <see cref="JumpLoadLinePercent"/> on a ground with jumps (0, no limit, becomes the jump line too); unchanged on any other ground.
    /// </summary>
    /// <param name="loadLinePercent">The load line before the jumps (0: no limit).</param>
    /// <param name="hasJumps">True when the ground's walk takes a running jump.</param>
    public static int JumpCapped(int loadLinePercent, bool hasJumps) =>
        hasJumps && (loadLinePercent == 0 || loadLinePercent > JumpLoadLinePercent) ? JumpLoadLinePercent : loadLinePercent;

    /// <summary>
    /// M6 14 pit-hold: the load line the fight's loot keeps under on <paramref name="ground"/> with the run's thresholds at their defaults: the ground's <c>loot.load_line_percent</c> when it names one, else
    /// <see cref="DefaultLoadLinePercent"/>, held to <see cref="JumpLoadLinePercent"/> on a ground with jumps (<c>FightOptions.EffectiveLootThresholds</c>' rule, for the audit's line).
    /// </summary>
    /// <param name="ground">The ground.</param>
    public static int LoadLineOf(Lifestone.Client.Session.Grounds.HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(ground);
        return JumpCapped(ground.Loot is { LoadLinePercent: > 0 } loot ? loot.LoadLinePercent : DefaultLoadLinePercent, ground.Jumps.Count > 0);
    }

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

    /// <summary>
    /// M6 item 1's scroll keep rule (the hook item 2 lands for it): the reason a found scroll is kept to be read, or null when it is not.
    /// A reason is the <see cref="MageLootVerdict.KeepAside"/> verdict, so the sale never sells it; null keeps no scroll.
    /// </summary>
    public Func<WorldObject, string?>? KeepScroll { get; init; }

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
/// <param name="Gear">M5 7 D149: the self-equip judge's decision on a heavy find worth a look (<see cref="MageLootRules.IsHeavy"/>), made on the corpse with the loot's appraisal; M6 14 (<c>m6-lore-cantrips</c>): and on every other appraised
/// caster or wearable the keep-aside rule does not keep (a wear upgrade is ranked first); null when not judged (a light find worth a look, or none asked).</param>
/// <param name="Look">M6 14 (<c>m6-lore-cantrips</c>): true when <paramref name="KeepAside"/> is the keep-aside rule's own match (a wand's bonus, a great cantrip): the soak line's <c>worth_a_look=</c> counts these alone, not a scroll the scroll rule keeps or a quest's want.</param>
/// <param name="GreatCantrip">
/// M6 14 keep every great cantrip (the owner, 2026-11-15): true when the keep-aside rule's match casts one of its cantrips at Major or above (<see cref="KeepAsideRule.GreatCantrips"/>; M6 14 useful cantrips, the owner 2026-11-16: a useful
/// one, <see cref="CantripJudge"/>, the same predicate as <see cref="CantripRoom.IsUsefulGreatPiece(GameState, uint, Func{uint, string}, CantripPlan, Action{string}, string)"/>): the piece outranks every piece of sell loot, the heavy rule
/// never leaves it, and the plan makes room for it from the sell loot she carries (<see cref="MageLootRules.Plan"/>). A wand kept for its War Magic bonus alone is not one.
/// </param>
public readonly record struct MageLootItem(uint Guid, string Name, uint ItemType, long? Value, long Burden, bool Held = false, string? KeepAside = null, uint Wcid = 0, SelfEquipChoice? Gear = null, bool Look = false, bool GreatCantrip = false)
{
    /// <summary>True for money (pyreals): the rule always takes it.</summary>
    public bool IsMoney => (ItemType & SellRules.ItemTypeMoney) != 0;

    /// <summary>M6 14 (<c>m6-lore-cantrips</c>): true for a find the self-equip judge would put on (a wear in a free slot, a swap for the held caster, a replace of a worn piece): ranked first with the keep-aside finds.</summary>
    public bool IsWearUpgrade => Gear is { Kind: SelfEquipKind.Wear or SelfEquipKind.Swap or SelfEquipKind.Replace };

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
    /// <param name="cantrips">M6 14 useful cantrips (the owner, 2026-11-16): the judge of her cantrips (<see cref="CantripJudge.Of"/>, her skills live): only a useful great cantrip is kept aside and outranks sell loot; null:
    /// <see cref="CantripJudge.Stateless"/>.</param>
    public MageLootItem ItemFor(WorldObject item, Appraisal? appraisal, bool held, Func<GearCandidate, SelfEquipChoice>? judgeWear = null, CantripJudge? cantrips = null)
    {
        var plain = ItemOf(item, appraisal, held);
        if (!plain.IsMoney && Thresholds.KeepScroll?.Invoke(item) is { } scroll)
        {
            return plain with { KeepAside = scroll };
        }

        if (plain.IsMoney)
        {
            return plain;
        }

        var match = Thresholds.KeepAside?.Match(item.ItemType, appraisal, SpellName, cantrips);
        var aside = plain with { KeepAside = match, Look = match is not null, GreatCantrip = match is not null && Thresholds.KeepAside!.GreatCantrips(item.ItemType, appraisal, SpellName, cantrips).Count > 0 }; // M6 14 keep every great cantrip; M6 14 useful cantrips: the useful ones only
        if (judgeWear is null || appraisal is not { IsNotFound: false } || !KeepAsideRule.NeedsAppraisal(item.ItemType))
        {
            return aside;
        }

        // M5 7 D149: a heavy find worth a look is kept only as a wearable upgrade (a light one is kept aside unasked, F30 unchanged); M6 14 (m6-lore-cantrips): any other caster or wearable is judged too, so a wear upgrade is ranked first
        return match is null || IsHeavy(aside) ? aside with { Gear = judgeWear(SelfEquipJudge.CandidateOf(item, appraisal)) } : aside;
    }

    /// <summary>M6 5 q4 (D57): the keep-aside verdict of an item the fight wants for a quest (<c>quest: want &lt;wcid&gt;</c>, the fight scenario's hook over <c>FightOptions.Want</c>): the heavy rule never leaves it (a tusk weighs 100), the slots and the load line still bound it.</summary>
    public const string QuestWantPrefix = "quest: want ";

    /// <summary>M6 5 q4 (D57): true when the item's keep-aside verdict is a quest want (<see cref="QuestWantPrefix"/>).</summary>
    /// <param name="item">The corpse's item.</param>
    public static bool IsQuestWant(in MageLootItem item) => item.KeepAside is { } why && why.StartsWith(QuestWantPrefix, StringComparison.Ordinal);

    /// <summary>M5 7 F29 (D96), D149: true when the item alone is heavier than <see cref="MageLootThresholds.MaxItemBurden"/> (never with the cap off, 0).</summary>
    /// <param name="item">The corpse's item.</param>
    public bool IsHeavy(in MageLootItem item) => Thresholds.MaxItemBurden > 0 && item.Burden > Thresholds.MaxItemBurden;

    /// <summary>Plans one corpse: which of its items the mage keeps, in the order they are taken.</summary>
    /// <param name="corpseName">The corpse's name.</param>
    /// <param name="playerName">The player's name (the own corpse is refused).</param>
    /// <param name="items">The corpse's items, in its order.</param>
    /// <param name="burden">The burden now.</param>
    /// <param name="capacity">The capacity (<see cref="LootContext.Capacity"/>); 0 with a load line is a load refusal for everything but pyreals, as <see cref="LootRules"/>'s.</param>
    /// <param name="freeSlots">The main pack's free slots now (<see cref="Quartermaster.PackView.MainPackFree"/>).</param>
    /// <param name="layout">M5 7 D121: the packs' jobs and free slots, or null for the main pack alone: a kept item whose job has a side pack with a free slot goes there (counted against that pack, not the main pack's slots); a full side pack falls back to the main pack.</param>
    /// <param name="packRules">M5 7 D121: the rules that give a kept item its job (with <paramref name="layout"/>).</param>
    /// <param name="stonesWanted">M5 7 S2b: the mana stones the kit still wants (<see cref="ItemManaKeeper.StonesWanted"/>): that many of the corpse's stones are kept whatever the floors say (<see cref="MageLootVerdict.KitStone"/>); the excess goes by the value rules.</param>
    /// <param name="sellLoot">
    /// M6 14 keep every great cantrip (the owner, 2026-11-15): the sell loot she carries in her main pack that may be dropped to make room (<see cref="CantripRoom.SellLoot"/>), or null for none. A great-cantrip piece
    /// (<see cref="MageLootItem.GreatCantrip"/>) the load line or the slots would refuse is kept by dropping from it the lowest value per burden first, until the piece fits under the line and a slot is free (<see cref="MageLootPlan.Drops"/>);
    /// when dropping all of it cannot make room, nothing is dropped and the piece is left with its reason (<see cref="MageLootPlan.Shorts"/>).
    /// M6 14 hour load (the owner's ruling, 2026-11-16: she is not married to something just because she picked it up): the same list makes room for plain sell loot the line or the slots would refuse, by a swap (<see cref="MageLootPlan.Swaps"/>):
    /// only sell loot of a lower value per burden than the find's is dropped, the lowest first, and the swap never makes her heavier than the line or than she is now; when it cannot make room nothing is dropped and the find is left
    /// (<see cref="MageLootPlan.SwapShorts"/>).
    /// </param>
    /// <param name="loadLinePercent">M6 14 hour load: the load line for this plan (the cycle's rising line, <see cref="CycleLoad.LineFor"/>), or null for the thresholds' own (<see cref="MageLootThresholds.LoadLinePercent"/>).</param>
    public MageLootPlan Plan(string? corpseName, string? playerName, IReadOnlyList<MageLootItem> items, int burden, int capacity, int freeSlots, PackLayout? layout = null, PackRules? packRules = null, int stonesWanted = 0, IReadOnlyList<CarriedLoot>? sellLoot = null, int? loadLinePercent = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        var line = loadLinePercent ?? Thresholds.LoadLinePercent;
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        var verdicts = new MageLootVerdict[items.Count];
        var order = new List<int>(items.Count);
        if (IsOwnCorpse(corpseName, playerName))
        {
            Array.Fill(verdicts, MageLootVerdict.OwnCorpse);
            return new MageLootPlan(this, items, verdicts, order, burden, capacity, freeSlots, ownCorpse: true, [], loadLinePercent: line);
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
            else if (item.KeepAside is not null && IsHeavy(item) && item.Gear is not { IsUpgrade: true } && item.Gear?.LoreNeed is null && !IsQuestWant(item) && !item.GreatCantrip) // M6 5 q4 (D57): a quest want is no gear: the heavy rule does not apply; M6 14 keep every great cantrip (the owner, 2026-11-15, amends D149): nor to a piece with a great cantrip
            {
                verdicts[i] = MageLootVerdict.HeavyNotUpgrade; // M5 7 D149 (the owner): a heavy find is kept aside only as a wearable upgrade; the long run of 2026-10-30 pinned its load over the trip line with heavy armour it could not wear
            }
            else if (item.KeepAside is not null || item.IsWearUpgrade)
            {
                // M5 7 F30 (the owner): kept whatever the value, burden and payout floors say; the slots and the load line still bound it (D149: a heavy one only as an upgrade, or, M6 14 (m6-lore-cantrips; the owner, 2026-11-14), one her
                // Arcane Lore alone keeps dark: the lore will rise). M6 14: a find the self-equip would put on is ranked with them, first, as plain loot for the station's pick (sold if it is not worn).
                aside.Add(i);
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
        aside.Sort((a, b) => IsQuestWant(items[a]) != IsQuestWant(items[b]) ? (IsQuestWant(items[a]) ? -1 : 1) : items[a].GreatCantrip != items[b].GreatCantrip ? (items[a].GreatCantrip ? -1 : 1) : Compare(items, a, b)); // M6 14 want room: the quest's wanted items first of all; M6 14 keep every great cantrip: then the great-cantrip pieces
        ranked.InsertRange(0, kitStones); // M5 7 S2b: the kit's stones before the ranked loot
        ranked.InsertRange(0, aside); // M6 14 (m6-lore-cantrips; the owner, 2026-11-14): the keep-aside finds and the wear upgrades first of all, so no lighter loot of the corpse takes the room under the load line or the last slot before them
        var sideFree = new int[4];
        if (layout is not null)
        {
            for (var j = 1; j < sideFree.Length; j++)
            {
                sideFree[j] = layout.PackOf((PackJob)j) is { } pack ? pack.Free : 0;
            }
        }

        var into = new Dictionary<uint, (PackJob Job, uint Container)>();
        var drops = new List<RoomDrop>();
        var shorts = new List<RoomShort>();
        var swapShorts = new List<RoomShort>();
        var dropped = new HashSet<uint>();
        var droppedRatio = 0.0; // M6 14 hour load: the highest value per burden this corpse's room drops leave on the ground
        foreach (var i in ranked)
        {
            var item = items[i];
            var job = PackJob.Main;
            if (layout is not null && packRules is not null)
            {
                var wanted = packRules.JobOf(new PackItem(item.Guid, item.Name, item.Wcid, item.ItemType, 0), item.GreatCantrip, rotation: false); // the review pack: great-cantrip pieces only (the owner, 2026-11-16: evaluate the armour out of the main pack)
                job = wanted != PackJob.Main && sideFree[(int)wanted] > 0 ? wanted : PackJob.Main; // M5 7 D121: a full side pack falls back to the main pack
            }

            var noSlot = job == PackJob.Main && slots <= 0;
            var overLine = line > 0 && (capacity <= 0 || (carried + item.Burden) * 100 > (long)capacity * line);
            if (!noSlot && !overLine && droppedRatio > 0 && IsSwappable(item, kitStones.Contains(i)) && item.ValuePerBurden < droppedRatio)
            {
                // M6 14 hour load: the room a drop freed is never filled with loot worth less a burden than what was dropped for it
                overLine = true;
                swapShorts.Add(new RoomShort(item.Guid, item.Name, item.Burden, MageLootVerdict.Load, string.Create(CultureInfo.InvariantCulture, $"its value per burden {CantripRoom.Ratio(item.ValuePerBurden)} is under the {CantripRoom.Ratio(droppedRatio)} of the sell loot this corpse's room drops leave on the ground: the room they freed is not filled with worse")));
            }
            else if ((noSlot || overLine) && !item.GreatCantrip && !IsQuestWant(item) && sellLoot is not null && IsSwappable(item, kitStones.Contains(i)))
            {
                // M6 14 hour load (the owner's ruling, 2026-11-16: she is not married to something just because she picked it up): sell loot of a lower value per burden she carries makes way for a better find, on every ground and at every load.
                // M6 14 (live ladder42 19:51:19 UTC: 0 of 102 slots free at 108% burden, under the line 136%): where the slots bind and the burden does not, a slot is worth its item's value: the lowest value she carries makes way
                var bySlot = noSlot && !overLine;
                if (MakeRoom(item, job == PackJob.Main, carried, slots, capacity, line, sellLoot, dropped, out var swapped, out var refused, belowRatio: bySlot ? double.PositiveInfinity : item.ValuePerBurden, noHeavier: true, belowValue: bySlot ? item.Value ?? 0 : long.MaxValue))
                {
                    foreach (var loot in swapped)
                    {
                        drops.Add(new RoomDrop(loot, item.Guid, item.Name, Swap: true, ForRatio: item.ValuePerBurden, BySlot: bySlot, ForValue: item.Value ?? 0));
                        dropped.Add(loot.Guid);
                        if (!bySlot)
                        {
                            droppedRatio = Math.Max(droppedRatio, loot.ValuePerBurden); // a slot swap's freed slot is the find's: no room is left to fill
                        }

                        carried -= loot.Burden;
                        slots++; // a main-pack item: its slot is free once it lies on the ground
                    }

                    noSlot = overLine = false;
                }
                else
                {
                    swapShorts.Add(new RoomShort(item.Guid, item.Name, item.Burden, noSlot ? MageLootVerdict.Slots : MageLootVerdict.Load, refused));
                }
            }

            var want = IsQuestWant(item);
            if ((noSlot || overLine) && (item.GreatCantrip || want))
            {
                // M6 14 keep every great cantrip (the owner, 2026-11-15): the piece outranks every piece of sell loot: room is made from what she carries, the lowest value per burden first.
                // M6 14 want room (live 2026-11-17 09:58:23 and 10:01:59, ladder57: three Pyreal Motes the Atlan wand's hunt wanted were left "over the load line" and "no free slot" while the pack held 65 pieces of sell loot): a quest's wanted
                // item outranks every sell loot line by the same rule; it is left only when dropping all of it cannot make room, loudly (an error).
                if (MakeRoom(item, job == PackJob.Main, carried, slots, capacity, line, sellLoot, dropped, out var picked, out var why))
                {
                    foreach (var loot in picked)
                    {
                        drops.Add(new RoomDrop(loot, item.Guid, item.Name, Want: want));
                        dropped.Add(loot.Guid);
                        droppedRatio = Math.Max(droppedRatio, loot.ValuePerBurden);
                        carried -= loot.Burden;
                        slots++; // a main-pack item: its slot is free once it lies on the ground
                    }

                    noSlot = overLine = false;
                }
                else
                {
                    shorts.Add(new RoomShort(item.Guid, item.Name, item.Burden, noSlot ? MageLootVerdict.Slots : MageLootVerdict.Load, why, want));
                }
            }

            if (noSlot)
            {
                verdicts[i] = MageLootVerdict.Slots;
            }
            else if (overLine)
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
        return new MageLootPlan(this, items, verdicts, order, burden, capacity, freeSlots, ownCorpse: false, into, drops, shorts, swapShorts, line);
    }

    /// <summary>
    /// M6 14 hour load: true for a find the swap may make room for: sell loot (no keep-aside verdict, not the kit's stone, not money) with a value. A wear upgrade is one (it is plain loot for the station's pick, sold if not worn, M6 14
    /// <c>m6-lore-cantrips</c>): it is ranked first and makes room by the same rule. A keep-aside piece keeps its own rule (a great cantrip's room, or none).
    /// </summary>
    private static bool IsSwappable(in MageLootItem item, bool kitStone) => !kitStone && !item.IsMoney && item.KeepAside is null && item.Value is > 0;

    /// <summary>
    /// M6 14 keep every great cantrip: the sell loot to drop so that <paramref name="piece"/> fits under the load line and, when it goes to the main pack (<paramref name="mainSlot"/>), finds a free slot: from <paramref name="sellLoot"/> not
    /// already dropped, the lowest value per burden first (a tie to the lower value, then the heavier, then the guid), until it fits. False, with nothing picked, when dropping all of it cannot make room (or the capacity is not read, or
    /// she carries none); <paramref name="why"/> then says so with the numbers.
    /// M6 14 hour load (the swap): with <paramref name="belowRatio"/> only sell loot of a value per burden strictly under it may be dropped; with <paramref name="noHeavier"/> the piece fits when the burden after it is at or under the
    /// line or at or under the burden now (a line the rising line fell back under never makes her shed loot: the swap only trades).
    /// </summary>
    /// M6 14 (the slots): with <paramref name="belowValue"/> only sell loot of a value strictly under it may be dropped, the lowest value first (where the slots bind, a slot is worth its item's value).
    private static bool MakeRoom(in MageLootItem piece, bool mainSlot, long carried, int slots, int capacity, int line, IReadOnlyList<CarriedLoot>? sellLoot, HashSet<uint> dropped, out List<CarriedLoot> picked, out string why, double belowRatio = double.PositiveInfinity, bool noHeavier = false, long belowValue = long.MaxValue)
    {
        picked = [];
        if (line > 0 && capacity <= 0)
        {
            why = "the capacity is not read, so no room under the load line can be measured";
            return false;
        }

        var bySlot = belowValue != long.MaxValue;
        var swap = !double.IsPositiveInfinity(belowRatio) || bySlot;
        var candidates = (sellLoot ?? []).Where(c => !dropped.Contains(c.Guid) && c.ValuePerBurden < belowRatio && c.Value < belowValue).ToList();
        candidates.Sort((a, b) =>
        {
            if (bySlot && a.Value != b.Value)
            {
                return a.Value.CompareTo(b.Value); // the slots bind: the lowest value first
            }

            var byRatio = a.ValuePerBurden.CompareTo(b.ValuePerBurden);
            if (byRatio != 0)
            {
                return byRatio;
            }

            var byValue = a.Value.CompareTo(b.Value);
            return byValue != 0 ? byValue : b.Burden.CompareTo(a.Burden) is var byBurden && byBurden != 0 ? byBurden : a.Guid.CompareTo(b.Guid);
        });

        long freed = 0;
        var count = 0;
        var pieceBurden = piece.Burden;
        var limit = line <= 0 ? 0 : noHeavier ? Math.Max((long)capacity * line, carried * 100) : (long)capacity * line;
        bool Fits() => (line <= 0 || (carried - freed + pieceBurden) * 100 <= limit) && (!mainSlot || slots + count > 0);
        foreach (var loot in candidates)
        {
            if (Fits())
            {
                break;
            }

            picked.Add(loot);
            freed += loot.Burden;
            count++;
        }

        if (Fits())
        {
            why = string.Empty;
            return true;
        }

        picked.Clear();
        var lineBurden = line <= 0 ? 0 : (long)capacity * line / 100;
        var which = bySlot ? string.Create(CultureInfo.InvariantCulture, $" of a value under its {belowValue} (the slots bind: a slot is worth its item's value)") : swap ? string.Create(CultureInfo.InvariantCulture, $" of a value per burden under its {CantripRoom.Ratio(belowRatio)}") : string.Empty;
        var against = noHeavier && line > 0 && carried > lineBurden ? string.Create(CultureInfo.InvariantCulture, $" (or the burden now, {carried}: a swap never makes her heavier)") : string.Empty;
        why = string.Create(
            CultureInfo.InvariantCulture,
            $"dropping all {candidates.Count} sell loot item(s) she carries{which} (burden {candidates.Sum(c => c.Burden)}, value {candidates.Sum(c => c.Value)}) cannot make room: burden {carried} + {pieceBurden} against the load line {(line <= 0 ? "(none)" : line + "% = " + lineBurden)}{against}, {Math.Max(0, slots)} free slot(s){(mainSlot ? " (the piece goes to the main pack)" : string.Empty)}");
        return false;
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
    private readonly MageLootVerdict[] _verdicts;
    private readonly List<uint> _kept;
    private readonly Dictionary<uint, string> _withdrawn = [];

    internal MageLootPlan(MageLootRules rules, IReadOnlyList<MageLootItem> items, MageLootVerdict[] verdicts, List<int> order, int burden, int capacity, int freeSlots, bool ownCorpse, Dictionary<uint, (PackJob Job, uint Container)> into, List<RoomDrop>? drops = null, List<RoomShort>? shorts = null, List<RoomShort>? swapShorts = null, int? loadLinePercent = null)
        : base(LootOptions.AllTypes, 0, long.MaxValue, 0)
    {
        _rules = rules;
        _into = into;
        LoadLinePercent = loadLinePercent ?? rules.Thresholds.LoadLinePercent;
        SwapShorts = swapShorts ?? [];
        OwnCorpse = ownCorpse;
        Burden = burden;
        Capacity = capacity;
        FreeSlots = freeSlots;
        Items = items;
        _verdicts = verdicts;
        Verdicts = verdicts;
        Drops = drops ?? [];
        Shorts = shorts ?? [];
        _byGuid = new Dictionary<uint, MageLootVerdict>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            _byGuid[items[i].Guid] = verdicts[i];
            _counts[(int)verdicts[i]]++;
        }

        _kept = new List<uint>(order.Count);
        long value = 0;
        long weight = 0;
        foreach (var i in order)
        {
            _kept.Add(items[i].Guid);
            value += items[i].Value ?? 0;
            weight += items[i].Burden;
        }

        Kept = _kept;
        KeptValue = value;
        KeptBurden = weight;
    }

    /// <summary>
    /// M6 14 keep every great cantrip (the owner, 2026-11-15): the sell loot the plan drops from her main pack to make room for its great-cantrip pieces, in the order to drop (the lowest value per burden first, piece by piece). The loot step drops
    /// them, checks each, and only then takes the corpse's items (<see cref="Refit"/> with what it reads after).
    /// </summary>
    public IReadOnlyList<RoomDrop> Drops { get; }

    /// <summary>M6 14 keep every great cantrip: the great-cantrip pieces the plan could not make room for (dropping all her sell loot would not), each with its reason: left, loudly, and the full-pack trip is due.</summary>
    public IReadOnlyList<RoomShort> Shorts { get; }

    /// <summary>The burden the plan's drops free.</summary>
    public long DroppedBurden => Drops.Sum(d => d.Loot.Burden);

    /// <summary>The Value the plan's drops leave on the ground.</summary>
    public long DroppedValue => Drops.Sum(d => d.Loot.Value);

    /// <summary>The pieces the plan made room for (the pieces its drops are for: the great-cantrip pieces and the swaps' finds), in the order they are taken.</summary>
    public IEnumerable<uint> RoomMadeFor => Drops.Select(d => d.ForGuid).Distinct();

    /// <summary>M6 14 hour load: the finds of sell loot the plan swapped in (the pieces its swap drops are for), in the order they are taken.</summary>
    public IEnumerable<uint> Swaps => Drops.Where(d => d.Swap).Select(d => d.ForGuid).Distinct();

    /// <summary>M6 14 hour load: the plain sell loot the line or the slots refused and no swap could make room for, each with the reason (every sell loot she carries is worth as much a burden or more, or dropping it cannot make room).</summary>
    public IReadOnlyList<RoomShort> SwapShorts { get; }

    /// <summary>M6 14 hour load: the load line the plan used, in percent of the capacity (the cycle's rising line, or the thresholds' own; 0 is no limit).</summary>
    public int LoadLinePercent { get; }

    /// <summary>
    /// M6 14 keep every great cantrip (check, act, check): takes a kept item off the plan after the loot step's check read that it no longer fits (the room was not made, or the burden or the slots read after the drops are short): its verdict
    /// becomes <paramref name="verdict"/> (<see cref="MageLootVerdict.Load"/> or <see cref="MageLootVerdict.Slots"/>), it leaves <see cref="Kept"/>, and the looter skips it. False when the plan does not keep it.
    /// </summary>
    /// <param name="guid">The item.</param>
    /// <param name="verdict">The verdict it is left with.</param>
    /// <param name="why">Why, for the log (<see cref="WithdrawnWhy"/>).</param>
    public bool Withdraw(uint guid, MageLootVerdict verdict, string why)
    {
        if (!_kept.Remove(guid))
        {
            return false;
        }

        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i].Guid == guid)
            {
                _counts[(int)_verdicts[i]]--;
                _verdicts[i] = verdict;
                _counts[(int)verdict]++;
                KeptValue -= Items[i].Value ?? 0;
                KeptBurden -= Items[i].Burden;
                break;
            }
        }

        _byGuid[guid] = verdict;
        _withdrawn[guid] = why;
        return true;
    }

    /// <summary>Why <paramref name="guid"/> was taken off the plan (<see cref="Withdraw"/>), or null when it was not.</summary>
    /// <param name="guid">The item.</param>
    public string? WithdrawnWhy(uint guid) => _withdrawn.TryGetValue(guid, out var why) ? why : null;

    /// <summary>
    /// M6 14 keep every great cantrip (the check after the drops): the plan's keeps fitted again to the burden and the free slots read now, in the order the plan ranked them (pyreals first, then the great-cantrip pieces, then the rest): each
    /// kept while it fits under the load line and, going to the main pack, a free slot remains; the others are withdrawn (<see cref="Withdraw"/>). Pyreals take a slot and are never withdrawn. The guids withdrawn, in that order.
    /// </summary>
    /// <param name="burden">The burden read now.</param>
    /// <param name="capacity">The capacity read now.</param>
    /// <param name="freeSlots">The main pack's free slots read now (<see cref="Quartermaster.PackView.MainPackFree"/>).</param>
    /// <param name="apply">False: only say which would be withdrawn, and withdraw none.</param>
    public List<uint> Refit(int burden, int capacity, int freeSlots, bool apply = true)
    {
        var withdrawn = new List<uint>();
        var line = LoadLinePercent;
        var limit = Math.Max((long)capacity * line, (long)Burden * 100); // M6 14 hour load: a swap planned at a burden over the line never made her heavier; the check holds it to the same bound
        var slots = Math.Max(0, freeSlots - _rules.Thresholds.SlotsKeptFree);
        var carried = (long)burden;
        foreach (var guid in _kept.ToArray())
        {
            var item = Items.First(i => i.Guid == guid);
            var main = JobOf(guid) == PackJob.Main;
            if (item.IsMoney)
            {
                slots--;
                continue;
            }

            if (main && slots <= 0)
            {
                if (apply)
                {
                    Withdraw(guid, MageLootVerdict.Slots, string.Create(CultureInfo.InvariantCulture, $"no free slot when read again ({freeSlots} free, {_rules.Thresholds.SlotsKeptFree} kept free)"));
                }

                withdrawn.Add(guid);
            }
            else if (line > 0 && (capacity <= 0 || (carried + item.Burden) * 100 > limit))
            {
                if (apply)
                {
                    Withdraw(guid, MageLootVerdict.Load, string.Create(CultureInfo.InvariantCulture, $"over the load line when read again (burden {carried} + {item.Burden} against {line}% of {capacity})"));
                }

                withdrawn.Add(guid);
            }
            else
            {
                carried += item.Burden;
                if (main)
                {
                    slots--;
                }
            }
        }

        return withdrawn;
    }

    /// <summary>
    /// M6 14 keep every great cantrip: one line per great-cantrip piece the plan makes room for (<c>loot: make room for '&lt;piece&gt;' ...: drop &lt;n&gt; sell loot item(s) ...</c>), with every drop it plans by name, value, burden and value per
    /// burden; and one loud line per piece it could not (<c>loot: NO ROOM for the great cantrip '&lt;piece&gt;' ...</c>).
    /// </summary>
    public IEnumerable<string> RoomLines()
    {
        var line = LoadLinePercent;
        var lineText = line <= 0 ? "no load line" : string.Create(CultureInfo.InvariantCulture, $"load line {line}% = {(long)Capacity * line / 100}");
        foreach (var piece in RoomMadeFor)
        {
            var item = Items.First(i => i.Guid == piece);
            var mine = Drops.Where(d => d.ForGuid == piece).ToList();
            var list = string.Join(", ", mine.Select(d => string.Create(CultureInfo.InvariantCulture, $"'{d.Loot.Name}' 0x{d.Loot.Guid:X8} value {d.Loot.Value} burden {d.Loot.Burden} v/b {CantripRoom.Ratio(d.Loot.ValuePerBurden)}")));
            if (mine[0].Swap)
            {
                // M6 14 hour load (the owner, 2026-11-16): the swap, with every number (M6 14: by value where the slots bind)
                yield return string.Create(
                    CultureInfo.InvariantCulture,
                    $"loot: swap: take '{item.Name}' 0x{item.Guid:X8} (value {item.Value ?? 0}, burden {item.Burden}, v/b {CantripRoom.Ratio(item.ValuePerBurden)}): burden {Burden} of {Capacity} ({lineText}), {FreeSlots} free slot(s): drop {mine.Count} sell loot item(s) {(mine[0].BySlot ? "of a lower value (the slots bind, the burden does not: a slot is worth its item's value)" : "of a lower value per burden")}, burden {mine.Sum(d => d.Loot.Burden)}, value {mine.Sum(d => d.Loot.Value)}, the lowest first: {list}");
                continue;
            }

            yield return string.Create(
                CultureInfo.InvariantCulture,
                $"loot: make room for {(mine[0].Want ? "the wanted item" : "the great cantrip")} '{item.Name}' 0x{item.Guid:X8} ({item.KeepAside}; value {item.Value ?? 0}, burden {item.Burden}): burden {Burden} of {Capacity} ({lineText}), {FreeSlots} free slot(s): drop {mine.Count} sell loot item(s), burden {mine.Sum(d => d.Loot.Burden)}, value {mine.Sum(d => d.Loot.Value)}, the lowest value per burden first: {list}");
        }

        foreach (var s in Shorts)
        {
            var item = Items.First(i => i.Guid == s.Guid);
            yield return s.Want
                ? string.Create(CultureInfo.InvariantCulture, $"loot: NO ROOM for the wanted item '{s.Name}' 0x{s.Guid:X8} ({item.KeepAside}; burden {s.Burden}): {s.Why}; nothing droppable is left, so it is left on the corpse ({(s.Verdict == MageLootVerdict.Slots ? "no free slot" : "over the load line")}) and the full-pack trip is due (m6-want-room)")
                : string.Create(CultureInfo.InvariantCulture, $"loot: NO ROOM for the great cantrip '{s.Name}' 0x{s.Guid:X8} ({item.KeepAside}; burden {s.Burden}): {s.Why}; it is left on the corpse ({(s.Verdict == MageLootVerdict.Slots ? "no free slot" : "over the load line")}) and the full-pack trip is due (m6-pack-room)");
        }

        foreach (var s in SwapShorts)
        {
            var item = Items.First(i => i.Guid == s.Guid);
            yield return string.Create(CultureInfo.InvariantCulture, $"loot: no swap for '{s.Name}' 0x{s.Guid:X8} (value {item.Value ?? 0}, burden {s.Burden}, v/b {CantripRoom.Ratio(item.ValuePerBurden)}): {s.Why}; it is left ({(s.Verdict == MageLootVerdict.Slots ? "no free slot" : "over the load line")})");
        }
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
    public long KeptValue { get; private set; }

    /// <summary>The sum of the kept items' burdens.</summary>
    public long KeptBurden { get; private set; }

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
        $"mage rule ({_rules.Thresholds}{(LoadLinePercent != _rules.Thresholds.LoadLinePercent ? $"; the hour's load line {LoadLinePercent}%" : string.Empty)}): keeps {Kept.Count} of {Items.Count} (value {KeptValue}, burden {KeptBurden}){(OwnCorpse ? "; the corpse is the player's own: left to the death handler" : string.Empty)}");

    /// <summary>
    /// M5 7 F30: the items the plan keeps aside as worth a look: the wands and cantrip pieces the keep-aside rule matched. M6 14 (<c>m6-lore-cantrips</c>, the count kept honest): a scroll the scroll rule keeps and a quest's want are kept aside too
    /// (<see cref="KeptAsideGuids"/>) but are not worth a look.
    /// </summary>
    public int WorthALookCount => WorthALookGuids.Count();

    /// <summary>The guids of the items the plan keeps aside as worth a look (the keep-aside rule's matches), in the order they are taken.</summary>
    public IEnumerable<uint> WorthALookGuids => Kept.Where(g => VerdictOf(g) == MageLootVerdict.KeepAside && Items.Any(i => i.Guid == g && i.Look));

    /// <summary>M6 14 (<c>m6-lore-cantrips</c>): the guids of every item the plan keeps aside (<see cref="MageLootVerdict.KeepAside"/>: the rule's matches, the scrolls kept and the quests' wants), in the order they are taken: what the sale never offers.</summary>
    public IEnumerable<uint> KeptAsideGuids => Kept.Where(g => VerdictOf(g) == MageLootVerdict.KeepAside);

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

            // M6 14 keep every great cantrip (the owner, 2026-11-15, amends D149): a heavy great-cantrip piece is kept whatever the self-equip says; the line says which of the two kept it
            var heavyWhy = Items[i].Gear is { IsUpgrade: true } || !Items[i].GreatCantrip ? "but an upgrade" : "and not an upgrade, but kept for its great cantrip (the owner, 2026-11-15)";
            var heavy = _rules.IsHeavy(Items[i]) ? string.Create(CultureInfo.InvariantCulture, $"; heavy (burden {Items[i].Burden} over {_rules.Thresholds.MaxItemBurden}) {heavyWhy}: {gear}") : string.Empty;
            var guid = Items[i].Guid;
            var mine = Drops.Where(d => d.ForGuid == guid).ToList();
            var room = mine.Count == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"; room made: {mine.Count} sell loot item(s) dropped first (burden {mine.Sum(d => d.Loot.Burden)}, value {mine.Sum(d => d.Loot.Value)})");
            var noRoom = Shorts.Any(s => s.Guid == guid) ? ", no room could be made (a great cantrip): the full-pack trip is due" : string.Empty;
            var withdrawn = WithdrawnWhy(guid) is { } off ? "; " + off : string.Empty;
            var left = Verdicts[i] switch
            {
                MageLootVerdict.Slots => "; left: no free slot" + noRoom,
                MageLootVerdict.Load => "; left: over the load line" + noRoom,
                MageLootVerdict.AlreadyHeld => "; already held",
                _ => string.Empty,
            };
            yield return string.Create(CultureInfo.InvariantCulture, $"loot: worth a look: '{Items[i].Name}' 0x{guid:X8} ({why}){heavy}{room}{left}{withdrawn}");
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
            if (item.KeepAside is null && item.IsWearUpgrade)
            {
                why += ", a wear upgrade ranked first (" + item.Gear!.Verb + ")"; // M6 14 (m6-lore-cantrips)
            }

            if (!item.GreatCantrip && Drops.Count(d => d.Swap && d.ForGuid == item.Guid) is > 0 and var swappedFor)
            {
                why += string.Create(CultureInfo.InvariantCulture, $", a swap: room made by dropping {swappedFor} sell loot item(s) {(Drops.First(d => d.Swap && d.ForGuid == item.Guid).BySlot ? "of a lower value (the slots bind)" : "of a lower value per burden")}"); // M6 14 hour load
            }
            else if (!item.GreatCantrip && SwapShorts.Any(s => s.Guid == item.Guid))
            {
                why += ", no swap could make room";
            }

            if (item.GreatCantrip)
            {
                // M6 14 keep every great cantrip: the room made for it, or that none could be
                var guid = item.Guid;
                var dropsFor = Drops.Count(d => d.ForGuid == guid);
                why += dropsFor > 0 ? string.Create(CultureInfo.InvariantCulture, $", a great cantrip: room made by dropping {dropsFor} sell loot item(s)")
                    : Shorts.Any(s => s.Guid == guid) ? ", a great cantrip: NO ROOM could be made" : ", a great cantrip";
            }

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
            $"keep {Kept.Count} of {Items.Count} (pyreals={Count(MageLootVerdict.Pyreals)} ranked={Count(MageLootVerdict.Keep)} value_per_burden={Count(MageLootVerdict.ValuePerBurden)} unknown_value={Count(MageLootVerdict.ValueUnknown)} not_sold={Count(MageLootVerdict.NotSold)} load={Count(MageLootVerdict.Load)} slots={Count(MageLootVerdict.Slots)} held={Count(MageLootVerdict.AlreadyHeld)} own_corpse={Count(MageLootVerdict.OwnCorpse)} too_heavy={Count(MageLootVerdict.TooHeavy)} payout_floor={Count(MageLootVerdict.Payout)} worth_a_look={WorthALookCount}{(Count(MageLootVerdict.HeavyNotUpgrade) > 0 ? $" heavy_not_upgrade={Count(MageLootVerdict.HeavyNotUpgrade)}" : string.Empty)}{(Count(MageLootVerdict.KitStone) > 0 ? $" kit_stones={Count(MageLootVerdict.KitStone)}" : string.Empty)}{(Drops.Count > 0 ? $" room_drops={Drops.Count} room_dropped_burden={DroppedBurden}" : string.Empty)}{(Swaps.Any() ? $" swaps={Swaps.Count()} swap_dropped_value={Drops.Where(d => d.Swap).Sum(d => d.Loot.Value)}" : string.Empty)}{(SwapShorts.Count > 0 ? $" no_swap={SwapShorts.Count}" : string.Empty)}{(Shorts.Count > 0 ? $" no_room={Shorts.Count}" : string.Empty)}); value {KeptValue}, burden {KeptBurden}{(list.Length == 0 ? string.Empty : ": " + list)}");
    }
}
