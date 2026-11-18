using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 14 (the owner, 2026-11-16: with the money she has, she buys every spell, eventually when affordable; "we don't have the spell" is no answer): the trip's every-spell step (<see cref="EverySpellPlan"/>), after the spell plan's lines and
/// before the held scrolls, on the trips she already makes. While <see cref="JourneyFacts.EverySpell"/> is on (the level up job's switch, <c>--autopilot-every-spell</c>):
/// <list type="number">
/// <item>Check before: the book and the qualities read; the purse (her coin and the faces of her notes, <see cref="FloorPurse"/>), the reserve (<see cref="EverySpellPlan.ReserveOf"/>), the pack's room and the burden line, each logged
/// with its numbers; then every pick and every refusal on its own <c>every_spell:</c> line.</item>
/// <item>Act: a home scroll joins the walk at its scrivener (<see cref="PlanLearn"/>: its own before-check, the buy's coin and slot checks, the components kit for a damage or buff line); a V or VI sold only off home goes by the Town Network
/// (<see cref="PlanEverySpellTown"/>, one town a trip, as the plan's); a held scroll is read, nothing bought. The batches are logged by vendor.</item>
/// <item>Check after: each read's proof is the spellbook's update (<see cref="ReadOne"/>); a read ACE refuses is remembered (<see cref="EverySpellMemory.Unreadable"/>), logged loud, its scroll kept from the sale
/// (<see cref="EverySpellKeepReason"/>) and never bought again.</item>
/// </list>
/// </summary>
public sealed partial class ResupplyRun
{
    /// <summary>The spells the every-spell step planned on this trip (bought or held), read and judged by the step's after-check.</summary>
    private readonly HashSet<uint> _everySpellIds = [];

    /// <summary>The spell plan's needs of this trip (<see cref="PlanLines"/>), which the every-spell step defers to.</summary>
    private List<LearnNeed> _planNeeds = [];

    /// <summary>The keep check's inputs, read once a trip (the sale asks each scroll).</summary>
    /// <summary>
    /// M6 14 own lore (the owner, 2026-11-17): true when a piece she wears (the caster aside) or a held piece the gear rules keep for her Arcane Lore asks more lore than her unbuffed Arcane Lore (<see cref="EverySpellPlan.GearNeedsLore"/>),
    /// logged once a call.
    /// </summary>
    private bool GearNeedsLore(PlayerQualities q)
    {
        _ = q;
        var over = EverySpellKeep.GearNeedsLore(S.State, Context.Facts.WearJudge, out var why); // M6 16 E5c2: the one reading, the session's sale context asks it too
        Log("every spell: gear lore: " + why);
        return over;
    }

    private EverySpellInput? _everySpellKeep;

    /// <summary>The scrolls the every-spell step planned on this trip.</summary>
    public int EverySpellPlanned => _everySpellIds.Count;

    /// <summary>True on a trip the every-spell rule runs on: the run's switch, a supply trip (not a kit, replace or town-shop trip).</summary>
    private bool EverySpellOn => Context.Facts.EverySpell && _options.Kit is null && !_options.Replace && _options.TownShop is null;

    private EverySpellMemory EverySpellMemo => EverySpellMemory.For(_memory);

    /// <summary>The every-spell step (see the class). Allocates: once a trip.</summary>
    private void PlanEverySpell(HuntingGround ground, List<ResupplyBuy> buys)
    {
        if (!EverySpellOn)
        {
            return;
        }

        var q = S.State.Self.Qualities;
        if (!S.State.Self.Spellbook.IsKnown || !q.IsKnown)
        {
            Log("every_spell: check before: the spellbook or the qualities are not read yet (no PlayerDescription): nothing is planned on an assumption");
            return;
        }

        var input = EverySpellInputFor(ground, buys, withPurse: true);
        var result = EverySpellPlan.Plan(input);
        var memo = EverySpellMemo;
        Log(Text($"every_spell: check before: {result.Summary}"));
        Log(Text($"every_spell: reserve {input.Reserve.Total}: {input.Reserve.Why}"));
        Log(Text($"every_spell: room: {(input.Room == int.MaxValue ? "open" : input.Room.ToString(CultureInfo.InvariantCulture))} scroll(s) a place (the main pack's free slots above the loot's floor and a note's split), burden room {(input.BurdenRoom == long.MaxValue ? "open" : input.BurdenRoom.ToString(CultureInfo.InvariantCulture))}"));
        foreach (var pick in result.Picks)
        {
            if (pick.Verdict is EverySpellVerdict.Known or EverySpellVerdict.PlanLine)
            {
                continue; // counted in the check before's line: the book holds it, or another rule of the trip decides it
            }

            var name = pick.Spell is { } s ? Text($"{s.Name} ({s.SpellId})") : pick.Line;
            Log(Text($"every_spell: {TierWord(pick.Tier)} {name}: {pick.Token}: {pick.Why}"));
            if (pick.Spell is not { } spell)
            {
                continue;
            }

            if (pick.Verdict == EverySpellVerdict.ReadHeld || (pick.Verdict == EverySpellVerdict.Buy && pick.Home))
            {
                _everySpellIds.Add(spell.SpellId);
                if (pick.Verdict == EverySpellVerdict.Buy)
                {
                    memo.Bought.Add(spell.ScrollWcid);
                }

                PlanLearn(ground, new LearnLine(spell.SpellId, spell.Name, spell.ScrollWcid, pick.VendorWcid), buys, kit: pick.Tier != EverySpellTier.Rest);
            }
            else if (pick.Verdict == EverySpellVerdict.Buy)
            {
                PlanEverySpellTown(ground, pick, spell);
            }
        }

        foreach (var batch in result.Batches)
        {
            Log(Text($"every_spell: batch {(batch.Home ? "home" : "town")} {batch.Key}: {batch.Picks.Count} scroll(s), {batch.Cost} pyreals: {string.Join(", ", batch.Picks.Select(p => p.Spell!.Name))}"));
        }

        var next = result.Picks.FirstOrDefault(p => p.Verdict is EverySpellVerdict.Purse or EverySpellVerdict.Room);
        memo.Last = new EverySpellMarker(result.Wanted, result.Buys.Count, input.Reserve.Total, next?.Spell?.SpellId ?? 0, next?.Price ?? 0);
    }

    /// <summary>One every-spell scroll sold only off home: its town by the Town Network's rule (<see cref="TownNetworkTrip.Choose"/> over its sellers' places), read on the trip there (<see cref="TownLearnTrip"/>).</summary>
    private void PlanEverySpellTown(HuntingGround ground, EverySpellPick pick, ScrivenerStockSpell spell)
    {
        var catalog = ScrivenerCatalog.Default;
        var learn = new LearnLine(spell.SpellId, spell.Name, spell.ScrollWcid, pick.Sellers.FirstOrDefault());
        var reason = LearnBefore(learn, out var why);
        if (reason != LearnReason.None)
        {
            Log(Text($"every_spell: {spell.Name} ({spell.SpellId}): off-home: {Token(reason)}: {why}"));
            return;
        }

        var candidates = pick.Sellers
            .Select(catalog.ScrivenerOf)
            .OfType<Scrivener>()
            .SelectMany(s => s.Places.Select(p => new TownTripCandidate(p.Town, s.Name, s.Wcid, p)))
            .OrderBy(k => k.Town, StringComparer.Ordinal)
            .ToList();
        var choice = TownNetworkTrip.Choose(candidates, TownInput(ground), Context.Routes, LeverDoors.Default);
        if (choice.Chosen is not { } plan)
        {
            if (OffHomeLogged.GetOrCreateValue(_memory).Add(spell.SpellId))
            {
                Log(Text($"every_spell: {choice.Stop}: {spell.Name} ({spell.SpellId}) is sold only off home and no trip by the Town Network is admitted: {choice.Why}; it waits (logged once a session)"));
            }

            return;
        }

        var price = catalog.ScrivenerOf(plan.Candidate.Wcid)!.PriceOf(spell.ScrollValue);
        learn = learn with { VendorWcid = plan.Candidate.Wcid };
        _toRead.Add(learn);
        _townLearns.Add((learn, plan, price));
        _everySpellIds.Add(spell.SpellId);
        EverySpellMemo.Bought.Add(spell.ScrollWcid);
        Log(Text($"every_spell: {spell.Name} ({spell.SpellId}): off-home: by the Town Network to {plan.Candidate.Town} (travel={TownNetworkTrip.TravelWord}), {price} pyreals at {plan.Candidate.Name} (0x{plan.Candidate.Vendor.Guid:X8}); {choice.Why}"));
    }

    /// <summary>
    /// The rule's inputs, read live: the book, the SpellTable's facts, the standing skills (<see cref="ScrivenerLearnPlan.Standing"/>, the buff bot's lines while it applies), the ladder's two bars, the spell plan's lines of this trip, the
    /// bot's cover, the ground's incoming elements and home vendors, the session's refusals and the held scrolls; with <paramref name="withPurse"/>, the purse, the reserve and the room too.
    /// </summary>
    private EverySpellInput EverySpellInputFor(HuntingGround ground, IReadOnlyList<ResupplyBuy> buys, bool withPurse)
    {
        // M6 16 E5c2: the inputs no purse or room decides are read in one place (EverySpellKeep.InputOf), which the session's sale context asks too; the trip's own parts are the plan's needs it read and the scrolls it reads already
        var input = EverySpellKeep.InputOf(S.State, ground, Context.Spells, _memory, _planNeeds, _toRead.Select(l => l.SpellId).Where(id => !_everySpellIds.Contains(id)).ToHashSet(), GearNeedsLore(S.State.Self.Qualities));

        if (!withPurse)
        {
            return input;
        }

        var free = Quartermaster.PackView.MainPackFree(S.State);
        var room = Math.Max(0, free - (ground.Loot?.MinFreeSlots ?? 0) - 1 - buys.Count); // a note split takes a slot (TownTripPurse); each line already on the list may take one
        var load = Fighter.LootContext.Of(S.State);
        var burdenLine = ground.Loot is { LoadLinePercent: > 0 } loot ? loot.LoadLinePercent : ground.Supplies.BurdenTripPercent;
        var burdenRoom = load.Capacity > 0 ? Math.Max(0, ((long)load.Capacity * burdenLine / 100) - load.Burden) : long.MaxValue;
        return input with { Purse = FloorPurse(), Reserve = EverySpellReserveFor(ground, buys), Room = room, BurdenRoom = burdenRoom };
    }

    /// <summary>The reserve (<see cref="EverySpellPlan.ReserveOf"/>) with this trip's planned needs: the buys already on its list (the spell plan's scrolls, the focus), the plan's town learns, the scrolls the plan saves for and the leather set's missing pieces.</summary>
    private EverySpellReserve EverySpellReserveFor(HuntingGround ground, IReadOnlyList<ResupplyBuy> buys)
    {
        var c = CultureInfo.InvariantCulture;
        long listed = 0;
        foreach (var buy in buys)
        {
            listed += (long)buy.Count * (buy.Vendor.Sells.FirstOrDefault(w => w.Wcid == buy.Line.Wcid)?.Price ?? 0);
        }

        var town = _townLearns.Where(t => !_everySpellIds.Contains(t.Learn.SpellId)).Sum(t => (long)t.Price);
        long saving = 0;
        foreach (var need in _planNeeds)
        {
            if (need is { Kind: LearnNeedKind.Saving, Spell: { } spell })
            {
                saving += ground.PriceOf(spell.ScrollWcid) ?? 0;
            }
        }

        long leather = 0;
        if (LeatherTrip && LeatherSet.Default is { } set)
        {
            var (worn, held) = LeatherHeld();
            if (LeatherSwap.Applies(set, worn))
            {
                leather = LeatherSwap.Missing(set, held, LeatherCoveredByOthers(set)).Sum(p => (long)p.Price);
            }
        }

        var planned = listed + town + saving + leather;
        var why = string.Create(c, $"this trip's list {listed} ({buys.Count} line(s): the spell plan's scrolls and the focus), the plan's town learns {town}, the scrolls it saves for {saving}, the leather set's missing pieces {leather}");
        return EverySpellPlan.ReserveOf(ground, S.State.Self.Qualities.Level, Context.Facts.BuffBotOn?.Invoke() ?? false, Context.Facts.EverySpellVilla, S.State.HouseStatus, planned, why);
    }

    /// <summary>
    /// The sale's keep reason for a scroll under the every-spell rule (after <see cref="ScrollKeepRule"/>'s), or null: a scroll whose read ACE refused this session (kept, never bought again), one the rule bought this session, or one of a line's
    /// target level she can cast and lacks (read on the trip, never sold and bought back).
    /// </summary>
    /// <param name="item">The pack item.</param>
    internal string? EverySpellKeepReason(WorldObject item) =>
        !EverySpellOn ? null : EverySpellKeep.Reason(S.State, _memory, item, () =>
        {
            if (_planNeeds.Count == 0 && ScrivenerLearnPlan.RungOf(LadderLoader.WarMage, Context.Dungeon.Key, S.State.Self.Qualities.Level) is { } rung)
            {
                _planNeeds = [.. PlanNeeds(Context.Dungeon, rung)]; // the sale runs before the plan: the plan's lines are read once here, so a plan line is the plan's here too
            }

            return _everySpellKeep ??= EverySpellInputFor(Context.Dungeon, [], withPurse: false);
        });

    /// <summary>The spell plan's needs on this trip's ground and rung (the arguments <see cref="PlanLines"/> gives them).</summary>
    private IReadOnlyList<LearnNeed> PlanNeeds(HuntingGround ground, LadderRung rung) => EverySpellKeep.PlanNeeds(S.State, ground, rung, Context.Facts.LearnTravelLines);

    /// <summary>The every-spell step's after-check of one read: a learn counted, a refusal remembered and logged loud (the scroll is kept and never bought again).</summary>
    private void EverySpellRead(LearnLine learn, LearnReason refused, string why)
    {
        if (!_everySpellIds.Contains(learn.SpellId))
        {
            return;
        }

        var memo = EverySpellMemo;
        if (refused == LearnReason.None)
        {
            memo.Learned++;
            Log(Text($"every_spell: check after: {learn.Name} ({learn.SpellId}) is in the spellbook (the server's update): learned; {memo.Learned} learned by the rule this session"));
            return;
        }

        if (refused is LearnReason.TownTrip or LearnReason.RefusedBefore or LearnReason.Known)
        {
            return; // no read was refused: the trip, an earlier refusal or the book decided
        }

        memo.Unreadable[learn.SpellId] = Token(refused);
        Log(Text($"every_spell: TODO(m6-every-spell-unreadable): {learn.Name} ({learn.SpellId}): ACE did not teach it ({Token(refused)}: {why}); its scroll is kept (the sale never offers it) and the rule never buys it again"));
    }

    /// <summary>A need's rank when a line has several: the one that acts first.</summary>
    internal static int NeedRank(LearnNeedKind kind) => kind switch
    {
        LearnNeedKind.Due => 0,
        LearnNeedKind.OffHome => 1,
        LearnNeedKind.Saving => 2,
        LearnNeedKind.SkillShort => 3,
        LearnNeedKind.Later => 4,
        LearnNeedKind.Known => 5,
        _ => 6,
    };

    private static string TierWord(EverySpellTier tier) => tier switch
    {
        EverySpellTier.Gate => "gate",
        EverySpellTier.Bootstrap => "bootstrap",
        EverySpellTier.Damage => "damage",
        EverySpellTier.Buff => "buff",
        _ => "rest",
    };
}
