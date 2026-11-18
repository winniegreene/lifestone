using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.World;
using Lifestone.Data.World;
using Lifestone.ServerTools;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>What <see cref="BuffBotRule"/> reads of one line of a profile: the bot's spell on the player, and the seconds it has left.</summary>
/// <param name="Line">The profile's line.</param>
/// <param name="Up">The bot's spell is in the player's enchantments.</param>
/// <param name="RemainingSeconds">The seconds it has left (0 when not up).</param>
public readonly record struct BuffBotLineRead(BuffBotLine Line, bool Up, double RemainingSeconds);

/// <summary>
/// M6 item 14 (m6-plan.md section 3.1, D124-D127): the rules of the stop at the buff bot with no session in them. A line is the bot's when the bot's own spell (<see cref="BuffBotWire"/>: the Other spell of the line at level VII)
/// is in the player's enchantments; the bot's spells last an hour.
/// </summary>
public static class BuffBotRule
{
    /// <summary>
    /// The seconds a bot's line must have left for the stop to count it as just cast (of the spell's 3600). The tuskers' 19 casts took 28 s from the word live (2026-11-12 00:49 UTC: 2 s to the first, 1.5 s a cast); the 28 of
    /// <c>all</c> are about 42 s at that pace (not yet seen live), and a whole answer's allowance (<see cref="AnswerSeconds"/>: 254 s for 28 lines) still leaves the first line over this.
    /// </summary>
    public const double FreshSeconds = 3300;

    /// <summary>
    /// The seconds under which a line at home sends her to the bot again: 45 minutes. Whenever she is home with a line under it, the visit renews the hour.
    /// </summary>
    public const double RenewUnderSeconds = 2700;

    /// <summary>
    /// The seconds under which a line in the field sends her home for the bot: 7 minutes, the lull a trip waits for and the recall. Live 2026-11-11 18:42 UTC, the Cave: no supply trip came for an hour (120 potions in the
    /// pack), the bot's lines lapsed in the field, her own level IV pass took their place in a ground she had entered on the bot's level VII, and she died ten minutes later. No trip "comes anyway": this one is asked.
    /// </summary>
    public const double TripUnderSeconds = 420;

    /// <summary>
    /// The seconds under which the trip for the bot no longer waits for a lull: 4 minutes. Live 2026-11-12 11:01 to 11:09 UTC, the Cave at 258 kills an hour: the trip was due and waited seven and a half minutes for a
    /// pause that never came, the bot's nineteen lines lapsed at 11:08, the hits went from 1 to 8 up to 14, 16 and a critical 25, and she died at 11:09:13. Under this she leaves by Lifestone Recall with the tuskers on her,
    /// while the bot's armour still stands and a recall's few seconds cost her nothing.
    /// </summary>
    public const double TripUrgentUnderSeconds = 240;

    /// <summary>The lines of <paramref name="profile"/> as the registry holds them at <paramref name="now"/>.</summary>
    /// <param name="profile">The profile.</param>
    /// <param name="qualities">The player's qualities.</param>
    /// <param name="now">The session clock.</param>
    /// <param name="reads">The list to fill (cleared first).</param>
    public static void Read(BuffBotProfile profile, PlayerQualities qualities, double now, List<BuffBotLineRead> reads)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(qualities);
        ArgumentNullException.ThrowIfNull(reads);
        reads.Clear();
        foreach (var line in profile.Lines)
        {
            var left = -1.0;
            foreach (var entry in qualities.ActiveEnchantments)
            {
                if (entry.SpellId == line.SpellId)
                {
                    left = Math.Max(left, entry.RemainingSeconds(now));
                }
            }

            reads.Add(new BuffBotLineRead(line, left >= 0, Math.Max(0, left)));
        }
    }

    /// <summary>The lines of <paramref name="reads"/> with at least <paramref name="seconds"/> left.</summary>
    /// <param name="reads">The lines read.</param>
    /// <param name="seconds">The seconds a line must have left.</param>
    public static int CountWith(IReadOnlyList<BuffBotLineRead> reads, double seconds)
    {
        var count = 0;
        foreach (var read in reads)
        {
            if (read.Up && read.RemainingSeconds >= seconds)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The fewest seconds any line has left (0 when a line is not up).</summary>
    /// <param name="reads">The lines read.</param>
    public static double MinRemaining(IReadOnlyList<BuffBotLineRead> reads)
    {
        var least = double.PositiveInfinity;
        foreach (var read in reads)
        {
            least = Math.Min(least, read.Up ? read.RemainingSeconds : 0);
        }

        return reads.Count == 0 ? 0 : least;
    }

    /// <summary>True when the stop at the bot is due at home: a line of the profile is not up, or has under <see cref="RenewUnderSeconds"/> left.</summary>
    /// <param name="reads">The lines read.</param>
    public static bool Due(IReadOnlyList<BuffBotLineRead> reads) => reads.Count > 0 && MinRemaining(reads) < RenewUnderSeconds;

    /// <summary>The seconds one answer of <paramref name="profile"/> may take before the stop reads what landed: each cast's wind-up and follow-through with room to spare, and a margin.</summary>
    /// <param name="profile">The profile.</param>
    public static double AnswerSeconds(BuffBotProfile profile) => (profile?.Lines.Count ?? 0) * BuffBotStation.CastSeconds + BuffBotStation.MarginSeconds;

    /// <summary>D138: true when a character of <paramref name="level"/> pays the bot's fee (a credit a round from <see cref="BuffBotWire.FeeLevel"/>); under it a round is free and no credit is asked for.</summary>
    /// <param name="level">Her level.</param>
    public static bool Pays(int level) => level >= BuffBotWire.FeeLevel;

    /// <summary>The metres from its post (<see cref="BuffBotWire.X"/>, <see cref="BuffBotWire.Y"/>) within which the bot stands at its post.</summary>
    public const double PostMetres = 3;

    /// <summary>
    /// D138: the seconds the stop waits for a bot away at the shop (a fallback, by name: in normal play she is done within the ten seconds the bot waits after a round before it leaves, so she never sees it away; this is
    /// for a stop that comes while it is out, about 156 m each way and a trade).
    /// </summary>
    public const double AwaySeconds = 360;

    /// <summary>
    /// D138 (the owner, 2026-11-14): the seconds the bot waits after a round's done line before it leaves for tapers, which give the customer time to finish (the mod's <c>BuffBotRules.LeaveDelaySeconds</c>). The stop ends as
    /// soon as every line is up, and its one more look after a done line with a line missing is <see cref="BuffBotStation.CastSeconds"/>, under this.
    /// </summary>
    public const double LeaveSeconds = 10;

    /// <summary>The metres from its post past which the bot, on its own landblock, is away (walking to the shop or back).</summary>
    public const double AwayMetres = 8;

    /// <summary>The most rest the stop allows the bot on one answer, in seconds (its mana from empty is about 35 minutes at its rate).</summary>
    public const double MaxRestSeconds = 2400;

    /// <summary>The metres from the bot's place to its post, in the landblock's plane.</summary>
    /// <param name="at">The bot's place.</param>
    public static double PostOff(CellFrame at) => Math.Sqrt(Math.Pow(at.Origin.X - BuffBotWire.X, 2) + Math.Pow(at.Origin.Y - BuffBotWire.Y, 2));

    /// <summary>True when the bot's place <paramref name="at"/> is its post (Holtburg's landblock, within <see cref="PostMetres"/>).</summary>
    /// <param name="at">The bot's place.</param>
    public static bool AtPost(CellFrame at) => at.CellId >> 16 == BuffBotWire.Landblock && PostOff(at) <= PostMetres;

    /// <summary>True when the bot stands on its own landblock more than <see cref="AwayMetres"/> from its post (a bot elsewhere, as a test's, is judged by its words alone).</summary>
    /// <param name="at">The bot's place.</param>
    public static bool Away(CellFrame at) => at.CellId >> 16 == BuffBotWire.Landblock && PostOff(at) > AwayMetres;

    /// <summary>The bot's lines since <paramref name="mark"/> (<see cref="ChatLog.Total"/> before the act): its tells to her and its says, by its id or its name.</summary>
    /// <param name="chat">The chat.</param>
    /// <param name="mark">The mark.</param>
    public static IEnumerable<string> BotSaid(ChatLog chat, long mark) =>
        QuestReads.Since(chat, mark)
            .Where(l => (l.Kind is ChatKind.Tell or ChatKind.Speech or ChatKind.RangedSpeech) && (l.SenderGuid == BuffBotWire.Guid || string.Equals(l.Sender, BuffBotWire.Name, StringComparison.Ordinal)))
            .Select(l => l.Text ?? string.Empty);

    /// <summary>The last credit count the bot told since <paramref name="mark"/> (<see cref="BuffBotWire.Credits"/>), or null.</summary>
    /// <param name="chat">The chat.</param>
    /// <param name="mark">The mark.</param>
    public static int? CreditsSince(ChatLog chat, long mark)
    {
        int? last = null;
        foreach (var text in BotSaid(chat, mark))
        {
            if (BuffBotWire.TryReadCredits(text, out var n))
            {
                last = n;
            }
        }

        return last;
    }

    /// <summary>The seconds of rest the bot told since <paramref name="mark"/> (<see cref="BuffBotWire.Rest"/>), summed.</summary>
    /// <param name="chat">The chat.</param>
    /// <param name="mark">The mark.</param>
    public static int RestSince(ChatLog chat, long mark) => BotSaid(chat, mark).Sum(t => BuffBotWire.TryReadRest(t, out var s) ? s : 0);

    /// <summary>True when a line of the bot since <paramref name="mark"/> holds <paramref name="text"/>.</summary>
    /// <param name="chat">The chat.</param>
    /// <param name="mark">The mark.</param>
    /// <param name="text">The text.</param>
    public static bool SaidSince(ChatLog chat, long mark, string text) => BotSaid(chat, mark).Any(t => t.Contains(text, StringComparison.Ordinal));

    /// <summary>True when the bot said since <paramref name="mark"/> that it is away or leaving for tapers (<see cref="BuffBotWire.AwayAnnouncement"/>, <see cref="BuffBotWire.AwayTell"/>) and not yet that it is back.</summary>
    /// <param name="chat">The chat.</param>
    /// <param name="mark">The mark.</param>
    public static bool AwaySince(ChatLog chat, long mark)
    {
        var away = false;
        foreach (var text in BotSaid(chat, mark))
        {
            if (text.Contains(BuffBotWire.AwayAnnouncement, StringComparison.Ordinal) || text.Contains(BuffBotWire.AwayTell, StringComparison.Ordinal))
            {
                away = true;
            }
            else if (text.Contains(BuffBotWire.BackAnnouncement, StringComparison.Ordinal))
            {
                away = false;
            }
        }

        return away;
    }
}

/// <summary>
/// <c>buff-bot</c> (M6 item 14, slice S2; m6-plan.md section 3.1, D124-D127): the stop at the buff bot by Holtburg's lifestone. Check, act, check (M5 D141):
/// <list type="number">
/// <item>before: the bot stands in view (<see cref="BuffBotWire.Guid"/>; the mod places it within two seconds of her arrival in Holtburg); the profile's lines as her enchantments hold them, one line;</item>
/// <item>the walk to within <see cref="BuffBotWire.ReachMetres"/> of it (the journey's travel seam, an object goal);</item>
/// <item>D138, from <see cref="BuffBotWire.FeeLevel"/> (her level as she knows it; under it a round is free and this step is skipped): the credit count, asked by <see cref="BuffBotWire.CreditWord"/> and read from the bot's
/// <see cref="BuffBotWire.Credits"/>; with none, one <see cref="BuffBotWire.FeeName"/> handed to it by the player's give (ACE takes one of a stack), its give line read and then a count of one or more. Named failures:
/// <c>no-scarab</c> (none in the pack), <c>give-refused</c> (ACE's refusal), <c>no-credit-after-give</c>, <c>credit-unread</c>;</item>
/// <item>the word, told to the bot by its id (<c>TalkDirect</c>: ACE sends a tell by name to players only), confirmed by the server's echo;</item>
/// <item>each line read as it lands (the bot's spell id in her enchantments with <see cref="BuffBotRule.FreshSeconds"/> left), until every line is up, the bot tells its last line, or the answer's time is up (lengthened by every
/// rest the bot tells, <see cref="BuffBotWire.Rest"/>); the stop ends as soon as every line is up, so she is done before the ten seconds the bot waits after a round;</item>
/// <item>after: every line of the profile up with most of its hour. One named recovery for a free round, <c>recovery=tell-again</c> (a talk that went unanswered); a paid round is never told again (a second round would take a
/// second credit). Then the station fails by name and the autopilot's own pass casts what she can (loud, never a silent wait).</item>
/// </list>
/// The profile is the word's (<see cref="BuffBotWire.ProfileOf"/>): the lines counted, the answer's time and the last line waited for are that profile's, so the stop is done only when every line of the word told is up.
/// A bot that answers the word with its help line (<see cref="BuffBotWire.HelpStart"/>: a server mod that does not know the word) fails the stop at once, <c>word-unknown</c>, with no second tell; one that answers
/// <see cref="BuffBotWire.NeedCredit"/> fails <c>no-credit</c>, one that stops short <c>bot-out-of</c>; one that stops out of reach is walked back to and told again (<c>recovery=walk-back</c>, at most
/// <see cref="ReachTries"/>), then fails <c>out-of-reach</c>.
/// <para><b>The bot away (a fallback, D138).</b> Under 100 tapers the bot leaves for the shop ten seconds after a round; in normal play she is gone by then. A stop that finds it off its post, or is told
/// <see cref="BuffBotWire.AwayTell"/>, waits up to <see cref="BuffBotRule.AwaySeconds"/> for it to stand at its post again (<c>recovery=wait-away</c>), then goes on; else it fails <c>bot-away</c>.</para>
/// The verdict: <c>bot= word= lines= landed= told= recoveries= s= min_left_s= fee= credits= gave= ok=</c>.
/// </summary>
/// <param name="word">The profile's word (null or blank: the key of the run's profile, <see cref="JourneyFacts.BuffBotProfile"/>, which is <c>all</c> unless the option names another; a profile by ground is <c>TODO(m6-buffbot-profiles)</c>).</param>
public sealed class BuffBotStation(string? word = null) : IStation
{
    /// <summary>The station's name.</summary>
    public const string StationName = "buff-bot";

    /// <summary>The spec argument naming the word.</summary>
    public const string WordArgument = "word";

    /// <summary>Seconds for the bot to be in view (the mod looks every two seconds, and the object's description follows).</summary>
    public const double FindSeconds = 15;

    /// <summary>Seconds for the server's echo of the talk.</summary>
    public const double TellSeconds = 10;

    /// <summary>The seconds one cast of the bot may take (ACE's wind-up at speed 2 and follow-through at speed 1: three to four seconds live for a level VII formula; Lifestone's allowance).</summary>
    public const double CastSeconds = 8;

    /// <summary>The margin on one answer, in seconds.</summary>
    public const double MarginSeconds = 30;

    /// <summary>
    /// M6 14 bot reach (live 2026-11-17 04:55:00, ladder52: the bot stopped the round at its twelfth line with <see cref="BuffBotWire.OutOfReach"/>; the server's mod read her 75 m off while her own client stood 4.7 m from it,
    /// so the Armor Self and Bludgeon Protection lines never landed and every gate read shut): the walks back to the bot after a round it stopped out of reach. Each is check, act, check: her distance to the bot as the
    /// client reads it, a fresh walk to within <see cref="BuffBotWire.ReachMetres"/> (it moves her, so the server reads her place anew), the credit read again on a paid round (the stopped round returned it), and the word
    /// told again; a round stopped out of reach once more after the last fails <c>out-of-reach</c> by name.
    /// </summary>
    public const int ReachTries = 2;

    /// <inheritdoc />
    public string Name => StationName;

    /// <inheritdoc />
    public StationNeeds Needs { get; } = new(StationWhere.Holtburg);

    /// <summary>The word the spec names, or null: the run's own (<see cref="WordFor"/>).</summary>
    public string? Word { get; } = string.IsNullOrWhiteSpace(word) ? null : word.Trim();

    /// <summary>The word told on a run with <paramref name="facts"/>: the spec's, else the key of the run's profile (<see cref="JourneyFacts.BuffBotProfile"/>).</summary>
    /// <param name="facts">The run's facts.</param>
    public string WordFor(JourneyFacts facts) => Word ?? (facts ?? throw new ArgumentNullException(nameof(facts))).BuffBotProfile.Key;

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new Run(context, WordFor(context.Facts));
    }

    /// <remarks>
    /// The station's own times are named in full here (<c>BuffBotStation.FindSeconds</c>, <c>BuffBotStation.CastSeconds</c>): <see cref="TravelLeg"/> has constants of the same names (12 s and 15 s), and a bare
    /// name inside this class is the base class's, not the station's.
    /// </remarks>
    private sealed class Run(JourneyContext context, string word) : TravelLeg(StationName, context)
    {
        private readonly List<BuffBotLineRead> _reads = [];
        private readonly HashSet<uint> _landed = [];
        private int _lines;
        private int _told;
        private int _recoveries;
        private double _started;
        private double _minLeft;
        private bool _ok;
        private bool _pays;
        private int? _credits;
        private int _gave;

        public override string VerdictFields() =>
            string.Create(CultureInfo.InvariantCulture, $"bot={BuffBotWire.Guid:X8} word={word} lines={_lines} landed={_landed.Count} told={_told} recoveries={_recoveries} s={(S is null ? 0 : S.Clock.Now - _started):0} min_left_s={_minLeft:0} fee={(_pays ? "paid" : "free")} credits={(_credits is { } n ? n.ToString(CultureInfo.InvariantCulture) : "-")} gave={_gave} ok={(_ok ? "yes" : "no")}")
            + (Failure is null ? string.Empty : " why=" + Failure.Replace(' ', '_'));

        protected override IEnumerable<ActionHandle?> Body()
        {
            _started = S.Clock.Now;
            var profile = BuffBotWire.ProfileOf(word);
            Require(profile is not null, $"buff-bot: no-profile: the bot knows no word '{word}' (its words: {string.Join(", ", BuffBotWire.Profiles.SelectMany(p => p.Words))}; a profile by ground is TODO(m6-buffbot-profiles))");
            _lines = profile!.Lines.Count;

            Stage = "find";
            foreach (var h in Until(() => BotAt() is not null, BuffBotStation.FindSeconds))
            {
                yield return h;
            }

            var at = BotAt();
            Require(at is not null, $"buff-bot: absent: {BuffBotWire.Name} (0x{BuffBotWire.Guid:X8}) is not in view within {BuffBotStation.FindSeconds:0} s of {Where()}: the server mod places it by Holtburg's lifestone (LIFESTONE_BUFF_BOT=off keeps it out)");
            BuffBotRule.Read(profile, S.State.Self.Qualities, S.Clock.Now, _reads);
            var level = S.State.Self.Qualities.Level;
            _pays = BuffBotRule.Pays(level);
            Log(string.Create(CultureInfo.InvariantCulture, $"check before: {BuffBotWire.Name} stands in cell 0x{at!.Value.CellId:X8} ({at.Value.Origin.X:0.#}, {at.Value.Origin.Y:0.#}); the profile '{profile.Key}' has {_lines} lines, {BuffBotRule.CountWith(_reads, 1)} of them up, the least {BuffBotRule.MinRemaining(_reads):0} s left; level {level}: {(_pays ? $"a round costs a credit (the fee from level {BuffBotWire.FeeLevel}), {Held()} {BuffBotWire.FeeName}(s) in the pack" : $"a round is free (under level {BuffBotWire.FeeLevel})")}"));

            // D138, a fallback: the bot off its post is at the shop for tapers (in normal play she is gone before it leaves).
            if (BuffBotRule.Away(at.Value))
            {
                foreach (var h in WaitAway(S.State.Chat.Total, string.Create(CultureInfo.InvariantCulture, $"the bot stands {BuffBotRule.PostOff(at.Value):0} m off its post")))
                {
                    yield return h;
                }

                at = BotAt() ?? at;
            }

            Stage = "walk";
            foreach (var h in Go(new TravelRequest(Destination("buff-bot", at!.Value.CellId, at.Value.Origin), TravelPurpose.Vendor, BuffBotWire.Guid, BuffBotWire.ReachMetres), "to the buff bot"))
            {
                yield return h;
            }

            if (_pays)
            {
                foreach (var h in Credit())
                {
                    yield return h;
                }
            }

            var awayWaits = 0;
            var reachWalks = 0;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (attempt == 1)
                {
                    _recoveries++;
                    Log(string.Create(CultureInfo.InvariantCulture, $"recovery=tell-again: {_landed.Count} of {_lines} lines landed within {BuffBotRule.AnswerSeconds(profile):0} s of the word; the word went unanswered, so it is told once more (a free round)"));
                }

                Stage = "tell";
                var mark = ChatMark;
                var said = S.State.Chat.Total;
                var talk = S.Actions.TalkDirect(BuffBotWire.Guid, word, ActionSource.Automation);
                _told++;
                foreach (var h in Until(() => talk.IsCompleted, BuffBotStation.TellSeconds))
                {
                    yield return h;
                }

                Require(talk.Outcome is { Kind: ActionOutcomeKind.Confirmed }, $"buff-bot: tell-refused: the word '{word}' told to 0x{BuffBotWire.Guid:X8} was not echoed by the server ({Describe(talk)})");
                Log($"told '{word}': the server echoed it; the bot's answer is read line by line");

                // The answer's time, lengthened by every rest the bot tells (its mana) and once by a busy line (another teller's round runs first).
                Stage = "buffs";
                var done = BuffBotWire.Done(profile);
                var told = S.Clock.Now;
                var rest = 0;
                double Allowance() => BuffBotRule.AnswerSeconds(profile) + BuffBotRule.RestSince(S.State.Chat, said) + (BuffBotRule.SaidSince(S.State.Chat, said, BuffBotWire.Busy) ? BuffBotRule.AnswerSeconds(profile) : 0);
                bool Stopped() => TextSince(mark, BuffBotWire.HelpStart) || BuffBotRule.SaidSince(S.State.Chat, said, BuffBotWire.NeedCredit) || BuffBotRule.SaidSince(S.State.Chat, said, BuffBotWire.OutOfStart)
                    || BuffBotRule.SaidSince(S.State.Chat, said, BuffBotWire.OutOfReach) || BuffBotRule.SaidSince(S.State.Chat, said, BuffBotWire.AwayTell);
                foreach (var h in Until(() => Landed(profile) == _lines || TextSince(mark, done) || Stopped() || S.Clock.Now - told >= Allowance(), BuffBotRule.AnswerSeconds(profile) + BuffBotRule.MaxRestSeconds))
                {
                    if (BuffBotRule.RestSince(S.State.Chat, said) is var r && r != rest)
                    {
                        rest = r;
                        Log(string.Create(CultureInfo.InvariantCulture, $"the bot rests for its mana: {r} s told in all; the answer's time is now {Allowance():0} s"));
                    }

                    yield return h;
                }

                // The bot answers a word it does not know with its help line and casts nothing (a server mod installed before the word was added): by name, at once, and no second tell that would wait the whole answer's time again.
                Require(!TextSince(mark, BuffBotWire.HelpStart), $"buff-bot: word-unknown: the bot answered the word '{word}' with its help line and casts nothing: the server mod installed does not know the word (the line lists the words it knows; install the mod of this build, or name one of its words with --autopilot-buff-bot-word)");
                Require(!BuffBotRule.SaidSince(S.State.Chat, said, BuffBotWire.NeedCredit), $"buff-bot: no-credit: the bot answered the word '{word}' with \"{BuffBotWire.NeedCredit}\" and casts nothing (level {level}; the count read before the word was {(_credits is { } c ? c.ToString(CultureInfo.InvariantCulture) : "not asked")})");
                Require(!BuffBotRule.SaidSince(S.State.Chat, said, BuffBotWire.OutOfStart), $"buff-bot: bot-out-of: the bot stopped the round short ({BuffBotRule.BotSaid(S.State.Chat, said).FirstOrDefault(t => t.StartsWith(BuffBotWire.OutOfStart, StringComparison.Ordinal))}); {_landed.Count} of {_lines} lines landed and the credit was returned");
                if (BuffBotRule.SaidSince(S.State.Chat, said, BuffBotWire.OutOfReach))
                {
                    // M6 14 bot reach: check, act, check: walk back to the bot (a fresh move, so the server reads her place anew), read the credit the stopped round returned, tell the word again; at most ReachTries.
                    Require(reachWalks < BuffBotStation.ReachTries, $"buff-bot: out-of-reach: the bot stopped the round short: \"{BuffBotWire.OutOfReach}\" ({_landed.Count} of {_lines} lines landed; {reachWalks} walk(s) back to it, the most is {BuffBotStation.ReachTries}: BuffBotStation.ReachTries)");
                    reachWalks++;
                    _recoveries++;
                    var bot = BotAt();
                    Require(bot is not null, $"buff-bot: out-of-reach: the bot stopped the round short and is no longer in view ({_landed.Count} of {_lines} lines landed)");
                    Log(string.Create(CultureInfo.InvariantCulture, $"recovery=walk-back {reachWalks} of {BuffBotStation.ReachTries}: check before: the bot stopped the round with \"{BuffBotWire.OutOfReach}\" after {Landed(profile)} of {_lines} lines; her client reads her {BotMetres(bot!.Value)} from it (the bot's reach rule is the server's: its spell's range from where the server reads her); act: a walk to within {BuffBotWire.ReachMetres:0} m, {(_pays ? "her credit read again, " : string.Empty)}the word told again"));
                    Stage = "walk";
                    foreach (var h in Go(new TravelRequest(Destination("buff-bot", bot.Value.CellId, bot.Value.Origin), TravelPurpose.Vendor, BuffBotWire.Guid, BuffBotWire.ReachMetres), "back to the buff bot"))
                    {
                        yield return h;
                    }

                    Log(string.Create(CultureInfo.InvariantCulture, $"recovery=walk-back {reachWalks} of {BuffBotStation.ReachTries}: check after: her client reads her {(BotAt() is { } now ? BotMetres(now) : "unseen")} from the bot"));
                    if (_pays)
                    {
                        foreach (var h in Credit())
                        {
                            yield return h;
                        }
                    }

                    attempt--; // the walk back's tell is its own, not the free round's tell-again
                    continue;
                }

                if (BuffBotRule.SaidSince(S.State.Chat, said, BuffBotWire.AwayTell) && Landed(profile) < _lines)
                {
                    // The bot was leaving for tapers when the word came (a fallback): it keeps the word and answers it when it is back.
                    Require(awayWaits++ == 0, $"buff-bot: bot-away: the bot was away for tapers a second time when told '{word}'");
                    foreach (var h in WaitAway(said, "the bot answered the word with \"" + BuffBotWire.AwayTell + "\""))
                    {
                        yield return h;
                    }

                    var back = S.Clock.Now;
                    foreach (var h in Until(() => Landed(profile) == _lines || TextSince(mark, done) || S.Clock.Now - back >= Allowance(), BuffBotRule.AnswerSeconds(profile) + BuffBotRule.MaxRestSeconds))
                    {
                        yield return h;
                    }
                }

                if (Landed(profile) == _lines)
                {
                    break; // done at once: she is on her way within the ten seconds the bot waits after a round
                }

                // The bot said it was done, or the time is up, with a line missing: one more look after the last cast's own time (within the bot's ten seconds), then the recovery or the failure.
                foreach (var h in Until(() => Landed(profile) == _lines, BuffBotStation.CastSeconds))
                {
                    yield return h;
                }

                if (Landed(profile) == _lines)
                {
                    break;
                }

                if (_pays)
                {
                    Log("no second tell on a paid round: a second word would take a second credit; the lines missing are her own pass's");
                    break;
                }
            }

            BuffBotRule.Read(profile, S.State.Self.Qualities, S.Clock.Now, _reads);
            _minLeft = BuffBotRule.MinRemaining(_reads);
            var fresh = BuffBotRule.CountWith(_reads, BuffBotRule.FreshSeconds);
            var missing = string.Join(", ", _reads.Where(r => !r.Up || r.RemainingSeconds < BuffBotRule.FreshSeconds).Select(r => r.Line.Line));
            Log(string.Create(CultureInfo.InvariantCulture, $"check after: {fresh} of {_lines} lines are up with at least {BuffBotRule.FreshSeconds:0} s left, the least {_minLeft:0} s{(missing.Length == 0 ? string.Empty : "; missing: " + missing)}"));
            Require(fresh == _lines, $"buff-bot: lines-missing: {fresh} of {_lines} lines landed after {_told} tell(s): {missing}");
            _ok = true;
        }

        /// <summary>
        /// D138, check, act, check: the credit count asked (<see cref="BuffBotWire.CreditWord"/>) and read; with none, one <see cref="BuffBotWire.FeeName"/> given by the player's give, ACE's give line read (a refusal by its
        /// name), then a count of one or more, and the pack one scarab lighter.
        /// </summary>
        private IEnumerable<ActionHandle?> Credit()
        {
            var c = CultureInfo.InvariantCulture;
            Stage = "credit";
            for (var ask = 0; ask < 2; ask++)
            {
                var asked = S.State.Chat.Total;
                var talk = S.Actions.TalkDirect(BuffBotWire.Guid, BuffBotWire.CreditWord, ActionSource.Automation);
                foreach (var h in Until(() => talk.IsCompleted, BuffBotStation.TellSeconds))
                {
                    yield return h;
                }

                Require(talk.Outcome is { Kind: ActionOutcomeKind.Confirmed }, $"buff-bot: tell-refused: '{BuffBotWire.CreditWord}' told to 0x{BuffBotWire.Guid:X8} was not echoed by the server ({Describe(talk)})");
                foreach (var h in Until(() => BuffBotRule.CreditsSince(S.State.Chat, asked) is not null || BuffBotRule.AwaySince(S.State.Chat, asked), BuffBotStation.TellSeconds))
                {
                    yield return h;
                }

                if (ask == 0 && BuffBotRule.CreditsSince(S.State.Chat, asked) is null && BuffBotRule.AwaySince(S.State.Chat, asked))
                {
                    foreach (var h in WaitAway(asked, $"the bot answered '{BuffBotWire.CreditWord}' with \"{BuffBotWire.AwayTell}\""))
                    {
                        yield return h;
                    }

                    continue; // its return answers the kept ask; it is asked once more all the same, the read is the newest
                }

                _credits = BuffBotRule.CreditsSince(S.State.Chat, asked);
                break;
            }

            Require(_credits is not null, $"buff-bot: credit-unread: the bot told no count within {BuffBotStation.TellSeconds:0} s of '{BuffBotWire.CreditWord}' (\"{BuffBotWire.Credits(1)}\" is its line)");
            if (_credits >= 1)
            {
                Log(string.Create(c, $"credit: the bot holds {_credits} credit(s) of hers: no give"));
                yield break;
            }

            Stage = "give";
            var held = Held();
            var scarab = ScarabInPack();
            Require(scarab != 0, $"buff-bot: no-scarab: level {S.State.Self.Qualities.Level} pays a credit a round from {BuffBotWire.FeeLevel}, the bot holds none of hers and the pack holds no {BuffBotWire.FeeName} ({BuffBotWire.FeeWcid}): her own pass covers the lines (the town stock buys them on a way out)");
            var given = S.State.Chat.Total;
            var give = S.Actions.Give(BuffBotWire.Guid, scarab, 1, ActionSource.Automation);
            yield return give;
            Log(string.Create(c, $"give: one {BuffBotWire.FeeName} (0x{scarab:X8}, {held} held) to {BuffBotWire.Name}: {Describe(give)}"));
            foreach (var h in Until(() => QuestReads.Give(S.State.Chat, given, BuffBotWire.Name) is not null || BuffBotRule.CreditsSince(S.State.Chat, given) is not null, BuffBotStation.TellSeconds))
            {
                yield return h;
            }

            var line = QuestReads.Give(S.State.Chat, given, BuffBotWire.Name);
            Require(line is null || line.Accepted, $"buff-bot: give-refused: {BuffBotWire.Name} did not take the {BuffBotWire.FeeName}: {line?.Outcome} ({line?.Text})");
            foreach (var h in Until(() => BuffBotRule.CreditsSince(S.State.Chat, given) is not null, BuffBotStation.TellSeconds))
            {
                yield return h;
            }

            _credits = BuffBotRule.CreditsSince(S.State.Chat, given);
            _gave++;
            Require(_credits is >= 1, $"buff-bot: no-credit-after-give: the {BuffBotWire.FeeName} was given ({(line is null ? "no give line" : line.Text)}) and {(_credits is null ? $"the bot told no count within {BuffBotStation.TellSeconds:0} s" : $"the count told is {_credits}")}");
            var after = Held();
            if (after == held - 1)
            {
                Log(string.Create(c, $"check after: the give took one {BuffBotWire.FeeName} ({held} -> {after} in the pack); the bot tells {_credits} credit(s)"));
            }
            else
            {
                Log(string.Create(c, $"check after: the bot tells {_credits} credit(s), but the pack held {held} {BuffBotWire.FeeName}(s) before the give and {after} after it, not one fewer (the pack's update may lag the bot's tell)"));
            }
        }

        /// <summary>D138, the fallback: the bot is away at the shop; wait, by name, for it to stand at its post again (its back line, or seen off its post and then on it), up to <see cref="BuffBotRule.AwaySeconds"/>.</summary>
        /// <param name="since">The chat mark from which its lines are read.</param>
        /// <param name="why">What showed it away.</param>
        private IEnumerable<ActionHandle?> WaitAway(long since, string why)
        {
            var stage = Stage;
            Stage = "away";
            _recoveries++;
            var waited = S.Clock.Now;
            Log(string.Create(CultureInfo.InvariantCulture, $"recovery=wait-away: {why}: the bot is away for tapers (a fallback: in normal play she is done before it leaves); waiting up to {BuffBotRule.AwaySeconds:0} s for it at its post"));
            var wasOff = false;
            bool Back()
            {
                if (BuffBotRule.SaidSince(S.State.Chat, since, BuffBotWire.BackAnnouncement))
                {
                    return true;
                }

                if (BotAt() is not { } b || b.CellId >> 16 != BuffBotWire.Landblock)
                {
                    return false;
                }

                wasOff |= BuffBotRule.Away(b);
                return wasOff && BuffBotRule.AtPost(b);
            }

            foreach (var h in Until(Back, BuffBotRule.AwaySeconds))
            {
                yield return h;
            }

            Require(Back(), $"buff-bot: bot-away: {why}, and it is not back at its post within {BuffBotRule.AwaySeconds:0} s");
            Log(string.Create(CultureInfo.InvariantCulture, $"the bot is back at its post after {S.Clock.Now - waited:0} s"));
            Stage = stage;
        }

        /// <summary>The fee's ware held in the pack (every stack).</summary>
        private int Held() => GearInventory.CountOf(S.State, [], BuffBotWire.FeeWcid);

        /// <summary>A stack of the fee's ware in the pack, or 0.</summary>
        private uint ScarabInPack()
        {
            var all = new List<uint>();
            GearInventory.Collect(S.State, all);
            return all.FirstOrDefault(g => S.State.Objects.TryGet(g, out var o) && o.Weenie.WeenieClassId == BuffBotWire.FeeWcid);
        }

        /// <summary>The bot's place when its object is in view with a position.</summary>
        private CellFrame? BotAt() => S.State.Objects.TryGet(BuffBotWire.Guid, out var bot) && bot.Position is { } at ? at : null;

        /// <summary>M6 14 bot reach: her distance to the bot at <paramref name="bot"/> as her client reads it, in words ("unplaced" when her own place is unknown).</summary>
        private string BotMetres(CellFrame bot) =>
            S.State.Self.Position is { } me ? string.Create(CultureInfo.InvariantCulture, $"{me.ToGlobal().DistanceTo(bot.ToGlobal()):0.0} m") : "unplaced";

        /// <summary>Counts the profile's lines whose spell is up fresh, logging each the first time it is seen.</summary>
        private int Landed(BuffBotProfile profile)
        {
            BuffBotRule.Read(profile, S.State.Self.Qualities, S.Clock.Now, _reads);
            foreach (var read in _reads)
            {
                if (read.Up && read.RemainingSeconds >= BuffBotRule.FreshSeconds && _landed.Add(read.Line.SpellId))
                {
                    Log(string.Create(CultureInfo.InvariantCulture, $"landed {_landed.Count} of {_lines}: {read.Line.Spell} ({read.Line.SpellId}) over {read.Line.Line}, {read.RemainingSeconds:0} s left"));
                }
            }

            return _landed.Count;
        }

        private string Where() => S.State.Self.Position is { } p ? string.Create(CultureInfo.InvariantCulture, $"cell 0x{p.CellId:X8} ({p.Origin.X:0.#}, {p.Origin.Y:0.#})") : "an unknown place";
    }
}
