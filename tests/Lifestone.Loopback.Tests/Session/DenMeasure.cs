using System.Globalization;
using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests;
using Lifestone.Client.World;
using Lifestone.Net.Types;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>Plain statistics and the one-line output of a measure (M5 item 0, c4).</summary>
internal static class MeasureText
{
    /// <summary><c>n=3 min=1 median=2 max=3</c>, or <c>n=0</c>.</summary>
    internal static string Dist(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
        {
            return "n=0";
        }

        var s = values.Order().ToArray();
        return string.Create(CultureInfo.InvariantCulture, $"n={s.Length} min={s[0]:0.##} median={Median(s):0.##} max={s[^1]:0.##}");
    }

    /// <summary>The median of an ordered array (NaN when empty).</summary>
    internal static double Median(double[] ordered) => ordered.Length == 0 ? double.NaN : ordered.Length % 2 == 1 ? ordered[ordered.Length / 2] : (ordered[(ordered.Length / 2) - 1] + ordered[ordered.Length / 2]) / 2.0;

    /// <summary>The line as the test prints it and as <c>cache/m5/0/measures/&lt;name&gt;.txt</c> of the running checkout keeps it (derived data, gitignored); <paramref name="more"/> are extra detail lines for the file and the output.</summary>
    internal static void Emit(ITestOutputHelper output, string name, string fields, IEnumerable<string>? more = null)
    {
        var line = $"[measure] {name} {fields}";
        output.WriteLine(line);
        var lines = new List<string> { line };
        foreach (var m in more ?? [])
        {
            output.WriteLine("[measure] " + name + " | " + m);
            lines.Add(m);
        }

        var dir = Path.Combine(RepositoryFiles.Root, "cache", "m5", "0", "measures");
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, name + ".txt"), lines);
    }
}

/// <summary>
/// M5 item 0 (c4): what the measuring legs watch while they run, from the session's own events and logs (no new mod verb, no engine): the creature creations and deaths of the tuskers, the player's magic lines, the vital pushes, the
/// action log's casts, the missiles created, the player's position and the held components. All subscriptions are made and released on the session's game thread (a leg's <see cref="Tick"/> and <see cref="Stop"/>), as the loopback legs do.
/// </summary>
internal sealed class DenMeasure
{
    /// <summary>The ItemType bit of a spell component (<c>GearKit.ItemTypeSpellComponents</c>).</summary>
    private const uint ComponentItemType = 0x1000;

    private readonly List<IDisposable> _subscriptions = [];
    private readonly HashSet<uint> _seenGuids = [];
    private IGameSessionView? _s;
    private long _logTotal;

    /// <summary>A tusker object the table received.</summary>
    internal readonly record struct Create(double T, uint Guid, uint Wcid, uint Cell, bool NewGuid, bool Respawn);

    /// <summary>A tusker's Dead motion.</summary>
    internal readonly record struct Death(double T, uint Guid, uint Wcid, uint Cell);

    /// <summary>One magic line of the server's, parsed.</summary>
    internal readonly record struct MagicLine(double T, MagicLineKind Kind, long Amount, bool Critical, string Text);

    /// <summary>One vital reading (a push of the private update, or a once-a-second sample).</summary>
    internal readonly record struct VitalSample(double T, uint Health, uint HealthMax, uint Stamina, uint StaminaMax, uint Mana, uint ManaMax);

    /// <summary>A projectile (Missile object) the server created.</summary>
    internal readonly record struct Missile(double T, uint Guid, uint Wcid, uint Cell, Vector3 Velocity);

    /// <summary>Where the player was.</summary>
    internal readonly record struct Pos(double T, double X, double Y, uint Cell);

    internal List<Create> Creates { get; } = [];

    internal List<Death> Deaths { get; } = [];

    internal List<double> KillTexts { get; } = [];

    internal List<MagicLine> Magic { get; } = [];

    internal List<VitalSample> Vitals { get; } = [];

    internal List<Missile> Missiles { get; } = [];

    internal List<Pos> Positions { get; } = [];

    internal List<ActionLogEntry> Actions { get; } = [];

    internal List<double> PlayerDeaths { get; } = [];

    private double _lastSample = double.NegativeInfinity;
    private uint _sweepMaxGuid;
    private bool _sealed;

    /// <summary>The highest dynamic guid (0x80000000 and up) of any object seen up to <see cref="SealSweep"/>: the first sweep's ceiling.</summary>
    internal uint FirstSweepMaxGuid => _sweepMaxGuid;

    /// <summary>Ends the first sweep: objects seen so far were loaded with the den; a dynamic guid above the highest of them was allocated after it (ACE's GuidManager hands dynamic guids out in increasing order from 0x80000000).</summary>
    internal void SealSweep() => _sealed = true;

    /// <summary>The creates that count as respawns by the rule above, and the rule's text.</summary>
    internal const string RespawnRule = "a Tusker create whose guid is dynamic (0x80000000 up) and above the highest dynamic guid seen in the first sweep (ACE's dynamic guids only increase), or a re-create of a guid that already had a Dead motion; each paired with the earliest unpaired earlier death";

    /// <summary>Subscribes (call on the game thread).</summary>
    internal void Start(IGameSessionView session)
    {
        _s = session;
        _logTotal = session.ActionLog.Total;
        foreach (var o in session.State.Objects.All)
        {
            if (o.Guid >= 0x80000000u)
            {
                _sweepMaxGuid = Math.Max(_sweepMaxGuid, o.Guid);
            }

            if (IsTusker(o))
            {
                _seenGuids.Add(o.Guid);
            }
        }

        _subscriptions.Add(session.Events.Subscribe<ObjectCreated>(OnCreated));
        _subscriptions.Add(session.Events.Subscribe<ObjectMotionChanged>(OnMotion));
        _subscriptions.Add(session.Events.Subscribe<ChatReceived>(OnChat));
        _subscriptions.Add(session.Events.Subscribe<PlayerKilledSomething>(OnKilled));
        _subscriptions.Add(session.Events.Subscribe<PlayerDied>(OnDied));
        _subscriptions.Add(session.Events.Subscribe<QualitiesChanged>(OnQualities));
        SampleVitals(session.Clock.Now);
    }

    /// <summary>Releases the subscriptions (on the game thread).</summary>
    internal void Stop()
    {
        foreach (var s in _subscriptions)
        {
            s.Dispose();
        }

        _subscriptions.Clear();
    }

    /// <summary>Once a frame: the position, the new action-log entries and a vital sample each second.</summary>
    internal void Tick()
    {
        var s = _s ?? throw new InvalidOperationException("DenMeasure.Start was not called");
        var now = s.Clock.Now;
        if (s.State.Self.Position is { } p && (Positions.Count == 0 || Positions[^1].T < now))
        {
            var g = p.ToGlobal();
            Positions.Add(new Pos(now, g.X, g.Y, p.CellId));
        }

        var log = s.ActionLog;
        var fresh = (int)Math.Min(log.Count, log.Total - _logTotal);
        for (var i = log.Count - fresh; i < log.Count; i++)
        {
            Actions.Add(log[i]);
        }

        _logTotal = log.Total;
        if (now - _lastSample >= 1.0)
        {
            SampleVitals(now);
        }
    }

    private static bool IsTusker(WorldObject o) => o.Weenie.WeenieClassId is 1628 or 1629;

    private void OnKilled(in PlayerKilledSomething e) => KillTexts.Add(_s!.Clock.Now);

    private void OnDied(in PlayerDied e) => PlayerDeaths.Add(_s!.Clock.Now);

    private void OnCreated(in ObjectCreated e)
    {
        var s = _s;
        if (s is null || e.IsSelf || !s.State.Objects.TryGet(e.Guid, out var o))
        {
            return;
        }

        if (!_sealed && e.Guid >= 0x80000000u)
        {
            _sweepMaxGuid = Math.Max(_sweepMaxGuid, e.Guid);
        }

        var now = s.Clock.Now;
        var cell = o.Position?.CellId ?? 0;
        if (IsTusker(o))
        {
            var guid = e.Guid;
            var isNew = _seenGuids.Add(guid);
            var respawn = _sealed && ((isNew && guid >= 0x80000000u && guid > _sweepMaxGuid) || Deaths.Any(d => d.Guid == guid));
            Creates.Add(new Create(now, guid, o.Weenie.WeenieClassId, cell, isNew, respawn));
        }
        else if ((o.State & PhysicsState.Missile) != 0)
        {
            Missiles.Add(new Missile(now, e.Guid, o.Weenie.WeenieClassId, cell, o.Velocity));
        }
    }

    private void OnMotion(in ObjectMotionChanged e)
    {
        var s = _s;
        if (s is null || e.IsSelf || e.Motion.Forward != WorldObjectTable.DeadMotionLowWord || !s.State.Objects.TryGet(e.Guid, out var o) || !IsTusker(o))
        {
            return;
        }

        var guid = e.Guid;
        if (Deaths.Any(d => d.Guid == guid))
        {
            return; // the Dead motion is repeated by later updates
        }

        Deaths.Add(new Death(s.Clock.Now, e.Guid, o.Weenie.WeenieClassId, o.Position?.CellId ?? 0));
    }

    private void OnChat(in ChatReceived e)
    {
        var s = _s;
        if (s is null || e.Line.ChatType != FightTally.MagicChatType || e.Line.Kind != ChatKind.System)
        {
            return;
        }

        var kind = FightTally.Classify(e.Line.Text, out var amount, out var critical);
        Magic.Add(new MagicLine(s.Clock.Now, kind, amount, critical, e.Line.Text));
    }

    private void OnQualities(in QualitiesChanged e)
    {
        if (e.Change is QualityChange.Vital or QualityChange.VitalCurrent && _s is { } s)
        {
            SampleVitals(s.Clock.Now);
        }
    }

    private void SampleVitals(double now)
    {
        _lastSample = now;
        var q = _s!.State.Self.Qualities;
        if (q.TryGetVitalLevels(MageTemplate.MaxHealth, out var h, out var hm) && q.TryGetVitalLevels(MageTemplate.MaxStamina, out var st, out var sm) && q.TryGetVitalLevels(MageTemplate.MaxMana, out var m, out var mm))
        {
            Vitals.Add(new VitalSample(now, h, hm, st, sm, m, mm));
        }
    }

    /// <summary>The latest vital reading at or before <paramref name="t"/>, or null.</summary>
    internal VitalSample? VitalAt(double t)
    {
        VitalSample? found = null;
        foreach (var v in Vitals)
        {
            if (v.T > t)
            {
                break;
            }

            found = v;
        }

        return found;
    }

    /// <summary>The seconds in <c>[from, to]</c> the mana was under <paramref name="ceiling"/> (the reading is held until the next one).</summary>
    internal double SecondsManaBelow(uint ceiling, double from, double to)
    {
        double total = 0;
        for (var i = 0; i < Vitals.Count; i++)
        {
            var start = Math.Max(Vitals[i].T, from);
            var end = Math.Min(i + 1 < Vitals.Count ? Vitals[i + 1].T : to, to);
            if (end > start && Vitals[i].Mana < ceiling)
            {
                total += end - start;
            }
        }

        return total;
    }

    /// <summary>The spell-component stacks held (pack, side packs and worn), by weenie id: the sum of the stack sizes.</summary>
    internal static SortedDictionary<uint, int> Components(IGameSessionView s)
    {
        var counts = new SortedDictionary<uint, int>();
        foreach (var o in s.State.Objects.All)
        {
            if ((o.ItemType & ComponentItemType) == 0 || !s.State.Self.Inventory.Contains(o.Guid))
            {
                continue;
            }

            var size = s.State.Self.Inventory.TryGetStack(o.Guid, out var stack, out _) ? (int)stack : (int)(o.Weenie.StackSize ?? 1u);
            counts[o.Weenie.WeenieClassId] = counts.GetValueOrDefault(o.Weenie.WeenieClassId) + size;
        }

        return counts;
    }

    /// <summary><c>625:100 626:55</c>.</summary>
    internal static string Format(IDictionary<uint, int> counts) => counts.Count == 0 ? "none" : string.Join(' ', counts.Select(kv => $"{kv.Key}:{kv.Value}"));

    /// <summary>
    /// The player's runs of at least <paramref name="minMetres"/> metres without a stop (a sample step over 20 m/s is a teleport and ends the run; a step under 2 m/s ends it): metres, seconds, metres per second.
    /// </summary>
    internal IEnumerable<(double Metres, double Seconds, double Speed)> Legs(double minMetres, double from = double.NegativeInfinity)
    {
        double metres = 0;
        double seconds = 0;
        for (var i = 1; i < Positions.Count; i++)
        {
            var a = Positions[i - 1];
            var b = Positions[i];
            if (b.T < from)
            {
                continue;
            }

            var dt = b.T - a.T;
            var d = Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));
            var speed = dt > 0 ? d / dt : 0;
            if (speed is >= 2.0 and <= 20.0)
            {
                metres += d;
                seconds += dt;
                continue;
            }

            if (metres >= minMetres && seconds > 0)
            {
                yield return (metres, seconds, metres / seconds);
            }

            metres = 0;
            seconds = 0;
        }

        if (metres >= minMetres && seconds > 0)
        {
            yield return (metres, seconds, metres / seconds);
        }
    }

    /// <summary>
    /// Each respawn create (<see cref="RespawnRule"/>) paired with the earliest unpaired death before it, in time order; the delays, the respawn creates, the pairs and the deaths left unpaired. A first sighting of a
    /// tusker that was alive before the fight is never a respawn. Also the pairs whose cells agree, by cell (secondary: tuskers die where they chased the mage, not at their spawn cell).
    /// </summary>
    internal (List<double> Delays, Dictionary<uint, List<double>> ByCell, int RespawnCreates, int UnpairedDeaths) Respawns(double from)
    {
        var creates = Creates.Where(c => c.Respawn && c.T >= from).OrderBy(c => c.T).ToList();
        var deaths = Deaths.Where(x => x.T >= from).OrderBy(x => x.T).ToList();
        var deathUsed = new HashSet<int>();
        var delays = new List<double>();
        var byCell = new Dictionary<uint, List<double>>();
        foreach (var c in creates)
        {
            var i = deaths.FindIndex(d => d.T < c.T && !deathUsed.Contains(deaths.IndexOf(d)));
            if (i < 0)
            {
                continue;
            }

            deathUsed.Add(i);
            delays.Add(c.T - deaths[i].T);
            if (deaths[i].Cell == c.Cell)
            {
                byCell.TryAdd(c.Cell, []);
                byCell[c.Cell].Add(c.T - deaths[i].T);
            }
        }

        return (delays, byCell, creates.Count, deaths.Count - deathUsed.Count);
    }
}
