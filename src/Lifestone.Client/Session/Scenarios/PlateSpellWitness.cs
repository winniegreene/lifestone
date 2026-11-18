using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 14 holding plates (check after): the session's witness of a jump over pressure plates (<see cref="IJumpWitness"/>): a plate that fires casts its spells on her (the profile's <c>plates.spells</c>: Nullify All Magic Other 1878 and
/// Tusker's Gift 2166 in the Tusker Holding), so after the release the chat lines that name the plate or one of its spells (landed or resisted) and a plate spell new in her enchantments say it reached her. Any such line, jump or
/// no jump, is logged as a warning (report-only: the walls are what keeps her off the plates).
/// </summary>
public sealed class PlateSpellWitness : IJumpWitness, IDisposable
{
    private const string LogCategory = "fight";

    private readonly IGameSessionView _session;
    private readonly GroundPlates _plates;
    private readonly ILogSink _log;
    private readonly IDisposable _chat;
    private readonly List<string> _lines = [];
    private HashSet<(ushort Spell, ushort Layer, double Start)> _before = [];
    private bool _armed;

    /// <summary>A witness of <paramref name="plates"/> over <paramref name="session"/>'s chat lines and her enchantments.</summary>
    /// <param name="session">The session.</param>
    /// <param name="plates">The ground's plates.</param>
    /// <param name="log">Where a plate's line is logged.</param>
    public PlateSpellWitness(IGameSessionView session, GroundPlates plates, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(plates);
        ArgumentNullException.ThrowIfNull(log);
        (_session, _plates, _log) = (session, plates, log);
        _chat = session.Events.Subscribe<ChatReceived>((in ChatReceived e) => Heard(e.Line));
    }

    /// <summary>The plate lines heard since the last take-off (all of them before the first).</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <inheritdoc/>
    public void TookOff(DirectedJump jump)
    {
        _lines.Clear();
        _before = [.. PlateEnchantments()];
        _armed = true;
    }

    /// <inheritdoc/>
    public string? Fired()
    {
        if (!_armed)
        {
            return null;
        }

        if (_lines.Count > 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"the chat line \"{_lines[0]}\"");
        }

        foreach (var e in PlateEnchantments())
        {
            if (!_before.Contains(e))
            {
                var name = _plates.Spells.FirstOrDefault(s => s.Id == e.Spell)?.Name ?? "a plate spell";
                return string.Create(CultureInfo.InvariantCulture, $"{name} {e.Spell} is on her, cast after the take-off");
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public void Dispose() => _chat.Dispose();

    /// <summary>True when <paramref name="text"/> names the plate or one of its spells.</summary>
    /// <param name="plates">The plates.</param>
    /// <param name="text">A chat line's text.</param>
    public static bool NamesPlate(GroundPlates plates, string text)
    {
        ArgumentNullException.ThrowIfNull(plates);
        ArgumentNullException.ThrowIfNull(text);
        return text.Contains(plates.Name, StringComparison.Ordinal) || plates.Spells.Any(s => text.Contains(s.Name, StringComparison.Ordinal));
    }

    private void Heard(ChatLine line)
    {
        if (!NamesPlate(_plates, line.Text) && (line.Sender is null || !NamesPlate(_plates, line.Sender)))
        {
            return;
        }

        _lines.Add(line.Text);
        _log.Warn(LogCategory, "a pressure plate's spell reached the player (report-only)", new LogField("line", line.Text), new LogField("sender", line.Sender ?? "none"), new LogField("plate", _plates.Name));
    }

    private IEnumerable<(ushort Spell, ushort Layer, double Start)> PlateEnchantments()
    {
        var q = _session.State.Self.Qualities;
        if (q.Enchantments.IsDefaultOrEmpty)
        {
            yield break;
        }

        foreach (var e in q.Enchantments)
        {
            if (_plates.Spells.Any(s => s.Id == e.SpellId))
            {
                yield return (e.SpellId, e.Layer, e.StartTime);
            }
        }
    }
}
