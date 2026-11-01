using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Actions.Trackers;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Recording;
using Lifestone.Client.Session.State;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M5 6 w2, the read model: the server's questions (GameEvent 0x0274 and 0x0276; ACE's one-per-type rule, <c>ConfirmationManager.cs</c> @95ef426d), the public property
/// updates on the object table (a chest's <see cref="WorldObject.Locked"/> from 0x02D2 key 3), the player's private updates (0x02D1 Afk, 0x02D7, 0x02D9, 0x02DB), the
/// three login notices logged once by name, and the character options applied locally (D32).
/// </summary>
public sealed class RetailTrancheReducerTests
{
    private const uint Chest = 0x7A9B4011;

    private static ReducerHarness InWorld()
    {
        var h = new ReducerHarness();
        h.EnterWorld();
        h.ClearEvents();
        return h;
    }

    private static void Ask(ReducerHarness h, uint type, uint context, string text = "Do you want to?") =>
        h.Reducer.OnConfirmationRequest(h.Context, new GameEventHeader(ReducerHarness.PlayerGuid, 1, GameEventTypes.CharacterConfirmationRequest), new ConfirmationRequestEvent(type, context, text));

    private static void Done(ReducerHarness h, uint type, uint context) =>
        h.Reducer.OnConfirmationDone(h.Context, new GameEventHeader(ReducerHarness.PlayerGuid, 2, GameEventTypes.CharacterConfirmationDone), new ConfirmationDoneEvent(type, context));

    private static void Lock(ReducerHarness h, byte sequence, bool locked, uint guid = Chest) =>
        h.Reducer.OnPublicPropertyBool(h.Context, new PublicPropertyUpdate<bool>(sequence, guid, GameStateReducer.LockedProperty, locked));

    private static void CreateChest(ReducerHarness h) =>
        h.Reducer.OnCreateObject(h.Context, ReducerHarness.Create(Chest, ReducerHarness.Stamps(), ReducerHarness.Position(0x860201AD, 12, 22), name: "Chest"), isUpdate: false);

    [Fact]
    public void A_request_opens_the_question_and_the_server_s_done_closes_it()
    {
        var h = InWorld();
        Ask(h, 7, 0x11, "Join the fellowship?");

        var pending = Assert.IsType<PendingConfirmation>(h.State.PendingConfirmation);
        Assert.Equal((7u, 0x11u, "Join the fellowship?", ConfirmationType.YesNo), (pending.Type, pending.Context, pending.Text, pending.Kind));
        Assert.Equal(new ConfirmationRequested(7, 0x11, "Join the fellowship?"), Assert.Single(h.Of<ConfirmationRequested>()));

        Done(h, 7, 0x11);
        Assert.Null(h.State.PendingConfirmation);
        Assert.Equal(new ConfirmationClosed(7, 0x11, ConfirmationCloseReason.ServerDone, null), Assert.Single(h.Of<ConfirmationClosed>()));
    }

    [Fact]
    public void A_done_for_another_context_or_for_no_open_question_is_ignored()
    {
        var h = InWorld();
        Ask(h, 2, 5);
        Done(h, 2, 6); // ACE sends 0x0276 only for the context it holds (ConfirmationManager.cs:57-61)
        Done(h, 3, 5);

        Assert.NotNull(h.State.PendingConfirmation);
        Assert.Empty(h.Of<ConfirmationClosed>());
    }

    [Fact]
    public void One_question_per_type_is_open_and_a_second_of_the_same_type_replaces_it_with_a_warning()
    {
        var h = InWorld();
        Ask(h, 2, 1, "Alter skill?");
        Ask(h, 7, 2, "Yes or no?");

        Assert.Equal(2, h.State.PendingConfirmations.Count);
        Assert.Equal(7u, h.State.PendingConfirmation!.Type); // the newest

        Ask(h, 2, 3, "Alter skill again?");
        Assert.Equal(new[] { (7u, 2u), (2u, 3u) }, h.State.PendingConfirmations.Select(c => (c.Type, c.Context)));
        Assert.Equal(new ConfirmationClosed(2, 1, ConfirmationCloseReason.Replaced, null), Assert.Single(h.Of<ConfirmationClosed>()));
        Assert.Contains(h.Log.Entries, l => l.Message.Contains("replaced the first", StringComparison.Ordinal));
    }

    [Fact]
    public void The_world_exit_closes_every_open_question()
    {
        var h = InWorld();
        Ask(h, 2, 1);
        Ask(h, 7, 2);

        h.Reducer.OnCharacterLogOff(h.Context);

        Assert.Null(h.State.PendingConfirmation);
        Assert.Equal(
            new[] { new ConfirmationClosed(2, 1, ConfirmationCloseReason.WorldExit, null), new ConfirmationClosed(7, 2, ConfirmationCloseReason.WorldExit, null) },
            h.Of<ConfirmationClosed>());
    }

    [Fact]
    public void The_answer_notice_closes_the_question_and_says_the_answer()
    {
        var h = InWorld();
        Ask(h, 7, 9);
        h.Reducer.ApplyLocal(new RecordingNotice(SessionNotices.ConfirmationAnswered, 7, "9:0"));

        Assert.Null(h.State.PendingConfirmation);
        Assert.Equal(new ConfirmationClosed(7, 9, ConfirmationCloseReason.Answered, false), Assert.Single(h.Of<ConfirmationClosed>()));
        Assert.Throws<InvalidDataException>(() => h.Reducer.ApplyLocal(new RecordingNotice(SessionNotices.ConfirmationAnswered, 7, "9")));
    }

    [Fact]
    public void A_locked_chest_reads_what_the_server_last_said_and_null_before()
    {
        var h = InWorld();
        CreateChest(h);
        Assert.True(h.State.Objects.TryGet(Chest, out var chest));
        Assert.Null(chest.Locked); // the CreateObject carries no lock

        Lock(h, 1, locked: true);
        Assert.True(chest.Locked);
        Lock(h, 2, locked: false); // the key unlocked it (ACE Lock.cs:275)
        Assert.False(chest.Locked);
        Assert.True(chest.Properties.TryGetBool(GameStateReducer.LockedProperty, out var stored) && !stored);
        Assert.Equal(
            new[] { new ObjectPropertyChanged(Chest, ObjectPropertyKind.Bool, 3), new ObjectPropertyChanged(Chest, ObjectPropertyKind.Bool, 3) },
            h.Of<ObjectPropertyChanged>());
    }

    [Fact]
    public void An_older_sequence_for_the_same_object_and_key_is_dropped_as_stale_and_a_wrap_is_newer()
    {
        var h = InWorld();
        CreateChest(h);
        Assert.True(h.State.Objects.TryGet(Chest, out var chest));

        Lock(h, 5, locked: false);
        Lock(h, 4, locked: true); // older
        Assert.False(chest.Locked);
        Assert.Contains(h.Of<MessageDropped>(), d => d.Opcode == MessageOpcodes.PublicUpdatePropertyBool && d.Reason == DropReason.Stale);

        Lock(h, 250, locked: true); // a distance above 127 from 5: older by the wrap rule
        Assert.False(chest.Locked);
        Lock(h, 100, locked: false);
        Lock(h, 200, locked: false);
        Lock(h, 3, locked: true); // 3 after 200 is a wrap: newer
        Assert.True(chest.Locked);
    }

    [Fact]
    public void Every_public_kind_is_kept_by_key_and_the_player_s_own_lands_in_the_qualities()
    {
        var h = InWorld();
        CreateChest(h);
        h.Reducer.OnPublicPropertyInt64(h.Context, new PublicPropertyUpdate<long>(1, Chest, 2, 123456789012));
        h.Reducer.OnPublicPropertyFloat(h.Context, new PublicPropertyUpdate<double>(1, Chest, 22, 0.5));
        h.Reducer.OnPublicPropertyString(h.Context, new PublicPropertyUpdate<string>(1, Chest, 16, "Locked tight"));
        h.Reducer.OnPublicPropertyDataId(h.Context, new PublicPropertyUpdate<uint>(1, Chest, 1, 0x02000123));
        h.Reducer.OnPublicPropertyBool(h.Context, new PublicPropertyUpdate<bool>(1, ReducerHarness.PlayerGuid, 110, true));

        Assert.True(h.State.Objects.TryGet(Chest, out var chest));
        var p = chest.Properties;
        Assert.True(p.TryGetInt64(2, out var i64) && i64 == 123456789012);
        Assert.True(p.TryGetFloat(22, out var f) && f == 0.5);
        Assert.True(p.TryGetString(16, out var s) && s == "Locked tight");
        Assert.True(p.TryGetDataId(1, out var d) && d == 0x02000123);
        Assert.Equal(4, p.Count);
        Assert.True(h.State.Self.Qualities.Afk);
        Assert.Equal(
            new[] { ObjectPropertyKind.Int64, ObjectPropertyKind.Float, ObjectPropertyKind.String, ObjectPropertyKind.DataId, ObjectPropertyKind.Bool },
            h.Of<ObjectPropertyChanged>().Select(e => e.Kind));
    }

    [Fact]
    public void An_update_for_an_object_not_in_the_table_is_ignored_and_named_once()
    {
        var h = InWorld();
        Lock(h, 1, locked: true, guid: 0x7A9B4999);
        Lock(h, 2, locked: true, guid: 0x7A9B4998);

        Assert.Empty(h.Of<ObjectPropertyChanged>());
        Assert.Single(h.Log.Entries, l => l.Message.Contains("TODO(m5-6-property-unknown-object)", StringComparison.Ordinal));
    }

    [Fact]
    public void The_private_updates_reach_the_player_s_qualities_and_an_older_one_is_dropped()
    {
        var h = InWorld();
        h.Reducer.OnPrivatePropertyBool(h.Context, new PrivatePropertyUpdate<bool>(3, PlayerQualities.AfkProperty, true));
        h.Reducer.OnPrivatePropertyBool(h.Context, new PrivatePropertyUpdate<bool>(2, PlayerQualities.AfkProperty, false)); // older: dropped
        h.Reducer.OnPrivatePropertyInstanceId(h.Context, new PrivatePropertyUpdate<uint>(1, 2, 0x80000777)); // CurrentAttacker
        h.Reducer.OnPrivatePropertyDataId(h.Context, new PrivatePropertyUpdate<uint>(1, 2, 0x09000001)); // MotionTable
        var corpse = ReducerHarness.Position(0xA9B40019, 1, 2, 3);
        h.Reducer.OnPrivatePosition(h.Context, new PrivatePositionUpdate(1, 14, corpse)); // LastOutsideDeath

        var q = h.State.Self.Qualities;
        Assert.True(q.Afk);
        Assert.True(q.TryGetInstanceId(2, out var attacker) && attacker == 0x80000777);
        Assert.True(q.TryGetDataId(2, out var table) && table == 0x09000001);
        Assert.True(q.TryGetPosition(14, out var where) && where == corpse);
        Assert.Equal(
            new[] { new QualitiesChanged(QualityChange.Bool, 110), new QualitiesChanged(QualityChange.InstanceId, 2), new QualitiesChanged(QualityChange.DataId, 2), new QualitiesChanged(QualityChange.Position, 14) },
            h.Of<QualitiesChanged>());
        Assert.Contains(h.Of<MessageDropped>(), d => d.Opcode == MessageOpcodes.PrivateUpdatePropertyBool && d.Reason == DropReason.Stale);
    }

    [Fact]
    public void The_three_login_notices_are_logged_once_each_by_name_and_change_nothing()
    {
        var h = InWorld();
        var version = h.State.Version;
        var header = new GameEventHeader(ReducerHarness.PlayerGuid, 1, 0);
        for (var i = 0; i < 3; i++)
        {
            h.Reducer.OnTurbineChatChannels(h.Context, header, new TurbineChatChannelsEvent(0, 2, 3, 4, 5, 10, 0, 7, 8, 9));
            h.Reducer.OnCharacterTitle(h.Context, header, new CharacterTitleEvent(1, 1, [1]));
            h.Reducer.OnFriendsListUpdate(h.Context, header, new FriendsListUpdateEvent([], 0));
        }

        Assert.Empty(h.Events);
        Assert.Equal(version, h.State.Version);
        foreach (var (type, name) in new[] { (0x0295u, "GameEvent 0x0295 SetTurbineChatChannels"), (0x0029u, "GameEvent 0x0029 CharacterTitle"), (0x0021u, "GameEvent 0x0021 FriendsListUpdate") })
        {
            Assert.Equal(3, h.Reducer.LoginNoticeCount(type));
            var line = Assert.Single(h.Log.Entries, l => l.Message.StartsWith(name, StringComparison.Ordinal));
            Assert.Contains("decoded and ignored", line.Message, StringComparison.Ordinal);
            Assert.Equal(LogSeverity.Info, line.Severity);
        }
    }

    [Fact]
    public void The_options_notice_sets_both_words_and_drops_the_session_overrides()
    {
        var h = InWorld();
        h.Reducer.OnPlayerDescription(h.Context, new GameEventHeader(ReducerHarness.PlayerGuid, 1, GameEventTypes.PlayerDescription), new PlayerDescriptionMsg { Options1 = 0x1, Options2 = 0x2 });
        h.Reducer.ApplyLocal(new RecordingNotice(SessionNotices.SessionOptionSet, 0x8000, "1:1"));
        Assert.Equal(0x8001u, h.State.Self.Qualities.CharacterOptions1);
        h.ClearEvents();

        h.Reducer.ApplyLocal(new RecordingNotice(SessionNotices.CharacterOptionsSet, 0x50C4A54A, "00948700"));

        Assert.Equal((0x50C4A54Au, 0x00948700u), (h.State.Self.Qualities.CharacterOptions1, h.State.Self.Qualities.CharacterOptions2));
        Assert.Equal(new QualitiesChanged(QualityChange.Options, 0), Assert.Single(h.Of<QualitiesChanged>()));
        Assert.Throws<InvalidDataException>(() => h.Reducer.ApplyLocal(new RecordingNotice(SessionNotices.CharacterOptionsSet, 1, "zz")));
    }
}

/// <summary>
/// M5 6 w2, the six actions (<see cref="IActionApi.RespondToConfirmation"/>, <see cref="IActionApi.SetCharacterOptions"/>, <see cref="IActionApi.RecallLifestone"/>,
/// <see cref="IActionApi.RecallMarketplace"/>, <see cref="IActionApi.Emote"/>, <see cref="IActionApi.SetAfk"/>): what each sends, and how ACE's answers complete or refuse it
/// (ACE @95ef426d: <c>ConfirmationManager.cs</c>, <c>GameActionSetCharacterOptions.cs</c>, <c>Player_Location.cs:132-262</c>, <c>Player.cs:822-831</c>, <c>Player_Networking.cs:438-452</c>).
/// </summary>
public sealed class RetailTrancheActionTests
{
    private const uint Player = WorldActionRig.Player;

    private static void Ask(WorldActionRig r, uint type, uint context) =>
        r.H.R.Reducer.OnConfirmationRequest(r.H.R.Context, new GameEventHeader(Player, 9, GameEventTypes.CharacterConfirmationRequest), new ConfirmationRequestEvent(type, context, "Really?"));

    private static void Afk(WorldActionRig r, byte sequence, bool value) =>
        r.H.R.Reducer.OnPrivatePropertyBool(r.H.R.Context, new PrivatePropertyUpdate<bool>(sequence, PlayerQualities.AfkProperty, value));

    [Fact]
    public void An_answer_sends_0x0275_completes_Sent_and_closes_the_question_at_once()
    {
        var r = new WorldActionRig();
        Ask(r, 7, 0x21);
        var answer = r.Api.RespondToConfirmation(7, 0x21, accepted: true, ActionSource.Ui);
        r.Tick();

        var sent = Assert.Single(r.H.Net.Of(FakeSendKind.ConfirmationResponse));
        Assert.Equal((7u, 0x21u, 1), (sent.Amount, sent.Guid, sent.Index));
        Assert.Equal(ActionOutcomeKind.Sent, answer.Outcome!.Value.Kind);
        Assert.Null(r.H.State.PendingConfirmation);
        Assert.Equal(new ConfirmationClosed(7, 0x21, ConfirmationCloseReason.Answered, true), Assert.Single(r.H.R.Of<ConfirmationClosed>()));
    }

    [Fact]
    public void An_answer_to_no_open_question_is_refused_and_sends_nothing()
    {
        var r = new WorldActionRig();
        Ask(r, 7, 0x21);
        var answer = r.Api.RespondToConfirmation(7, 0x22, accepted: false, ActionSource.Ui);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Refused, answer.Outcome!.Value.Kind);
        Assert.Empty(r.H.Net.Of(FakeSendKind.ConfirmationResponse));
        Assert.NotNull(r.H.State.PendingConfirmation);
    }

    [Fact]
    public void Set_character_options_sends_the_block_from_the_read_model_and_applies_the_words_at_once()
    {
        var r = new WorldActionRig();
        r.H.Describe(new PlayerDescriptionMsg { Options1 = 0x1, Options2 = 0x2, SpellbookFilters = 0x3FFF & ~0x800u, SpellBars = [[0x0B4F, 0x0B50], [], [0x0042]] });
        r.Api.SetSessionOption(1, 0x8000, true);
        var set = r.Api.SetCharacterOptions(0x50C4A54A, 0x00948700, ActionSource.Ui);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Sent, set.Outcome!.Value.Kind);
        var block = Assert.Single(r.H.Net.OptionBlocks);
        Assert.Equal((0x50C4A54Au, 0x00948700u, 0x3FFF & ~0x800u), (block.Options1, block.Options2, block.SpellbookFilters));
        Assert.Equal(8, block.SpellBars.Length); // as ACE's description sends them
        Assert.Equal(new uint[] { 0x0B4F, 0x0B50 }, block.SpellBars[0]);
        Assert.Equal(new uint[] { 0x0042 }, block.SpellBars[2]);
        Assert.Empty(block.SpellBars[7]);
        Assert.Equal((0x50C4A54Au, 0x00948700u), (r.H.State.Self.Qualities.CharacterOptions1, r.H.State.Self.Qualities.CharacterOptions2));
    }

    [Fact]
    public void Set_character_options_before_the_description_is_refused()
    {
        var r = new WorldActionRig();
        var set = r.Api.SetCharacterOptions(1, 2, ActionSource.Ui);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Refused, set.Outcome!.Value.Kind);
        Assert.Empty(r.H.Net.OptionBlocks);
    }

    [Fact]
    public void A_lifestone_recall_sends_0x0063_and_is_confirmed_by_the_teleport()
    {
        var r = new WorldActionRig();
        var recall = r.Api.RecallLifestone(ActionSource.Ui);
        r.Tick();
        Assert.Single(r.H.Net.Of(FakeSendKind.TeleToLifestone));

        r.H.ServerMessage("Tester is recalling to the lifestone.", 0x17);
        r.Tick(12);
        Assert.False(recall.IsCompleted);

        r.Raw(AceWireWriters.PlayerTeleport(2));
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, recall.Outcome!.Value.Kind);
        Assert.Contains("after the recall line", recall.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0x001Du, "YoureTooBusy")]
    [InlineData(0x0498u, "YouHaveMovedTooFar")]
    [InlineData(0x04CCu, "YouHaveBeenInPKBattleTooRecently")]
    [InlineData(0x055Du, "ExitTrainingAcademyToUseCommand")]
    public void A_recall_refused_by_a_WeenieError_fails_with_its_name(uint code, string name)
    {
        var r = new WorldActionRig();
        var recall = r.Api.RecallMarketplace(ActionSource.Ui);
        r.Tick();
        Assert.Single(r.H.Net.Of(FakeSendKind.TeleToMarketPlace));

        r.WeenieError(code);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.ServerError, recall.Outcome!.Value.Kind);
        Assert.Equal(code, recall.Outcome.Value.Code);
        Assert.Contains(name, recall.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void The_olthoi_refusal_is_the_marketplace_s_and_the_sanctuary_line_the_lifestone_s()
    {
        var r = new WorldActionRig();
        var lifestone = r.Api.RecallLifestone(ActionSource.Ui);
        r.Tick();
        r.WeenieError(0x0593); // OlthoiCanOnlyRecallToLifestone: not an answer to a lifestone recall
        r.Tick();
        Assert.False(lifestone.IsCompleted);

        r.H.ServerMessage(RecallTracker.NoSanctuaryLine, 1);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.ServerError, lifestone.Outcome!.Value.Kind);
        Assert.Contains("sanctuary", lifestone.Outcome.Value.Detail, StringComparison.Ordinal);

        var market = r.Api.RecallMarketplace(ActionSource.Ui);
        r.Tick();
        r.WeenieError(0x0593);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.ServerError, market.Outcome!.Value.Kind);
        Assert.Contains("OlthoiCanOnlyRecallToLifestone", market.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_recall_times_out_past_ACE_s_delay_and_the_margin_without_a_retry()
    {
        var r = new WorldActionRig();
        var market = r.Api.RecallMarketplace(ActionSource.Ui);
        r.Tick();
        r.Tick(ActionPolicyRecall.MarketplaceSeconds - 1);
        Assert.False(market.IsCompleted);
        r.Tick(2);
        Assert.Equal(ActionOutcomeKind.TimedOut, market.Outcome!.Value.Kind);

        var lifestone = r.Api.RecallLifestone(ActionSource.Ui);
        r.Tick();
        r.Tick(ActionPolicyRecall.LifestoneSeconds + 1);
        Assert.Equal(ActionOutcomeKind.TimedOut, lifestone.Outcome!.Value.Kind);
        Assert.Contains(r.H.Log.Entries, l => l.Message.Contains("TODO(m5-6-recall-anim-length)", StringComparison.Ordinal));
        Assert.Single(r.H.Net.Of(FakeSendKind.TeleToMarketPlace));
        Assert.Single(r.H.Net.Of(FakeSendKind.TeleToLifestone));
    }

    [Fact]
    public void An_emote_sends_0x01DF_and_is_confirmed_by_the_server_s_echo()
    {
        var r = new WorldActionRig();
        var emote = r.Api.Emote("waves.", ActionSource.Ui);
        r.Tick();
        Assert.Equal("waves.", Assert.Single(r.H.Net.Of(FakeSendKind.Emote)).Text);

        r.Raw(AceWireWriters.EmoteText(0x50000099, "Other", "waves too.")); // someone else's
        r.Tick();
        Assert.False(emote.IsCompleted);

        r.Raw(AceWireWriters.EmoteText(Player, "Tester", "waves."));
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, emote.Outcome!.Value.Kind);
    }

    [Fact]
    public void An_empty_emote_is_refused_and_a_gagged_one_is_a_server_error()
    {
        var r = new WorldActionRig();
        var empty = r.Api.Emote("  ", ActionSource.Ui);
        Assert.Equal(ActionOutcomeKind.Refused, empty.Outcome!.Value.Kind);

        var gagged = r.Api.Emote("waves.", ActionSource.Ui);
        r.Tick();
        r.Server((p, s) => AceWireWriters.TransientString(p, s, ChatReplies.GagLineStart + " for 10 minutes."));
        r.Tick();
        Assert.Equal(ActionOutcomeKind.ServerError, gagged.Outcome!.Value.Kind);
    }

    [Fact]
    public void Afk_with_a_message_sends_0x0010_then_0x000F_and_is_confirmed_by_0x02D1()
    {
        var r = new WorldActionRig();
        var away = r.Api.SetAfk(true, "brb", ActionSource.Ui);
        r.Tick();

        var sends = r.H.Net.Sends.Where(s => s.Kind is FakeSendKind.SetAfkMessage or FakeSendKind.SetAfkMode).ToList();
        Assert.Equal(new[] { FakeSendKind.SetAfkMessage, FakeSendKind.SetAfkMode }, sends.Select(s => s.Kind));
        Assert.Equal(("brb", 1), (sends[0].Text, sends[1].Index));

        Afk(r, 1, false); // not the value asked
        r.Tick();
        Assert.False(away.IsCompleted);

        Afk(r, 2, true);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, away.Outcome!.Value.Kind);
        Assert.True(r.H.State.Self.Qualities.Afk);
    }

    [Fact]
    public void Back_from_afk_sends_0x000F_alone_and_times_out_without_0x02D1()
    {
        var r = new WorldActionRig();
        var back = r.Api.SetAfk(false, "ignored", ActionSource.Ui);
        r.Tick();
        Assert.Equal(0, Assert.Single(r.H.Net.Sends, s => s.Kind is FakeSendKind.SetAfkMessage or FakeSendKind.SetAfkMode).Index);

        r.Tick(ActionPolicy.TalkSeconds + 1);
        Assert.Equal(ActionOutcomeKind.TimedOut, back.Outcome!.Value.Kind);
    }

    [Fact]
    public void The_kill_switch_and_dry_run_apply_to_automation()
    {
        var r = new WorldActionRig();
        r.Api.DryRun = true;
        var dry = r.Api.RecallLifestone(ActionSource.Automation);
        Assert.Equal(ActionOutcomeKind.DryRun, dry.Outcome!.Value.Kind);
        Assert.Contains("0x0063", dry.Outcome.Value.Detail, StringComparison.Ordinal);

        r.Api.DryRun = false;
        r.Api.KillSwitch = true;
        var stopped = r.Api.SetAfk(true, null, ActionSource.Automation);
        Assert.Equal(ActionOutcomeKind.Refused, stopped.Outcome!.Value.Kind);
        Assert.Equal(ActionApi.KillSwitchRefusal, stopped.Outcome.Value.Detail);

        r.Tick();
        Assert.Empty(r.H.Net.Of(FakeSendKind.TeleToLifestone));
        Assert.Empty(r.H.Net.Of(FakeSendKind.SetAfkMode));
    }

    [Fact]
    public void Every_action_is_refused_outside_the_world()
    {
        var h = new ActionHarness();
        h.AtCharacterSelect();
        var handles = new[]
        {
            h.Api.RespondToConfirmation(7, 1, true, ActionSource.Ui),
            h.Api.SetCharacterOptions(1, 2, ActionSource.Ui),
            h.Api.RecallLifestone(ActionSource.Ui),
            h.Api.RecallMarketplace(ActionSource.Ui),
            h.Api.Emote("waves.", ActionSource.Ui),
            h.Api.SetAfk(true, null, ActionSource.Ui),
        };
        h.Tick();

        Assert.All(handles, x => Assert.Equal(ActionOutcomeKind.Refused, x.Outcome!.Value.Kind));
        Assert.DoesNotContain(h.Net.Sends, s => s.Kind >= FakeSendKind.ConfirmationResponse);
    }
}
