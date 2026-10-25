using System.Globalization;
using System.Security.Cryptography;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Dat;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M3 1.1 s1b, DoD 1: the stat actions against the bundled ACE. WRITTEN AND COMPILED, RUN BY THE LEAD. On <c>Lifestone Throwaway</c> only (found on the
/// test account, or created with the headless character intent; never Smoke, Loopback or BSD Mage) it grants experience with the mod's <c>xp</c> verb
/// (<see cref="PlayerTools.AddXp"/>, a 64-bit amount), then raises Focus by one rank, Max Stamina by one rank and a trained skill by one rank, and trains
/// the cheapest untrained skill the credits allow.
/// </summary>
/// <remarks>
/// <para><b>What it proves.</b> (1) Each of RaiseAttribute, RaiseVital, RaiseSkill and TrainSkill completes <c>Confirmed</c> on ACE's private update of the
/// stat (0x02E3, 0x02E7, 0x02DD, and the credits' 0x02CD for the train). (2) The experience ACE takes for a rank equals what <see cref="XpTableData"/> says
/// the rank costs, counting ranks from the creation value (the stat's rank r to r+1 costs <c>list[r+1] - list[r]</c>): the available experience falls by
/// exactly that, the stat's experience spent rises by exactly that, and its rank rises by exactly one. That is s0's reading, confirmed live (a wrong reading
/// would make the server's rank differ from r+1, or the spent experience differ). (3) The read model follows the server: after the grant the total
/// experience, available experience, level and skill credits equal the mod's reply and the level is the XpTable's level for the total; raising spends the
/// available experience and leaves the total and the level alone; a train spends exactly the SkillTable's credits and turns the skill trained. (4) Each raise's chat line ("Your base X is now N!")
/// reaches the chat model within a few seconds of the stat's update (M3 4.2 x3, cluster-k1 gap 1).</para>
/// <para><b>Touches</b> only the throwaway (levelled freely, D3). A leftover throwaway pending deletion fails the run with the instruction to purge it
/// (<c>ModCharPurgeLiveTests</c> does). Since M4 P4 the run is over the throwaway's shared session (one login per test process; the throwaway is put in peace mode first) and does not log off at the end; the lists after the login are those of the session's entry.</para>
/// </remarks>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test-tools")]
[LoopbackCharacter("Lifestone Throwaway")]
[LiveSharedSession]
public sealed class StatsLiveTests(ITestOutputHelper output)
{
    private const string Throwaway = "Lifestone Throwaway";

    [LoopbackFact]
    public void The_four_stat_actions_confirm_on_the_private_updates_and_spend_what_the_tables_say()
    {
        using var dats = DatSet.Open();
        var leg = new StatsLeg(HeadlessCharacter.RetailSkillClasses(dats), XpTableData.Load(dats), SkillTableData.Load(dats), output.WriteLine);
        var result = Run(leg, 600);
        Assert.True(result.Ok, result.Failure);
        Assert.Equal("done", leg.Stage);

        Assert.Equal(ActionOutcomeKind.Confirmed, leg.Outcomes["RaiseAttribute"]);
        Assert.Equal(ActionOutcomeKind.Confirmed, leg.Outcomes["RaiseVital"]);
        Assert.Equal(ActionOutcomeKind.Confirmed, leg.Outcomes["RaiseSkill"]);
        Assert.Equal(ActionOutcomeKind.Confirmed, leg.Outcomes["TrainSkill"]);
        Assert.All(leg.Spent, x => Assert.True(x.Expected == x.Actual, $"{x.What}: the table says {x.Expected}, the server changed it by {x.Actual}")); // each (what, table cost, server's change) triple agrees
        foreach (var x in leg.Spent)
        {
            output.WriteLine($"{x.What}: table {x.Expected}, server {x.Actual}");
        }
        Assert.Equal(7, leg.Spent.Count);
        Assert.Equal(3, leg.RaiseLines); // the attribute's, the vital's and the skill's raise each told "Your base X is now N!" and the line reached the chat model (cluster-k1 gap 1)
    }

    private HeadlessRunResult Run(IScenario scenario, double seconds) =>
        LiveRun.Run(output, scenario, "stats", seconds, item: "p4", character: Throwaway);

    /// <summary>
    /// The whole leg in one session, written as a script that yields a handle to wait for it (or null to wait a frame): login, the throwaway (found or created),
    /// the world, the grant, the four actions with their checks, log off, quit. A failure logs off first when the player is in the world.
    /// </summary>
    internal sealed class StatsLeg : IScenario
    {
        private const double ArrivalSeconds = 90;
        private const double SettleSeconds = 10;
        private const double BailSeconds = 30;
        private const uint Focus = 5;
        private const uint MaxStamina = 3;

        private readonly IReadOnlyList<uint> _skillClasses;
        private readonly XpTableData _xp;
        private readonly SkillTableData _skills;
        private readonly Action<string> _log;
        private readonly IEnumerator<ActionHandle?> _script;
        private ActionHandle? _wait;
        private double _since;
        private IGameSessionView? _session;

        public StatsLeg(IReadOnlyList<uint> skillClasses, XpTableData xp, SkillTableData skills, Action<string> log)
        {
            _skillClasses = skillClasses;
            _xp = xp;
            _skills = skills;
            _log = log;
            _script = Script().GetEnumerator();
        }

        public string Name => "stats-leg";

        public string Stage { get; private set; } = "start";

        public string? Failure { get; private set; }

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        /// <summary>The outcome of each stat action, by name.</summary>
        public Dictionary<string, ActionOutcomeKind> Outcomes { get; } = [];

        /// <summary>For each action: what it was (the stat and the table's price), the price the XpTable or SkillTable gives, and what the server's state changed by.</summary>
        public List<(string What, long Expected, long Actual)> Spent { get; } = [];

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            ArgumentNullException.ThrowIfNull(session);
            _session = session;
            if (Stage == "start" && LiveMage.AlreadyInWorld(session) && LiveMage.WrongCharacter(session, Throwaway) is { } wrong)
            {
                // Nothing was sent (the shared session is the host's: no log off).
                Failure = wrong;
                _log("FAILED: " + wrong);
                Stage = "failed";
                return ScenarioStatus.Failed;
            }

            switch (Stage)
            {
                case "done":
                    return ScenarioStatus.Succeeded;
                case "failed":
                    return ScenarioStatus.Failed;
                case "bailing":
                    if (_wait is { IsCompleted: true } || session.State.Phase != SessionPhase.InWorld || session.Clock.Now - _since > BailSeconds)
                    {
                        Stage = "failed";
                        return ScenarioStatus.Failed;
                    }

                    return ScenarioStatus.Running;
            }

            if (_wait is { IsCompleted: false })
            {
                return ScenarioStatus.Running;
            }

            try
            {
                if (_script.MoveNext())
                {
                    _wait = _script.Current;
                    return ScenarioStatus.Running;
                }

                Stage = "done";
                return ScenarioStatus.Succeeded;
            }
            catch (InvalidOperationException e)
            {
                Failure ??= Stage + ": " + e.Message;
                _log("FAILED: " + Failure);
                if (session.State.Phase == SessionPhase.InWorld)
                {
                    _since = session.Clock.Now;
                    _wait = session.Actions.LogOff(ActionSource.Host);
                    Stage = "bailing";
                    return ScenarioStatus.Running;
                }

                Stage = "failed";
                return ScenarioStatus.Failed;
            }
        }

        /// <summary>How long the raise's chat line may trail the stat's private update (ACE sends it after the updates, in the same turn).</summary>
        private const double ChatSeconds = 5.0;

        /// <summary>How many raises (of the three) had their "Your base X is now N!" line (ACE's <c>Player_Xp</c> raise methods) reach <c>GameState.Chat</c>.</summary>
        public int RaiseLines => raiseLines;

        private int raiseLines;

        /// <summary>True when a chat line since <paramref name="mark"/> (a <c>ChatLog.Total</c>) reads "Your base X is now N!".</summary>
        private static bool RaiseLineSeen(GameState state, long mark)
        {
            var fresh = (int)Math.Min(state.Chat.Count, state.Chat.Total - mark);
            for (var i = state.Chat.Count - fresh; i < state.Chat.Count; i++)
            {
                var text = state.Chat[i].Text;
                if (text.StartsWith("Your base ", StringComparison.Ordinal) && text.Contains(" is now ", StringComparison.Ordinal) && text.EndsWith('!'))
                {
                    return true;
                }
            }

            return false;
        }

        private static void Require(bool ok, string what)
        {
            if (!ok)
            {
                throw new InvalidOperationException(what);
            }
        }

        private static void Confirmed(ActionHandle handle, string what) =>
            Require(handle.Outcome is { Kind: ActionOutcomeKind.Confirmed }, what + ": " + handle.Outcome?.Kind + (handle.Outcome?.Detail is { } d ? ": " + d : string.Empty));

        private static CharacterSlot? Find(IGameSessionView session, string name) =>
            session.State.Characters.Entries.OrderBy(c => c.PendingDeletion).ThenBy(c => c.Guid).Cast<CharacterSlot?>().FirstOrDefault(c => string.Equals(c!.Value.Name, name, StringComparison.OrdinalIgnoreCase));

        private static string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

        private IEnumerable<ActionHandle?> Script()
        {
            var s = _session ?? throw new InvalidOperationException("no session");
            if (LiveMage.AlreadyInWorld(s))
            {
                Stage = "running";
                _log("already in the world: the shared session's entry logged the character in");
            }
            else
            {
                Stage = "login";
                var login = s.Actions.Login(ActionSource.Host);
                yield return login;
                Confirmed(login, "login");

                Stage = "throwaway";
                var slot = Find(s, Throwaway);
                Require(slot is not { PendingDeletion: true }, $"'{Throwaway}' is pending deletion from an earlier run: purge it first (ModCharPurgeLiveTests does)");
                if (slot is null)
                {
                    var create = s.Actions.CreateCharacter(HeadlessCharacter.Intent(Throwaway, _skillClasses), ActionSource.Host);
                    yield return create;
                    Confirmed(create, "creating the throwaway");
                    slot = Find(s, Throwaway);
                    Require(slot is { PendingDeletion: false }, "creating the throwaway: the list does not show it");
                }

                Stage = "entering";
                var enter = s.Actions.EnterWorld(slot!.Value.Guid, ActionSource.Host);
                yield return enter;
                Confirmed(enter, "entering the world as " + Throwaway);
                Stage = "arriving";
                _since = s.Clock.Now;
                while (s.State.Self.Travel != SelfTravel.Present || !s.State.Self.Qualities.IsKnown)
                {
                    Require(s.Clock.Now - _since <= ArrivalSeconds, "arriving: the player was not unhidden with its qualities within " + ArrivalSeconds + " s");
                    yield return null;
                }
            }

            // --- the plan: one rank of each, priced by the tables from the server's own starting numbers ---
            Stage = "planning";
            var q = s.State.Self.Qualities;
            Require(q.TryGetAttribute(Focus, out var focus), "the description has no Focus");
            Require(q.TryGetVital(MaxStamina, out var stamina), "the description has no Max Stamina");
            Require(focus.Experience == _xp.AttributeXpList[(int)focus.Ranks], $"Focus has {focus.Experience} experience spent at rank {focus.Ranks}: not on a rank boundary (the throwaway is hand-modified)");
            Require(stamina.Experience == _xp.VitalXpList[(int)stamina.Ranks], $"Max Stamina has {stamina.Experience} experience spent at rank {stamina.Ranks}: not on a rank boundary");
            var attributeCost = _xp.XpToRaiseAttribute((int)focus.Ranks, (int)focus.Ranks + 1);
            var vitalCost = _xp.XpToRaiseVital((int)stamina.Ranks, (int)stamina.Ranks + 1);

            var trained = q.Skills.Values.Where(k => k.AdvancementClass >= 2).OrderBy(k => k.Id).Cast<SkillDesc?>().FirstOrDefault();
            Require(trained is not null, "the throwaway has no trained skill to raise");
            var skillList = trained!.Value.AdvancementClass == 3 ? _xp.SpecializedSkillXpList : _xp.TrainedSkillXpList;
            Require(trained.Value.Experience == skillList[trained.Value.Ranks], $"skill {trained.Value.Id} is not on a rank boundary");
            var skillCost = XpTableData.XpToRaise(skillList, trained.Value.Ranks, trained.Value.Ranks + 1);

            var untrained = _skills.Skills.Where(k => k.TrainedCost > 0 && q.TryGetSkill(k.Id, out var sk) && sk.AdvancementClass == 1).OrderBy(k => k.TrainedCost).ThenBy(k => k.Id).FirstOrDefault();
            Require(untrained is not null, "the throwaway has no untrained skill to train");
            var credits = untrained!.TrainedCost;

            // The grant: the three raises, and the levels that give the credits the train needs.
            var needCredits = Math.Max(0, credits - q.AvailableSkillCredits);
            long levelGrant = 0;
            if (needCredits > 0)
            {
                var level = q.Level;
                while (level < _xp.LevelCap && _xp.SkillCreditsThroughLevel(level, q.Level) < needCredits)
                {
                    level++;
                }

                Require(_xp.SkillCreditsThroughLevel(level, q.Level) >= needCredits, "the XpTable has no level that gives the credits needed");
                levelGrant = Math.Max(0, (long)_xp.XpForLevel(level) - q.TotalExperience);
            }

            var raiseTotal = (long)(attributeCost + vitalCost + skillCost);
            var grant = Math.Max(1, Math.Max(levelGrant, raiseTotal - q.AvailableExperience));
            _log($"plan: Focus {focus.Ranks}->{focus.Ranks + 1} costs {N((long)attributeCost)}; Max Stamina {stamina.Ranks}->{stamina.Ranks + 1} costs {N((long)vitalCost)}; skill {trained.Value.Id} class {trained.Value.AdvancementClass} {trained.Value.Ranks}->{trained.Value.Ranks + 1} costs {N((long)skillCost)}; train skill {untrained.Id} '{untrained.Name}' for {credits} credits (have {q.AvailableSkillCredits}); grant {N(grant)} experience (level {q.Level}, total {N(q.TotalExperience)}, available {N(q.AvailableExperience)})");

            // --- the grant, and the read model following the mod's reply ---
            Stage = "granting";
            var totalBefore = q.TotalExperience;
            var availableBefore = q.AvailableExperience;
            var add = s.Tools.AddXp(grant, ActionSource.Host);
            yield return add;
            Confirmed(add, "the xp grant");
            var reply = add.Result ?? throw new InvalidOperationException("the xp grant gave no result");
            Require(reply.Added > 0, "the xp grant added nothing");
            _since = s.Clock.Now;
            while (q.TotalExperience != reply.Total || q.AvailableExperience != reply.Available || q.Level != reply.Level || q.AvailableSkillCredits < credits)
            {
                Require(s.Clock.Now - _since <= SettleSeconds, $"the read model did not follow the grant: total {q.TotalExperience} (reply {reply.Total}), available {q.AvailableExperience} (reply {reply.Available}), level {q.Level} (reply {reply.Level}), credits {q.AvailableSkillCredits} (need {credits})");
                yield return null;
            }

            Require(q.TotalExperience == totalBefore + reply.Added && q.AvailableExperience == availableBefore + reply.Added, "the grant changed the total and the available experience by different amounts");
            Require((long)_xp.XpForLevel(q.Level) <= q.TotalExperience && (q.Level >= _xp.LevelCap || q.TotalExperience < (long)_xp.XpForLevel(q.Level + 1)), $"level {q.Level} is not the XpTable's level for {N(q.TotalExperience)} experience");
            _log($"granted {N(reply.Added)}: level {q.Level}, total {N(q.TotalExperience)}, available {N(q.AvailableExperience)}, credits {q.AvailableSkillCredits}");

            // --- RaiseAttribute ---
            Stage = "raising-attribute";
            var (level0, total0) = (q.Level, q.TotalExperience);
            availableBefore = q.AvailableExperience;
            var chatMark = s.State.Chat.Total;
            var attribute = s.Actions.RaiseAttribute(Focus, (uint)attributeCost, ActionSource.Host);
            yield return attribute;
            Outcomes["RaiseAttribute"] = attribute.Outcome?.Kind ?? ActionOutcomeKind.Cancelled;
            Confirmed(attribute, "RaiseAttribute");
            _since = s.Clock.Now;
            while (!RaiseLineSeen(s.State, chatMark))
            {
                Require(s.Clock.Now - _since <= ChatSeconds, "Focus: ACE's \"Your base ... is now ...!\" line did not reach the chat model within " + ChatSeconds + " s of the raise");
                yield return null;
            }

            raiseLines++;
            Require(q.TryGetAttribute(Focus, out var focusAfter) && focusAfter.Ranks == focus.Ranks + 1, $"Focus ranks {focus.Ranks} -> {focusAfter.Ranks}: expected one more");
            Spent.Add(("Focus: available experience taken", (long)attributeCost, availableBefore - q.AvailableExperience));
            Spent.Add(("Focus: experience spent on the stat", (long)attributeCost, focusAfter.Experience - focus.Experience));
            _log($"RaiseAttribute Focus: {attribute.Outcome?.Detail}");

            // --- RaiseVital ---
            Stage = "raising-vital";
            availableBefore = q.AvailableExperience;
            chatMark = s.State.Chat.Total;
            var vital = s.Actions.RaiseVital(MaxStamina, (uint)vitalCost, ActionSource.Host);
            yield return vital;
            Outcomes["RaiseVital"] = vital.Outcome?.Kind ?? ActionOutcomeKind.Cancelled;
            Confirmed(vital, "RaiseVital");
            _since = s.Clock.Now;
            while (!RaiseLineSeen(s.State, chatMark))
            {
                Require(s.Clock.Now - _since <= ChatSeconds, "Max Stamina: ACE's \"Your base ... is now ...!\" line did not reach the chat model within " + ChatSeconds + " s of the raise");
                yield return null;
            }

            raiseLines++;
            Require(q.TryGetVital(MaxStamina, out var staminaAfter) && staminaAfter.Ranks == stamina.Ranks + 1, "Max Stamina did not gain exactly one rank");
            Spent.Add(("Max Stamina: available experience taken", (long)vitalCost, availableBefore - q.AvailableExperience));
            Spent.Add(("Max Stamina: experience spent on the stat", (long)vitalCost, staminaAfter.Experience - stamina.Experience));
            _log($"RaiseVital Max Stamina: {vital.Outcome?.Detail}");

            // --- RaiseSkill ---
            Stage = "raising-skill";
            availableBefore = q.AvailableExperience;
            chatMark = s.State.Chat.Total;
            var skill = s.Actions.RaiseSkill(trained.Value.Id, (uint)skillCost, ActionSource.Host);
            yield return skill;
            Outcomes["RaiseSkill"] = skill.Outcome?.Kind ?? ActionOutcomeKind.Cancelled;
            Confirmed(skill, "RaiseSkill");
            _since = s.Clock.Now;
            while (!RaiseLineSeen(s.State, chatMark))
            {
                Require(s.Clock.Now - _since <= ChatSeconds, "the skill: ACE's \"Your base ... is now ...!\" line did not reach the chat model within " + ChatSeconds + " s of the raise");
                yield return null;
            }

            raiseLines++;
            Require(q.TryGetSkill(trained.Value.Id, out var skillAfter) && skillAfter.Ranks == trained.Value.Ranks + 1, "the skill did not gain exactly one rank");
            Spent.Add(("the skill: available experience taken", (long)skillCost, availableBefore - q.AvailableExperience));
            Spent.Add(("the skill: experience spent on the stat", (long)skillCost, skillAfter.Experience - trained.Value.Experience));
            _log($"RaiseSkill {trained.Value.Id}: {skill.Outcome?.Detail}");

            // The raises left the total and the level alone.
            Require(q.Level == level0 && q.TotalExperience == total0, "spending experience changed the level or the total experience");

            // --- TrainSkill ---
            Stage = "training";
            var creditsBefore = q.AvailableSkillCredits;
            var train = s.Actions.TrainSkill(untrained.Id, credits, ActionSource.Host);
            yield return train;
            Outcomes["TrainSkill"] = train.Outcome?.Kind ?? ActionOutcomeKind.Cancelled;
            Confirmed(train, "TrainSkill");
            Require(q.TryGetSkill(untrained.Id, out var trainedNow) && trainedNow.AdvancementClass >= 2, "the skill is not trained after TrainSkill");
            Spent.Add(("TrainSkill: credits taken", credits, creditsBefore - q.AvailableSkillCredits));
            _log($"TrainSkill {untrained.Id} '{untrained.Name}': {train.Outcome?.Detail}");

            // --- out ---
            if (LiveRun.KeepSession)
            {
                _log("the shared session stays logged in (no log off at the end of the leg)");
                yield break;
            }

            Stage = "logging-off";
            var off = s.Actions.LogOff(ActionSource.Host);
            yield return off;
            Confirmed(off, "logging off");
            Stage = "quitting";
            var quit = s.Actions.Quit(ActionSource.Host);
            yield return quit;
            Require(quit.Outcome is { Kind: ActionOutcomeKind.Sent }, "quitting: " + quit.Outcome?.Kind);
        }
    }
}
