using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
using Lifestone.Data.Motion;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// <c>pathwarden</c> (M6 item 0 a3a, DoD 2): the Pathwarden quest as stations reading <c>quests/pathwarden.json</c> (<see cref="QuestCatalog"/>): go to Pathwarden Thorolf in Holtburg, talk, give the Pathwarden Token, check the Supply Key arrived, use the key on the Aluvian Pathwarden Chest,
/// take the robe, gauntlets, sollerets and trinket, and wear them (M6 item 14, the owner's order: the quest file's <c>armour</c> block, the chest's five plate pieces, is taken in place of the robe whenever ACE lets her lift it: <see cref="ArmourSet"/>,
/// <see cref="ArmourForm"/>; the robe's pieces are the named fallback). Every step is check, act, check (M5 D141): the state before, the act, the state after, and one named recovery when the after-check fails (logged <c>recovery=&lt;name&gt;</c>, never silent), after which a miss fails the station by name.
/// The completion is recorded through <see cref="JourneyContext.CompleteQuest"/> (the context's <see cref="QuestTimers"/>, saved through the host's store; a host with none logs <c>TODO(m6-quest-timers-no-store)</c> once). The walk to Thorolf has a purpose (M5 D47): the quest's first step.
/// <para>The station starts from Holtburg (the journey's known-state puts a character that is not there in Holtburg by a recovery place, counted apart, not a grant). Where the Academy's exit arrives is a3b's; until it lands, <c>--scenario=academy-exit --character=Winnie</c> is the bridge.
/// The station asks the server mod only for <c>quest has</c> (a read), so the journey's <c>granted=</c> stays what the station sent: nothing.</para>
/// </summary>
public sealed class PathwardenStation : IStation
{
    /// <summary>The station's name (the verdict line's <c>station=</c>).</summary>
    public const string StationName = "pathwarden";

    /// <inheritdoc />
    public string Name => StationName;

    /// <inheritdoc />
    public StationNeeds Needs { get; } = new(Where: StationWhere.Holtburg, Peace: true);

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context) => new PathwardenRun(context, QuestCatalog.Get("pathwarden"));
}

/// <summary>The Pathwarden station's run (see <see cref="PathwardenStation"/>).</summary>
public sealed class PathwardenRun : StationLeg
{
    /// <summary>How close the player stands to Thorolf and the chest for a Use, in metres: 1.5 m, proved live (M6 item 0's round 9 was green at it: the give, the key, the chest opened, 3 of 4 pieces worn; m6-plan.md item 0's record), closing <c>m6-pathwarden-use-range</c>.</summary>
    public const double UseRange = 1.5;

    /// <summary>The seconds a stamp or an item has to show after an act.</summary>
    public const double AfterSeconds = 8;

    private readonly Quest _quest;
    private uint _thorolf;
    private uint _chest;
    private int _recoveries;
    /// <summary>
    /// M6 7 AF (the owner: the robe leaves the hands bare): the burden gate's coverage rule. A piece over the line is still taken when it closes a part of the body the taken set leaves bare (<see cref="GearCoverage.ClosesGap"/>: it covers a
    /// gap, overlaps no taken piece, has armour) and the burden it leaves, less the worn pieces the plan sheds, stays under a load of 2, where retail's LoadMod is 0 (<c>MovementSystem.LoadMod</c>: she could not move); the Strength floor then
    /// pays the capacity from the measured burden (<c>StrengthFloor</c>). The pieces' coverage and armour level are the object's and its appraisal's, never named.
    /// </summary>
    private bool CoverageClosed(uint inChest, QuestThing piece, IReadOnlyCollection<uint> planned, int now, int own, int capacity, out string why)
    {
        var coverage = PathwardenWear.CoverageOf(S.State, inChest);
        int? armour = S.TryGetAppraisal(inChest, out var seen) ? seen.ArmorLevel : null;
        if (!GearCoverage.ClosesGap(planned, coverage, armour, out why))
        {
            why = $"{piece.Name}: {why}";
            return false;
        }

        var shed = 0;
        var all = planned.Aggregate(coverage, (a, c) => a | c);
        foreach (var w in WornNow())
        {
            if ((w.Coverage & all) != 0 && !_questWcids.Contains(w.Wcid) && S.State.Objects.TryGet(w.Guid, out var o) && o.Weenie.Burden is { } b)
            {
                shed += b;
            }
        }

        var left = now + own - shed;
        if (capacity <= 0 || left >= 2L * capacity)
        {
            why = $"{piece.Name}: {why}, but the burden it leaves, {now} + {own} - {shed} shed = {left}, is not under twice the capacity {capacity} (retail's LoadMod is 0 there)";
            return false;
        }

        why = $"{why}; burden {now} + {own} - {shed} the plan sheds = {left} of the capacity {capacity} ({100L * left / capacity}%), under a load of 2; the Strength floor pays the rest";
        return true;
    }

    private HashSet<uint> _questWcids = [];
    private readonly HashSet<uint> _leftInChest = []; // M6 0 lead: the pieces the burden gate left in the chest (counted left off by name)
    private readonly List<uint> _takenOff = []; // M6 0 lead: the worn pieces the wear plan took off for a quest piece (the shed drops them)

    internal PathwardenRun(JourneyContext context, Quest quest)
        : base(StationNameOf(quest), context)
    {
        _quest = quest;
        PieceCount = quest.Steps.Count(s => s.Kind == QuestStepKind.PickUp);
    }

    /// <summary>The quest's timers for the character: the completion is recorded here.</summary>
    public QuestTimers Timers => Context.QuestTimersInUse;

    /// <summary>True when the quest's done condition was seen (the stamp held and the trinket worn).</summary>
    public bool PathwardenDone { get; private set; }

    /// <summary>True when the Pathwarden Trinket is worn at the end (the field reads what it names; the pieces are <see cref="WornCount"/> and <see cref="BlockedCount"/>).</summary>
    public bool TrinketWorn { get; private set; }

    /// <summary>The pieces worn (0 to 4).</summary>
    public int WornCount { get; private set; }

    /// <summary>The pieces left off because another of the quest's own pieces covers the same parts (ACE refuses the second; <c>TODO(m6-pathwarden-robe-covers-feet)</c>): the sollerets, while the robe is worn (0 to 3).</summary>
    public int BlockedCount { get; private set; }

    /// <inheritdoc />
    public override string VerdictFields() =>
        string.Create(CultureInfo.InvariantCulture, $"pathwarden_done={Yes(PathwardenDone)} trinket_worn={Yes(TrinketWorn)} worn={WornCount}/{PieceCount} blocked={BlockedCount} form={FormName} load={LoadField} bot_first={BotFirstField} timer={(Timers.Get(_quest.Key) is null ? "none" : "recorded")} recoveries={_recoveries} problems={Problems.Count} granted={Granted}")
        + (Failure is null ? string.Empty : " why=" + Failure.Replace(' ', '_'));

    private static string StationNameOf(Quest quest) => "quest-" + quest.Key;

    private QuestStep StepOf(QuestStepKind kind, int nth = 0) => _quest.Steps.Where(s => s.Kind == kind).ElementAt(nth);

    /// <inheritdoc />
    protected override IEnumerable<ActionHandle?> Script()
    {
        var go = StepOf(QuestStepKind.Go);
        var talk = _quest.Steps.FirstOrDefault(s => s.Kind == QuestStepKind.Talk); // none in pathwarden.json since item 0's live round 1: the give needs no talk
        var give = StepOf(QuestStepKind.Give);
        var use = StepOf(QuestStepKind.Use);
        var found = talk is null ? null : talk.Expect.Stamp ?? throw new InvalidOperationException("TODO(m6-pathwarden-stamp): the talk step names no stamp");
        var complete = _quest.Done.Stamp ?? throw new InvalidOperationException("TODO(m6-pathwarden-stamp): the quest names no done stamp");
        var token = give.Item!.Wcid!.Value;
        var key = give.Expect.Item!.Wcid!.Value;
        var thorolfWcid = go.Target!.Wcid!.Value;
        var chestWcid = use.Target!.Wcid!.Value;

        // M6 item 14 (the lead's order of 2026-11-11): the bot's buffs come before the chest. With the armour form wanted and the stop at the bot applying to the run, the stop runs first: her Strength of 10 becomes 50, the capacity 7,500,
        // and no lift at the chest is marginal. A stop that fails is named and the chest is opened on her own Strength, the shed first.
        Stage = "buff-bot";
        foreach (var h in BotFirst()) { yield return h; }

        // Step 1, go: check (Thorolf within reach?), act (the walk), check (he is in the object table within reach).
        Stage = "go";
        foreach (var h in Approach(go.Place!, thorolfWcid, go.Target.Name, id => _thorolf = id)) { yield return h; }

        // Step 2, talk (only when the quest file has one): check (the stamp is not held yet), act (Use him), check (the stamp), recovery (walk up again and Use him once more).
        if (found is not null)
        {
            Stage = "talk";
            var hasFound = Has(found);
            foreach (var h in hasFound.Wait(this)) { yield return h; }

            if (hasFound.Held)
            {
                Log($"talk: {found} is held already (an earlier run); the greeting is skipped");
            }
            else
            {
                foreach (var h in ActAndStamp("talk", () => S.Actions.Use(_thorolf, ActionSource.Host), found, () => Approach(go.Place!, thorolfWcid, go.Target.Name, id => _thorolf = id))) { yield return h; }
            }
        }

        // Step 3, give: check (the token is in the pack), act (Give), check (the key is in the pack and the done stamp is held), recovery (a fresh approach and one more Give).
        Stage = "give";
        var tokenGuid = InPack(token).FirstOrDefault();
        var keyBefore = InPack(key).FirstOrDefault();
        if (keyBefore == 0 && !Verify(tokenGuid != 0, $"give: the pack holds no {give.Item.Name} ({token}) and no {give.Expect.Item!.Name} ({key})"))
        {
            throw new InvalidOperationException("give: no token and no key");
        }

        if (keyBefore == 0)
        {
            var give1 = S.Actions.Give(_thorolf, tokenGuid, 1, ActionSource.Host);
            yield return give1;
            Log("Give the token to Thorolf: " + Describe(give1));
            foreach (var h in Until(() => InPack(key).Count > 0, AfterSeconds)) { yield return h; }

            if (InPack(key).Count == 0)
            {
                _recoveries++;
                Log($"recovery=give-again: no {give.Expect.Item!.Name} in the pack {AfterSeconds:0} s after the Give (outcome {Describe(give1)}); walking up to Thorolf and giving once more");
                foreach (var h in Approach(go.Place!, thorolfWcid, go.Target.Name, id => _thorolf = id)) { yield return h; }

                tokenGuid = InPack(token).FirstOrDefault();
                if (Verify(tokenGuid != 0, "give: the token left the pack but no key came (the Give was taken and the reward lost)"))
                {
                    var give2 = S.Actions.Give(_thorolf, tokenGuid, 1, ActionSource.Host);
                    yield return give2;
                    Log("Give the token to Thorolf (recovery): " + Describe(give2));
                    foreach (var h in Until(() => InPack(key).Count > 0, AfterSeconds)) { yield return h; }
                }
            }

            Require(Verify(InPack(key).Count > 0, $"give: no {give.Expect.Item!.Name} ({key}) in the pack after the recovery"), "give: the key never arrived");
        }

        // Thorolf's emote set gives the key (order 2) before it stamps the quest (order 4, after a Tell with its delay: the W0
        // SELECT of his Give emotes), so the stamp is asked once a second for up to AfterSeconds (live round 3: asked at once
        // after the key, it was not held yet). Each ask is logged with its answer: a wait for a known later effect, not a retry.
        var hasComplete = Has(complete);
        foreach (var h in hasComplete.Wait(this)) { yield return h; }
        for (var ask = 2; !hasComplete.Held && ask <= (int)AfterSeconds; ask++)
        {
            foreach (var h in Until(() => false, 1.0)) { yield return h; }
            hasComplete = Has(complete);
            foreach (var h in hasComplete.Wait(this)) { yield return h; }
            Log($"give: the quest stamp {complete} asked again after the key (ask {ask}): held={(hasComplete.Held ? "yes" : "no")}");
        }

        Verify(hasComplete.Held, $"give: the quest stamp {complete} is not held {AfterSeconds:0} s after the key arrived");

        // Step 4, use: check (the key is held, the chest is in view), act (UseWithTarget), check (the chest opens and lists its pieces), recovery (walk up to the chest and use the key once more).
        Stage = "use";
        foreach (var h in Approach(use.Place!, chestWcid, use.Target!.Name, id => _chest = id)) { yield return h; }

        var keyGuid = InPack(key).FirstOrDefault();
        Require(Verify(keyGuid != 0 && _chest != 0, "use: the key or the chest is missing before the unlock"), "use: no key or no chest");
        var unlock = S.Actions.UseWithTarget(keyGuid, _chest, ActionSource.Host);
        yield return unlock;
        Log("UseWithTarget the key on the chest: " + Describe(unlock));
        foreach (var h in OpenChest()) { yield return h; }

        if (!ChestOpen)
        {
            _recoveries++;
            Log("recovery=unlock-again: the chest did not open after the key; walking up to it and using the key once more");
            foreach (var h in Approach(use.Place!, chestWcid, use.Target.Name, id => _chest = id)) { yield return h; }

            var unlock2 = S.Actions.UseWithTarget(keyGuid, _chest, ActionSource.Host);
            yield return unlock2;
            Log("UseWithTarget the key on the chest (recovery): " + Describe(unlock2));
            foreach (var h in OpenChest()) { yield return h; }
        }

        Require(Verify(ChestOpen, "use: the chest is not open after the key and the recovery"), "use: the chest stayed shut");

        // Steps 5 to 8, pick-up: check (the piece is in the chest), act (PickUp), check (it is in the pack).
        Stage = "pick-up";
        var pieces = _quest.Steps.Where(s => s.Kind == QuestStepKind.PickUp).Select(s => s.Item!).ToList();
        var carry = _quest.Carry;
        var taken = 0;
        var startBurden = -1;
        var planned = new List<uint>(); // M6 7 AF: the coverage of the pieces taken so far: the set she will wear, which the coverage rule of the burden gate reads

        // M6 item 14 (the owner: armour instead of the robe): the quest file's armour block. The forecast (ArmourForm.Choose on the burden, the worn pieces the set replaces and the Strength, raw and now) is logged with its numbers; the state
        // decides: the set's pieces, in its order, then the quest's other pieces (the trinket), each lifted and worn before the next (the hauberk's Strength line raises the capacity for the rest). A first piece ACE would not let her lift
        // is the named fallback: the quest's own pieces (the robe form), as before this item.
        var armour = ArmourSet.Of(_quest.Key);
        _ = ChooseForm(armour);
        if (armour is not null)
        {
            Form = ArmourFormKind.Armour;
            var armourPieces = armour.PiecesWith(pieces);
            _questWcids = [.. armourPieces.Select(p => p.Wcid!.Value), .. armour.InsteadOf];
            var blockedBefore = BlockedCount;
            foreach (var h in ArmourPieces(armourPieces, armour)) { yield return h; }

            BlockedCount = blockedBefore; // the wear stage below counts every piece once
            if (_armourNotLifted)
            {
                Form = ArmourFormKind.Robe;
                _questWcids = [];
                _leftInChest.Clear();
            }
            else
            {
                pieces = armourPieces;
            }
        }

        var robeForm = Form == ArmourFormKind.Robe;
        if (robeForm && carry is not null)
        {
            // M6 0 F8: a quest with a burden gate takes the lightest piece first, so the gate keeps the most pieces the capacity allows.
            pieces = [.. pieces.OrderBy(p => InChest(p.Wcid!.Value) is var g and not 0 && S.State.Objects.TryGet(g, out var o) ? o.Weenie.Burden ?? int.MaxValue : int.MaxValue)];
        }

        foreach (var piece in robeForm ? pieces : [])
        {
            if (InPack(piece.Wcid!.Value).Count > 0 || Worn(piece.Wcid.Value))
            {
                Log($"pick-up: {piece.Name} ({piece.Wcid}) is held already");
                continue;
            }

            var inChest = InChest(piece.Wcid.Value);
            if (!Verify(inChest != 0, $"pick-up: the chest does not list {piece.Name} ({piece.Wcid})"))
            {
                continue;
            }

            if (carry is not null)
            {
                // Check before (the burden gate): the burden now, the piece's own burden and the capacity; act, take it or leave it in the chest by name; check after is the pack check below.
                var load = LootContext.Of(S.State);
                startBurden = startBurden < 0 ? load.Burden : startBurden;
                var now = Math.Max(load.Burden, startBurden + taken);
                int? own = S.State.Objects.TryGet(inChest, out var chestPiece) && chestPiece.Weenie.Burden is { } pieceBurden ? pieceBurden : null;
                if (own is null)
                {
                    // M6 2 d (m6-pathwarden-piece-burden, closed): the object model carries no burden for a piece the chest lists, so the piece is appraised and its Burden (assessment property 5, the appraisal's) is read: check (no burden held),
                    // act (Identify), check (the appraisal carries a Burden). A piece whose appraisal does not arrive is still left by name, the gate does not guess.
                    var ask = S.Actions.Identify(inChest, ActionSource.Host);
                    yield return ask;
                    foreach (var h in Until(() => S.TryGetAppraisal(inChest, out var seen) && seen.Burden is not null, AfterSeconds)) { yield return h; }

                    own = S.TryGetAppraisal(inChest, out var appraisal) ? appraisal.Burden : null;
                    Log(own is null
                        ? $"pick-up: {piece.Name} ({piece.Wcid}): the appraisal carries no Burden {AfterSeconds:0} s after the Identify"
                        : $"pick-up: {piece.Name} ({piece.Wcid}): burden {own} read from its appraisal (assessment property 5)");
                }

                if (own is null)
                {
                    Log($"pick-up: {piece.Name} ({piece.Wcid}) left in the chest: its burden is not read, neither from the object nor from its appraisal; the burden gate does not guess");
                    continue;
                }

                if (!carry.Fits(now, load.Capacity, own.Value) && carry.TakesAlways(piece.Wcid.Value))
                {
                    Log($"pick-up: {piece.Name} ({piece.Wcid}) taken over the line: the quest's named reward (carry.always); burden {now} + {own} against {carry.LinePercent}% of the capacity {load.Capacity} (ACE slows the run above 100%, EncumbranceSystem.GetBurdenMod)");
                }
                else if (!carry.Fits(now, load.Capacity, own.Value) && CoverageClosed(inChest, piece, planned, now, own.Value, load.Capacity, out var closeWhy))
                {
                    Log($"pick-up: {piece.Name} ({piece.Wcid}) taken over the line: the coverage rule (M6 7 AF): {closeWhy}");
                }
                else if (!carry.Fits(now, load.Capacity, own.Value))
                {
                    Log($"pick-up: {piece.Name} ({piece.Wcid}) left in the chest: burden {now} + {own} + the restock reserve {carry.ReserveBurden} is over {carry.LinePercent}% of the capacity {load.Capacity} (150 x Strength; raise Strength and it is taken)");
                    _leftInChest.Add(piece.Wcid.Value);
                    continue;
                }

                taken += own.Value;
                Log($"pick-up: {piece.Name} ({piece.Wcid}) fits: burden {now} + {own} + the restock reserve {carry.ReserveBurden} is within {carry.LinePercent}% of the capacity {load.Capacity}");
            }

            var pieceCoverage = PathwardenWear.CoverageOf(S.State, inChest);
            var mark = S.State.Chat.Total;
            var take = S.Actions.PickUp(inChest, 0, 0, ActionSource.Host);
            yield return take;
            Log($"PickUp {piece.Name} (0x{inChest:X8}, container before 0x{ContainerOf(inChest):X8}): {Describe(take)}");
            foreach (var h in Until(() => InPack(piece.Wcid.Value).Count > 0, AfterSeconds)) { yield return h; }

            if (Verify(InPack(piece.Wcid.Value).Count > 0, $"pick-up: {piece.Name} ({piece.Wcid}) is not in the pack after the PickUp ({Describe(take)})", inChest, mark) && pieceCoverage != 0)
            {
                planned.Add(pieceCoverage);
            }
        }

        foreach (var h in CloseChest()) { yield return h; }

        // M6 7 AF: the check after, by state: with the quest's pieces taken, a bare part of the body is named (the floor's gauntlets buy the hands later).
        if (!robeForm)
        {
            planned = [.. WornNow().Where(w => _questWcids.Contains(w.Wcid)).Select(w => w.Coverage)];
        }

        Log($"pick-up: the taken pieces' coverage leaves 0x{GearCoverage.Gaps(planned):X} of the body bare (hands are 0x{GearCoverage.Hands:X})");

        // Wear (live round 2: worn 2/4): ACE refuses a clothing piece while any worn piece's ClothingPriority overlaps its own, so each wear is check (in the pack, not worn, who covers its parts), act (take off a starter piece in the way, then
        // Wield at the item's own locations), check (the equipped entry), recovery (one more Wield after a pause). A quest piece in the way (the robe covers the feet, the sollerets' parts) is not taken off: the later piece is left in the pack, loudly.
        Stage = "wear";
        var questWcids = robeForm ? pieces.Select(p => p.Wcid!.Value).ToHashSet() : _questWcids;
        _questWcids = questWcids;
        foreach (var piece in pieces)
        {
            var wcid = piece.Wcid!.Value;
            if (Worn(wcid))
            {
                WornCount++;
                continue;
            }

            if (_leftInChest.Contains(wcid))
            {
                BlockedCount++;
                Log($"wear: {piece.Name} ({wcid}) was left in the chest by the burden gate: counted left off by name, not worn");
                continue;
            }

            var guid = InPack(wcid).FirstOrDefault();
            if (!Verify(guid != 0, $"wear: {piece.Name} ({wcid}) is not in the pack", guid))
            {
                continue;
            }

            foreach (var h in WearPiece(piece, guid, questWcids)) { yield return h; }

            if (_wearLeftOff)
            {
                continue; // named already: a quest piece in the way (counted blocked), or a piece in the way that would not come off
            }

            if (Verify(Worn(wcid), $"wear: {piece.Name} ({wcid}) is not worn", guid))
            {
                WornCount++;
            }
        }

        // M6 0 lead (live probe 3: the climber stood at 164% of a Strength-10 capacity, the Academy Coat 600 and the starter boots 420 among it): the shed. Clothing the quest's pieces replaced
        // is dropped: every piece the wear plan took off, and every piece of clothing in the pack whose coverage overlaps a quest piece now worn (it cannot be worn beside it, ACE
        // Creature_Equipment.cs:50-58, 93-101 @95ef426d). Check (in the pack, not worn, not a quest piece), act (Drop, GameAction 0x001B), check (no longer in the pack).
        Stage = "shed";
        var questCoverage = WornNow().Where(w => questWcids.Contains(w.Wcid)).Aggregate(0u, (a, w) => a | w.Coverage);
        var shed = _takenOff.Where(g => !IsEquipped(g)).ToList();
        foreach (var entry in S.State.Self.Inventory.Pack)
        {
            if (entry.ContainerType == 0 && !shed.Contains(entry.Guid) && S.State.Objects.TryGet(entry.Guid, out var po) && !questWcids.Contains(po.Weenie.WeenieClassId)
                && (PathwardenWear.CoverageOf(S.State, entry.Guid) & questCoverage) != 0)
            {
                shed.Add(entry.Guid);
            }
        }

        var leather = LeatherSet.Of(_quest.Key);
        foreach (var g in shed)
        {
            if (leather is not null && S.State.Objects.TryGet(g, out var kept) && leather.PieceFilledBy(kept.Weenie.WeenieClassId) is { } part && !IsEquipped(g))
            {
                // M6 item 14 (leather in the robe's place): a piece that fills a part of the leather set (the Leather Boots a new character wears: armour level 130) is kept for the swap; the robe covers its part until then.
                Log($"shed: kept {kept.Name} (0x{g:X8}, burden {kept.Weenie.Burden ?? 0}): it fills the leather set's '{part.Name}' part (armour level {part.ArmourLevel}); carried until the set is bought and worn in the robe's place");
                continue;
            }

            foreach (var h in ShedOne(g, $"replaced by the quest's pieces (coverage 0x{questCoverage:X})")) { yield return h; }
        }

        TrinketWorn = _quest.Done.Item?.Wcid is { } trinket && Worn(trinket);

        // The done condition: the stamp held, the done item (the trinket) worn and every piece either worn or left off by name (the sollerets, while the robe covers the feet); recorded through the timers.
        Stage = "done-check";
        var finished = Has(complete);
        foreach (var h in finished.Wait(this)) { yield return h; }

        PieceCount = pieces.Count;
        PathwardenDone = finished.Held && TrinketWorn && WornCount + BlockedCount == PieceCount && (robeForm || BlockedCount == 0);
        LogLoad("done", last: true);
        if (Verify(PathwardenDone, $"done: stamp {complete} held {finished.Held}, the trinket worn {TrinketWorn}, {WornCount} of the {PieceCount} pieces worn and {BlockedCount} left off by name (form {FormName})"))
        {
            var entry = Context.CompleteQuest(_quest, S.Clock.Time.GetUtcNow(), QuestTimers.HowItem); // M6 5 Q2: record, save through the host's store, log the line (closes m6-pathwarden-timers-save)
            Context.Facts.MarkQuestDone(_quest.Key); // M6 0 F4: the level up job reads it (the ladder's quest rung)
            Log($"quest {_quest.Key} completed (completions {entry.Completions})");
        }
    }

    // ---------------------------------------------------------------------------------------------- the armour form (M6 item 14)

    private bool _wearLeftOff;
    private bool _armourNotLifted; // the set's first piece could not be lifted: the run fell back to the quest's own pieces (the robe form), by name

    /// <summary>
    /// The wear of one piece held in the pack (live round 2: worn 2/4): check (who covers its parts), act (take off a piece in the way that is not the quest's, then Wield at the item's own locations), check (the equipped entry), recovery
    /// (one more Wield after a pause). A quest piece in the way is the named blocker: the piece stays in the pack, counted <see cref="BlockedCount"/>, and <c>_wearLeftOff</c> says the caller's worn check is not due.
    /// </summary>
    private IEnumerable<ActionHandle?> WearPiece(QuestThing piece, uint guid, ISet<uint> questWcids)
    {
        var wcid = piece.Wcid!.Value;
        _wearLeftOff = false;
        var plan = PathwardenWear.Plan(guid, WornNow(), PathwardenWear.CoverageOf(S.State, guid), S.State.Objects.TryGet(guid, out var held) ? held.Weenie.ValidLocations ?? 0u : 0u, questWcids);
        if (plan.Blocker is { } blocker)
        {
            BlockedCount++;
            _wearLeftOff = true;
            Warn("wear", $"{piece.Name} ({wcid}) is left in the pack: {blocker.Name} (guid 0x{blocker.Guid:X8}, wcid {blocker.Wcid}), one of the quest's own pieces, covers 0x{blocker.Coverage & plan.Coverage:X} of its parts 0x{plan.Coverage:X}, and ACE refuses it while that is worn (Creature_Equipment.cs:50-58, 93-101); TODO(m6-pathwarden-robe-covers-feet): the lead decides robe or sollerets", guid);
            yield break;
        }

        foreach (var inTheWay in plan.TakeOff)
        {
            foreach (var h in TakeOff(inTheWay, piece.Name)) { yield return h; }

            if (IsEquipped(inTheWay.Guid))
            {
                _wearLeftOff = true;
            }
        }

        if (_wearLeftOff)
        {
            yield break;
        }

        for (var attempt = 0; attempt < 2 && !Worn(wcid); attempt++)
        {
            if (attempt == 1)
            {
                _recoveries++;
                Log($"recovery=wear-again: {piece.Name} is not worn {AfterSeconds:0} s after the Wield; one more Wield after a short pause");
                foreach (var h in Pause(1)) { yield return h; }
            }

            var valid = S.State.Objects.TryGet(guid, out var o) ? o.Weenie.ValidLocations ?? 0u : 0u;
            if (!Verify(valid != 0, $"wear: {piece.Name} has no valid locations in the object model", guid))
            {
                break;
            }

            // M6 16 E5e1: the one wield routine: the slot by its one rule, the wield, its answer logged, then the wait for the piece's class in the worn set
            var before = LocationOf(guid);
            var mark = S.State.Chat.Total;
            var wield = new Quartermaster.WieldResult();
            var host = new Quartermaster.WieldHost(Until)
            {
                Sending = _ => mark = S.State.Chat.Total,
                Answered = r => Log($"Wield {piece.Name} (0x{guid:X8}) at 0x{r.Location:X8} of valid 0x{valid:X8}: {r.Outcome(Describe)}; location before 0x{before:X8}"),
            };
            foreach (var h in Quartermaster.Wardrobe.PutOn(S, new Quartermaster.WieldRequest(guid) { Name = piece.Name, Valid = valid, Source = ActionSource.Host, WornSeconds = AfterSeconds, Worn = () => Worn(wcid) }, wield, host)) { yield return h; }

            var location = wield.Location;
            Log($"Wield {piece.Name}: location after 0x{LocationOf(guid):X8}{ServerText(mark)}");
            if (!Worn(wcid))
            {
                Warn("wear", $"{piece.Name} is not worn after the Wield (attempt {attempt + 1}); requested 0x{location:X8} of valid 0x{valid:X8}; the Wield was {wield.Outcome(Describe)}; location before 0x{before:X8} after 0x{LocationOf(guid):X8}", guid, mark);
            }
        }
    }

    /// <summary>A worn piece in the way taken off into the pack: check (it is worn), act (Unwield), check (it left the equipped list); it is then one of the pieces the shed drops.</summary>
    private IEnumerable<ActionHandle?> TakeOff(PathwardenWear.WornPiece inTheWay, string forWhat)
    {
        var mark = S.State.Chat.Total;
        var off = Quartermaster.Wardrobe.SendTakeOff(S, new Quartermaster.WieldBlocker(inTheWay.Guid, inTheWay.Name) { Source = ActionSource.Host }); // M6 16 E5e1: the wear service's take-off
        yield return off;
        Log($"Unwield {inTheWay.Name} (0x{inTheWay.Guid:X8}, wcid {inTheWay.Wcid}, at 0x{inTheWay.Location:X8}, covers 0x{inTheWay.Coverage:X}) to free {forWhat}: {Describe(off)}");
        foreach (var h in Until(() => !IsEquipped(inTheWay.Guid), AfterSeconds)) { yield return h; }

        if (Verify(!IsEquipped(inTheWay.Guid), $"wear: {inTheWay.Name} (0x{inTheWay.Guid:X8}) is still worn after the Unwield ({Describe(off)}), so {forWhat} cannot be worn", inTheWay.Guid, mark))
        {
            _takenOff.Add(inTheWay.Guid);
        }
    }

    /// <summary>The drop of one piece the quest's pieces replaced: check (in the pack, not worn), act (Drop, GameAction 0x001B), check (no longer in the pack).</summary>
    private IEnumerable<ActionHandle?> ShedOne(uint g, string why)
    {
        var name = S.State.Objects.TryGet(g, out var so) ? so.Name : "?";
        var burden = S.State.Objects.TryGet(g, out var sb) ? sb.Weenie.Burden ?? 0 : 0;
        if (IsEquipped(g) || !S.State.Self.Inventory.Pack.Any(e => e.Guid == g))
        {
            Log($"shed: {name} (0x{g:X8}) is worn or not in the pack: kept");
            yield break;
        }

        var drop = S.Actions.Drop(g, ActionSource.Host);
        yield return drop;
        foreach (var h in Until(() => !S.State.Self.Inventory.Pack.Any(e => e.Guid == g), AfterSeconds)) { yield return h; }

        Verify(!S.State.Self.Inventory.Pack.Any(e => e.Guid == g), $"shed: {name} (0x{g:X8}) is still in the pack after the Drop ({Describe(drop)})", g);
        Log($"shed: dropped {name} (0x{g:X8}, burden {burden}): {why}; {Describe(drop)}");
    }

    /// <summary>
    /// The check after a drop or a lift, by state: the server's burden (EncumbranceVal, a private property update that trails the act) has moved from <paramref name="before"/> the way the act moves it. A wait for a known later effect,
    /// logged with the figures; when it does not move in <see cref="AfterSeconds"/> the line says so and the next gate reads what the server holds (it never adds a guess of its own).
    /// </summary>
    private IEnumerable<ActionHandle?> BurdenSettles(int before, bool lighter, string what)
    {
        bool Moved() => lighter ? LootContext.Of(S.State).Burden < before : LootContext.Of(S.State).Burden > before;
        if (!Moved())
        {
            foreach (var h in Until(Moved, AfterSeconds)) { yield return h; }
        }

        Log($"burden: after {what}: {before} -> {LootContext.Of(S.State).Burden}{(Moved() ? string.Empty : $" (not moved {AfterSeconds:0} s after the act: the server's figure is read as it stands)")}");
    }

    /// <summary>The form this run took: the armour set, or the quest's own pieces (the robe).</summary>
    public ArmourFormKind Form { get; private set; } = ArmourFormKind.Robe;

    /// <summary>The pieces the run was to wear (the form's): the verdict's <c>worn=n/m</c>.</summary>
    public int PieceCount { get; private set; }

    private string FormName => Form == ArmourFormKind.Armour ? "armour" : "robe";

    /// <summary>The verdict's <c>load=</c>: the burden in percent of the capacity at the end (the Strength with its enchantments), or <c>?</c> before it is read.</summary>
    private string LoadField { get; set; } = "?";

    private (int Raw, int Now) StrengthRead()
    {
        var q = S.State.Self.Qualities;
        return q.TryGetAttributeLevels(StandingStrength.Strength, out _, out var raw) ? ((int)raw, (int)EnchantmentRules.EnchantAttribute(q.Enchantments, StandingStrength.Strength, raw)) : ((int)LootContext.UnknownStrength, (int)LootContext.UnknownStrength);
    }

    /// <summary>What she wears and what lies in her main pack, for <see cref="PathwardenWear.ShedFirst"/>: the worn pieces as <see cref="WornNow"/> reads them, the pack's items (no side pack, no focus) by their headers.</summary>
    private List<PathwardenWear.HeldPiece> HeldNow()
    {
        var list = new List<PathwardenWear.HeldPiece>();
        foreach (var w in WornNow())
        {
            list.Add(new PathwardenWear.HeldPiece(w.Guid, w.Wcid, w.Name, w.Coverage, S.State.Objects.TryGet(w.Guid, out var o) ? o.Weenie.Burden ?? 0 : 0, Worn: true));
        }

        foreach (var entry in S.State.Self.Inventory.Pack)
        {
            if (entry.ContainerType == 0 && S.State.Objects.TryGet(entry.Guid, out var po))
            {
                list.Add(new PathwardenWear.HeldPiece(entry.Guid, po.Weenie.WeenieClassId, po.Name, PathwardenWear.CoverageOf(S.State, entry.Guid), po.Weenie.Burden ?? 0, Worn: false));
            }
        }

        return list;
    }

    /// <summary>The verdict's <c>bot_first=</c>: <c>ok</c> (the stop ran and her Strength holds the bot's line), <c>up</c> (it held it already), <c>failed</c> (the stop ran and it does not: named, the shed-first order follows), <c>off</c> (the stop does not apply to the run), <c>n/a</c> (the quest has no armour set).</summary>
    public string BotFirstField { get; private set; } = "n/a";

    /// <summary>
    /// The stop at the buff bot before the chest (see <see cref="ArmourForm.BotFirst"/>): check before (the armour set wanted, the stop applying, her Strength raw and now), act (the <see cref="BuffBotStation"/> run to its end, as the
    /// logistics trip's stop), check after (her Strength holds the bot's line). A failure is a WARN by name (<c>armour-bot-first-failed</c>) and never fails the station.
    /// </summary>
    private IEnumerable<ActionHandle?> BotFirst()
    {
        var (raw, now) = StrengthRead();
        var plan = ArmourForm.BotFirst(ArmourSet.Of(_quest.Key), Context.Facts.BuffBotOn?.Invoke() == true, raw, now);
        BotFirstField = plan.Field;
        Log(plan.Why);
        if (plan.Kind != BotFirstKind.Stop)
        {
            yield break;
        }

        var stop = new BuffBotStation().Start(Context);
        var status = stop.Step(S, S.Clock.Now);
        while (status == ScenarioStatus.Running)
        {
            yield return null;
            status = stop.Step(S, S.Clock.Now);
        }

        var (rawAfter, after) = StrengthRead();
        if (ArmourForm.BotStrengthUp(rawAfter, after))
        {
            BotFirstField = "ok";
            Log($"bot first: check after: Strength {now} -> {after} (raw {rawAfter}, the bot's line +{StandingStrength.BotPoints}): the capacity {EncumbranceSystem.EncumbranceCapacity(now, 0)} -> {EncumbranceSystem.EncumbranceCapacity(after, 0)}; the stop ended {status}: {stop.VerdictFields()}; the chest follows");
        }
        else
        {
            BotFirstField = "failed";
            Context.Log.Warn("station", $"[live {Name} t={S.Clock.Now:0.0}] bot first: FAILED (armour-bot-first-failed): Strength {now} -> {after} (raw {rawAfter}) does not hold the bot's line (+{StandingStrength.BotPoints}); the stop ended {status}: {stop.VerdictFields()}; the chest is opened on her own Strength, the shed first");
        }
    }

    /// <summary>The form's forecast on the state (<see cref="ArmourForm.Choose"/>), logged with its numbers: at WARN when it says a set the file names cannot be lifted (never silent). The first lift decides (<see cref="ArmourPieces"/>).</summary>
    private ArmourFormChoice ChooseForm(ArmourSet? armour)
    {
        var load = LootContext.Of(S.State);
        var (raw, now) = StrengthRead();
        var shed = 0;
        if (armour is not null)
        {
            HashSet<uint> own = [.. armour.Pieces.Select(p => p.Wcid), .. armour.InsteadOf];
            var first = PathwardenWear.ShedFirst(HeldNow(), armour.Coverage, own);
            shed = first.Sum(h => h.Burden);
            Log($"forecast: the shed before the first lift: {(first.Count == 0 ? "nothing" : string.Join(", ", first.Select(h => $"{h.Name} ({(h.Worn ? "worn" : "in the pack")}, burden {h.Burden})")))}: {shed} in all");
        }

        var choice = ArmourForm.Choose(armour, load.Burden, shed, raw, now);
        if (armour is not null && choice.Kind != ArmourFormKind.Armour)
        {
            Context.Log.Warn("station", $"[live {Name} t={S.Clock.Now:0.0}] forecast: {choice.Why}; the state decides at the first lift, after the shed");
        }
        else
        {
            Log("forecast: " + choice.Why);
        }

        if (choice.Kind == ArmourFormKind.Armour && StandingStrength.BotPoints is var bot and > 0)
        {
            Log($"armour form: with the buff bot's Strength line (+{bot}): {ArmourForm.LoadText(choice.BurdenAfter, Math.Max(choice.StrengthAfter, raw + bot))}");
        }

        return choice;
    }

    /// <summary>The load now, logged and kept for the verdict's <c>load=</c>; the station's last read (<paramref name="last"/>) is written at WARN when it is over the line where the run slows, with what the buff bot's Strength line would make it.</summary>
    private void LogLoad(string when, bool last = false)
    {
        var load = LootContext.Of(S.State);
        var (raw, now) = StrengthRead();
        LoadField = load.Capacity > 0 ? string.Create(CultureInfo.InvariantCulture, $"{100L * load.Burden / load.Capacity}%") : "?";
        var text = $"load: {when}: form {FormName}: {ArmourForm.LoadText(load.Burden, now)} (raw Strength {raw})";
        if (last && load.Capacity > 0 && load.Burden > load.Capacity)
        {
            var bot = StandingStrength.BotPoints;
            Context.Log.Warn("station", $"[live {Name} t={S.Clock.Now:0.0}] {text}; with the buff bot's Strength line (+{bot}): {ArmourForm.LoadText(load.Burden, Math.Max(now, raw + bot))}; the Strength floor pays what is left (StrengthFloor, StandingStrength)");
        }
        else
        {
            Log(text);
        }
    }

    /// <summary>
    /// The armour form's pick-ups: the worn pieces the set replaces are taken off and dropped first (the Academy Coat and the starter boots: their burden is what lets a Strength of 10 lift the hauberk under ACE's limit), then each piece in the
    /// set's order is lifted and worn before the next: check before (it is in the chest; the burden, its own burden and ACE's limit of <see cref="ArmourForm.LiftLimit"/> x the capacity now, waited for once when the capacity trails the last
    /// wield), act (PickUp, Wield), check after (in the pack, then worn; the load logged). A piece ACE would not let her lift is left in the chest by name and counted <see cref="BlockedCount"/>; the done check then fails the station by name.
    /// </summary>
    private IEnumerable<ActionHandle?> ArmourPieces(List<QuestThing> pieces, ArmourSet armour)
    {
        Stage = "shed-first";
        foreach (var surplus in PathwardenWear.ShedFirst(HeldNow(), armour.Coverage, _questWcids))
        {
            if (surplus.Worn)
            {
                var worn = WornNow().FirstOrDefault(w => w.Guid == surplus.Guid);
                if (worn is not null)
                {
                    foreach (var h in TakeOff(worn, "the armour set")) { yield return h; }
                }
            }

            if (!IsEquipped(surplus.Guid))
            {
                var before = LootContext.Of(S.State).Burden;
                foreach (var h in ShedOne(surplus.Guid, $"replaced by the armour set (coverage 0x{armour.Coverage:X}), {(surplus.Worn ? "taken off" : "in the pack")}, dropped before the first piece is lifted")) { yield return h; }

                foreach (var h in BurdenSettles(before, lighter: true, $"the drop of {surplus.Name}")) { yield return h; }

                if (!S.State.Self.Inventory.Pack.Any(e => e.Guid == surplus.Guid))
                {
                    _takenOff.Remove(surplus.Guid); // dropped here: the shed stage has nothing left to do for it
                }
            }
        }

        Stage = "pick-up";
        foreach (var piece in pieces)
        {
            var wcid = piece.Wcid!.Value;
            if (Worn(wcid))
            {
                Log($"pick-up: {piece.Name} ({wcid}) is worn already");
                continue;
            }

            var guid = InPack(wcid).FirstOrDefault();
            if (guid == 0)
            {
                var inChest = InChest(wcid);
                if (!Verify(inChest != 0, $"pick-up: the chest does not list {piece.Name} ({wcid})"))
                {
                    continue;
                }

                int? own = S.State.Objects.TryGet(inChest, out var chestPiece) && chestPiece.Weenie.Burden is { } pieceBurden ? pieceBurden : armour.PieceOf(wcid)?.Burden;
                if (own is null)
                {
                    var ask = S.Actions.Identify(inChest, ActionSource.Host);
                    yield return ask;
                    foreach (var h in Until(() => S.TryGetAppraisal(inChest, out var seen) && seen.Burden is not null, AfterSeconds)) { yield return h; }

                    own = S.TryGetAppraisal(inChest, out var appraisal) ? appraisal.Burden : null;
                }

                if (own is not { } pieceOwn)
                {
                    Warn("pick-up", $"{piece.Name} ({wcid}) left in the chest: its burden is not read, neither from the object, the quest file nor its appraisal; the gate does not guess", inChest);
                    _leftInChest.Add(wcid);
                    continue;
                }

                bool Liftable()
                {
                    var l = LootContext.Of(S.State);
                    return l.Capacity > 0 && (long)l.Burden + pieceOwn <= (long)ArmourForm.LiftLimit * l.Capacity;
                }

                if (!Liftable())
                {
                    // The capacity trails the wield that raised it (the hauberk's Strength line arrives as an enchantment after the wield's own confirmation) and the burden trails a drop: a wait for a known later effect, logged, not a retry.
                    foreach (var h in Until(Liftable, AfterSeconds)) { yield return h; }

                    Log($"pick-up: {piece.Name} ({wcid}): waited for the capacity and the burden to settle: {(Liftable() ? "liftable now" : "still over ACE's limit")}");
                }

                var load = LootContext.Of(S.State);
                if (!Liftable() && ReferenceEquals(piece, pieces[0]))
                {
                    _armourNotLifted = true;
                    Context.Log.Warn("station", $"[live {Name} t={S.Clock.Now:0.0}] armour form: NOT taken, the robe form by name (armour-first-piece-not-lifted): the {piece.Name} ({wcid}), the set's first piece, cannot be lifted: burden {load.Burden} + {pieceOwn} is over ACE's limit of {ArmourForm.LiftLimit} x the capacity {load.Capacity} (Player_Inventory.cs:53-76); nothing of the set is taken (a part of it would leave the rest of the body bare): the quest's own pieces follow");
                    yield break;
                }

                if (!Liftable())
                {
                    Warn("pick-up", $"{piece.Name} ({wcid}) left in the chest: burden {load.Burden} + {pieceOwn} is over ACE's limit of {ArmourForm.LiftLimit} x the capacity {load.Capacity} (Player_Inventory.cs:53-76): the part it covers stays bare (named: armour-piece-not-lifted)", inChest);
                    _leftInChest.Add(wcid);
                    continue;
                }

                Log($"pick-up: {piece.Name} ({wcid}) taken: burden {load.Burden} + {pieceOwn} = {load.Burden + pieceOwn} of the capacity {load.Capacity} ({100L * (load.Burden + pieceOwn) / load.Capacity}%), under ACE's limit {(long)ArmourForm.LiftLimit * load.Capacity}; the armour form takes every piece of the set (the owner's order) and says the load");
                var mark = S.State.Chat.Total;
                var take = S.Actions.PickUp(inChest, 0, 0, ActionSource.Host);
                yield return take;
                Log($"PickUp {piece.Name} (0x{inChest:X8}, container before 0x{ContainerOf(inChest):X8}): {Describe(take)}");
                foreach (var h in Until(() => InPack(wcid).Count > 0, AfterSeconds)) { yield return h; }

                if (!Verify(InPack(wcid).Count > 0, $"pick-up: {piece.Name} ({wcid}) is not in the pack after the PickUp ({Describe(take)})", inChest, mark))
                {
                    continue;
                }

                guid = InPack(wcid).First();
                foreach (var h in BurdenSettles(load.Burden, lighter: false, $"the lift of {piece.Name}")) { yield return h; } // the next piece's gate reads the server's burden: it must hold this one first
            }

            Stage = "wear";
            foreach (var h in WearPiece(piece, guid, _questWcids)) { yield return h; }

            Stage = "pick-up";
            if (Worn(wcid))
            {
                LogLoad($"{piece.Name} worn");
            }
        }
    }

    // ---------------------------------------------------------------------------------------------- the checks and the acts

    /// <summary>A check (<see cref="ScriptLeg.Check"/>) that also writes the failure at WARN, with the step and the state read: the item's guid and weenie class, its container, its wield location and coverage, and what the server said since <paramref name="mark"/> (live round 2: only WARN lines reached the log).</summary>
    private bool Verify(bool ok, string what, uint guid = 0, long mark = -1)
    {
        if (!ok)
        {
            Warn(Stage, what, guid, mark);
        }

        return Check(ok, what);
    }

    /// <summary>Writes a WARN line (never silent) with the step, the text, the item's state and the server's replies since <paramref name="mark"/>.</summary>
    private void Warn(string step, string what, uint guid = 0, long mark = -1) =>
        Context.Log.Warn("station", $"[live {Name} t={S.Clock.Now:0.0}] PROBLEM step={step}: {what}; {ItemState(guid)}{ServerText(mark)}");

    /// <summary>The state read of an item for a problem line: guid, weenie class, container, wield location, coverage and whether the pack lists it.</summary>
    private string ItemState(uint guid)
    {
        if (guid == 0)
        {
            return "item=none";
        }

        if (!S.State.Objects.TryGet(guid, out var o))
        {
            return $"item=0x{guid:X8} not in the object table";
        }

        return string.Create(CultureInfo.InvariantCulture, $"item=0x{guid:X8} wcid={o.Weenie.WeenieClassId} name={o.Name} container=0x{o.ContainerId ?? 0:X8} wielder=0x{o.WielderId ?? 0:X8} location=0x{LocationOf(guid):X8} valid=0x{o.Weenie.ValidLocations ?? 0:X8} coverage=0x{PathwardenWear.CoverageOf(S.State, guid):X} in_pack={InPack(o.Weenie.WeenieClassId).Contains(guid)}");
    }

    private uint LocationOf(uint guid) => S.State.Self.Inventory.TryGetEquipped(guid, out var e) ? e.Location : 0;

    private uint ContainerOf(uint guid) => S.State.Objects.TryGet(guid, out var o) ? o.ContainerId ?? 0 : 0;

    private bool IsEquipped(uint guid) => LocationOf(guid) != 0;

    /// <summary>What the server said since <paramref name="mark"/> (<see cref="ChatLog.Total"/> before the act): its transient strings, weenie errors, system text and popups, as <c>; server said: ...</c>, or empty.</summary>
    private string ServerText(long mark)
    {
        if (mark < 0)
        {
            return string.Empty;
        }

        var chat = S.State.Chat;
        var fresh = (int)Math.Min(chat.Count, chat.Total - mark);
        var lines = new List<string>();
        for (var i = chat.Count - fresh; i < chat.Count; i++)
        {
            var line = chat[i];
            if (line.Kind is ChatKind.Transient or ChatKind.WeenieError or ChatKind.System or ChatKind.Popup)
            {
                lines.Add($"{line.Kind}{(line.ChatType == 0 ? string.Empty : $" 0x{line.ChatType:X}")} \"{line.Text}\"");
            }
        }

        return lines.Count == 0 ? "; server said: nothing" : "; server said: " + string.Join(" | ", lines);
    }

    /// <summary>Everything worn now, with the weenie class and coverage, for <see cref="PathwardenWear.Plan"/>.</summary>
    private List<PathwardenWear.WornPiece> WornNow()
    {
        var list = new List<PathwardenWear.WornPiece>();
        foreach (var e in S.State.Self.Inventory.Equipped)
        {
            if (S.State.Objects.TryGet(e.Guid, out var o))
            {
                list.Add(new PathwardenWear.WornPiece(e.Guid, o.Weenie.WeenieClassId, o.Name, e.Location, e.Priority != 0 ? e.Priority : o.Weenie.Priority ?? 0));
            }
            else
            {
                list.Add(new PathwardenWear.WornPiece(e.Guid, 0, "?", e.Location, e.Priority));
            }
        }

        return list;
    }

    private bool ChestOpen => S.State.Containers.Viewed is { } v && v.Guid == _chest && _chest != 0;

    /// <summary>The walk to <paramref name="place"/> and the object of <paramref name="wcid"/> beside it: check the object is in the table within reach, act (travel), check it is.</summary>
    private IEnumerable<ActionHandle?> Approach(QuestPlace place, uint wcid, string what, Action<uint> found)
    {
        var at = place.Point ?? throw new InvalidOperationException($"TODO(m6-pathwarden-place): the quest file gives no point for {what}");
        var guid = WorldObject(wcid, place.Cell, at, 40);
        if (guid != 0 && Distance(guid) <= UseRange + 1)
        {
            found(guid);
            Log($"{what}: in reach already ({Distance(guid):0.0} m)");
            yield break;
        }

        foreach (var h in TravelTo(new TravelRequest(Destination(place.Name, place.Cell, at), TravelPurpose.Cell, guid, UseRange), what)) { yield return h; }

        foreach (var h in Until(() => WorldObject(wcid, place.Cell, at, 40) != 0, NpcSeconds)) { yield return h; }

        guid = WorldObject(wcid, place.Cell, at, 40);
        Require(Verify(guid != 0, $"{what} ({wcid}) is not in the object table within 40 m of {place}"), $"{what} not in view");
        Verify(Distance(guid) <= UseRange + 3, $"{what} is {Distance(guid):0.0} m away after the walk (a Use walks the rest, ACE's MoveTo)");
        found(guid);
    }

    /// <summary>The act and its stamp check, with the one named recovery: re-approach and act once more.</summary>
    private IEnumerable<ActionHandle?> ActAndStamp(string what, Func<ActionHandle> act, string stamp, Func<IEnumerable<ActionHandle?>> reApproach)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (attempt == 1)
            {
                _recoveries++;
                Log($"recovery={what}-again: the stamp {stamp} is not held {AfterSeconds:0} s after the act; walking up again and acting once more");
                foreach (var h in reApproach()) { yield return h; }
            }

            var handle = act();
            yield return handle;
            Log($"{what}: {Describe(handle)}");
            var waited = 0.0;
            QuestWait last;
            do
            {
                last = Has(stamp);
                foreach (var h in last.Wait(this)) { yield return h; }

                if (last.Held)
                {
                    break;
                }

                foreach (var h in Pause(1)) { yield return h; }

                waited += 1;
            }
            while (waited < AfterSeconds);

            if (last.Held)
            {
                Log($"{what}: the stamp {stamp} is held");
                yield break;
            }
        }

        Verify(false, $"{what}: the stamp {stamp} is not held after the act and the recovery");
        throw new InvalidOperationException($"{what}: stamp {stamp} never held");
    }

    private IEnumerable<ActionHandle?> OpenChest()
    {
        var open = S.Actions.OpenContainer(_chest, ActionSource.Host);
        yield return open;
        Log("OpenContainer the chest: " + Describe(open));
    }

    private IEnumerable<ActionHandle?> CloseChest()
    {
        if (!ChestOpen)
        {
            yield break;
        }

        var close = S.Actions.CloseContainer(_chest, ActionSource.Host);
        yield return close;
        Log("CloseContainer the chest: " + Describe(close));
    }

    private QuestWait Has(string stamp) => new(S.Tools.HasQuest(stamp, ActionSource.Host));

    private uint InChest(uint wcid)
    {
        foreach (var o in S.State.Objects.All)
        {
            if (o.Weenie.WeenieClassId == wcid && o.ContainerId == _chest && _chest != 0)
            {
                return o.Guid;
            }
        }

        return 0;
    }

    private bool Worn(uint wcid)
    {
        foreach (var guid in HeldOf(wcid))
        {
            if (S.State.Self.Inventory.TryGetEquipped(guid, out var e) && e.Location != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The nearest world object (not held, not in a container) of that weenie class within <paramref name="metres"/> of <paramref name="at"/> in that landblock, or 0.</summary>
    private uint WorldObject(uint wcid, uint cell, Vector3 at, double metres)
    {
        uint best = 0;
        var bestDistance = double.MaxValue;
        foreach (var o in S.State.Objects.All)
        {
            if (o.Weenie.WeenieClassId != wcid || o.Position is not { } p || p.CellId >> 16 != cell >> 16 || (o.ContainerId is { } c && c != 0))
            {
                continue;
            }

            var d = Vector3.Distance(p.Origin, at);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = o.Guid;
            }
        }

        return bestDistance <= metres ? best : 0;
    }

    private double Distance(uint guid) =>
        S.State.Self.Position is { } here && S.State.Objects.TryGet(guid, out var o) && o.Position is { } p && p.CellId >> 16 == here.CellId >> 16 ? Vector3.Distance(p.Origin, here.Origin) : double.MaxValue;

    /// <summary>A <c>quest has</c> request in flight: waited on from the script, read after.</summary>
    private sealed class QuestWait(ToolHandle<QuestResult> handle)
    {
        public bool Held => handle.Result is { Held: true };

        public IEnumerable<ActionHandle?> Wait(PathwardenRun run)
        {
            yield return handle;
            if (handle.Outcome is not { Kind: ActionOutcomeKind.Confirmed })
            {
                run.Verify(false, "quest has: " + (handle.Outcome?.Kind.ToString() ?? "unsettled") + (handle.Outcome?.Detail is null ? string.Empty : ": " + handle.Outcome.Value.Detail));
            }
        }
    }
}

/// <summary>
/// The Pathwarden wear rule (M6 item 0 fix slice F1, live round 2: worn 2 of 4). ACE refuses to wield a clothing or armour piece while any worn piece's <c>ClothingPriority</c> (<c>CoverageMask</c>) overlaps its own
/// (<c>WieldedLocationIsAvailable</c> through <c>GetEquippedItems</c> and <c>GetEquippedClothingArmor</c>, Creature_Equipment.cs:50-58, 93-101; the refusal is "You must remove your X to wield Y", Player_Inventory.cs:1801-1809, @95ef426d),
/// and a jewellery piece while another holds its location. A new character wears a starter shirt, pants and shoes (PlayerFactory.cs:109-130), and the pinned world SQL gives the pieces their parts: the Pathwarden Robe 40439 ClothingPriority 0x17F00
/// (outerwear, head and FEET), the Gauntlets 33606 0x8000 (hands), the Sollerets 33605 0x10000 (feet), the Trinket 41513 none (ValidLocations 0x04000000, TrinketOne). So the starter shoes block the robe and the sollerets, and the robe blocks the sollerets
/// for good (the same rule the mage's gear pass reads, <c>SelfEquipJudge.PickFootwear</c>, M5 7 D129). The plan takes off a worn piece in the way that is not one of the quest's pieces, and names a quest piece in the way as the blocker.
/// </summary>
public static class PathwardenWear
{
    /// <summary>One worn piece as the plan reads it.</summary>
    /// <param name="Guid">Its object id.</param>
    /// <param name="Wcid">Its weenie class (0 when the object table does not know it).</param>
    /// <param name="Name">Its name.</param>
    /// <param name="Location">The location it is worn at (the equipped entry's).</param>
    /// <param name="Coverage">Its ClothingPriority (CoverageMask), 0 for jewellery.</param>
    public sealed record WornPiece(uint Guid, uint Wcid, string Name, uint Location, uint Coverage);

    /// <summary>What to do before wearing a piece.</summary>
    /// <param name="Coverage">The piece's own ClothingPriority (0 for jewellery).</param>
    /// <param name="TakeOff">Worn pieces in the way that are not the quest's: to be taken off into the pack first.</param>
    /// <param name="Blocker">A worn piece of the quest's own in the way (the piece is then left in the pack), or null.</param>
    public sealed record WearPlan(uint Coverage, IReadOnlyList<WornPiece> TakeOff, WornPiece? Blocker);

    /// <summary>A piece she holds, worn or in the main pack, as the shed reads it.</summary>
    /// <param name="Guid">Its object id.</param>
    /// <param name="Wcid">Its weenie class.</param>
    /// <param name="Name">Its name.</param>
    /// <param name="Coverage">Its ClothingPriority (CoverageMask), 0 for a piece that covers no part.</param>
    /// <param name="Burden">Its burden, 0 when not read.</param>
    /// <param name="Worn">True when it is worn; false: it lies in the main pack.</param>
    public sealed record HeldPiece(uint Guid, uint Wcid, string Name, uint Coverage, int Burden, bool Worn);

    /// <summary>
    /// M6 item 14 (live 2026-11-11 23:40 UTC: the armour form was not taken, 40 burden over ACE's limit): the pieces to drop before the armour set's first piece is lifted: every piece she holds, WORN OR IN THE PACK, that is none of the
    /// quest's and whose coverage overlaps the set's. The fresh climber wears the Leather Boots (420) and carries the Academy Coat (600) in her pack, not on her: a shed of the worn pieces alone dropped the boots and left the coat, so she
    /// stood at 2,040 where the lift needed 2,000 or less. Neither can be worn beside the set (ACE Creature_Equipment.cs:50-58, 93-101 @95ef426d), so both are surplus the moment the set is chosen.
    /// </summary>
    /// <param name="held">What she wears and what lies in her main pack.</param>
    /// <param name="coverage">The armour set's coverage.</param>
    /// <param name="questWcids">The quest's own pieces: never shed.</param>
    public static List<HeldPiece> ShedFirst(IEnumerable<HeldPiece> held, uint coverage, ISet<uint> questWcids)
    {
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(questWcids);
        return [.. held.Where(h => (h.Coverage & coverage) != 0 && !questWcids.Contains(h.Wcid)).OrderByDescending(h => h.Worn)]; // the worn ones first: each is taken off, then dropped
    }

    /// <summary>The piece's ClothingPriority from the object model (0 when the header carries none or the object is unknown).</summary>
    public static uint CoverageOf(GameState state, uint guid)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Objects.TryGet(guid, out var o) ? o.Weenie.Priority ?? 0 : 0;
    }

    /// <summary>
    /// The plan to wear <paramref name="item"/> (coverage <paramref name="coverage"/>, valid locations <paramref name="valid"/>) with <paramref name="worn"/> on: a worn piece whose coverage overlaps (for a piece with coverage), or
    /// whose location overlaps the valid locations (for a piece with none), is in the way; one whose weenie class is in <paramref name="questWcids"/> is the blocker, the others are taken off.
    /// </summary>
    public static WearPlan Plan(uint item, IReadOnlyCollection<WornPiece> worn, uint coverage, uint valid, ISet<uint> questWcids)
    {
        ArgumentNullException.ThrowIfNull(worn);
        ArgumentNullException.ThrowIfNull(questWcids);
        var takeOff = new List<WornPiece>();
        WornPiece? blocker = null;
        foreach (var w in worn)
        {
            if (w.Guid == item)
            {
                continue;
            }

            var inTheWay = coverage != 0 ? (w.Coverage & coverage) != 0 : (w.Location & valid) != 0;
            if (!inTheWay)
            {
                continue;
            }

            if (questWcids.Contains(w.Wcid))
            {
                blocker ??= w;
            }
            else
            {
                takeOff.Add(w);
            }
        }

        return new WearPlan(coverage, takeOff, blocker);
    }
}
