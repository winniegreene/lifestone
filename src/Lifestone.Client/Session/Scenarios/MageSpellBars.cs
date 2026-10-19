using System.Collections.Immutable;
using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>One change to a favourite bar: take a spell off, or put it at an index (<see cref="MageSpellBars.Plan"/>).</summary>
/// <param name="Remove">True for RemoveSpellFavorite (0x01E4), false for AddSpellFavorite (0x01E3).</param>
/// <param name="Spell">The spell id.</param>
/// <param name="Index">The position on the bar for an add (0 for a remove).</param>
/// <param name="Bar">The bar (0 is the first spell tab).</param>
public readonly record struct BarStep(bool Remove, uint Spell, int Index, int Bar);

/// <summary>
/// The BSD mage's spell bars (M3 4.2 p1, the owner's P1): tab I (bar 0) holds the buffs, tab II (bar 1) the fight, in the owner's order; every other bar is left as it is (tab III, bar 2, keeps what ACE holds, the Flame Bolt
/// bars of the earlier plan on a character that ran it). ACE keeps a spell already on a bar where it is (<c>AddSpellToBar</c>), so an exact layout needs removals: <see cref="Plan"/> keeps the longest prefix of a bar that
/// already matches, removes the rest and adds what is missing at its index, as a pure function over <see cref="Spellbook.SpellBars"/>; a second run plans nothing (<c>bars=ok</c>). A spell the book lacks is left off and logged by
/// name. Used by <see cref="DeathScenario"/>'s <c>bars</c> stage and by <see cref="BuffScenario"/>'s start, for BSD Mage only (the scenarios' own character guard). One step at a time, by the Action API.
/// </summary>
public sealed class MageSpellBars
{
    /// <summary>The log category.</summary>
    public const string LogCategory = "bars";

    /// <summary>Tab I: the buffs.</summary>
    public const int BuffBar = 0;

    /// <summary>Tab II: the fight.</summary>
    public const int FightBar = 1;

    /// <summary>The limit on the spellbook (and its bars) to arrive and the changes to be sent, in seconds.</summary>
    public const double Seconds = 30;

    /// <summary>Fire Vulnerability Other VII (the dat's "Inferno's Gift"; the dat titles level VII lines, so the id is pinned by its category and power in <c>MageSpellBarsDatTests</c>).</summary>
    public const uint FireVulnerabilityOtherVii = 2170;

    /// <summary>Flame Bolt VII ("Ilservian's Flame").</summary>
    public const uint FlameBoltVii = 2128;

    /// <summary>Revitalize Self VII ("Robustification").</summary>
    public const uint RevitalizeSelfVii = 2083;

    /// <summary>Stamina to Mana Self VII ("Meditative Trance").</summary>
    public const uint StaminaToManaSelfVii = 2345;

    /// <summary>Heal Self VII ("Adja's Intervention").</summary>
    public const uint HealSelfVii = 2073;

    private static readonly string[] OpeningLines = ["Focus Self", "Willpower Self", "Creature Enchantment Mastery Self"];

    private readonly ILogSink _log;
    private readonly List<BarStep> _plan = [];
    private ActionHandle? _pending;
    private bool _planned;
    private int _next;
    private int _changes;
    private double _start = double.NaN;

    /// <summary>The bars step.</summary>
    /// <param name="log">Where the plan and what is left off are logged.</param>
    public MageSpellBars(ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <summary>What the step ended with: <c>set:&lt;n&gt;</c> (n changes sent) or <c>ok</c> (nothing to send); <c>n/a</c> before it ends.</summary>
    public string Note { get; private set; } = "n/a";

    /// <summary>Tab I as the read model held it when the step succeeded (empty until then).</summary>
    public ImmutableArray<uint> BuffTabAtEnd { get; private set; } = [];

    /// <summary>Tab II as the read model held it when the step succeeded (empty until then).</summary>
    public ImmutableArray<uint> FightTabAtEnd { get; private set; } = [];

    /// <summary>Why the step failed (null while it has not).</summary>
    public string? Failure { get; private set; }

    /// <summary>Tab I as the owner asks: Focus, Willpower and Creature Enchantment Mastery at VI, then every buff line at VII in the buff cycle's order.</summary>
    public static IReadOnlyList<uint> BuffTab { get; } = BuildBuffTab();

    /// <summary>Tab II as the owner asks: Fire Vulnerability Other VII, Flame Bolt VII, Revitalize Self VII, Stamina to Mana Self VII, Heal Self VII.</summary>
    public static IReadOnlyList<uint> FightTab { get; } = [FireVulnerabilityOtherVii, FlameBoltVii, RevitalizeSelfVii, StaminaToManaSelfVii, HealSelfVii];

    /// <summary>
    /// The changes that make tabs I and II exactly the owner's lists (spells <paramref name="book"/> lacks left off, logged by name): for each bar the longest prefix that already matches stays, every later spell
    /// on the bar is removed (last first), then the missing spells are added at their index in order. Nothing for a bar that already matches.
    /// </summary>
    /// <param name="book">The spellbook with its bars (from the PlayerDescription).</param>
    /// <param name="log">Where a spell left off is logged, or null.</param>
    public static IReadOnlyList<BarStep> Plan(Spellbook book, ILogSink? log = null)
    {
        ArgumentNullException.ThrowIfNull(book);
        var plan = new List<BarStep>();
        PlanBar(book, BuffBar, BuffTab, plan, log);
        PlanBar(book, FightBar, FightTab, plan, log);
        return plan;
    }

    /// <summary>One step of the bars: waits for the spellbook, plans once, then sends one change at a time and waits for its outcome.</summary>
    /// <param name="session">The session.</param>
    /// <param name="clock">The session clock's time.</param>
    /// <returns>Running; Succeeded when the bars are as asked (<see cref="Note"/>); Failed with <see cref="Failure"/>.</returns>
    public ScenarioStatus Step(IGameSessionView session, double clock)
    {
        if (double.IsNaN(_start))
        {
            _start = clock;
        }

        if (_pending is { Outcome: { } sent })
        {
            _pending = null;
            if (sent.Kind != ActionOutcomeKind.Sent)
            {
                Failure = "the change was not sent: " + sent.Kind + (sent.Detail is null ? string.Empty : ": " + sent.Detail);
                return ScenarioStatus.Failed;
            }

            _changes++;
        }

        if (!_planned)
        {
            var book = session.State.Self.Spellbook;
            if (!book.IsKnown)
            {
                if (clock - _start > Seconds)
                {
                    Failure = "the spellbook (and its bars) did not arrive with the PlayerDescription";
                    return ScenarioStatus.Failed;
                }

                return ScenarioStatus.Running;
            }

            _plan.AddRange(Plan(book, _log));
            _planned = true;
        }

        if (_pending is null && _next < _plan.Count)
        {
            var step = _plan[_next++];
            _pending = step.Remove
                ? session.Actions.RemoveSpellFavorite(step.Spell, step.Bar, ActionSource.Host)
                : session.Actions.AddSpellFavorite(step.Spell, step.Index, step.Bar, ActionSource.Host);
            return ScenarioStatus.Running;
        }

        if (_pending is not null)
        {
            return ScenarioStatus.Running;
        }

        BuffTabAtEnd = session.State.Self.Spellbook.Bar(BuffBar);
        FightTabAtEnd = session.State.Self.Spellbook.Bar(FightBar);
        Note = _changes > 0 ? "set:" + _changes.ToString(CultureInfo.InvariantCulture) : "ok";
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"the spell bars: {_changes} change(s) to tab I and tab II ({Note})"));
        return ScenarioStatus.Succeeded;
    }

    private static List<uint> BuildBuffTab()
    {
        var tab = new List<uint>();
        foreach (var name in OpeningLines)
        {
            tab.Add((ArenaProfile.LineNamed(name) ?? throw new InvalidOperationException("the profile has no line " + name)).Spells[5].Id); // VI
        }

        foreach (var line in BuffCycle.OrderedLines)
        {
            tab.Add(line.Spells[6].Id); // VII
        }

        return tab;
    }

    private static void PlanBar(Spellbook book, int bar, IReadOnlyList<uint> wanted, List<BarStep> plan, ILogSink? log)
    {
        var target = new List<uint>(wanted.Count);
        foreach (var id in wanted)
        {
            if (book.Knows(id))
            {
                target.Add(id);
            }
            else
            {
                log?.Warn(LogCategory, "bars: " + NameOf(id) + " is not in the spellbook; it is left off tab " + (bar + 1).ToString(CultureInfo.InvariantCulture));
            }
        }

        ImmutableArray<uint> current = book.Bar(bar);
        var keep = 0;
        while (keep < current.Length && keep < target.Count && current[keep] == target[keep])
        {
            keep++;
        }

        for (var i = current.Length - 1; i >= keep; i--)
        {
            plan.Add(new BarStep(true, current[i], 0, bar));
        }

        for (var i = keep; i < target.Count; i++)
        {
            plan.Add(new BarStep(false, target[i], i, bar));
        }
    }

    private static string NameOf(uint id) => ArenaProfile.Spell(id)?.Name ?? id switch
    {
        FireVulnerabilityOtherVii => "Fire Vulnerability Other VII",
        HealSelfVii => "Heal Self VII",
        _ => "spell " + id.ToString(CultureInfo.InvariantCulture),
    };
}
