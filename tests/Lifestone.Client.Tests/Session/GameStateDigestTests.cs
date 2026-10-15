using Lifestone.Client.Session.Recording;
using Lifestone.Client.Session.State;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The state digest a recording's trailer keeps and a replay must reproduce (step ST13): equal for sessions fed the same
/// messages, blind to times and to float noise below 1e-4, and changed by anything the read model holds.
/// </summary>
public sealed class GameStateDigestTests
{
    [Fact]
    public void Two_sessions_fed_the_same_messages_have_the_same_digest_whatever_the_times()
    {
        var a = World();
        var b = new ReducerHarness();
        b.Time.Advance(TimeSpan.FromSeconds(37));
        Feed(b);
        Assert.Equal(GameStateDigest.Size, GameStateDigest.Compute(a.State).Length);
        Assert.Equal(GameStateDigest.Compute(a.State), GameStateDigest.Compute(b.State));
        Assert.Equal(64, GameStateDigest.ToHex(GameStateDigest.Compute(a.State)).Length);
    }

    [Fact]
    public void Float_noise_below_the_quantum_does_not_change_the_digest_and_a_real_move_does()
    {
        var a = World();
        var b = World();
        var before = GameStateDigest.Compute(a.State);
        a.Reducer.OnUpdatePosition(a.Context, ReducerHarness.Move(0x80000001, 1, 2, 1, 1, x: 11.00001f));
        b.Reducer.OnUpdatePosition(b.Context, ReducerHarness.Move(0x80000001, 1, 2, 1, 1, x: 11f));
        Assert.Equal(GameStateDigest.Compute(a.State), GameStateDigest.Compute(b.State));
        Assert.NotEqual(before, GameStateDigest.Compute(a.State));

        b.Reducer.OnUpdatePosition(b.Context, ReducerHarness.Move(0x80000001, 1, 3, 1, 1, x: 11.01f));
        Assert.NotEqual(GameStateDigest.Compute(a.State), GameStateDigest.Compute(b.State));
    }

    [Fact]
    public void The_list_the_chat_the_phase_and_the_qualities_all_count()
    {
        var baseline = GameStateDigest.Compute(World().State);

        var chat = World();
        chat.Reducer.OnServerMessage(chat.Context, new ServerMessageMsg("a line", 0));
        Assert.NotEqual(baseline, GameStateDigest.Compute(chat.State));

        var phase = World();
        phase.Phase(SessionPhase.InWorld, SessionPhase.LoggingOff);
        Assert.NotEqual(baseline, GameStateDigest.Compute(phase.State));

        var selected = World();
        selected.Reducer.ApplyLocal(new RecordingNotice(SessionNotices.CharacterSelected, ReducerHarness.PlayerGuid, null));
        Assert.NotEqual(baseline, GameStateDigest.Compute(selected.State));

        var qualities = World();
        qualities.Reducer.OnPrivateUpdate(qualities.Context, new PrivateUpdateMsg(PrivateUpdateKind.PropertyInt, 1, 25, 7, default, default, default));
        Assert.NotEqual(baseline, GameStateDigest.Compute(qualities.State));
    }

    [Fact]
    public void A_destination_shorter_than_the_digest_is_refused()
    {
        Assert.Throws<ArgumentException>(() => GameStateDigest.Compute(new ReducerHarness().State, new byte[31]));
    }

    private static ReducerHarness World()
    {
        var h = new ReducerHarness();
        Feed(h);
        return h;
    }

    private static void Feed(ReducerHarness h)
    {
        h.Phase(SessionPhase.Idle, SessionPhase.Connecting);
        h.Reducer.OnCharacterList(h.Context, new CharacterListMsg(0, [new CharacterListEntry(ReducerHarness.PlayerGuid, "Tester", 0)], 0, 11, "account", 0, 1));
        h.Phase(SessionPhase.Connecting, SessionPhase.CharacterSelect);
        h.Phase(SessionPhase.CharacterSelect, SessionPhase.InWorld);
        h.EnterWorld();
        h.Reducer.OnCreateObject(h.Context, ReducerHarness.Create(0x80000001, ReducerHarness.Stamps(), ReducerHarness.Position(0x860201AD, 11)), isUpdate: false);
    }
}
