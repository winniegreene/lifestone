using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Dat;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M5 6 k2 (D33 and DoD 11), the two live sentences that need a server: "a chest unlocked by a key reads unlocked" and the Yes/No dialog's request and answer. WRITTEN AND COMPILED BY THE SLICE, RUN BY THE LEAD
/// (they need the backend with the Lifestone server mod ready, and item 6's wire, reducer and actions merged). Lifestone Throwaway on the tools account, every step through <see cref="IActionApi"/> and the mod's
/// EXISTING verbs (<c>place loc</c> and <c>item</c>); no new verb and no mod change. Each test prints one verdict line and the bytes of the messages it was written to capture, then asserts.
/// <list type="bullet">
/// <item><b>A_key_unlocks_the_locked_Aluvian_Pathwarden_Chest_and_the_server_says_so</b>. The chest is the Aluvian Pathwarden Chest, weenie 33609, a static instance of the pinned world database in
/// Holtburg's landblock: cell 0xA9B40022 at (99.0, 29.5, 94.0), 27 m from the arrival of <c>place poi "Holtburg"</c> (0xA9B40019 at 84.0, 7.1) and exactly where M4's chests station already stands to be refused. Its weenie
/// has <c>PropertyBool.Locked</c> true and LockCode <c>pathwardenchestkey</c>; the key is the Pathwarden Supply Key, weenie 33608 (KeyCode <c>pathwardenchestkey</c>, Structure 1: ACE consumes it on the first
/// unlock, <c>Lock.cs</c> <c>UnlockerHelper.ConsumeUnlocker</c>, @95ef426d). The Throwaway is given the key with the mod's <c>item</c> verb (<c>SpawnItem(33608)</c>), the way every loopback test grants an item, and
/// is put at (99, 31, 94) of that cell with <c>place loc</c>, 1.5 m from the chest (M4 <c>ChestsRun.LockedRange</c>; a longer walk timed ACE's use out at 15 s). The test reads the chest (its CreateObject does not
/// carry the lock, so <see cref="WorldObject.Locked"/> is <b>null</b> on the first read; ACE sends GameMessage 0x02D2 only on a change), probes it with a plain use (a locked chest answers a "locked" refusal; an
/// open answer means an earlier run left it unlocked, and the test closes it and waits for ACE's reset), uses the key on it (<see cref="IActionApi.UseWithTarget"/>), and asserts that a 0x02D2 for the chest, key 3
/// (<c>PropertyBool.Locked</c>), value 0 arrives and the object table reads <c>Locked == false</c>. Every 0x02D2 seen is printed as hex. Clean-up: the key is consumed by ACE (asserted); the chest is opened and
/// closed once, which makes ACE reset it (<c>Chest.cs</c> <c>Open</c> schedules <c>Reset</c> after <c>ChestResetInterval</c>, 120 s, which closes it and sets <c>Locked</c> true again and, unless the
/// <c>fix_chest_missing_inventory_window</c> property is set, broadcasts a second 0x02D2), and the test waits for it (reported, not asserted, since a server property can silence the broadcast).</item>
/// <item><b>A_server_question_arrives_and_No_closes_it</b>. The cheapest path to a GameEvent 0x0274 that a solo level-1 character reaches with the existing grants: the Coordination To Endurance Gem, weenie 23045
/// (an <c>AttributeTransferDevice</c>; <c>item 23045</c>), used from the pack. ACE's <c>AttributeTransferDevice.ActOnUse</c> (@95ef426d) checks the requirements (Coordination innate above 10, Endurance below 100,
/// nothing wielded with an attribute requirement) and sends <c>Confirmation_AlterAttribute</c>: GameEvent 0x0274 type 3 (AlterAttribute) with the text "This action will transfer 10 points from your Coordination to your
/// Endurance." The test prints the request's bytes, answers No through <see cref="IActionApi.RespondToConfirmation"/>, and asserts that the question closed (<c>ConfirmationClosed</c> by the answer, no pending question)
/// and that the gem is still in the pack (a No consumes nothing, <c>Confirmation_AlterAttribute.ProcessConfirmation</c>). ACE answers a client's response with nothing (the 0x0276 is only its 30 s timeout), so the
/// close asserted is the client's own; no 0x0276 is expected or required. The gem stays in the pack for the next run, which reuses it.</item>
/// </list>
/// </summary>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test-tools")]
[LoopbackCharacter("Lifestone Throwaway")]
[LiveSharedSession]
public sealed class KeyChestLiveTests(ITestOutputHelper output)
{
    private const uint KeyWcid = 33608; // Pathwarden Supply Key
    private const uint ChestWcid = 33609; // Aluvian Pathwarden Chest
    private const string ChestName = "Aluvian Pathwarden Chest";
    private const uint ChestCell = 0xA9B40022;
    private const uint GemWcid = 23045; // Coordination To Endurance Gem
    private const uint LockedKey = 3; // ACE PropertyBool.Locked

    [LoopbackFact]
    public void A_key_unlocks_the_locked_Aluvian_Pathwarden_Chest_and_the_server_says_so()
    {
        using var dats = DatSet.Open();
        var leg = new KeyChestLeg(HeadlessCharacter.RetailSkillClasses(dats), output.WriteLine, dats);
        LiveRun.Run(output, leg, "keychest", 420);
        var line = leg.Verdict();
        output.WriteLine(line);
        foreach (var hex in leg.Bytes)
        {
            output.WriteLine("keychest 02D2 " + hex);
        }

        Assert.True(leg.Problems.Count == 0, "the checks that failed: " + string.Join("; ", leg.Problems) + " | " + line);
        Assert.True(leg.Unlocked, line);
        Assert.True(leg.MessagesForChest >= 1, "no GameMessage 0x02D2 for the chest arrived | " + line);
    }

    [LoopbackFact]
    public void A_server_question_arrives_and_No_closes_it()
    {
        using var dats = DatSet.Open();
        var leg = new QuestionLeg(HeadlessCharacter.RetailSkillClasses(dats), output.WriteLine, dats);
        LiveRun.Run(output, leg, "keychest-question", 120);
        var line = leg.Verdict();
        output.WriteLine(line);
        foreach (var hex in leg.Bytes)
        {
            output.WriteLine("question " + hex);
        }

        Assert.True(leg.Problems.Count == 0, "the checks that failed: " + string.Join("; ", leg.Problems) + " | " + line);
        Assert.True(leg.Requested && leg.Closed, line);
    }

    // ------------------------------------------------------------------------------------------------ the legs

    private static string Hex(ReadOnlySpan<byte> body) => Convert.ToHexString(body);

    private sealed class KeyChestLeg(IReadOnlyList<uint> classes, Action<string> log, DatSet dats) : LiveLeg("keychest", classes, log, dats)
    {
        private readonly List<string> _bytes = [];
        private string _firstRead = "unseen";
        private string _probe = "none";
        private string _use = "none";
        private string _relock = "not_run";
        private int _keysGranted;
        private int _keysLeft = -1;

        public IReadOnlyList<string> Bytes => _bytes;

        public bool Unlocked { get; private set; }

        public int MessagesForChest { get; private set; }

        public string Verdict() =>
            string.Create(CultureInfo.InvariantCulture, $"keychest first_read_locked={_firstRead} probe={_probe} keys_granted={_keysGranted} use={_use} m02d2_for_chest={MessagesForChest} m02d2_seen={_bytes.Count} unlocked={(Unlocked ? "yes" : "no")} key_left={_keysLeft} relock={_relock} problems={Problems.Count}");

        protected override IEnumerable<ActionHandle?> Script()
        {
            // Every GameMessage 0x02D2 in hex (opcode, a sequence byte, the object, the property, the value): the lead's capture of the real message.
            uint chest = 0;
            var relockSeen = false;
            using var tap = S.Events.SubscribeRaw((direction, time, fragment, queue, body) =>
            {
                if (direction != MessageDirection.Inbound || body.Length < 17 || BinaryPrimitives.ReadUInt32LittleEndian(body) != 0x02D2)
                {
                    return;
                }

                var guid = BinaryPrimitives.ReadUInt32LittleEndian(body[5..]);
                var key = BinaryPrimitives.ReadUInt32LittleEndian(body[9..]);
                var value = BinaryPrimitives.ReadUInt32LittleEndian(body[13..]);
                _bytes.Add(string.Create(CultureInfo.InvariantCulture, $"t={time:0.0} guid={guid:X8} key={key} value={value} bytes={Hex(body)}"));
                if (chest != 0 && guid == chest && key == LockedKey)
                {
                    MessagesForChest++;
                    relockSeen |= value != 0;
                }
            });

            foreach (var h in EnterThrowaway())
            {
                yield return h;
            }

            foreach (var h in Pause(3))
            {
                yield return h;
            }

            // The hop: the mod's existing `place loc`, to 1.5 m from the chest (it is 27 m from the Holtburg POI's arrival; no walking).
            foreach (var h in PlaceAndSettle(Destination("pathwarden chest", ChestCell, new Vector3(99, 31, 94)), "the Aluvian Pathwarden Chest"))
            {
                yield return h;
            }

            foreach (var h in Until(() => Nearest(ChestName, 10) != 0, 20))
            {
                yield return h;
            }

            chest = Nearest(ChestName, 10);
            Require(chest != 0, $"no '{ChestName}' within 10 m of the arrival at cell 0x{ChestCell:X8} (the world database's instance 33609)");
            _firstRead = S.State.Objects.TryGet(chest, out var seen) ? (seen.Locked is { } l ? l.ToString().ToLowerInvariant() : "null") : "missing";
            Log($"the first read of the chest {chest:X8}: Locked is {_firstRead} (its CreateObject does not carry the lock; ACE sends 0x02D2 only on a change)");

            // The probe: a plain use. A locked chest refuses ("locked"); a chest an earlier run left unlocked opens.
            var probe = S.Actions.OpenContainer(chest, ActionSource.Host);
            yield return probe;
            Log("probe OpenContainer: " + Describe(probe));
            if (probe.Outcome is { Kind: ActionOutcomeKind.ServerError, Detail: { } d } && d.Contains("locked", StringComparison.OrdinalIgnoreCase))
            {
                _probe = "refused";
            }
            else if (probe.Outcome is { Kind: ActionOutcomeKind.Confirmed })
            {
                _probe = "opened";
                Log("the chest opened: an earlier run left it unlocked. Closing it and waiting for ACE's reset (up to 150 s)");
                var close = S.Actions.CloseContainer(chest, ActionSource.Host);
                yield return close;
                foreach (var h in Until(() => S.State.Objects.TryGet(chest, out var o) && o.Locked == true, 150))
                {
                    yield return h;
                }
            }
            else
            {
                _probe = "other";
            }

            Check(_probe is "refused" or "opened", "the probe use of the chest: wanted a 'locked' refusal (or an open from an earlier run), got " + Describe(probe));
            var before = MessagesForChest;

            // The key: the mod's `item` verb grants one (or one a failed run left is reused).
            uint key = HeldOf(KeyWcid).FirstOrDefault();
            if (key == 0)
            {
                var spawn = S.Tools.SpawnItem(KeyWcid, null, ActionSource.Host);
                yield return spawn;
                Require(spawn.Result is not null, "the Pathwarden Supply Key grant (wcid 33608) failed: " + Describe(spawn));
                key = spawn.Result!.Guid;
                _keysGranted++;
                foreach (var h in WaitHeld(key, "the key"))
                {
                    yield return h;
                }

                foreach (var h in Pause(1))
                {
                    yield return h;
                }
            }

            var use = S.Actions.UseWithTarget(key, chest, ActionSource.Host);
            yield return use;
            _use = use.Outcome is { } o2 ? o2.Kind.ToString() : "not_settled";
            Log("UseWithTarget (the key on the chest): " + Describe(use));
            foreach (var h in Until(() => S.State.Objects.TryGet(chest, out var o) && o.Locked == false, 10))
            {
                yield return h;
            }

            Unlocked = S.State.Objects.TryGet(chest, out var after) && after.Locked == false;
            Check(Unlocked, "the object table does not read the chest unlocked after the key (WorldObject.Locked is " + (S.State.Objects.TryGet(chest, out var now) && now.Locked is { } nl ? nl.ToString() : "null") + ")");
            Check(MessagesForChest > before, "no GameMessage 0x02D2 (key 3) for the chest arrived after the key was used");
            _keysLeft = HeldOf(KeyWcid).Count;
            Check(_keysLeft == 0, $"the key was not consumed by ACE (still {_keysLeft} held; Structure 1 should be used up)");

            // Clean-up: open and close the chest once. ACE's Open schedules its Reset (120 s), which relocks it; the test waits for that and reports whether the second 0x02D2 came.
            var open = S.Actions.OpenContainer(chest, ActionSource.Host);
            yield return open;
            Log("OpenContainer (the unlocked chest): " + Describe(open));
            Check(open.Outcome is { Kind: ActionOutcomeKind.Confirmed }, "the unlocked chest did not open: " + Describe(open));
            var closeAfter = S.Actions.CloseContainer(chest, ActionSource.Host);
            yield return closeAfter;
            _relock = "waiting";
            foreach (var h in Until(() => relockSeen || (S.State.Objects.TryGet(chest, out var o) && o.Locked == true), 150))
            {
                yield return h;
            }

            _relock = relockSeen || (S.State.Objects.TryGet(chest, out var last) && last.Locked == true) ? "seen" : "not_seen";
            Log($"the chest's reset: {_relock} (a missing broadcast is a server property, fix_chest_missing_inventory_window; the chest is reset on the server either way, 120 s after it was opened)");
            foreach (var h in LogOffAndQuit())
            {
                yield return h;
            }
        }
    }

    private sealed class QuestionLeg(IReadOnlyList<uint> classes, Action<string> log, DatSet dats) : LiveLeg("keychest-question", classes, log, dats)
    {
        private readonly List<string> _bytes = [];
        private string _text = "none";
        private string _answer = "none";
        private int _requests;
        private int _closes;
        private uint _type;

        public IReadOnlyList<string> Bytes => _bytes;

        public bool Requested { get; private set; }

        public bool Closed { get; private set; }

        public string Verdict() =>
            string.Create(CultureInfo.InvariantCulture, $"question text_len={_text.Length} requested={(Requested ? "yes" : "no")} type={_type} answer={_answer} closed={(Closed ? "yes" : "no")} requests={_requests} closes={_closes} problems={Problems.Count}");

        protected override IEnumerable<ActionHandle?> Script()
        {
            // The server's 0x0274 and 0x0276 arrive in GameEvent envelopes (0xF7B0: opcode, the player, the event sequence, the event type, the body).
            using var tap = S.Events.SubscribeRaw((direction, time, fragment, queue, body) =>
            {
                if (direction == MessageDirection.Inbound && body.Length >= 16 && BinaryPrimitives.ReadUInt32LittleEndian(body) == 0xF7B0
                    && BinaryPrimitives.ReadUInt32LittleEndian(body[12..]) is 0x0274u or 0x0276u)
                {
                    _bytes.Add(string.Create(CultureInfo.InvariantCulture, $"t={time:0.0} event=0x{BinaryPrimitives.ReadUInt32LittleEndian(body[12..]):X4} bytes={Hex(body)}"));
                }
            });
            using var requested = S.Events.Subscribe((in ConfirmationRequested e) => _requests++);
            using var closed = S.Events.Subscribe((in ConfirmationClosed e) =>
            {
                _closes++;
                Closed |= e is { Reason: ConfirmationCloseReason.Answered, Accepted: false };
            });

            foreach (var h in EnterThrowaway())
            {
                yield return h;
            }

            foreach (var h in Pause(3))
            {
                yield return h;
            }

            Require(S.State.PendingConfirmation is null, "a question was already open before the test asked one");
            uint gem = HeldOf(GemWcid).FirstOrDefault();
            if (gem == 0)
            {
                var spawn = S.Tools.SpawnItem(GemWcid, null, ActionSource.Host);
                yield return spawn;
                Require(spawn.Result is not null, "the Coordination To Endurance Gem grant (wcid 23045) failed: " + Describe(spawn) + " (the lead's other path: a craft recipe with UseCraftSuccessDialog, or an NPC's InqYesNo emote)");
                gem = spawn.Result!.Guid;
                foreach (var h in WaitHeld(gem, "the gem"))
                {
                    yield return h;
                }

                foreach (var h in Pause(1))
                {
                    yield return h;
                }
            }

            var use = S.Actions.Use(gem, ActionSource.Host);
            foreach (var h in Until(() => S.State.PendingConfirmation is not null, 10))
            {
                yield return h;
            }

            Log("Use (the gem): " + Describe(use));
            var pending = S.State.PendingConfirmation;
            Requested = Check(pending is not null, "no GameEvent 0x0274 arrived within 10 s of using the gem (ACE may have refused the use: " + Describe(use) + ")");
            if (pending is null)
            {
                foreach (var h in LogOffAndQuit())
                {
                    yield return h;
                }

                yield break;
            }

            _type = pending.Type;
            _text = pending.Text;
            Log($"the request: type {pending.Type}, context {pending.Context}, text '{pending.Text}'");
            Check(pending.Type == 3 && pending.Text.Contains("transfer 10 points", StringComparison.Ordinal), $"the request is not ACE's AlterAttribute question: type {pending.Type}, '{pending.Text}'");

            var no = S.Actions.RespondToConfirmation(pending.Type, pending.Context, accepted: false, ActionSource.Host);
            yield return no;
            _answer = no.Outcome is { } o ? o.Kind.ToString() : "not_settled";
            Log("RespondToConfirmation(No): " + Describe(no));
            Check(no.Outcome is { Succeeded: true }, "the answer was not sent: " + Describe(no));
            foreach (var h in Pause(2))
            {
                yield return h;
            }

            Check(S.State.PendingConfirmation is null, "the question is still open after the answer");
            Check(Closed, "no ConfirmationClosed by the answer (accepted false) was published");
            Check(HeldOf(GemWcid).Count >= 1, "the gem is gone from the pack after a No");
            foreach (var h in LogOffAndQuit())
            {
                yield return h;
            }
        }
    }
}
