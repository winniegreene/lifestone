using System.Globalization;
using Lifestone.ServerTools;

namespace Lifestone.ServerMod;

/// <summary>What a talk or a give asked the bot (the hook its emote set stamps, <see cref="BuffBotRules.ReadHook"/>).</summary>
internal enum BuffBotHookKind
{
    /// <summary>A profile's word: a round.</summary>
    Word,

    /// <summary>The credit word: the teller's count.</summary>
    Credits,

    /// <summary>A Platinum Scarab given: a credit.</summary>
    Give,
}

/// <summary>One hook: what was asked, and the profile of a word.</summary>
/// <param name="Kind">What was asked.</param>
/// <param name="Profile">The word's profile (a <see cref="BuffBotHookKind.Word"/>).</param>
internal readonly record struct BuffBotHook(BuffBotHookKind Kind, BuffBotProfile? Profile = null);

/// <summary>The character that told or gave: its object id, name and level (ACE's, read when it asked), and whether it stands in reach of a tell's answer.</summary>
/// <param name="Guid">The character's object id.</param>
/// <param name="Name">Its name.</param>
/// <param name="Level">Its level.</param>
internal readonly record struct BuffBotCaller(uint Guid, string Name, int Level);

/// <summary>Where the bot is in its day.</summary>
internal enum BuffBotPhase
{
    /// <summary>At its post, free.</summary>
    Idle,

    /// <summary>Casting a round.</summary>
    Round,

    /// <summary>The ten seconds after a round before it leaves for tapers (D138).</summary>
    Leaving,

    /// <summary>On the way to the shop, at it or on the way back.</summary>
    Away,
}

/// <summary>What the ACE side does for one step of the desk.</summary>
internal enum BuffBotActKind
{
    /// <summary>Turn to <see cref="BuffBotAct.Player"/>.</summary>
    Turn,

    /// <summary>Tell <see cref="BuffBotAct.Player"/> <see cref="BuffBotAct.Text"/>.</summary>
    Tell,

    /// <summary>Say <see cref="BuffBotAct.Text"/> in local chat.</summary>
    Say,

    /// <summary>Cast the round of <see cref="BuffBotAct.Profile"/> on <see cref="BuffBotAct.Player"/>.</summary>
    Round,

    /// <summary>Walk to the shop.</summary>
    Leave,

    /// <summary>Write <see cref="BuffBotAct.Text"/> to the server log.</summary>
    Log,

    /// <summary>Save the ledger to the state file.</summary>
    Save,
}

/// <summary>One step for the ACE side.</summary>
/// <param name="Kind">What to do.</param>
/// <param name="Player">The character it is for.</param>
/// <param name="Text">The line told, said or logged.</param>
/// <param name="Profile">The round's profile.</param>
internal readonly record struct BuffBotAct(BuffBotActKind Kind, uint Player = 0, string? Text = null, BuffBotProfile? Profile = null);

/// <summary>How a round ended.</summary>
internal enum BuffBotRoundEnd
{
    /// <summary>Every line cast.</summary>
    Completed,

    /// <summary>The pack lacked a component of the next cast (<see cref="BuffBotWire.OutOf"/>).</summary>
    OutOfComponents,

    /// <summary>The teller stood past the spell's reach (<see cref="BuffBotWire.OutOfReach"/>).</summary>
    OutOfReach,

    /// <summary>The teller left the world or the landblock.</summary>
    Gone,
}

/// <summary>The round in hand: who, which profile, and whether a credit paid for it.</summary>
/// <param name="Caller">The payer.</param>
/// <param name="Profile">The profile.</param>
/// <param name="Charged">True when a credit was taken.</param>
internal sealed record BuffBotRound(BuffBotCaller Caller, BuffBotProfile Profile, bool Charged);

/// <summary>
/// D138 (m6-plan.md): the bot's day with nothing from ACE in it, so <c>BuffBotDeskTests</c> runs it. The ACE side (<c>BuffBotBody</c>) feeds it what was asked (<see cref="Hear"/>), how a round ended
/// (<see cref="RoundEnded"/>), the clock (<see cref="Tick"/>) and the trip's end (<see cref="TripEnded"/>), and does the steps it answers with (<see cref="BuffBotAct"/>), in their order.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>The fee.</b> A word from a teller of <see cref="BuffBotWire.FeeLevel"/> or more takes one credit (<see cref="BuffBotWire.Spent"/>) or, with none, is answered <see cref="BuffBotWire.NeedCredit"/> and casts nothing; under it a round is free.</item>
/// <item><b>Credits.</b> A Platinum Scarab given is one credit and one scarab in the pack, in any phase (ACE has taken it). The credit word is answered with the count.</item>
/// <item><b>A round stopped short</b> (no components, the teller out of reach or gone) gives its credit back.</item>
/// <item><b>Restocking (the owner, 2026-11-14).</b> With fewer than <see cref="BuffBotRules.RestockUnder"/> tapers and no round in hand, the bot waits <see cref="BuffBotRules.LeaveDelaySeconds"/>, then says
/// <see cref="BuffBotWire.AwayAnnouncement"/> and leaves; a round in hand always finishes first, so it never leaves in the middle of one. A word or a credit ask while it is leaving or away is answered
/// <see cref="BuffBotWire.AwayTell"/> and kept for its return, when <see cref="BuffBotWire.BackAnnouncement"/> is said and the kept asks are answered in their order. A trip that failed is not tried again for
/// <see cref="BuffBotRules.RetryTripSeconds"/>.</item>
/// </list>
/// </remarks>
internal sealed class BuffBotDesk(BuffBotLedger ledger)
{
    private readonly List<(BuffBotCaller Caller, BuffBotHook Hook)> _waiting = [];

    /// <summary>The pack, the purse and the credits.</summary>
    public BuffBotLedger Ledger => ledger;

    /// <summary>The phase.</summary>
    public BuffBotPhase Phase { get; private set; }

    /// <summary>The round in hand, or null.</summary>
    public BuffBotRound? Round { get; private set; }

    /// <summary>When the bot leaves (in <see cref="BuffBotPhase.Leaving"/>).</summary>
    public double LeaveAt { get; private set; } = double.NaN;

    /// <summary>No trip starts before this (after a failed one).</summary>
    public double NextTripAt { get; private set; } = double.NegativeInfinity;

    /// <summary>The asks kept for the bot's return, in their order.</summary>
    public IReadOnlyList<(BuffBotCaller Caller, BuffBotHook Hook)> Waiting => _waiting;

    /// <summary>A talk or a give: the steps that answer it.</summary>
    /// <param name="caller">The teller or giver.</param>
    /// <param name="hook">What was asked.</param>
    /// <param name="now">The clock, in seconds.</param>
    public List<BuffBotAct> Hear(BuffBotCaller caller, BuffBotHook hook, double now)
    {
        var acts = new List<BuffBotAct>();
        var c = CultureInfo.InvariantCulture;
        if (hook.Kind == BuffBotHookKind.Give)
        {
            var credits = ledger.AddCredit(caller.Guid, caller.Name);
            ledger.Scarabs++;
            acts.Add(Log(string.Create(c, $"credit: {caller.Name} (0x{caller.Guid:X8}, level {caller.Level}) gave one {BuffBotWire.FeeName}: {credits} credit(s); the pack {ledger.Summary()}")));
            acts.Add(new(BuffBotActKind.Tell, caller.Guid, BuffBotWire.Credits(credits)));
            acts.Add(new(BuffBotActKind.Save));
            return acts;
        }

        if (Phase is BuffBotPhase.Leaving or BuffBotPhase.Away)
        {
            _waiting.RemoveAll(w => w.Caller.Guid == caller.Guid && w.Hook.Kind == hook.Kind);
            _waiting.Add((caller, hook));
            acts.Add(Log(string.Create(c, $"away ({Phase}): {caller.Name} asked {Asked(hook)}; kept for the return ({_waiting.Count} waiting)")));
            acts.Add(new(BuffBotActKind.Tell, caller.Guid, BuffBotWire.AwayTell));
            return acts;
        }

        if (hook.Kind == BuffBotHookKind.Credits)
        {
            acts.Add(new(BuffBotActKind.Turn, caller.Guid));
            acts.Add(new(BuffBotActKind.Tell, caller.Guid, BuffBotWire.Credits(ledger.CreditsOf(caller.Guid))));
            return acts;
        }

        if (Phase == BuffBotPhase.Round)
        {
            if (Round!.Caller.Guid != caller.Guid && !_waiting.Any(w => w.Caller.Guid == caller.Guid && w.Hook.Kind == BuffBotHookKind.Word))
            {
                _waiting.Add((caller, hook));
            }

            acts.Add(Log(string.Create(c, $"busy: {caller.Name} told '{hook.Profile?.Key}' while the round of {Round.Caller.Name} runs")));
            acts.Add(new(BuffBotActKind.Tell, caller.Guid, BuffBotWire.Busy));
            return acts;
        }

        Start(caller, hook.Profile ?? BuffBotWire.Default, acts);
        return acts;
    }

    /// <summary>The round in hand ended: its last line, a credit given back when it stopped short, then the bot is free (<see cref="Tick"/> decides a trip; a kept ask is answered first).</summary>
    /// <param name="end">How it ended.</param>
    /// <param name="detail">The component's name (<see cref="BuffBotRoundEnd.OutOfComponents"/>) or the reason, for the log.</param>
    /// <param name="now">The clock.</param>
    public List<BuffBotAct> RoundEnded(BuffBotRoundEnd end, string? detail, double now)
    {
        var acts = new List<BuffBotAct>();
        if (Round is not { } round)
        {
            return acts;
        }

        var c = CultureInfo.InvariantCulture;
        var who = round.Caller;
        switch (end)
        {
            case BuffBotRoundEnd.Completed:
                acts.Add(new(BuffBotActKind.Tell, who.Guid, BuffBotWire.Done(round.Profile)));
                break;
            case BuffBotRoundEnd.OutOfComponents:
                acts.Add(new(BuffBotActKind.Tell, who.Guid, BuffBotWire.OutOf(detail ?? "components")));
                break;
            case BuffBotRoundEnd.OutOfReach:
                acts.Add(new(BuffBotActKind.Tell, who.Guid, BuffBotWire.OutOfReach));
                break;
        }

        var returned = end != BuffBotRoundEnd.Completed && round.Charged;
        if (returned)
        {
            ledger.AddCredit(who.Guid, who.Name);
        }

        acts.Add(Log(string.Create(c, $"round ended ({end}{(detail is null ? string.Empty : ": " + detail)}): '{round.Profile.Key}' for {who.Name}{(round.Charged ? returned ? ", the credit given back" : ", paid by a credit" : ", free")}; the pack {ledger.Summary()}")));
        acts.Add(new(BuffBotActKind.Save));
        Round = null;
        Phase = BuffBotPhase.Idle;
        Serve(acts);
        acts.AddRange(Tick(now));
        return acts;
    }

    /// <summary>The clock: a free bot under the restock line starts its ten seconds; at their end it says it is away and leaves. Never while a round is in hand.</summary>
    /// <param name="now">The clock.</param>
    public List<BuffBotAct> Tick(double now)
    {
        var acts = new List<BuffBotAct>();
        if (Phase == BuffBotPhase.Idle && ledger.Tapers < BuffBotRules.RestockUnder && now >= NextTripAt)
        {
            Phase = BuffBotPhase.Leaving;
            LeaveAt = now + BuffBotRules.LeaveDelaySeconds;
            acts.Add(Log(string.Create(CultureInfo.InvariantCulture, $"restock: {ledger.Tapers} tapers, under {BuffBotRules.RestockUnder}: no round in hand, it leaves for the shop in {BuffBotRules.LeaveDelaySeconds:0} s")));
        }
        else if (Phase == BuffBotPhase.Leaving && now >= LeaveAt)
        {
            Phase = BuffBotPhase.Away;
            LeaveAt = double.NaN;
            acts.Add(new(BuffBotActKind.Say, Text: BuffBotWire.AwayAnnouncement));
            acts.Add(new(BuffBotActKind.Leave));
        }

        return acts;
    }

    /// <summary>The trip ended with the bot at its post (or recalled to it): it says it is back and answers the kept asks.</summary>
    /// <param name="ok">True when the shop was reached and the trade done.</param>
    /// <param name="why">The trip's line for the log.</param>
    /// <param name="now">The clock.</param>
    public List<BuffBotAct> TripEnded(bool ok, string why, double now)
    {
        var acts = new List<BuffBotAct>();
        if (Phase != BuffBotPhase.Away)
        {
            return acts;
        }

        Phase = BuffBotPhase.Idle;
        if (!ok)
        {
            NextTripAt = now + BuffBotRules.RetryTripSeconds;
        }

        acts.Add(Log(string.Create(CultureInfo.InvariantCulture, $"restock: back ({(ok ? "ok" : "failed")}: {why}); the pack {ledger.Summary()}{(ok ? string.Empty : $"; no trip for {BuffBotRules.RetryTripSeconds:0} s")}")));
        acts.Add(new(BuffBotActKind.Save));
        acts.Add(new(BuffBotActKind.Say, Text: BuffBotWire.BackAnnouncement));
        Serve(acts);
        return acts;
    }

    /// <summary>The bot was placed again (its landblock unloaded and loaded): any round or trip is forgotten, the kept asks with it; the ledger stays.</summary>
    public void Reset()
    {
        Phase = BuffBotPhase.Idle;
        Round = null;
        LeaveAt = double.NaN;
        _waiting.Clear();
    }

    private void Serve(List<BuffBotAct> acts)
    {
        while (Phase == BuffBotPhase.Idle && _waiting.Count > 0)
        {
            var (caller, hook) = _waiting[0];
            _waiting.RemoveAt(0);
            if (hook.Kind == BuffBotHookKind.Credits)
            {
                acts.Add(new(BuffBotActKind.Tell, caller.Guid, BuffBotWire.Credits(ledger.CreditsOf(caller.Guid))));
                continue;
            }

            Start(caller, hook.Profile ?? BuffBotWire.Default, acts);
        }
    }

    private void Start(BuffBotCaller caller, BuffBotProfile profile, List<BuffBotAct> acts)
    {
        var c = CultureInfo.InvariantCulture;
        acts.Add(new(BuffBotActKind.Turn, caller.Guid));
        var charged = false;
        if (caller.Level >= BuffBotWire.FeeLevel)
        {
            if (!ledger.TakeCredit(caller.Guid, caller.Name, out var left))
            {
                acts.Add(Log(string.Create(c, $"no credit: {caller.Name} (level {caller.Level}, the fee from {BuffBotWire.FeeLevel}) told '{profile.Key}' and holds none: nothing is cast")));
                acts.Add(new(BuffBotActKind.Tell, caller.Guid, BuffBotWire.NeedCredit));
                return;
            }

            charged = true;
            acts.Add(new(BuffBotActKind.Tell, caller.Guid, BuffBotWire.Spent(left)));
            acts.Add(new(BuffBotActKind.Save));
        }

        Round = new BuffBotRound(caller, profile, charged);
        Phase = BuffBotPhase.Round;
        acts.Add(Log(string.Create(c, $"round: '{profile.Key}' ({profile.Lines.Count} lines) for {caller.Name} (level {caller.Level}): {(charged ? $"one credit, {ledger.CreditsOf(caller.Guid)} left" : $"free under level {BuffBotWire.FeeLevel}")}")));
        acts.Add(new(BuffBotActKind.Tell, caller.Guid, BuffBotWire.Begins(profile)));
        acts.Add(new(BuffBotActKind.Round, caller.Guid, Profile: profile));
    }

    private static string Asked(BuffBotHook hook) => hook.Kind == BuffBotHookKind.Word ? "'" + hook.Profile?.Key + "'" : "the credit count";

    private static BuffBotAct Log(string text) => new(BuffBotActKind.Log, Text: text);
}

/// <summary>One slot of a cast's formula as the bot's pack holds it: the ware's weenie, its name for the lines, and ACE's component destruction modifier.</summary>
/// <param name="Wcid">The ware's weenie class id.</param>
/// <param name="Name">Its plural name (<c>Prismatic Tapers</c>).</param>
/// <param name="Cdm">The dat's CDM (SpellComponentTable).</param>
internal readonly record struct BuffBotSlot(uint Wcid, string Name, float Cdm);

/// <summary>How one cast went.</summary>
internal enum BuffBotCastKind
{
    /// <summary>The spell lands.</summary>
    Landed,

    /// <summary>The cast fizzles (its mana and its burns are spent).</summary>
    Fizzled,

    /// <summary>The mana does not pay it: rest, then try again.</summary>
    NoMana,

    /// <summary>A component is missing from the pack: the round stops.</summary>
    NoComponent,
}

/// <summary>One cast's outcome.</summary>
/// <param name="Kind">How it went.</param>
/// <param name="Mana">The mana it costs (spent after the wind-up).</param>
/// <param name="RestSeconds">The seconds to rest (<see cref="BuffBotCastKind.NoMana"/>).</param>
/// <param name="Missing">The missing ware's name (<see cref="BuffBotCastKind.NoComponent"/>).</param>
/// <param name="Burned">The slots burned.</param>
/// <param name="Chance">The chance it had to land.</param>
internal sealed record BuffBotCastResult(BuffBotCastKind Kind, uint Mana, int RestSeconds, string? Missing, IReadOnlyList<BuffBotSlot> Burned, double Chance);

/// <summary>
/// D138: one cast of the bot by a player's rules, mirrored from ACE @95ef426d with the bot's own numbers (the ACE side reads them from the bot and the spell):
/// <list type="number">
/// <item>the formula's components must all be in the pack (<c>Player.HasComponentsForSpell</c>, Player_Magic.cs 1227-1250), else the round stops;</item>
/// <item>the fizzle roll: the chance is <c>SkillCheck.GetMagicSkillChance(skill, power)</c> (SkillCheck.cs 19-22: the 0.07 factor), and none under <c>power - 50</c> (<c>GetCastingPreCheckStatus</c>,
/// Player_Magic.cs 529-546);</item>
/// <item>the mana: a cast that lands costs ACE's <c>CalculateManaUsage</c> (Creature_Magic.cs 14-53, Mana Conversion's saving), one that fizzles 5 (Player_Magic.cs 566-572); more than the bot holds and it rests
/// for the seconds its regeneration takes (ACE regenerates a creature's mana each heartbeat by its ManaRate, Creature_Vitals.cs 81-160);</item>
/// <item>the burn, whether it lands or fizzles (Player_Magic.cs 866-868): each slot of the foci formula burns with the chance <c>ComponentLoss x CDM x min(1, Power / skill)</c> (<c>Spell.TryBurnComponents</c>,
/// Entity/Spell.cs 157-189).</item>
/// </list>
/// </summary>
internal static class BuffBotCasting
{
    /// <summary>The mana a fizzle costs (Player_Magic.cs 571-572).</summary>
    public const uint FizzleMana = 5;

    /// <summary>The fizzles one line is tried through before the bot goes on to the next (a player tries again; Lifestone's cap, as the client's own <c>MaxFizzles</c>).</summary>
    public const int MaxFizzlesPerLine = 3;

    /// <summary>ACE's chance of a cast landing: none under <c>power - 50</c>, else <c>1 - 1 / (1 + e^(0.07 (skill - power)))</c>.</summary>
    /// <param name="skill">The caster's skill in the spell's school.</param>
    /// <param name="power">The spell's power (its difficulty).</param>
    public static double Chance(uint skill, uint power)
    {
        if (skill == 0 || skill < (long)power - 50)
        {
            return 0;
        }

        var chance = 1.0 - (1.0 / (1.0 + Math.Exp(0.07 * ((int)skill - (int)power))));
        return Math.Min(1.0, Math.Max(0.0, chance));
    }

    /// <summary>ACE's burn chance of one slot: <c>ComponentLoss x CDM x min(1, Power / skill)</c>, in ACE's float arithmetic.</summary>
    /// <param name="componentLoss">The spell's ComponentLoss.</param>
    /// <param name="cdm">The component's CDM.</param>
    /// <param name="power">The spell's power.</param>
    /// <param name="skill">The caster's skill.</param>
    public static float BurnRate(float componentLoss, float cdm, uint power, uint skill) =>
        componentLoss * cdm * Math.Min(1.0f, skill == 0 ? 1.0f : (float)power / skill);

    /// <summary>The seconds to rest for <paramref name="deficit"/> mana at <paramref name="manaPerSecond"/>, and one heartbeat more (ACE adds regeneration once a heartbeat).</summary>
    /// <param name="deficit">The mana short.</param>
    /// <param name="manaPerSecond">The regeneration.</param>
    /// <param name="heartbeatSeconds">The heartbeat.</param>
    public static int RestSeconds(uint deficit, double manaPerSecond, double heartbeatSeconds) =>
        manaPerSecond <= 0 ? int.MaxValue : (int)Math.Ceiling((deficit / manaPerSecond) + heartbeatSeconds);

    /// <summary>The first ware the pack lacks for <paramref name="slots"/> (a slot counted once a slot: four tapers need four), or null.</summary>
    /// <param name="pack">The pack.</param>
    /// <param name="slots">The formula.</param>
    public static string? Missing(BuffBotLedger pack, IReadOnlyList<BuffBotSlot> slots)
    {
        foreach (var group in slots.GroupBy(s => s.Wcid))
        {
            var held = group.Key switch
            {
                BuffBotRules.TaperWcid => pack.Tapers,
                BuffBotWire.FeeWcid => pack.Scarabs,
                _ => 0,
            };
            if (held < group.Count())
            {
                return group.First().Name;
            }
        }

        return null;
    }

    /// <summary>One cast (see the class). Applies nothing: <see cref="Apply"/> takes the burns from the pack after the wind-up.</summary>
    /// <param name="pack">The pack.</param>
    /// <param name="slots">The formula the bot casts on (its foci formula).</param>
    /// <param name="skill">The bot's skill in the spell's school.</param>
    /// <param name="power">The spell's power.</param>
    /// <param name="componentLoss">The spell's ComponentLoss.</param>
    /// <param name="manaNow">The bot's mana.</param>
    /// <param name="manaCost">ACE's cost of the cast landing (<c>CalculateManaUsage</c>).</param>
    /// <param name="manaPerSecond">The bot's regeneration.</param>
    /// <param name="heartbeatSeconds">The bot's heartbeat.</param>
    /// <param name="roll">A uniform roll in [0, 1) (ACE's <c>ThreadSafeRandom.Next(0f, 1f)</c>).</param>
    public static BuffBotCastResult Attempt(BuffBotLedger pack, IReadOnlyList<BuffBotSlot> slots, uint skill, uint power, float componentLoss, uint manaNow, uint manaCost, double manaPerSecond, double heartbeatSeconds, Func<double> roll)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(roll);
        var chance = Chance(skill, power);
        if (Missing(pack, slots) is { } missing)
        {
            return new(BuffBotCastKind.NoComponent, 0, 0, missing, [], chance);
        }

        var lands = chance > roll();
        var mana = lands ? manaCost : FizzleMana;
        if (mana > manaNow)
        {
            return new(BuffBotCastKind.NoMana, mana, RestSeconds(mana - manaNow, manaPerSecond, heartbeatSeconds), null, [], chance);
        }

        var burned = new List<BuffBotSlot>();
        foreach (var slot in slots)
        {
            if (roll() < BurnRate(componentLoss, slot.Cdm, power, skill))
            {
                burned.Add(slot);
            }
        }

        return new(lands ? BuffBotCastKind.Landed : BuffBotCastKind.Fizzled, mana, 0, null, burned, chance);
    }

    /// <summary>Takes a cast's burns from the pack.</summary>
    /// <param name="pack">The pack.</param>
    /// <param name="result">The cast.</param>
    public static void Apply(BuffBotLedger pack, BuffBotCastResult result)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(result);
        foreach (var slot in result.Burned)
        {
            if (slot.Wcid == BuffBotRules.TaperWcid)
            {
                pack.Tapers = Math.Max(0, pack.Tapers - 1);
            }
            else if (slot.Wcid == BuffBotWire.FeeWcid)
            {
                pack.Scarabs = Math.Max(0, pack.Scarabs - 1);
            }
        }
    }
}

/// <summary>The trade at the shop: the scarabs sold and what they paid, the tapers bought and what they cost.</summary>
/// <param name="ScarabsSold">The Platinum Scarabs sold (those over <see cref="BuffBotRules.ScarabKeep"/>).</param>
/// <param name="SaleTotal">What the vendor paid for them.</param>
/// <param name="TapersBought">The Prismatic Tapers bought.</param>
/// <param name="BuyTotal">What they cost.</param>
/// <param name="Short">True when the purse did not pay the tapers up to <see cref="BuffBotRules.RestockTo"/>.</param>
internal sealed record BuffBotTrade(int ScarabsSold, long SaleTotal, int TapersBought, long BuyTotal, bool Short);

/// <summary>
/// D138: the bot's trade at Archmage Cindrue by her prices, mirrored from ACE's vendor @95ef426d (Vendor.cs 577-597): she sells at <c>max(1, ceil(SellPrice x Value - 0.1))</c> and buys at
/// <c>max(1, floor(BuyPrice x Value + 0.1))</c>, on a stack's whole value, in float arithmetic. The bot sells the scarabs it holds over its keep, then buys tapers up to <see cref="BuffBotRules.RestockTo"/> with what
/// the purse holds.
/// </summary>
internal static class BuffBotShop
{
    /// <summary>ACE's sell cost of a stack (what the vendor charges).</summary>
    /// <param name="sellRate">The vendor's SellPrice.</param>
    /// <param name="unitValue">The ware's Value.</param>
    /// <param name="count">The stack.</param>
    public static long SellCost(double sellRate, int unitValue, int count) =>
        count <= 0 ? 0 : Math.Max(1, (long)Math.Ceiling(((float)sellRate * ((long)unitValue * count)) - 0.1));

    /// <summary>ACE's buy cost of a stack (what the vendor pays).</summary>
    /// <param name="buyRate">The vendor's BuyPrice.</param>
    /// <param name="unitValue">The ware's Value.</param>
    /// <param name="count">The stack.</param>
    public static long BuyCost(double buyRate, int unitValue, int count) =>
        count <= 0 ? 0 : Math.Max(1, (long)Math.Floor(((float)buyRate * ((long)unitValue * count)) + 0.1));

    /// <summary>The trade (see the class), not yet applied.</summary>
    /// <param name="pack">The pack and purse.</param>
    /// <param name="sellRate">The vendor's SellPrice.</param>
    /// <param name="taperValue">The taper's Value.</param>
    /// <param name="buyRate">The vendor's BuyPrice.</param>
    /// <param name="scarabValue">The scarab's Value.</param>
    /// <param name="buysScarabs">True when the vendor buys the scarab's item type.</param>
    public static BuffBotTrade Plan(BuffBotLedger pack, double sellRate, int taperValue, double buyRate, int scarabValue, bool buysScarabs)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var sold = buysScarabs ? Math.Max(0, pack.Scarabs - BuffBotRules.ScarabKeep) : 0;
        var paid = BuyCost(buyRate, scarabValue, sold);
        var purse = pack.Pyreals + paid;
        var want = Math.Max(0, BuffBotRules.RestockTo - pack.Tapers);
        var n = want;
        while (n > 0 && SellCost(sellRate, taperValue, n) > purse)
        {
            n = (int)Math.Min(n - 1, Math.Max(0, (long)Math.Floor(purse / Math.Max(1e-9, sellRate * taperValue))));
        }

        return new BuffBotTrade(sold, paid, n, SellCost(sellRate, taperValue, n), n < want);
    }

    /// <summary>Applies the trade to the pack and purse.</summary>
    /// <param name="pack">The pack and purse.</param>
    /// <param name="trade">The trade.</param>
    public static void Apply(BuffBotLedger pack, BuffBotTrade trade)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(trade);
        pack.Scarabs -= trade.ScarabsSold;
        pack.Pyreals += trade.SaleTotal - trade.BuyTotal;
        pack.Tapers += trade.TapersBought;
    }
}
