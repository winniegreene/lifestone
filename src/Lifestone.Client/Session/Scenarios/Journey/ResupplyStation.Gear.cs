using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M5 7 D121, D122, D125: the home visit's gear phase, at the lifestone in peace before the walk and once more after the buys. (1) The packs sorted to their jobs (<see cref="PackLayout.Sort"/>). (2) The worn pieces, the side packs' gear,
/// the stones and the main pack's casters and wearables appraised (bounded). (3) The footwear (<see cref="SelfEquipJudge.PickFootwear"/>): only while the feet are free; a robe that covers them is the logged reason. (4) The self-equip
/// (<see cref="SelfEquipJudge"/>): a better caster swapped in (the old one to the kit pack: rotation gear), a wearable with a useful cantrip worn in a free slot, any other interesting find to the review pack. (5) The worn items' mana
/// (<see cref="ItemManaKeeper"/>): a charged stone on the player when a piece runs low, an empty stone on the main pack's junk magic item while the kit is short of charged stones. A kit trip (the death's rearm) runs none of it.
/// </summary>
public sealed partial class ResupplyRun
{
    /// <summary>The most appraisals the gear phase asks for in one visit.</summary>
    public const int GearAppraisalCap = 40;

    /// <summary>The most stone uses in one visit (each is a refill or a capture, appraised again after).</summary>
    public const int MaxStoneUses = 6;

    /// <summary>The items the sorts moved to their pack.</summary>
    public int PackMoves { get; private set; }

    /// <summary>The pieces the self-equip put on (a caster swapped in counts).</summary>
    public int SelfEquips { get; private set; }

    /// <summary>The charged stones used on the player.</summary>
    public int StoneRefills { get; private set; }

    /// <summary>The empty stones used on a junk item.</summary>
    public int StoneCaptures { get; private set; }

    /// <summary>The shoe step's finding: <c>worn</c>, <c>wearing</c>, <c>covered</c> (a worn piece covers the feet), <c>none</c> (nothing fits) or <c>n/a</c> (not run).</summary>
    public string Shoes { get; private set; } = "n/a";

    /// <summary>The gear phase's verdict fields (report-only in their first gate).</summary>
    public string GearFields() => string.Create(CultureInfo.InvariantCulture, $"pack_moves={PackMoves} self_equips={SelfEquips} stone_refills={StoneRefills} stone_captures={StoneCaptures} shoes={Shoes} stones_kept={StonesKept} stones_shed={StonesShed} stones_sold={StonesSold}");

    private readonly List<uint> _shedGuids = [];

    /// <summary>M5 7 D139 (d): the stones this trip shed that left the inventory since (sold, or dropped where no vendor buys them).</summary>
    public int StonesSold => _shedGuids.Count(g => !S.State.Self.Inventory.Contains(g));

    private PackRules? _packRules;

    private PackLayout Layout(HuntingGround ground)
    {
        _packRules ??= new PackRules(ground);
        var layout = PackLayout.Of(S.State, _packRules, _memory.KeptAside.Contains, PackMemory.Shared.Rotation.Contains);
        layout.Publish();
        return layout;
    }

    /// <summary>
    /// M5 7 D121: moves each item not in a pack its job allows to its pack, one move at a time, each logged (<see cref="PackMove"/>); the items whose pack is full are named and stay. The layout and the free slots per pack are logged before.
    /// </summary>
    internal IEnumerable<ActionHandle?> SortPacks(HuntingGround ground, string why)
    {
        var layout = Layout(ground);
        Log(Text($"packs: layout ({why}): {layout.Why}; {layout.SlotsLine()}"));
        var stays = new List<string>();
        var moves = layout.Sort(PackLayout.Items(S.State), _packRules!, _memory.KeptAside.Contains, PackMemory.Shared.Rotation.Contains, stays);
        foreach (var line in stays)
        {
            Log(line);
        }

        foreach (var move in moves)
        {
            if (!S.State.Self.Inventory.Contains(move.Guid))
            {
                continue;
            }

            var handle = S.Actions.MoveToPack(move.Guid, move.Container, 0, ActionSource.Automation);
            yield return handle;
            if (handle.Outcome is { Kind: ActionOutcomeKind.Confirmed })
            {
                PackMoves++;
                Log(move + ": done");
            }
            else
            {
                Log(move + ": " + Describe(handle) + " (counted, it stays where it is)");
            }
        }

        Log(Text($"packs: sorted ({why}): {moves.Count} move(s), {stays.Count} left for a full pack; {Layout(ground).SlotsLine()}"));
    }

    /// <summary>
    /// The gear phase (see the class remarks). M5 7 D139 (m): the caster pick runs before the sort, so the best usable caster is in the hands before the sort sends the casters worth a look to the review pack (round 13, 03:06:29 UTC: the
    /// sort moved the Fire Sceptre to the review pack and the pick wanted it 7 s later); (d) the mana stones beyond the keep go to the sale before the pick (they had filled the kit pack) and once more after the mana's refills.
    /// </summary>
    internal IEnumerable<ActionHandle?> GearPhase(HuntingGround ground)
    {
        Stage = "gear";
        foreach (var h in AppraiseGear()) { yield return h; }

        var skills = new QualitiesSkills(S.State.Self.Qualities);
        var judge = Judge(ground);
        foreach (var h in ShedStones(ground, "the visit's start")) { yield return h; }

        foreach (var h in SwapBestCaster(ground, judge, skills)) { yield return h; }

        foreach (var h in SortPacks(ground, "the visit's start")) { yield return h; }

        foreach (var h in WearShoes(skills)) { yield return h; }

        foreach (var h in SelfEquip(ground, judge, skills)) { yield return h; }

        foreach (var h in KeepItemMana(ground)) { yield return h; }

        foreach (var h in ShedStones(ground, "after the mana")) { yield return h; }

        Log("gear: done: " + GearFields());
    }

    /// <summary>The self-equip's judge for the ground's plan: its war element and the keep-aside block's useful cantrips.</summary>
    private SelfEquipJudge Judge(HuntingGround ground)
    {
        var family = ground.PlanFor(S.State.Self.Qualities.Level)?.Family;
        var element = SelfEquipJudge.ElementOf(family);
        if (element == 0)
        {
            Log($"gear: TODO(m5-self-equip-element): the plan's war family '{family ?? "none"}' has no element in the table: no caster counts as better by its bonus");
        }

        return new SelfEquipJudge(ground.Loot?.KeepAside?.SpellNames() ?? [], element, id => Context.Spells.TryGetValue(id, out var s) ? s.Name : null);
    }

    /// <summary>The held items the gear phase reads: the worn pieces, the side packs' items, the stones and the main pack's casters, wearables and magic items, that no appraisal of is held.</summary>
    private IEnumerable<ActionHandle?> AppraiseGear()
    {
        var asked = 0;
        var wanted = new List<uint>();
        foreach (var e in S.State.Self.Inventory.Equipped)
        {
            wanted.Add(e.Guid);
        }

        foreach (var item in PackLayout.Items(S.State))
        {
            if (KeepAsideRule.NeedsAppraisal(item.ItemType) || (item.ItemType & PackRules.ManaStoneType) != 0)
            {
                wanted.Add(item.Guid);
            }
        }

        foreach (var guid in wanted)
        {
            if (asked >= GearAppraisalCap || S.TryGetAppraisal(guid, out _))
            {
                continue;
            }

            asked++;
            yield return S.Actions.Identify(guid, ActionSource.Host);
        }

        Log(Text($"gear: appraised {asked} worn or held piece(s) (at most {GearAppraisalCap} a visit)"));
    }

    /// <summary>Re-appraises one item (the appraisals are a snapshot: a worn piece's mana and a stone's change as they are used).</summary>
    private IEnumerable<ActionHandle?> Reappraise(uint guid)
    {
        if (guid != 0 && S.State.Self.Inventory.Contains(guid))
        {
            yield return S.Actions.Identify(guid, ActionSource.Host);
        }
    }

    /// <summary>M5 7 D122: the footwear from the mage's own items, only while the feet are free (see <see cref="SelfEquipJudge.PickFootwear"/>).</summary>
    private IEnumerable<ActionHandle?> WearShoes(IGearSkills skills)
    {
        var worn = SelfEquipJudge.Worn(S.State);
        var owned = HeldCandidates().Where(c => (c.ValidLocations & SelfEquipJudge.FootWear) != 0 && (c.ItemType & SelfEquipJudge.CasterType) == 0).ToList();
        var pick = SelfEquipJudge.PickFootwear(owned, worn, skills, out var why);
        if (pick is null)
        {
            Shoes = why.StartsWith("feet covered by", StringComparison.Ordinal) ? "covered" : why.StartsWith("the mage wears", StringComparison.Ordinal) ? "wearing" : "none";
            Log("gear: shoes: " + why); // M5 7 D129: no shoe is ever bought (m5-shoes-vendor closed); looted footwear is loot like anything else
            yield break;
        }

        Log("gear: shoes: wear " + why);
        var handle = S.Actions.Wield(pick.Guid, (EquipMask)SelfEquipJudge.FootWear, ActionSource.Automation);
        yield return handle;
        Shoes = handle.Outcome is { Kind: ActionOutcomeKind.Confirmed } ? "worn" : "refused";
        if (Shoes == "worn")
        {
            SelfEquips++;
            PackTally.AddEquip();
        }

        Log(Text($"gear: shoes: '{pick.Name}' 0x{pick.Guid:X8}: {Describe(handle)}"));
    }

    private List<GearCandidate> HeldCandidates()
    {
        var list = new List<GearCandidate>();
        foreach (var item in PackLayout.Items(S.State))
        {
            if (S.State.Objects.TryGet(item.Guid, out var o) && KeepAsideRule.NeedsAppraisal(o.ItemType))
            {
                list.Add(SelfEquipJudge.CandidateOf(S.State, o));
            }
        }

        return list;
    }

    /// <summary>M5 7 D125: the self-equip over every held wearable (see <see cref="SelfEquipJudge"/>); the caster is the one pick's (<see cref="SwapBestCaster"/>, run before the sort).</summary>
    private IEnumerable<ActionHandle?> SelfEquip(HuntingGround ground, SelfEquipJudge judge, IGearSkills skills)
    {
        foreach (var candidate in HeldCandidates())
        {
            if (!S.State.Self.Inventory.Contains(candidate.Guid) || S.State.Self.Inventory.Find(candidate.Guid).Kind == InventoryPlaceKind.Equipped)
            {
                continue;
            }

            var choice = judge.Judge(candidate, SelfEquipJudge.Worn(S.State), skills);
            if (choice.Kind == SelfEquipKind.None || (choice.Kind is SelfEquipKind.Swap or SelfEquipKind.Wear && (candidate.ItemType & SelfEquipJudge.CasterType) != 0))
            {
                continue; // M5 7 S2c: a caster is wielded only by the one pick below, never item by item
            }

            Log(choice.ToString());
            if (choice.Kind == SelfEquipKind.Wear)
            {
                var on = S.Actions.Wield(choice.Guid, (EquipMask)choice.Location, ActionSource.Automation);
                yield return on;
                Log(Text($"gear: wear '{choice.Name}' 0x{choice.Guid:X8}: {Describe(on)}"));
                if (on.Outcome is { Kind: ActionOutcomeKind.Confirmed })
                {
                    SelfEquips++;
                    PackTally.AddEquip();
                }
            }
            else if (choice.Kind == SelfEquipKind.Review && S.State.Self.Inventory.Find(candidate.Guid).Kind == InventoryPlaceKind.MainPack)
            {
                PackMemory.Shared.Interesting.Add(candidate.Guid);
                _memory.KeptAside.Add(candidate.Guid); // never offered to a vendor
                var review = Layout(ground).PackOf(PackJob.Review);
                if (review is { Free: > 0 } pack)
                {
                    var move = S.Actions.MoveToPack(candidate.Guid, pack.Guid, 0, ActionSource.Automation);
                    yield return move;
                    Log(Text($"gear: '{candidate.Name}' 0x{candidate.Guid:X8} to the review pack 0x{pack.Guid:X8}: {Describe(move)}"));
                }
            }
        }
    }

    /// <summary>
    /// M5 7 S2c: the one caster swap of the visit: the best of every caster held (<see cref="SelfEquipJudge.BestCaster"/>), the wielded caster to a pack with room, the pick wielded and confirmed. The rehearsal of 2026-10-29 sent a swap
    /// item by item and only the first (a <c>swapped</c> flag), so it wielded an Electric Sceptre and logged three better "swap in" lines it never sent.
    /// <para>M5 7 D139 (k): the held caster goes only to a pack with a free slot by the client's count (<see cref="HeldCaster.SwapPack"/>: the kit pack, else the main pack, else the review pack); with none, no swap, logged. ACE never
    /// swaps the held one out by itself (a wield into the occupied held slot is refused, Player_Inventory.cs 1800-1810), so the move comes first. (l): a refused move of the held caster is re-checked against the hands the server
    /// left (ACE's fallback puts it in the main pack, not back in the hands): with the hands empty the pick is wielded at once, and whatever fails, the hands end with a caster (<see cref="RearmHands"/>).</para>
    /// </summary>
    private IEnumerable<ActionHandle?> SwapBestCaster(HuntingGround ground, SelfEquipJudge judge, IGearSkills skills)
    {
        var casters = HeldCandidates().Where(c => (c.ItemType & SelfEquipJudge.CasterType) != 0 && S.State.Self.Inventory.Find(c.Guid).Kind != InventoryPlaceKind.Equipped).ToList();
        var choice = judge.BestCaster(casters, SelfEquipJudge.Worn(S.State), skills, out var why);
        Log(why);
        if (choice is null)
        {
            foreach (var h in RearmHands(judge, "the caster pick")) { yield return h; }
            yield break;
        }

        Log(choice.ToString());
        if (choice.Replaces != 0)
        {
            var into = HeldCaster.SwapPack(Layout(ground), out var where);
            if (into == 0)
            {
                Log(Text($"gear: no swap: the held caster 0x{choice.Replaces:X8} has no pack to go to: {where}; '{choice.Name}' 0x{choice.Guid:X8} stays in its pack"));
                yield break;
            }

            Log(Text($"gear: the held caster 0x{choice.Replaces:X8} to {where}"));
            PackMemory.Shared.Rotation.Add(choice.Replaces); // rotation gear wherever it lands: never sold
            var off = S.Actions.MoveToPack(choice.Replaces, into, 0, ActionSource.Automation);
            yield return off;
            if (off.Outcome is not { Kind: ActionOutcomeKind.Confirmed })
            {
                var heldNow = S.State.Self.Inventory.EquippedAt(SelfEquipJudge.Held);
                Log(Text($"gear: the held caster 0x{choice.Replaces:X8} did not go to {where}: {Describe(off)}; the hands hold {(heldNow is { } g ? $"0x{g:X8}" : "nothing")} now (the server's updates)"));
                if (HeldCaster.Held(S.State))
                {
                    Log("gear: no swap: the old caster is still held");
                    yield break;
                }

                Log(Text($"gear: the hands hold no caster after the refused move (ACE's fallback put 0x{choice.Replaces:X8} in a pack, Player_Inventory.cs 1335): wield the pick at once"));
            }
        }

        var on = S.Actions.Wield(choice.Guid, (EquipMask)SelfEquipJudge.Held, ActionSource.Automation);
        yield return on;
        var held = S.State.Self.Inventory.EquippedAt(SelfEquipJudge.Held);
        Log(Text($"gear: wield '{choice.Name}' 0x{choice.Guid:X8}: {Describe(on)}; held now 0x{held ?? 0:X8}"));
        if (on.Outcome is { Kind: ActionOutcomeKind.Confirmed } && held == choice.Guid)
        {
            SelfEquips++;
            PackTally.AddEquip();
            yield break;
        }

        if (choice.Replaces != 0 && !HeldCaster.Held(S.State))
        {
            var back = S.Actions.Wield(choice.Replaces, (EquipMask)SelfEquipJudge.Held, ActionSource.Automation);
            yield return back;
            Log(Text($"gear: the old caster 0x{choice.Replaces:X8} wielded again: {Describe(back)}"));
        }

        foreach (var h in RearmHands(judge, "the failed swap")) { yield return h; }
    }

    /// <summary>
    /// M5 7 D139 (l): when the hands hold no caster (read from the server's updates: a refused move of the held caster empties them), the best caster held in the packs is wielded (<see cref="HeldCaster.Rearm"/>), logged with
    /// <paramref name="after"/>; nothing is sent while a caster is held.
    /// </summary>
    private IEnumerable<ActionHandle?> RearmHands(SelfEquipJudge judge, string after)
    {
        if (HeldCaster.Held(S.State))
        {
            yield break;
        }

        if (HeldCaster.Rearm(S.State, judge, out var why) is not { } pick)
        {
            Log($"gear: the hands hold no caster after {after} and none can be wielded: {why} (TODO(m5-no-caster): the magic combat mode fails with \"no caster held\")");
            yield break;
        }

        Log($"gear: the hands hold no caster after {after}: wield {why}");
        var on = S.Actions.Wield(pick.Guid, (EquipMask)SelfEquipJudge.Held, ActionSource.Automation);
        yield return on;
        Log(Text($"gear: rearm '{pick.Name}' 0x{pick.Guid:X8}: {Describe(on)}; a caster held now: {(HeldCaster.Held(S.State) ? "yes" : "no")}"));
    }

    /// <summary>The empty mana stones this trip put out for sale (<see cref="ShedStones"/>).</summary>
    public int StonesShed { get; private set; }

    /// <summary>The mana stones kept at the last <see cref="ShedStones"/> (the unappraised ones included).</summary>
    public int StonesKept { get; private set; }

    /// <summary>
    /// M5 7 D139 (d): the empty mana stones beyond the keep (<see cref="ItemManaKeeper.Shed"/>) go to the sale: each is marked (<see cref="PackMemory.Shed"/>, so the sort leaves it in the main pack and the sale offers it) and
    /// moved from its side pack to the main pack while the main pack has a free slot. With no home vendor that buys mana stones (ItemType 0x80000 in his mask: Archmage Cindrue's 0x000CB000 at Holtburg) each is dropped at the home
    /// instead. Round 13 (03:06 UTC) held 23 empty stones against the keep of 4: the kit pack had no free slot, which the caster swap then fell into.
    /// </summary>
    private IEnumerable<ActionHandle?> ShedStones(HuntingGround ground, string why)
    {
        var stones = new List<HeldStone>();
        var unread = 0;
        var where = new Dictionary<uint, uint>();
        foreach (var item in PackLayout.Items(S.State))
        {
            if ((item.ItemType & PackRules.ManaStoneType) == 0)
            {
                continue;
            }

            if (!S.TryGetAppraisal(item.Guid, out var a))
            {
                unread++;
                continue;
            }

            stones.Add(new HeldStone(item.Guid, item.Name, a.ItemMana, a.Float(ItemEfficiencyFloat) ?? ItemManaKeeper.EfficiencyByWcid.GetValueOrDefault(item.Wcid)));
            where[item.Guid] = item.Container;
        }

        foreach (var s in stones.Where(s => s.Charged))
        {
            PackMemory.Shared.Shed.Remove(s.Guid); // a stone that captured mana since is kit again
        }

        var kit = Layout(ground).ContainerOf(PackJob.Kit);
        var shed = ItemManaKeeper.Shed(stones, guid => where.GetValueOrDefault(guid) == kit, out var keepWhy);
        StonesKept = stones.Count - shed.Count + unread;
        Log(Text($"gear stones ({why}): {keepWhy}{(unread > 0 ? $"; {unread} not appraised, kept" : string.Empty)}"));
        if (shed.Count == 0)
        {
            yield break;
        }

        var buyer = ground.Vendors.FirstOrDefault(v => (v.BuysItemTypes & PackRules.ManaStoneType) != 0);
        foreach (var stone in shed)
        {
            if (!S.State.Self.Inventory.Contains(stone.Guid))
            {
                continue;
            }

            PackMemory.Shared.Shed.Add(stone.Guid);
            var again = _shedGuids.Contains(stone.Guid);
            if (!again)
            {
                _shedGuids.Add(stone.Guid);
                StonesShed++;
            }

            if (buyer is null)
            {
                var drop = S.Actions.Drop(stone.Guid, ActionSource.Automation);
                yield return drop;
                Log(Text($"gear stones: '{stone.Name}' 0x{stone.Guid:X8} empty beyond the keep: no vendor of {ground.Home.Town} buys mana stones: dropped at the home: {Describe(drop)}"));
                continue;
            }

            var from = where.GetValueOrDefault(stone.Guid);
            if (from == S.State.Self.Guid)
            {
                if (!again)
                {
                    Log(Text($"gear stones: '{stone.Name}' 0x{stone.Guid:X8} empty beyond the keep: in the main pack already, for {buyer.Name}"));
                }

                continue; // a stone this visit shed already is not named twice
            }

            if (Layout(ground).FreeSlots(PackJob.Main) <= 0)
            {
                Log(Text($"gear stones: '{stone.Name}' 0x{stone.Guid:X8} empty beyond the keep: the main pack is full, it waits in its pack 0x{from:X8}"));
                continue;
            }

            var move = S.Actions.MoveToPack(stone.Guid, S.State.Self.Guid, 0, ActionSource.Automation);
            yield return move;
            Log(Text($"gear stones: '{stone.Name}' 0x{stone.Guid:X8} empty beyond the keep: from 0x{from:X8} to the main pack, for {buyer.Name}: {Describe(move)}"));
        }
    }

    /// <summary>M5 7 D125: the worn items' mana kept with the mage's stones (see <see cref="ItemManaKeeper"/>).</summary>
    private IEnumerable<ActionHandle?> KeepItemMana(HuntingGround ground)
    {
        for (var use = 0; use < MaxStoneUses; use++)
        {
            var worn = ItemManaKeeper.WornOf(S.State);
            var stones = new List<HeldStone>();
            var junk = new List<DrainTarget>();
            foreach (var item in PackLayout.Items(S.State))
            {
                if (!S.TryGetAppraisal(item.Guid, out var a))
                {
                    continue;
                }

                if ((item.ItemType & PackRules.ManaStoneType) != 0)
                {
                    stones.Add(new HeldStone(item.Guid, item.Name, a.ItemMana, a.Float(ItemEfficiencyFloat) ?? ItemManaKeeper.EfficiencyByWcid.GetValueOrDefault(item.Wcid))); // M5 7 S2b: the efficiency from the appraisal (PropertyFloat 87, assessed), else the world SQL table
                }
                else if (item.Container == S.State.Self.Guid && a.ItemMana is > 0 && a.ItemMaxMana is > 0 && !_memory.KeptAside.Contains(item.Guid) && !PackMemory.Shared.Rotation.Contains(item.Guid)
                    && a.Bool(RetainedBool) != true && _packRules!.JobOf(item, false, false) == PackJob.Main && (item.ItemType & (PackRules.MoneyType | PackRules.NoteType)) == 0)
                {
                    var value = S.State.Objects.TryGet(item.Guid, out var o) ? o.Weenie.Value ?? 0 : 0;
                    junk.Add(new DrainTarget(item.Guid, item.Name, a.ItemMana.Value, a.ItemMaxMana.Value, value, Payout(ground, item.ItemType, value))); // M5 7 S2b: the payout the drain gives up
                }
            }

            var weighed = new List<string>();
            var step = ItemManaKeeper.Next(worn, stones, junk, S.State.Self.Guid, weighed);
            foreach (var line in weighed)
            {
                Log(line); // M5 7 S2b: every junk item weighed, the mana's worth against the payout
            }

            Log(step.ToString());
            if (step.Act == ItemManaAct.None)
            {
                yield break;
            }

            var handle = S.Actions.UseManaStone(step.Stone, step.Target, ActionSource.Automation);
            yield return handle;
            Log(Text($"gear mana: the stone 0x{step.Stone:X8}: {Describe(handle)}"));
            if (handle.Outcome is not { Kind: ActionOutcomeKind.Confirmed })
            {
                yield break;
            }

            if (step.Act == ItemManaAct.Refill)
            {
                StoneRefills++;
                PackTally.AddRefill();
                foreach (var w in worn)
                {
                    foreach (var h in Reappraise(w.Guid)) { yield return h; }
                }
            }
            else
            {
                StoneCaptures++;
                PackTally.AddCapture();
                foreach (var h in Until(() => !S.State.Self.Inventory.Contains(step.Target), 3)) { yield return h; }
            }

            foreach (var h in Reappraise(step.Stone)) { yield return h; }
        }
    }

    /// <summary>ACE's PropertyFloat ItemEfficiency (87), an assessment property (PropertyFloat.cs:102-103).</summary>
    private const uint ItemEfficiencyFloat = 87;

    /// <summary>D96's payout at the ground's best buyer of the type (<see cref="MageLootThresholds.PayoutOf"/>).</summary>
    private static long Payout(HuntingGround ground, uint itemType, long value) => new MageLootThresholds { Buyers = ground.LootBuyers }.PayoutOf(itemType, value);

    /// <summary>ACE's PropertyBool Retained (91): a retained item refuses a stone's drain (ManaStone.cs:91-92).</summary>
    private const uint RetainedBool = 91;
}
