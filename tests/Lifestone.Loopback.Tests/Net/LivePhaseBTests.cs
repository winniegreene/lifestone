using System.Globalization;
using System.Numerics;
using Lifestone.Data.Dat;
using Lifestone.Data.Motion;
using Lifestone.Data.Terrain;
using Lifestone.Data.World.Scenery;
using Lifestone.Net.Session;
using Lifestone.Net.Types;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;
using static Lifestone.Loopback.Tests.Net.LivePlacement;
using static Lifestone.Loopback.Tests.Net.LiveSteps;

namespace Lifestone.Loopback.Tests.Net;

/// <summary>
/// The net track's live phase B (m2-plan N12; DoD 2 and 12) against the bundled ACE on 127.0.0.1, on the test account with the loopback character
/// (<see cref="LiveNetClient.LoopbackCharacter"/>): the teleport to Holtburg through the Lifestone server mod, a walk there that ACE accepts, and the two minute
/// lossy soak. Every test ends with the character back at its start (the Training Academy), so the phase-A tests, the headless loopback tests and the game's login
/// check find it where they expect it, in any order. Skipped, with the probe's reason, unless the backend runs with the mod ready.
/// </summary>
/// <remarks>
/// The tests run in step order (<see cref="LiveStepAttribute"/>, after phase A's 1 and 2). The soak carries <c>Tier=Soak</c>, which default runs and the plain loopback
/// run exclude (<c>--filter "Category=Loopback&amp;Tier=Soak"</c> runs it).
/// </remarks>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[TestCaseOrderer("Lifestone.Loopback.Tests.Net.LiveStepOrderer", "Lifestone.Loopback.Tests")]
public sealed class LivePhaseBTests(ITestOutputHelper output)
{
    private const uint HoltburgLandblock = 0xA9B4;

    /// <summary>How close the server's position must be to what the client sent or was told (m2-plan N12, docs/protocol.md section 9.4 step 9).</summary>
    private const float Tolerance = 0.5f;

    /// <summary>A lap of Holtburg's landblock (metres in the landblock frame, north from the town's teleport destination, then east, south and back).</summary>
    private static readonly Vector2[] HoltburgLap = [new(84f, 100f), new(140f, 100f), new(140f, 8f), new(84f, 8f)];

    [LoopbackFact]
    [LiveStep(10)]
    public void Place_poi_Holtburg_sends_PlayerTeleport_and_UpdatePosition_in_landblock_A9B4_and_place_loc_brings_the_character_back()
    {
        using var dats = DatSet.Open();
        using var client = LiveNetClient.Create(dats, retail: false, LiveNetClient.LoopbackCharacter);
        var start = default(AcPosition);
        var back = false;
        try
        {
            start = EnterReadyAtStart(client, output);
            var objectsBefore = client.Sink.OtherObjects;

            var arrival = PlaceAndArrive(client, new PlacePoiRequest("Holtburg", NextId()));
            output.WriteLine($"place poi Holtburg: {arrival.Place.Describe()}; F751 x{arrival.Teleports}; F748 cell 0x{arrival.Position.Position.CellId:X8} seqs teleport {arrival.Position.TeleportSeq} force {arrival.Position.ForcePositionSeq}");
            Assert.Equal("poi", arrival.Place.Kind);
            Assert.Equal("Holtburg", arrival.Place.Name, ignoreCase: true);
            Assert.Equal(HoltburgLandblock, LandblockOf(arrival.Place.Cell));
            Assert.True(arrival.Teleports >= 1, "no F751 PlayerTeleport");
            Assert.Equal(HoltburgLandblock, arrival.Position.Position.CellId >> 16);
            var destination = new Vector3(arrival.Place.X, arrival.Place.Y, arrival.Place.Z);
            Assert.True(
                Vector3.Distance(arrival.Position.Position.Origin, destination) <= Tolerance,
                $"the F748 puts the player at {arrival.Position.Position.Origin}, {Vector3.Distance(arrival.Position.Position.Origin, destination):F2} m from the place reply's {destination}");

            Assert.Equal(HoltburgLandblock, LandblockOf(arrival.Where.Cell));
            Assert.False(arrival.Where.Teleporting);
            Assert.True(Distance(arrival.Where, arrival.Position.Position) <= Tolerance, $"where is {Distance(arrival.Where, arrival.Position.Position):F2} m from the F748");

            // Holtburg's statics, NPCs and the lifestone arrive as CreateObjects after the teleport.
            client.PumpUntil(() => client.Sink.OtherObjects > objectsBefore, TimeSpan.FromSeconds(10));
            output.WriteLine($"objects sent for Holtburg: {client.Sink.OtherObjects - objectsBefore}; malformed: {client.Sink.Malformed.Count}");
            Assert.True(client.Sink.OtherObjects > objectsBefore, "ACE sent no CreateObject for Holtburg");
            Assert.Empty(client.Sink.Malformed);

            // And back: an exact location in the Academy's EnvCell, the other verb.
            var returned = ReturnToStart(client, start);
            back = true;
            output.WriteLine($"place loc back: cell {returned.Where.Cell} at ({returned.Where.X:F2}, {returned.Where.Y:F2}, {returned.Where.Z:F2}); F751 x{returned.Teleports}");
            Assert.Equal(start.CellId, ToolsWire.TryParseCell(returned.Where.Cell, out var cell) ? cell : 0u);

            LogOffAndQuit(client);
        }
        finally
        {
            if (!back)
            {
                RestoreBestEffort(client, start, output);
            }

            LeaveTheWorld(client);
        }
    }

    [LoopbackFact]
    [LiveStep(11)]
    public void Walking_Holtburg_on_the_retail_cadence_keeps_where_within_half_a_metre_and_the_force_position_sequence_still()
    {
        using var dats = DatSet.Open();
        using var client = LiveNetClient.Create(dats, retail: false, LiveNetClient.LoopbackCharacter);
        var start = default(AcPosition);
        var back = false;
        try
        {
            start = EnterReadyAtStart(client, output);
            var arrival = PlaceAndArrive(client, new PlacePoiRequest("Holtburg", NextId()));
            var forceSeq = arrival.Where.ForcePositionSeq;
            output.WriteLine($"at Holtburg {arrival.Where.Cell} ({arrival.Where.X:F2}, {arrival.Where.Y:F2}, {arrival.Where.Z:F2}); force-position sequence {forceSeq}, teleport sequence {arrival.Where.TeleportSeq}");

            var sink = client.Sink;
            var (teleports, positions) = (sink.Teleports, sink.SelfPositions.Count);
            var walker = HoltburgWalker(dats, new Vector2(arrival.Place.X, arrival.Place.Y));
            var driver = new WalkDriver(client, walker, new PositionCadence());

            // Fifteen seconds in three legs; between them the server's own view is read (the walker stands while the question is asked).
            for (var leg = 1; leg <= 3; leg++)
            {
                driver.Run(TimeSpan.FromSeconds(5));
                var where = AskData<WhereData>(client, new WhereRequest(NextId()));
                var sent = driver.LastSent!.Value;
                output.WriteLine($"leg {leg}: walked {walker.Metres:F1} m, {driver.AutonomousPositions} AutonomousPosition + {driver.MoveToStates} MoveToState sent; where ({where.X:F2}, {where.Y:F2}, {where.Z:F2}) is {Distance(where, sent):F2} m from the last position sent; force {where.ForcePositionSeq}");
                Assert.True(Distance(where, sent) <= Tolerance, $"leg {leg}: ACE has the player {Distance(where, sent):F2} m from the last position the client sent");
                Assert.Equal(forceSeq, where.ForcePositionSeq);
                Assert.False(where.Teleporting);
            }

            driver.Stop();
            client.PumpUntil(() => false, TimeSpan.FromSeconds(2));
            var final = AskData<WhereData>(client, new WhereRequest(NextId()));
            var last = driver.LastSent!.Value;
            output.WriteLine($"stopped: where {final.Cell} ({final.X:F2}, {final.Y:F2}, {final.Z:F2}), {Distance(final, last):F2} m from the last position sent; F748 for the player during the walk: {sink.SelfPositions.Count - positions}");
            Assert.True(walker.Metres > 30, $"the walker covered only {walker.Metres:F1} m");
            Assert.True(driver.AutonomousPositions >= 8, $"only {driver.AutonomousPositions} AutonomousPosition in 15 s on the retail cadence");
            Assert.Equal(HoltburgLandblock, LandblockOf(final.Cell));
            Assert.True(Distance(final, last) <= Tolerance, $"ACE has the player {Distance(final, last):F2} m from the last position the client sent");
            Assert.Equal(forceSeq, final.ForcePositionSeq);
            Assert.Equal(arrival.Where.TeleportSeq, final.TeleportSeq);
            Assert.All(sink.SelfPositions.Skip(positions), p => Assert.Equal(forceSeq, p.ForcePositionSeq));
            Assert.Equal(teleports, sink.Teleports);
            Assert.Empty(sink.Malformed);
            Assert.Equal(SessionPhase.InWorld, client.Session.Phase);

            // ACE echoes the accepted position back as F748 (docs/protocol.md section 6): each is one the client sent.
            var echoes = sink.SelfPositions.Skip(positions).ToList();
            var worst = echoes.Count == 0 ? 0f : echoes.Max(e => driver.Sent.Min(s => Vector3.Distance(s.Position.Origin, e.Position.Origin)));
            output.WriteLine($"{echoes.Count} F748 echoes for {driver.Sent.Count} messages sent; the one furthest from every position sent is {worst:F2} m away");
            Assert.NotEmpty(echoes);
            Assert.True(worst <= Tolerance, $"an F748 puts the player {worst:F2} m from every position the client sent");

            ReturnToStart(client, start);
            back = true;
            LogOffAndQuit(client);
        }
        finally
        {
            if (!back)
            {
                RestoreBestEffort(client, start, output);
            }

            LeaveTheWorld(client);
        }
    }

    [LoopbackFact]
    [LiveStep(12)]
    public void Duplicated_encrypted_client_packets_are_counted_by_ACE_as_bad_checksums_and_the_session_survives()
    {
        // The positive control of the soak's oracle: on a link that only duplicates (3% each way, no loss, no reordering) the duplicate count is not zero, so
        // "ACE's bad checksums equal the duplicate deliveries" says something. A short walk; the soak below is the long one.
        using var dats = DatSet.Open();
        var link = new LossyLink(seed: 21, dropPercent: 0, swapPercent: 0, duplicatePercent: 3);
        var (before, after) = WalkOverLossyLink(dats, link, TimeSpan.FromSeconds(30));
        var crcErrors = after.C2SCrcErrors - before.C2SCrcErrors;
        var duplicates = link.ClientToServer.DuplicateEncryptedDeliveries;
        output.WriteLine($"ACE counted {crcErrors} bad checksums; the link made {duplicates} duplicate encrypted deliveries ({link.Describe()})");
        Assert.True(duplicates > 0, "the duplicating link made no duplicate encrypted delivery in 30 s: " + link.Describe());
        Assert.True(crcErrors == duplicates, $"ACE counted {crcErrors} bad checksums, the link made {duplicates} duplicate encrypted deliveries ({link.Describe()})");
    }

    [LoopbackFact]
    [LiveStep(20)]
    [Trait("Tier", "Soak")]
    public void Two_minute_walk_over_a_five_percent_loss_two_percent_reorder_link_leaves_ACEs_CRC_count_equal_to_the_duplicate_deliveries()
    {
        using var dats = DatSet.Open();
        var link = new LossyLink(seed: 12, dropPercent: 5, swapPercent: 2);
        var (before, after) = WalkOverLossyLink(dats, link, TimeSpan.FromMinutes(2));
        var crcErrors = after.C2SCrcErrors - before.C2SCrcErrors;
        var duplicates = link.ClientToServer.DuplicateEncryptedDeliveries;
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"ACE counted {crcErrors} bad checksums during the soak; the link made {duplicates} duplicate encrypted deliveries; resend requests: ACE sent {after.S2CRetransmitRequests - before.S2CRetransmitRequests}, received {after.C2SRetransmitRequests - before.C2SRetransmitRequests}"));
        Assert.True(link.ClientToServer.Dropped > 0 && link.ServerToClient.Dropped > 0, "the link dropped nothing: " + link.Describe());
        Assert.True(crcErrors == duplicates, $"ACE counted {crcErrors} bad checksums, the link made {duplicates} duplicate encrypted deliveries ({link.Describe()})");
    }

    /// <summary>
    /// The body of both lossy tests: ACE's counters are server-wide since it booted, so a clean session reads them before and another after; the session in
    /// between walks Holtburg for <paramref name="duration"/> over <paramref name="link"/> (an AutonomousPosition every 200 ms like the virtual soak, walking 20 s and
    /// standing 3 s so MoveToStates change too) and is the only thing that changes them. It checks the session survives the link, that ACE has the player where the
    /// client last put it with the force-position sequence unchanged, and that the character is back at its start.
    /// </summary>
    private (NetData Before, NetData After) WalkOverLossyLink(DatSet dats, LossyLink link, TimeSpan duration)
    {
        var wait = TimeSpan.FromSeconds(45);
        NetData before;
        AcPosition start;
        using (var clean = LiveNetClient.Create(dats, retail: false, LiveNetClient.LoopbackCharacter))
        {
            try
            {
                start = EnterReadyAtStart(clean, output);
                before = AskData<NetData>(clean, new NetRequest(NextId()));
                LogOffAndQuit(clean);
            }
            finally
            {
                LeaveTheWorld(clean);
            }
        }

        output.WriteLine($"before: {before.Describe()}");
        using (var soak = LiveNetClient.Create(dats, retail: false, LiveNetClient.LoopbackCharacter, link))
        {
            var back = false;
            try
            {
                // The link is lossy from the first encrypted send (the data check) on, so the way in can take a few probes.
                Login(soak, waitOutAceLinger: true);
                Enter(soak);
                Unhide(soak);
                var arrival = PlaceAndArrive(soak, new PlacePoiRequest("Holtburg", NextId()), wait);
                var forceSeq = arrival.Where.ForcePositionSeq;
                var positions = soak.Sink.SelfPositions.Count;

                var walker = HoltburgWalker(dats, new Vector2(arrival.Place.X, arrival.Place.Y));
                var driver = new WalkDriver(soak, walker, new PositionCadence(stressIntervalSeconds: 0.2), walkSpan: TimeSpan.FromSeconds(20), standSpan: TimeSpan.FromSeconds(3));
                driver.Run(duration);
                driver.Stop();
                output.WriteLine($"walked {walker.Metres:F0} m: {driver.AutonomousPositions} AutonomousPosition + {driver.MoveToStates} MoveToState; link: {link.Describe()}");

                // Let the last packets through: acks, NAKs and the tail probe deliver what the link lost at the end.
                soak.PumpUntil(() => false, TimeSpan.FromSeconds(10));
                Assert.True(soak.Session.Phase == SessionPhase.InWorld && soak.Session.Failure is null, $"the session is {soak.Session.Phase} ({soak.Session.Failure}). " + soak.Describe());
                var where = AskData<WhereData>(soak, new WhereRequest(NextId()), wait);
                var last = driver.LastSent!.Value;
                output.WriteLine($"where is {Distance(where, last):F2} m from the last position sent; force {where.ForcePositionSeq} (was {forceSeq}); client link: {soak.Session.Link}");
                Assert.True(Distance(where, last) <= Tolerance, $"ACE has the player {Distance(where, last):F2} m from the last position the client sent");
                Assert.Equal(forceSeq, where.ForcePositionSeq);
                Assert.All(soak.Sink.SelfPositions.Skip(positions), p => Assert.Equal(forceSeq, p.ForcePositionSeq));
                // The soak log's CRC and duplicate counts (M2 DoD 12): the client's side of the link, beside the ACE-side counts the two tests print.
                output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"soak counts: client CRC failures {soak.Session.Link.ChecksumFailures}, client duplicate server packets dropped {soak.Session.Link.DuplicatesDropped}, link duplicate encrypted deliveries C2S {link.ClientToServer.DuplicateEncryptedDeliveries} S2C {link.ServerToClient.DuplicateEncryptedDeliveries}"));
                // M4 1.1 s5, report-only: the counts M2 DoD 12 names as one greppable line, read from the net session's own counters (the client's side of the link).
                output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"soak crc_failures={soak.Session.Link.ChecksumFailures} duplicates={soak.Session.Link.DuplicatesDropped} gap_skips={soak.Session.Link.GapSkips} link_duplicate_deliveries_c2s={link.ClientToServer.DuplicateEncryptedDeliveries} link_duplicate_deliveries_s2c={link.ServerToClient.DuplicateEncryptedDeliveries}"));
                Assert.Equal(0, soak.Session.Link.ChecksumFailures);
                Assert.Equal(0, soak.Session.Link.GapSkips);
                Assert.True(link.Socket is { Armed: true }, "the lossy socket was never armed: " + link.Describe());
                Assert.Empty(soak.Sink.Malformed);

                ReturnToStart(soak, start, wait);
                back = true;
                LogOffAndQuit(soak);
            }
            finally
            {
                if (!back)
                {
                    RestoreBestEffort(soak, start, output);
                }

                LeaveTheWorld(soak);
            }
        }

        NetData after;
        using (var clean = LiveNetClient.Create(dats, retail: false, LiveNetClient.LoopbackCharacter))
        {
            try
            {
                EnterTheWorld(clean);
                Unhide(clean);
                Assert.Equal(start.CellId >> 16, (clean.Sink.SelfCreate?.Physics.Position?.CellId ?? 0) >> 16);
                after = AskData<NetData>(clean, new NetRequest(NextId()));
                LogOffAndQuit(clean);
            }
            finally
            {
                LeaveTheWorld(clean);
            }
        }

        output.WriteLine($"after: {after.Describe()}");
        return (before, after);
    }

    private static KinematicWalker HoltburgWalker(DatSet dats, Vector2 start)
    {
        var terrain = dats.GetLandblockTerrain(LandblockCoord.Holtburg) ?? throw new InvalidOperationException("the dats have no terrain for Holtburg's landblock");
        var sampler = new TerrainSampler(terrain, dats.TerrainRegion.LandHeightTable);
        return new KinematicWalker(sampler, LandblockCoord.Holtburg, start, HoltburgLap, MotionInterpreter.WalkAnimSpeed);
    }
}
