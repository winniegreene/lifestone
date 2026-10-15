using Lifestone.Net.Messages;
using Lifestone.Net.Packets;
using Lifestone.Net.Session;
using Lifestone.Net.Tests.FakeAce;
using Lifestone.Net.Tests.Support;

namespace Lifestone.Net.Tests.Session;

/// <summary>
/// The DDD data check (m2-plan §3.3 N5; docs/protocol.md §4.2 rows 7 to 9): F7E5 answered with the host's iterations,
/// the high-res dat only when the server allows it, F7EA never echoed, character select only once both F7EA and F658
/// are in, and a data check that never finishes fails.
/// </summary>
public sealed class DataCheckTests
{
    [Fact]
    public void The_high_res_dat_is_reported_when_the_server_allows_it()
    {
        var script = new FakeAceScript { AllowHighResDat = true, HighResIteration = 497 };
        script.AddAccount(SessionRig.Account, SessionRig.Password);
        using var rig = new SessionRig(script, o => o with { Iterations = new DddIterations(2072, 982, 994, 497) });
        rig.ToCharacterSelect();

        var answer = AceClientReaders.ReadDddInterrogationResponse(Answer(rig));
        Assert.Equal(4, answer.Lists.Count);
        Assert.Contains(answer.Lists, l => l.DatType == DddCodec.HighResDatType && l.Iterations == 497);
    }

    [Fact]
    public void The_high_res_dat_is_left_out_when_the_server_does_not_allow_it()
    {
        using var rig = new SessionRig(configure: o => o with { Iterations = new DddIterations(2072, 982, 994, 497) });
        rig.ToCharacterSelect();

        var answer = AceClientReaders.ReadDddInterrogationResponse(Answer(rig));
        Assert.Equal(3, answer.Lists.Count);
        Assert.DoesNotContain(answer.Lists, l => l.DatType == DddCodec.HighResDatType);
    }

    [Fact]
    public void F7EA_is_never_echoed()
    {
        using var rig = new SessionRig();
        rig.ToCharacterSelect();
        rig.RunFor(TimeSpan.FromSeconds(1));

        Assert.DoesNotContain(MessageOpcodes.DddEnd, rig.Tap.OutboundOpcodes);
        Assert.Equal(new[] { MessageOpcodes.DddInterrogationResponse }, rig.Tap.OutboundOpcodes);
    }

    [Fact]
    public void Character_select_waits_for_the_list_when_F7EA_comes_first()
    {
        // Arrival order, and the packet with F658 and F7E1 lost once: F7EA (answering the F7E6) arrives before the NAKed
        // list does.
        using var rig = new SessionRig(configure: o => o with { Order = InboundOrder.Arrival });
        var dropped = false;
        rig.Harness.Network.Drop = (from, to, datagram) =>
        {
            if (dropped || from.Port < 9000 || from.Port > 9001 || !LoginFailureTests.ServerOpcodes(datagram, from.Port).Contains(MessageOpcodes.CharacterList))
            {
                return false;
            }

            dropped = true;
            return true;
        };

        rig.ToCharacterSelect();

        var complete = Assert.Single(rig.Sink.OfKind(SessionEventKind.DataCheckComplete));
        Assert.Equal(SessionPhase.DataCheck, complete.Phase);
        var order = rig.Sink.Opcodes.ToList();
        Assert.True(order.IndexOf(MessageOpcodes.DddEnd) < order.IndexOf(MessageOpcodes.CharacterList), string.Join(", ", order.Select(o => o.ToString("X4"))));
        Assert.True(rig.Session.Link.NaksSent > 0);
    }

    [Fact]
    public void In_fragment_order_a_late_F7E5_is_still_dispatched_before_the_list()
    {
        // The F7E5 packet is lost once; the list behind it waits for the resend.
        using var rig = new SessionRig();
        var dropped = false;
        rig.Harness.Network.Drop = (from, to, datagram) =>
        {
            if (dropped || from.Port < 9000 || from.Port > 9001 || !LoginFailureTests.ServerOpcodes(datagram, from.Port).Contains(MessageOpcodes.DddInterrogation))
            {
                return false;
            }

            dropped = true;
            return true;
        };

        rig.ToCharacterSelect();

        Assert.True(dropped);
        Assert.Equal(new uint[] { 0, 1, 2, 3 }, rig.Sink.Dispatched.Select(d => d.Sequence));
        Assert.Equal(MessageOpcodes.DddInterrogation, rig.Sink.Dispatched[0].Opcode);
        Assert.Equal(0, rig.Session.Link.GapSkips);
    }

    [Fact]
    public void A_data_check_that_never_completes_fails_after_fifteen_seconds()
    {
        using var rig = new SessionRig();
        rig.Harness.Network.Drop = (from, to, datagram) => LoginFailureTests.ClientOpcode(datagram) == MessageOpcodes.DddInterrogationResponse;
        rig.Login();

        Assert.True(rig.RunUntil(() => rig.Session.Phase == SessionPhase.Failed, TimeSpan.FromSeconds(20)));
        Assert.Equal(SessionFailureKind.DataCheckTimeout, rig.Session.Failure!.Kind);
        Assert.Contains("F7EA missing, F658 received", rig.Session.Failure.Message, StringComparison.Ordinal);
        Assert.InRange(rig.Harness.Time.Elapsed, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15.5));
    }

    private static byte[] Answer(SessionRig rig) =>
        Assert.Single(rig.ServerSession.HandledMessages, m => BitConverter.ToUInt32(m) == MessageOpcodes.DddInterrogationResponse);
}
