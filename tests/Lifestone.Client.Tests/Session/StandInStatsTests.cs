using System.Buffers.Binary;
using System.Collections.Immutable;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Data.CharacterCreation;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The offline stand-in answers a raise and a train as ACE does (M3 1.1 s1b): the experience spent (0x02CF), then the stat's private update with the
/// rank the XpTable gives for the experience spent in total; the refusals ACE tells are lines, the ones it drops are silence. The XpTable here is a
/// synthetic one of the retail layout (cumulative lists), so the tests need no dats; the play-checks raise prices by the retail table.
/// </summary>
public sealed class StandInStatsTests
{
    private const uint Strength = 1;
    private const uint MaxHealth = 1;
    private const uint WarMagic = 34;
    private const uint Lockpick = 24;
    private const uint CreatureEnchantment = 31;
    private const uint LifeMagic = 33;

    /// <summary>A table of the retail layout: attribute rank n costs 100 n(n+1)/2 in total, a vital 50 n(n+1)/2, a trained skill 20, a specialised skill 10; 5 levels.</summary>
    internal static XpTableData SyntheticTable()
    {
        const int ranks = 10;
        var bytes = new List<byte>();
        void U32(uint v)
        {
            var b = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(b, v);
            bytes.AddRange(b);
        }

        void U64(ulong v)
        {
            var b = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(b, v);
            bytes.AddRange(b);
        }

        void List(uint unit)
        {
            for (uint n = 0; n <= ranks; n++)
            {
                U32(unit * n * (n + 1) / 2);
            }
        }

        U32(XpTableData.DataId);
        for (var i = 0; i < 4; i++)
        {
            U32(ranks);
        }

        U32(5);
        List(100);
        List(50);
        List(20);
        List(10);
        for (uint level = 0; level <= 5; level++)
        {
            U64(level * 1000UL);
        }

        for (uint level = 0; level <= 5; level++)
        {
            U32(level == 0 ? 0u : 1u);
        }

        return XpTableData.Decode(bytes.ToArray());
    }

    private static StandInVisit Visit(XpTableData? table, long availableXp = 1_000) => new()
    {
        EnterAfter = 0.2,
        UnhideAfter = 1.0,
        XpTable = table,
        Stats = new StandInPlayerStats
        {
            Level = 5,
            TotalExperience = 10_000,
            AvailableExperience = availableXp,
            AvailableSkillCredits = 3,
            Skills =
            [
                new SkillDesc(WarMagic, 0, 1, 3, 0, 0, 0, 0.0),
                new SkillDesc(CreatureEnchantment, 0, 1, 1, 0, 0, 0, 0.0),
                new SkillDesc(LifeMagic, 0, 1, 2, 0, 0, 0, 0.0),
            ],
        },
    };

    private sealed class Rig : IDisposable
    {
        private readonly ManualTimeProvider _time = new();

        public Rig(XpTableData? table, bool answerWorld = true, long availableXp = 1_000)
        {
            var visit = Visit(table, availableXp);
            Log = new RecordingLogSink();
            Session = GameSession.Offline(
                h =>
                {
                    StandInSessions.CharacterSelect(h);
                    StandInSessions.PlayerVisits(h, visit);
                    if (answerWorld)
                    {
                        StandInSessions.AnswerWorld(h, visit);
                    }
                },
                _time,
                Log);
        }

        public RecordingLogSink Log { get; }

        public GameSession Session { get; }

        public PlayerQualities Q => Session.State.Self.Qualities;

        public void Run(double seconds)
        {
            for (var t = 0.0; t < seconds; t += 0.1)
            {
                _time.Advance(TimeSpan.FromSeconds(0.1));
                Session.Tick(Session.Clock.Now);
            }
        }

        public ActionOutcome Do(ActionHandle handle, double seconds = 1.5)
        {
            Run(seconds);
            Assert.True(handle.IsCompleted);
            return handle.Outcome!.Value;
        }

        public void Dispose() => Session.Dispose();
    }

    private static Rig InWorld(XpTableData? table = null, bool answerWorld = true, long availableXp = 1_000)
    {
        var rig = new Rig(table ?? SyntheticTable(), answerWorld, availableXp);
        rig.Run(2.0);
        return rig;
    }

    [Fact]
    public void The_description_carries_the_level_the_experience_and_the_credits()
    {
        using var rig = InWorld();
        Assert.Equal((5, 10_000L, 1_000L, 3), (rig.Q.Level, rig.Q.TotalExperience, rig.Q.AvailableExperience, rig.Q.AvailableSkillCredits));
    }

    [Fact]
    public void A_raise_spends_the_experience_and_the_rank_follows_the_table_from_the_total_spent()
    {
        using var rig = InWorld();
        var one = rig.Session.Actions.RaiseAttribute(Strength, 100, ActionSource.Ui);
        Assert.Equal(ActionOutcomeKind.Confirmed, rig.Do(one).Kind);
        Assert.True(rig.Q.TryGetAttribute(Strength, out var a1));
        Assert.Equal((1u, 100u), (a1.Ranks, a1.Experience));
        Assert.Equal(900, rig.Q.AvailableExperience);
        Assert.Equal(10_000, rig.Q.TotalExperience); // spending does not change the total

        var two = rig.Session.Actions.RaiseAttribute(Strength, 200, ActionSource.Ui); // 100 + 200 = 300: rank 2
        Assert.Equal(ActionOutcomeKind.Confirmed, rig.Do(two).Kind);
        Assert.True(rig.Q.TryGetAttribute(Strength, out var a2));
        Assert.Equal((2u, 300u), (a2.Ranks, a2.Experience));
        Assert.Equal(700, rig.Q.AvailableExperience);

        var partial = rig.Session.Actions.RaiseAttribute(Strength, 50, ActionSource.Ui); // not a rank: the experience is spent, the update still answers
        Assert.Equal(ActionOutcomeKind.Confirmed, rig.Do(partial).Kind);
        Assert.True(rig.Q.TryGetAttribute(Strength, out var a3));
        Assert.Equal((2u, 350u), (a3.Ranks, a3.Experience));
    }

    [Fact]
    public void The_raise_chat_placeholder_is_logged_once_where_the_stand_in_leaves_the_line_out()
    {
        using var rig = InWorld();
        Assert.Equal(ActionOutcomeKind.Confirmed, rig.Do(rig.Session.Actions.RaiseAttribute(Strength, 100, ActionSource.Ui)).Kind);
        Assert.Equal(ActionOutcomeKind.Confirmed, rig.Do(rig.Session.Actions.RaiseAttribute(Strength, 200, ActionSource.Ui)).Kind);

        Assert.Single(rig.Log.Lines, line => line.Contains("TODO(m3-standin-raise-chat)", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Log.Lines, line => line.Contains("TODO(m3-standin-skill-names)", StringComparison.Ordinal)); // an attribute is named, not an id
    }

    [Fact]
    public void The_skill_names_placeholder_is_logged_once_when_a_skill_is_named_by_its_id()
    {
        using var rig = InWorld();
        Assert.Equal(ActionOutcomeKind.Confirmed, rig.Do(rig.Session.Actions.RaiseSkill(WarMagic, 10, ActionSource.Ui)).Kind);
        rig.Do(rig.Session.Actions.TrainSkill(LifeMagic, 1, ActionSource.Ui)); // already trained: the refusal line names the skill by its id

        Assert.Single(rig.Log.Lines, line => line.Contains("TODO(m3-standin-skill-names)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_vital_and_a_skill_price_by_their_own_lists_and_a_specialised_skill_by_the_specialised_one()
    {
        using var rig = InWorld();
        Assert.Equal(ActionOutcomeKind.Confirmed, rig.Do(rig.Session.Actions.RaiseVital(MaxHealth, 150, ActionSource.Ui)).Kind); // 50 n(n+1)/2: 150 is rank 2
        Assert.True(rig.Q.TryGetVital(MaxHealth, out var vital));
        Assert.Equal((2u, 150u), (vital.Ranks, vital.Experience));

        Assert.Equal(ActionOutcomeKind.Confirmed, rig.Do(rig.Session.Actions.RaiseSkill(LifeMagic, 60, ActionSource.Ui)).Kind); // trained: 20 n(n+1)/2: 60 is rank 2
        Assert.True(rig.Q.TryGetSkill(LifeMagic, out var trained));
        Assert.Equal(2, trained.Ranks);

        Assert.Equal(ActionOutcomeKind.Confirmed, rig.Do(rig.Session.Actions.RaiseSkill(WarMagic, 60, ActionSource.Ui)).Kind); // specialised: 10 n(n+1)/2: 60 is rank 3, where the trained list gave 2
        Assert.True(rig.Q.TryGetSkill(WarMagic, out var specialised));
        Assert.Equal(3, specialised.Ranks);
    }

    [Fact]
    public void ACE_s_refusals_are_told_where_ACE_tells_them_and_silent_where_it_is()
    {
        using var rig = InWorld();
        var beyondCap = rig.Session.Actions.RaiseAttribute(Strength, 5_501, ActionSource.Ui); // more than the 1,000 the character has: silent
        Assert.Equal(ActionOutcomeKind.TimedOut, rig.Do(beyondCap, 6).Kind);

        var vital = rig.Session.Actions.RaiseVital(MaxHealth, 5_000, ActionSource.Ui); // more than the character has: a vital's told refusal
        var told = rig.Do(vital);
        Assert.Equal(ActionOutcomeKind.ServerError, told.Kind);
        Assert.Contains("Your attempt to raise Max Health has failed.", told.Detail, StringComparison.Ordinal);

        var untrained = rig.Session.Actions.RaiseSkill(CreatureEnchantment, 20, ActionSource.Ui); // an untrained skill: silent
        Assert.Equal(ActionOutcomeKind.TimedOut, rig.Do(untrained, 6).Kind);
        Assert.Equal(1_000, rig.Q.AvailableExperience);
    }

    [Fact]
    public void An_attribute_past_its_rank_cap_gets_the_told_refusal()
    {
        using var rig = InWorld(availableXp: 100_000);
        var up = rig.Session.Actions.RaiseAttribute(Strength, 5_500, ActionSource.Ui); // the whole table: rank 10 is 100 x 55 = 5,500
        Assert.Equal(ActionOutcomeKind.Confirmed, rig.Do(up).Kind);
        var more = rig.Session.Actions.RaiseAttribute(Strength, 1, ActionSource.Ui);
        var told = rig.Do(more);
        Assert.Equal(ActionOutcomeKind.ServerError, told.Kind);
        Assert.Contains("Your attempt to raise Strength has failed.", told.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_train_spends_the_credits_trains_the_skill_and_a_second_train_is_refused_with_the_line()
    {
        using var rig = InWorld();
        var train = rig.Session.Actions.TrainSkill(CreatureEnchantment, 2, ActionSource.Ui);
        Assert.Equal(ActionOutcomeKind.Confirmed, rig.Do(train).Kind);
        Assert.True(rig.Q.TryGetSkill(CreatureEnchantment, out var skill));
        Assert.Equal(2u, skill.AdvancementClass);
        Assert.Equal(1, rig.Q.AvailableSkillCredits);

        var again = rig.Session.Actions.TrainSkill(CreatureEnchantment, 1, ActionSource.Ui);
        Assert.Equal(ActionOutcomeKind.ServerError, rig.Do(again).Kind);
        Assert.Equal(1, rig.Q.AvailableSkillCredits);

        var poor = rig.Session.Actions.TrainSkill(Lockpick, 5, ActionSource.Ui); // more credits than the character has: silent
        Assert.Equal(ActionOutcomeKind.TimedOut, rig.Do(poor, 6).Kind);
    }

    [Fact]
    public void Without_an_XpTable_the_stand_in_says_so_loudly_and_spends_nothing()
    {
        using var rig = new Rig(table: null);
        rig.Run(2.0);
        var raise = rig.Session.Actions.RaiseAttribute(Strength, 100, ActionSource.Ui);
        var outcome = rig.Do(raise);
        Assert.Equal(ActionOutcomeKind.ServerError, outcome.Kind);
        Assert.Contains("TODO(m3-standin-xp-table)", outcome.Detail, StringComparison.Ordinal);
        Assert.Equal(1_000, rig.Q.AvailableExperience);
    }

    [Fact]
    public void A_session_that_did_not_opt_in_refuses_the_stat_actions_as_every_offline_session_does()
    {
        using var rig = InWorld(answerWorld: false);
        var raise = rig.Session.Actions.RaiseAttribute(Strength, 100, ActionSource.Ui);
        rig.Run(0.5);
        Assert.Equal(ActionOutcomeKind.Refused, raise.Outcome!.Value.Kind);
    }
}
