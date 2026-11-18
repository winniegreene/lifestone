using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;

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

    /// <summary>
    /// The owner, 2026-11-16: the review pack holds only the pieces the owner wants to see, the useful great-cantrip ones (<see cref="CantripRoom.IsUsefulGreatPiece(GameState, uint, IReadOnlyDictionary{uint, Lifestone.Data.Magic.SpellInfo}, CantripPlan, Action{string}, string)"/>);
    /// every other kept item stays in the main pack. M6 14 useful cantrips: a piece in the review pack whose great cantrips are not useful is sorted back to the main pack, where the sale offers it as sell loot; each piece's verdict is logged once.
    /// </summary>
    private bool ReviewPiece(uint guid) => CantripRoom.IsUsefulGreatPiece(S.State, guid, Context.Spells, log: Log);

    private PackLayout Layout(HuntingGround ground)
    {
        _packRules ??= new PackRules(ground);
        var layout = PackLayout.Of(S.State, _packRules, ReviewPiece, PackMemory.Shared.Rotation.Contains);
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
        var moves = layout.Sort(PackLayout.Items(S.State), _packRules!, ReviewPiece, PackMemory.Shared.Rotation.Contains, stays);
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

        foreach (var h in KeepGemBuffs()) { yield return h; } // M6 14 (the owner, 2026-11-15): keep Blackmoor's Favor active

        var skills = new QualitiesSkills(S.State.Self.Qualities);
        var judge = Judge(ground);
        foreach (var h in ShedStones(ground, "the visit's start")) { yield return h; }

        foreach (var h in SwapBestCaster(ground, judge, skills)) { yield return h; }

        foreach (var h in SortPacks(ground, "the visit's start")) { yield return h; }

        foreach (var h in WearShoes(skills)) { yield return h; }

        foreach (var h in SelfEquip(ground, judge, skills)) { yield return h; }

        foreach (var h in RefillWornMana(ground)) { yield return h; } // M6 item 14: the worn items' mana by the time to the next visit (ItemManaRefillStep.cs), in KeepItemMana's place

        foreach (var h in ShedStones(ground, "after the mana")) { yield return h; }

        Log("gear: done: " + GearFields());
    }

    private SelfEquipJudge? _judge;
    private GearScoreNotes? _gearNotes;

    /// <summary>
    /// The self-equip's judge for the ground's plan: its war element and the keep-aside block's useful cantrips, made once a run. M6 2 b (<c>m5-self-equip-element</c>, slice a's <see cref="SelfEquipJudge.ElementOf(string?, string?, out string)"/>):
    /// the element is the ladder rung's first (the rung of the character's level, when it names this ground), else the spell plan's family, with the source logged; the run's one <see cref="GearScoreNotes"/> writes each score TODO once to
    /// the log.
    /// </summary>
    private SelfEquipJudge Judge(HuntingGround ground)
    {
        if (_judge is not null)
        {
            return _judge;
        }

        _gearNotes ??= new GearScoreNotes(Log);
        var judge = Wardrobe.For(S.State).JudgeOn(ground, Context.Spells, out var read, _gearNotes); // M6 16 E5e2: the session's one wear judge (the element by the one rule, the ladder's caster terms, the lit pieces, the session's memory of a wield that did not land)
        Log(Text($"gear: element 0x{read.Element:X}: {read.ElementSource}{(read.Element == 0 ? ": no caster counts as better by its bonus" : string.Empty)}"));
        Log(Text($"gear: caster score: {read.Caster}; the bolt: {read.BoltSource}"));
        Wardrobe.For(S.State).NoteBlindElement(Context.Log, "station", ground, read); // M6 16 E5e3: no element while the fight has a line is an error, once a session
        return _judge = judge;
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
        var handle = new WieldResult();
        foreach (var h in Wardrobe.PutOn(S, new WieldRequest(pick.Guid) { Name = pick.Name, Location = SelfEquipJudge.FootWear }, handle)) { yield return h; } // M6 16 E5e1: the one wield routine
        Shoes = handle.Confirmed ? "worn" : "refused";
        if (Shoes == "worn")
        {
            SelfEquips++;
            PackTally.AddEquip();
        }

        Log(Text($"gear: shoes: '{pick.Name}' 0x{pick.Guid:X8}: {handle.Outcome(Describe)}"));
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
        ReleaseWearReview(); // M6 14 (m6-armour): the pieces kept at the last visit are reviewed again now, before the sale
        judge = WithWearTerms(ground, judge, "the gear phase");
        foreach (var candidate in HeldCandidates())
        {
            if (!S.State.Self.Inventory.Contains(candidate.Guid) || S.State.Self.Inventory.Find(candidate.Guid).Kind == InventoryPlaceKind.Equipped)
            {
                continue;
            }

            if (KeepUnread(candidate))
            {
                continue;
            }

            var worn = SelfEquipJudge.Worn(S.State);
            var choice = judge.Judge(candidate, worn, skills);
            if (choice.Kind == SelfEquipKind.None || (choice.Kind is SelfEquipKind.Swap or SelfEquipKind.Wear && (candidate.ItemType & SelfEquipJudge.CasterType) != 0))
            {
                if (judge.Wear is not null && choice.Kind == SelfEquipKind.None && GearScore.SlotOf(candidate.ItemType, candidate.Coverage, candidate.ValidLocations) is GearSlot.Armour or GearSlot.Jewellery)
                {
                    Log(choice.ToString() + " (to the pack rules)"); // M6 14 (m6-armour): every armour and jewellery verdict is logged with its numbers
                }

                continue; // M5 7 S2c: a caster is wielded only by the one pick below, never item by item
            }

            Log(choice.ToString());
            if (choice.Kind is SelfEquipKind.Wear or SelfEquipKind.Replace && InCombatMode())
            {
                foreach (var h in EnterPeaceForSwap(choice)) { yield return h; }
            }

            if (choice.Kind is SelfEquipKind.Wear or SelfEquipKind.Replace && !WearInPeace(choice))
            {
                KeepForReview(candidate, "the swap waits for peace mode");
                continue;
            }

            if (choice.Kind == SelfEquipKind.Wear)
            {
                // M6 2 f (2-R15): with the state check after the wield (the piece in the equipped slot), a named line when it is not
                foreach (var h in WearChecked(choice)) { yield return h; }
            }
            else if (choice.Kind == SelfEquipKind.Replace)
            {
                // M6 2 f (2-R2): a looted piece the judge would put on in place of a worn one is worn here, before the sale could offer it (check, act, check in ReplaceChecked)
                foreach (var h in ReplaceChecked(choice, worn)) { yield return h; }
            }
            else if (choice.Kind == SelfEquipKind.Review && judge.Wear is not null && S.State.Self.Inventory.Find(candidate.Guid).Kind != InventoryPlaceKind.MainPack)
            {
                KeepForReview(candidate, "the wear pick's review"); // M6 14 (m6-armour): already in a side pack: kept there, reviewed again at the next visit
            }
            else if (choice.Kind == SelfEquipKind.Review && S.State.Self.Inventory.Find(candidate.Guid).Kind == InventoryPlaceKind.MainPack)
            {
                PackMemory.Shared.Interesting.Add(candidate.Guid);
                _memory.KeptAside.Add(candidate.Guid); // never offered to a vendor
                if (judge.Wear is not null)
                {
                    KeepForReview(candidate, "the wear pick's review"); // M6 14 (m6-armour): reviewed again at the next visit, then worn or released to the sale
                }

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
    /// <summary>M6 14 (<c>m6-caster-spend</c>): the named failure of a caster swap whose wield did not land (the check after: the server's answer and the caster in the hands).</summary>
    public const string CasterWieldFailed = "caster-wield-failed";

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
        if (choice.Replaces != 0 && InCombatMode())
        {
            // M6 14 (m6-caster): check before: a swap only at a safe point, in peace mode as retail's player swaps (this reads the mode the server last said, never assumes it). M6 14 (live 2026-11-14: the Fire Sceptre's swap
            // waited a whole visit twice because she stood in the magic mode): the station sets peace itself at home and checks the server's answer; the fight re-enters its mode when it starts
            foreach (var h in EnterPeaceForSwap(choice)) { yield return h; }
            if (InCombatMode())
            {
                Log(Text($"gear: no swap: the combat mode is {S.State.Self.Qualities.CombatMode}, not peace, after asking peace: '{choice.Name}' 0x{choice.Guid:X8} waits for the next visit"));
                yield break;
            }
        }

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

        var on = new WieldResult();
        foreach (var h in Wardrobe.PutOn(S, new WieldRequest(choice.Guid) { Name = choice.Name, Location = SelfEquipJudge.Held }, on)) { yield return h; } // M6 16 E5e1: the one wield routine
        var held = S.State.Self.Inventory.EquippedAt(SelfEquipJudge.Held);
        Log(Text($"gear: wield '{choice.Name}' 0x{choice.Guid:X8}: {on.Outcome(Describe)}; held now 0x{held ?? 0:X8}"));
        if (on.Confirmed && held == choice.Guid)
        {
            SelfEquips++;
            PackTally.AddEquip();
            CasterSpellsDropped.Shared.Clear(choice.Guid, choice.Name);
            CasterAurasAfterSwap(choice);
            yield break;
        }

        _equipRefused.Add(Text($"wield '{choice.Name}' ({on.Outcome(Describe)})"));

        // M6 14 (m6-caster-spend): the check after failed: the server did not confirm the wield, or the hands do not hold the pick. A named failure, and the caster is held back for the run (the pick, the rearm and the spend pass it over),
        // so a refusal (ACE's 0x0468 under a wield requirement) is never sent again at every visit.
        var failed = Text($"{CasterWieldFailed}: the wield of '{choice.Name}' 0x{choice.Guid:X8} did not land: the server's answer {on.Outcome(Describe)}, the hands hold 0x{held ?? 0:X8}");
        Log("gear: FAILED " + failed + "; it is held back for the run");
        judge.HeldBack.Hold(choice.Guid, failed);
        if (choice.Replaces != 0 && !HeldCaster.Held(S.State))
        {
            var back = new WieldResult();
            foreach (var h in Wardrobe.PutOn(S, new WieldRequest(choice.Replaces) { Name = "the old caster", Location = SelfEquipJudge.Held }, back)) { yield return h; } // M6 16 E5e1: the one wield routine
            Log(Text($"gear: the old caster 0x{choice.Replaces:X8} wielded again: {back.Outcome(Describe)}"));
        }

        foreach (var h in RearmHands(judge, "the failed swap")) { yield return h; }
    }

    /// <summary>
    /// M6 14 (<c>m6-caster</c>): the check after a confirmed swap: her caster auras (<see cref="CasterAuraCheck"/>) read from her enchantments now. An aura up carries to the new caster (it is hers); one down or under the re-buff margin is
    /// owed and asks the pass before the next fight (<see cref="CasterAuraMemory"/>), which casts it on her at the level her live skill lands at the buff bar. Logged either way, with both casters named.
    /// </summary>
    private void CasterAurasAfterSwap(SelfEquipChoice choice)
    {
        var lines = CasterAuraCheck.AfterSwap(S.State, S.Clock.Now, BuffOptions.DefaultRebuffMarginSeconds);
        Log(Text($"gear: caster auras after the swap to '{choice.Name}' 0x{choice.Guid:X8} from 0x{choice.Replaces:X8}: {CasterAuraCheck.Describe(lines)}"));
        if (lines.Any(l => l.Verdict == CasterAuraVerdict.Owed))
        {
            CasterAuraMemory.Shared.Owe(Text($"'{choice.Name}' 0x{choice.Guid:X8} wielded at the visit: {string.Join(", ", lines.Where(l => l.Verdict == CasterAuraVerdict.Owed).Select(l => l.Line))} owed"));
        }
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
            // M6 2 b (m5-no-caster): the loud stop. Before the buys the floor may still buy one (GearFloor), so the stop is decided at the trip's end (NoCasterStop): with a caster held by then it clears, else the trip fails by name.
            _noCaster = Text($"the hands hold no caster after {after} and none can be wielded: {why} (the magic combat mode fails with \"no caster held\", Player_Combat.cs 872-880 @95ef426d)");
            Log($"gear: STOP {GearFloor.NoCaster}: {_noCaster}");
            yield break;
        }

        Log($"gear: the hands hold no caster after {after}: wield {why}");
        var on = new WieldResult();
        foreach (var h in Wardrobe.PutOn(S, new WieldRequest(pick.Guid) { Name = pick.Name, Location = SelfEquipJudge.Held }, on)) { yield return h; } // M6 16 E5e1: the one wield routine
        var heldNow = HeldCaster.Held(S.State);
        Log(Text($"gear: rearm '{pick.Name}' 0x{pick.Guid:X8}: {on.Outcome(Describe)}; a caster held now: {(heldNow ? "yes" : "no")}"));
        if (heldNow)
        {
            CasterSpellsDropped.Shared.Clear(pick.Guid, pick.Name);
            _noCaster = null;
        }
        else
        {
            _equipRefused.Add(Text($"rearm '{pick.Name}' ({on.Outcome(Describe)})"));
            _noCaster = Text($"the hands hold no caster after {after}: the wield of '{pick.Name}' 0x{pick.Guid:X8} was refused ({on.Outcome(Describe)})");
            Log($"gear: STOP {GearFloor.NoCaster}: {_noCaster}");
        }
    }

    private string? _noCaster;

    /// <summary>M6 2 b (<c>m5-no-caster</c>): at the trip's end, the hands still empty after <see cref="RearmHands"/> found nothing to wield: the trip fails by name (<see cref="GearFloor.NoCaster"/>).</summary>
    private void NoCasterStop(string where)
    {
        if (_noCaster is not { } why)
        {
            return;
        }

        if (HeldCaster.Held(S.State))
        {
            Log(Text($"gear: {GearFloor.NoCaster} cleared at the end of {where}: a caster is held now"));
            _noCaster = null;
            return;
        }

        Fail(Text($"{GearFloor.NoCaster}: at the end of {where}: {why}"));
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

            if (Layout(ground).FreeIn(PackJob.Main) <= 0)
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
            var step = ItemManaKeeper.Next(worn, stones, junk, S.State.Self.Guid, weighed, CasterInput(ground));
            foreach (var line in weighed)
            {
                Log(line); // M5 7 S2b: every junk item weighed, the mana's worth against the payout
            }

            var stepLine = step.ToString();
            Log(stepLine);
            if (step.Caster is { } decision && !stepLine.StartsWith("caster mana:", StringComparison.Ordinal))
            {
                Log(decision.ToString()); // M6 2 c: the caster's own line (a swap or a rewield is the step's line already)
            }

            // M6 2 b: the caster's decision, acted on with check, act, check (M5 D141)
            if (step.Caster is { Act: CasterManaAct.SwapToSpare } swap)
            {
                var swapped = false;
                foreach (var h in WieldCaster(ground, swap.Spare, "the caster's mana: the held caster is out, the charged spare", ok => swapped = ok)) { yield return h; }
                if (!swapped)
                {
                    yield break;
                }

                continue;
            }

            if (step.Caster is { Act: CasterManaAct.Rewield } again)
            {
                var rewielded = false;
                foreach (var h in RewieldCaster(ground, again.Caster, again.Name, ok => rewielded = ok)) { yield return h; }
                if (!rewielded)
                {
                    yield break;
                }

                continue;
            }

            if (step.Caster is { Act: CasterManaAct.WantStone } want)
            {
                _casterWant = want; // the floor's charge line (GearFloor), bought on this trip's walk
                Log("gear mana: the caster's want joins the floor: " + want.Reason);
            }

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

    private readonly List<string> _equipRefused = [];
    private CasterManaDecision? _casterWant;

    /// <summary>M6 2 b: the trip's gear floor (<see cref="GearFloor"/>), or null before it was planned (a kit trip plans none).</summary>
    public GearFloorPlan? FloorPlan { get; private set; }

    /// <summary>M6 2 b: the floor lines bought and proved (the item arrived, the purse paid).</summary>
    public int FloorBought { get; private set; }

    /// <summary>M6 2 b: the floor lines not bought, by name with the reason.</summary>
    public List<string> FloorRefused { get; } = [];

    /// <summary>M6 2 b: the equip pass's pieces put on in a free slot (a caster wielded counts).</summary>
    public int EquipWorn { get; private set; }

    /// <summary>M6 2 b: the equip pass's replacements (a worn piece off, a better one on in its place).</summary>
    public int EquipReplaced { get; private set; }

    /// <summary>M6 2 b: the equip pass's summary line, or null when it did not run.</summary>
    public string? EquipLine { get; private set; }

    /// <summary>
    /// M6 2 c, wired by b: the held caster's mana (its appraisal), whether its spells dropped (<see cref="CasterSpellsDropped"/>, from the "out of Mana" line), the spare casters in the packs with their mana, and every ware of the ground's
    /// profile vendors (the rule picks the charges).
    /// </summary>
    private CasterManaInput CasterInput(HuntingGround ground)
    {
        WornMana? held = null;
        var spares = new List<WornMana>();
        foreach (var e in S.State.Self.Inventory.Equipped)
        {
            if ((e.Location & SelfEquipJudge.Held) != 0 && S.State.Objects.TryGet(e.Guid, out var o) && (o.ItemType & SelfEquipJudge.CasterType) != 0)
            {
                held = S.TryGetAppraisal(e.Guid, out var a) && !a.IsNotFound ? new WornMana(e.Guid, o.Name, a.ItemMana ?? 0, a.ItemMaxMana ?? 0, a.Float(5) ?? 0.0) : new WornMana(e.Guid, o.Name, 0, 0, 0);
            }
        }

        foreach (var item in PackLayout.Items(S.State))
        {
            if ((item.ItemType & SelfEquipJudge.CasterType) != 0 && S.TryGetAppraisal(item.Guid, out var a) && !a.IsNotFound && a.ItemMaxMana is > 0)
            {
                spares.Add(new WornMana(item.Guid, item.Name, a.ItemMana ?? 0, a.ItemMaxMana.Value, a.Float(5) ?? 0.0));
            }
        }

        var offers = ground.Vendors.SelectMany(v => v.Sells.Select(w => new ManaOffer(v.Wcid, v.Name, w.Wcid, w.Price))).ToList();
        var dropped = held is { } h && CasterSpellsDropped.Shared.IsDropped(h.Guid, h.Name);
        return new CasterManaInput(held, dropped, spares, offers);
    }

    /// <summary>
    /// M6 2 b: wields the caster <paramref name="guid"/> in the hands (check: held in a pack, not wielded; act: the held caster to a pack with room, then the wield; check: the hands hold it). A refusal is named; the recovery wields the old
    /// caster again when the hands are left empty, then <see cref="RearmHands"/>.
    /// </summary>
    private IEnumerable<ActionHandle?> WieldCaster(HuntingGround ground, uint guid, string why, Action<bool> done)
    {
        var name = S.State.Objects.TryGet(guid, out var o) ? o.Name : "caster";
        var place = S.State.Self.Inventory.Find(guid).Kind;
        if (guid == 0 || !S.State.Self.Inventory.Contains(guid) || place == InventoryPlaceKind.Equipped)
        {
            Log(Text($"gear: wield '{name}' 0x{guid:X8} ({why}): check before failed: it is {(guid == 0 || !S.State.Self.Inventory.Contains(guid) ? "not held" : "wielded already")}: nothing sent"));
            done(false);
            yield break;
        }

        var old = S.State.Self.Inventory.EquippedAt(SelfEquipJudge.Held) ?? 0;
        if (old != 0)
        {
            var into = HeldCaster.SwapPack(Layout(ground), out var where);
            if (into == 0)
            {
                _equipRefused.Add(Text($"wield '{name}' (no pack for the held caster: {where})"));
                Log(Text($"gear: wield '{name}' 0x{guid:X8} ({why}): the held caster 0x{old:X8} has no pack to go to: {where}; nothing sent"));
                done(false);
                yield break;
            }

            PackMemory.Shared.Rotation.Add(old);
            var off = S.Actions.MoveToPack(old, into, 0, ActionSource.Automation);
            yield return off;
            Log(Text($"gear: the held caster 0x{old:X8} to {where}: {Describe(off)}"));
        }

        var on = new WieldResult();
        foreach (var h in Wardrobe.PutOn(S, new WieldRequest(guid) { Name = name, Location = SelfEquipJudge.Held }, on)) { yield return h; } // M6 16 E5e1: the one wield routine
        var heldNow = S.State.Self.Inventory.EquippedAt(SelfEquipJudge.Held);
        var ok = heldNow == guid;
        Log(Text($"gear: wield '{name}' 0x{guid:X8} ({why}): {on.Outcome(Describe)}; check after: held now 0x{heldNow ?? 0:X8}{(ok ? string.Empty : " (refused)")}"));
        if (ok)
        {
            SelfEquips++;
            PackTally.AddEquip();
            CasterSpellsDropped.Shared.Clear(guid, name);
            done(true);
            yield break;
        }

        _equipRefused.Add(Text($"wield '{name}' ({on.Outcome(Describe)})"));
        if (old != 0 && !HeldCaster.Held(S.State))
        {
            var back = new WieldResult();
            foreach (var h in Wardrobe.PutOn(S, new WieldRequest(old) { Name = "the old caster", Location = SelfEquipJudge.Held }, back)) { yield return h; } // M6 16 E5e1: the one wield routine
            Log(Text($"gear: recovery: the old caster 0x{old:X8} wielded again: {back.Outcome(Describe)}"));
        }

        foreach (var h in RearmHands(Judge(ground), "the refused wield of " + name)) { yield return h; }
        done(false);
    }

    /// <summary>
    /// M6 2 b (the mage half of <c>m4-gear-mana-reactivate</c>): the held caster's spells dropped when it ran dry and only a wield brings them back (Player_Inventory.cs:313-315 @95ef426d): check: it is the held one; act: to a pack, then
    /// wielded again; check: held again. A refusal is named; the recovery is <see cref="RearmHands"/>.
    /// </summary>
    private IEnumerable<ActionHandle?> RewieldCaster(HuntingGround ground, uint guid, string name, Action<bool> done)
    {
        if (S.State.Self.Inventory.EquippedAt(SelfEquipJudge.Held) != guid)
        {
            Log(Text($"gear: rewield '{name}' 0x{guid:X8}: check before failed: it is not the held caster; nothing sent"));
            done(false);
            yield break;
        }

        var into = HeldCaster.SwapPack(Layout(ground), out var where);
        if (into == 0)
        {
            _equipRefused.Add(Text($"rewield '{name}' (no pack: {where})"));
            Log(Text($"gear: rewield '{name}' 0x{guid:X8}: no pack to take it off into: {where}; nothing sent"));
            done(false);
            yield break;
        }

        var off = S.Actions.MoveToPack(guid, into, 0, ActionSource.Automation);
        yield return off;
        var offOk = S.State.Self.Inventory.Find(guid).Kind != InventoryPlaceKind.Equipped;
        Log(Text($"gear: rewield '{name}' 0x{guid:X8}: off to {where}: {Describe(off)}; check: {(offOk ? "off" : "still wielded")}"));
        if (!offOk)
        {
            _equipRefused.Add(Text($"rewield '{name}' (take-off {Describe(off)})"));
            done(false);
            yield break;
        }

        var on = new WieldResult();
        foreach (var h in Wardrobe.PutOn(S, new WieldRequest(guid) { Name = name, Location = SelfEquipJudge.Held }, on)) { yield return h; } // M6 16 E5e1: the one wield routine
        var ok = S.State.Self.Inventory.EquippedAt(SelfEquipJudge.Held) == guid;
        Log(Text($"gear: rewield '{name}' 0x{guid:X8}: {on.Outcome(Describe)}; check after: {(ok ? "held, its spells back with its mana" : "not held (refused)")}"));
        if (ok)
        {
            SelfEquips++;
            CasterSpellsDropped.Shared.Clear(guid, name);
            done(true);
            yield break;
        }

        _equipRefused.Add(Text($"rewield '{name}' ({on.Outcome(Describe)})"));
        foreach (var h in RearmHands(Judge(ground), "the refused rewield of " + name)) { yield return h; }
        done(false);
    }

    /// <summary>
    /// The pyreals the trip plans with: the coin (unread counted as 0) and the face of every trade note held, the side packs' too (M6 16 E5a1: the quartermaster's <see cref="PurseView.EverythingHeldUnreadAsZero"/>, the same sum as
    /// before). M6 16 E5a2: a buy changes any note she holds when the coin is short, a side pack's too (<see cref="VendorCounter"/>), so this is what a counter that takes a note pays with: <see cref="PurseView.CounterCanPayUnreadAsZero"/>.
    /// </summary>
    internal long FloorPurse() => PurseView.EverythingHeldUnreadAsZero(S.State);

    /// <summary>
    /// M6 2 b: the gear floor (<see cref="GearFloor"/>) of this trip: its lines join the buy list BESIDE the kit (never <c>supplies.kit</c>), first, so the caster is paid before the stock; the charge line is the caster's mana want
    /// (<see cref="KeepItemMana"/>). False with the trip failed by name when no caster is held and none can be bought (<see cref="GearFloor.NoCaster"/>); a missing spare that cannot be bought is the same stop's line, logged, and the trip
    /// goes on (the autopilot's audit is the stop for it, slice e).
    /// </summary>
    private bool PlanFloor(HuntingGround ground)
    {
        var floorLevel = GearFloor.FloorLevel(LadderLoader.WarMage);
        var unread = new List<string>();
        var plan = GearFloor.Plan(ground, GearFloor.StateOf(S.State, FloorPurse()), floorLevel, _casterWant, SelfEquipJudge.Worn(S.State));
        FloorPlan = plan;
        Log(plan.Why);
        if (GearFloor.CastersHeld(S.State, unread) > 0 && unread.Count > 0)
        {
            Log(Text($"gear floor: {unread.Count} caster(s) counted while unread (not appraised yet): {string.Join(", ", unread)} (2-R3: a caster counts only when wieldable)")); // named, never silent
        }

        foreach (var note in plan.Notes)
        {
            Log(note);
        }

        var buys = new List<ResupplyBuy>();
        foreach (var line in plan.Lines)
        {
            Log(line.ToString());
            var buy = ResupplyBuy.ForFloor(line);
            _floorBuys.Add(buy);
            buys.Add(buy);
        }

        if (buys.Count > 0)
        {
            _buyList = [.. buys, .. _buyList];
        }

        if (plan.Stop is { } stop)
        {
            if (plan.CastersAfter == 0)
            {
                Fail("gear floor: " + stop);
                return false;
            }

            Log("gear floor: STOP " + stop + " (one caster is held: the trip goes on; the audit's stop is the autopilot's)");
        }

        return true;
    }

    /// <summary>
    /// M6 2 b: one floor line's buy with check, act, check (M5 D141): check (the vendor is open: the visit met and opened him; the price beside the profile's and the purse: <see cref="BuyLine"/>), act (the buy), check (the item arrived:
    /// the held count rose by the line's count; the purse paid). A refused buy is a named line (<see cref="FloorRefused"/>), never a silent skip.
    /// </summary>
    private IEnumerable<ActionHandle?> FloorBuy(HuntingGround ground, GroundVendor vendor, uint guid, ResupplyBuy buy)
    {
        var scratch = new List<uint>();
        var name = buy.Line.Name;
        var heldBefore = GearInventory.CountOf(S.State, scratch, buy.Line.Wcid);
        var spentBefore = Spent;
        var refusalsBefore = _refusals.Count;
        Log(Text($"gear floor: buy {buy.Count} x {name} at {vendor.Name}: check before: open 0x{guid:X8} {(S.State.Vendor?.Vendor == guid ? "yes" : "no")}, the profile's price {(ground.PriceOf(buy.Line.Wcid) ?? vendor.Sells.FirstOrDefault(w => w.Wcid == buy.Line.Wcid)?.Price)?.ToString(CultureInfo.InvariantCulture) ?? "none"}, coin {Coins()}, held {heldBefore}"));
        foreach (var h in BuyLine(ground, vendor, guid, buy)) { yield return h; }

        var heldAfter = GearInventory.CountOf(S.State, scratch, buy.Line.Wcid);
        var paid = Spent - spentBefore;
        if (heldAfter >= heldBefore + buy.Count && paid > 0)
        {
            FloorBought++;
            Log(Text($"gear floor: bought {buy.Count} x {name} at {vendor.Name}: check after: held {heldBefore} -> {heldAfter}, paid {paid}"));
            yield break;
        }

        var reason = _refusals.Count > refusalsBefore ? "refused: " + _refusals[^1] : paid == 0 ? "nothing paid (the purse, the stock or the pack: the lines above)" : "the item did not arrive";
        FloorRefused.Add(Text($"{name} ({reason})"));
        Log(Text($"gear floor: refused: {name} at {vendor.Name}: {reason}; check after: held {heldBefore} -> {heldAfter}, paid {paid}"));
    }

    /// <summary>
    /// M6 2 b: the equip pass after the buys. The new and unread pieces appraised; the best caster wielded (<see cref="SwapBestCaster"/>: the other kept as the spare); then the self-equip's judge over every held piece, acted on: a
    /// <see cref="SelfEquipKind.Wear"/> in its free slot, a <see cref="SelfEquipKind.Replace"/> (the worn piece off to the main pack, the better one on at its location), each with check, act, check per wield and ACE's coverage rule (the
    /// judge's <see cref="SelfEquipJudge.FreeLocation"/>, Creature_Equipment.cs:50-58, 93-105 @95ef426d: the Pathwarden robe's 0x17F00 holds the head and the feet, so the sollerets stay in the pack). One summary line:
    /// <c>equip pass: worn=&lt;n&gt; replaced=&lt;n&gt; casters=&lt;held&gt; refused=&lt;n&gt; (&lt;names&gt;)</c>. A Replace is not an upgrade for the loot's keep-aside (the lead's decision: the den's loot lines unchanged).
    /// </summary>
    internal IEnumerable<ActionHandle?> EquipPass(HuntingGround ground)
    {
        Stage = "equip";
        var refusedFrom = 0; // M6 2 f (2-R2): the whole run's refusals, the gear phase's Replace and Wear among them (the counters worn= and replaced= are the run's too)
        foreach (var h in AppraiseGear()) { yield return h; }

        foreach (var h in LeatherSwapPass()) { yield return h; } // M6 item 14: once every piece of the leather set is held, the robe off and the set on, as one act

        var skills = new QualitiesSkills(S.State.Self.Qualities);
        var judge = Judge(ground);
        var equipsBefore = SelfEquips;
        foreach (var h in SwapBestCaster(ground, judge, skills)) { yield return h; }

        EquipWorn += SelfEquips - equipsBefore;
        judge = WithWearTerms(ground, judge, "the equip pass"); // M6 14 (m6-armour): her load read again after the sale
        foreach (var candidate in HeldCandidates())
        {
            if (!S.State.Self.Inventory.Contains(candidate.Guid) || S.State.Self.Inventory.Find(candidate.Guid).Kind == InventoryPlaceKind.Equipped || (candidate.ItemType & SelfEquipJudge.CasterType) != 0)
            {
                continue; // a caster is the one pick's (above)
            }

            if (KeepUnread(candidate))
            {
                continue;
            }

            var worn = SelfEquipJudge.Worn(S.State);
            var choice = judge.Judge(candidate, worn, skills);
            if (choice.Kind is SelfEquipKind.Wear or SelfEquipKind.Replace && InCombatMode())
            {
                foreach (var h in EnterPeaceForSwap(choice)) { yield return h; }
            }

            if (choice.Kind is SelfEquipKind.Wear or SelfEquipKind.Replace && !WearInPeace(choice))
            {
                KeepForReview(candidate, "the swap waits for peace mode");
            }
            else if (choice.Kind == SelfEquipKind.Wear)
            {
                Log("equip pass: " + choice);
                foreach (var h in WearChecked(choice)) { yield return h; }
            }
            else if (choice.Kind == SelfEquipKind.Replace)
            {
                Log("equip pass: " + choice);
                foreach (var h in ReplaceChecked(choice, worn)) { yield return h; }
            }
            else if (choice.Kind == SelfEquipKind.Review && judge.Wear is not null)
            {
                Log("equip pass: " + choice);
                KeepForReview(candidate, "the wear pick's review");
            }
        }

        // The floor's robe: a plain robe scores no armour gain (the Faran Robe with Hood is armour level 0), so the judge never wears it; the floor bought it because the chest is bare, so it goes on in its free slot.
        if (FloorPlan?.Lines.FirstOrDefault(l => l.Kind == GearFloorKind.Robe) is { } robeLine && GearFloor.ChestPiece(SelfEquipJudge.Worn(S.State)) is null
            && HeldCandidates().FirstOrDefault(c => S.State.Objects.TryGet(c.Guid, out var o) && o.Weenie.WeenieClassId == robeLine.Wcid && S.State.Self.Inventory.Find(c.Guid).Kind != InventoryPlaceKind.Equipped) is { } robe)
        {
            if (SelfEquipJudge.FreeLocation(robe, SelfEquipJudge.Worn(S.State), out var blocker) is { } location)
            {
                foreach (var h in WearChecked(new SelfEquipChoice(SelfEquipKind.Wear, robe.Guid, robe.Name, location, 0, "the floor's robe: no chest-covering piece is worn"))) { yield return h; }
            }
            else
            {
                _equipRefused.Add(Text($"wear '{robe.Name}' ({blocker})"));
                Log(Text($"equip pass: the floor's robe '{robe.Name}' 0x{robe.Guid:X8} cannot go on: {blocker}"));
            }
        }

        // M6 7 AH: the floor's gauntlets, as the robe: the judge may score a pair no better than bare hands, but the floor bought them because no worn piece covers the hands, so they go on in their free slot (check before, the wear's own check after).
        if (FloorPlan?.Lines.FirstOrDefault(l => l.Kind == GearFloorKind.Gauntlets) is { } glovesLine && !GearFloor.HandsCoveredBy(SelfEquipJudge.Worn(S.State))
            && HeldCandidates().FirstOrDefault(c => S.State.Objects.TryGet(c.Guid, out var o) && o.Weenie.WeenieClassId == glovesLine.Wcid && S.State.Self.Inventory.Find(c.Guid).Kind != InventoryPlaceKind.Equipped) is { } gloves)
        {
            if (SelfEquipJudge.FreeLocation(gloves, SelfEquipJudge.Worn(S.State), out var blocker) is { } location)
            {
                foreach (var h in WearChecked(new SelfEquipChoice(SelfEquipKind.Wear, gloves.Guid, gloves.Name, location, 0, "the floor's gauntlets: no worn piece covers the hands"))) { yield return h; }
            }
            else
            {
                _equipRefused.Add(Text($"wear '{gloves.Name}' ({blocker})"));
                Log(Text($"equip pass: the floor's gauntlets '{gloves.Name}' 0x{gloves.Guid:X8} cannot go on: {blocker}"));
            }
        }

        if (FloorPlan?.Lines.Any(l => l.Kind == GearFloorKind.Gauntlets) == true)
        {
            Log(GearFloor.HandsCoveredBy(SelfEquipJudge.Worn(S.State)) ? "equip pass: check after: the hands are covered" : "equip pass: check after: the hands are still bare although the floor bought gauntlets (named: gauntlets-not-worn)");
        }

        if (!HeldCaster.Held(S.State) && _noCaster is null)
        {
            foreach (var h in RearmHands(judge, "the equip pass")) { yield return h; }
        }

        var refused = _equipRefused.Skip(refusedFrom).ToList();
        EquipLine = Text($"equip pass: worn={EquipWorn} replaced={EquipReplaced} casters={GearFloor.CastersHeld(S.State)} refused={refused.Count} ({(refused.Count == 0 ? "none" : string.Join(", ", refused))})");
        Log(EquipLine);
    }

    /// <summary>A Wear with check (held, not worn), act (the wield at its location), check (worn now); a refusal is named and the piece stays in its pack.</summary>
    private IEnumerable<ActionHandle?> WearChecked(SelfEquipChoice choice)
    {
        if (!S.State.Self.Inventory.Contains(choice.Guid) || S.State.Self.Inventory.Find(choice.Guid).Kind == InventoryPlaceKind.Equipped)
        {
            Log(Text($"equip pass: wear '{choice.Name}' 0x{choice.Guid:X8}: check before failed: not held in a pack; nothing sent"));
            yield break;
        }

        var numbersBefore = SelfNumbers();
        var on = new WieldResult();
        foreach (var h in Wardrobe.PutOn(S, new WieldRequest(choice.Guid) { Name = choice.Name, Location = choice.Location }, on)) { yield return h; } // M6 16 E5e1: the one wield routine
        var ok = S.State.Self.Inventory.Find(choice.Guid).Kind == InventoryPlaceKind.Equipped;
        Log(Text($"equip pass: wear '{choice.Name}' 0x{choice.Guid:X8} at 0x{choice.Location:X}: {on.Outcome(Describe)}; check after: {(ok ? "worn" : "not worn (refused)")}; her numbers before: {numbersBefore}; after: {SelfNumbers()}"));
        if (ok)
        {
            EquipWorn++;
            SelfEquips++;
            PackTally.AddEquip();
            yield break;
        }

        _equipRefused.Add(Text($"wear '{choice.Name}' ({on.Outcome(Describe)})"));
    }

    /// <summary>
    /// A Replace with check (the worn piece still worn, a free main-pack slot to take it off into), act (the worn piece to the main pack), check (off), act (the better piece on at its location), check (worn); a refused wield is named and the
    /// recovery puts the old piece back on at its location (checked and logged).
    /// </summary>
    private IEnumerable<ActionHandle?> ReplaceChecked(SelfEquipChoice choice, IReadOnlyList<WornPiece> worn)
    {
        // M6 14 (m6-armour): the wear pick may displace several pieces (a robe over a coat and leggings); each comes off checked, and a refused wield puts every one back
        var guids = choice.Displaced.Count > 0 ? choice.Displaced : [choice.Replaces];
        var olds = new List<WornPiece>();
        foreach (var guid in guids)
        {
            var old = worn.FirstOrDefault(w => w.Guid == guid);
            if (old is null || S.State.Self.Inventory.Find(guid).Kind != InventoryPlaceKind.Equipped)
            {
                Log(Text($"equip pass: replace with '{choice.Name}': check before failed: the worn piece 0x{guid:X8} is not worn; nothing sent"));
                yield break;
            }

            olds.Add(old);
        }

        var names = string.Join(", ", olds.Select(o => $"'{o.Name}'"));
        if (PackView.MainPackFree(S.State) < olds.Count)
        {
            _equipRefused.Add(Text($"replace {names} (no free main-pack slot)"));
            Log(Text($"equip pass: replace {names} with '{choice.Name}': the main pack has {PackView.MainPackFree(S.State)} free slot(s) for {olds.Count} piece(s) to take off; nothing sent"));
            yield break;
        }

        var numbersBefore = SelfNumbers();
        // M6 16 E5e1: the one wield routine: the worn pieces come off first, each checked (a move to the main pack), then the piece goes on; one that stays on ends it, and what came off goes back
        var on = new WieldResult();
        var host = new WieldHost(VendorCounterHost.ClockUntil(S))
        {
            TookOff = (old, off, offOk) =>
            {
                Log(Text($"equip pass: replace: '{old.Name}' 0x{old.Guid:X8} off to the main pack (the pack rules decide it at the sale): {Describe(off)}; check: {(offOk ? "off" : "still worn")}"));
                if (!offOk)
                {
                    _equipRefused.Add(Text($"take off '{old.Name}' ({Describe(off)})"));
                }
            },
        };
        foreach (var h in Wardrobe.PutOn(S, new WieldRequest(choice.Guid) { Name = choice.Name, Location = choice.Location, TakeOff = [.. olds.Select(o => new WieldBlocker(o.Guid, o.Name) { Into = S.State.Self.Guid })] }, on, host)) { yield return h; }

        var offDone = olds.Take(on.TakenOff.Count).ToList();
        if (on.Refusal == Wardrobe.Blocked)
        {
            foreach (var h in PutBack(offDone)) { yield return h; }
            yield break;
        }
        var ok = S.State.Self.Inventory.Find(choice.Guid).Kind == InventoryPlaceKind.Equipped;
        Log(Text($"equip pass: replace: '{choice.Name}' 0x{choice.Guid:X8} on at 0x{choice.Location:X}: {on.Outcome(Describe)}; check after: {(ok ? "worn" : "not worn (refused)")}; her numbers before: {numbersBefore}; after: {SelfNumbers()}"));
        if (ok)
        {
            EquipReplaced++;
            SelfEquips++;
            PackTally.AddEquip();
            yield break;
        }

        _equipRefused.Add(Text($"replace with '{choice.Name}' ({on.Outcome(Describe)})"));
        foreach (var h in PutBack(offDone)) { yield return h; }
    }

    /// <summary>The recovery of a failed replace: every piece taken off goes back on at its location, each checked and logged.</summary>
    private IEnumerable<ActionHandle?> PutBack(IReadOnlyList<WornPiece> pieces)
    {
        foreach (var old in pieces)
        {
            var back = new WieldResult();
            foreach (var h in Wardrobe.PutOn(S, new WieldRequest(old.Guid) { Name = old.Name, Location = old.Location }, back)) { yield return h; } // M6 16 E5e1: the one wield routine
            var backOk = S.State.Self.Inventory.Find(old.Guid).Kind == InventoryPlaceKind.Equipped;
            Log(Text($"equip pass: recovery: '{old.Name}' 0x{old.Guid:X8} back on at 0x{old.Location:X}: {back.Outcome(Describe)}; check: {(backOk ? "worn again" : "NOT worn: the parts it covered are bare")}"));
        }
    }

    /// <summary>
    /// M6 14 (<c>m6-armour</c>): <paramref name="judge"/> with the wear pick's terms of her now (<see cref="WearTerms"/>): the ladder's weights, the caster terms, the ground's Magic Defense, the rung's hitter and gate lines, her capacity and
    /// kept load, the ground's jumps; logged with <paramref name="where"/>.
    /// </summary>
    private SelfEquipJudge WithWearTerms(HuntingGround ground, SelfEquipJudge judge, string where)
    {
        var withWear = Wardrobe.For(S.State).WithWearTerms(ground, judge, () => ReadBook(ground), keptBurden: true, out var terms); // M6 16 Q10c: the terms are the wardrobe's; this trip's read-only sale context is the one they ask
        Log(Text($"gear: wear pick ({where}): {terms}"));
        Context.Facts.WearJudge = withWear; // M6 14 (m6-lore-spend): the experience spend's Arcane Lore rule judges her held pieces with this pick between visits
        return withWear;
    }

    /// <summary>M6 16 Q10c: the wardrobe's (<see cref="Wardrobe.StaysInPackWhenOff"/>) over this trip's read-only sale context; the tests ask it here.</summary>
    internal Func<WornPiece, bool> StaysInPackWhenOff(HuntingGround ground) => Wardrobe.For(S.State).StaysInPackWhenOff(() => ReadBook(ground));

    /// <summary>M6 16 Q10c: the wardrobe's (<see cref="Wardrobe.KeptBurden"/>) over this trip's read-only sale context; the tests ask it here.</summary>
    internal int? KeptBurden(HuntingGround ground, int burden) => Wardrobe.For(S.State).KeptBurden(ground, burden, () => ReadBook(ground));

    /// <summary>
    /// M6 14 (the owner, 2026-11-15: keep Blackmoor's Favor active): check, act, check for each eternal gem she holds (<see cref="SelfBuffGems"/>): its spell's time left on her (her registry); under
    /// <see cref="SelfBuffGems.RefreshUnderSeconds"/> or absent, she uses the gem (a player's double click in the pack) and the line after reads the spell again; each named.
    /// </summary>
    private IEnumerable<ActionHandle?> KeepGemBuffs()
    {
        foreach (var gem in SelfBuffGems.All)
        {
            var guid = HeldOf(gem.Wcid).FirstOrDefault();
            if (guid == 0)
            {
                continue;
            }

            var left = GemLeft(gem.SpellId);
            if (left is { } l && l >= SelfBuffGems.RefreshUnderSeconds)
            {
                Log(string.Create(CultureInfo.InvariantCulture, $"gem: {gem.Name} 0x{guid:X8}: its spell has {l / 3600:0.#} h left (refreshed under {SelfBuffGems.RefreshUnderSeconds / 3600:0} h): not used"));
                continue;
            }

            var before = left is { } t ? string.Create(CultureInfo.InvariantCulture, $"has {t / 60:0} min left") : "is not on her";
            Log(string.Create(CultureInfo.InvariantCulture, $"gem: {gem.Name} 0x{guid:X8}: check before: its spell {before}: using the gem"));
            var use = S.Actions.Use(guid, ActionSource.Automation);
            foreach (var h in Until(() => use.IsCompleted && GemLeft(gem.SpellId) is > SelfBuffGems.RefreshUnderSeconds, 5)) { yield return h; }

            var after = GemLeft(gem.SpellId);
            var afterText = after is { } a ? string.Create(CultureInfo.InvariantCulture, $"has {a / 60:0} min left") : "is not on her";
            Log(after is > SelfBuffGems.RefreshUnderSeconds
                ? string.Create(CultureInfo.InvariantCulture, $"gem: {gem.Name}: check after: its spell is on her, {after.Value / 3600:0.#} h left ({Describe(use)})")
                : string.Create(CultureInfo.InvariantCulture, $"gem: {gem.Name}: FAILED: check after: its spell {afterText} after the use ({Describe(use)}): the next visit tries again"));
        }
    }

    /// <summary>The seconds left of <paramref name="spellId"/> on her (the longest of its entries), or null when it is not on her.</summary>
    private double? GemLeft(uint spellId)
    {
        double? left = null;
        var active = S.State.Self.Qualities.ActiveEnchantments;
        if (active.IsDefaultOrEmpty)
        {
            return null;
        }

        var now = S.Clock.Now;
        foreach (var e in active)
        {
            var r = e.RemainingSeconds(now);
            if (e.Enchantment.SpellId == spellId && r > 0 && (left is null || r > left))
            {
                left = r;
            }
        }

        return left;
    }

    /// <summary>True while the server last said a combat mode (melee, missile or magic), not peace.</summary>
    private bool InCombatMode() => S.State.Self.Qualities.CombatMode is CombatModes.Melee or CombatModes.Missile or CombatModes.Magic;

    /// <summary>
    /// M6 14 (live 2026-11-14: the Fire Sceptre's swap waited two visits in a row in the magic mode): the act between a swap's checks: the station, at home and in a safe moment, asks peace for the swap itself and logs the
    /// server's answer; the caller reads the mode again (check after) and waits for the next visit only when peace did not land.
    /// </summary>
    private IEnumerable<ActionHandle?> EnterPeaceForSwap(SelfEquipChoice choice)
    {
        Log(Text($"gear: the combat mode is {S.State.Self.Qualities.CombatMode}: setting peace for the swap of '{choice.Name}' 0x{choice.Guid:X8}"));
        var peace = S.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Automation);
        yield return peace;
        Log(Text($"gear: peace for the swap: {Describe(peace)}; the mode is {S.State.Self.Qualities.CombatMode} now"));
    }

    /// <summary>M6 14 (<c>m6-armour</c>): check before a wearable swap: peace mode (the mode the server last said), as retail's player swaps at a safe point; false, logged, in a combat mode.</summary>
    private bool WearInPeace(SelfEquipChoice choice)
    {
        if (S.State.Self.Qualities.CombatMode is CombatModes.Melee or CombatModes.Missile or CombatModes.Magic)
        {
            Log(Text($"gear: no swap: the combat mode is {S.State.Self.Qualities.CombatMode}, not peace: '{choice.Name}' 0x{choice.Guid:X8} waits for the next visit"));
            return false;
        }

        return true;
    }

    /// <summary>M6 14 (<c>m6-armour</c>): her numbers a worn piece's cantrips move, read from the state: War, Life, Creature and Item, Mana Conversion, Arcane Lore, the maximum mana and health, the burden.</summary>
    private string SelfNumbers()
    {
        var skills = new QualitiesSkills(S.State.Self.Qualities);
        string N(int? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "unread";
        return Text($"War Magic {N(skills.Current(MageTemplate.WarMagic))}, Life {N(skills.Current(MageTemplate.LifeMagic))}, Creature {N(skills.Current(MageTemplate.CreatureEnchantment))}, Item {N(skills.Current(MageTemplate.ItemEnchantment))}, Mana Conversion {N(skills.Current(MageTemplate.ManaConversion))}, Arcane Lore {N(skills.Current(MageTemplate.ArcaneLore))}, max mana {N(skills.VitalMax(5))}, max health {N(skills.VitalMax(1))}, burden {N(S.State.Self.Inventory.Burden)}");
    }

    /// <summary>
    /// M6 14 (<c>m6-armour</c>, not sold blind): a held armour, clothing or jewellery piece with no appraisal (past the visit's cap of <see cref="GearAppraisalCap"/>) is kept for the next visit's review, never offered unread; true when kept.
    /// </summary>
    private bool KeepUnread(GearCandidate candidate)
    {
        if (candidate.Appraisal is { IsNotFound: false } || GearScore.SlotOf(candidate.ItemType, candidate.Coverage, candidate.ValidLocations) is not (GearSlot.Armour or GearSlot.Jewellery))
        {
            return false;
        }

        KeepForReview(candidate, "not appraised this visit: not sold blind");
        return true;
    }

    /// <summary>M6 14 (<c>m6-armour</c>): keeps <paramref name="candidate"/> from the sale until the next visit's gear phase reviews it (<see cref="PackMemory.WearReview"/>), logged with <paramref name="why"/>.</summary>
    private void KeepForReview(GearCandidate candidate, string why)
    {
        if (PackMemory.Shared.WearReview.Add(candidate.Guid))
        {
            _memory.KeptAside.Add(candidate.Guid);
            Log(Text($"gear: wear pick: '{candidate.Name}' 0x{candidate.Guid:X8} kept for the review at the next visit ({why}): pack rule gear-review"));
        }
    }

    /// <summary>M6 14 (<c>m6-armour</c>): at the gear phase's start the pieces kept at the last visit are released to this visit's review (worn, kept again, or sold by the pack rules).</summary>
    private void ReleaseWearReview()
    {
        if (PackMemory.Shared.WearReview.Count == 0)
        {
            return;
        }

        foreach (var guid in PackMemory.Shared.WearReview)
        {
            _memory.KeptAside.Remove(guid);
            PackMemory.Shared.Interesting.Remove(guid);
        }

        Log(Text($"gear: wear pick: {PackMemory.Shared.WearReview.Count} piece(s) kept at the last visit are reviewed again now"));
        PackMemory.Shared.WearReview.Clear();
    }
}
