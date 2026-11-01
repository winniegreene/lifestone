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
/// <remarks>M5 7 P7u (D148, D88): one shared source for the harm-heal station's pre-check and the hunt's fight-back (<c>HuntScenario.SelfDefence.cs</c>), so the hunt fights back what the station would refuse to start among.</remarks>
public static class HostileReach
{
    /// <summary>The ObjectDescriptionFlags bit of an object that may be attacked (<c>BF_ATTACKABLE</c>, 0x10; ACE <c>ObjectDescriptionFlag.Attackable</c>).</summary>
    public const uint AttackableFlag = 0x10;

    /// <summary>The ItemType bit of a creature (<c>TYPE_CREATURE</c>, 0x10).</summary>
    public const uint CreatureItemType = 0x10;

    /// <summary>M5 7 P7p: the reach, centre to centre, inside which a creature heard attacking counts as a hostile in reach (Lifestone's own: the gate's wasp bolts came from 12 to 14 m). The station's pre-check and the hunt's fight-back share it.</summary>
    public const double DefaultMetres = 40;

    /// <summary>The nearest hostile within <paramref name="metres"/> of the player whose name is among <paramref name="attackers"/>: its guid, name and distance; false when none stands there (or the player has no position).</summary>
    /// <param name="state">The game state.</param>
    /// <param name="metres">The reach, centre to centre.</param>
    /// <param name="attackers">The names of the creatures heard attacking the player.</param>
    /// <param name="guid">The hostile's guid, or 0.</param>
    /// <param name="name">Its name, or empty.</param>
    /// <param name="distance">Its distance in metres, or NaN.</param>
    /// <param name="skip">M5 7 P7u: the guids the caller has set aside (the hunt's refused or blocked targets); null skips none.</param>
    public static bool TryNearest(GameState state, double metres, IReadOnlySet<string> attackers, out uint guid, out string name, out double distance, Func<uint, bool>? skip = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(attackers);
        guid = 0;
        name = string.Empty;
        distance = double.NaN;
        if (state.Self.Position is not { } here || attackers.Count == 0)
        {
            return false;
        }

        var origin = here.ToGlobal();
        var best = metres;
        foreach (var o in state.Objects.All)
        {
            if (o.IsSelf || o.Guid == state.Self.Guid || o.Position is not { } p || !attackers.Contains(o.Name) || (o.ItemType & CreatureItemType) == 0 || (o.DescriptionFlags & AttackableFlag) == 0
                || (o.DescriptionFlags & WorldObjectTable.PlayerFlag) != 0 || o.Health is <= 0f || o.Motion is { Forward: WorldObjectTable.DeadMotionLowWord } || (skip is not null && skip(o.Guid)))
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
    private IDisposable? _combat;
    private IDisposable? _chat;
    private bool _started;

    /// <summary>The names heard since the last <see cref="Clear"/>.</summary>
    public IReadOnlySet<string> Names => _names;

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

    private void OnCombat(in CombatEvent e)
    {
        if (e.Kind is CombatEventKind.DefenseHit or CombatEventKind.DefenseEvaded && !string.IsNullOrEmpty(e.Who))
        {
            Heard(e.Who, e.Kind == CombatEventKind.DefenseHit ? "damage notice" : "evasion notice");
        }
    }

    private void OnChat(in ChatReceived e)
    {
        if (e.Line.ChatType == FightTally.MagicChatType && e.Line.Kind == ChatKind.System && FightScenario.HostileSpellSource(e.Line.Text, out var source, out var how))
        {
            Heard(source, how);
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
