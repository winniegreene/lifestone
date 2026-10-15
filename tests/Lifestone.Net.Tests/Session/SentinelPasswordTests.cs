using System.Buffers.Binary;
using System.Text;
using Lifestone.Net.Messages;
using Lifestone.Net.Packets;
using Lifestone.Net.Session;
using Lifestone.Net.Tests.FakeAce;
using Lifestone.Net.Types;

namespace Lifestone.Net.Tests.Session;

/// <summary>
/// Password hygiene (m2-plan §3.3 N5, DoD 2): a sentinel password goes through login (with a retry), character
/// creation, entering the world, LoginComplete, a walk through every world send (N7), logoff and quit, and must never
/// appear in a tap body, a log line, a session event, a failure, an exception message or the link stats, in ASCII,
/// UTF-16LE or String32L framing. On the wire it is in the LoginRequests only, and the session's own copy is gone once
/// character select is reached.
/// </summary>
public sealed class SentinelPasswordTests
{
    private const string Sentinel = "Qz7-SENTINEL-p4ss";

    [Fact]
    public void The_sentinel_never_leaves_the_login_request()
    {
        var script = new FakeAceScript();
        script.AddAccount(SessionRig.Account, Sentinel);
        using var rig = new SessionRig(script);
        var droppedFirst = false;
        rig.Harness.Network.Drop = (from, to, datagram) =>
        {
            // Lose the first LoginRequest, so the password is sent again from the session's copy.
            if (!droppedFirst && (BinaryPrimitives.ReadUInt32LittleEndian(datagram.AsSpan(4)) & (uint)PacketHeaderFlags.LoginRequest) != 0)
            {
                droppedFirst = true;
                return true;
            }

            return false;
        };

        rig.Login(password: Sentinel);
        Assert.True(rig.Machine.HoldsPassword);
        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.CharacterSelect, TimeSpan.FromSeconds(15)));
        Assert.False(rig.Machine.HoldsPassword);

        rig.Session.CreateCharacter(CharacterOpsTests.Request("Sentinel Test"));
        Assert.True(rig.RunUntil(() => rig.Sink.CharGenResponses.Count == 1, TimeSpan.FromSeconds(2)));
        var guid = rig.Sink.CharGenResponses[0].Guid;
        rig.Session.EnterWorld(guid);
        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.InWorld, TimeSpan.FromSeconds(2)));
        rig.Session.SendLoginComplete();
        rig.RunFor(TimeSpan.FromSeconds(1));

        // A walk through every world send (N7): none of them may carry the password either.
        var here = new AcPosition(0xA9B40019, new System.Numerics.Vector3(84f, 7.1f, 94f), System.Numerics.Quaternion.Identity);
        var running = RawMotionStateWire.Default with { CurrentHoldKey = 2, ForwardCommand = 0x45000005, Actions = [new RawMotionAction(0x13000087, 1, true, 1f)] };
        rig.Session.SendMoveToState(running, here, default, contact: true, standingLongJump: false);
        rig.Session.SendAutonomousPosition(here with { Origin = new System.Numerics.Vector3(86f, 7.1f, 94f) }, default, contact: true);
        rig.Session.SendJump(0.5f, new System.Numerics.Vector3(0f, 0f, 4f), here, default);
        rig.Session.SendUse(0x7A9B4002);
        rig.Session.SendQueryHealth(0x7A9B4002);
        rig.Session.SendTalk("hello from the walk");
        rig.Session.SendTell("Sentinel Test", "psst");
        Assert.True(rig.RunUntil(() => rig.Sink.Speech.Count == 1, TimeSpan.FromSeconds(2)));
        Assert.Equal(8, rig.Tap.Outbound.Count(b => BitConverter.ToUInt32(b) == MessageOpcodes.GameAction));  // LoginComplete and the seven
        rig.Session.LogOff();
        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.CharacterSelect, TimeSpan.FromSeconds(10)));
        rig.Session.Quit();
        rig.Harness.Step();

        var exceptions = new List<string>();
        using (var tooLong = new AcCredentials(SessionRig.Account, Sentinel + new string('x', 300)))
        using (var nonAscii = new AcCredentials(SessionRig.Account, Sentinel + "é"))
        using (var fresh = AcSession.Create(rig.Options, new RecordingSink()))
        {
            exceptions.Add(Assert.Throws<ArgumentException>(() => fresh.Login(tooLong)).ToString());
            exceptions.Add(Assert.Throws<ArgumentException>(() => fresh.Login(nonAscii)).ToString());
            exceptions.Add(tooLong.ToString());
        }

        AssertClean("tap bodies", rig.Tap.Messages.Select(m => m.Body));
        AssertClean("log lines", rig.Log.Lines.Select(l => Encoding.UTF8.GetBytes(l.Message)));
        AssertClean("session events", rig.Sink.Events.Select(e => Encoding.UTF8.GetBytes(e.ToString())));
        AssertClean("exception messages", exceptions.Select(Encoding.UTF8.GetBytes));
        AssertClean("link stats", [Encoding.UTF8.GetBytes(rig.Session.Link.ToString())]);
        AssertClean("the failure", [Encoding.UTF8.GetBytes(rig.Session.Failure?.ToString() ?? string.Empty)]);

        // On the wire, only the two LoginRequests (the lost one and its retry) carry it.
        var carriers = rig.Harness.Network.Sent.Where(s => SecretScan.Contains(s.Datagram, Sentinel)).ToList();
        Assert.Equal(2, carriers.Count);
        Assert.All(carriers, c => Assert.NotEqual(0u, BinaryPrimitives.ReadUInt32LittleEndian(c.Datagram.AsSpan(4)) & (uint)PacketHeaderFlags.LoginRequest));
        Assert.Equal(SessionPhase.Closed, rig.Session.Phase);
    }

    [Fact]
    public void A_failed_login_drops_the_password_copy()
    {
        using var rig = new SessionRig();
        rig.Login(password: Sentinel);
        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.Failed, TimeSpan.FromSeconds(2)));

        Assert.False(rig.Machine.HoldsPassword);
        Assert.False(SecretScan.Contains(Encoding.UTF8.GetBytes(rig.Session.Failure!.ToString()), Sentinel));
        Assert.All(rig.Log.Lines, l => Assert.DoesNotContain(Sentinel, l.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void Disposing_the_session_drops_the_password_copy()
    {
        using var rig = new SessionRig();
        rig.Harness.ServerAlive = false;
        rig.Login(password: Sentinel);
        Assert.True(rig.Machine.HoldsPassword);
        rig.Session.Dispose();
        Assert.False(rig.Machine.HoldsPassword);
    }

    private static void AssertClean(string where, IEnumerable<byte[]> outputs)
    {
        var count = outputs.Count(o => SecretScan.Contains(o, Sentinel));
        Assert.True(count == 0, $"the sentinel password appears in {count} of the {where}");
    }
}
