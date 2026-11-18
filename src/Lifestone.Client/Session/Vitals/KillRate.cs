using System.Globalization;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Vitals;

/// <summary>
/// M6 14 the losing race, the kill side (the lead, 2026-11-15: the owner's direction is to fight the way there and kill everything that attacks; a recall is for a fight she cannot win; live 06:17:07 UTC: her bolt at a Reaper of Magic
/// Defense 362 was resisted against her War Magic 362): her bolts as measured. Over the last <see cref="WindowSeconds"/>: the mean of her damage lines ("You incinerate White Phyntos Wasp for 157 points with Ilservian's Flame.",
/// <see cref="FightTally.Classify"/>'s <c>DamageDealt</c>) and the mean seconds of her War Magic casts (the Action API's log: each <c>Cast spell=</c> of a War Magic spell, from its queue to its end). Whether a bolt lands on a kind is ACE's
/// resist roll (<see cref="LandChance"/>: <c>MagicDefenseCheck</c>, her buffed War Magic against the kind's Magic Defense at 0.03, <c>WorldObject_Magic.cs:104-113</c> @95ef426d), the measured landings and resists of her bolts on that kind
/// taking over as they come (<see cref="LandShare"/>). Her damage a second on a kind is the mean landed damage times its landing share over the mean cast (<see cref="TryAgainst"/>).
/// <para>M6 14 the losing race, the killing blow (live ladder45 2026-11-16 23:07:43 UTC: four Shock Wave VI casts in 20 s each killed a Zefir outright, so no damage line came and her bolts read "not measured"): the killing blow writes
/// no damage line, only the death line ACE sends the killer (GameEvent 0x01AD, <c>Creature.GetDeathMessage</c>, <c>Creature_Death.cs:58-80</c> @95ef426d: <c>Strings.GetDeathMessage</c>'s killer form with the victim's name). The
/// vitals guard ties that line to her latest cast when it was War Magic (<see cref="TryKillingBolt"/>) and names its target, and notes it as a landed bolt of the target's known health before the blow (<see cref="NoteKill"/>): a lower
/// bound of the bolt's damage, so still the safe side.</para> Engine-free; reading allocates only a kind's first count.
/// </summary>
public sealed class KillRate
{
    /// <summary>The seconds the mean damage and the mean cast are read over (Lifestone's own: about ten bolts, long enough that one critical or one slow cast does not decide them).</summary>
    public const double WindowSeconds = 30;

    /// <summary>The longest a cast is counted for (Lifestone's own: a cast held behind another action in its lane past this is the lane's wait, not the cast).</summary>
    public const double MaxCastSeconds = 10;

    private const string CastPrefix = "Cast spell=";
    private const string ResistTail = " resists your spell";
    private const int Ring = 64;

    private readonly IReadOnlyDictionary<uint, SpellInfo> _spells;
    private readonly double[] _castAt = new double[Ring];
    private readonly double[] _castSeconds = new double[Ring];
    private readonly double[] _hitAt = new double[Ring];
    private readonly double[] _hitDamage = new double[Ring];
    private readonly bool[] _hitKill = new bool[Ring];
    private readonly double[] _anyAt = new double[Ring];
    private readonly uint[] _anySpell = new uint[Ring];
    private readonly uint[] _anyTarget = new uint[Ring];
    private int _anyNext;
    private readonly Dictionary<string, (int Landed, int Resisted)> _kinds = new(StringComparer.Ordinal);
    private int _castNext;
    private int _hitNext;
    private long _actionsSeen = -1;
    private long _chatSeen = -1;

    /// <summary>The rate of the spells of <paramref name="spells"/> (their schools say which are War Magic).</summary>
    /// <param name="spells">The SpellTable's spells.</param>
    public KillRate(IReadOnlyDictionary<uint, SpellInfo> spells)
    {
        _spells = spells ?? throw new ArgumentNullException(nameof(spells));
        Array.Fill(_castAt, double.NegativeInfinity);
        Array.Fill(_hitAt, double.NegativeInfinity);
        Array.Fill(_anyAt, double.NegativeInfinity);
    }

    /// <summary>
    /// ACE's chance that her spell of <paramref name="skill"/> lands on a creature of Magic Defense <paramref name="defence"/>: it fails the resist roll (<c>MagicDefenseCheck</c>: <c>SkillCheck.GetSkillChance</c> at the regular 0.03,
    /// <c>WorldObject_Magic.cs:104-113</c> @95ef426d). Her War Magic 362 against a Reaper's 362 is a half. M6 16 E1e: the one function the drain pick asks too (<see cref="ResistRoll.LandChance"/>); a Magic Defense of 0 is
    /// rolled as ACE rolls it (0.99998 at 362), where this gave exactly 1 before and the drain pick an even 0.5.
    /// </summary>
    /// <param name="skill">Her current (buffed) skill in the spell's school.</param>
    /// <param name="defence">The creature's Magic Defense.</param>
    public static double LandChance(uint skill, uint defence) => ResistRoll.LandChance(skill, defence);

    /// <summary>Reads the action log's and the chat's new entries (the ones before the first read are not this rate's). <paramref name="drainOwns"/> claims a resist line that is the vitals guard's own drain's.</summary>
    /// <param name="actions">The Action API's log.</param>
    /// <param name="chat">The chat.</param>
    /// <param name="drainOwns">True for a resisting kind's name while the guard's drain at it is in flight, or null.</param>
    public void Read(ActionLog actions, ChatLog chat, Func<string, bool>? drainOwns = null)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(chat);
        if (_actionsSeen < 0 || _chatSeen < 0)
        {
            (_actionsSeen, _chatSeen) = (actions.Total, chat.Total);
            return;
        }

        var fresh = (int)Math.Min(actions.Count, actions.Total - _actionsSeen);
        for (var i = actions.Count - fresh; i < actions.Count; i++)
        {
            var e = actions[i];
            if (e.Kind != ActionKind.CastSpell || SpellOf(e.Summary) is not { } id)
            {
                continue;
            }

            NoteAnyCast(e.CompletedAt, id, TargetOfCast(e.Summary));
            if (_spells.TryGetValue(id, out var info) && info.School == MagicSchool.WarMagic)
            {
                NoteCast(e.CompletedAt, e.CompletedAt - e.QueuedAt);
            }
        }

        _actionsSeen = actions.Total;
        var lines = (int)Math.Min(chat.Count, chat.Total - _chatSeen);
        for (var i = chat.Count - lines; i < chat.Count; i++)
        {
            var line = chat[i];
            if (line.Kind != ChatKind.System || line.ChatType != FightTally.MagicChatType)
            {
                continue;
            }

            if (line.Text.EndsWith(ResistTail, StringComparison.Ordinal) && line.Text.Length > ResistTail.Length)
            {
                var kind = line.Text[..^ResistTail.Length];
                if (drainOwns?.Invoke(kind) != true)
                {
                    NoteResist(kind);
                }

                continue;
            }

            if (FightTally.Classify(line.Text, out var amount, out _) == MagicLineKind.DamageDealt)
            {
                NoteHit(line.Time, amount, TargetOf(line.Text));
            }
        }

        _chatSeen = chat.Total;
    }

    /// <summary>One War Magic cast ended at <paramref name="at"/> after <paramref name="seconds"/> (cut to <see cref="MaxCastSeconds"/>).</summary>
    /// <param name="at">The session clock at its end.</param>
    /// <param name="seconds">Its seconds.</param>
    public void NoteCast(double at, double seconds)
    {
        (_castAt[_castNext], _castSeconds[_castNext]) = (at, Math.Clamp(seconds, 0, MaxCastSeconds));
        _castNext = (_castNext + 1) % Ring;
    }

    /// <summary>One of her damage lines of <paramref name="damage"/> at <paramref name="at"/>, on a creature named <paramref name="kind"/> (a landing on it), or null.</summary>
    /// <param name="at">The session clock.</param>
    /// <param name="damage">The damage.</param>
    /// <param name="kind">The creature's name, or null.</param>
    public void NoteHit(double at, double damage, string? kind = null) => NoteLanded(at, damage, kind, kill: false);

    /// <summary>
    /// M6 14 the losing race, the killing blow: her bolt killed a creature named <paramref name="kind"/> at <paramref name="at"/>, its known health before the blow <paramref name="healthBefore"/>: a landed bolt of at least that (the
    /// death line carries no amount; the bolt's damage past the health is not known, so the health is a lower bound: the safe side).
    /// </summary>
    /// <param name="at">The session clock of the death line.</param>
    /// <param name="healthBefore">The creature's known health before the blow.</param>
    /// <param name="kind">The creature's name.</param>
    public void NoteKill(double at, double healthBefore, string kind) => NoteLanded(at, healthBefore, kind, kill: true);

    /// <summary>
    /// M6 14 the losing race, the killing blow: true when her latest cast that ended at or before <paramref name="at"/> was War Magic: <paramref name="target"/> is its target (0 when the summary named none), <paramref name="spell"/>
    /// its spell. A death line after another kind of cast of hers (a drain, a heal) is not tied to a bolt.
    /// </summary>
    /// <param name="at">The session clock of the death line.</param>
    /// <param name="spell">The bolt's spell.</param>
    /// <param name="target">The bolt's target.</param>
    public bool TryKillingBolt(double at, out uint spell, out uint target)
    {
        (spell, target) = (0, 0);
        var latest = double.NegativeInfinity;
        for (var i = 0; i < Ring; i++)
        {
            if (_anyAt[i] <= at && _anyAt[i] > latest)
            {
                (latest, spell, target) = (_anyAt[i], _anySpell[i], _anyTarget[i]);
            }
        }

        return !double.IsNegativeInfinity(latest) && _spells.TryGetValue(spell, out var info) && info.School == MagicSchool.WarMagic && at - latest <= WindowSeconds;
    }

    /// <summary>M6 14 the losing race, the killing blow: her casts at <paramref name="target"/> that ended in the last <see cref="WindowSeconds"/> before <paramref name="now"/>.</summary>
    /// <param name="now">The session clock.</param>
    /// <param name="target">The target's guid.</param>
    public int CastsAt(double now, uint target)
    {
        var count = 0;
        for (var i = 0; i < Ring; i++)
        {
            if (_anyTarget[i] == target && now - _anyAt[i] <= WindowSeconds && _anyAt[i] <= now)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Why her bolts are not measured at <paramref name="now"/>, in words: the War Magic casts, the damage lines and the killing blows the window holds.</summary>
    /// <param name="now">The session clock.</param>
    public string Unmeasured(double now)
    {
        var (casts, lines, kills) = (0, 0, 0);
        for (var i = 0; i < Ring; i++)
        {
            casts += now - _castAt[i] <= WindowSeconds ? 1 : 0;
            if (now - _hitAt[i] <= WindowSeconds)
            {
                (lines, kills) = _hitKill[i] ? (lines, kills + 1) : (lines + 1, kills);
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"her bolts are not measured yet: {casts} War Magic cast(s), {lines} damage line(s) and {kills} killing blow(s) in the last {WindowSeconds:0} s");
    }

    /// <summary>The killing blows counted as landed bolts in the last <see cref="WindowSeconds"/> before <paramref name="now"/>.</summary>
    /// <param name="now">The session clock.</param>
    public int KillsIn(double now)
    {
        var kills = 0;
        for (var i = 0; i < Ring; i++)
        {
            kills += _hitKill[i] && now - _hitAt[i] <= WindowSeconds ? 1 : 0;
        }

        return kills;
    }

    private void NoteAnyCast(double at, uint spell, uint target)
    {
        (_anyAt[_anyNext], _anySpell[_anyNext], _anyTarget[_anyNext]) = (at, spell, target);
        _anyNext = (_anyNext + 1) % Ring;
    }

    private void NoteLanded(double at, double damage, string? kind, bool kill)
    {
        (_hitAt[_hitNext], _hitDamage[_hitNext], _hitKill[_hitNext]) = (at, Math.Max(0, damage), kill);
        _hitNext = (_hitNext + 1) % Ring;
        if (kind is not null)
        {
            var (landed, resisted) = _kinds.TryGetValue(kind, out var k) ? k : (0, 0);
            _kinds[kind] = (landed + 1, resisted);
        }
    }

    /// <summary>A creature named <paramref name="kind"/> resisted one of her spells ("&lt;name&gt; resists your spell", ACE's resist line).</summary>
    /// <param name="kind">The creature's name.</param>
    public void NoteResist(string kind)
    {
        var (landed, resisted) = _kinds.TryGetValue(kind, out var k) ? k : (0, 0);
        _kinds[kind] = (landed, resisted + 1);
    }

    /// <summary>
    /// The share of her bolts that land on <paramref name="kind"/>, measured where it can be: the landings plus ACE's chance (<paramref name="seed"/>, its weight one bolt) over the landings and resists plus one, so the formula leads and the
    /// measures take over as they come; the formula alone with none.
    /// </summary>
    /// <param name="kind">The creature's name.</param>
    /// <param name="seed">ACE's chance (<see cref="LandChance"/>).</param>
    public double LandShare(string kind, double seed)
    {
        var s = Math.Clamp(seed, 0, 1);
        return _kinds.TryGetValue(kind, out var k) && k.Landed + k.Resisted > 0 ? (k.Landed + s) / (k.Landed + k.Resisted + 1.0) : s;
    }

    /// <summary>Her bolts' landings and resists measured on <paramref name="kind"/>.</summary>
    /// <param name="kind">The creature's name.</param>
    public (int Landed, int Resisted) Of(string kind) => _kinds.TryGetValue(kind, out var k) ? k : (0, 0);

    /// <summary>Her bolts at <paramref name="now"/>: the window's mean landed damage and mean cast seconds; false with no damage line or no cast in the window (not measured).</summary>
    /// <param name="now">The session clock.</param>
    /// <param name="meanDamage">The mean damage of a landed bolt.</param>
    /// <param name="castSeconds">The mean seconds of a cast.</param>
    /// <param name="hits">The damage lines counted.</param>
    /// <param name="casts">The casts counted.</param>
    public bool TryBolt(double now, out double meanDamage, out double castSeconds, out int hits, out int casts)
    {
        (meanDamage, castSeconds, hits, casts) = (0, 0, 0, 0);
        var (damage, seconds) = (0.0, 0.0);
        for (var i = 0; i < Ring; i++)
        {
            if (now - _castAt[i] <= WindowSeconds)
            {
                (casts, seconds) = (casts + 1, seconds + _castSeconds[i]);
            }

            if (now - _hitAt[i] <= WindowSeconds)
            {
                (hits, damage) = (hits + 1, damage + _hitDamage[i]);
            }
        }

        if (hits == 0 || casts == 0 || seconds <= 0)
        {
            return false;
        }

        (meanDamage, castSeconds) = (damage / hits, seconds / casts);
        return true;
    }

    /// <summary>Her damage a second on <paramref name="kind"/>: the mean landed damage times its landing share (<see cref="LandShare"/> of ACE's <paramref name="landChance"/>) over the mean cast; false when her bolts are not measured.</summary>
    /// <param name="now">The session clock.</param>
    /// <param name="kind">The creature's name.</param>
    /// <param name="landChance">ACE's chance her bolt lands on it (<see cref="LandChance"/>).</param>
    /// <param name="perSecond">The damage a second.</param>
    public bool TryAgainst(double now, string kind, double landChance, out double perSecond)
    {
        perSecond = 0;
        if (!TryBolt(now, out var mean, out var seconds, out _, out _))
        {
            return false;
        }

        perSecond = mean * LandShare(kind, landChance) / seconds;
        return true;
    }

    /// <summary>The target of "You &lt;verb&gt; &lt;target&gt; for N points with &lt;spell&gt;.", or null.</summary>
    /// <param name="text">The line.</param>
    internal static string? TargetOf(string text)
    {
        const string you = "You ";
        var verbEnd = text.StartsWith(you, StringComparison.Ordinal) ? text.IndexOf(' ', you.Length) : -1;
        var forAt = verbEnd < 0 ? -1 : text.LastIndexOf(" for ", StringComparison.Ordinal);
        return forAt > verbEnd + 1 ? text[(verbEnd + 1)..forAt] : null;
    }

    /// <summary>The target guid of an Action API summary <c>Cast spell=&lt;id&gt; target=0x&lt;guid&gt;</c>, or 0.</summary>
    /// <param name="summary">The summary.</param>
    internal static uint TargetOfCast(string summary)
    {
        const string key = " target=0x";
        var at = summary is null ? -1 : summary.IndexOf(key, StringComparison.Ordinal);
        if (summary is null || at < 0)
        {
            return 0;
        }

        var digits = summary.AsSpan(at + key.Length);
        var end = digits.IndexOf(' ');
        return uint.TryParse(end < 0 ? digits : digits[..end], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var guid) ? guid : 0;
    }

    /// <summary>The spell id of an Action API summary <c>Cast spell=&lt;id&gt;[ target=...]</c>, or null.</summary>
    /// <param name="summary">The summary.</param>
    internal static uint? SpellOf(string summary)
    {
        if (summary is null || !summary.StartsWith(CastPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var digits = summary.AsSpan(CastPrefix.Length);
        var end = digits.IndexOf(' ');
        return uint.TryParse(end < 0 ? digits : digits[..end], NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
    }
}
