using Lifestone.Client.Session.Contracts;
using Lifestone.Client.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M5 7 P7p (D141, D88): the living hostile creatures near the player, for a station that must not start among them (the harm-heal station: the item-end gate's Archer drank its potion while a Gold Phyntos Wasp, a Shadow Child
/// and a Brutish Monouga the forest hunt left standing attacked it). A hostile is a creature that attacked the player (its name heard by an <see cref="AttackWatch"/>: a damage or evasion notice, a hostile spell line) and that
/// stands in the object table as an object whose ItemType has the creature bit (0x10), whose description flags say it may be attacked (<see cref="AttackableFlag"/>, ACE's <c>ObjectDescriptionFlag.Attackable</c>) and not that it
/// is a player, that is not known dead (health above 0, forward motion not Dead), within the reach, centre to centre. A creature that stands near but has not attacked (a camp the hunt left alone) is not one; a vendor or another NPC is
/// not attackable; a corpse is not a creature. Lifestone's own rule, not retail's.
/// </summary>
/// <remarks>M5 7 P7u (D148, D88): one shared source for the harm-heal station's pre-check and the hunt's fight-back (<c>HuntScenario.SelfDefence.cs</c>), so the hunt fights back what the station would refuse to start among.
/// M6 14 caught on the run (the owner, 2026-11-15, closing <c>TODO(m6-live-position-reach)</c>; the one world picture, 2026-11-12): every read here judges a creature where the session's <see cref="IGameSessionView.Live"/> says it
/// stands (the body the client draws when it draws one and the body can be trusted, else the server's last report: <see cref="LivePositions"/>), not by the report alone. A monster in melee that follows the player sends no position, so
/// its report lies behind its body: live 2026-11-15 04:10:23 UTC the run past read a Reedshark Ravager drawn 2.6 m from her while the fight-back's pick, reading the report, had it at 18.9 m "beyond the cast range" and waited 11 s while it
/// bit her eleven times. Her own place is the caller's (<c>here</c>: a travel leg passes her body as the client draws it), else her server position.</remarks>
public static class HostileReach
{
    /// <summary>The ObjectDescriptionFlags bit of an object that may be attacked (<c>BF_ATTACKABLE</c>, 0x10; ACE <c>ObjectDescriptionFlag.Attackable</c>).</summary>
    public const uint AttackableFlag = 0x10;

    /// <summary>The ItemType bit of a creature (<c>TYPE_CREATURE</c>, 0x10).</summary>
    public const uint CreatureItemType = 0x10;

    /// <summary>
    /// M6 7 AY: the distances, centre to centre, of every living creature that may be attacked, players excepted (a dead one's motion is Dead), each where the session draws it (<see cref="IGameSessionView.Live"/>): what the big pack
    /// rule reads at a death (<see cref="DeathTally.AwakeAtDeath"/>).
    /// </summary>
    /// <param name="session">The session (its objects, its live positions).</param>
    public static List<double> Distances(IGameSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var found = new List<double>();
        if (session.State.Self.Position is not { } here)
        {
            return found;
        }

        var origin = here.ToGlobal();
        var live = session.Live;
        foreach (var o in session.State.Objects.All)
        {
            if (!LivingCreature(session.State, o) || live.At(o) is not { } p)
            {
                continue;
            }

            found.Add(p.ToGlobal().DistanceTo(origin));
        }

        return found;
    }

    /// <summary>M6 7 finish: how many living creatures that may be attacked stand within <paramref name="metres"/> of her (where the session draws them), the same read as <see cref="Distances"/> with nothing allocated: the fight's pack-count line samples it.</summary>
    /// <param name="session">The session.</param>
    /// <param name="metres">The reach, centre to centre.</param>
    public static int CountWithin(IGameSessionView session, double metres)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.State.Self.Position is not { } here)
        {
            return 0;
        }

        var origin = here.ToGlobal();
        var live = session.Live;
        var count = 0;
        foreach (var o in session.State.Objects.All)
        {
            if (LivingCreature(session.State, o) && live.At(o) is { } p && p.ToGlobal().DistanceTo(origin) <= metres)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>M5 7 P7p: the reach, centre to centre, inside which a creature heard attacking counts as a hostile in reach (Lifestone's own: the gate's wasp bolts came from 12 to 14 m). The station's pre-check and the hunt's fight-back share it.</summary>
    public const double DefaultMetres = 40;

    /// <summary>
    /// The nearest hostile within <paramref name="metres"/> of the player whose name is among <paramref name="attackers"/>: its guid, name and distance where the session draws it (<see cref="IGameSessionView.Live"/>); false when none
    /// stands there (or the player has no position).
    /// </summary>
    /// <param name="session">The session (its objects, its live positions).</param>
    /// <param name="metres">The reach, centre to centre.</param>
    /// <param name="attackers">The names of the creatures heard attacking the player.</param>
    /// <param name="guid">The hostile's guid, or 0.</param>
    /// <param name="name">Its name, or empty.</param>
    /// <param name="distance">Its distance in metres, or NaN.</param>
    /// <param name="skip">M5 7 P7u: the guids the caller has set aside (the hunt's refused or blocked targets); null skips none.</param>
    /// <param name="here">M6 14: where she stands as the caller reads her (a travel leg: her body as the client draws it), or null: her server position.</param>
    public static bool TryNearest(IGameSessionView session, double metres, IReadOnlySet<string> attackers, out uint guid, out string name, out double distance, Func<uint, bool>? skip = null, CellFrame? here = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(attackers);
        guid = 0;
        name = string.Empty;
        distance = double.NaN;
        if ((here ?? session.State.Self.Position) is not { } at || attackers.Count == 0)
        {
            return false;
        }

        var origin = at.ToGlobal();
        var live = session.Live;
        var best = metres;
        foreach (var o in session.State.Objects.All)
        {
            if (!attackers.Contains(o.Name) || !LivingCreature(session.State, o) || (skip is not null && skip(o.Guid)) || live.At(o) is not { } p)
            {
                continue;
            }

            var d = p.ToGlobal().DistanceTo(origin);
            if (d <= best)
            {
                best = d;
                guid = o.Guid;
                name = o.Name;
                distance = d;
            }
        }

        return guid != 0;
    }

    /// <summary>
    /// M6 14 retreat attackers (rule 1): every hostile within <paramref name="metres"/> of the player whose name is among <paramref name="attackers"/> (the creatures heard attacking), by guid, weenie class, name and distance where the
    /// session draws it (<see cref="IGameSessionView.Live"/>): the attackers the line from the creatures present reads (<see cref="Vitals.LiveFightLine.ReadAttackers"/>). The same filter as <see cref="TryNearest"/>, none skipped (a
    /// creature left as out of cast range still hits). Fills <paramref name="into"/> (cleared first); allocates nothing else.
    /// </summary>
    /// <param name="session">The session (its objects, its live positions).</param>
    /// <param name="metres">The reach, centre to centre.</param>
    /// <param name="attackers">The names of the creatures heard attacking the player.</param>
    /// <param name="into">The list filled.</param>
    /// <param name="here">M6 14: where she stands as the caller reads her (a travel leg: her body as the client draws it), or null: her server position.</param>
    public static void Present(IGameSessionView session, double metres, IReadOnlySet<string> attackers, List<Vitals.PresentAttacker> into, CellFrame? here = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(attackers);
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        if ((here ?? session.State.Self.Position) is not { } at || attackers.Count == 0)
        {
            return;
        }

        var origin = at.ToGlobal();
        var live = session.Live;
        foreach (var o in session.State.Objects.All)
        {
            if (!attackers.Contains(o.Name) || !LivingCreature(session.State, o) || live.At(o) is not { } p)
            {
                continue;
            }

            var d = p.ToGlobal().DistanceTo(origin);
            if (d <= metres)
            {
                into.Add(new Vitals.PresentAttacker(o.Guid, o.Weenie.WeenieClassId, o.Name, d));
            }
        }
    }

    /// <summary>True for a living creature that may be attacked and is neither the player nor another player (a dead one's health is 0 or its motion Dead), placed in the world by the server's record.</summary>
    private static bool LivingCreature(GameState state, WorldObject o) =>
        !o.IsSelf && o.Guid != state.Self.Guid && o.Position is not null && (o.ItemType & CreatureItemType) != 0 && (o.DescriptionFlags & AttackableFlag) != 0 && (o.DescriptionFlags & WorldObjectTable.PlayerFlag) == 0
        && o.Health is not <= 0f && o.Motion is not { Forward: WorldObjectTable.DeadMotionLowWord };

    /// <summary>
    /// The outdoor point <paramref name="metres"/> from the player straight away from <paramref name="from"/> (on the line from it through the player; due east when they stand on one spot), as a cell and a landblock-local
    /// point; false when the player is indoors (a room has no "away" the station can name) or the point is off the map.
    /// </summary>
    /// <param name="here">The player's position.</param>
    /// <param name="from">The hostile's position.</param>
    /// <param name="metres">How far to step.</param>
    /// <param name="away">The outdoor cell and point.</param>
    public static bool TryAwayPoint(CellFrame here, CellFrame from, double metres, out CellPosition away)
    {
        away = default;
        if (!AcCoordinates.IsOutdoorCell(here.CellId))
        {
            return false;
        }

        var g = here.ToGlobal();
        var h = from.ToGlobal();
        var dx = g.X - h.X;
        var dy = g.Y - h.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        (dx, dy) = length < 0.01 ? (1.0, 0.0) : (dx / length, dy / length);
        return AcCoordinates.TryGetOutdoorCell(new GlobalPosition(g.X + (dx * metres), g.Y + (dy * metres), g.Z), out away);
    }
}

/// <summary>
/// M5 7 P7p: the names of the creatures that attacked the player while it is held, as ACE tells the defender (the same notices <c>FightScenario</c>'s self-defence hears, F4): GameEvent 0x01B2 and 0x01B4 as a
/// <see cref="CombatEvent"/> (<see cref="CombatEventKind.DefenseHit"/>, <see cref="CombatEventKind.DefenseEvaded"/>), and the Magic-typed lines a creature's spell writes to the player (<c>FightScenario.HostileSpellSource</c>:
/// a damage line, a drain, a resist, a spell cast on the player). Each new name is logged once by <paramref name="log"/> ("heard '&lt;name&gt;' attack the player (&lt;how&gt;)"; the caller prefixes it).
/// </summary>
/// <remarks>M5 7 P7u (D148, D88): each name also keeps the session clock it was last heard at, so the hunt asks for the creatures that attacked within the last few seconds (<see cref="HeardWithin"/>).</remarks>
/// <param name="session">The session whose events are heard.</param>
/// <param name="log">Logs a name the first time it is heard.</param>
public sealed class AttackWatch(IGameSessionView session, Action<string> log) : IDisposable
{
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _lastAt = new(StringComparer.Ordinal);
    private readonly HashSet<string> _recent = new(StringComparer.Ordinal);
    private readonly Vitals.RecentHits _hits = new();
    private double _tallyFrom = double.NaN;
    private double _tallyDamage;
    private double _tallyBiggest;
    private IDisposable? _combat;
    private IDisposable? _chat;
    private bool _started;

    /// <summary>The names heard since the last <see cref="Clear"/>.</summary>
    public IReadOnlySet<string> Names => _names;

    /// <summary>M6 14 retreat attackers (rule 3): what the recent hits say at <paramref name="now"/> (the damage notices' and the hostile spell lines' amounts, the same seam as the autopilot's <c>HostileWatch</c>).</summary>
    /// <param name="now">The session clock.</param>
    public Vitals.IncomingRead Incoming(double now) => _hits.Read(now);

    /// <summary>M6 14 retreat attackers: a new tally of the hits begins (a fight-back's start): <see cref="Measured"/> reads its mean and its largest hit.</summary>
    public void BeginTally() => (_tallyFrom, _tallyDamage, _tallyBiggest) = (session.Clock.Now, 0, 0);

    /// <summary>
    /// M6 14 retreat attackers (rule 3): the hits this fight-back has actually seen: the damage a second, the larger of the recent hits' rate (<see cref="Incoming"/>) and the tally's mean since <see cref="BeginTally"/> (over at least
    /// <see cref="Vitals.RecentHits.RecentSeconds"/>, so one early hit is not a rate), and the largest hit, the tally's or the recent ten seconds'.
    /// </summary>
    /// <param name="now">The session clock.</param>
    public (double PerSecond, double BiggestHit) Measured(double now)
    {
        var recent = _hits.Read(now);
        var mean = double.IsNaN(_tallyFrom) ? 0 : _tallyDamage / Math.Max(Vitals.RecentHits.RecentSeconds, now - _tallyFrom);
        return (Math.Max(recent.PerSecond, mean), Math.Max(recent.BiggestHit, double.IsNaN(_tallyFrom) ? 0 : _tallyBiggest));
    }

    /// <summary>Starts hearing (once).</summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _combat = session.Events.Subscribe<CombatEvent>(OnCombat);
        _chat = session.Events.Subscribe<ChatReceived>(OnChat);
    }

    /// <summary>Forgets the names heard (a new listen begins).</summary>
    public void Clear()
    {
        _names.Clear();
        _lastAt.Clear();
    }

    /// <summary>M5 7 P7u: the session clock <paramref name="name"/> was last heard attacking at; false when it was not heard since the last <see cref="Clear"/>.</summary>
    public bool LastHeard(string name, out double at) => _lastAt.TryGetValue(name, out at);

    /// <summary>
    /// M6 14 caught on the run (the owner's ruling, 2026-11-15: she is not in a lull while she is under attack): the last attack heard on her from any creature since the last <see cref="Clear"/> (a damage or evasion notice, a hostile spell line):
    /// its name and the session clock it came at; false when none was heard. A line that says she is in a lull, or that no attacker is heard, names it so the claim can be checked.
    /// </summary>
    /// <param name="name">The creature's name, or empty.</param>
    /// <param name="at">The session clock, or NaN.</param>
    public bool LastAttack(out string name, out double at)
    {
        (name, at) = (string.Empty, double.NaN);
        foreach (var (who, when) in _lastAt)
        {
            if (double.IsNaN(at) || when > at)
            {
                (name, at) = (who, when);
            }
        }

        return !double.IsNaN(at);
    }

    /// <summary>
    /// M5 7 P7u: the names heard attacking within the last <paramref name="seconds"/> of the session clock (the set is the watch's own, refilled by each call: read it before the next).
    /// </summary>
    public IReadOnlySet<string> HeardWithin(double seconds)
    {
        _recent.Clear();
        var now = session.Clock.Now;
        foreach (var (name, at) in _lastAt)
        {
            if (now - at <= seconds)
            {
                _recent.Add(name);
            }
        }

        return _recent;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _combat?.Dispose();
        _chat?.Dispose();
        _combat = null;
        _chat = null;
    }

    /// <summary>M6 14 run past (D137): the attacks heard from <paramref name="name"/> since the watch started (each damage or evasion notice and each hostile spell line: a swing or a throw that reached her, landed or not), and the damage they dealt.</summary>
    /// <param name="name">The creature's name.</param>
    public (int Attacks, double Damage) AttacksOf(string name) => _byName.TryGetValue(name, out var tally) ? tally : (0, 0);

    private readonly Dictionary<string, (int Attacks, double Damage)> _byName = new(StringComparer.Ordinal);

    private void OnCombat(in CombatEvent e)
    {
        if (e.Kind is CombatEventKind.DefenseHit or CombatEventKind.DefenseEvaded && !string.IsNullOrEmpty(e.Who))
        {
            Heard(e.Who, e.Kind == CombatEventKind.DefenseHit ? "damage notice" : "evasion notice");
            Attacked(e.Who, e.Kind == CombatEventKind.DefenseHit ? e.Damage : 0);
        }

        if (e.Kind == CombatEventKind.DefenseHit)
        {
            Hit(e.Damage, e.Who); // M6 14 retreat attackers: the travel legs' fight-backs feed the measured floor
        }
    }

    private void OnChat(in ChatReceived e)
    {
        if (e.Line.ChatType != FightTally.MagicChatType || e.Line.Kind != ChatKind.System)
        {
            return;
        }

        if (FightTally.TryHealthLoss(e.Line.Text, out var lossSource, out var loss))
        {
            Heard(lossSource, "health drain");
            var lost = (uint)Math.Clamp(loss, 0, uint.MaxValue);
            Attacked(lossSource, lost);
            Hit(lost, lossSource); // M6 14 the walk's line: health a drain took is a hit
            return;
        }

        if (FightScenario.HostileSpellSource(e.Line.Text, out var source, out var how))
        {
            Heard(source, how);
            var kind = FightTally.Classify(e.Line.Text, out var amount, out _);
            var taken = kind == MagicLineKind.DamageTaken || (kind == MagicLineKind.DrainTaken && e.Line.Text.EndsWith(" points of your health.", StringComparison.Ordinal));
            var damage = taken ? (uint)Math.Clamp(amount, 0, uint.MaxValue) : 0u;
            Attacked(source, damage);
            if (taken)
            {
                Hit(damage, source);
            }

            return;
        }

        if (FightTally.TryCastOnYou(e.Line.Text, out var caster))
        {
            Timeline(caster); // an enchantment on her: the attack timeline of a kind heard attacking, no name heard by it alone (ACE writes it for a buff too)
        }
    }

    private void Attacked(string name, uint damage)
    {
        var (attacks, dealt) = _byName.TryGetValue(name, out var tally) ? tally : (0, 0);
        _byName[name] = (attacks + 1, dealt + damage);
        Timeline(name);
    }

    // ------------------------------------------------------------------------------------------------ M6 14 the walk's line: each kind's attack timeline

    private const int KindRing = 128;
    private readonly double[] _kindHitAt = [.. Enumerable.Repeat(double.NegativeInfinity, KindRing)];
    private readonly string?[] _kindHitWho = new string?[KindRing];
    private readonly double[] _kindHitDamage = new double[KindRing];
    private int _kindHitNext;
    private readonly Dictionary<string, (double First, double Last, int Attacks)> _timeline = new(StringComparer.Ordinal);

    /// <summary>
    /// M6 14 the walk's line (live ladder51 2026-11-17 03:48:30.997 UTC: the walk's line read 95% from the table's rates of three Dark Wisps that had landed nothing, and she retreated at 142 of 191 from a fight she was winning): one
    /// kind's attacks in this attack and its hits in the last <see cref="Vitals.RecentHits.AverageSeconds"/>: the seconds since its first attack (a quiet of <see cref="QuietSeconds"/> starts a new attack), its attacks heard (a hit,
    /// an evade, a resisted spell, a drain, an enchantment cast on her), its hits' damage and its largest hit in the window. False when the kind's last attack is a quiet ago or it was never heard. Allocation-free.
    /// </summary>
    /// <param name="name">The kind's name.</param>
    /// <param name="now">The session clock.</param>
    public Vitals.KindMeasure? Measure(string name, double now)
    {
        if (!_timeline.TryGetValue(name, out var t) || now - t.Last >= QuietSeconds)
        {
            return null;
        }

        var (damage, biggest) = (0.0, 0.0);
        for (var i = 0; i < KindRing; i++)
        {
            if (now - _kindHitAt[i] <= Vitals.RecentHits.AverageSeconds && string.Equals(_kindHitWho[i], name, StringComparison.Ordinal))
            {
                damage += _kindHitDamage[i];
                biggest = Math.Max(biggest, _kindHitDamage[i]);
            }
        }

        return new Vitals.KindMeasure(now - t.First, t.Attacks, damage, biggest);
    }

    /// <summary>The quiet after which a kind's next attack starts a new attack (the autopilot's <c>HostileWatch.AttackQuietSeconds</c>, the re-buff driver's fight quiet).</summary>
    public const double QuietSeconds = Mage.RebuffDriver.FightQuietSeconds;

    private void Timeline(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var now = session.Clock.Now;
        _timeline[name] = _timeline.TryGetValue(name, out var t) && now - t.Last < QuietSeconds ? (t.First, now, t.Attacks + 1) : (now, now, 1);
    }

    private void Hit(uint damage, string? who)
    {
        if (!string.IsNullOrEmpty(who))
        {
            (_kindHitAt[_kindHitNext], _kindHitWho[_kindHitNext], _kindHitDamage[_kindHitNext]) = (session.Clock.Now, who, damage);
            _kindHitNext = (_kindHitNext + 1) % KindRing;
        }

        _hits.Note(session.Clock.Now, damage);
        if (!double.IsNaN(_tallyFrom))
        {
            _tallyDamage += damage;
            _tallyBiggest = Math.Max(_tallyBiggest, damage);
        }
    }

    private void Heard(string name, string how)
    {
        _lastAt[name] = session.Clock.Now;
        if (_names.Add(name))
        {
            log($"heard '{name}' attack the player ({how})");
        }
    }
}
