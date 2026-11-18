using System.Globalization;
using System.Runtime.CompilerServices;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Data.Magic;
using DatReaderWriter.Enums;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>M5 7 P7j (D143, D141): why a spell of the ground's learn list was not bought or not learned on a trip.</summary>
public enum LearnReason
{
    /// <summary>Nothing stands in the way.</summary>
    None,

    /// <summary>The spellbook holds the spell: the scroll is never bought again.</summary>
    Known,

    /// <summary>No PlayerDescription has filled the spellbook yet: nothing is bought on an assumption.</summary>
    SpellbookUnread,

    /// <summary>The spell's school is not trained (ACE: "You are not trained in ...!").</summary>
    Untrained,

    /// <summary>The school's current skill is under the spell's power less ACE's margin of 50 (ACE: "You are not skilled enough in ... to learn this spell.").</summary>
    SkillShort,

    /// <summary>The scroll is not in the pack when it is to be read (not bought: the coin, the pack or the vendor said no).</summary>
    NoScroll,

    /// <summary>The use was refused (a UseDone with an error, or no answer).</summary>
    UseRefused,

    /// <summary>The use was confirmed but the spellbook did not gain the spell within the wait.</summary>
    NotLearned,

    /// <summary>A refusal earlier in the session: not tried again until the session ends (no loop).</summary>
    RefusedBefore,

    /// <summary>M6 4 t4a: the trip by the Town Network to the town that sells it failed by name (a hop, the walk to the scrivener, the buy): not tried again this session (no loop).</summary>
    TownTrip,
}

/// <summary>
/// M5 7 P7j (D143, the owner: learned by play): the resupply's learn step. For each spell of the ground's learn list (<see cref="HuntingGround.Learn"/>) the spellbook lacks, the trip buys one scroll at the profile's vendor on its walk
/// (before: the spellbook is read, the spell is not in it, the school meets ACE's requirement; the buy's own checks then hold the coin and a free slot) and reads it after the walk (before: the scroll is in the pack and the skill still
/// meets the requirement; act: Use; after: the spellbook holds the spell from the server's MagicUpdateSpell, and ACE's "You learn the ... spell." line). A spell the spellbook holds is never bought again; a refusal is logged with its
/// <see cref="LearnReason"/> and remembered in <see cref="SaleMemory.LearnRefused"/>, so no trip of the session tries it again (no loop).
/// </summary>
/// <remarks>
/// ACE (@95ef426d): <c>Scroll.ActOnUse</c> (Scroll.cs 45-127) sets the player to peace, plays the reading motion, waits 1 s, then answers "You already know that spell!" (86-90), or, when <c>Player.CanReadScroll</c> (Player_Skills.cs
/// 458-471) fails, "You are not trained in X!" or "You are not skilled enough in X to learn this spell." (96-105), else consumes the scroll and calls <c>LearnSpellWithNetworking</c> (Player_Spells.cs 48-78: GameEvent 0x02C1
/// MagicUpdateSpell and "You learn the X spell."), then "The scroll is destroyed." (112). The UseDone follows the whole motion (Player_Use.cs 223-238), with no error either way: the chat line and the spellbook are the proof.
/// </remarks>
public sealed partial class ResupplyRun
{
    /// <summary>ACE's margin under a scroll spell's power that the school's current skill must reach (<c>magicSkillCheckMargin</c>, Player_Skills.cs 456 @95ef426d).</summary>
    public const uint ScrollSkillMargin = 50;

    /// <summary>WeenieError 0x001D, ACE's "You're too busy!" (<c>Scroll.ActOnUse</c>, Scroll.cs 58-62 @95ef426d).</summary>
    private const uint YoureTooBusy = 0x001D;

    /// <summary>The longest wait for the spellbook to gain the spell after the use (ACE's reading motion, the 1 s read and the ready motion take about 4 s).</summary>
    public const double LearnSeconds = 15;

    /// <summary>M6 7 T: the pause after a "too busy" answer to a scroll read, before the one re-send.</summary>
    public const double BusyPauseSeconds = 2;

    private readonly List<LearnLine> _toRead = [];

    /// <summary>M6 4 t4a: the plan's lines sold only off home, each with its trip by the Town Network and the catalog's price (read on that trip, not on the home walk).</summary>
    private readonly List<(LearnLine Learn, TownTripPlan Plan, int Price)> _townLearns = [];

    /// <summary>The spells learned on this trip.</summary>
    public int Learned { get; private set; }

    /// <summary>The scrolls bought for the learn list on this trip.</summary>
    public int ScrollsBought { get; private set; }

    /// <summary>The learn list's refusals on this trip, one per spell, as "spell:reason".</summary>
    public List<string> LearnRefusals { get; } = [];

    /// <summary>The learn step's line for the trip's end (the lead's grep: <c>resupply: learn:</c>).</summary>
    public string LearnFields => string.Create(CultureInfo.InvariantCulture, $"learned={Learned} scrolls_bought={ScrollsBought} learn_refused={(LearnRefusals.Count == 0 ? "none" : string.Join('+', LearnRefusals))}");

    /// <summary>
    /// ACE's <c>Player.CanReadScroll</c> (Player_Skills.cs 458-471 @95ef426d) over the read model: a spell of power under 50 or from 300 is read by anyone; else the school is trained and its current skill (with the buffs, as ACE's
    /// <c>Current</c>) is at least the power less <see cref="ScrollSkillMargin"/>. <paramref name="why"/> names the numbers.
    /// </summary>
    /// <param name="q">The player's qualities.</param>
    /// <param name="school">The spell's school.</param>
    /// <param name="power">The spell's power.</param>
    /// <param name="why">The numbers, or the reason the skill could not be read.</param>
    public static LearnReason CanRead(PlayerQualities q, MagicSchool school, uint power, out string why)
    {
        ArgumentNullException.ThrowIfNull(q);
        if (power < 50 || power >= 300)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"power {power} needs no skill (ACE: under 50 or from 300)");
            return LearnReason.None;
        }

        if (!GearCasts.TryCurrentSkill(q, GearCasts.SkillOf(school), out var skill, out var noSkill))
        {
            why = noSkill;
            return LearnReason.Untrained;
        }

        return CanRead(skill, school, power, out why);
    }

    /// <summary>
    /// M6 14 T: the same rule on a skill the caller names: the learn plan asks it of the standing skill (<see cref="ScrivenerLearnPlan.Standing"/>), so what is due does not turn on the enchantments on her at the moment of the plan's read; the
    /// read itself still asks ACE's own question of the skill she has then (<see cref="CanRead(PlayerQualities, MagicSchool, uint, out string)"/>).
    /// </summary>
    /// <param name="skill">The trained skill's value.</param>
    /// <param name="school">The spell's school.</param>
    /// <param name="power">The spell's power.</param>
    /// <param name="why">The numbers.</param>
    public static LearnReason CanRead(uint skill, MagicSchool school, uint power, out string why)
    {
        if (power < 50 || power >= 300)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"power {power} needs no skill (ACE: under 50 or from 300)");
            return LearnReason.None;
        }

        var need = power - ScrollSkillMargin;
        why = string.Create(CultureInfo.InvariantCulture, $"{school} skill {skill}, power {power} needs {need}");
        return skill >= need ? LearnReason.None : LearnReason.SkillShort;
    }

    /// <summary>
    /// The learn list's scrolls to buy on this trip (each a one-ware buy at its vendor), every decision logged; the spells to read after the walk are kept. M6 item 1 c (D41, D40): the ground's own <c>learn</c> lines first, then the spell
    /// plan's (<see cref="ScrivenerLearnPlan"/>: the rung's bolt line and the fight's debuff lines, the target level by the plan's chance on the current skill, only the level the book lacks); a target no scrivener of the home town sells goes
    /// by the Town Network (M6 4 t4a, closes <c>TODO(m6-scrivener-town-network)</c>): <see cref="TownNetworkTrip.Choose"/> picks the town, and the trip after the home walk (<see cref="TownLearnTrip"/>) hops there, buys, reads and comes home;
    /// no town admitted is a loud stop logged once a session with the towns and the spell.
    /// </summary>
    private List<ResupplyBuy> PlanLearns(HuntingGround ground)
    {
        var buys = new List<ResupplyBuy>();
        _toRead.Clear();
        _townLearns.Clear();
        _scrollKits.Clear();
        _kitLearns.Clear();
        _everySpellIds.Clear();
        _everySpellKeep = null;
        PlanFoci(ground, buys); // M6 7 AU: first in the list, so the plan's order puts it right after what the Burrow's gate names
        foreach (var learn in ground.Learn)
        {
            PlanLearn(ground, learn, buys);
        }

        foreach (var learn in PlanLines(ground))
        {
            if (!ground.Learn.Any(l => l.SpellId == learn.SpellId))
            {
                PlanLearn(ground, learn, buys);
            }
        }

        PlanEverySpell(ground, buys); // M6 14 (the owner, 2026-11-16): every spell she can cast, beyond the plan, on the trip she makes anyway
        PlanHeldScrolls(ground);
        return buys;
    }

    /// <summary>
    /// M6 7 AU (the owner: acquire the foci of the other schools): the Foci of Enchantment, then of Artifice, one a trip by name (<see cref="FociPlan.Next"/>: the Burrow gate's conditions met, the school trained, not held, sold at home). Check before: the purse (coin and
    /// note faces, <see cref="FloorPurse"/>) pays the price and the load stays under the burden line with its burden; a short purse or a full load names the fallback and buys nothing (the full formula's components at two passes' keep, AT's stock rule). The buy is
    /// <see cref="BuyLine"/>, which changes a note at the scrivener when the coin is short; its check after is the focus in the pack, logged.
    /// </summary>
    private void PlanFoci(HuntingGround ground, List<ResupplyBuy> buys)
    {
        var q = S.State.Self.Qualities;
        if (!S.State.Self.Spellbook.IsKnown || !q.IsKnown || !CharacterPolicy.For(S.State.Self.Name).StarterFoci)
        {
            return; // the climber's plan only
        }

        var need = FociPlan.Next(ground, LadderLoader.WarMage, ScrivenerCatalog.Default, q, RungGateRule.BookKnows(BuffProfiles.Climber, S.State.Self.Spellbook.Knows), w => GearInventory.CountOf(S.State, [], w), _memory.FociRefused, out var why, BuffBotCover.For(S.State).Covering); // M6 7 AX: a focus is a type-2 entry beside the side packs, which ScrollsHeld does not read
        if (need is null)
        {
            Log(Text($"foci: not due: {why}"));
            return;
        }

        var purse = FloorPurse();
        var load = Fighter.LootContext.Of(S.State);
        var burdenLine = ground.Loot is { LoadLinePercent: > 0 } loot ? loot.LoadLinePercent : ground.Supplies.BurdenTripPercent;
        if (purse < need.Price)
        {
            Log(Text($"foci: {need.Focus.Name} waits: the purse {purse} does not pay {need.Price}; fallback=full-formula-components-two-passes"));
            return;
        }

        if (load.Capacity > 0 && (long)(load.Burden + need.Focus.Burden) * 100 > (long)load.Capacity * burdenLine)
        {
            Log(Text($"foci: {need.Focus.Name} waits: the load {load.Burden} and the focus' {need.Focus.Burden} are over {burdenLine}% of the capacity {load.Capacity}; fallback=full-formula-components-two-passes"));
            return;
        }

        buys.Add(new ResupplyBuy(new StockLine(need.Focus.Wcid, need.Focus.Name, 1, 1, 1), need.Vendor, 0, 1));
        Log(Text($"foci: check before: {need.Why}; the purse {purse} pays {need.Price}, the load {load.Burden} + {need.Focus.Burden} is under {burdenLine}% of {load.Capacity}: buy it at {need.Vendor.Name} (0x{need.Vendor.Guid:X8}), a note is changed there when the coin is short"));
    }

    /// <summary>
    /// M6 item 1 e (D4, the VII rule; closes the learn half of <c>TODO(m6-scroll-read)</c> and <c>TODO(m6-vii-loot-rule)</c>): a scroll the pack holds that <see cref="ScrollKeepRule"/> keeps (looted, so no learn line named it: the plan's next level of the rung's bolt or debuff line that the book lacks and ACE lets the character read, a VII always) is read after the walk like a bought one. Check: the scroll is held and the rule keeps it on the live book and skills; act and after-check:
    /// <see cref="ReadScrolls"/>. A scroll the rule does not keep is logged and left; a spell refused earlier this session is not tried again (no loop).
    /// </summary>
    private void PlanHeldScrolls(HuntingGround ground)
    {
        var q = S.State.Self.Qualities;
        var book = S.State.Self.Spellbook;
        if (!book.IsKnown || ScrivenerLearnPlan.RungOf(LadderLoader.WarMage, ground.Key, q.Level) is not { } rung)
        {
            return; // PlanLines logged why
        }

        var lines = ScrollKeepRule.PlanLines(rung, Context.Spells, ScrivenerCatalog.Default, BuffBotCover.For(S.State).Covering); // M6 item 14 S3: a held scroll of a line the buff bot casts is left, by name
        foreach (var guid in PackAndSidePacks())
        {
            if (!S.State.Objects.TryGet(guid, out var scroll) || (scroll.ItemType & WritableType) == 0)
            {
                continue;
            }

            var verdict = ScrollKeepRule.Evaluate(scroll, lines, book, q, LadderLoader.WarMage.PlanCastChance);
            if (!verdict.Keep || verdict.Level is not { } level)
            {
                Log(Text($"learn: held scroll {scroll.Name} (0x{scroll.Guid:X8}, spell {scroll.Weenie.SpellId?.ToString(CultureInfo.InvariantCulture) ?? "none"}): left in the pack, {verdict.Kind}: {verdict.Reason}"));
                continue;
            }

            if (_toRead.Any(l => l.SpellId == level.SpellId))
            {
                continue; // the plan or the list named it already: one read
            }

            if (_memory.LearnRefused.TryGetValue(level.SpellId, out var before))
            {
                Log(Text($"learn: held scroll {scroll.Name} (0x{scroll.Guid:X8}): {level.Name} ({level.SpellId}) was refused earlier this session ({Token(before)}): not tried again"));
                continue;
            }

            _toRead.Add(new LearnLine(level.SpellId, level.Name, scroll.Weenie.WeenieClassId, 0));
            Log(Text($"learn: held scroll {scroll.Name} (0x{scroll.Guid:X8}, weenie {scroll.Weenie.WeenieClassId}): {verdict.Reason}; no buy, read after the walk"));
        }
    }

    /// <summary>
    /// M6 1 fix (1-R2): the guids of the main pack's ordinary items, then those in every side pack (a kept scroll can be parked in a review pack by item 2's layout, and ACE reads a scroll from any pack). The main pack comes first, so
    /// a read finds the main pack's copy first.
    /// </summary>
    private IEnumerable<uint> PackAndSidePacks()
    {
        var inventory = S.State.Self.Inventory;
        foreach (var entry in inventory.Pack)
        {
            if (entry.ContainerType == 0)
            {
                yield return entry.Guid;
            }
        }

        foreach (var side in inventory.SidePacks)
        {
            foreach (var entry in inventory.ItemsIn(side.Guid))
            {
                if (entry.ContainerType == 0)
                {
                    yield return entry.Guid;
                }
            }
        }
    }

    /// <summary>The guids of the scrolls of weenie class <paramref name="wcid"/> the character holds in the main pack or any side pack (M6 1 fix, 1-R2).</summary>
    private List<uint> ScrollsHeld(uint wcid)
    {
        var list = new List<uint>();
        foreach (var guid in PackAndSidePacks())
        {
            if (S.State.Objects.TryGet(guid, out var o) && o.Weenie.WeenieClassId == wcid)
            {
                list.Add(guid);
            }
        }

        return list;
    }

    /// <summary>One learn line: the before-check, a scroll held already (read, not bought), else the buy at its vendor.</summary>
    /// <param name="ground">The trip's ground.</param>
    /// <param name="learn">The line.</param>
    /// <param name="buys">The trip's buys.</param>
    /// <param name="kit">M6 14 (the every-spell rule): false for a line no pass or fight casts (the rest of the shop): no components kit is planned beside it (two passes of each of a hundred such spells would buy scarabs no cast burns); the line is named.</param>
    private void PlanLearn(HuntingGround ground, LearnLine learn, List<ResupplyBuy> buys, bool kit = true)
    {
        var reason = LearnBefore(learn, out var why);
        if (reason != LearnReason.None)
        {
            Log(Text($"learn: {learn.Name} ({learn.SpellId}): {Token(reason)}: {why}"));
            return;
        }

        _toRead.Add(learn);
        var held = ScrollsHeld(learn.ScrollWcid).Count;
        if (held > 0)
        {
            Log(Text($"learn: {learn.Name} ({learn.SpellId}): not in the spellbook, {why}; a scroll (weenie {learn.ScrollWcid}) is in the pack already: no buy, read after the walk"));
            return;
        }

        var vendor = ground.VendorOf(learn.VendorWcid);
        var ware = vendor?.Sells.FirstOrDefault(w => w.Wcid == learn.ScrollWcid);
        if (vendor is null || ware is null)
        {
            _toRead.Remove(learn);
            Log(Text($"learn: {learn.Name} ({learn.SpellId}): TODO(m5-learn-vendor): no vendor of weenie {learn.VendorWcid} in {ground.Home.Town} (the profile's or the scrivener catalog's) lists the scroll {learn.ScrollWcid}; nothing is bought"));
            return;
        }

        ScrollSupplyPlan? supply = null;
        if (kit)
        {
            Supply(ground, learn, buys, out supply); // the owner's rule of 2026-11-09 (D116): the check never stops the buy
        }
        else
        {
            Log(Text($"learn: {learn.Name} ({learn.SpellId}): no components kit beside it (the every-spell rule's rest: no pass or fight casts it; its components are the stock's when a rule does)"));
        }

        buys.Add(new ResupplyBuy(new StockLine(ware.Wcid, ware.Name, 1, 1, 1), vendor, 0, 1) { Components = supply?.Components });
        var walk = ScrivenerCatalog.Default.ScrivenerOf(vendor.Wcid)?.Places.FirstOrDefault(p => p.Guid == vendor.Guid) is { CellFromPoint: true } place
            ? Text($"; his row's cell 0x{place.RowCell:X8} has the low word 0, so the walk goes to the outdoor cell 0x{place.Cell:X8} under his point and reads him live by guid before the last metres")
            : string.Empty;
        Log(Text($"learn: {learn.Name} ({learn.SpellId}): not in the spellbook, {why}: buy its scroll (weenie {ware.Wcid}, {ware.Price} pyreals) at {vendor.Name} (0x{vendor.Guid:X8}) on the walk, read it after{walk}"));
    }

    /// <summary>The spell plan's lines for this trip (<see cref="ScrivenerLearnPlan"/>), each decision logged; an off-home stop logged once a session (<see cref="OffHomeLogged"/>).</summary>
    private List<LearnLine> PlanLines(HuntingGround ground)
    {
        var lines = new List<LearnLine>();
        var q = S.State.Self.Qualities;
        var ladder = LadderLoader.WarMage;
        if (ScrivenerLearnPlan.RungOf(ladder, ground.Key, q.Level) is not { } rung)
        {
            Log(Text($"learn: plan: no rung of the ladder '{ladder.Key}' names the ground {ground.Key}: the ground's own list only"));
            return lines;
        }

        if (!S.State.Self.Spellbook.IsKnown)
        {
            Log("learn: plan: the spellbook is not read yet (no PlayerDescription): nothing is planned on an assumption");
            return lines;
        }

        var logged = OffHomeLogged.GetOrCreateValue(_memory);
        if (ScrivenerLearnPlan.IncomingElements(ground).Count == 0)
        {
            Log(Text($"learn: plan: rung {rung.Key}: TODO(m6-outskirts-damage-types): no target of {ground.Key} names its damage types: Armor Self only, no protection line"));
        }

        foreach (var element in ScrivenerLearnPlan.UncoveredElements(ground, ScrivenerCatalog.Default))
        {
            Log(Text($"learn: plan: rung {rung.Key}: TODO(m6-protection-line-other-types): the targets of {ground.Key} deal {element} damage and the catalog and the climber's buff profile hold no protection line of it"));
        }

        var cover = BuffBotCover.For(S.State);
        var covered = 0;
        var casters = CasterGain.Of(S.State, rung.Element is { } rungElement && SelfEquipJudge.ElementBits.TryGetValue(rungElement, out var bits) ? bits : 0); // M6 14 (m6-caster): the caster auras pay only on a caster she holds
        _planNeeds = [.. ScrivenerLearnPlan.Needs(ground, rung, ladder.PlanCastChance, q, S.State.Self.Spellbook, ScrivenerCatalog.Default, ladder, cover.Covering, Context.Facts.LearnTravelLines, casters)]; // M6 14: the every-spell step defers to these
        foreach (var need in _planNeeds)
        {
            if (need.Kind == LearnNeedKind.BotCovers)
            {
                // M6 item 14 S3: a line the buff bot casts calls no trip of the plan: named once a session (and again when the cover comes back on), then counted; M6 14 own buffs: the every-spell step buys her own level of it
                covered++;
                if (cover.FirstNote(need.Line.Name))
                {
                    Log(Text($"learn: plan: rung {rung.Key} level {q.Level}: {LearnNeedToken(need.Kind)}: {need.Why}"));
                }

                continue;
            }

            var name = need.Spell is { } sp ? Text($"{sp.Name} ({sp.SpellId})") : need.Line.Name;
            if (need.Kind == LearnNeedKind.Due && need.Spell is { } spell && spell.Sellers.FirstOrDefault(s => ground.VendorOf(s.VendorWcid) is not null) is { } seller)
            {
                Log(Text($"learn: plan: rung {rung.Key} level {q.Level}: {name}: due: {need.Why} (create-list row {seller.CreateListId} of {seller.VendorWcid})"));
                lines.Add(new LearnLine(spell.SpellId, spell.Name, spell.ScrollWcid, seller.VendorWcid));
            }
            else if (need.Kind == LearnNeedKind.OffHome && need.Spell is { } far)
            {
                PlanTownLearn(ground, rung, q.Level, need, far, name, logged);
            }
            else
            {
                Log(Text($"learn: plan: rung {rung.Key} level {q.Level}: {name}: {LearnNeedToken(need.Kind)}: {need.Why}"));
            }
        }

        if (covered > 0)
        {
            Log(Text($"learn: plan: rung {rung.Key} level {q.Level}: {covered} line(s) are the buff bot's ({cover.Why}): the plan calls no trip for them; the every-spell step buys her own level of each ({EverySpellPlan.CoveredNote}); each was named once"));
        }

        return lines;
    }

    /// <summary>
    /// M6 7 X: a scroll the trip's budget cannot pay (<see cref="ResupplyPlanner.BuyList(HuntingGround, LogisticsCounters, long, IReadOnlyList{ResupplyBuy}, List{ResupplyBuy}, IReadOnlyList{ResupplyBuy}, uint?)"/>) waits: it is not read, and its learn trigger is held until the
    /// coin pays it within the budget or the level rises, so the counters do not call the same trip again at once (a trip that bought another scroll holds nothing by itself, M6 1 d). Logged with the numbers.
    /// </summary>
    private void WaitLearn(HuntingGround ground, ResupplyBuy scroll, LogisticsCounters counters)
    {
        var learn = _toRead.FirstOrDefault(l => l.ScrollWcid == scroll.Line.Wcid);
        if (learn is null)
        {
            return;
        }

        _toRead.Remove(learn);
        var price = scroll.Vendor.Sells.FirstOrDefault(w => w.Wcid == scroll.Line.Wcid)?.Price ?? 0;
        if (_everySpellIds.Remove(learn.SpellId))
        {
            // M6 14 (the every-spell rule): its scroll calls no trip and holds no coin back from the leather set; the next trip's plan asks again
            Log(Text($"every_spell: {learn.Name} ({learn.SpellId}): waits: its scroll costs {price} and this trip's budget ({ground.Supplies.Purse.TripSpendPercent}% of the purse {FloorPurse()}, after the stock and the scrolls before it) cannot pay it; the next trip's plan asks again"));
            return;
        }

        _waitingScrollCoin += price; // M6 item 14: a scroll put off comes before the leather set (LeatherSwap)
        var percent = ground.Supplies.Purse.TripSpendPercent;
        var q = S.State.Self.Qualities;
        var pay = (int)Math.Min(int.MaxValue - 1, PurseView.CounterCanPayUnreadAsZero(S.State, TownTripPurse.ScrivenersTakeNotes)); // M6 16 E5a2: what the scrivener's counter can pay with (the coin and the faces of the notes held): the hold is released on it, as the counters read it
        var coin = Math.Max((int)Math.Ceiling(price * 100.0 / percent), pay + 1); // more than this trip's purse: the same purse would only release it for the same trip
        var held = counters.HoldLearn(learn.SpellId, coin, q.Level);
        Log(Text($"learn: {learn.Name} ({learn.SpellId}): waits: its scroll costs {price} and this trip's budget ({percent}% of the purse {FloorPurse()}, after the stop lines and the scrolls before it) cannot pay it; {(held ? $"its trigger is held until the coin reaches {coin} or the level passes {q.Level}" : "its trigger was held already")}"));
        if (held && pay != PurseView.CoinUnreadAsZero(S.State))
        {
            Log(Text($"learn: {learn.Name} ({learn.SpellId}): the trade notes held ({pay - PurseView.CoinUnreadAsZero(S.State)}) count with the coin toward the {coin}: the hold is released by what the scrivener's counter can pay with, {pay} now"));
        }
    }

    /// <summary>
    /// M6 4 t4a (D40): one line sold only off home. Before: the book is read and lacks the spell, no refusal this session, ACE lets the character read it (<see cref="LearnBefore"/>); a scroll already held is read at home (no trip). Then the
    /// town by <see cref="TownNetworkTrip.Choose"/> among the catalog's places of the spell's sellers (the choice and every refusal on one line); none admitted is the loud stop, logged once a session.
    /// </summary>
    private void PlanTownLearn(HuntingGround ground, LadderRung rung, int level, LearnNeed need, ScrivenerSpell far, string name, HashSet<uint> logged)
    {
        var catalog = ScrivenerCatalog.Default;
        var learn = new LearnLine(far.SpellId, far.Name, far.ScrollWcid, far.Sellers.FirstOrDefault()?.VendorWcid ?? 0);
        var reason = LearnBefore(learn, out var why);
        if (reason != LearnReason.None)
        {
            Log(Text($"learn: plan: rung {rung.Key} level {level}: {name}: off-home: {Token(reason)}: {why}"));
            return;
        }

        if (ScrollsHeld(far.ScrollWcid).Count > 0)
        {
            _toRead.Add(learn);
            Log(Text($"learn: plan: rung {rung.Key} level {level}: {name}: off-home ({need.Why}); a scroll (weenie {far.ScrollWcid}) is in the pack already: no trip, read after the walk"));
            return;
        }

        var candidates = far.Sellers
            .Select(s => catalog.ScrivenerOf(s.VendorWcid))
            .OfType<Scrivener>()
            .SelectMany(s => s.Places.Select(p => new TownTripCandidate(p.Town, s.Name, s.Wcid, p)))
            .OrderBy(k => k.Town, StringComparer.Ordinal) // the towns as the plan names them (ScrivenerCatalog.TownsSelling)
            .ToList();
        var choice = TownNetworkTrip.Choose(candidates, TownInput(ground), Context.Routes, LeverDoors.Default);
        if (choice.Chosen is not { } plan)
        {
            if (logged.Add(far.SpellId))
            {
                Log(Text($"learn: plan: {choice.Stop}: {name} is sold only at {string.Join(", ", need.Towns)}, not at the home {ground.Home.Town}, and no trip by the Town Network is admitted: {choice.Why}; the learn stops here and the fight casts what the book holds ({need.Why}); logged once a session"));
            }

            return;
        }

        var price = catalog.ScrivenerOf(plan.Candidate.Wcid)!.PriceOf(far.ScrollValue);
        learn = learn with { VendorWcid = plan.Candidate.Wcid };
        _toRead.Add(learn);
        _townLearns.Add((learn, plan, price));
        Log(Text($"learn: plan: rung {rung.Key} level {level}: {name}: off-home ({need.Why}): by the Town Network to {plan.Candidate.Town} (travel={TownNetworkTrip.TravelWord}), {price} pyreals at {plan.Candidate.Name} (0x{plan.Candidate.Vendor.Guid:X8}); {choice.Why}"));
    }

    /// <summary>The trip rule's input for this character: the home lifestone's travel place, the level, Lifestone Recall in the book, and whether the ground's home is the travel file's start.</summary>
    private TownTripInput TownInput(HuntingGround ground) => new(
        TownNetworkTrip.PlaceKeyOf(ground.Home.LifestoneCell >> 16, Context.Routes),
        S.State.Self.Qualities.Level,
        S.State.Self.Spellbook.Knows(Fighter.RecallHome.LifestoneRecallSpell),
        Context.Routes.StartLifestone.Cell >> 16 == ground.Home.LifestoneCell >> 16);

    /// <summary>
    /// M6 4 t4a (D40, D43; closes <c>TODO(m6-scrivener-town-network)</c>): the trip by the Town Network for the lines sold only off home, after the home walk and its reads. One town a trip (the first town of the plan's order that the purse and the pack admit, M6 item 14; a line of another town
    /// waits for the next trip, logged). Check, act, check (M5 D141) at each step, a failure named and remembered for the session (<see cref="LearnReason.TownTrip"/>, no loop):
    /// <list type="number">
    /// <item>before: the purse covers the scrolls at the catalog's price and the pack has the room (M6 item 14, m6-purse: <see cref="TownTripPurse"/>: the coin and the faces of the trade notes a buy would change at the scrivener's counter; the
    /// scrolls the purse pays, cheapest first, the rest waiting by name; a town the purse pays nothing of is passed for the plan's next town; a full pack is named before the trip leaves); the body stands in the first portal's place (else the
    /// walk to the home lifestone's point, the travel file's start, and the check again);</item>
    /// <item>the hops (<see cref="PortalHopLeg"/>: walk to the front, Use, arrive in the portal's destination landblock), each its own line;</item>
    /// <item>at each scrivener: met in reach (the resupply's walk: the goal-tile horizon since M6 4 W1, the NPC bodies since W2), his shop open, the scroll bought (<see cref="BuyLine"/>: the scroll counts in <c>scrolls_bought=</c>);</item>
    /// <item>the reads (<see cref="ReadOne"/>: <c>learned=</c>), then home: Lifestone Recall when the book holds it (the journey's travel seam, <see cref="TravelPurpose.Home"/>), else the rung change's <c>direction=home</c> by portals; after: present in
    /// the home's landblock (the recall) or the travel file's start (the portals), off the lifestone.</item>
    /// </list>
    /// One line: <c>learn_trip: town= vendors= spells= hops= travel=town-network bought= learned= home= ok= reason=</c>.
    /// </summary>
    private IEnumerable<ActionHandle?> TownLearnTrip(HuntingGround ground)
    {
        if (_townLearns.Count == 0)
        {
            yield break;
        }

        if (Context.Facts.CoinToNotes)
        {
            // M6 item 14 (the den run's trip, the owner's order of 2026-11-13): no town trip for scrolls: the coin goes into notes for the run, and a scroll bought now would change a note back into loose coin
            Log(Text($"learn: town trip: none on this trip (the order turns the coin into notes: m6-coin-to-notes): {string.Join(", ", _townLearns.Select(t => t.Learn.Name))} wait for the next ordinary trip (nothing is remembered as refused)"));
            yield break;
        }

        // M6 item 14 (m6-purse): the check before, a town at a time in the plan's order: the purse is the coin and the trade notes a buy changes at the counter, and the pack must take the scroll and the note's payout (TownTripPurse).
        // One town a trip; a town the purse or the pack refuses is named with its own learn_trip line and the plan's next town is tried, so a dear town first does not starve a cheap one.
        var coins = Coins();
        var notes = HeldNotes(ground).Select(h => new TownTripPurse.Note(h.Note.Name, h.Note.Value, S.State.Objects.TryGet(h.Guid, out var stack) ? GearInventory.StackOf(S.State, stack) : 1)).ToList();
        var freeMain = PackView.MainPackFree(S.State);
        var freeAll = freeMain + PackLayout.SidePacks(S.State).Sum(p => Math.Max(0, p.Capacity - p.Count));
        List<(LearnLine Learn, TownTripPlan Plan, int Price)>? chosen = null;
        TownTripPlan? chosenPlan = null;
        var towns = _townLearns.GroupBy(t => t.Plan.Candidate.Town).Select(g => g.ToList()).ToList();
        for (var n = 0; n < towns.Count; n++)
        {
            var lines = towns[n];
            var first = lines[0].Plan;
            var to = first.Candidate.Town;
            if (chosen is not null)
            {
                foreach (var later in lines)
                {
                    Log(Text($"learn: town trip: {later.Learn.Name} ({later.Learn.SpellId}) is bought in {to}: it waits for the next trip (one town a trip)"));
                }

                continue;
            }

            var verdict = TownTripPurse.Judge(coins, notes, TownTripPurse.ScrivenersTakeNotes, freeMain, freeAll, [.. lines.Select(t => new TownTripPurse.Line(t.Learn.Name, t.Price))]);
            Log(Text($"learn: town trip: check before: to {to} for {string.Join(", ", lines.Select(t => t.Learn.Name))} at {string.Join(", ", lines.Select(t => t.Plan.Candidate.Name).Distinct())}; hops {first.HopKeys}; home by {first.Home}; {verdict.Why}"));
            foreach (var i in verdict.Goes ? verdict.Waits : Enumerable.Range(0, lines.Count))
            {
                LearnRefusals.Add(Text($"{lines[i].Learn.SpellId}:{(verdict.Goes ? "purse-short" : verdict.Reason)}"));
            }

            if (verdict.Goes)
            {
                foreach (var i in verdict.Waits)
                {
                    Log(Text($"learn: town trip: {lines[i].Learn.Name} ({lines[i].Learn.SpellId}) waits: the purse pays {verdict.Pays} of {to}'s {verdict.Cost} and its scroll costs {lines[i].Price}; the next trip the purse pays buys it (nothing is remembered as refused)"));
                }

                chosen = [.. verdict.Buys.Select(i => lines[i])];
                chosenPlan = chosen[0].Plan; // the town's hops and way home are one for all its scriveners (TownNetworkTrip.Choose plans by the place)
                continue;
            }

            Log(Text($"learn_trip: town={BuyTripRule.Word(to)} vendors={string.Join('+', lines.Select(t => t.Plan.Candidate.Wcid).Distinct())} spells={string.Join('+', lines.Select(t => t.Learn.SpellId.ToString(CultureInfo.InvariantCulture)))} hops={first.HopKeys} travel={TownNetworkTrip.TravelWord} bought=0 learned=0 home={first.Home} ok=no reason={verdict.Reason}"));
            Log(Text($"learn: town trip: recovery: no trip to {to} ({verdict.Reason}); {(n + 1 < towns.Count ? "the plan's next town is tried" : "the plan names no other town, she fights on with what the book holds")}, and the next trip checks {to} again"));
        }

        if (chosen is not { } trip || chosenPlan is not { } plan)
        {
            yield break;
        }

        var town = plan.Candidate.Town;
        var boughtBefore = ScrollsBought;
        var learnedBefore = Learned;
        var reason = "none";
        var ok = false;
        var spells = string.Join('+', trip.Select(t => t.Learn.SpellId.ToString(CultureInfo.InvariantCulture)));
        Stage = "town-trip:" + town;
        try
        {
            if (plan.Hops.Count > 0 && plan.Hops[0].FromLandblocks.Count > 0 && (S.State.Self.Position is not { } at || !plan.Hops[0].FromLandblocks.Contains((ushort)(at.CellId >> 16))))
            {
                Log(Text($"walking: purpose=town-trip (to {ground.Home.Town}'s lifestone point, in the place of {plan.Hops[0].Name}, the first portal of the trip to {town})"));
                foreach (var h in TravelTo(new TravelRequest(Destination("home-lifestone-point", ground.Home.LifestoneCell, ground.Home.LifestonePoint), TravelPurpose.Bind), "the trip's start", optional: true)) { yield return h; }

                if (LastTravelFailure is { } walk || S.State.Self.Position is not { } there || !plan.Hops[0].FromLandblocks.Contains((ushort)(there.CellId >> 16)))
                {
                    reason = "start";
                    TownTripRefused(trip, Text($"the walk to the trip's start ended at {Where()}, not in {plan.Hops[0].Name}'s place: {LastTravelFailure ?? "the check after"}"));
                    yield break;
                }
            }

            for (var n = 0; n < plan.Hops.Count; n++)
            {
                var portal = plan.Hops[n];
                var hop = new PortalHopLeg(Context, portal, Text($"the learn trip to {town}, hop {n + 1} of {plan.Hops.Count}"));
                while (hop.Step(S, S.Clock.Now) == ScenarioStatus.Running)
                {
                    yield return null;
                }

                var seen = hop.Outcome;
                Log(Text($"learn_trip_hop: n={n + 1} edge={portal.Key} to={RungRoute.Hex(S.State.Self.Position?.CellId >> 16 ?? 0)} walk_m={seen.WalkMetres:0} taken={(seen.Taken ? "yes" : "no")} recoveries={seen.Recoveries} ok={(hop.Failure is null ? "yes" : "no")}"));
                if (hop.Failure is not null)
                {
                    reason = "hop:" + (hop.FailureName ?? portal.Key);
                    TownTripRefused(trip, Text($"the hop {portal.Key} failed: {hop.Failure}"));
                    foreach (var h in TownTripHome(ground, plan)) { yield return h; }

                    yield break;
                }
            }

            Log(Text($"learn: town trip: check after the hops: in {town} ({Where()}), the drop 0x{plan.DropCell:X8}"));
            var notesBefore = NotesSold;
            foreach (var vendorGuid in trip.Select(t => t.Plan.Candidate.Vendor.Guid).Distinct())
            {
                var lines = trip.Where(t => t.Plan.Candidate.Vendor.Guid == vendorGuid).ToList();
                var c = lines[0].Plan.Candidate;
                var vendor = ScrivenerCatalog.Default.VendorsIn(town).FirstOrDefault(v => v.Guid == vendorGuid)
                    ?? new GroundVendor(c.Name, c.Wcid, c.Vendor.Guid, c.Vendor.Cell, c.Vendor.Point, c.Vendor.StopCell ?? c.Vendor.Cell, c.Vendor.StopPoint, 0, []);
                Stage = "town-trip:vendor:" + vendor.Name;
                Log(Text($"walking: purpose=vendor:{vendor.Name} (the learn trip in {town}: {string.Join(", ", lines.Select(l => l.Learn.Name))})"));
                uint guid = 0;
                foreach (var h in Meet(vendor, g => guid = g)) { yield return h; }

                if (guid == 0)
                {
                    reason = "vendor-unreached";
                    var noStop = vendor.StopCell == vendor.Cell && vendor.StopPoint is null ? "; his catalog place has no stop before his counter (TODO(m6-town-scrivener-stops))" : string.Empty;
                    TownTripRefused(lines, Text($"{vendor.Name} 0x{vendor.Guid:X8} was not met in reach: {_unreachable ?? "not seen within " + NpcSeconds.ToString("0", CultureInfo.InvariantCulture) + " s"}{noStop}"));
                    continue;
                }

                var open = S.Actions.OpenVendor(guid, ActionSource.Automation);
                yield return open;
                if (open.Outcome is not { Kind: ActionOutcomeKind.Confirmed } || S.State.Vendor is not { } opened || opened.Vendor != guid)
                {
                    reason = "vendor-closed";
                    TownTripRefused(lines, Text($"{vendor.Name}'s shop did not open: {Describe(open)}"));
                    continue;
                }

                Vendors++;
                foreach (var line in lines)
                {
                    var ware = new StockLine(line.Learn.ScrollWcid, line.Learn.Name, 1, 1, 1);
                    foreach (var h in BuyLine(ground, vendor, guid, new ResupplyBuy(ware, vendor, 0, 1))) { yield return h; }
                }
            }

            // M6 item 14 (m6-purse): the check after the buys: the scrolls held against the scrolls planned, the coin and the notes changed; a scroll not bought was named by its buy (the purse, the pack or ACE's answer) and its read says the next trip tries again
            Log(Text($"learn: town trip: check after the buys: {ScrollsBought - boughtBefore} of {trip.Count} scroll(s) bought in {town}, {NotesSold - notesBefore} note(s) changed at the counter, the coin {coins} -> {Coins()}"));
            foreach (var t in trip)
            {
                if (!_memory.LearnRefused.ContainsKey(t.Learn.SpellId))
                {
                    foreach (var h in ReadOne(t.Learn)) { yield return h; }
                }
            }

            var before = reason;
            reason = "home";
            foreach (var h in TownTripHome(ground, plan)) { yield return h; }

            ok = true;
            reason = before == "none" ? (Learned - learnedBefore == trip.Count ? "learned" : "partial") : before;
        }
        finally
        {
            Log(Text($"learn_trip: town={BuyTripRule.Word(town)} vendors={string.Join('+', trip.Select(t => t.Plan.Candidate.Wcid).Distinct())} spells={spells} hops={plan.HopKeys} travel={TownNetworkTrip.TravelWord} bought={ScrollsBought - boughtBefore} learned={Learned - learnedBefore} home={plan.Home} ok={(ok ? "yes" : "no")} reason={reason}"));
        }
    }

    /// <summary>The trip's lines refused by name and remembered for the session (no loop).</summary>
    private void TownTripRefused(IEnumerable<(LearnLine Learn, TownTripPlan Plan, int Price)> lines, string why)
    {
        foreach (var t in lines)
        {
            RefuseLearn(t.Learn, LearnReason.TownTrip, Text($"the trip to {t.Plan.Candidate.Town}: {why}"));
        }
    }

    /// <summary>
    /// The trip's way home: Lifestone Recall when the book holds it (the travel seam's <see cref="TravelPurpose.Home"/>), else the rung change's <c>direction=home</c> by portals (the travel file's ways home). After: present in the home's
    /// landblock (or the travel file's start), then off the lifestone. A failure fails the run by name (the character is away from home).
    /// </summary>
    private IEnumerable<ActionHandle?> TownTripHome(HuntingGround ground, TownTripPlan plan)
    {
        Stage = "town-trip:home";
        var recall = S.State.Self.Spellbook.Knows(Fighter.RecallHome.LifestoneRecallSpell);
        uint homeLandblock;
        if (recall)
        {
            homeLandblock = ground.Home.LifestoneCell >> 16;
            Log(Text($"walking: purpose=home (the learn trip to {plan.Candidate.Town}: Lifestone Recall to {ground.Home.Town})"));
            foreach (var h in TravelTo(new TravelRequest(Destination("home-lifestone", ground.Home.LifestoneCell, ground.Home.LifestonePoint), TravelPurpose.Home), "home", optional: true)) { yield return h; }

            if (LastTravelFailure is { } failed)
            {
                Fail(Text($"learn: town trip: the way home from {plan.Candidate.Town} by Lifestone Recall: {failed}"));
                throw new InvalidOperationException(Failure);
            }
        }
        else
        {
            homeLandblock = Context.Routes.StartLifestone.Cell >> 16;
            Log(Text($"walking: purpose=home (the learn trip to {plan.Candidate.Town}: the book lacks Lifestone Recall; the rung change direction=home by portals to {Context.Routes.StartPlace})"));
            var run = new RungChangeStation(RungDirection.Home).Start(Context);
            while (run.Step(S, S.Clock.Now) == ScenarioStatus.Running)
            {
                yield return null;
            }

            Log(Text($"learn: town trip: the way home by portals: {run.VerdictFields()}"));
            if (!run.Ok)
            {
                Fail(Text($"learn: town trip: the way home from {plan.Candidate.Town} by portals failed: {run.VerdictFields()}"));
                throw new InvalidOperationException(Failure);
            }
        }

        if (S.State.Self.Position is not { } home || home.CellId >> 16 != homeLandblock)
        {
            Fail(Text($"learn: town trip: after the way home the player stands at {Where()}, not in the home landblock 0x{homeLandblock:X4}"));
            throw new InvalidOperationException(Failure);
        }

        Log(Text($"learn: town trip: check after the way home: at {Where()}"));
        if (S.State.Objects.NearestWithFlags(WorldObjectTable.LifestoneFlag, in home, RecallHomeStation.OnLifestoneMetres, out var lifestone))
        {
            Log(Text($"learn: town trip: stands in lifestone {lifestone:X8}: stepping off it to the lifestone point (m5-logistics-step-off)"));
            foreach (var h in TravelTo(new TravelRequest(Destination("home-lifestone-point", ground.Home.LifestoneCell, ground.Home.LifestonePoint), TravelPurpose.Bind), "off the lifestone", optional: true)) { yield return h; }
        }
    }

    private string Where() => S.State.Self.Position is { } p ? Text($"cell 0x{p.CellId:X8} ({p.Origin.X:0.#}, {p.Origin.Y:0.#})") : "an unknown place";

    /// <summary>The off-home stops already logged, per session's memory (logged once a session, D40).</summary>
    private static readonly ConditionalWeakTable<SaleMemory, HashSet<uint>> OffHomeLogged = new();

    /// <summary>True when the trip has a learn step on <paramref name="ground"/>: the ground's own list, a home town with catalog scriveners (the plan's lines; M6 1 c), or a ground some rung plays (the scrolls the pack holds; M6 1 e).</summary>
    /// <param name="ground">The trip's ground.</param>
    internal static bool LearnsOn(HuntingGround ground) => ground.Learn.Count > 0 || ground.Scriveners.Count > 0 || LadderLoader.WarMage.Rungs.Any(r => string.Equals(ScrivenerLearnPlan.PlayedGround(r), ground.Key, StringComparison.Ordinal)); // M6 1 e: a ground a rung plays reads the scrolls its pack holds

    /// <summary>True when the ware <paramref name="wcid"/> is a scroll this trip buys to read (the ground's list or the plan's).</summary>
    /// <param name="ground">The trip's ground.</param>
    /// <param name="wcid">The ware's weenie class id.</param>
    internal bool IsLearnScroll(HuntingGround ground, uint wcid) => ground.Learn.Any(l => l.ScrollWcid == wcid) || _toRead.Any(l => l.ScrollWcid == wcid);

    /// <summary>The plan's kind as the log's token (kebab case).</summary>
    internal static string LearnNeedToken(LearnNeedKind kind) => kind switch
    {
        LearnNeedKind.Due => "due",
        LearnNeedKind.Known => "known",
        LearnNeedKind.Untrained => "untrained",
        LearnNeedKind.NoLevel => "no-level",
        LearnNeedKind.SkillShort => "skill-short",
        LearnNeedKind.OffHome => "off-home",
        LearnNeedKind.Saving => "saving",
        LearnNeedKind.BotCovers => "bot-covers",
        LearnNeedKind.Later => "later",
        LearnNeedKind.NoGain => "no-gain",
        _ => "other",
    };

    /// <summary>The before-check of a learn: the spellbook is read and lacks the spell, no refusal earlier in the session, and the school meets ACE's requirement.</summary>
    private LearnReason LearnBefore(LearnLine learn, out string why)
    {
        var book = S.State.Self.Spellbook;
        if (!book.IsKnown)
        {
            why = "the spellbook is not read yet (no PlayerDescription): nothing is bought on an assumption";
            return LearnReason.SpellbookUnread;
        }

        if (book.Knows(learn.SpellId))
        {
            why = "the spellbook holds it: the scroll is never bought again";
            return LearnReason.Known;
        }

        if (_memory.LearnRefused.TryGetValue(learn.SpellId, out var before))
        {
            why = "refused earlier this session (" + Token(before) + "): not tried again";
            return LearnReason.RefusedBefore;
        }

        if (!SpellOf(learn.SpellId, out var school, out var power))
        {
            why = "TODO(m5-learn-spell-data): neither the SpellTable given to the trip, the profile nor the scrivener catalog holds the spell's school and power";
            return LearnReason.UseRefused;
        }

        return CanRead(S.State.Self.Qualities, school, power, out why);
    }

    /// <summary>The spell's school and power: the SpellTable the trip was given, else the profile's line (pinned against the dat by <c>BsdProfileSpellsTests</c>).</summary>
    private bool SpellOf(uint id, out MagicSchool school, out uint power)
    {
        if (Context.Spells.TryGetValue(id, out var info))
        {
            (school, power) = (info.School, info.Power);
            return true;
        }

        foreach (var line in ArenaProfile.Lines)
        {
            foreach (var spell in line.Spells)
            {
                if (spell.Id == id)
                {
                    (school, power) = (line.School, spell.Power);
                    return true;
                }
            }
        }

        if (ScrivenerCatalog.Default.Spell(id) is { } scroll && ScrivenerCatalog.Default.Line(scroll.Line) is { } scrollLine)
        {
            (school, power) = (scrollLine.School, scroll.Power); // M6 1 c: the catalog's (pinned against the dat by ScrivenerCatalogTests)
            return true;
        }

        if (FightDebuffs.Spells.FirstOrDefault(d => d.Id == id) is { Id: not 0 } debuff)
        {
            (school, power) = (debuff.School == DebuffSchool.LifeMagic ? MagicSchool.LifeMagic : MagicSchool.CreatureEnchantment, debuff.Power); // M6 1 e: a VII debuff scroll the loot kept (FightDebuffs, pinned to the SpellTable by FightDebuffsDatTests)
            return true;
        }

        (school, power) = (default, 0);
        return false;
    }

    /// <summary>
    /// Reads each scroll the plan kept, after the walk: before (held, the skill), the use, after (the spellbook holds the spell; the chat line). A refusal is remembered for the session. The lines sold only off home are read on their trip by
    /// the Town Network, which runs last (<see cref="TownLearnTrip"/>, M6 4 t4a).
    /// </summary>
    private IEnumerable<ActionHandle?> ReadScrolls()
    {
        foreach (var learn in _toRead)
        {
            if (_townLearns.Any(t => t.Learn.SpellId == learn.SpellId))
            {
                continue; // read on its trip
            }

            foreach (var h in ReadOne(learn)) { yield return h; }
        }

        foreach (var h in TownLearnTrip(Context.Dungeon)) { yield return h; }

        foreach (var h in TieAfterLearn()) { yield return h; }
    }

    /// <summary>
    /// M6 7 AS (spec 2, D43: Lifestone Recall and Tie arrive with Item Enchantment at 16): once the book holds both Lifestone Recall and Lifestone Tie and nothing this session proved the tie (<see cref="JourneyFacts.LifestoneTied"/>), she ties to Holtburg's lifestone by
    /// the journey's own bind station (<see cref="BindStation"/>: walk, Use, then Lifestone Tie 2644 with its fizzle retries by name, "linked with the life stone" read after). Check before: both spells in the book, the tie not proven, the body in Holtburg's landblock (the
    /// station's lifestone is Holtburg's). Check after: <see cref="JourneyFacts.LifestoneTied"/>. A failure is logged by name, never retried here: the recall's 0x049E refusal binds again (M5 7 F14) and the next trip reaches this step again. A Recall whose Item skill is under
    /// the plan's bar is never bought (<see cref="LearnNeedKind.SkillShort"/> or <see cref="LearnNeedKind.NoLevel"/>, logged by the plan), so the book lacks it and every way home stays on foot or by portals.
    /// </summary>
    private IEnumerable<ActionHandle?> TieAfterLearn()
    {
        var book = S.State.Self.Spellbook;
        if (!book.IsKnown)
        {
            yield break;
        }

        var recall = book.Knows(Fighter.RecallHome.LifestoneRecallSpell);
        var tie = book.Knows(BindStation.LifestoneTieSpell);
        if (!recall || Context.Facts.LifestoneTied == true)
        {
            yield break;
        }

        if (!tie)
        {
            Log(Text($"learn: tie: Lifestone Recall is in the book and Lifestone Tie {BindStation.LifestoneTieSpell} is not: the tie waits for its scroll (the plan buys it next)"));
            yield break;
        }

        if (S.State.Self.Position is not { } here || here.CellId >> 16 != Fighter.RecallHome.HoltburgLandblock)
        {
            Log(Text($"learn: tie: both spells are in the book, the tie is not proven and the body stands at {Where()}, not in Holtburg's landblock 0x{Fighter.RecallHome.HoltburgLandblock:X4}: the tie waits for a trip at home (the recall's 0x049E refusal binds, F14)"));
            yield break;
        }

        Stage = "learn:tie";
        Log(Text($"learn: tie: Lifestone Recall and Lifestone Tie are in the book, the tie is not proven this session: binding and tying at Holtburg's lifestone (check after: the link line)"));
        var run = new BindStation().Start(Context);
        while (run.Step(S, S.Clock.Now) == ScenarioStatus.Running)
        {
            yield return null;
        }

        var tied = Context.Facts.LifestoneTied == true;
        Log(Text($"learn: tie: the bind station ended {run.VerdictFields()} ok={(run.Ok ? "yes" : "no")}; check after: lifestone_tied={(tied ? "yes" : "no")}{(tied ? string.Empty : "; named stop TODO(m6-tie-after-learn): not retried here, the next trip tries again and a recall refused with 0x049E binds (F14)")}"));
    }

    /// <summary>One scroll read (see <see cref="ReadScrolls"/>).</summary>
    private IEnumerable<ActionHandle?> ReadOne(LearnLine learn)
    {
        {
            Stage = "learn:" + learn.Name;
            var reason = LearnBefore(learn, out var why);
            if (reason == LearnReason.Known)
            {
                Log(Text($"learn: {learn.Name} ({learn.SpellId}) is in the spellbook already: no read"));
                yield break;
            }

            if (reason != LearnReason.None)
            {
                RefuseLearn(learn, reason, why);
                yield break;
            }

            CheckScrollKit(learn); // the owner's rule of 2026-11-08: the check after the buy
            var scroll = ScrollsHeld(learn.ScrollWcid).FirstOrDefault();
            if (scroll == 0)
            {
                Log(Text($"learn: {learn.Name} ({learn.SpellId}): no scroll (weenie {learn.ScrollWcid}) is in the pack after the walk (the buy said why above): the next trip tries again"));
                LearnRefusals.Add(Text($"{learn.SpellId}:{Token(LearnReason.NoScroll)}"));
                yield break;
            }

            var mark = S.State.Chat.Total;
            Log(Text($"learn: reading the scroll 0x{scroll:X8} of {learn.Name} ({learn.SpellId}): {why}"));
            string? answer = null;
            for (var attempt = 1; ; attempt++)
            {
                var use = S.Actions.Use(scroll, ActionSource.Automation);
                yield return use;
                if (use.Outcome is not { Kind: ActionOutcomeKind.Confirmed })
                {
                    RefuseLearn(learn, LearnReason.UseRefused, "the use of the scroll ended " + Describe(use));
                    yield break;
                }

                foreach (var h in Until(() => S.State.Self.Spellbook.Knows(learn.SpellId) || (answer = ScrollAnswer(mark)) is { } a && !a.StartsWith("You learn", StringComparison.Ordinal) || TooBusy(mark), LearnSeconds)) { yield return h; }

                // M6 7 T: the previous scroll's use animation can still run when this use arrives: ACE answers "You're too busy!" (0x001D) and reads nothing. One named re-send after a pause, then the refusal as before.
                if (S.State.Self.Spellbook.Knows(learn.SpellId) || answer is not null || !TooBusy(mark) || attempt >= 2)
                {
                    break;
                }

                Log(Text($"learn: ACE answered \"You're too busy!\" to the use of {learn.Name} ({learn.SpellId}): the previous read is still finishing; waiting {BusyPauseSeconds:0.#} s, then the scroll is used once more (the one named recovery)"));
                foreach (var h in Until(() => false, BusyPauseSeconds)) { yield return h; }

                mark = S.State.Chat.Total;
            }

            answer ??= ScrollAnswer(mark);
            if (S.State.Self.Spellbook.Knows(learn.SpellId))
            {
                Learned++;
                Log(Text($"learn: learned {learn.Name} ({learn.SpellId}): the spellbook holds it (the server's update); ACE said: {answer ?? "(no line yet)"}; scrolls of it left in the pack {ScrollsHeld(learn.ScrollWcid).Count}"));
                EverySpellRead(learn, LearnReason.None, string.Empty);
                yield break;
            }

            var refused = answer switch
            {
                { } t when t.Contains("not skilled enough", StringComparison.Ordinal) => LearnReason.SkillShort,
                { } t when t.Contains("not trained", StringComparison.Ordinal) => LearnReason.Untrained,
                { } t when t.Contains("already know", StringComparison.Ordinal) => LearnReason.Known,
                _ => LearnReason.NotLearned,
            };
            RefuseLearn(learn, refused, Text($"the spellbook does not hold it after the use (waited up to {LearnSeconds:0} s); ACE said: {answer ?? "nothing"}"));
        }
    }

    /// <summary>
    /// Whether ACE answered "You're too busy!" in the chat since <paramref name="mark"/>. M6 7 AA: the line is a WeenieError: it holds the code and an empty text (the chat looks the words up), so T's read of the words never matched live: in r3 every second scroll of a run of reads
    /// (Armor Self II, Magic Yield Other I) waited out its 15 s and was refused for the session with the scroll in the pack, and the trip at level 10 logged "refused-before".
    /// </summary>
    private bool TooBusy(long mark)
    {
        var chat = S.State.Chat;
        var fresh = (int)Math.Min(chat.Count, chat.Total - mark);
        for (var i = chat.Count - fresh; i < chat.Count; i++)
        {
            if (chat[i] is { Kind: ChatKind.WeenieError, ChatType: YoureTooBusy })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>ACE's answer to a scroll's use in the chat since <paramref name="mark"/>: the learn line or a refusal line, else null.</summary>
    private string? ScrollAnswer(long mark)
    {
        var chat = S.State.Chat;
        var fresh = (int)Math.Min(chat.Count, chat.Total - mark);
        for (var i = chat.Count - fresh; i < chat.Count; i++)
        {
            var text = chat[i].Text;
            if (text.StartsWith("You learn the ", StringComparison.Ordinal) || text.Contains("to learn this spell", StringComparison.Ordinal) || text.StartsWith("You are not trained in", StringComparison.Ordinal) || text.StartsWith("You already know that spell", StringComparison.Ordinal))
            {
                return text.TrimEnd();
            }
        }

        return null;
    }

    private void RefuseLearn(LearnLine learn, LearnReason reason, string why)
    {
        _memory.LearnRefused[learn.SpellId] = reason;
        LearnRefusals.Add(Text($"{learn.SpellId}:{Token(reason)}"));
        Refuse("learn", Text($"{learn.Name} ({learn.SpellId}): {Token(reason)}: {why}; not tried again this session (no loop)"));
        EverySpellRead(learn, reason, why); // M6 14: a read the every-spell rule planned and ACE refused: kept, named loud, never bought again
    }

    /// <summary>The reason as the log's token (kebab case).</summary>
    internal static string Token(LearnReason reason) => reason switch
    {
        LearnReason.None => "none",
        LearnReason.Known => "known",
        LearnReason.SpellbookUnread => "spellbook-unread",
        LearnReason.Untrained => "untrained",
        LearnReason.SkillShort => "skill-short",
        LearnReason.NoScroll => "no-scroll",
        LearnReason.UseRefused => "use-refused",
        LearnReason.NotLearned => "not-learned",
        LearnReason.RefusedBefore => "refused-before",
        LearnReason.TownTrip => "town-trip",
        _ => "other",
    };
}
