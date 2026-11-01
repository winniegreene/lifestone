using System.Globalization;
using System.Text;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Session.Mage;

/// <summary>M5 7 D121 (the owner, polish point 1): the job of a pack. The main pack holds the loot to sell, the coins and the trade notes; each of the three side packs holds one job.</summary>
public enum PackJob
{
    /// <summary>The main pack: the loot to sell, the coins and the trade notes, nothing else.</summary>
    Main,

    /// <summary>The spell components: the scarabs, the tapers, any spell component.</summary>
    Components,

    /// <summary>The equipment the mage wears and rotates through, with its kit: a spare of a worn or wielded kit line, a piece the self-equip took off, the healing kits, the mana stones, the potions of the stock.</summary>
    Kit,

    /// <summary>The finds kept for the owner to review: the keep-aside rule's "worth a look" (M5 7 F30) and any other find the self-equip judged interesting but did not wear.</summary>
    Review,
}

/// <summary>One of the player's side packs (a container, ACE ContainerType 1; the foci, type 2, are not packs of the layout and stay in their side slots).</summary>
/// <param name="Guid">The pack's object id.</param>
/// <param name="Name">The pack's name, for the log.</param>
/// <param name="Capacity">The items it holds at most (the weenie's ItemsCapacity, <see cref="PackLayout.SidePackCapacity"/> when the description sent none).</param>
/// <param name="Count">The items in it now.</param>
public readonly record struct SidePack(uint Guid, string Name, int Capacity, int Count)
{
    /// <summary>The free slots.</summary>
    public int Free => Math.Max(0, Capacity - Count);
}

/// <summary>An item as the layout sees it.</summary>
/// <param name="Guid">The item's object id.</param>
/// <param name="Name">Its name, for the log.</param>
/// <param name="Wcid">Its weenie class id.</param>
/// <param name="ItemType">Its ItemType bits.</param>
/// <param name="Container">The pack that holds it: the player's id for the main pack, a side pack's id, 0 when it is worn or not held.</param>
public readonly record struct PackItem(uint Guid, string Name, uint Wcid, uint ItemType, uint Container);

/// <summary>A move the sort plans: the item, the job of the pack it is in and of the pack it goes to, the container to send it to and why.</summary>
/// <param name="Guid">The item.</param>
/// <param name="Name">Its name.</param>
/// <param name="From">The job of the pack it is in.</param>
/// <param name="To">The job of the pack it goes to.</param>
/// <param name="Container">The container the move names (<see cref="IActionApi.MoveToPack"/>).</param>
/// <param name="Why">Why it belongs there.</param>
public readonly record struct PackMove(uint Guid, string Name, PackJob From, PackJob To, uint Container, string Why)
{
    /// <summary>The move as the log prints it: <c>packs: move '&lt;name&gt;' 0x&lt;guid&gt; &lt;from&gt; -&gt; &lt;to&gt; pack 0x&lt;container&gt; (&lt;why&gt;)</c>.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"packs: move '{Name}' 0x{Guid:X8} {PackLayout.Word(From)} -> {PackLayout.Word(To)} pack 0x{Container:X8} ({Why})");
}

/// <summary>
/// M5 7 D121: which pack an item belongs in. Pure over the ground's supplies (its stock lines, its kit lines and the purse's notes) and the item's weenie class and ItemType (ACE <c>ItemType.cs</c>: Money 0x40, SpellComponents 0x1000,
/// PromissoryNote 0x40000, ManaStone 0x80000). Two memories the caller holds are asked by guid: the keep-aside rule's "worth a look" (<c>SaleMemory.KeptAside</c>) and the pieces the self-equip took off (<see cref="PackMemory.Rotation"/>).
/// </summary>
/// <remarks>
/// <para><b>The rule, in order.</b> Money, a trade note (the PromissoryNote type or a purse note's class): the main pack. A spell component (the SpellComponents type, or a class the ground's <c>components</c> names): the components pack. A stock line that is no
/// component (the Mana Potion), a mana stone (the ManaStone type), a healing kit (a name that ends "Healing Kit"), a worn or wielded kit line (a spare robe, gauntlets or wand), or a piece the self-equip took off: the kit pack. A find kept for the
/// owner: the review pack. Everything else, the loot to sell: the main pack.</para>
/// <para><b>Tolerance.</b> An item already in the kit pack that is equipment (armour, clothing, jewellery, a caster or a weapon) stays: it is gear the mage rotates through, kept by an earlier run whose memory a restart emptied. An item already in the
/// review pack that is not money, a note, a component or kit stays: the owner's. Neither is ever sold (the sale reads the main pack only).</para>
/// </remarks>
public sealed class PackRules
{
    /// <summary>ACE's ItemType Money.</summary>
    public const uint MoneyType = 0x40;

    /// <summary>ACE's ItemType SpellComponents.</summary>
    public const uint ComponentType = 0x1000;

    /// <summary>ACE's ItemType PromissoryNote (the trade notes).</summary>
    public const uint NoteType = 0x40000;

    /// <summary>ACE's ItemType ManaStone.</summary>
    public const uint ManaStoneType = 0x80000;

    /// <summary>ACE's ItemType Container (a side pack).</summary>
    public const uint ContainerType = 0x200;

    /// <summary>The equipment types a kit pack keeps as rotation gear: MeleeWeapon, Armor, Clothing, Jewelry, MissileWeapon and Caster.</summary>
    public const uint EquipmentTypes = 0x1 | 0x2 | 0x4 | 0x8 | 0x100 | 0x8000;

    private readonly HashSet<uint> _components = [];
    private readonly HashSet<uint> _kit = [];
    private readonly HashSet<uint> _notes = [];

    /// <summary>The rules of a ground (its stock, kit, components and purse); null for a ground-less run (the types alone decide).</summary>
    /// <param name="ground">The ground, or null.</param>
    public PackRules(HuntingGround? ground)
    {
        if (ground is null)
        {
            return;
        }

        foreach (var c in ground.Components)
        {
            _components.Add(c.Wcid);
        }

        foreach (var s in ground.Supplies.Stock)
        {
            if (!_components.Contains(s.Wcid))
            {
                _kit.Add(s.Wcid);
            }
        }

        foreach (var k in ground.Supplies.Kit)
        {
            if (k.Slot != KitSlot.Pack)
            {
                _kit.Add(k.Wcid);
            }
        }

        foreach (var n in ground.Supplies.Purse.Notes)
        {
            _notes.Add(n.Wcid);
        }
    }

    /// <summary>M5 7 D139 (d): true for an empty mana stone beyond the keep (<see cref="ItemManaKeeper.Shed"/>), which is loot to sell in the main pack, not kit; the process's <see cref="PackMemory.Shed"/> unless set.</summary>
    public Func<uint, bool> Shed { get; init; } = PackMemory.Shared.Shed.Contains;

    /// <summary>The job of the pack an item belongs in (see the class remarks).</summary>
    /// <param name="item">The item.</param>
    /// <param name="keptAside">True when the keep-aside rule kept it as worth a look.</param>
    /// <param name="rotation">True when the self-equip took it off.</param>
    public PackJob JobOf(in PackItem item, bool keptAside, bool rotation)
    {
        if ((item.ItemType & (MoneyType | NoteType)) != 0 || _notes.Contains(item.Wcid))
        {
            return PackJob.Main;
        }

        if ((item.ItemType & ComponentType) != 0 || _components.Contains(item.Wcid))
        {
            return PackJob.Components;
        }

        if ((item.ItemType & ManaStoneType) != 0 && Shed(item.Guid))
        {
            return PackJob.Main;
        }

        if (_kit.Contains(item.Wcid) || (item.ItemType & ManaStoneType) != 0 || item.Name.EndsWith("Healing Kit", StringComparison.OrdinalIgnoreCase) || rotation)
        {
            return PackJob.Kit;
        }

        return keptAside ? PackJob.Review : PackJob.Main;
    }

    /// <summary>Why <see cref="JobOf"/> chose the job, for the log.</summary>
    public string WhyOf(in PackItem item, bool keptAside, bool rotation) => JobOf(item, keptAside, rotation) switch
    {
        PackJob.Main when (item.ItemType & MoneyType) != 0 => "coin",
        PackJob.Main when (item.ItemType & NoteType) != 0 || _notes.Contains(item.Wcid) => "a trade note",
        PackJob.Main when (item.ItemType & ManaStoneType) != 0 => "an empty mana stone beyond the keep, to sell",
        PackJob.Main => "loot to sell",
        PackJob.Components => "a spell component",
        PackJob.Kit when rotation => "taken off by the self-equip",
        PackJob.Kit when (item.ItemType & ManaStoneType) != 0 => "a mana stone",
        PackJob.Kit when _kit.Contains(item.Wcid) => "a line of the kit or the stock",
        PackJob.Kit => "a healing kit",
        _ => "worth a look",
    };

    /// <summary>True when the item may stay in a pack of job <paramref name="where"/>: its own job, or the tolerance of the class remarks.</summary>
    /// <param name="item">The item.</param>
    /// <param name="where">The job of the pack it is in.</param>
    /// <param name="keptAside">True when kept aside.</param>
    /// <param name="rotation">True when the self-equip took it off.</param>
    public bool Belongs(in PackItem item, PackJob where, bool keptAside, bool rotation)
    {
        var job = JobOf(item, keptAside, rotation);
        if (job == where)
        {
            return true;
        }

        return where switch
        {
            PackJob.Kit => (item.ItemType & EquipmentTypes) != 0 && job is PackJob.Main or PackJob.Review,
            PackJob.Review => job == PackJob.Main && (item.ItemType & (MoneyType | NoteType)) == 0 && !_notes.Contains(item.Wcid),
            _ => false,
        };
    }
}

/// <summary>
/// M5 7 D121: the side packs' jobs, chosen stably, and the free slots per job. The rule (Lifestone's own, logged): a pack that already holds items of a job keeps that job (the most such items wins, in the order components, kit, review), and the
/// jobs left go to the packs left in their slot order (ACE's PlacementPosition, kept with the character). With the three side packs empty (the database on 2026-10-29) the first takes the components, the second the kit and the third the review; once
/// filled, the contents keep each pack's job whatever the slot order does. A job with no pack (fewer than three side packs) falls to the main pack, logged.
/// </summary>
public sealed class PackLayout
{
    /// <summary>The items a side pack holds when its description sent no ItemsCapacity: 24, the Pack's and the Sack's (ACE weenies 136 and 166, PropertyInt 6 ItemsCapacity, the pinned world SQL v0.9.296).</summary>
    public const int SidePackCapacity = 24;

    private static readonly PackJob[] SideJobs = [PackJob.Components, PackJob.Kit, PackJob.Review];

    private readonly SidePack?[] _packs = new SidePack?[4];

    private PackLayout(uint owner, int mainCapacity, int mainCount, string why)
    {
        Owner = owner;
        MainCapacity = mainCapacity;
        MainCount = mainCount;
        Why = why;
    }

    /// <summary>The player's id: the main pack's container.</summary>
    public uint Owner { get; }

    /// <summary>The main pack's capacity.</summary>
    public int MainCapacity { get; }

    /// <summary>The main pack's ordinary items now.</summary>
    public int MainCount { get; }

    /// <summary>How each job got its pack, for the log.</summary>
    public string Why { get; }

    /// <summary>The side pack of <paramref name="job"/>, or null (the main pack's job, or no pack left for it).</summary>
    public SidePack? PackOf(PackJob job) => _packs[(int)job];

    /// <summary>The container an item of <paramref name="job"/> goes to: its side pack, else the main pack (the player).</summary>
    public uint ContainerOf(PackJob job) => _packs[(int)job]?.Guid ?? Owner;

    /// <summary>The job of the pack <paramref name="container"/>: a side pack's job, <see cref="PackJob.Main"/> for the player or a pack with no job.</summary>
    public PackJob JobOfContainer(uint container)
    {
        foreach (var job in SideJobs)
        {
            if (_packs[(int)job] is { } p && p.Guid == container)
            {
                return job;
            }
        }

        return PackJob.Main;
    }

    /// <summary>The free slots of the pack of <paramref name="job"/> (the main pack's for <see cref="PackJob.Main"/>, and for a job with no pack).</summary>
    public int FreeSlots(PackJob job) => _packs[(int)job] is { } p ? p.Free : Math.Max(0, MainCapacity - MainCount);

    /// <summary>The word for a job in the log lines.</summary>
    public static string Word(PackJob job) => job switch
    {
        PackJob.Main => "main",
        PackJob.Components => "components",
        PackJob.Kit => "kit",
        _ => "review",
    };

    /// <summary>
    /// The layout: the side packs <paramref name="packs"/> (containers only, in slot order) get their jobs by the class rule from what <paramref name="items"/> already puts in them.
    /// </summary>
    /// <param name="owner">The player's id.</param>
    /// <param name="mainCapacity">The main pack's capacity.</param>
    /// <param name="mainCount">The main pack's ordinary items.</param>
    /// <param name="packs">The side packs, in slot order (no foci).</param>
    /// <param name="items">The held items (main pack and side packs), with their containers.</param>
    /// <param name="rules">The rules.</param>
    /// <param name="keptAside">True for a guid the keep-aside rule kept.</param>
    /// <param name="rotation">True for a guid the self-equip took off.</param>
    public static PackLayout Assign(uint owner, int mainCapacity, int mainCount, IReadOnlyList<SidePack> packs, IReadOnlyList<PackItem> items, PackRules rules, Func<uint, bool> keptAside, Func<uint, bool> rotation)
    {
        ArgumentNullException.ThrowIfNull(packs);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(keptAside);
        ArgumentNullException.ThrowIfNull(rotation);
        var claimed = new bool[packs.Count];
        var why = new StringBuilder();
        var layout = new PackLayout(owner, mainCapacity, mainCount, string.Empty);
        var counts = new int[packs.Count, 4];
        foreach (var item in items)
        {
            for (var p = 0; p < packs.Count; p++)
            {
                if (packs[p].Guid == item.Container)
                {
                    counts[p, (int)rules.JobOf(item, keptAside(item.Guid), rotation(item.Guid))]++;
                }
            }
        }

        foreach (var job in SideJobs)
        {
            var best = -1;
            for (var p = 0; p < packs.Count; p++)
            {
                if (!claimed[p] && counts[p, (int)job] > 0 && (best < 0 || counts[p, (int)job] > counts[best, (int)job]))
                {
                    best = p;
                }
            }

            if (best >= 0)
            {
                claimed[best] = true;
                layout._packs[(int)job] = packs[best];
                Append(why, job, packs[best], string.Create(CultureInfo.InvariantCulture, $"holds {counts[best, (int)job]} item(s) of the job"));
            }
        }

        foreach (var job in SideJobs)
        {
            if (layout._packs[(int)job] is not null)
            {
                continue;
            }

            var next = Array.IndexOf(claimed, false);
            if (next < 0)
            {
                why.Append(why.Length == 0 ? string.Empty : "; ").Append(Word(job)).Append("=the main pack (no side pack left: TODO(m5-pack-short))");
                continue;
            }

            claimed[next] = true;
            layout._packs[(int)job] = packs[next];
            Append(why, job, packs[next], string.Create(CultureInfo.InvariantCulture, $"side slot {next + 1}"));
        }

        var done = new PackLayout(owner, mainCapacity, mainCount, why.ToString());
        Array.Copy(layout._packs, done._packs, done._packs.Length);
        return done;
    }

    private static void Append(StringBuilder why, PackJob job, SidePack pack, string how) =>
        why.Append(why.Length == 0 ? string.Empty : "; ").Append(CultureInfo.InvariantCulture, $"{Word(job)}='{pack.Name}' 0x{pack.Guid:X8} ({how})");

    /// <summary>The layout of the player's packs now (<see cref="Assign"/> over the read model).</summary>
    /// <param name="state">The session's read model.</param>
    /// <param name="rules">The rules.</param>
    /// <param name="keptAside">True for a guid the keep-aside rule kept.</param>
    /// <param name="rotation">True for a guid the self-equip took off.</param>
    public static PackLayout Of(GameState state, PackRules rules, Func<uint, bool> keptAside, Func<uint, bool> rotation)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Assign(state.Self.Guid, MainCapacityOf(state), state.Self.Inventory.Pack.Count, SidePacks(state), Items(state), rules, keptAside, rotation);
    }

    /// <summary>The main pack's capacity: the player weenie's ItemsCapacity when it was sent, else <see cref="GearKit.PackCapacity"/> (as <see cref="MageLootRules.FreeSlots"/>).</summary>
    private static int MainCapacityOf(GameState state) =>
        state.Objects.TryGet(state.Self.Guid, out var self) && self.Weenie.ItemsCapacity is > 0 ? (int)self.Weenie.ItemsCapacity.Value : GearKit.PackCapacity;

    /// <summary>The player's side packs (containers, not foci) in slot order, with their capacity and count.</summary>
    public static List<SidePack> SidePacks(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var inventory = state.Self.Inventory;
        var list = new List<SidePack>(inventory.SidePacks.Count);
        foreach (var entry in inventory.SidePacks)
        {
            if (entry.ContainerType != 1)
            {
                continue; // a foci (type 2) stays in its side slot and is no pack of the layout
            }

            var known = state.Objects.TryGet(entry.Guid, out var o);
            var capacity = known && o!.Weenie.ItemsCapacity is > 0 ? o.Weenie.ItemsCapacity.Value : SidePackCapacity;
            list.Add(new SidePack(entry.Guid, known ? o!.Name : "pack", capacity, inventory.ItemsIn(entry.Guid).Count));
        }

        return list;
    }

    /// <summary>The ordinary items the player holds in the main pack and the side packs (not the side packs and foci themselves, not the worn items), with their containers.</summary>
    public static List<PackItem> Items(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var inventory = state.Self.Inventory;
        var list = new List<PackItem>(inventory.Pack.Count + 24);
        foreach (var entry in inventory.Pack)
        {
            if (entry.ContainerType == 0 && state.Objects.TryGet(entry.Guid, out var o))
            {
                list.Add(new PackItem(entry.Guid, o.Name, o.Weenie.WeenieClassId, o.ItemType, state.Self.Guid));
            }
        }

        foreach (var side in inventory.SidePacks)
        {
            foreach (var entry in inventory.ItemsIn(side.Guid))
            {
                if (entry.ContainerType == 0 && state.Objects.TryGet(entry.Guid, out var o))
                {
                    list.Add(new PackItem(entry.Guid, o.Name, o.Weenie.WeenieClassId, o.ItemType, side.Guid));
                }
            }
        }

        return list;
    }

    /// <summary>
    /// The sort: every item not in a pack its job allows (<see cref="PackRules.Belongs"/>) is moved to the pack of its job, while that pack has a free slot (counted per job as the moves are planned); one that finds its pack full stays, named in
    /// <paramref name="stays"/>. The main pack's items first, in slot order, then each side pack's. The foci and the side packs themselves are never moved.
    /// </summary>
    /// <param name="items">The held items with their containers (<see cref="Items"/>).</param>
    /// <param name="rules">The rules.</param>
    /// <param name="keptAside">True for a guid the keep-aside rule kept.</param>
    /// <param name="rotation">True for a guid the self-equip took off.</param>
    /// <param name="stays">Filled with one line per item that belongs elsewhere but finds its pack full.</param>
    public List<PackMove> Sort(IReadOnlyList<PackItem> items, PackRules rules, Func<uint, bool> keptAside, Func<uint, bool> rotation, List<string>? stays = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(keptAside);
        ArgumentNullException.ThrowIfNull(rotation);
        var free = new int[4];
        for (var j = 0; j < 4; j++)
        {
            free[j] = FreeSlots((PackJob)j);
        }

        var moves = new List<PackMove>();
        foreach (var item in items)
        {
            if ((item.ItemType & PackRules.ContainerType) != 0 || GearKit.MageFociWcids.Contains(item.Wcid))
            {
                continue;
            }

            var from = item.Container == Owner ? PackJob.Main : JobOfContainer(item.Container);
            var aside = keptAside(item.Guid);
            var off = rotation(item.Guid);
            if (rules.Belongs(item, from, aside, off))
            {
                continue;
            }

            var to = rules.JobOf(item, aside, off);
            var container = ContainerOf(to);
            if (container == item.Container)
            {
                continue; // the job has no pack of its own and the item is in the main pack already
            }

            var slot = _packs[(int)to] is null ? (int)PackJob.Main : (int)to;
            if (free[slot] <= 0)
            {
                stays?.Add(string.Create(CultureInfo.InvariantCulture, $"packs: '{item.Name}' 0x{item.Guid:X8} belongs in the {Word(to)} pack ({rules.WhyOf(item, aside, off)}) but it is full: stays in the {Word(from)} pack"));
                continue;
            }

            free[slot]--;
            free[_packs[(int)from] is null ? (int)PackJob.Main : (int)from]++;
            moves.Add(new PackMove(item.Guid, item.Name, from, to, container, rules.WhyOf(item, aside, off)));
        }

        return moves;
    }

    /// <summary>
    /// The free slots per pack as the log and the status print them: <c>packs free: main=&lt;free&gt;/&lt;capacity&gt; components=... kit=... review=...</c> (M5 7 D139 c: "free:" says the numbers are the FREE slots, not the used ones,
    /// which round 13's relay misread).
    /// </summary>
    public string SlotsLine()
    {
        var sb = new StringBuilder("packs free: main=");
        sb.Append(CultureInfo.InvariantCulture, $"{FreeSlots(PackJob.Main)}/{MainCapacity}");
        foreach (var job in SideJobs)
        {
            sb.Append(' ').Append(Word(job)).Append('=');
            if (_packs[(int)job] is { } p)
            {
                sb.Append(CultureInfo.InvariantCulture, $"{p.Free}/{p.Capacity}");
            }
            else
            {
                sb.Append("main");
            }
        }

        return sb.ToString();
    }

    /// <summary>Publishes the free slots per job to <see cref="PackTally"/> (the soak line reads them there, allocation-free).</summary>
    public void Publish() => PackTally.Set(FreeSlots(PackJob.Components), FreeSlots(PackJob.Kit), FreeSlots(PackJob.Review));
}

/// <summary>
/// M5 7 D121: the death's take-back (<see cref="LootRules.TakeAll"/>) with each item put into the pack of its job: the layout is read at each pick-up, so a side pack picked up earlier from the corpse takes its job's items after it; a job
/// whose pack is full or not held takes the main pack.
/// </summary>
/// <param name="rules">The ground's pack rules.</param>
/// <param name="keptAside">True for a guid the keep-aside rule kept.</param>
/// <param name="rotation">True for a guid the self-equip took off.</param>
public sealed class LayoutTakeAll(PackRules rules, Func<uint, bool> keptAside, Func<uint, bool> rotation) : LootRules(LootOptions.AllTypes, 0, long.MaxValue, 0)
{
    /// <inheritdoc/>
    public override uint DestinationOf(WorldObject item, GameState state)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(state);
        if ((item.ItemType & PackRules.ContainerType) != 0 || GearKit.MageFociWcids.Contains(item.Weenie.WeenieClassId))
        {
            return 0; // a side pack or a foci goes back to its side slot (ACE places it)
        }

        var job = rules.JobOf(new PackItem(item.Guid, item.Name, item.Weenie.WeenieClassId, item.ItemType, 0), keptAside(item.Guid), rotation(item.Guid));
        if (job == PackJob.Main)
        {
            return 0;
        }

        var layout = PackLayout.Of(state, rules, keptAside, rotation);
        return layout.PackOf(job) is { Free: > 0 } pack ? pack.Guid : 0;
    }

    /// <inheritdoc/>
    public override string Describe() => "take all, each item into the pack of its job (D121)";
}

/// <summary>M5 7 D121, D125: what the pack layout and the self-equip remember over the process (item guids are unique within a session).</summary>
public sealed class PackMemory
{
    /// <summary>The pieces the self-equip took off for an upgrade: kept in the kit pack as rotation gear and never sold.</summary>
    public HashSet<uint> Rotation { get; } = [];

    /// <summary>The finds the self-equip judged interesting but did not wear (to the review pack, never sold).</summary>
    public HashSet<uint> Interesting { get; } = [];

    /// <summary>M5 7 D139 (d): the empty mana stones beyond the keep (<see cref="ItemManaKeeper.Shed"/>): loot to sell in the main pack, never moved back to the kit pack.</summary>
    public HashSet<uint> Shed { get; } = [];

    /// <summary>The memory of the run.</summary>
    public static PackMemory Shared { get; } = new();
}

/// <summary>
/// M5 7 D121, D125 (report-only): the side packs' free slots last published (<see cref="PackLayout.Publish"/>) and the gear counters, for the soak line's <c>free_components= free_kit= free_review= self_equips= stone_refills= stone_captures=</c>.
/// Read without allocating; -1 before a layout was published.
/// </summary>
public static class PackTally
{
    private static long _components = -1;
    private static long _kit = -1;
    private static long _review = -1;
    private static long _equips;
    private static long _refills;
    private static long _captures;

    /// <summary>The components pack's free slots, or -1.</summary>
    public static long FreeComponents => Interlocked.Read(ref _components);

    /// <summary>The kit pack's free slots, or -1.</summary>
    public static long FreeKit => Interlocked.Read(ref _kit);

    /// <summary>The review pack's free slots, or -1.</summary>
    public static long FreeReview => Interlocked.Read(ref _review);

    /// <summary>The pieces the self-equip put on over the process.</summary>
    public static long SelfEquips => Interlocked.Read(ref _equips);

    /// <summary>The charged mana stones used on the player over the process.</summary>
    public static long StoneRefills => Interlocked.Read(ref _refills);

    /// <summary>The empty mana stones used on a junk magic item over the process.</summary>
    public static long StoneCaptures => Interlocked.Read(ref _captures);

    /// <summary>Sets the free slots.</summary>
    public static void Set(int components, int kit, int review)
    {
        Interlocked.Exchange(ref _components, components);
        Interlocked.Exchange(ref _kit, kit);
        Interlocked.Exchange(ref _review, review);
    }

    /// <summary>Counts a piece put on.</summary>
    public static void AddEquip() => Interlocked.Increment(ref _equips);

    /// <summary>Counts a refill.</summary>
    public static void AddRefill() => Interlocked.Increment(ref _refills);

    /// <summary>Counts a capture.</summary>
    public static void AddCapture() => Interlocked.Increment(ref _captures);
}
