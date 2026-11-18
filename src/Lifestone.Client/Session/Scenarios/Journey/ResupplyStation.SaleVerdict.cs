using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 16 E5c (docs/automation-engine.md E5; m6-plan.md D139): the town trip's sale list from the rule book's one verdict (<see cref="SaleVerdict.Of"/>): an item is offered when its verdict is sell, and for no other reason. The
/// trip's part is the context (<see cref="SaleBook"/>), and the lines its filters logged and the memory they wrote, each keyed on the step that decided (<see cref="SaleStep"/>). M6 16 Q11: the old list and the guard that held it
/// beside the verdict's are gone.
/// </summary>
public sealed partial class ResupplyRun
{
    /// <summary>
    /// The items the sale offers: every main-pack item, and every component, healing kit and essence in a side pack, whose verdict is sell (<see cref="SaleVerdict.Of"/>). M5 7 G1 (edge audit items 2 and 3): the loot rule's kept
    /// list does not decide, so loot kept before a death, a reconnect or a restart is sold too.
    /// </summary>
    private List<(uint Guid, WorldObject Object)> SaleItems(HuntingGround ground)
    {
        return SaleItemsByVerdict(ground);
    }

    /// <summary>
    /// The verdict's context for this trip's sale: the session's (<see cref="SaleContext.For"/>) with the trip's own parts named over it: the appraisals through the session view, the casters and stones of this sale's gear keeps
    /// (worked out once a sale and logged, <see cref="GearKeepsNow"/>), the scroll rule with the every-spell rule's keeps, the leather set, the drink stacks the pack budget cut, and the components rule that logs each verdict once a trip.
    /// </summary>
    /// <param name="ground">The ground.</param>
    /// <param name="keepAside">The keep-aside rule in force.</param>
    /// <param name="floorCasters">The casters the gear rules keep (<see cref="FloorCasters"/>).</param>
    private SaleContext SaleBook(HuntingGround ground, KeepAsideRule? keepAside, HashSet<uint> floorCasters) =>
        SaleContext.For(S.State, ground, Context.Character, Context.Spells, _memory) with
        {
            Appraisal = guid => S.TryGetAppraisal(guid, out var appraisal) ? appraisal : null,
            KeepAside = keepAside,
            SpellName = SpellNameOf,
            CasterNeed = guid => GearKeepsNow().Casters(guid),
            StoneKept = StoneKeptElseShed, // the one part that writes: a stone beyond the keep is noted and named where the sale's filter did it
            FloorCaster = floorCasters.Contains,
            ScrollKeep = ScrollKeepReason,
            LeatherKeeps = LeatherKeeps,
            DrinkSurplus = _drinkSales.Contains,
            Component = (guid, o) => ComponentVerdict(ground, guid, o),
            OffersRefusedClasses = true, // M6 16 E5c2, kept by Q11: the trip offers a Bread or an Arrow as it always did (ACE refuses it, and the second trip's refusal drops it: NoteRefusal); the guard unsellable-class is the other askers'
        };

    /// <summary>
    /// M6 16 E5c2: the verdict's context for the trip's reads that are no sale: the junk drop (<see cref="DropJunk"/>) and the item mana drain's never-list (<c>DrainCandidates</c>). The session's context as the pack stands
    /// (the gear rules' casters and stones worked out afresh, with no line) with the trip's read-only parts: the appraisals through the session view, the scroll rule with the every-spell rule's keeps, the leather set and the
    /// drinks the budget put on the sale. It writes no memory and no line: a stone beyond the keep is not noted as shed here. The junk drop names the components rule over it, which says each component once a trip as the
    /// sale does; the drain names the gear keeps it logs.
    /// </summary>
    private SaleContext ReadBook(HuntingGround ground) =>
        SaleContext.For(S.State, ground, Context.Character, Context.Spells, _memory) with
        {
            Appraisal = guid => S.TryGetAppraisal(guid, out var appraisal) ? appraisal : null,
            ScrollKeep = ScrollKeepReason,
            LeatherKeeps = LeatherKeeps,
            DrinkSurplus = _drinkSales.Contains,
        };

    /// <summary>
    /// True for a mana stone the kit keeps (its target, the charged first: M5 7 D139 (d), M6 14; Archmage Cindrue's mask buys mana stones). One it does not keep is noted as shed in the layout's memory, and named the first time: it
    /// is loot to sell.
    /// </summary>
    private bool StoneKeptElseShed(uint guid)
    {
        if (GearKeepsNow().Stones(guid))
        {
            return true;
        }

        if (PackMemory.Shared.Shed.Add(guid))
        {
            Log($"sale: '{(S.State.Objects.TryGet(guid, out var o) ? o.Name : "?")}' 0x{guid:X8} is a mana stone beyond the kit's target of {ItemManaKeeper.StoneTarget} (the charged first): sell loot (M6 14)");
        }

        return false;
    }

    /// <summary>The sale list by the verdict.</summary>
    private List<(uint Guid, WorldObject Object)> SaleItemsByVerdict(HuntingGround ground)
    {
        var items = new List<(uint, WorldObject)>();
        if (!_options.Loot)
        {
            Log("sale: looting off (D38): no walk to the loot buyer, sold=0");
            return items;
        }

        var keepAside = KeepAsideRule.For(ground, Context.Character); // M6 14 (m6-lore-cantrips): the profile's block, else the climber's default: a great-cantrip piece is never offered
        _gearKeeps = null; // M6 14 (the owner, 2026-11-16): the casters and stones her gear rules keep, worked out afresh for this sale
        var book = SaleBook(ground, keepAside, FloorCasters(ground, keepAside));
        var notOffered = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (guid, o, inSidePack) in PackItems().Select(p => (p.Guid, p.Object, false)).Concat(SidePackComponents().Select(p => (p.Guid, p.Object, true)))) // M6 14 pack rules: the components in the side packs too
        {
            var caster = (o.ItemType & SelfEquipJudge.CasterType) != 0;
            if (!caster && S.TryGetAppraisal(guid, out var appraisal) && KeepAsideRule.NeedsAppraisal(o.ItemType) && book.Cantrips.GreatOf(appraisal, SpellNameOf) is { Count: > 0 } verdicts
                && CantripVerdictLog.Once(guid, o.Name, verdicts, "the sale") is { } line)
            {
                Log(line); // M6 14 useful cantrips (the owner, 2026-11-16): only a useful great cantrip keeps a piece from the sale, her skills read live
            }

            var answer = SaleVerdict.Of(guid, o, inSidePack, book);
            NoteSaleAnswer(guid, o, caster, answer, notOffered);
            if (answer.Action == PackAction.Sell)
            {
                items.Add((guid, o)); // the verdict, and nothing else, puts an item on the sale list
            }
        }

        if (notOffered.Count > 0 && !_saleKeptLogged)
        {
            _saleKeptLogged = true; // once a trip
            Log(Text($"sale: kept {notOffered.Values.Sum()} ({string.Join(", ", notOffered.Select(p => $"{p.Value} {p.Key}"))}): not offered: ACE refuses the retained and the value-0 ones (Player_Commerce.cs:268-280 @95ef426d); Lifestone keeps the worn, the attuned (ACE buys an attuned piece and only stops the vendor reselling it, Vendor.cs:626-627: they are kept for the return to their NPC) and the floor's casters"));
        }

        return items;
    }

    /// <summary>What the trip logs and remembers of one verdict, by the step that decided it: the lines and the memory the sale's filters wrote before M6 16 E5c, unchanged.</summary>
    private void NoteSaleAnswer(uint guid, WorldObject o, bool caster, SaleAnswer answer, SortedDictionary<string, int> notOffered)
    {
        if (answer.Step == SaleStep.KeptAside)
        {
            _memory.KeptAside.Add(guid); // M5 7 F30: the note outlives the appraisal (a restart empties the ledger; the rule finds the item again)
            Log($"sale: '{o.Name}' 0x{guid:X8} is kept aside ({answer.Detail}), worth a look: not offered");
            return;
        }

        if (caster)
        {
            _memory.KeptAside.Remove(guid); // M6 14 (the owner, 2026-11-16): a caster her gear rules do not need is sell loot, whatever the loot step noted
        }

        switch (answer.Step)
        {
            case SaleStep.Leather:
                _leatherKept++;
                if (_leatherKept == 1)
                {
                    Log($"sale: '{o.Name}' 0x{guid:X8} is a piece of the leather set and the swap is still to come: kept, not offered (every such piece is kept; said once a trip)");
                }

                break;
            case SaleStep.DrinkKept:
                _drinksKept++;
                if (_drinksKept == 1)
                {
                    Log($"sale: '{o.Name}' 0x{guid:X8} is a drink she uses (health, mana or stamina): kept, not offered, whatever this ground's stock lists (every such drink is kept, but the ones over the pack budget's cut keeps, m6-pack-room; said once a trip; m6-sale-keeps-drinks)");
                }

                break;
            case SaleStep.Unsellable:
                notOffered[answer.Detail!] = notOffered.GetValueOrDefault(answer.Detail!) + 1; // M6 2 d: never offered, counted by reason
                break;
            case SaleStep.Table:
                // M6 14 pack rules (PackRuleBook): every other item gets its class and rule, logged once a trip (a component is named by the components rule's own line)
                if (!answer.IsComponent && _ruleLogged.Add(guid))
                {
                    Log(PackRuleBook.Line(answer.Class, answer.Verdict, o.Name, guid, (int)(o.Weenie.StackSize ?? 1)));
                }

                break;
        }
    }
}
