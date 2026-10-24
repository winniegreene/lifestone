using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// What a hunt counts (M4 1.2 s4a). It is a tally of its own and does not reuse <see cref="FightTally"/>: that one reads a mage's damage out of the Magic chat lines, and a melee or missile blow
/// has no such line (ACE sends the blow as the combat notice 0x01B1, the evade as 0x01B3, the arrow that struck the scenery as a chat line); only the kills and the experience are the same reads.
/// A bus subscriber (<see cref="Start"/>, <see cref="Dispose"/>), allocation-free per message apart from the chat line's search.
/// </summary>
/// <remarks>
/// <b>Swings and shots</b> are the attack sequence's (<see cref="AttackChanged"/>): the server-driven run of swings or launches that one send starts, summed over the sequences of the run
/// (capture.md C4: <c>swings</c> counts the player's own swing motions and <c>shots</c> the arrows launched, not the sends). <b>Hits</b> are 0x01B1 lines the player dealt, <b>misses</b> the 0x01B3 lines
/// (the target evaded) plus the "hit the environment" lines of a shot that missed the target. <b>Damage taken</b> is the target's blows on the player (0x01B2) plus their spells' Magic-channel lines (<see cref="SpellDamageTaken"/>, as <see cref="FightTally"/> sums them); <b>hits taken</b> and <b>evades</b> count the 0x01B2 and 0x01B4 notices.
/// </remarks>
public sealed class HuntTally : IDisposable
{
    private const string EnvironmentText = "hit the environment";

    private readonly List<IDisposable> _subscriptions = [];
    private IGameSessionView? _view;
    private int _sequence = -1;
    private int _sequenceSwings;
    private int _sequenceShots;
    private int _bankedSwings;
    private int _bankedShots;
    private long _xpBaseline;
    private bool _xpBaselinePending;

    /// <summary>The kills (0x01AD kill notices).</summary>
    public int Kills { get; private set; }

    /// <summary>The player's swings over the run's attack sequences.</summary>
    public int Swings => _bankedSwings + _sequenceSwings;

    /// <summary>The arrows launched over the run's attack sequences.</summary>
    public int Shots => _bankedShots + _sequenceShots;

    /// <summary>The blows the player landed (0x01B1).</summary>
    public int Hits { get; private set; }

    /// <summary>The blows that did not land: 0x01B3 and the arrows that hit the environment.</summary>
    public int Misses { get; private set; }

    /// <summary>The arrows that hit the environment (the "hit the environment" chat lines; <c>blocked</c>, M4 P4 bow, report-only in its first gate). They are also counted in <see cref="Misses"/>.</summary>
    public int Blocked { get; private set; }

    /// <summary>The environment misses since the last hit or evade (the walk-on rule's count, <see cref="HuntScenario.BlockedBeforeWalking"/>).</summary>
    public int BlockedRun { get; private set; }

    /// <summary>The times the hunt sent the player to a creature the client already knew (<c>seeks</c>, M4 P4 seek; report-only in its first gate, printed after <c>blocked=</c>).</summary>
    public int Seeks { get; private set; }

    /// <summary>Counts one seek (<see cref="Seeks"/>).</summary>
    public void NoteSeek() => Seeks++;

    /// <summary>Called with one line per shot or blow outcome (<c>shot &lt;n&gt;: hit|evaded|environment ...</c>); the hunt logs it at INFO. Null: nothing is told.</summary>
    public Action<string>? OutcomeLogged { get; set; }

    private int _outcomes;

    /// <summary>Zeroes the environment run (a new target or a new cell starts a new count).</summary>
    public void ClearBlockedRun() => BlockedRun = 0;

    /// <summary>The damage the player's blows did, from the 0x01B1 lines (the killing blow has none: 0x01AD).</summary>
    public long DamageDealt { get; private set; }

    /// <summary>The damage the player took: the 0x01B2 blows plus the creatures' spell damage (<see cref="SpellDamageTaken"/>).</summary>
    public long DamageTaken { get; private set; }

    /// <summary>The part of <see cref="DamageTaken"/> that came from Magic-channel "... you for N points" lines (a creature's spell); <see cref="HitsTaken"/> counts the 0x01B2 notices only.</summary>
    public long SpellDamageTaken { get; private set; }

    /// <summary>The blows that hit the player.</summary>
    public int HitsTaken { get; private set; }

    /// <summary>The blows the player evaded (0x01B4).</summary>
    public int Evades { get; private set; }

    /// <summary>The experience gained since <see cref="Start"/> (or <see cref="Reset"/>).</summary>
    public long XpGained { get; private set; }

    /// <summary>The items the hunt picked up from its kills' corpses (the PickUps whose outcome was Confirmed; <c>looted</c>, M4 2.2 s0). Counted by the scenario's loot loop, not by a bus subscriber; not in <see cref="Fields"/> (the leg prints it).</summary>
    public int Looted { get; private set; }

    /// <summary>The corpse entries the loot rule left (<c>loot_skipped</c>, M4 2.2 s0; every reason: value, type, burden, held, unresolved).</summary>
    public int LootSkipped { get; private set; }

    /// <summary>Adds a finished (or abandoned) loot loop's counts.</summary>
    /// <param name="looted">The moves confirmed.</param>
    /// <param name="skipped">The entries the rule left.</param>
    public void AddLoot(int looted, int skipped)
    {
        Looted += looted;
        LootSkipped += skipped;
    }

    /// <summary>The items the loop's end sold to the shopkeeper (<c>sold</c>, M4 3.2 s4a: stacks the vendor took, counted from the inventory after each sale). Counted by the scenario, not by a bus subscriber; not in <see cref="Fields"/>.</summary>
    public int Sold { get; private set; }

    /// <summary>The Handy Healing Kits the loop's end bought (<c>bought</c>, M4 3.2 s4a: kits held after the purchase less before). Counted by the scenario; not in <see cref="Fields"/>.</summary>
    public int Bought { get; private set; }

    /// <summary>The pyreals the sale paid: the coins after the sale less the coins before it (<c>pyreals</c>, M4 3.2 s4a; the purchase's price is not in it, the detail line has it). Not in <see cref="Fields"/>.</summary>
    public int Pyreals { get; private set; }

    /// <summary>The looted items whose Identify was answered with the tables (<c>appraised</c>, M4 3.2 s4a, <see cref="AppraisalTally.Appraised"/>); not in <see cref="Fields"/>.</summary>
    public int Appraised { get; private set; }

    /// <summary>Adds a finished sale's counts.</summary>
    /// <param name="sold">The stacks the vendor took.</param>
    /// <param name="pyreals">The coin delta of the sale.</param>
    public void AddSale(int sold, int pyreals)
    {
        Sold += sold;
        Pyreals += pyreals;
    }

    /// <summary>Adds a finished purchase's count.</summary>
    /// <param name="bought">The kits that arrived.</param>
    public void AddBought(int bought) => Bought += bought;

    /// <summary>Sets the answered appraisals (the scenario's <see cref="AppraisalTally"/>).</summary>
    /// <param name="appraised">The count.</param>
    public void SetAppraised(int appraised) => Appraised = appraised;

    /// <summary>True between <see cref="Start"/> and <see cref="Dispose"/>.</summary>
    public bool Running => _view is not null;

    /// <summary>Starts counting on a session (disposes an earlier subscription).</summary>
    public void Start(IGameSessionView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        Dispose();
        _view = view;
        _subscriptions.Add(view.Events.Subscribe<PlayerKilledSomething>(OnKill));
        _subscriptions.Add(view.Events.Subscribe<CombatEvent>(OnCombat));
        _subscriptions.Add(view.Events.Subscribe<AttackChanged>(OnAttack));
        _subscriptions.Add(view.Events.Subscribe<ChatReceived>(OnChat));
        _subscriptions.Add(view.Events.Subscribe<QualitiesChanged>(OnQualities));
        TakeBaseline();
    }

    /// <summary>Stops counting.</summary>
    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        _view = null;
    }

    /// <summary>Zeroes every count and takes a new experience baseline.</summary>
    public void Reset()
    {
        Kills = 0;
        Hits = 0;
        Misses = 0;
        Blocked = 0;
        BlockedRun = 0;
        Seeks = 0;
        _outcomes = 0;
        DamageDealt = 0;
        DamageTaken = 0;
        SpellDamageTaken = 0;
        HitsTaken = 0;
        Evades = 0;
        Looted = 0;
        LootSkipped = 0;
        Sold = 0;
        Bought = 0;
        Pyreals = 0;
        Appraised = 0;
        _bankedSwings = _bankedShots = _sequenceSwings = _sequenceShots = 0;
        _sequence = -1;
        TakeBaseline();
    }

    /// <summary>The counters as verdict fields (no spaces inside a value): <c>kills swings shots hits misses blocked seeks damage_dealt damage_taken hits_taken evades xp_gained</c>.</summary>
    public string Fields() =>
        string.Create(CultureInfo.InvariantCulture, $"kills={Kills} swings={Swings} shots={Shots} hits={Hits} misses={Misses} blocked={Blocked} seeks={Seeks} damage_dealt={DamageDealt} damage_taken={DamageTaken} hits_taken={HitsTaken} evades={Evades} xp_gained={XpGained}");

    private void TakeBaseline()
    {
        XpGained = 0;
        if (_view is null)
        {
            return;
        }

        if (_view.State.Self.Qualities.TryGetInt64(PlayerQualities.TotalExperienceProperty, out var total))
        {
            _xpBaseline = total;
            _xpBaselinePending = false;
        }
        else
        {
            _xpBaseline = 0;
            _xpBaselinePending = true; // the description has not arrived: the first total seen is the baseline
        }
    }

    private void OnKill(in PlayerKilledSomething e) => Kills++;

    private void OnCombat(in CombatEvent e)
    {
        switch (e.Kind)
        {
            case CombatEventKind.AttackLanded:
                Hits++;
                BlockedRun = 0;
                DamageDealt += e.Damage;
                Outcome($"hit for {e.Damage} (0x01B1)");
                break;
            case CombatEventKind.AttackEvaded:
                Misses++;
                BlockedRun = 0; // an evade is the target's dodge: the arrow reached it
                Outcome("evaded (0x01B3)");
                break;
            case CombatEventKind.DefenseHit:
                HitsTaken++;
                DamageTaken += e.Damage;
                break;
            case CombatEventKind.DefenseEvaded:
                Evades++;
                break;
        }
    }

    private void OnAttack(in AttackChanged e)
    {
        if (e.Number != _sequence)
        {
            _bankedSwings += _sequenceSwings;
            _bankedShots += _sequenceShots;
            _sequence = e.Number;
        }

        _sequenceSwings = e.Swings;
        _sequenceShots = e.Shots;
    }

    private void OnChat(in ChatReceived e)
    {
        if (e.Line.ChatType == FightTally.MagicChatType && e.Line.Kind == ChatKind.System && FightTally.Classify(e.Line.Text, out var amount, out _) == MagicLineKind.DamageTaken)
        {
            // A creature's spell ("X bashes you for 32 points with Shock Wave II.") is a Magic-channel line, not a 0x01B2: the live melee run of 2026-10-20 lost 32 of its 64 points this way (review 1.2-R3).
            SpellDamageTaken += amount;
            DamageTaken += amount;
            return;
        }

        if (e.Line.Text.Contains(EnvironmentText, StringComparison.OrdinalIgnoreCase))
        {
            Misses++;
            Blocked++;
            BlockedRun++;
            Outcome($"environment (chat: {e.Line.Text.Trim()}); {BlockedRun} in a row");
        }
    }

    private void Outcome(string what) => OutcomeLogged?.Invoke(string.Create(CultureInfo.InvariantCulture, $"shot {++_outcomes}: {what}"));

    private void OnQualities(in QualitiesChanged e)
    {
        if (_view is null || (e.Change != QualityChange.Description && !(e.Change == QualityChange.Int64 && e.Key == PlayerQualities.TotalExperienceProperty)))
        {
            return;
        }

        if (!_view.State.Self.Qualities.TryGetInt64(PlayerQualities.TotalExperienceProperty, out var total))
        {
            return;
        }

        if (_xpBaselinePending)
        {
            _xpBaseline = total;
            _xpBaselinePending = false;
        }

        XpGained = total - _xpBaseline;
    }
}
