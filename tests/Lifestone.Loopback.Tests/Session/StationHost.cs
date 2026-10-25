using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;
using Lifestone.Tests.Shared.Loopback;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// What the loopback facts that host a journey station share (M4 P5b J3a): the station's context over the shared session. The facts may place (a <see cref="PlaceTravel"/>-backed travel, today's behaviour); the stations themselves never do.
/// The keeper is none unless a fact passes one, and the dats are the fact's (so the station's own follower walks the client's body after a server move, as the old loopback leg's did).
/// </summary>
internal static class StationHost
{
    /// <summary>A context for <paramref name="character"/> over <paramref name="dats"/>.</summary>
    internal static JourneyContext Context(string character, DatSet dats, RecordingLogSink? log = null, IJourneyTravel? travel = null, VitalsKeeper? keeper = null) => new()
    {
        Character = character,
        Account = LoopbackAccounts.For(character),
        Log = log ?? new RecordingLogSink(),
        Travel = travel ?? new PlaceTravel(),
        Spells = SpellTableData.Load(dats).Spells,
        Keeper = keeper,
        Dats = dats,
    };
}
