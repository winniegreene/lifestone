using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 7 AR: the ground's keep-out (<see cref="GroundHunt"/>): under the profile's level the pick, the tour and the schedule leave its cells alone. The emergency, the drain and the fight back keep the ground's whole filter: an attacker from a kept-out cell is fought (M5 D88).
/// </summary>
public sealed partial class FightScenario
{
    /// <summary>The seconds a drink is waited for before the emergency goes on without it (Lifestone's own: live, a drink from the magic stance ends 2.5 s after its send, 3.5 s behind a cast).</summary>
    private const double EmergencyDrinkSeconds = 4;

    /// <summary>M6 14 T: the drinks in a row that restored nothing after which the health drinks are set aside (Lifestone's own).</summary>
    private const int EmergencyDrinkFails = 2;

    /// <summary>M6 14 T: the seconds the health drinks are set aside after <see cref="EmergencyDrinkFails"/> drinks in a row restored nothing (Lifestone's own): the heal decides alone, then the drinks are tried again.</summary>
    private const double EmergencyDrinkAsideSeconds = 10;

    /// <summary>M6 14 T: the seconds without an emergency after which the next one logs its call again (Lifestone's own: a cast and its hit wait are under it).</summary>
    private const double EmergencyDrinkNoteSeconds = 8;

    private CellSet _keptOut = CellSet.Empty;
    private readonly List<uint> _drinkScratch = [];
    private HealHandle? _emergencyDrink;
    private double _emergencyDrinkAt;
    private uint _emergencyDrinkHealth;
    private HealthDrink _emergencyDrinkKind;
    private int _emergencyDrinkFailsInARow;
    private double _emergencyDrinkAsideUntil = double.NegativeInfinity;
    private HealthDrinkCall? _emergencyDrinkNoted;
    private double _emergencyDrinkAskedAt = double.NegativeInfinity;
    private int _emergencyDrinks;
    private long _emergencyDrinkRestored;

    /// <summary>M6 14 T: the health drinks the emergency drank that restored health (<c>health_drinks=</c> in the drink's log line).</summary>
    public int EmergencyHealthDrinks => _emergencyDrinks;

    /// <summary>M6 14 T: the health those drinks restored, by ACE's own lines.</summary>
    public long EmergencyHealthRestored => _emergencyDrinkRestored;

    /// <summary>M6 7 AR: the farthest corpse the loot step takes: <c>--fight-loot-radius-m</c> when it was set, else the ground's <c>loot.radius_m</c>, else the option's default.</summary>
    private double LootRadius => _options.LootRadiusMetres.Equals(FightOptions.DefaultLootRadiusMetres) && _ground.Loot is { RadiusMetres: > 0 } loot ? loot.RadiusMetres : _options.LootRadiusMetres;

    /// <summary>
    /// M6 7 AR (the audit's row 13: the keeper's in-fight potion is the hunt's, her Health Draught sat in the pack), M6 14 T (the owner: carry health drinks): the emergency's first question, before any heal that costs mana. Check, act, check:
    /// <list type="number">
    /// <item>before: the pack is read for every health drink (<see cref="HealthDrinks.All"/>) and the one that fits is picked (<see cref="HealthDrinks.Fitting"/>: none that would overflow, the biggest the missing health takes); the mana is read
    /// against the cheapest heal the fight offers and the recent hits against the health (<see cref="HealthDrinks.Emergency"/>): a drink goes when the mana pays no heal, when the next hit kills or when the hits of the last seconds would kill her before one heal could land (<see cref="HealthDrinks.IsDesperate"/>), else the heal goes first and the drinks are kept
    /// (logged when the call changes);</item>
    /// <item>act: one drink is sent;</item>
    /// <item>after (<see cref="SettleEmergencyDrink"/>, in the tick the drink ends): its end is logged with the health before and after and what ACE's own line says it restored. A drink that restored nothing is named;
    /// <see cref="EmergencyDrinkFails"/> in a row set the drinks aside for <see cref="EmergencyDrinkAsideSeconds"/> s (the heal decides alone), so a refused drink is never a loop.</item>
    /// </list>
    /// True while the drink is in flight.
    /// </summary>
    private bool EmergencyPotionStep(IGameSessionView session, double clock, uint health, uint healthMax)
    {
        var c = CultureInfo.InvariantCulture;
        if (_emergencyDrink is { } drink)
        {
            if (clock - _emergencyDrinkAt < EmergencyDrinkSeconds)
            {
                return true; // in flight: its end settles it at once (SettleEmergencyDrink), whether or not the emergency still holds then
            }

            SettleEmergencyDrink(drink, session); // no answer: named, and the emergency goes on without it
            return false;
        }

        if (clock < _emergencyDrinkAsideUntil)
        {
            return false;
        }

        if (clock - _emergencyDrinkAskedAt > EmergencyDrinkNoteSeconds)
        {
            _emergencyDrinkNoted = null; // an emergency of its own: its call is logged again
        }

        _emergencyDrinkAskedAt = clock;
        var missing = healthMax > health ? healthMax - health : 0u;
        var fits = HealthDrinks.Find(session.State, _drinkScratch, missing, out var potion, out var kind, out var count, out var heldAll);
        if (heldAll == 0)
        {
            _emergencyDrinkNoted = null;
            return false;
        }

        var read = Picker.Rates.Read(clock);
        var nextHitKills = read.NextHitKills(health);
        var desperate = HealthDrinks.IsDesperate(health, read.PerSecond);
        var mana = session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var held, out _) ? held : 0u;
        var paid = EmergencyHealPaid(session, clock, mana, nextHitKills, out var cheapest);
        var call = HealthDrinks.Emergency(heldAll, fits, paid, nextHitKills, desperate);
        if (!HealthDrinks.Drinks(call))
        {
            if (_emergencyDrinkNoted != call)
            {
                _emergencyDrinkNoted = call;
                _log.Info(LogCategory, string.Create(c, $"emergency: potion: none drunk at health {health} of {healthMax} ({missing} missing), mana {mana}, {heldAll} health drink(s) held: {HealthDrinks.Words(call)}{(cheapest == uint.MaxValue ? string.Empty : string.Create(c, $" (the cheapest heal offered costs {cheapest})"))}"));
            }

            return false;
        }

        _emergencyDrinkNoted = null;
        _log.Info(LogCategory, string.Create(c, $"emergency: potion: check before: {kind.Name} 0x{potion:X8} (+{kind.Boost}, {count} held of {heldAll} health drink(s)) at health {health} of {healthMax} ({missing} missing: the biggest that fits), mana {mana}{(cheapest == uint.MaxValue ? ", no heal offered" : string.Create(c, $", the cheapest heal offered costs {cheapest}"))}, incoming {read.PerSecond:0.0} a second: {HealthDrinks.Words(call)}"));
        var sent = session.Actions.Drink(potion, ActionSource.Automation);
        (_emergencyDrink, _emergencyDrinkAt, _emergencyDrinkHealth, _emergencyDrinkKind) = (sent, clock, health, kind);
        if (sent.IsCompleted)
        {
            SettleEmergencyDrink(sent, session); // refused at once
            return false;
        }

        sent.Completed += _ => SettleEmergencyDrink(sent, session); // the check after runs in the tick the drink ends, also when that drink ended the emergency
        return true;
    }

    /// <summary>
    /// M6 14 T: the check after a drink, once for each drink: what ACE's own line says it restored (the vitals alone cannot say: hits land meanwhile), the health before and now. A drink that restored nothing is named;
    /// <see cref="EmergencyDrinkFails"/> in a row set the drinks aside for <see cref="EmergencyDrinkAsideSeconds"/> s.
    /// </summary>
    private void SettleEmergencyDrink(HealHandle drink, IGameSessionView session)
    {
        if (!ReferenceEquals(drink, _emergencyDrink))
        {
            return; // settled already (its wait ran out first)
        }

        var c = CultureInfo.InvariantCulture;
        _emergencyDrink = null;
        var now = session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var healthMax) ? string.Create(c, $"{health} of {healthMax}") : "unread";
        var restored = drink.Result is { Kind: HealResultKind.Drank, Amount: > 0 } result ? result.Amount : 0;
        var outcome = drink.Outcome?.Kind.ToString() ?? string.Create(c, $"no answer in {EmergencyDrinkSeconds:0} s");
        if (restored > 0)
        {
            _emergencyDrinkFailsInARow = 0;
            _emergencyDrinks++;
            _emergencyDrinkRestored += restored;
            _log.Info(LogCategory, string.Create(c, $"emergency: potion: check after: {outcome}: the {_emergencyDrinkKind.Name} restored {restored} health (ACE's line); health {_emergencyDrinkHealth} then {now} with the hits taken meanwhile; health_drinks={_emergencyDrinks} restored={_emergencyDrinkRestored}"));
            return;
        }

        _emergencyDrinkFailsInARow++;
        var aside = _emergencyDrinkFailsInARow >= EmergencyDrinkFails;
        _log.Warn(LogCategory, string.Create(c, $"emergency: potion: check after: {outcome}: the {_emergencyDrinkKind.Name} restored nothing ({drink.Outcome?.Detail ?? "no UseDone"}); health {_emergencyDrinkHealth} then {now}; {_emergencyDrinkFailsInARow} in a row{(aside ? string.Create(c, $": the health drinks are set aside {EmergencyDrinkAsideSeconds:0} s and the heal decides alone") : ": the heal goes now and the drink is tried again")}"));
        if (aside)
        {
            (_emergencyDrinkFailsInARow, _emergencyDrinkAsideUntil) = (0, session.Clock.Now + EmergencyDrinkAsideSeconds);
        }
    }

    /// <summary>
    /// M6 14 T: true when the mana pays a heal the emergency may cast now: the cheapest of the offered Drain Health Other levels and Heal Self (their ceilings, as the emergency reads them) is held, and the way out's rule does not keep that mana
    /// for the recall (<c>WayOutSparesDrain</c>, which changes nothing). <paramref name="cheapest"/> is that cost, or <see cref="uint.MaxValue"/> when no heal is offered (not in the book, or not paid for by the pack).
    /// </summary>
    private bool EmergencyHealPaid(IGameSessionView session, double clock, uint mana, bool nextHitKills, out uint cheapest)
    {
        cheapest = _drain.HasHeal ? _drain.HealCeiling : uint.MaxValue;
        var levels = _drain.Levels;
        for (var i = 0; i < levels.Length; i++)
        {
            cheapest = Math.Min(cheapest, GearCasts.ManaCeiling(levels[i].Info, 0));
        }

        return cheapest != uint.MaxValue && mana >= cheapest && !WayOutSparesDrain(session, clock, mana, cheapest, nextHitKills);
    }

    /// <summary>M6 7 AY: the cells the hunt leaves alone: the drop-back's list while she is held (it holds the keep-out too), else the keep-out while the level holds it, else none.</summary>
    /// <param name="hunt">The ground's data.</param>
    /// <param name="levelHolds">The keep-out holds at her level.</param>
    /// <param name="packHold">The fight is held to the first slots.</param>
    internal static CellSet HeldOut(GroundHunt hunt, bool levelHolds, bool packHold) => packHold && hunt.HasDropBack ? hunt.DropBack : levelHolds ? hunt.KeepOut : CellSet.Empty;

    /// <summary>Check, act, check: reads the level, sets the keep-out on the pick and the tour, logs what it holds out. A level that cannot be read keeps the hall out: the safer guess, and it is logged. A wanted trophy whose holder stands in the keep-out is a loud TODO: the plain hunt goes on.</summary>
    private void ApplyHuntKeepOut(IGameSessionView session)
    {
        if (_ground.Hunt is not { } hunt || _options.ArenaHere)
        {
            return;
        }

        var c = CultureInfo.InvariantCulture;
        var known = session.State.Self.Qualities.TryGetInt(PlayerQualities.LevelProperty, out var level) && level > 0;
        var on = !known || hunt.HoldsAt(level);
        var held = _options.PackHold && hunt.HasDropBack;
        _keptOut = HeldOut(hunt, on, _options.PackHold);
        if (held)
        {
            _log.Info(LogCategory, string.Create(c, $"hunt: drop-back on: {hunt.DropBack.Cells.Count} cell(s) of {_ground.Name} out of the hunt: she holds to the first slots after two deaths to big packs; an attacker from there is fought (M5 D88)"));
        }

        if (_walk?.Tour is GroundTour tour)
        {
            tour.KeepOut(_keptOut);
            _log.Info(LogCategory, string.Create(c, $"hunt: keep-out {(on ? "on" : "off")}: level {(known ? level.ToString(c) : "unknown")}, the line {hunt.UnderLevel}: {hunt.KeepOut.Cells.Count} cell(s) of {_ground.Name}; {tour.Schedule.HuntedCount} of {tour.Schedule.Count} slot(s) hunted; an attacker from there is fought (M5 D88)"));
        }
        else
        {
            _log.Info(LogCategory, string.Create(c, $"hunt: keep-out {(on ? "on" : "off")}: level {(known ? level.ToString(c) : "unknown")}, the line {hunt.UnderLevel}: {hunt.KeepOut.Cells.Count} cell(s) of {_ground.Name}"));
        }

        foreach (var pull in hunt.Pulls)
        {
            if (on && _options.Want.Contains(pull.Item) && pull.Stand is not null)
            {
                BeginPull(session, pull); // M6 7 AX
            }
            else if (on && _options.Want.Contains(pull.Item))
            {
                _log.Warn(LogCategory, string.Create(c, $"TODO(m6-tusk-pull): {pull.Name} {pull.Item} is wanted, its chosen holders stand in the keep-out ({string.Join(", ", pull.Cells.Select(x => $"0x{x:X8}"))}) and no pull is built (a stand point and one step that shoots from it): the plain hunt goes on, the trophy waits"));
            }
        }
    }
}
