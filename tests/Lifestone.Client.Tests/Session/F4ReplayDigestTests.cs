using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Recording;
using Lifestone.Data.Magic;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 P5b F4 (TODO(m4-replay-vitae-digest)): the mage journey of 2026-10-24 replayed off its digest because the live client composed "Vitae penalty has
/// expired." from the SpellTable, which the recording does not hold, and the replay had none. A live session over the scripted server now records the line
/// (SessionNotices.ExpiryLine) and the final digest's parts (SessionNotices.DigestParts), so a replay with no table matches, and a replay that does not names
/// the part that differs.
/// </summary>
public sealed class F4ReplayDigestTests
{
    private const uint VitaeType = 0x800000 | 0x4000 | 0x2000 | 0x2;

    private static readonly SpellInfo Vitae = WorldActionRig.Spell(666, DatReaderWriter.Enums.SpellType.Enchantment) with { Name = "Vitae" };

    [Fact]
    public void A_live_vitae_expiry_is_recorded_and_replays_to_the_digest_without_the_spell_table()
    {
        var recording = VitaeExpiryRecording(out var liveDigest);

        var (replay, chat) = Replay(recording);

        Assert.True(replay.DigestMatches, replay.DigestDiagnosis());
        Assert.Equal(liveDigest, replay.StateDigest);
        Assert.Contains((ChatKind.Notice, 7u, "Vitae penalty has expired."), chat);
    }

    [Fact]
    public void A_replay_with_the_spell_table_takes_the_recorded_expiry_line_once()
    {
        var recording = VitaeExpiryRecording(out _);

        var (replay, chat) = Replay(recording, id => id == 666 ? Vitae : null);

        Assert.True(replay.DigestMatches, replay.DigestDiagnosis());
        Assert.Single(chat, l => l.Text == "Vitae penalty has expired.");
    }

    [Fact]
    public void A_live_recording_carries_its_digest_parts_and_a_replay_compares_them()
    {
        var recording = VitaeExpiryRecording(out _);

        var replay = new SessionReplayer().Replay(new MemoryStream(recording));

        Assert.NotNull(replay.RecordedParts);
        Assert.Equal(replay.ReplayedParts, replay.RecordedParts);
        Assert.Empty(replay.PartDifferences!);
        Assert.Contains(replay.RecordedParts!, p => p.Name == "chat");
        Assert.Contains(replay.RecordedParts!, p => p.Name == "qualities.vitals");
    }

    [Fact]
    public void The_digest_parts_round_trip_and_name_the_part_that_differs()
    {
        var a = new GameState();
        var b = new GameState();
        b.Chat.Add(new ChatLine(1, ChatKind.Notice, 7, null, 0, "Vitae penalty has expired."));

        var recorded = GameStateDigest.ParseParts(GameStateDigest.FormatParts(GameStateDigest.Parts(b)));
        Assert.Equal(GameStateDigest.Parts(b), recorded);

        var differ = Assert.Single(GameStateDigest.CompareParts(recorded, GameStateDigest.Parts(a)));
        Assert.StartsWith("chat: recorded ", differ, StringComparison.Ordinal);
        Assert.Contains("(total=1,kept=1)", differ, StringComparison.Ordinal);
        Assert.Contains("(total=0,kept=0)", differ, StringComparison.Ordinal);
        Assert.Empty(GameStateDigest.CompareParts(recorded, GameStateDigest.Parts(b)));
        Assert.Contains(GameStateDigest.CompareParts([new DigestPart("enchantments", "00", "n=1")], GameStateDigest.Parts(a)), d => d.StartsWith("enchantments: recorded 00(n=1), not a part", StringComparison.Ordinal));
    }

    [Fact]
    public void A_replay_that_misses_the_digest_names_the_differing_part_in_its_diagnosis()
    {
        var recording = VitaeExpiryRecording(out _);
        var replay = new SessionReplayer().Replay(new MemoryStream(recording));
        var tampered = replay with { };
        var chat = replay.ReplayedParts.Single(p => p.Name == "chat");
        tampered = tampered with
        {
            PartDifferences = GameStateDigest.CompareParts([.. replay.ReplayedParts.Select(p => p.Name == "chat" ? p with { Hash = "0000000000000000" } : p)], replay.ReplayedParts),
        };

        var diagnosis = tampered.DigestDiagnosis();

        Assert.StartsWith("differing parts: chat: recorded 0000000000000000(", diagnosis, StringComparison.Ordinal);
        Assert.Contains("replayed " + chat.Hash, diagnosis, StringComparison.Ordinal);
        Assert.Contains("recorded_digest=", diagnosis, StringComparison.Ordinal);
        Assert.StartsWith("the recording holds no digest parts", (replay with { PartDifferences = null }).DigestDiagnosis(), StringComparison.Ordinal);
    }

    /// <summary>A live session that enters the world, takes the vitae, has it removed with the expiry notice (02C3), logs off and quits.</summary>
    private static byte[] VitaeExpiryRecording(out byte[] digest)
    {
        var h = new LiveHarness();
        h.Session.ActionApi.SpellLookup = id => id == 666 ? Vitae : null;
        h.Present();
        h.Server.Inject(AceM3Writers.MagicUpdateEnchantment(ScriptedAceSession.Amy, 900, new AceEnchantment(666, 1, 0, 0, 1, 0, 1800, ScriptedAceSession.Amy, 0f, 0f, 0.0, VitaeType, 0, 0.95f, 0)));
        h.Run(0.2);
        h.Server.Inject(AceM3Writers.MagicRemoveEnchantment(ScriptedAceSession.Amy, 901, 666, 1));
        h.Run(0.2);
        Assert.Contains((ChatKind.Notice, 7u, "Vitae penalty has expired."), Lines(h.State.Chat));
        var logOff = h.Actions.LogOff(ActionSource.Ui);
        Assert.True(h.RunUntil(() => logOff.IsCompleted));
        h.Actions.Quit(ActionSource.Ui);
        h.Run(0.5);
        h.Dispose();
        digest = GameStateDigest.Compute(h.State);
        return h.RecordingBytes();
    }

    private static (ReplayResult Result, List<(ChatKind Kind, uint ChatType, string Text)> Chat) Replay(byte[] recording, Func<uint, SpellInfo?>? spells = null)
    {
        var replayer = new SessionReplayer(spells: spells);
        var result = replayer.Replay(new MemoryStream(recording));
        return (result, Lines(replayer.State.Chat));
    }

    private static List<(ChatKind Kind, uint ChatType, string Text)> Lines(ChatLog chat)
    {
        var lines = new List<(ChatKind, uint, string)>(chat.Count);
        for (var i = 0; i < chat.Count; i++)
        {
            lines.Add((chat[i].Kind, chat[i].ChatType, chat[i].Text));
        }

        return lines;
    }
}
