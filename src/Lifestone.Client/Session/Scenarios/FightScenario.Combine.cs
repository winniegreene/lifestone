using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 14 combine (the owner's ruling, 2026-11-17: she can also combine the motes into nuggets and bars to save pack space): in a hunt-until whose quest names a no-roll combine chain for the hunted item (<see cref="FightUntil.Chain"/>,
/// <see cref="QuestCombineChain"/>: the Atlan wand's motes to slivers, slivers to nuggets, nuggets to bars, recipes 790, 792 and 791, each skill 0 and difficulty 0, chance 1.0, RecipeManager.cs:151-164 @95ef426d), she combines every pair she
/// holds of a link at a safe moment by the fight's own rules: between kills (the choice, no loot or kill notice in flight, no stop asked), no attacker on her and no living hostile within the awake reach (<see cref="PackPeakReachMetres"/>).
/// ACE crafts in peace only (<c>allow_combat_mode_crafting</c> false, RecipeManager.cs:51-57 @95ef426d), so she leaves the magic mode for the combines and enters it again after. Each combine is check, act, check: two objects of the link held,
/// one <see cref="IActionApi.UseRecipe"/>, then the pair gone and one result arrived, logged with the slots freed and the burden. A hostile that comes into reach ends the combines and she fights; a combine that does not show its result in
/// <see cref="CombineAnswerSeconds"/> sets them aside for the fight, named. The roll step of the chain (the ingot, Alchemy against 150) is never a link and never runs here.
/// </summary>
public sealed partial class FightScenario
{
    /// <summary>The seconds a combine's result is waited for (Lifestone's own: a recipe answers in one round trip).</summary>
    public const double CombineAnswerSeconds = 5;

    /// <summary>The seconds after a combine run was cut by a hostile before another is begun (Lifestone's own: not a flip of the mode every frame at the reach's edge).</summary>
    public const double CombineRetrySeconds = 10;

    /// <summary>
    /// M6 14 combine2 (live 2026-11-17 13:38:37, ladder61: a combine sent 117 ms after the corpse's last pick-up was refused 0x001D, ACE's <c>YoureTooBusy</c>): ACE refuses a recipe while the player is busy (RecipeManager.cs:45-48 @95ef426d),
    /// and a pick-up keeps her busy through its return animation after the item is placed (<c>EnqueuePickupDone</c>, Player_Inventory.cs:765-780), as a cast does to its end (MagicState.OnCastStart, :128) and a recipe to its own. The check before: no
    /// combine begins within <see cref="CombineSettleSeconds"/> of the loot step's last work (the cast's busy wait, <see cref="Casting.CastRules.DefaultBusyWaitSeconds"/>); a busy answer still left is the moment's, not the recipe's: it is sent again
    /// after the same wait, at most <see cref="CombineBusyRetries"/> times, each logged, before the combines are set aside.
    /// </summary>
    public const double CombineSettleSeconds = Casting.CastRules.DefaultBusyWaitSeconds;

    /// <summary>The busy answers a combine is sent again after (the loot drops' bound, <see cref="LootBusyRetries"/>).</summary>
    public const int CombineBusyRetries = LootBusyRetries;

    private double _lootStepAt = double.NegativeInfinity;
    private int _combineBusy;
    private double _combineRetryUseAt = double.NegativeInfinity;

    private enum CombineStage
    {
        ToPeace,
        Pick,
        Waiting,
        ToMagic,
    }

    private CombineStage _combineStage;
    private ActionHandle? _combineHandle;
    private double _combineDeadline;
    private double _combineRetryAt = double.NegativeInfinity;
    private bool _combineOff;
    private int _combines;
    private (int Link, int SourceBefore, int ResultBefore, int SlotsBefore, int? BurdenBefore, string Source, string Result) _combineUse;
    private readonly List<uint> _combineScratch = [];

    /// <summary>The combines this fight made (a test reads it).</summary>
    internal int Combines => _combines;

    /// <summary>The fight's until counted along its chain: the hunted item's equivalents (the item itself with no chain).</summary>
    private long UntilUnits(GameState state, FightUntil until) =>
        until.Chain.Count < 2 ? GearInventory.CountOf(state, _untilScratch, until.ItemWcid) : ChainUnits(state, until.Chain); // the closure only with a chain: the until is read every frame

    private long ChainUnits(GameState state, IReadOnlyList<uint> chain) => QuestCombineChain.Units(chain, w => GearInventory.CountOf(state, _untilScratch, w));

    /// <summary>The chain's counts held, named, for a log line (its own method: a captured local in a per-frame method allocates at every entry).</summary>
    private string ChainHeld(GameState state, IReadOnlyList<uint> chain) => QuestCombineChain.Describe(chain, w => GearInventory.CountOf(state, _untilScratch, w), NameOf);

    /// <summary>The lowest link of the chain whose pair she holds (two objects of its source), or -1.</summary>
    private int PairLink(GameState state, IReadOnlyList<uint> chain)
    {
        for (var i = 0; i < chain.Count - 1; i++)
        {
            if (Objects(state, chain[i]).Count >= QuestCombineChain.Pair)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The guids of the held objects of <paramref name="wcid"/> in her packs (not worn), into the scratch list.</summary>
    private List<uint> Objects(GameState state, uint wcid)
    {
        GearInventory.Collect(state, _combineScratch);
        _combineScratch.RemoveAll(g => !state.Objects.TryGet(g, out var o) || o.Weenie.WeenieClassId != wcid || o.Weenie.WielderId is > 0);
        return _combineScratch;
    }

    /// <summary>Why it is not safe to combine now, or null: an attacker on her, or a living hostile within the awake reach.</summary>
    private string? CombineUnsafe(IGameSessionView session)
    {
        if (_attackers.Count > 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{_attackers.Count} attacker(s) on her");
        }

        if (session.State.Self.Position is { } here && session.State.Objects.NearestCreature(PickWcids(session, in here), in here, (float)PackPeakReachMetres, null, _killed, out var target, session.Live))
        {
            return string.Create(CultureInfo.InvariantCulture, $"a living creature of the ground's pick 0x{target:X8} within {PackPeakReachMetres:0} m"); // between kills: one to fight comes first
        }

        var near = HostileReach.CountWithin(session, PackPeakReachMetres);
        return near > 0 ? string.Create(CultureInfo.InvariantCulture, $"{near} hostile(s) within {PackPeakReachMetres:0} m") : null;
    }

    /// <summary>At the choice, between kills: begins the combines when a pair is held and the moment is safe. Null when none begins (the choice goes on).</summary>
    private ScenarioStatus? BeginCombines(IGameSessionView session, double clock)
    {
        if (_options.Until is not { Chain.Count: > 1 } until || _combineOff || clock < _combineRetryAt || LootBusy || SplashPending || _killPendingAt >= 0 || _stopRequest is not null || clock - _lootStepAt < CombineSettleSeconds)
        {
            return null; // M6 14 combine2: and not within the settle after the loot step's last pick-up (ACE's busy through its return animation)
        }

        var state = session.State;
        var link = PairLink(state, until.Chain);
        if (link < 0 || CombineUnsafe(session) is not null)
        {
            return null;
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"combine: check before: {ChainHeld(state, until.Chain)} held, {UntilUnits(state, until)} of {until.Count} {NameOf(until.ItemWcid)} equivalents; a pair of a no-roll link, no attacker on her, no hostile within {PackPeakReachMetres:0} m, between kills: she leaves the magic mode to combine (ACE crafts in peace only)"));
        _phase = Phase.Combine;
        _combineStage = CombineStage.ToPeace;
        _combineHandle = session.State.Self.Qualities.CombatMode == CombatModes.NonCombat ? null : session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Automation);
        return ScenarioStatus.Running;
    }

    private readonly Dictionary<uint, string> _chainNames = [];

    /// <summary>A name for a weenie class of the chain: the name of a held object of it, kept once seen (the last mote combined still names the motes), else the weenie class id.</summary>
    private string NameOf(uint wcid)
    {
        if (_chainNames.TryGetValue(wcid, out var known))
        {
            return known;
        }

        if (_view is { } view)
        {
            foreach (var guid in Objects(view.State, wcid))
            {
                if (view.State.Objects.TryGet(guid, out var o) && o.Name is { Length: > 0 } name)
                {
                    _chainNames[wcid] = name;
                    return name;
                }
            }
        }

        return "weenie " + wcid.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The combine phase, one step a frame.</summary>
    private ScenarioStatus CombineStep(IGameSessionView session, double clock)
    {
        var until = _options.Until!;
        var state = session.State;
        if (_combineStage != CombineStage.ToMagic && CombineUnsafe(session) is { } why)
        {
            _log.Info(LogCategory, $"combine: {why}: the combines stop, back to the fight");
            _combineRetryAt = clock + CombineRetrySeconds;
            return ToMagic(session);
        }

        switch (_combineStage)
        {
            case CombineStage.ToPeace:
                if (_combineHandle is { Outcome: null })
                {
                    return ScenarioStatus.Running;
                }

                if (_combineHandle is { Outcome: { Succeeded: false } refused })
                {
                    _log.Warn(LogCategory, "combine: the peace mode was not entered: " + Describe(refused) + "; the combines are set aside for this fight");
                    _combineOff = true;
                    return ToMagic(session);
                }

                _combineStage = CombineStage.Pick;
                return ScenarioStatus.Running;

            case CombineStage.Pick:
                if (clock < _combineRetryUseAt)
                {
                    return ScenarioStatus.Running; // the busy wait before the use is sent again
                }

                var link = PairLink(state, until.Chain);
                if (link < 0)
                {
                    _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"combine: no pair left: {ChainHeld(state, until.Chain)}, {UntilUnits(state, until)} of {until.Count} {NameOf(until.ItemWcid)} equivalents; back to the magic mode"));
                    return ToMagic(session);
                }

                var pair = Objects(state, until.Chain[link]).Take(QuestCombineChain.Pair).ToArray();
                _combineUse = (link, GearInventory.CountOf(state, _untilScratch, until.Chain[link]), GearInventory.CountOf(state, _untilScratch, until.Chain[link + 1]), Quartermaster.PackView.MainPackFree(state), state.Self.Inventory.Burden, NameOf(until.Chain[link]), NameOf(until.Chain[link + 1]));
                _combineHandle = session.Actions.UseRecipe(pair[0], pair[1], ActionSource.Automation);
                _combineDeadline = clock + CombineAnswerSeconds;
                _combineStage = CombineStage.Waiting;
                return ScenarioStatus.Running;

            case CombineStage.Waiting:
                var u = _combineUse;
                var source = GearInventory.CountOf(state, _untilScratch, until.Chain[u.Link]);
                var result = GearInventory.CountOf(state, _untilScratch, until.Chain[u.Link + 1]);
                if (source == u.SourceBefore - QuestCombineChain.Pair && result == u.ResultBefore + 1)
                {
                    _combines++;
                    u = u with { Result = NameOf(until.Chain[u.Link + 1]) }; // the result's name, read now it is held (a first one was not before the use)
                    var slots = Quartermaster.PackView.MainPackFree(state);
                    var burden = state.Self.Inventory.Burden;
                    _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"combine: 2 {u.Source} -> {u.Result} (a no-roll link of the quest's chain): pack slots freed {slots - u.SlotsBefore} ({u.SlotsBefore} -> {slots} free), burden {(u.BurdenBefore is { } b ? b.ToString(CultureInfo.InvariantCulture) : "unread")} -> {(burden is { } a ? a.ToString(CultureInfo.InvariantCulture) : "unread")}; check after: {u.Source} {u.SourceBefore} -> {source}, {u.Result} {u.ResultBefore} -> {result}; {UntilUnits(state, until)} of {until.Count} {NameOf(until.ItemWcid)} equivalents"));
                    _combineStage = CombineStage.Pick;
                    return ScenarioStatus.Running;
                }

                if (_combineHandle is { Outcome: { Succeeded: false, Code: 0x001D } } && _combineBusy < CombineBusyRetries)
                {
                    _combineBusy++;
                    _combineRetryUseAt = clock + CombineSettleSeconds;
                    _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"combine: 2 {u.Source} -> {u.Result}: the server answered busy (0x001D YoureTooBusy: ACE refuses a recipe while she is busy, a pick-up's return animation or a cast still running, RecipeManager.cs:45-48 @95ef426d); nothing was used ({u.Source} {u.SourceBefore} -> {source}); sent again in {CombineSettleSeconds:0.#} s (busy {_combineBusy} of {CombineBusyRetries})"));
                    _combineStage = CombineStage.Pick;
                    return ScenarioStatus.Running;
                }

                if (_combineHandle is { Outcome: { Succeeded: false } failed } || clock > _combineDeadline)
                {
                    _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"combine: 2 {u.Source} -> {u.Result}: check after: {u.Source} {u.SourceBefore} -> {source}, {u.Result} {u.ResultBefore} -> {result} within {CombineAnswerSeconds:0} s ({(_combineHandle?.Outcome is { } o ? Describe(o) : "no answer")}): the combines are set aside for this fight (TODO(m6-combine-unanswered))"));
                    _combineOff = true;
                    return ToMagic(session);
                }

                return ScenarioStatus.Running;

            default:
                if (_combineHandle is { Outcome: null })
                {
                    return ScenarioStatus.Running;
                }

                if (_combineHandle is { Outcome: { Succeeded: false } back })
                {
                    _log.Warn(LogCategory, "combine: the magic mode was not entered again: " + Describe(back));
                }

                _combineHandle = null;
                _phase = Phase.Choose;
                return ScenarioStatus.Running;
        }
    }

    private ScenarioStatus ToMagic(IGameSessionView session)
    {
        _combineStage = CombineStage.ToMagic;
        _combineHandle = session.State.Self.Qualities.CombatMode == CombatModes.Magic ? null : session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Automation);
        return ScenarioStatus.Running;
    }
}
