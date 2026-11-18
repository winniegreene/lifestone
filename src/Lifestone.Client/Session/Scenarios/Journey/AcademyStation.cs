using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
using Lifestone.Data.Magic;
using Lifestone.Data.World;
using DatReaderWriter.Enums;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// A station's start rule (M6 item 0 a3b): where the player must stand for the station to be playable. A cheat-free journey (<see cref="JourneyPlan.CheatFree"/>) reads it at its known state and stops by name, never places, when the first station is not playable.
/// </summary>
public interface IKnownStart
{
    /// <summary>Null when the station can start from <paramref name="at"/>; else the short name of where the player is (<c>cell-0x...</c>, <c>no-position</c>), the stop's <c>&lt;where&gt;</c>.</summary>
    /// <param name="at">The player's position, or null.</param>
    string? Unplayable(CellFrame? at);
}

/// <summary>
/// <c>academy</c> (M6 item 0 a3b, DoD 2): the Training Academy by play, a station reading <c>quests/academy.json</c> by step kind (<see cref="AcademyPlan"/>), from the start cell a new character lands in to the exit portal's arrival in Holtburg: the Calling Stone to the
/// Society Greeter, the three leather pieces picked up and worn (F1's wear rule, <see cref="PathwardenWear"/>), Samuel and the Training Master, a Sparring Golem killed with war spells and its Academy Token looted and given (AwardXP 1,000), the Central Courtyard portal,
/// the Sentry, an Adolescent Olthoi killed and its Protection Orb looted and given (a level-proportional award, SentryTaskComplete), and the Exit to Holtburg. Every step is check, act, check (M5 D141), with one named recovery and PROBLEM lines; every walk has the
/// step's purpose (M5 D47); the character fights back any attacker (M5 D88). The station sends no mod verb but the <c>quest has</c> read, and never a place: the Academy's rooms are walked through the journey's runner and its portals are used.
/// <para><b>The fight.</b> A single-target kill over the Action API's targeted cast (the cast seam the fight's <c>CastAttempt</c> wraps), not the ground fight: the Academy is a quest's two named creatures in two named rooms, not a hunting ground, and the token and the
/// orb are looted by the quest file's weenie class, which the fight's value picker does not promise. The war spell is the best the spellbook knows (War Magic, a projectile, the highest level, the least mana); the caster comes out of the pack first (a new character wields a weapon).</para>
/// </summary>
public sealed class AcademyStation(string questKey = AcademyStation.FullRouteKey) : IStation, IKnownStart
{
    /// <summary>The station's name (the verdict line's <c>station=</c>).</summary>
    public const string StationName = "academy";

    /// <summary>The full route's quest key (<c>quests/academy.json</c>: the Sentry, the hive, the exit portal; waits for item 4's ramps, D37).</summary>
    public const string FullRouteKey = "academy";

    /// <summary>The tutorial's own skip's quest key (<c>quests/academy-skip.json</c>, M6 0 F7, D37): Jonathan's Exit Token given back, the server's teleport to Holtburg.</summary>
    public const string SkipKey = "academy-skip";

    /// <inheritdoc />
    public string Name => StationName;

    /// <inheritdoc />
    public StationNeeds Needs { get; } = new(Where: StationWhere.Here, Peace: true);

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context) => new AcademyRun(context, QuestCatalog.Get(questKey));

    /// <inheritdoc />
    public string? Unplayable(CellFrame? at) => AcademyPlan.Unplayable(QuestCatalog.Get(questKey), at);
}

/// <summary>What the Academy station does for one step of the quest file.</summary>
public enum AcademyAct
{
    /// <summary>A <c>go</c>: walk to the step's place (a point in a room), for the step's target.</summary>
    Go,

    /// <summary>A <c>talk</c>: Use the NPC, the reply is the check.</summary>
    Talk,

    /// <summary>A <c>give</c>: Give the item to the NPC, the reply and the stamp are the check.</summary>
    Give,

    /// <summary>A <c>pick-up</c> with no target: the item lies on the floor; picked up, and worn when it can be.</summary>
    PickUpFloor,

    /// <summary>A <c>pick-up</c> whose target is a creature: kill it with war spells and loot the item from its corpse.</summary>
    KillAndLoot,

    /// <summary>A <c>use</c> of a portal inside the Academy (the Central Courtyard): the check is a teleport away from it.</summary>
    UsePortal,

    /// <summary>A <c>use</c> whose place is outside the Academy's landblock: the exit portal, the check is the arrival there and the reply.</summary>
    Exit,

    /// <summary>A <c>use</c> with an item (M6 0 F6): the key used on a locked door (the Academy Library Key on the Academy Library door); the check is the server's unlock line or the door's Locked property false.</summary>
    Unlock,

    /// <summary>A <c>give</c> whose place is outside the Academy's landblock (M6 0 F7, D37): the Exit Token given back to Jonathan, whose emote set teleports the player to Holtburg by the server's own teleport; the check is the teleport the client saw, the landblock and the reply.</summary>
    GiveExit,
}

/// <summary>One step of the Academy's plan.</summary>
/// <param name="Act">What the station does.</param>
/// <param name="Step">The quest file's step.</param>
public sealed record AcademyStepPlan(AcademyAct Act, QuestStep Step);

/// <summary>The Academy station's pure parts (M6 item 0 a3b): the plan read from the quest file, the cell matching by low word and the start rule.</summary>
public static class AcademyPlan
{
    /// <summary>
    /// The plan of <paramref name="quest"/>, step by step: go, talk and give as named; a pick-up with a target is a kill and a loot (the target is the creature whose corpse holds the item), without one a floor pick-up; a use whose place is outside the start's landblock is the exit, any other
    /// use a portal inside. A step the station cannot play fails here, naming it: a go without a point, a kill or a talk or a give without the target's weenie class, a pick-up without the item's.
    /// </summary>
    /// <param name="quest">The quest (<c>academy</c>).</param>
    /// <exception cref="InvalidOperationException">A step the station cannot play.</exception>
    public static IReadOnlyList<AcademyStepPlan> Of(Quest quest)
    {
        ArgumentNullException.ThrowIfNull(quest);
        var plan = new List<AcademyStepPlan>(quest.Steps.Count);
        foreach (var step in quest.Steps)
        {
            AcademyAct act;
            switch (step.Kind)
            {
                case QuestStepKind.Go:
                    _ = step.Place?.Point ?? throw Unplayable(step, "TODO(m6-academy-go-point): a go step needs a place with a point");
                    act = AcademyAct.Go;
                    break;
                case QuestStepKind.Talk:
                    _ = step.Target?.Wcid ?? throw Unplayable(step, "the talk names no NPC weenie class");
                    act = AcademyAct.Talk;
                    break;
                case QuestStepKind.Give:
                    _ = step.Target?.Wcid ?? throw Unplayable(step, "the give names no NPC weenie class");
                    _ = step.Item?.Wcid ?? throw Unplayable(step, "the give names no item weenie class");
                    act = step.Place is { } leaves && leaves.Cell >> 16 != quest.Start.Cell >> 16 ? AcademyAct.GiveExit : AcademyAct.Give;
                    break;
                case QuestStepKind.PickUp:
                    _ = step.Item?.Wcid ?? throw Unplayable(step, "the pick-up names no item weenie class");
                    act = step.Target is null ? AcademyAct.PickUpFloor : step.Target.Wcid is null ? throw Unplayable(step, "the pick-up's creature has no weenie class") : AcademyAct.KillAndLoot;
                    break;
                case QuestStepKind.Use:
                    _ = step.Target?.Wcid ?? throw Unplayable(step, "the use names no object weenie class");
                    act = step.Item is not null ? (step.Item.Wcid is null ? throw Unplayable(step, "the use's item (the key) has no weenie class") : AcademyAct.Unlock)
                        : step.Place is { } place && place.Cell >> 16 != quest.Start.Cell >> 16 ? AcademyAct.Exit : AcademyAct.UsePortal;
                    break;
                default:
                    throw Unplayable(step, $"TODO(m6-academy-step-{QuestStepKinds.Word(step.Kind)}): the Academy station has no act for this kind");
            }

            plan.Add(new AcademyStepPlan(act, step));
        }

        return plan;
    }

    /// <summary>The route a quest file is, for the verdict's <c>route=</c> (M6 0 F7): <c>full</c> for <c>academy</c>, else the key without its <c>academy-</c> prefix (<c>skip</c> for <c>academy-skip</c>).</summary>
    /// <param name="quest">The quest.</param>
    public static string RouteOf(Quest quest)
    {
        ArgumentNullException.ThrowIfNull(quest);
        return quest.Key == AcademyStation.FullRouteKey ? "full" : quest.Key.StartsWith("academy-", StringComparison.Ordinal) ? quest.Key["academy-".Length..] : quest.Key;
    }

    /// <summary>
    /// The cell <paramref name="fileCell"/> of the quest file in the Academy instance the character stands in: the Academy's layout is placed in several landblocks with the same cell low words (cache/m6/0/a3-facts.md), so a cell of the quest's start landblock is moved to
    /// <paramref name="landblock"/> by its low word; a cell of another landblock (Holtburg, the exit's arrival) is kept.
    /// </summary>
    /// <param name="fileCell">The quest file's cell.</param>
    /// <param name="startCell">The quest's start cell (<see cref="Quest.Start"/>).</param>
    /// <param name="landblock">The landblock the station started in (the high word of the start cell the character stood in).</param>
    public static uint Rebase(uint fileCell, uint startCell, uint landblock) =>
        fileCell >> 16 == startCell >> 16 ? (landblock << 16) | (fileCell & 0xFFFF) : fileCell;

    /// <summary>
    /// The start rule: null when <paramref name="at"/> is the quest's start cell by its low word (an EnvCell, low word 0x100 or more, in whichever Academy instance), else <c>cell-0x...</c> or <c>no-position</c>. A character in another room of the Academy (an earlier failed run) or out of it is not
    /// at the start: the cheat-free climber stops by name rather than be put back (the Academy's stamps cannot be read back by play).
    /// </summary>
    /// <param name="quest">The quest.</param>
    /// <param name="at">The player's position, or null.</param>
    public static string? Unplayable(Quest quest, CellFrame? at)
    {
        ArgumentNullException.ThrowIfNull(quest);
        if (at is not { } p)
        {
            return "no-position";
        }

        var low = p.CellId & 0xFFFF;
        return low >= 0x100 && low == (quest.Start.Cell & 0xFFFF) ? null : "cell-0x" + p.CellId.ToString("X8", CultureInfo.InvariantCulture);
    }

    private static InvalidOperationException Unplayable(QuestStep step, string why) =>
        new($"academy step {step.Index} ({QuestStepKinds.Word(step.Kind)}: {step.What}): {why}");
}

/// <summary>What the station does about an object its next act needs within a range (<see cref="QuestStepLeg.ReachOf"/>, M6 0 F4).</summary>
public enum AcademyReach
{
    /// <summary>The object is in range: nothing to do.</summary>
    InRange,

    /// <summary>The object is within the use reach: no walk, the act is sent and the server's move-to walks the rest.</summary>
    Use,

    /// <summary>The object is farther: the journey's runner walks first.</summary>
    Walk,
}

/// <summary>The Academy station's run (see <see cref="AcademyStation"/>); its step runners are the shared <see cref="QuestStepLeg"/>'s (M6 5 Q3, D56), its cells rebased to the instance it stands in (<see cref="Cell"/>).</summary>
public sealed class AcademyRun : QuestStepLeg
{
    /// <summary>The range the war spell is cast from, in metres (<see cref="QuestStepLeg.CastRange"/>; named here too, since the travel legs' fight-back cites the Academy's).</summary>
    public new const double CastRange = QuestStepLeg.CastRange;

    /// <summary>The seconds one kill may take (<see cref="QuestStepLeg.KillSeconds"/>; named here too, since the travel legs' fight-back cites the Academy's).</summary>
    public new const double KillSeconds = QuestStepLeg.KillSeconds;

    private readonly Quest _quest;
    private readonly IReadOnlyList<AcademyStepPlan> _plan;
    private readonly HashSet<uint> _floorWcids;
    private uint _landblock;
    private int _stepIndex;
    private CellFrame? _taken; // M6 0 F4b: where the walk to a portal left the player when the portal took it on touch (the next step is that portal's Use)
    private long _takenMark;
    private uint _takenWcid;
    private int _stepsDone;
    private long _xpStart;

    internal AcademyRun(JourneyContext context, Quest quest)
        : base("quest-" + quest.Key, context)
    {
        _quest = quest;
        _plan = AcademyPlan.Of(quest);
        _floorWcids = _plan.Where(p => p.Act == AcademyAct.PickUpFloor).Select(p => p.Step.Item!.Wcid!.Value).ToHashSet();
    }

    /// <summary>True when the exit's arrival in its place and its reply (the quest's done condition) were both seen.</summary>
    public bool AcademyDone { get; private set; }

    /// <summary>The steps played to their check.</summary>
    public int StepsDone => _stepsDone;

    /// <summary>The character's level at the start and now (its own property).</summary>
    public (int Start, int Now) Level { get; private set; }

    /// <inheritdoc />
    public override string VerdictFields() =>
        string.Create(CultureInfo.InvariantCulture, $"academy_done={Yes(AcademyDone)} route={AcademyPlan.RouteOf(_quest)} steps={_stepsDone}/{_plan.Count} worn={WornCount}/{_floorWcids.Count} kills={Kills} casts={Casts} level={Level.Start}->{Level.Now} xp=+{XpGained} recoveries={Recoveries} problems={Problems.Count} granted={Granted}")
        + (Failure is null ? string.Empty : " why=" + Failure.Replace(' ', '_'));

    private long XpGained => S is null ? 0 : S.State.Self.Qualities.TotalExperience - _xpStart;

    /// <inheritdoc />
    protected override ISet<uint> OwnPieces => _floorWcids;

    /// <inheritdoc />
    protected override IEnumerable<ActionHandle?> Script()
    {
        Watch();
        Stage = "start";
        var unplayable = AcademyPlan.Unplayable(_quest, S.State.Self.Position);
        Require(Verify(unplayable is null, $"start: the player is not at the Academy's start ({unplayable})"), "start: not at the Academy's start");
        _landblock = S.State.Self.Position!.Value.CellId >> 16;
        _xpStart = S.State.Self.Qualities.TotalExperience;
        Level = (S.State.Self.Qualities.Level, S.State.Self.Qualities.Level);
        Log($"start: the Academy instance 0x{_landblock:X4}, cell 0x{S.State.Self.Position!.Value.CellId:X8}, level {Level.Start}, experience {_xpStart}; {_plan.Count} steps: {string.Join(", ", _plan.Select(p => p.Act))}");

        foreach (var (p, i) in _plan.Select((p, i) => (p, i)))
        {
            Stage = string.Create(CultureInfo.InvariantCulture, $"{i}:{p.Act.ToString().ToLowerInvariant()}");
            _stepIndex = i;
            foreach (var h in DefendIfAttacked()) { yield return h; } // M5 D88: an attacker is fought before the next step
            if (p.Step.Todo is { } todo)
            {
                Log($"step {i} ({p.Act}: {p.Step.What}) carries {todo}"); // M6 0 F6: a step's named unknown is said before it is played (loud stubs)
            }

            var steps = p.Act switch
            {
                AcademyAct.Go => Go(p.Step),
                AcademyAct.Talk => Talk(p.Step),
                AcademyAct.Give => Give(p.Step),
                AcademyAct.PickUpFloor => PickUpFloor(p.Step),
                AcademyAct.KillAndLoot => KillAndLoot(p.Step),
                AcademyAct.UsePortal => UsePortal(p.Step),
                AcademyAct.Unlock => Unlock(p.Step),
                AcademyAct.GiveExit => GiveExit(p.Step),
                _ => Exit(p.Step),
            };
            foreach (var h in steps) { yield return h; }

            _stepsDone++;
            Level = (Level.Start, S.State.Self.Qualities.Level);
            Log(string.Create(CultureInfo.InvariantCulture, $"step {i} done ({p.Act}: {p.Step.What}); level {Level.Now}, experience +{XpGained}, cell 0x{S.State.Self.Position?.CellId ?? 0:X8}"));
        }

        Stage = "done-check";
        Level = (Level.Start, S.State.Self.Qualities.Level);
        Verify(AcademyDone, "done: the exit's arrival and its reply were not both seen");
        Log(string.Create(CultureInfo.InvariantCulture, $"the Academy by play: done={Yes(AcademyDone)}, level {Level.Start} -> {Level.Now} (the character's own property), experience +{XpGained}; route {AcademyPlan.RouteOf(_quest)}; DoD 2 names level 5: the level reached is reported, never assumed (TODO(m6-academy-sentry-percent) on the full route when it is lower)"));
    }

    // ---------------------------------------------------------------------------------------------- the steps

    private IEnumerable<ActionHandle?> Go(QuestStep step)
    {
        var place = step.Place!;
        LastPlace = place;
        var cell = Cell(place.Cell);
        var at = place.Point!.Value;
        var wcid = step.Target?.Wcid;
        var guid = wcid is { } w ? WorldObjectNear(w, cell, at, SearchMetres) : 0;
        var creature = guid != 0 && IsCreature(guid);
        var reach = creature ? CastRange : UseRange + 1;
        if (guid != 0 && Distance(guid) <= reach)
        {
            Log($"go: {step.What}: {step.Target!.Name} (0x{guid:X8}) is in reach already ({Distance(guid):0.0} m)");
            yield break;
        }

        // Check (where the player is), act (the walk, for the step's purpose: M5 D47), check (the target in the table near the point).
        Log(string.Create(CultureInfo.InvariantCulture, $"go: {step.What}: from cell 0x{S.State.Self.Position?.CellId ?? 0:X8} to 0x{cell:X8} at ({at.X:0.00}, {at.Y:0.00}, {at.Z:0.00}){(guid == 0 ? string.Empty : $" for 0x{guid:X8}")}"));
        var markAtGo = S.State.Chat.Total;
        var teleportsAtGo = Teleports; // M6 0 F6: a touch is a teleport the client saw since the walk began, never a distance alone
        var request = new TravelRequest(Destination(place.Name, cell, at), TravelPurpose.Cell, guid, guid == 0 ? 0 : creature ? CastRange - 3 : UseRange);
        if (wcid is { } touchWcid && NextStepUses(touchWcid))
        {
            // M6 0 lead (live round 6): the walk to a portal the next step uses can end in the portal's destination, and the walker then reports a failure from the landing (it replans
            // from a pocket it cannot leave). So the first walk is optional, the landing is checked before any recovery, and only a walk that failed WITHOUT a teleport is recovered.
            foreach (var h in TravelTo(request, step.What, optional: true)) { yield return h; }
            foreach (var h in Until(() => S.State.Self.Travel == SelfTravel.Present, PortalSeconds)) { yield return h; }

            if (S.State.Self.Position is { } landed && TouchTeleported(Teleports - teleportsAtGo, landed.CellId, landed.Origin, cell, at))
            {
                _taken = landed;
                _takenMark = markAtGo;
                _takenWcid = touchWcid;
                Log(string.Create(CultureInfo.InvariantCulture, $"go: {step.What}: the walk ended {Vector3.Distance(landed.Origin, at):0.0} m from the portal's point in cell 0x{landed.CellId:X8} ({landed.Origin.X:0.00}, {landed.Origin.Y:0.00}, {landed.Origin.Z:0.00}){(LastTravelFailure is null ? string.Empty : " (the walker reported from the landing: " + LastTravelFailure + ")")}: the portal took the player on touch; the next step's Use is done (taken=yes)"));
                yield break;
            }

            if (LastTravelFailure is not null)
            {
                // M6 0 F6 (live round 7): a walk that ended short with no teleport is a walk failure, never a touch: a door that stayed shut fails by name, any other refusal gets the one named recovery.
                var stood = S.State.Self.Position is { } shortOf ? Vector3.Distance(shortOf.Origin, at) : double.NaN;
                Log(string.Create(CultureInfo.InvariantCulture, $"go: {step.What}: the walk ended {stood:0.0} m from the portal's point with no touch (teleports since the walk began: {Teleports - teleportsAtGo}): a walk failure, {LastTravelFailure}"));
                RequireNoShutDoor(step.What, LastTravelFailure);
                foreach (var h in WalkWithRecovery(request, step.What)) { yield return h; }
            }
        }
        else
        {
            foreach (var h in WalkWithRecovery(request, step.What)) { yield return h; }
        }

        // Check after the walk (M6 0 F4b, live round 5): a portal takes the player on touch, so the walk to it can end in the portal's destination. When the next step uses this very portal, that is its Use done.
        if (wcid is { } portalWcid && NextStepUses(portalWcid))
        {
            if (Teleports == teleportsAtGo)
            {
                // M6 0 F6: the walk arrived beside the portal; the touch's teleport may follow the arrival by a moment, so it is waited for briefly (none: the next step Uses the portal).
                foreach (var h in Until(() => Teleports > teleportsAtGo, TouchSeconds)) { yield return h; }
            }

            foreach (var h in Until(() => S.State.Self.Travel == SelfTravel.Present, PortalSeconds)) { yield return h; }

            if (S.State.Self.Position is { } now && TouchTeleported(Teleports - teleportsAtGo, now.CellId, now.Origin, cell, at))
            {
                _taken = now;
                _takenMark = markAtGo;
                _takenWcid = portalWcid;
                Log(string.Create(CultureInfo.InvariantCulture, $"go: {step.What}: the walk ended {Vector3.Distance(now.Origin, at):0.0} m from the portal's point in cell 0x{now.CellId:X8} ({now.Origin.X:0.00}, {now.Origin.Y:0.00}, {now.Origin.Z:0.00}): the portal took the player on touch; the next step's Use is done (taken=yes)"));
                yield break;
            }
        }

        if (wcid is { } target)
        {
            foreach (var h in Until(() => WorldObjectNear(target, cell, at, SearchMetres) != 0, NpcSeconds)) { yield return h; }

            Verify(WorldObjectNear(target, cell, at, SearchMetres) != 0, $"go: {step.Target!.Name} ({target}) is not in the object table within {SearchMetres:0} m of {place} after the walk");
        }
    }

    private IEnumerable<ActionHandle?> UsePortal(QuestStep step)
    {
        var wcid = step.Target!.Wcid!.Value;
        CellFrame? arrival = null;
        foreach (var h in UseAndTeleport(step, wcid, a => arrival = a)) { yield return h; }

        Require(Verify(arrival is not null, $"use: no teleport away from {step.Target.Name} after the Use and the recovery"), "use: no teleport");
        Log(string.Create(CultureInfo.InvariantCulture, $"use: {step.Target.Name} teleported the player to cell 0x{arrival!.Value.CellId:X8} at ({arrival.Value.Origin.X:0.00}, {arrival.Value.Origin.Y:0.00}, {arrival.Value.Origin.Z:0.00})"));
        if (step.Place is { } landing)
        {
            // M6 0 F6: the landing the quest file names (the portal's instance link to its Portal Linkspot), in this Academy instance.
            Verify(arrival.Value.CellId == Cell(landing.Cell), $"use: {step.Target.Name} landed in cell 0x{arrival.Value.CellId:X8}, not in {landing.Name} (0x{Cell(landing.Cell):X8})");
        }
    }

    /// <summary>
    /// The key on a locked door (M6 0 F6): check (the key in the pack; the door in view; already unlocked or open is nothing to do), act (UseWithTarget, the key on the door: ACE walks the player to the door, <c>UnlockerHelper.UseUnlocker</c> clears its Locked property,
    /// broadcasts it and says "The {door} has been unlocked.", <c>Lock.cs:248-280</c> @95ef426d), check (the line or the door's Locked false within <see cref="QuestStepLeg.AfterSeconds"/>), with one named recovery (<c>recovery=unlock-again</c>).
    /// </summary>
    private IEnumerable<ActionHandle?> Unlock(QuestStep step)
    {
        var key = step.Item!.Wcid!.Value;
        var doorWcid = step.Target!.Wcid!.Value;
        var keyGuid = InPack(key).FirstOrDefault();
        Require(Verify(keyGuid != 0, $"unlock: the pack holds no {step.Item.Name} ({key})"), "unlock: no key");
        uint door = 0;
        foreach (var h in Approach(doorWcid, step.Target.Name, g => door = g)) { yield return h; }

        if (Unlocked(door))
        {
            Log($"unlock: {step.Target.Name} (0x{door:X8}) is unlocked or open already (locked {LockedText(door)}): no key used");
            yield break;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (attempt == 1)
            {
                Recoveries++;
                Log($"recovery=unlock-again: {step.Target.Name} is still locked {AfterSeconds:0} s after the key; walking up again and using the key once more");
                foreach (var h in Approach(doorWcid, step.Target.Name, g => door = g)) { yield return h; }
            }

            var mark = S.State.Chat.Total;
            var use = S.Actions.UseWithTarget(keyGuid, door, ActionSource.Host);
            yield return use;
            Log($"unlock: UseWithTarget {step.Item.Name} (0x{keyGuid:X8}) on {step.Target.Name} (0x{door:X8}) from {Distance(door):0.0} m: {Describe(use)}");
            foreach (var h in Until(() => (step.Expect.Reply is not null && Replied(step.Expect, mark)) || Unlocked(door), AfterSeconds)) { yield return h; }

            var replied = step.Expect.Reply is not null && Replied(step.Expect, mark);
            if (replied || Unlocked(door))
            {
                Log($"unlock: {step.Target.Name} unlocked: replied={Yes(replied)} locked={LockedText(door)}{Said(mark)}");
                yield break;
            }

            Warn(Stage, $"unlock: {step.Target.Name} is still locked after the key (attempt {attempt + 1}, locked {LockedText(door)}, {Describe(use)})", door, mark);
        }

        Require(Verify(false, $"unlock: {step.Target.Name} stayed locked after {step.Item.Name} and the recovery"), "unlock: still locked");
    }

    /// <summary>
    /// The Exit Token given back to Jonathan (M6 0 F7, D37), whose emote set ends in the server's own teleport to Holtburg (emote type 63; not a tool verb, and the station sends no place). Check before (the player is in the Academy's landblock, the token is in the pack, the NPC is
    /// in reach), act (Give, nothing else), check after (a teleport the client saw, <see cref="PlayerTeleportAnnounced"/> as <see cref="QuestStepLeg.TouchTeleported"/> counts it, the player in the landblock of the step's place, the NPC's reply, the token gone from the pack), with one named
    /// recovery (<c>recovery=give-again</c>) only when nothing happened (no teleport, no reply, the token still in the pack). The XP award follows the Tell, so the level and the experience are read after a pause.
    /// </summary>
    private IEnumerable<ActionHandle?> GiveExit(QuestStep step)
    {
        var wcid = step.Target!.Wcid!.Value;
        var item = step.Item!.Wcid!.Value;
        var place = step.Place!;
        var itemGuid = InPack(item).FirstOrDefault();
        Require(Verify(itemGuid != 0, $"give-exit: the pack holds no {step.Item.Name} ({item})"), "give-exit: no item");
        Require(Verify(S.State.Self.Position is { } before && before.CellId >> 16 == _landblock, $"give-exit: the player is not in the Academy (landblock 0x{_landblock:X4}) before the Give (cell 0x{S.State.Self.Position?.CellId ?? 0:X8})"), "give-exit: not in the Academy");
        uint npc = 0;
        foreach (var h in Approach(wcid, step.Target.Name, g => npc = g)) { yield return h; }

        var levelBefore = S.State.Self.Qualities.Level;
        var xpBefore = S.State.Self.Qualities.TotalExperience;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (attempt == 1)
            {
                Recoveries++;
                Log($"recovery=give-again: {step.Item.Name} is still in the pack, no teleport and no reply came {PortalSeconds:0} s after the Give; walking up again and giving once more");
                foreach (var h in Approach(wcid, step.Target.Name, g => npc = g)) { yield return h; }
            }

            var teleportsAtGive = Teleports;
            var mark = S.State.Chat.Total;
            var give = S.Actions.Give(npc, itemGuid, 1, ActionSource.Host);
            yield return give;
            Log($"give-exit: {step.Item.Name} (0x{itemGuid:X8}) to {step.Target.Name} (0x{npc:X8}) from cell 0x{S.State.Self.Position?.CellId ?? 0:X8}: {Describe(give)}");
            foreach (var h in Until(() => Teleports > teleportsAtGive && S.State.Self.Travel == SelfTravel.Present && S.State.Self.Position is { } now && now.CellId >> 16 == place.Cell >> 16, PortalSeconds)) { yield return h; }

            var arrived = Teleports > teleportsAtGive && S.State.Self.Position is { } there && there.CellId >> 16 == place.Cell >> 16;
            if (!arrived && !Replied(step.Expect, mark) && InPack(item).Contains(itemGuid))
            {
                Warn(Stage, $"give-exit: no teleport to {place}, no reply and the {step.Item.Name} is still in the pack {PortalSeconds:0} s after the Give (attempt {attempt + 1}, teleports {Teleports - teleportsAtGive})", npc, mark);
                continue;
            }

            foreach (var h in Pause(2)) { yield return h; }

            foreach (var h in Until(() => Replied(step.Expect, mark), AfterSeconds)) { yield return h; }

            var replied = Replied(step.Expect, mark);
            var taken = !InPack(item).Contains(itemGuid);
            Verify(arrived, $"give-exit: the player is not in {place} after the Give (cell 0x{S.State.Self.Position?.CellId ?? 0:X8}, teleports {Teleports - teleportsAtGive})", npc, mark);
            Verify(replied, $"give-exit: no '{step.Expect.Reply}' after the Give", npc, mark);
            Verify(taken, $"give-exit: the {step.Item.Name} is still in the pack after the Give", itemGuid, mark);
            foreach (var h in Pause(1)) { yield return h; } // the award follows the Tell (the emote set's order)

            AcademyDone = arrived && replied && (_quest.Done.Reply is null || Replied(new QuestExpect(_quest.Done.Reply, null, null), mark));
            if (AcademyDone)
            {
                Context.Facts.MarkQuestDone(_quest.Key); // the skip keeps no stamp (finalize_exit erases it): the level up job reads this fact (the ladder's quest rung)
            }

            Log(string.Create(CultureInfo.InvariantCulture, $"give-exit: arrived={Yes(arrived)} replied={Yes(replied)} taken={Yes(taken)} teleports=+{Teleports - teleportsAtGive} in cell 0x{S.State.Self.Position?.CellId ?? 0:X8}{Said(mark)}"));
            Log(string.Create(CultureInfo.InvariantCulture, $"give-exit: experience {xpBefore} -> {S.State.Self.Qualities.TotalExperience} (+{S.State.Self.Qualities.TotalExperience - xpBefore}), level {levelBefore} -> {S.State.Self.Qualities.Level} (the character's own property)"));
            yield break;
        }

        Require(Verify(false, $"give-exit: {step.Target.Name} took nothing, said nothing and teleported nobody after the Give and the recovery", itemGuid), "give-exit: refused");
    }

    private IEnumerable<ActionHandle?> Exit(QuestStep step)
    {
        var wcid = step.Target!.Wcid!.Value;
        var place = step.Place!;
        var mark = _taken is not null && _takenWcid == wcid ? _takenMark : S.State.Chat.Total; // taken on touch: the reply came during the walk
        CellFrame? arrival = null;
        foreach (var h in UseAndTeleport(step, wcid, a => arrival = a)) { yield return h; }

        foreach (var h in Until(() => Replied(step.Expect, mark), AfterSeconds)) { yield return h; }

        var arrived = arrival is { } a && a.CellId >> 16 == place.Cell >> 16;
        var replied = Replied(step.Expect, mark);
        Verify(arrived, $"exit: the player is not in {place} after {step.Target.Name} (cell 0x{S.State.Self.Position?.CellId ?? 0:X8})");
        Verify(replied, $"exit: no '{step.Expect.Reply}' after {step.Target.Name}", 0, mark);
        AcademyDone = arrived && replied && (_quest.Done.Reply is null || Replied(new QuestExpect(_quest.Done.Reply, null, null), mark));
        if (AcademyDone)
        {
            Context.Facts.MarkQuestDone(_quest.Key); // M6 0 F4: the Academy's exit erases its stamps, so the level up job reads this fact (the ladder's quest rung)
        }

        Log($"exit: arrived={Yes(arrived)} replied={Yes(replied)} in cell 0x{S.State.Self.Position?.CellId ?? 0:X8}{Said(mark)}");
    }

    // ---------------------------------------------------------------------------------------------- the walks and the reads

    private IEnumerable<ActionHandle?> UseAndTeleport(QuestStep step, uint wcid, Action<CellFrame> arrived)
    {
        if (_taken is { } taken && _takenWcid == wcid)
        {
            _taken = null;
            Log(string.Create(CultureInfo.InvariantCulture, $"use: {step.Target!.Name} took the player on touch during the walk (taken=yes), to cell 0x{taken.CellId:X8}: no Use is sent"));
            arrived(S.State.Self.Position ?? taken);
            yield break;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            uint portal = 0;
            if (attempt == 1)
            {
                Recoveries++;
                Log($"recovery=use-again: no teleport {PortalSeconds:0} s after using {step.Target!.Name}; walking up again and using it once more");
            }

            foreach (var h in Approach(wcid, step.Target!.Name, g => portal = g)) { yield return h; }

            var from = S.State.Self.Position!.Value;
            var portalAt = S.State.Objects.TryGet(portal, out var po) && po.Position is { } pp ? pp.Origin : from.Origin;
            var away = false;
            var teleportsAtUse = Teleports;
            var mark = S.State.Chat.Total;
            var use = S.Actions.Use(portal, ActionSource.Host);
            yield return use;
            Log($"use: {step.Target.Name} (0x{portal:X8}) from cell 0x{from.CellId:X8}: {Describe(use)}");
            foreach (var h in Until(
                () =>
                {
                    away |= S.State.Self.Travel != SelfTravel.Present || Teleports > teleportsAtUse;
                    return away && S.State.Self.Travel == SelfTravel.Present && S.State.Self.Position is { } now && (now.CellId >> 16 != from.CellId >> 16 || Vector3.Distance(now.Origin, portalAt) > 10);
                },
                PortalSeconds))
            {
                yield return h;
            }

            if (away && S.State.Self.Travel == SelfTravel.Present && S.State.Self.Position is { } there && (there.CellId >> 16 != from.CellId >> 16 || Vector3.Distance(there.Origin, portalAt) > 10))
            {
                foreach (var h in Pause(2)) { yield return h; }

                arrived(S.State.Self.Position ?? there);
                yield break;
            }

            Warn(Stage, $"use: no teleport after using {step.Target.Name} (attempt {attempt + 1}, travel {S.State.Self.Travel})", portal, mark);
        }
    }

    /// <summary>True when the step after the current one is a portal's Use (or the exit) of the object <paramref name="wcid"/>.</summary>
    private bool NextStepUses(uint wcid) => _stepIndex + 1 < _plan.Count && _plan[_stepIndex + 1] is { Act: AcademyAct.UsePortal or AcademyAct.Exit } next && next.Step.Target?.Wcid == wcid;

    /// <inheritdoc />
    protected override uint Cell(uint fileCell) => AcademyPlan.Rebase(fileCell, _quest.Start.Cell, _landblock);
}
