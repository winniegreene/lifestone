using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Session.Housing;

/// <summary>
/// The writ hand-in (M6 villa): minor stones to Holtburg's Agent of the Arcanum in pairs, one Writ of Refuge a pair (<see cref="WritRule.PlanHandIn"/>; the Agent's flow in <see cref="WritFacts"/>). Check, act, check:
/// <list type="number">
/// <item>Before: the stones held and the plan (a lone stone with no half pair is kept: <c>reason=not-due:one-stone-kept</c>, ok); the body in Holtburg (else Lifestone Recall home first) and the Agent met in reach.</item>
/// <item>Each give: before, the stone still held and the Agent in reach; the act, one Give of one stone; after, the Agent's Tell: <see cref="WritFacts.FirstStoneLine"/> (the half of a pair) or <see cref="WritFacts.WritLine"/>
/// with the writ count risen by one within <see cref="HouseLeg.AfterSeconds"/>. The Agent's busy refusal waits <see cref="QuestRun.BusyQuietSeconds"/> and gives once more (<c>recovery=give-after-busy</c>); any other refusal,
/// no reply, or no writ after the writ line fails by name.</item>
/// </list>
/// The line: <c>writ_hand_in: stones_before= stones_after= writs_before= writs_after= gives= half_pending= ok= reason=</c>. The run ends at the Agent (Holtburg is home).
/// </summary>
/// <param name="halfPending">True when the caller knows the server holds a first stone (the last hand-in's verdict said <c>half_pending=yes</c>).</param>
public sealed class WritHandInStation(bool halfPending = false) : IStation
{
    /// <summary>The station's name.</summary>
    public const string StationName = "writ-hand-in";

    /// <summary>The line's prefix.</summary>
    public const string LinePrefix = "writ_hand_in:";

    /// <summary>Holtburg's landblock (the Agent's and the home lifestone's).</summary>
    public const uint HoltburgLandblock = 0xA9B4;

    /// <inheritdoc />
    public string Name => StationName;

    /// <inheritdoc />
    /// <remarks>In peace (a give to an NPC), with the mana for the recall home.</remarks>
    public StationNeeds Needs { get; } = new(Peace: true, MinManaPercent: 20);

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new Run(context, halfPending);
    }

    private sealed class Run(JourneyContext context, bool halfPending) : HouseLeg(StationName, context)
    {
        private int _stonesBefore = -1, _stonesAfter = -1, _writsBefore = -1, _writsAfter = -1, _gives;
        private bool _half = halfPending, _lined;

        protected override string Prefix => "writ";

        protected override string Fields() => string.Create(
            CultureInfo.InvariantCulture,
            $"stones_before={_stonesBefore} stones_after={_stonesAfter} writs_before={_writsBefore} writs_after={_writsAfter} gives={_gives} half_pending={Yes(_half)} ok={Yes(Failure is null)} reason={Reason}");

        protected override IEnumerable<ActionHandle?> Script()
        {
            try
            {
                Stage = "count";
                var stones = Stones();
                _stonesBefore = stones.Sum(s => s.Stack);
                _writsBefore = Held(VillaFacts.WritWcid);
                var (gives, writs, decision) = WritRule.PlanHandIn(stones, _half);
                Log($"writ: check before: {decision.Why}; writs held {_writsBefore}");
                if (!decision.May)
                {
                    NotNow(decision.Token, decision.Why);
                    yield break;
                }

                Stage = "travel";
                if (!InLandblock(HoltburgLandblock))
                {
                    foreach (var h in GoHome()) { yield return h; }

                    if (Failure is not null || !InLandblock(HoltburgLandblock))
                    {
                        Fail("not-in-holtburg", $"the way to Holtburg's Agent ended at {Where()}");
                        yield break;
                    }
                }

                var agent = WritFacts.HoltburgAgent;
                var reached = false;
                foreach (var h in Meet(agent, TravelPurpose.Cell, PathwardenRun.UseRange, QuestStepLeg.UseReachMetres, ok => reached = ok)) { yield return h; }

                if (!reached)
                {
                    Fail("agent-unreached", $"the Agent of the Arcanum 0x{agent.Guid:X8} was not met in reach: {LastTravelFailure ?? "not seen in reach"}");
                    yield break;
                }

                Stage = "give";
                for (var n = 0; n < gives.Count && Failure is null; n++)
                {
                    foreach (var h in GiveOne(agent, gives[n], n + 1, gives.Count)) { yield return h; }
                }

                if (Failure is null)
                {
                    Log(string.Create(CultureInfo.InvariantCulture, $"writ: check after the hand-in: writs {_writsBefore} -> {Held(VillaFacts.WritWcid)} (planned +{writs}); {(_half ? "a half pair is at the Agent" : "no half pair left")}"));
                }
            }
            finally
            {
                Emit();
            }
        }

        /// <summary>One give, check, act, check (see the class).</summary>
        private IEnumerable<ActionHandle?> GiveOne(PlacedThing agent, uint stone, int n, int of)
        {
            if (!S.State.Self.Inventory.Contains(stone))
            {
                Fail("stone-gone", $"give {n} of {of}: the stone 0x{stone:X8} is no longer held");
                yield break;
            }

            if (DistanceTo(agent.Guid) is not { } metres || metres > QuestStepLeg.UseReachMetres)
            {
                Fail("agent-out-of-reach", string.Create(CultureInfo.InvariantCulture, $"give {n} of {of}: the Agent stands {(DistanceTo(agent.Guid) is { } d ? d.ToString("0.0", CultureInfo.InvariantCulture) + " m" : "unseen")} away, beyond {QuestStepLeg.UseReachMetres:0.0} m"));
                yield break;
            }

            var writsBefore = Held(VillaFacts.WritWcid);
            var name = S.State.Objects.TryGet(stone, out var o) ? o.Name : "a stone";
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var mark = S.State.Chat.Total;
                Log(string.Create(CultureInfo.InvariantCulture, $"writ: give {n} of {of}: {name} 0x{stone:X8} to the Agent 0x{agent.Guid:X8} ({metres:0.0} m); writs {writsBefore}, half pair {Yes(_half)}"));
                var give = S.Actions.Give(agent.Guid, stone, 1, ActionSource.Automation);
                yield return give;
                var refusal = QuestReads.Give(S.State.Chat, mark, agent.Name);
                if (give.Outcome is not { Kind: ActionOutcomeKind.Confirmed })
                {
                    if (attempt == 0 && refusal is { Outcome: QuestGiveOutcome.Busy })
                    {
                        Log($"writ: recovery=give-after-busy: the Agent is busy; one more give after {QuestRun.BusyQuietSeconds:0.0} s");
                        foreach (var h in Pause(QuestRun.BusyQuietSeconds)) { yield return h; }

                        continue;
                    }

                    Fail("give-refused", $"give {n} of {of}: {Describe(give)}{QuestReads.Said(S.State.Chat, mark)}");
                    yield break;
                }

                foreach (var h in Until(() => QuestReads.Replied(S.State.Chat, mark, WritFacts.FirstStoneLine) || QuestReads.Replied(S.State.Chat, mark, WritFacts.WritLine), AfterSeconds)) { yield return h; }

                _gives++;
                if (QuestReads.Replied(S.State.Chat, mark, WritFacts.WritLine))
                {
                    foreach (var h in Until(() => Held(VillaFacts.WritWcid) >= writsBefore + 1, AfterSeconds)) { yield return h; }

                    var after = Held(VillaFacts.WritWcid);
                    if (after < writsBefore + 1)
                    {
                        Fail("after-check", $"give {n} of {of}: the Agent said the writ line but the writs are {writsBefore} -> {after}");
                        yield break;
                    }

                    _half = false;
                    Log($"writ: check after give {n}: the writ arrived, writs {writsBefore} -> {after}");
                }
                else if (QuestReads.Replied(S.State.Chat, mark, WritFacts.FirstStoneLine))
                {
                    _half = true;
                    Log($"writ: check after give {n}: the first stone of a pair (the Agent: \"{WritFacts.FirstStoneLine}\")");
                }
                else
                {
                    Fail("no-reply", $"give {n} of {of}: the Agent said neither line within {AfterSeconds:0} s{QuestReads.Said(S.State.Chat, mark)}");
                }

                yield break;
            }
        }

        /// <summary>The minor stones held (any of the four), guid and stack.</summary>
        private List<(uint Guid, int Stack)> Stones()
        {
            var list = new List<(uint, int)>();
            foreach (var entry in S.State.Self.Inventory.Pack.Concat(S.State.Self.Inventory.SidePacks.SelectMany(p => S.State.Self.Inventory.ItemsIn(p.Guid))))
            {
                if (S.State.Objects.TryGet(entry.Guid, out var o) && WritFacts.StoneWcids.Contains(o.Weenie.WeenieClassId))
                {
                    list.Add((entry.Guid, Mage.GearInventory.StackOf(S.State, o)));
                }
            }

            return list;
        }

        private void Emit()
        {
            if (_lined)
            {
                return;
            }

            _lined = true;
            _stonesAfter = Stones().Sum(s => s.Stack);
            _writsAfter = Held(VillaFacts.WritWcid);
            Log(LinePrefix + " " + Fields());
        }
    }
}
