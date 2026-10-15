using System.Diagnostics;
using System.Numerics;
using Lifestone.Data.Dat;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;
using static Lifestone.Loopback.Tests.Net.LiveSteps;

namespace Lifestone.Loopback.Tests.Net;

/// <summary>What a teleport asked of the mod brought: the mod's answer, the player's F748 that followed the F751, and the server's own view once it left Teleporting.</summary>
/// <param name="Place">The mod's <c>place</c> reply (where the teleport goes).</param>
/// <param name="Position">The last F748 UpdatePosition for the player after the request.</param>
/// <param name="Where">The mod's <c>where</c> reply after LoginComplete, with the server no longer teleporting.</param>
/// <param name="Teleports">How many F751 PlayerTeleport messages the request brought.</param>
internal sealed record Arrival(PlaceData Place, UpdatePositionMsg Position, WhereData Where, int Teleports);

/// <summary>
/// The teleport steps of the live phase-B tests (m2-plan N12): <c>@lifestone place poi|loc</c>, the client's part of the arrival (the F751 and F748, then
/// LoginComplete, which is what takes the server out of Teleporting, docs/protocol.md section 4), the server's <c>where</c>, and putting the loopback
/// character back where the other tests expect it. The character is <see cref="LiveNetClient.LoopbackCharacter"/>; whatever moves it must end with
/// <see cref="EnsureAtStart"/>'s start frame restored (the Training Academy's first starting location, which a new character is created at).
/// </summary>
internal static class LivePlacement
{
    /// <summary>The Training Academy's landblock, where a new character starts (cell 0x860201AD).</summary>
    public const uint AcademyLandblock = 0x8602;

    /// <summary>How long a teleport may take from the mod's answer to the F751 and F748 (ACE may hold it for a second to clear fog; a lossy link adds retries).</summary>
    public static readonly TimeSpan TeleportWait = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The first Academy starting location of the Aluvian start area (CharGen <c>StarterAreas[0].Locations[0]</c>, docs/interiors.md section 6). Used only when a
    /// character is found elsewhere at the start of a test (a failed run left it away), as the place to send it back to.
    /// TODO(loopback-start-frame): the rotation is the identity here because the table has none; a character created fresh arrives with the real one.
    /// </summary>
    public static AcPosition AcademyStart { get; } = new(0x860201AD, new Vector3(12.3199f, -28.482f, 0.005f), Quaternion.Identity);

    /// <summary>
    /// The character-keeper: logs <c>Lifestone Loopback</c> in, sends it to the Academy start when it is anywhere else (a run that failed in the dungeon or in
    /// Holtburg leaves it there), and logs it out. A scenario's home is where its login found the character, so a test that moves it calls this before the run
    /// and in a <c>finally</c>. Idempotent.
    /// </summary>
    public static void PutTheCharacterAtTheAcademyStart(DatSet dats, ITestOutputHelper output, Action<LiveNetClient>? beforeLeaving = null)
    {
        using var client = LiveNetClient.Create(dats, retail: false, LiveNetClient.LoopbackCharacter);
        try
        {
            EnterReadyAtStart(client, output);
            var arrival = ReturnToStart(client, AcademyStart);
            output.WriteLine($"the character is at {arrival.Where.Cell} ({arrival.Where.X:F2}, {arrival.Where.Y:F2}) after {arrival.Teleports} teleport(s)");
            beforeLeaving?.Invoke(client);
        }
        finally
        {
            LeaveTheWorld(client);
        }
    }

    /// <summary>A position as the mod's <c>place loc</c> takes it.</summary>
    public static ToolLocation ToLocation(AcPosition p) =>
        new(p.CellId, p.Origin.X, p.Origin.Y, p.Origin.Z, p.Rotation.W, p.Rotation.X, p.Rotation.Y, p.Rotation.Z);

    /// <summary>
    /// Logs in, creates the character when missing, enters the world and unhides; checks the account is a plain Player with the first-account promotion off (the run
    /// aborts otherwise, as phase A's first test does); and returns the character's start frame: where it arrived when that is the Academy, else the Academy
    /// start it has just been sent back to.
    /// </summary>
    public static AcPosition EnterReadyAtStart(LiveNetClient client, ITestOutputHelper output)
    {
        EnterTheWorld(client);
        Unhide(client);
        var status = AskStatus(client);
        if (!string.Equals(status.Access, "Player", StringComparison.Ordinal) || status.AutoPromotion)
        {
            LoopbackRun.Abort($"the test account's access is {status.Access} and ACE's first-account auto-promotion is {(status.AutoPromotion ? "ON" : "off")} (owner decision D11)");
        }

        return EnsureAtStart(client, output);
    }

    /// <summary>The arrival frame when the character is in the Academy's landblock; otherwise sends it to <see cref="AcademyStart"/> and returns that.</summary>
    public static AcPosition EnsureAtStart(LiveNetClient client, ITestOutputHelper output)
    {
        var arrival = client.Sink.SelfCreate?.Physics.Position ?? throw new InvalidOperationException("the player's own object has no position");
        if ((arrival.CellId >> 16) == AcademyLandblock)
        {
            output.WriteLine($"{client.CharacterName} starts at cell 0x{arrival.CellId:X8}");
            return arrival;
        }

        output.WriteLine($"{client.CharacterName} was left at cell 0x{arrival.CellId:X8}; sending it back to the Academy start");
        PlaceAndArrive(client, new PlaceLocRequest(ToLocation(AcademyStart), NextId()));
        return AcademyStart;
    }

    /// <summary>
    /// Asks the mod to teleport the player (<paramref name="request"/>: a <see cref="PlacePoiRequest"/> or <see cref="PlaceLocRequest"/>), waits for the F751
    /// and an F748 after it, sends LoginComplete as the client does on leaving portal space, and polls <c>where</c> until the server has left Teleporting.
    /// </summary>
    public static Arrival PlaceAndArrive(LiveNetClient client, ToolRequest request, TimeSpan? askWait = null)
    {
        var started = StartTeleport(client, request, askWait);
        return FinishArrival(client, started, askWait);
    }

    /// <summary>A teleport the mod started and the client has seen (the F751 and an F748), before LoginComplete: the server still has the player Teleporting.</summary>
    /// <param name="Place">The mod's <c>place</c> reply.</param>
    /// <param name="Position">The last F748 for the player after the request.</param>
    /// <param name="Teleports">How many F751 PlayerTeleport messages the request brought.</param>
    internal sealed record StartedTeleport(PlaceData Place, UpdatePositionMsg Position, int Teleports);

    /// <summary>
    /// The first half of <see cref="PlaceAndArrive"/>: asks the mod to teleport the player and waits for the F751 and an F748 after it. The player stays
    /// Teleporting on the server (so a second <c>place</c> is answered <c>busy</c>) until <see cref="FinishArrival"/> sends LoginComplete.
    /// </summary>
    public static StartedTeleport StartTeleport(LiveNetClient client, ToolRequest request, TimeSpan? askWait = null)
    {
        var sink = client.Sink;
        var teleports = sink.Teleports;
        var positions = sink.SelfPositions.Count;
        var place = AskData<PlaceData>(client, request, askWait);
        Assert.True(
            client.PumpUntil(() => sink.Teleports > teleports && sink.SelfPositions.Count > positions, TeleportWait),
            $"the teleport to {place.Cell} brought {sink.Teleports - teleports} PlayerTeleport and {sink.SelfPositions.Count - positions} UpdatePosition messages. " + client.Describe());
        return new StartedTeleport(place, sink.SelfPositions[^1], sink.Teleports - teleports);
    }

    /// <summary>The second half of <see cref="PlaceAndArrive"/>: sends LoginComplete as the client does on leaving portal space and polls <c>where</c> until the server has left Teleporting.</summary>
    public static Arrival FinishArrival(LiveNetClient client, StartedTeleport started, TimeSpan? askWait = null)
    {
        client.Session.SendLoginComplete();
        var watch = Stopwatch.StartNew();
        while (true)
        {
            var where = AskData<WhereData>(client, new WhereRequest(NextId()), askWait);
            if (!where.Teleporting)
            {
                return new Arrival(started.Place, started.Position, where, started.Teleports);
            }

            Assert.True(watch.Elapsed < TeleportWait, "the server is still teleporting after LoginComplete. " + client.Describe());
            client.PumpUntil(() => false, TimeSpan.FromMilliseconds(300));
        }
    }

    /// <summary>Polls <c>where</c> until the server has left Teleporting (after LoginComplete); throws when it has not within <see cref="TeleportWait"/>.</summary>
    private static WhereData WaitUntilArrived(LiveNetClient client, TimeSpan? askWait = null)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            var where = AskData<WhereData>(client, new WhereRequest(NextId()), askWait);
            if (!where.Teleporting)
            {
                return where;
            }

            Assert.True(watch.Elapsed < TeleportWait, "the server is still teleporting after LoginComplete. " + client.Describe());
            client.PumpUntil(() => false, TimeSpan.FromMilliseconds(300));
        }
    }

    /// <summary>Sends the character back to <paramref name="start"/> and checks it got there: an F751 and F748 in the start's landblock, and the server's <c>where</c> within 0.5 m.</summary>
    public static Arrival ReturnToStart(LiveNetClient client, AcPosition start, TimeSpan? askWait = null)
    {
        var arrival = PlaceAndArrive(client, new PlaceLocRequest(ToLocation(start), NextId()), askWait);
        Assert.Equal(start.CellId >> 16, arrival.Position.Position.CellId >> 16);
        Assert.Equal(start.CellId >> 16, LandblockOf(arrival.Where.Cell));
        Assert.True(Distance(arrival.Where, start) <= 0.5f, $"after the return, the server has the character {Distance(arrival.Where, start):F2} m from its start");
        return arrival;
    }

    /// <summary>
    /// For a test's <c>finally</c>: when the session is still in the world and the server has the character outside <paramref name="start"/>'s landblock, sends it back.
    /// Never throws (the test's own failure is the one to report); says what happened in <paramref name="output"/>.
    /// </summary>
    public static void RestoreBestEffort(LiveNetClient client, AcPosition start, ITestOutputHelper output)
    {
        try
        {
            if (start.CellId == 0)
            {
                output.WriteLine("restore: the test failed before it knew the character's start; the next test's EnsureAtStart sends it back if it is away");
                return;
            }

            if (client.Session.Phase != SessionPhase.InWorld)
            {
                output.WriteLine($"restore: the session is {client.Session.Phase}; the next test's EnsureAtStart sends the character back if it is away");
                return;
            }

            var where = AskData<WhereData>(client, new WhereRequest(NextId()));
            if (where.Teleporting)
            {
                // A request the test expected to be refused was carried out (or a teleport was left half-done): the server refuses a new place as busy
                // until the arrival ends, so finish the arrival (LoginComplete, as the client sends on leaving portal space) before sending the character back.
                output.WriteLine($"restore: the server still has the character teleporting (at {where.Cell}); finishing the arrival first");
                client.Session.SendLoginComplete();
                where = WaitUntilArrived(client);
            }

            if (LandblockOf(where.Cell) != start.CellId >> 16)
            {
                output.WriteLine($"restore: the character is at {where.Cell}; sending it back to 0x{start.CellId:X8}");
                ReturnToStart(client, start);
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            output.WriteLine($"restore failed ({e.GetType().Name}: {e.Message}); the next test's EnsureAtStart sends the character back if it is away");
        }
    }
}
