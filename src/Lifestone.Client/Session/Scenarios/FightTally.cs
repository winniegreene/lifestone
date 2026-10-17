using System.Globalization;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// What a fight did, counted from the session bus (M3 3.1 s0; plan section 4.7; spec section 5.4, "Combat lines"): the
/// casts the scenario sent, the spell-damage lines the server wrote for the player's own casts and for what hit the
/// player, the kills (GameEvent 0x01AD <see cref="PlayerKilledSomething"/>), the melee hits taken (0x01B2) and evades
/// (0x01B4), and the experience gained (the 0x02CF total-experience delta). <see cref="Start"/> subscribes and takes the
/// baselines; <see cref="Dispose"/> unsubscribes.
/// </summary>
/// <remarks>
/// <para><b>The server's lines.</b> ACE writes a spell projectile's damage in <c>SpellProjectile.cs</c> (vendor-eval,
/// lines 810-845), through <c>GameMessageSystemChat</c> with <c>ChatMessageType.Magic</c> (0x07, ACE.Entity
/// <c>ChatMessageType.cs</c> line 98); the reducer keeps that value in <see cref="ChatLine.ChatType"/> (F7E0
/// <c>OnServerMessage</c>) and publishes <see cref="ChatReceived"/>. The forms, each with the optional prefixes
/// "Critical hit! ", "Overpower! " and "Sneak Attack! " (in that order) and the optional suffixes the server appends after
/// the full stop:
/// "You &lt;verb&gt; &lt;target&gt; for &lt;n&gt; points with &lt;spell&gt;." (dealt), "With &lt;spell&gt; you drain &lt;n&gt; points of
/// &lt;vital&gt; from &lt;target&gt;." (dealt, stamina or mana: counted in <see cref="DrainDealt"/>, not in
/// <see cref="DamageDealt"/>), "&lt;source&gt; &lt;verbs&gt; you for &lt;n&gt; points with &lt;spell&gt;." (taken) and
/// "&lt;source&gt; casts &lt;spell&gt; and drains &lt;n&gt; points of your &lt;vital&gt;." (taken, <see cref="DrainTaken"/>). A
/// Magic line of any other shape (a resist, a cast notice, a buff expiry) is counted in <see cref="UnparsedMagicLines"/>
/// and otherwise ignored, never guessed.</para>
/// <para><b>The killing blow has no line.</b> When a projectile kills, ACE calls <c>OnDeath</c> instead of writing the
/// damage line (<c>SpellProjectile.cs</c> 846-851), so <see cref="DamageDealt"/> is the damage of the blows that did not
/// kill; the kill itself is the 0x01AD notice. <see cref="DamageTaken"/> adds the melee damage of the 0x01B2 notices to
/// the spell damage taken (<see cref="SpellDamageTaken"/>); <see cref="HitsTaken"/> counts the 0x01B2 notices only.</para>
/// <para><b>Parser.</b> A hand parser over spans (no Regex, no allocation per line): it finds " points with " (or
/// " points of "), reads the digits before it, and checks the words around them. A kill's text is kept as the string the
/// event already holds.</para>
/// </remarks>
public sealed class FightTally : IDisposable
{
    /// <summary>ACE's <c>ChatMessageType.Magic</c> (ACE.Entity <c>ChatMessageType.cs</c> line 98).</summary>
    public const uint MagicChatType = 0x07;

    private const string CritPrefix = "Critical hit! ";
    private const string OverpowerPrefix = "Overpower! ";
    private const string SneakPrefix = "Sneak Attack! ";
    private const string PointsWith = " points with ";

    private readonly List<IDisposable> _subscriptions = [];
    private IGameSessionView? _view;
    private string? _targetName;
    private long _xpBaseline;
    private bool _xpBaselinePending;

    /// <summary>The casts the scenario sent (<see cref="NoteCast"/>).</summary>
    public int Casts { get; private set; }

    /// <summary>The casts that ended without a projectile or without a damage line (<see cref="NoteMiss"/>).</summary>
    public int Misses { get; private set; }

    /// <summary>The sum of the points in the player's own spell-damage lines.</summary>
    public long DamageDealt { get; private set; }

    /// <summary>How many spell-damage lines the player's casts produced.</summary>
    public int DamageLinesDealt { get; private set; }

    /// <summary>How many of those lines were critical hits ("Critical hit! ").</summary>
    public int CriticalsDealt { get; private set; }

    /// <summary>The points of stamina and mana the player's casts drained (not damage).</summary>
    public long DrainDealt { get; private set; }

    /// <summary>Spell damage taken plus the melee damage of the 0x01B2 notices.</summary>
    public long DamageTaken { get; private set; }

    /// <summary>The spell damage taken: the sum of the lines "&lt;source&gt; &lt;verbs&gt; you for &lt;n&gt; points with &lt;spell&gt;.".</summary>
    public long SpellDamageTaken { get; private set; }

    /// <summary>The points of stamina and mana drained from the player by a spell.</summary>
    public long DrainTaken { get; private set; }

    /// <summary>How many GameEvent 0x01B2 DefenderNotification arrived (an attack landed on the player).</summary>
    public int HitsTaken { get; private set; }

    /// <summary>How many GameEvent 0x01B4 EvasionDefenderNotification arrived (the player evaded an attack).</summary>
    public int Evades { get; private set; }

    /// <summary>How many GameEvent 0x01AD KillerNotification arrived.</summary>
    public int Kills { get; private set; }

    /// <summary>
    /// How many of the <see cref="Kills"/> named the scenario's target (<see cref="SetTarget"/>): the 0x01AD text contains the target's name (ACE's death messages all name the creature, for example "You flatten
    /// Tusker Slave's body with the force of your assault!"). Report-only: the verdict reads <see cref="Kills"/> (DoD 8 judges <c>kills &gt;= 1</c>); this says whether those kills were the target's (review 3.1-R7).
    /// </summary>
    public int TargetKills { get; private set; }

    /// <summary>True when the last 0x01AD named the target (false before any kill, and for a kill that named someone else or when no target name was set).</summary>
    public bool LastKillNamedTarget { get; private set; }

    /// <summary>The last 0x01AD text ("You flatten X's body with the force of your assault!" and the like), or null.</summary>
    public string? LastKillText { get; private set; }

    /// <summary>The experience gained since <see cref="Start"/> (or <see cref="Reset"/>): the total-experience delta from the baseline.</summary>
    public long XpGained { get; private set; }

    /// <summary>How many Magic-typed lines matched no spell-damage form (counted, never guessed).</summary>
    public int UnparsedMagicLines { get; private set; }

    /// <summary>True between <see cref="Start"/> and <see cref="Dispose"/>.</summary>
    public bool Running => _view is not null;

    /// <summary>Subscribes to the bus and takes the experience baseline; calling it again re-subscribes.</summary>
    /// <param name="view">The session.</param>
    public void Start(IGameSessionView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        Dispose();
        _view = view;
        _subscriptions.Add(view.Events.Subscribe<ChatReceived>(OnChat));
        _subscriptions.Add(view.Events.Subscribe<PlayerKilledSomething>(OnKill));
        _subscriptions.Add(view.Events.Subscribe<CombatEvent>(OnCombat));
        _subscriptions.Add(view.Events.Subscribe<QualitiesChanged>(OnQualities));
        TakeBaseline();
    }

    /// <summary>Unsubscribes; the counters stay readable.</summary>
    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        _view = null;
    }

    /// <summary>The scenario chose a target: the name a 0x01AD must contain to count in <see cref="TargetKills"/> (null or empty: no kill counts toward the target, TODO(m3-fight-target-name)).</summary>
    /// <param name="name">The target's name from its CreateObject.</param>
    public void SetTarget(string? name) => _targetName = name;

    /// <summary>The scenario sent a cast.</summary>
    public void NoteCast() => Casts++;

    /// <summary>A cast ended without a projectile, or a projectile ended without a damage line.</summary>
    public void NoteMiss() => Misses++;

    /// <summary>Zeroes every counter and takes the experience baseline again (the subscriptions stay).</summary>
    public void Reset()
    {
        Casts = 0;
        Misses = 0;
        DamageDealt = 0;
        DamageLinesDealt = 0;
        CriticalsDealt = 0;
        DrainDealt = 0;
        DamageTaken = 0;
        SpellDamageTaken = 0;
        DrainTaken = 0;
        HitsTaken = 0;
        Evades = 0;
        Kills = 0;
        TargetKills = 0;
        LastKillNamedTarget = false;
        LastKillText = null;
        XpGained = 0;
        UnparsedMagicLines = 0;
        TakeBaseline();
    }

    /// <summary>
    /// Classifies one Magic line and reports what it carries (the parser the bus handler calls, public so the tests pin
    /// each form). Allocates nothing.
    /// </summary>
    /// <param name="text">The line.</param>
    /// <param name="amount">The points, or 0.</param>
    /// <param name="critical">True when the line carries the "Critical hit! " prefix.</param>
    /// <returns>Which form it is.</returns>
    public static MagicLineKind Classify(ReadOnlySpan<char> text, out long amount, out bool critical)
    {
        amount = 0;
        critical = false;
        while (true)
        {
            if (text.StartsWith(CritPrefix, StringComparison.Ordinal))
            {
                critical = true;
                text = text[CritPrefix.Length..];
            }
            else if (text.StartsWith(OverpowerPrefix, StringComparison.Ordinal))
            {
                text = text[OverpowerPrefix.Length..];
            }
            else if (text.StartsWith(SneakPrefix, StringComparison.Ordinal))
            {
                text = text[SneakPrefix.Length..];
            }
            else
            {
                break;
            }
        }

        // "With <spell> you drain <n> points of <vital> from <target>."
        if (text.StartsWith("With ", StringComparison.Ordinal))
        {
            return TryAmountAfter(text, " you drain ", " points of ", out amount) && HasFullStop(text) ? MagicLineKind.DrainDealt : MagicLineKind.Unparsed;
        }

        // "<source> casts <spell> and drains <n> points of your <vital>."
        if (text.Contains(" casts ", StringComparison.Ordinal))
        {
            return TryAmountAfter(text, " and drains ", " points of your ", out amount) && HasFullStop(text) ? MagicLineKind.DrainTaken : MagicLineKind.Unparsed;
        }

        var at = text.IndexOf(PointsWith, StringComparison.Ordinal);
        if (at <= 0 || !HasFullStop(text[(at + PointsWith.Length)..]) || !TryDigitsBefore(text, at, out amount, out var digitsStart))
        {
            return MagicLineKind.Unparsed;
        }

        var head = text[..digitsStart];

        // "You <verb> <target> for <n> points with <spell>."
        if (text.StartsWith("You ", StringComparison.Ordinal))
        {
            return head.EndsWith(" for ", StringComparison.Ordinal) && head.Length > "You  for ".Length ? MagicLineKind.DamageDealt : MagicLineKind.Unparsed;
        }

        // "<source> <verbs> you for <n> points with <spell>."
        return head.EndsWith(" you for ", StringComparison.Ordinal) && head.Length > " you for ".Length ? MagicLineKind.DamageTaken : MagicLineKind.Unparsed;
    }

    private static bool HasFullStop(ReadOnlySpan<char> text) => text.IndexOf('.') >= 0;

    /// <summary>The digits that follow <paramref name="lead"/> and are followed by <paramref name="trail"/>.</summary>
    private static bool TryAmountAfter(ReadOnlySpan<char> text, string lead, string trail, out long amount)
    {
        amount = 0;
        var at = text.IndexOf(lead, StringComparison.Ordinal);
        if (at < 0)
        {
            return false;
        }

        var rest = text[(at + lead.Length)..];
        var end = rest.IndexOf(trail, StringComparison.Ordinal);
        return end > 0 && long.TryParse(rest[..end], NumberStyles.None, CultureInfo.InvariantCulture, out amount);
    }

    /// <summary>The run of digits that ends just before <paramref name="end"/>.</summary>
    private static bool TryDigitsBefore(ReadOnlySpan<char> text, int end, out long amount, out int start)
    {
        start = end;
        while (start > 0 && char.IsAsciiDigit(text[start - 1]))
        {
            start--;
        }

        amount = 0;
        return start < end && long.TryParse(text[start..end], NumberStyles.None, CultureInfo.InvariantCulture, out amount);
    }

    private void TakeBaseline()
    {
        if (_view is null)
        {
            return;
        }

        var q = _view.State.Self.Qualities;
        if (q.TryGetInt64(PlayerQualities.TotalExperienceProperty, out var total))
        {
            _xpBaseline = total;
            _xpBaselinePending = false;
        }
        else
        {
            // The description has not arrived: the first total-experience value we see is the baseline.
            _xpBaseline = 0;
            _xpBaselinePending = true;
        }

        XpGained = 0;
    }

    private void OnChat(in ChatReceived e)
    {
        if (e.Line.ChatType != MagicChatType)
        {
            return;
        }

        switch (Classify(e.Line.Text, out var amount, out var critical))
        {
            case MagicLineKind.DamageDealt:
                DamageDealt += amount;
                DamageLinesDealt++;
                if (critical)
                {
                    CriticalsDealt++;
                }

                break;
            case MagicLineKind.DrainDealt:
                DrainDealt += amount;
                break;
            case MagicLineKind.DamageTaken:
                SpellDamageTaken += amount;
                DamageTaken += amount;
                break;
            case MagicLineKind.DrainTaken:
                DrainTaken += amount;
                break;
            default:
                UnparsedMagicLines++;
                break;
        }
    }

    private void OnKill(in PlayerKilledSomething e)
    {
        Kills++;
        LastKillText = e.Text;
        LastKillNamedTarget = !string.IsNullOrEmpty(_targetName) && e.Text is not null && e.Text.Contains(_targetName, StringComparison.OrdinalIgnoreCase);
        if (LastKillNamedTarget)
        {
            TargetKills++;
        }
    }

    private void OnCombat(in CombatEvent e)
    {
        switch (e.Kind)
        {
            case CombatEventKind.DefenseHit:
                HitsTaken++;
                DamageTaken += e.Damage;
                break;
            case CombatEventKind.DefenseEvaded:
                Evades++;
                break;
        }
    }

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

/// <summary>Which spell-damage form a Magic-typed line is (<see cref="FightTally.Classify"/>).</summary>
public enum MagicLineKind
{
    /// <summary>Not one of the four forms: counted in <see cref="FightTally.UnparsedMagicLines"/>.</summary>
    Unparsed,

    /// <summary>"You &lt;verb&gt; &lt;target&gt; for &lt;n&gt; points with &lt;spell&gt;.".</summary>
    DamageDealt,

    /// <summary>"With &lt;spell&gt; you drain &lt;n&gt; points of &lt;vital&gt; from &lt;target&gt;.".</summary>
    DrainDealt,

    /// <summary>"&lt;source&gt; &lt;verbs&gt; you for &lt;n&gt; points with &lt;spell&gt;.".</summary>
    DamageTaken,

    /// <summary>"&lt;source&gt; casts &lt;spell&gt; and drains &lt;n&gt; points of your &lt;vital&gt;.".</summary>
    DrainTaken,
}
