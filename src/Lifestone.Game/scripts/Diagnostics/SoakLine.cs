using System.Diagnostics;
using System.Text;
using Godot;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session;
using Lifestone.Game.Core;
using Lifestone.Game.World.ServerObjects;

namespace Lifestone.Game.Diagnostics;

/// <summary>
/// The soak's in-process line (M5 item 0 c3, <c>m5-memory-soak</c>; DoD 13): one INFO line <c>[soak] ws_mb=... private_mb=...</c> every
/// <c>--soak-line-s</c> seconds (default 60; 0 = none), so a run of a day leaves a time series of memory, collections, frames, the object
/// counts, the link, Godot's errors and the log size in <c>lifestone.log</c>, whatever happens to the run (the summaries that other systems
/// write at exit are lost to a timeout kill). The fields and their sources are cache/m5/longrun-risks.md section 4's table;
/// <see cref="SoakLineFormat"/> builds the line.
/// </summary>
/// <remarks>
/// <para>A child of the world beside <see cref="World.Objects.ObjectsSystem"/> (<c>ObjectsLayer.Install</c>). The frame path does one
/// <see cref="SoakFrameWindow.Add"/> and one clock read: no allocation. Everything else (the process, the collector, Godot's monitors, the
/// counts, the string) happens once per interval, into one reused <see cref="StringBuilder"/> and <see cref="SoakSnapshot"/>.</para>
/// <para>Play mode binds the session, the avatar set and the light table with <see cref="Bind"/>; until then (and in a scene without a session) their fields read
/// <c>n/a</c>. An exception while sampling or logging is logged once as an error and never reaches the frame.</para>
/// <para><c>kills</c>, <c>deaths</c> and <c>cycles</c> read <c>n/a</c> until item 4's driver reports them (<c>TODO(m5-soak-tallies)</c>, logged once at the start).</para>
/// </remarks>
public partial class SoakLine : Node
{
    private const double Mb = 1024.0 * 1024.0;
    private const ulong SilenceSampleMilliseconds = 250;

    private readonly SoakSnapshot _snapshot = new();
    private readonly SoakFrameWindow _frames = new();
    private readonly StringBuilder _builder = new(768);
    private double _intervalSeconds;
    private ulong _intervalMilliseconds;
    private ulong _nextLineAt;
    private ulong _nextSilenceSampleAt;
    private double _silenceMaxSeconds;
    private bool _silenceSeen;
    private bool _failureLogged;
    private GameSession? _session;
    private Func<AvatarSet?>? _avatars;
    private ObjectLightTable? _lightTable;

    /// <summary>The seconds between two lines (0 = none).</summary>
    public double IntervalSeconds => _intervalSeconds;

    /// <summary>Lines written so far.</summary>
    public int Lines { get; private set; }

    /// <summary>Starts the clock: the first line comes <paramref name="intervalSeconds"/> from now. 0 or less turns the node off.</summary>
    /// <param name="intervalSeconds"><c>--soak-line-s</c>.</param>
    public void Configure(double intervalSeconds)
    {
        _intervalSeconds = Math.Max(0.0, intervalSeconds);
        _intervalMilliseconds = (ulong)Math.Max(1.0, _intervalSeconds * 1000.0);
        _nextLineAt = Time.GetTicksMsec() + _intervalMilliseconds;
        _nextSilenceSampleAt = 0;
        _silenceMaxSeconds = 0.0;
        _silenceSeen = false;
        _frames.Reset();
        SetProcess(_intervalSeconds > 0.0);
    }

    /// <summary>Gives the line its play-mode sources: the live session, the avatar set (a function: a log off builds a new one), and the object light table. Any may be null.</summary>
    /// <param name="session">The session.</param>
    /// <param name="avatars">The current avatar set.</param>
    /// <param name="lightTable">The light table.</param>
    public void Bind(GameSession? session, Func<AvatarSet?>? avatars, ObjectLightTable? lightTable)
    {
        _session = session;
        _avatars = avatars;
        _lightTable = lightTable;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always; // a paused tree is still part of the soak
        SetProcess(_intervalSeconds > 0.0);
        if (_intervalSeconds > 0.0)
        {
            Log.Info(
                SoakLineFormat.Category,
                "soak line on",
                ("every_s", _intervalSeconds),
                ("todo_na", string.Join(',', SoakLineFormat.Todo.Select(t => t.Field + ":" + t.Todo))),
                ("godot_errors", GodotErrorLogger.Installed ? "counted" : "n/a TODO(m5-soak-godot-errors)"));
        }
    }

    public override void _Process(double delta)
    {
        try
        {
            _frames.Add(delta * 1000.0);
            var now = Time.GetTicksMsec();
            if (now >= _nextSilenceSampleAt)
            {
                _nextSilenceSampleAt = now + SilenceSampleMilliseconds;
                if ((_session as Lifestone.Client.Session.Contracts.IGameSessionView)?.SecondsSinceLastServerPacket is { } silence)
                {
                    _silenceSeen = true;
                    _silenceMaxSeconds = Math.Max(_silenceMaxSeconds, silence);
                }
            }

            if (now >= _nextLineAt)
            {
                _nextLineAt = now + _intervalMilliseconds;
                WriteLine();
            }
        }
        catch (Exception ex)
        {
            // Never the frame's problem: logged once, and the next interval tries again.
            if (!_failureLogged)
            {
                _failureLogged = true;
                Log.Error(SoakLineFormat.Category, $"the soak line threw ({ex.GetType().Name}: {ex.Message}); logged once, the frame goes on", ("stack", ex.StackTrace));
            }
        }
    }

    private void WriteLine()
    {
        var s = _snapshot;
        using (var process = Process.GetCurrentProcess())
        {
            s.WsMb = process.WorkingSet64 / Mb;
            s.PrivateMb = process.PrivateMemorySize64 / Mb;
        }

        var info = GC.GetGCMemoryInfo(GCKind.Any);
        s.ManagedMb = GC.GetTotalMemory(false) / Mb;
        s.CommittedMb = info.TotalCommittedBytes / Mb;
        s.FragMb = info.FragmentedBytes / Mb;
        s.LohMb = info.GenerationInfo.Length > 3 ? info.GenerationInfo[3].SizeAfterBytes / Mb : null; // generation 3 is the large object heap
        s.Gen0 = GC.CollectionCount(0);
        s.Gen1 = GC.CollectionCount(1);
        s.Gen2 = GC.CollectionCount(2);
        s.GcPauseMs = GC.GetTotalPauseDuration().TotalMilliseconds;

        s.StaticMb = OS.GetStaticMemoryUsage() / Mb;
        s.VideoMb = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) / Mb;
        s.TexMb = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TextureMemUsed) / Mb;
        s.Nodes = (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
        s.Orphans = (long)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
        s.Resources = (long)Performance.GetMonitor(Performance.Monitor.ObjectResourceCount);

        var session = _session;
        s.Objects = session?.State.Objects.Count;
        s.CullPending = session?.Cull.Pending; // M5 0 fix 0-R11: what 0-R1's ghosts would show in the 24 h series
        s.Culled = session?.Cull.Culled;
        s.Phase = session?.State.Phase.ToString();
        s.Reconnects = session?.Reconnects;
        s.TickMs = session?.LastTickMs;
        s.PumpMsgs = session?.LastPumpMessages;
        if (session is null)
        {
            s.Sent = s.Recv = s.Resends = s.Naks = s.Dup = s.Gaps = null;
            s.LossPct = null;
        }
        else
        {
            var link = session.Link;
            s.Sent = link.PacketsSent;
            s.Recv = link.PacketsReceived;
            s.Resends = link.Resends;
            s.Naks = link.NaksSent;
            s.Dup = link.DuplicatesDropped;
            s.Gaps = link.GapSkips;
            s.LossPct = link.LossPercent;
        }

        s.SilenceMaxS = _silenceSeen ? _silenceMaxSeconds : null;

        if (_avatars?.Invoke() is { } avatars)
        {
            var all = avatars.All;
            var inCell = 0;
            for (var i = 0; i < all.Count; i++)
            {
                if (all[i].Body.InCell)
                {
                    inCell++;
                }
            }

            s.Avatars = all.Count;
            s.InCell = inCell;
        }
        else
        {
            s.Avatars = null;
            s.InCell = null;
        }

        s.LightRows = _lightTable?.InUse;
        s.FrameMaxMs = _frames.Max;
        s.FrameP99Ms = _frames.P99();
        s.Over50 = _frames.Over50;
        s.Over75 = _frames.Over75;
        s.Kills = null; // TODO(m5-soak-tallies): item 4's cycle driver
        s.Deaths = null;
        s.Cycles = null;
        s.GodotErrors = GodotErrorLogger.Installed ? GodotErrorLogger.Count : null;
        s.LogBytes = Log.FileLength();

        _builder.Clear();
        SoakLineFormat.Append(_builder, s);
        Log.Info(SoakLineFormat.Category, _builder.ToString());
        Lines++;
        _frames.Reset();
        _silenceMaxSeconds = 0.0;
        _silenceSeen = false;
    }
}
