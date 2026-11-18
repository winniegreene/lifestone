using System.Globalization;
using System.Text.RegularExpressions;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 item 14 S2c: the stop at the buff bot (<see cref="BuffBotStation"/>) over <see cref="J3bRig"/> with the bot scripted as the server mod's answers a direct talk (ACE's GameActionTalkDirect.cs @95ef426d and the
/// mod's emote chain): the confirmation line <c>You tell NAME, "WORD"</c> of chat type OutgoingTell, a tell that it begins, one enchantment a cast (GameEvent 0x02C2, the Other spell of the line at level VII for an
/// hour), then the tell that it is done. The station finds the bot, walks to five metres of it through the travel seam, tells it the word by its id, reads each line as it lands and ends with every line fresh; one
/// recovery (the word told again); every failure by name. The stop's mechanics are proved on a run whose profile is the tuskers' (<see cref="Bot.Profile"/>: the option naming <c>tusker</c>); the default run's word,
/// <c>all</c>, and its 28 lines have their own tests at the end.
/// </summary>
public sealed partial class BuffBotStationTests
{
    private const uint OutgoingTell = 0x04;

    [GeneratedRegex(@"^bot=7A9B4F14 word=(?<word>\S+) lines=(?<lines>\d+) landed=(?<landed>\d+) told=(?<told>\d+) recoveries=(?<recoveries>\d+) s=(?<s>\d+) min_left_s=(?<min>\d+) fee=(?<fee>free|paid) credits=(?<credits>-|\d+) gave=(?<gave>\d+) ok=(?<ok>yes|no)(?: why=(?<why>\S+))?$")]
    private static partial Regex Verdict();

    private static BuffBotProfile Tusker => BuffBotWire.Tusker;

    /// <summary>The profile's lines: read, never written here, so a line added to the profile changes no test of the stop (the list is pinned in the mod's <c>BuffBotRulesTests</c>).</summary>
    private static int N => Tusker.Lines.Count;

    /// <summary>The seconds one answer may take (<see cref="BuffBotRule.AnswerSeconds"/>: a cast's 8 s a line and the margin), as the logs print it.</summary>
    private static string Allowance => BuffBotRule.AnswerSeconds(Tusker).ToString("0", CultureInfo.InvariantCulture);

    /// <summary>The scripted bot: placed in view, it hears each direct talk sent to its id and answers as <see cref="Casts"/> says.</summary>
    private sealed class Bot
    {
        private readonly List<(double Due, Action What)> _later = [];
        private int _heard;

        public Bot(bool inView = true)
        {
            if (inView)
            {
                Place();
            }
        }

        public J3bRig T { get; } = new();

        /// <summary>The profile the run asks the bot for (<see cref="JourneyFacts.BuffBotProfile"/>, the option's): the tuskers' here, as <c>--autopilot-buff-bot-word=tusker</c> sets it; null: the facts' own default.</summary>
        public BuffBotProfile? Profile { get; set; } = Tusker;

        /// <summary>The lines the bot casts in answer to the n-th talk it hears (1 the first), in that order; null: the whole profile in its order.</summary>
        public Func<int, IEnumerable<BuffBotLine>>? Casts { get; set; }

        /// <summary>False: the server sends no confirmation line (and the bot answers nothing).</summary>
        public bool Echo { get; set; } = true;

        /// <summary>When set, the server answers each talk with this WeenieError and nothing more.</summary>
        public uint Refusal { get; set; }

        /// <summary>The talks the bot answers nothing to after the confirmation line (ACE's emote manager drops a talk that arrives while an answer runs).</summary>
        public HashSet<int> Dropped { get; } = [];

        /// <summary>M6 14 bot reach: by the n-th talk the bot hears (1 the first), the lines it casts before it stops the round with <see cref="BuffBotWire.OutOfReach"/> and returns the credit the round took.</summary>
        public Dictionary<int, int> OutOfReachAfter { get; } = [];

        /// <summary>Seconds between two casts.</summary>
        public double CastEvery { get; set; } = 4;

        /// <summary>The profiles this bot knows (a server mod of before the second profile knows the tuskers' alone); null: every profile of <see cref="BuffBotWire"/>.</summary>
        public IReadOnlyList<BuffBotProfile>? Knows { get; set; }

        /// <summary>The talks heard: the session clock and the text.</summary>
        public List<(double At, string Text)> Heard { get; } = [];

        /// <summary>D138: her credits in the bot's ledger.</summary>
        public int Credits { get; set; }

        /// <summary>D138: the gives heard: the session clock and the item's id.</summary>
        public List<(double At, uint Item)> Given { get; } = [];

        /// <summary>D138: the give is examined and handed back (ACE's Refuse emote line) in place of taken.</summary>
        public bool RefuseGive { get; set; }

        /// <summary>D138: the give is taken and no credit line follows.</summary>
        public bool SilentAfterGive { get; set; }

        /// <summary>D138: the bot answers the credit word with nothing.</summary>
        public bool MuteCredits { get; set; }

        /// <summary>D138: the bot answers every word with <see cref="BuffBotWire.NeedCredit"/>.</summary>
        public bool AlwaysNeedsCredit { get; set; }

        /// <summary>D138: while true the bot is at the shop: every talk is answered <see cref="BuffBotWire.AwayTell"/> and the last word is kept for its return (<see cref="ComeBack"/>).</summary>
        public bool Away { get; set; }

        /// <summary>D138: the rests the bot tells before its first cast, in seconds (each delays the casts by as long).</summary>
        public int Rest { get; set; }

        /// <summary>The session clock when the bot told its done line, or null.</summary>
        public double? DoneAt { get; private set; }

        /// <summary>The session clock the bot's done line is due at, once a round is scheduled, or null.</summary>
        public double? DoneDue { get; private set; }

        private string? _kept;

        /// <summary>Her level, as the server sends it.</summary>
        public void Level(int level) => T.R.PrivateInt(PlayerQualities.LevelProperty, level);

        /// <summary>A stack of Platinum Scarabs in her main pack.</summary>
        public void Scarabs(uint guid, int count = 1)
        {
            var r = T.R.H.R;
            var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: BuffBotWire.FeeName);
            r.Reducer.OnCreateObject(r.Context, basis with { Weenie = basis.Weenie with { Name = BuffBotWire.FeeName, Header = WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player, WeenieClassId = BuffBotWire.FeeWcid, StackSize = (ushort)count } }, isUpdate: false);
            T.R.Server((p, seq) => AceM3Writers.PutObjInContainer(p, seq, guid, WorldActionRig.Player, 0, 0));
        }

        /// <summary>The bot back at its post: its say, then the word it kept answered.</summary>
        public void ComeBack()
        {
            Away = false;
            Say(BuffBotWire.BackAnnouncement);
            if (_kept is { } word)
            {
                _kept = null;
                Answer(word, 0.5);
            }
        }

        /// <summary>The bot's say in local chat.</summary>
        public void Say(string text) => T.R.Raw(AceWireWriters.HearSpeech(text, BuffBotWire.Name, BuffBotWire.Guid, 0x02));

        /// <summary>The <c>landed=</c> field each time it changed, with the session clock.</summary>
        public List<(double At, int Landed)> LandedOverTime { get; } = [];

        public double Now => T.Now;

        public IEnumerable<string> Log => T.R.H.Log.Lines;

        public void Place()
        {
            var r = T.R.H.R;
            r.Reducer.OnCreateObject(r.Context, ReducerHarness.Create(BuffBotWire.Guid, ReducerHarness.Stamps(), ReducerHarness.Position(J3bRig.AcademyCell, 14, 26), name: BuffBotWire.Name), isUpdate: false);
        }

        public void After(double seconds, Action what) => _later.Add((Now + seconds, what));

        /// <summary>The bot's spell on her as ACE's 0x02C2 carries it: an hour from now, layer 1 (a recast by the same caster renews the entry).</summary>
        public void Enchant(BuffBotLine line, double duration = 3600)
        {
            var category = (ushort)(1000 + BuffBotWire.All.Lines.ToList().IndexOf(line)); // a category a line: every profile begins with the tuskers' lines
            T.R.Server((p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, new AceEnchantment((ushort)line.SpellId, 1, category, 0, 300, 0.0, duration, BuffBotWire.Guid, 0f, 0f, 0.0, 0x9001, 1, 0f, 0)));
        }

        public void Tell(string text)
        {
            var r = T.R.H.R;
            r.Reducer.OnTell(r.Context, T.R.H.Header, new TellEvent(text, BuffBotWire.Name, BuffBotWire.Guid, WorldActionRig.Player, 3, 0));
        }

        /// <summary>One frame: every new talk is heard, and what is due is done.</summary>
        public void Frame(IStationRun run)
        {
            var talks = T.R.H.Net.Of(FakeSendKind.TalkDirect);
            while (_heard < talks.Count)
            {
                Hear(talks[_heard++]);
            }

            var gives = T.R.H.Net.Of(FakeSendKind.GiveObject);
            while (Given.Count < gives.Count)
            {
                var item = gives[Given.Count].Guid;
                Given.Add((Now, item));
                if (RefuseGive)
                {
                    After(0.7, () => T.R.H.ServerMessage($"You allow {BuffBotWire.Name} to examine your {BuffBotWire.FeeName}."));
                    continue;
                }

                // ACE's give to an NPC with a Give emote: the item leaves the pack, "You give ...", then the bot's answer.
                After(0.7, () =>
                {
                    T.R.Raw(AceM3Writers.InventoryRemoveObject(item));
                    T.R.H.ServerMessage($"You give {BuffBotWire.Name} {BuffBotWire.FeeName}.");
                    if (!SilentAfterGive)
                    {
                        Credits++;
                        Tell(BuffBotWire.Credits(Credits));
                    }
                });
            }

            for (var i = 0; i < _later.Count; i++)
            {
                if (_later[i].Due <= Now)
                {
                    var due = _later[i].What;
                    _later.RemoveAt(i--);
                    due();
                }
            }

            var landed = int.Parse(Verdict().Match(run.VerdictFields()).Groups["landed"].Value, CultureInfo.InvariantCulture);
            if (LandedOverTime.Count == 0 || LandedOverTime[^1].Landed != landed)
            {
                LandedOverTime.Add((Now, landed));
            }
        }

        private void Hear(FakeSend talk)
        {
            Assert.Equal(BuffBotWire.Guid, talk.Guid);
            Heard.Add((Now, talk.Text!));
            if (Refusal != 0)
            {
                After(0.5, () => T.R.WeenieError(Refusal));
                return;
            }

            if (!Echo)
            {
                return;
            }

            After(0.5, () => T.R.H.ServerMessage($"You tell {BuffBotWire.Name}, \"{talk.Text}\"", (int)OutgoingTell));
            if (Dropped.Contains(_heard))
            {
                return;
            }

            if (Away)
            {
                After(1, () => Tell(BuffBotWire.AwayTell));
                if (BuffBotWire.ProfileOf(talk.Text) is not null)
                {
                    _kept = talk.Text;
                }

                return;
            }

            if (string.Equals(talk.Text, BuffBotWire.CreditWord, StringComparison.Ordinal))
            {
                if (!MuteCredits)
                {
                    After(1, () => Tell(BuffBotWire.Credits(Credits)));
                }

                return;
            }

            Answer(talk.Text!, 1);
        }

        /// <summary>The bot's answer to a word, from <paramref name="first"/> seconds on: the fee (D138), its first line, a cast a line, its last line.</summary>
        private void Answer(string word, double first)
        {
            if (BuffBotWire.ProfileOf(word) is not { } profile || Knows?.Contains(profile) == false)
            {
                After(first, () => Tell(BuffBotWire.Help));
                return;
            }

            if (AlwaysNeedsCredit || (T.R.H.State.Self.Qualities.Level >= BuffBotWire.FeeLevel && Credits == 0))
            {
                After(first, () => Tell(BuffBotWire.NeedCredit));
                return;
            }

            var paid = T.R.H.State.Self.Qualities.Level >= BuffBotWire.FeeLevel;
            if (paid)
            {
                Credits--;
                var left = Credits;
                After(first, () => Tell(BuffBotWire.Spent(left)));
            }

            if (OutOfReachAfter.TryGetValue(_heard, out var short_))
            {
                // the mod's reach rule (BuffBotBody.CastNext): the round stops at the first line whose spell's range she stands past; the credit is given back
                After(first, () => Tell(BuffBotWire.Begins(profile)));
                var stopAt = first;
                foreach (var line in profile.Lines.Take(short_))
                {
                    stopAt += CastEvery;
                    After(stopAt, () => Enchant(line));
                }

                After(stopAt + 1, () =>
                {
                    if (paid)
                    {
                        Credits++;
                    }

                    Tell(BuffBotWire.OutOfReach);
                });
                return;
            }

            After(first, () => Tell(BuffBotWire.Begins(profile)));
            var at = first;
            if (Rest > 0)
            {
                After(first, () => Tell(BuffBotWire.Rest(Rest)));
                at += Rest;
            }

            foreach (var line in Casts?.Invoke(_heard) ?? profile.Lines)
            {
                at += CastEvery;
                After(at, () => Enchant(line));
            }

            DoneDue = Now + at + 1;
            After(at + 1, () =>
            {
                DoneAt = Now;
                Tell(BuffBotWire.Done(profile));
            });
        }

        public (IStationRun Run, ScenarioStatus Status, Match Fields) Run(BuffBotStation? station = null, double maxSeconds = 600)
        {
            var (run, status) = T.RunStation(station ?? new BuffBotStation(), T.Context(f => f.BuffBotProfile = Profile ?? f.BuffBotProfile), maxSeconds, perFrame: Frame);
            var fields = Verdict().Match(run.VerdictFields());
            Assert.True(fields.Success, run.VerdictFields());
            return (run, status, fields);
        }

        public string[] LandedLines() => [.. Log.Where(l => l.Contains("] landed ", StringComparison.Ordinal))];
    }

    private static int Int(Match fields, string name) => int.Parse(fields.Groups[name].Value, CultureInfo.InvariantCulture);

    private static (int Lines, int Landed, int Told, int Recoveries, string Ok) Counts(Match f) => (Int(f, "lines"), Int(f, "landed"), Int(f, "told"), Int(f, "recoveries"), f.Groups["ok"].Value);

    // ------------------------------------------------------------------------------------------------ the station as the catalog makes it

    [Fact]
    public void The_station_is_the_catalogs_buff_bot_at_Holtburg_and_its_word_is_the_runs_profiles_unless_named()
    {
        // No word named: the word is the key of the run's profile, 'all' on a run that names none (the owner, 2026-11-12) and 'tusker' on one whose option names it.
        var station = new BuffBotStation();
        Assert.Equal(("buff-bot", (string?)null), (station.Name, station.Word));
        Assert.Equal("all", station.WordFor(new JourneyFacts()));
        Assert.Equal("tusker", station.WordFor(new JourneyFacts { BuffBotProfile = Tusker }));
        Assert.Same(BuffBotWire.All, new JourneyFacts().BuffBotProfile);
        Assert.Equal(StationWhere.Holtburg, station.Needs.Where);
        Assert.Null(new BuffBotStation("  ").Word);
        Assert.Equal("tuskers", new BuffBotStation(" tuskers ").Word);
        Assert.Equal("tuskers", new BuffBotStation(" tuskers ").WordFor(new JourneyFacts())); // a spec's word is told as given, whatever the run's profile
        Assert.Equal((BuffBotStation.StationName, StationKinds.BuffBot), ("buff-bot", "buff-bot"));
        Assert.Equal((15d, 10d), (BuffBotStation.FindSeconds, BuffBotStation.TellSeconds));

        // The catalog makes it from the autopilot's spec, and a spec's word argument names the word.
        var catalog = JourneyStations.Create(JourneyPlans.Mage, [], null);
        var made = Assert.IsType<BuffBotStation>(catalog.Create(AutopilotScenario.BuffBotSpec));
        Assert.Null(made.Word); // the autopilot's spec names no word: the run's profile decides
        var named = AutopilotScenario.BuffBotSpec with { Args = new Dictionary<string, string> { [BuffBotStation.WordArgument] = "tuskers" } };
        Assert.Equal("tuskers", Assert.IsType<BuffBotStation>(catalog.Create(named)).Word);
        Assert.Equal((BuffBotStation.StationName, StationKinds.BuffBot, StationNeeds.None), (AutopilotScenario.BuffBotSpec.Name, AutopilotScenario.BuffBotSpec.Kind, AutopilotScenario.BuffBotSpec.NeedsOverride));
    }

    // ------------------------------------------------------------------------------------------------ (a) the happy path

    [Fact]
    public void The_bot_in_view_is_walked_to_told_the_word_and_every_line_is_read_as_it_lands()
    {
        var bot = new Bot();
        var started = bot.Now;
        var (run, status, f) = bot.Run();

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Equal((N, N, 1, 0, "yes"), Counts(f));
        Assert.Equal("tusker", f.Groups["word"].Value);
        Assert.False(f.Groups["why"].Success);
        Assert.InRange(Int(f, "min"), (int)BuffBotRule.FreshSeconds, 3600); // the line cast first has most of its hour when the last one lands
        Assert.InRange(Int(f, "s"), N * 4, (N * 4) + 10); // a cast a line four seconds apart, read as they land: not the whole answer's allowance
        Assert.True(Int(f, "s") < BuffBotRule.AnswerSeconds(Tusker) / 2);
        Assert.Equal(bot.Now - started, Int(f, "s"), 1.0);

        // The walk: one request through the travel seam, to the bot as an object goal, five metres of it; no place.
        var request = Assert.Single(bot.T.Travel.Requests);
        Assert.Equal((TravelPurpose.Vendor, BuffBotWire.Guid, BuffBotWire.ReachMetres), (request.Purpose, request.ObjectGuid, request.Range));
        Assert.Equal(J3bRig.AcademyCell, request.Destination.Location.Position.CellId);
        Assert.Equal(new System.Numerics.Vector3(14, 26, 30), request.Destination.Location.Position.Local);
        Assert.Equal(0, bot.T.Places);

        // The word: one direct talk to the bot's id, as automation, sent after the walk.
        var (_, text) = Assert.Single(bot.Heard);
        Assert.Equal("tusker", text);
        var entry = Enumerable.Range(0, bot.T.View.ActionLog.Count).Select(i => bot.T.View.ActionLog[i]).Single(e => e.Kind == ActionKind.TalkDirect);
        Assert.Equal((ActionSource.Automation, ActionOutcomeKind.Confirmed), (entry.Source, entry.Outcome.Kind));
        var lines = bot.Log.ToList();
        Assert.True(lines.FindIndex(l => l.Contains("travel to the buff bot: arrived", StringComparison.Ordinal)) < lines.FindIndex(l => l.Contains("told 'tusker': the server echoed it", StringComparison.Ordinal)));

        // Check before and after, one line each; one "landed n of N" line a line, in the order cast, each naming the bot's spell and the line it stands over.
        Assert.Single(lines, l => l.Contains($"check before: Koji the Enchanter stands in cell 0x860201AD (14, 26); the profile 'tusker' has {N} lines, 0 of them up, the least 0 s left", StringComparison.Ordinal));
        Assert.Single(lines, l => l.Contains($"check after: {N} of {N} lines are up with at least 3300 s left, the least ", StringComparison.Ordinal) && !l.Contains("missing", StringComparison.Ordinal));
        var landed = bot.LandedLines();
        Assert.Equal(N, landed.Length);
        for (var i = 0; i < N; i++)
        {
            var line = Tusker.Lines[i];
            Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"landed {i + 1} of {N}: {line.Spell} ({line.SpellId}) over {line.Line}, 3600 s left"), landed[i], StringComparison.Ordinal);
        }

        Assert.Equal(Enumerable.Range(0, N + 1), bot.LandedOverTime.Select(l => l.Landed)); // one at a time, never a jump
        Assert.DoesNotContain(lines, l => l.Contains("recovery=", StringComparison.Ordinal));
        Assert.All(Tusker.Lines, l => Assert.Contains(bot.T.R.H.State.Self.Qualities.ActiveEnchantments, e => e.SpellId == l.SpellId));
    }

    [Fact]
    public void A_bot_that_comes_into_view_within_the_find_time_is_found()
    {
        // The mod places the bot within two seconds of her arrival, and its description follows: the station looks for 15 s.
        var bot = new Bot(inView: false);
        bot.After(13, bot.Place);
        var (run, status, f) = bot.Run();

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Equal((N, N, 1, 0, "yes"), Counts(f));
        Assert.InRange(Assert.Single(bot.Heard).At, 13, 15);
    }

    [Fact]
    public void Another_word_of_the_profile_is_told_as_given()
    {
        var bot = new Bot();
        var (run, status, f) = bot.Run(new BuffBotStation("Tuskers"));

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Equal("Tuskers", Assert.Single(bot.Heard).Text);
        Assert.Equal("Tuskers", f.Groups["word"].Value);
        Assert.Equal((N, N, 1, 0, "yes"), Counts(f));
    }

    // ------------------------------------------------------------------------------------------------ (b) the bot absent, a word it does not know, a walk that fails

    [Fact]
    public void A_bot_that_is_not_in_view_fails_absent_after_the_find_time_and_nothing_is_told()
    {
        var bot = new Bot(inView: false);
        var (run, status, f) = bot.Run();

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.False(run.Ok);
        Assert.StartsWith("find: buff-bot: absent: Koji the Enchanter (0x7A9B4F14) is not in view within 15 s of cell 0x", run.Failure, StringComparison.Ordinal);
        Assert.Contains("LIFESTONE_BUFF_BOT=off keeps it out", run.Failure, StringComparison.Ordinal);
        Assert.Equal((N, 0, 0, 0, "no"), Counts(f));
        Assert.Equal(15, Int(f, "s")); // the station's own find time (BuffBotStation.FindSeconds), not a travel leg's
        Assert.StartsWith("find:_buff-bot:_absent:", f.Groups["why"].Value, StringComparison.Ordinal);
        Assert.Empty(bot.Heard);
        Assert.Empty(bot.T.Travel.Requests);
    }

    [Fact]
    public void A_word_the_bot_does_not_know_fails_no_profile_before_anything_is_done()
    {
        var bot = new Bot();
        var (run, status, f) = bot.Run(new BuffBotStation("olthoi"));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("buff-bot: no-profile: the bot knows no word 'olthoi' (its words: tusker, tuskers, all, wasp, wasps, phyntos; a profile by ground is TODO(m6-buffbot-profiles))", run.Failure, StringComparison.Ordinal);
        Assert.Equal((0, 0, 0, 0, "no"), Counts(f));
        Assert.Empty(bot.Heard);
        Assert.Empty(bot.T.Travel.Requests);
    }

    [Fact]
    public void A_walk_that_fails_fails_the_stop_by_the_walks_reason_and_nothing_is_told()
    {
        var bot = new Bot();
        bot.T.Travel.FailWith = "no way to the hilltop";
        var (run, status, f) = bot.Run();

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal("walk: travel to the buff bot failed: no way to the hilltop", run.Failure);
        Assert.Equal((N, 0, 0, 0, "no"), Counts(f));
        Assert.Empty(bot.Heard);
    }

    // ------------------------------------------------------------------------------------------------ (c) the talk not echoed

    [Fact]
    public void A_talk_the_server_does_not_echo_fails_tell_refused_at_the_actions_own_limit()
    {
        var bot = new Bot { Echo = false };
        var (run, status, f) = bot.Run();

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal("tell: buff-bot: tell-refused: the word 'tusker' told to 0x7A9B4F14 was not echoed by the server (TimedOut: no confirmation within 5 s)", run.Failure);
        Assert.Equal((N, 0, 1, 0, "no"), Counts(f));
        Assert.Single(bot.Heard); // the refusal is no reason to tell again
        Assert.DoesNotContain(bot.Log, l => l.Contains("recovery=", StringComparison.Ordinal));
    }

    [Fact]
    public void A_talk_the_server_refuses_as_no_creature_fails_tell_refused_by_ACEs_name()
    {
        // ACE answers a direct talk to an id that is no creature on the landblock with CharacterNotAvailable (GameActionTalkDirect.cs 21-27).
        var bot = new Bot { Refusal = 0x052B };
        var (run, status, f) = bot.Run();

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.StartsWith("tell: buff-bot: tell-refused: the word 'tusker' told to 0x7A9B4F14 was not echoed by the server (ServerError (code 0x52B): no creature with the id 0x7A9B4F14 is on the landblock", run.Failure, StringComparison.Ordinal);
        Assert.Equal((N, 0, 1, 0, "no"), Counts(f));
        Assert.Single(bot.Heard);
    }

    // ------------------------------------------------------------------------------------------------ (d) the recovery: the word told again

    [Fact]
    public void Lines_that_do_not_land_at_the_first_answer_land_after_the_word_is_told_again()
    {
        // The first answer casts ten lines and says it is done; the second, as the bot's every answer, casts the whole profile.
        var bot = new Bot { Casts = n => n == 1 ? Tusker.Lines.Take(10) : Tusker.Lines };
        var (run, status, f) = bot.Run();

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Equal((N, N, 2, 1, "yes"), Counts(f));
        Assert.Equal(["tusker", "tusker"], bot.Heard.Select(h => h.Text));
        Assert.Single(bot.Log, l => l.Contains($"recovery=tell-again: 10 of {N} lines landed within {Allowance} s of the word; the word went unanswered, so it is told once more (a free round)", StringComparison.Ordinal));
        Assert.Equal(N, bot.LandedLines().Length); // a line renewed by the second answer is not logged twice
        Assert.InRange(Int(f, "min"), (int)BuffBotRule.FreshSeconds, 3600);

        // The bot said it was done after ten casts (42 s): the station gives the last cast its own time (8 s), not the whole answer's allowance, before it tells again.
        Assert.InRange(bot.Heard[1].At - bot.Heard[0].At, 42 + BuffBotStation.CastSeconds, 42 + BuffBotStation.CastSeconds + 2);
        Assert.Single(bot.Log, l => l.Contains($"check after: {N} of {N} lines are up", StringComparison.Ordinal));
    }

    [Fact]
    public void A_talk_the_bot_drops_is_told_again_after_the_answers_time_and_the_second_answer_lands()
    {
        // The confirmation line comes (ACE sends it before the creature hears), and the bot answers nothing: no tell, no cast.
        var bot = new Bot();
        bot.Dropped.Add(1);
        var (run, status, f) = bot.Run();

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Equal((N, N, 2, 1, "yes"), Counts(f));
        Assert.Single(bot.Log, l => l.Contains($"recovery=tell-again: 0 of {N} lines landed within {Allowance} s", StringComparison.Ordinal));

        // The wait is the answer's allowance (a cast of 8 s a line and the margin of 30 s) and one cast's time more (8 s), from the echo.
        var answer = BuffBotRule.AnswerSeconds(Tusker) + BuffBotStation.CastSeconds;
        Assert.Equal((N * 8) + 30 + 8, answer);
        Assert.InRange(bot.Heard[1].At - bot.Heard[0].At, answer, answer + 2);
    }

    // ------------------------------------------------------------------------------------------------ (e) lines still missing after the second tell

    [Fact]
    public void Lines_still_missing_after_the_second_tell_fail_lines_missing_and_name_them()
    {
        var bot = new Bot { Casts = _ => Tusker.Lines.Where(l => l.Line is not ("Armor Self" or "Mana Renewal Self")) };
        var (run, status, f) = bot.Run();

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.False(run.Ok);
        Assert.Equal($"buffs: buff-bot: lines-missing: {N - 2} of {N} lines landed after 2 tell(s): Armor Self, Mana Renewal Self", run.Failure);
        Assert.Equal((N, N - 2, 2, 1, "no"), Counts(f));
        Assert.Equal(0, Int(f, "min")); // a line is down
        Assert.Equal($"buffs:_buff-bot:_lines-missing:_{N - 2}_of_{N}_lines_landed_after_2_tell(s):_Armor_Self,_Mana_Renewal_Self", f.Groups["why"].Value);
        Assert.Equal(2, bot.Heard.Count); // one recovery, never a third tell
        Assert.Single(bot.Log, l => l.Contains($"recovery=tell-again: {N - 2} of {N} lines landed", StringComparison.Ordinal));
        Assert.Single(bot.Log, l => l.Contains($"check after: {N - 2} of {N} lines are up with at least 3300 s left, the least 0 s; missing: Armor Self, Mana Renewal Self", StringComparison.Ordinal));
        Assert.Equal(N - 2, bot.LandedLines().Length);
    }

    [Fact]
    public void A_bot_that_answers_nothing_twice_fails_lines_missing_with_every_line_named()
    {
        var bot = new Bot();
        bot.Dropped.Add(1);
        bot.Dropped.Add(2);
        var (run, status, f) = bot.Run();

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.StartsWith($"buffs: buff-bot: lines-missing: 0 of {N} lines landed after 2 tell(s): Focus Self, Willpower Self, ", run.Failure, StringComparison.Ordinal);
        Assert.EndsWith(", " + Tusker.Lines[^1].Line, run.Failure, StringComparison.Ordinal);
        Assert.All(Tusker.Lines, l => Assert.Contains(l.Line, run.Failure, StringComparison.Ordinal));
        Assert.Equal((N, 0, 2, 1, "no"), Counts(f));
        var twice = 2 * (int)(BuffBotRule.AnswerSeconds(Tusker) + BuffBotStation.CastSeconds);
        Assert.InRange(Int(f, "s"), twice, twice + 8); // two answers' allowances (each with one cast's time more) and the echoes: the stop's longest
    }

    // ------------------------------------------------------------------------------------------------ (f) a line already up but stale

    [Fact]
    public void A_line_already_up_but_stale_counts_as_landed_only_once_the_bot_renews_it()
    {
        // The bot's Focus stands from an earlier visit with 3,200 s left (under the 3,300 of a line just cast). This answer casts it last.
        var bot = new Bot { Casts = _ => Tusker.Lines.Skip(1).Append(Tusker.Lines[0]) };
        bot.Enchant(Tusker.Lines[0]);
        bot.T.R.Tick(400);
        var (run, status, f) = bot.Run();

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Equal((N, N, 1, 0, "yes"), Counts(f));
        Assert.Single(bot.Log, l => l.Contains($"the profile 'tusker' has {N} lines, 1 of them up, the least 0 s left", StringComparison.Ordinal));

        // Up all along, it is not read as landed before its own cast: the count climbs one a cast and Focus is the last.
        Assert.Equal(Enumerable.Range(0, N + 1), bot.LandedOverTime.Select(l => l.Landed));
        var landed = bot.LandedLines();
        Assert.Contains($"landed 1 of {N}: Bolstered Will (2090) over Willpower Self", landed[0], StringComparison.Ordinal);
        Assert.Contains($"landed {N} of {N}: Calming Gaze (2066) over Focus Self, 3600 s left", landed[^1], StringComparison.Ordinal);
        var heard = bot.Heard[0].At;
        Assert.InRange(bot.LandedOverTime[^1].At - heard, N * 4, (N * 4) + 3);
    }

    [Fact]
    public void A_stale_line_the_bot_never_renews_is_missing_by_name_though_it_is_up()
    {
        var bot = new Bot { Casts = _ => Tusker.Lines.Skip(1) };
        bot.Enchant(Tusker.Lines[0]);
        bot.T.R.Tick(400);
        var (run, status, f) = bot.Run();

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal($"buffs: buff-bot: lines-missing: {N - 1} of {N} lines landed after 2 tell(s): Focus Self", run.Failure);
        Assert.Equal((N, N - 1, 2, 1, "no"), Counts(f));
        Assert.InRange(Int(f, "min"), 2900, 3200); // it is up, with what the earlier visit left of it
    }

    // ------------------------------------------------------------------------------------------------ (g) the word: 'all' unless the run names another (the owner, 2026-11-12)

    [Fact]
    public void On_a_run_that_names_no_word_she_tells_all_and_the_stop_is_done_when_its_28_lines_are_up()
    {
        var all = BuffBotWire.All;
        var bot = new Bot { Profile = null }; // the facts' own default: no option named a word
        var (run, status, f) = bot.Run();

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Equal("all", Assert.Single(bot.Heard).Text);
        Assert.Equal("all", f.Groups["word"].Value);
        Assert.Equal((28, 28, 1, 0, "yes"), Counts(f));
        Assert.InRange(Int(f, "min"), (int)BuffBotRule.FreshSeconds, 3600); // the line cast first is still fresh when the 28th lands

        // The log lines that show the word told and the lines landed.
        var lines = bot.Log.ToList();
        Assert.Single(lines, l => l.Contains("check before: Koji the Enchanter stands in cell 0x860201AD (14, 26); the profile 'all' has 28 lines, 0 of them up, the least 0 s left", StringComparison.Ordinal));
        Assert.Single(lines, l => l.Contains("told 'all': the server echoed it; the bot's answer is read line by line", StringComparison.Ordinal));
        Assert.Single(lines, l => l.Contains("check after: 28 of 28 lines are up with at least 3300 s left, the least ", StringComparison.Ordinal) && !l.Contains("missing", StringComparison.Ordinal));
        var landed = bot.LandedLines();
        Assert.Equal(28, landed.Length);
        for (var i = 0; i < all.Lines.Count; i++)
        {
            Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"landed {i + 1} of 28: {all.Lines[i].Spell} ({all.Lines[i].SpellId}) over {all.Lines[i].Line}, 3600 s left"), landed[i], StringComparison.Ordinal);
        }

        Assert.Contains("landed 21 of 28: Boon of the Arrow Turner (2160) over Piercing Protection Self", landed[20], StringComparison.Ordinal);
        Assert.Contains("landed 25 of 28: Storm's Boon (2158) over Lightning Protection Self", landed[24], StringComparison.Ordinal);
        Assert.All(all.Lines, l => Assert.Contains(bot.T.R.H.State.Self.Qualities.ActiveEnchantments, e => e.SpellId == l.SpellId));
    }

    [Fact]
    public void Told_all_a_bot_that_casts_the_tuskers_lines_alone_leaves_the_stop_failed_with_the_nine_missing_by_name()
    {
        // The stop is judged by the profile asked for: nineteen lines landed is not done when the word was 'all' (a mod of before the second profile would answer 'all' with its help line and cast nothing; this bot casts the old nineteen).
        var bot = new Bot { Profile = null, Casts = _ => Tusker.Lines };
        var (run, status, f) = bot.Run();

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.False(run.Ok);
        Assert.Equal("buffs: buff-bot: lines-missing: 19 of 28 lines landed after 2 tell(s): Blade Protection Self, Piercing Protection Self, Fire Protection Self, Cold Protection Self, Acid Protection Self, Lightning Protection Self, Magic Resistance Self, Impregnability Self, Arcane Enlightenment Self", run.Failure);
        Assert.Equal((28, 19, 2, 1, "no"), Counts(f));
        Assert.Equal(["all", "all"], bot.Heard.Select(h => h.Text));
    }

    [Fact]
    public void A_bot_that_answers_all_with_its_help_line_fails_the_stop_word_unknown_at_once_and_is_not_told_again()
    {
        // A server mod installed before the second profile: it knows the tuskers' words, answers any other text with its help line, and casts nothing.
        var bot = new Bot { Profile = null, Knows = [Tusker] };
        var (run, status, f) = bot.Run();

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.False(run.Ok);
        Assert.StartsWith("buffs: buff-bot: word-unknown: the bot answered the word 'all' with its help line and casts nothing: the server mod installed does not know the word", run.Failure, StringComparison.Ordinal);
        Assert.Contains("--autopilot-buff-bot-word", run.Failure, StringComparison.Ordinal);
        Assert.Equal((28, 0, 1, 0, "no"), Counts(f));
        Assert.Single(bot.Heard); // no second tell: it would wait the whole answer's time (254 s) for a bot that will not cast
        Assert.InRange(Int(f, "s"), 0, 10);
        Assert.DoesNotContain(bot.Log, l => l.Contains("recovery=", StringComparison.Ordinal));

        // The same bot told a word it knows casts as before.
        var known = new Bot { Profile = Tusker, Knows = [Tusker] };
        var (again, ok, g) = known.Run();
        Assert.True(ok == ScenarioStatus.Succeeded && again.Ok, again.Failure);
        Assert.Equal((19, 19, 1, 0, "yes"), Counts(g));
    }

    [Fact]
    public void On_a_run_whose_option_names_tusker_she_tells_tusker_and_its_19_lines_are_the_whole_stop()
    {
        // --autopilot-buff-bot-word=tusker: the run's profile is the tuskers', and a station with no word of its own tells its key.
        var bot = new Bot { Profile = Tusker };
        var (run, status, f) = bot.Run();

        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
        Assert.Equal("tusker", Assert.Single(bot.Heard).Text);
        Assert.Equal((19, 19, 1, 0, "yes"), Counts(f));
        Assert.DoesNotContain(bot.T.R.H.State.Self.Qualities.ActiveEnchantments, e => e.SpellId == 2160); // no Piercing Protection: not a line of the tuskers' profile
    }
}
