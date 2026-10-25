using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>A travel seam that records every request and arrives at once by <see cref="TravelMeans.None"/> (M4 P5b lane J3a): a station asks for the walk, and the test sees what it asked for.</summary>
internal sealed class J3aTravel : IJourneyTravel
{
    public List<TravelRequest> Requests { get; } = [];

    public bool Busy => false;

    public ITravelLeg GoTo(IGameSessionView session, TravelRequest request, double now)
    {
        Requests.Add(request);
        return new Arrived();
    }

    private sealed class Arrived : ITravelLeg
    {
        public TravelMeans Means => TravelMeans.None;

        public string? Failure => null;

        public string Fields => string.Empty;

        public TravelStatus Step(IGameSessionView session, double now) => TravelStatus.Arrived;

        public void Pause()
        {
        }

        public void Resume(double now)
        {
        }

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// The offline session a station of lane J3a runs against (M4 P5b): a present player on <see cref="WorldActionRig"/> (no character data, no objects), a recording travel, and a frame loop that steps the station's run every quarter second (and a pump for a scripted character) to its first server wait.
/// <see cref="Places"/> counts the <c>place</c> tool calls in the action log: a station never makes one (lanes.md contract 5).
/// </summary>
internal sealed class J3aStationRig
{
    public J3aStationRig(WorldActionRig? rig = null, IReadOnlyDictionary<uint, SpellInfo>? spells = null)
    {
        R = rig ?? new WorldActionRig();
        View = new J3aView(R);
        Spells = spells ?? new Dictionary<uint, SpellInfo>();
    }

    public WorldActionRig R { get; }

    public J3aView View { get; }

    public IReadOnlyDictionary<uint, SpellInfo> Spells { get; }

    public J3aTravel Travel { get; } = new();

    /// <summary>The <c>place</c> tool calls the station made: the action log's and the requests actually sent to the server (an unsettled request is not in the log yet): always 0 for a station.</summary>
    public int Places => Actions(ActionKind.ToolPlace) + ToolRequests("place");

    /// <summary>The player-tool requests of that verb the scripted server was sent (<c>@lifestone &lt;verb&gt; ...</c>).</summary>
    public int ToolRequests(string verb) => R.H.Net.ToolRequests().Count(t => t is not null && t.StartsWith("@lifestone " + verb + " ", StringComparison.Ordinal));

    /// <summary>The count of settled entries of that kind in the action log.</summary>
    public int Actions(ActionKind kind)
    {
        var log = View.ActionLog;
        var count = 0;
        for (var i = 0; i < log.Count; i++)
        {
            count += log[i].Kind == kind ? 1 : 0;
        }

        return count;
    }

    /// <summary>The journey's context for <paramref name="character"/> over this rig.</summary>
    public JourneyContext Context(string character, VitalsKeeper? keeper = null) => new()
    {
        Character = character,
        Account = "test",
        Log = R.H.Log,
        Travel = Travel,
        Spells = Spells,
        Keeper = keeper,
    };

    /// <summary>Starts the station for <paramref name="character"/>.</summary>
    public IStationRun Start(IStation station, string character = "Holtburg Brawler", VitalsKeeper? keeper = null) => station.Start(Context(character, keeper));

    /// <summary>Steps the run once per quarter second for up to <paramref name="frames"/> frames; the last status. A run that ends is not stepped again.</summary>
    public ScenarioStatus Run(IStationRun run, int frames = 40, double dt = 0.25, Action? pump = null)
    {
        var status = ScenarioStatus.Running;
        for (var i = 0; i < frames && status == ScenarioStatus.Running; i++)
        {
            R.Tick(dt);
            pump?.Invoke();
            R.Tick();
            status = run.Step(View, R.H.R.Clock.Now);
        }

        return status;
    }
}
