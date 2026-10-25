using System.Globalization;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// Counts the moments the player's combat mode and its wielded weapon disagree (M4 3.1 g; the owner's rule: a character is never in melee mode with a wand or an orb, in magic mode without a caster, or in missile mode without a
/// launcher). A bus subscriber in the pattern of <see cref="FightTally"/>: <see cref="Start"/> subscribes and judges the state it finds, <see cref="Dispose"/> unsubscribes, and no handler allocates. It counts <i>whoever</i> caused the
/// disagreement: our own send, ACE's revert, a key of the human's.
/// </summary>
/// <remarks>
/// <para><b>What is a disagreement.</b> The rules are <see cref="CombatModeGuard"/>'s: <see cref="CombatModeProblem.MeleeWithCaster"/>, <see cref="CombatModeProblem.MeleeWithLauncher"/>, <see cref="CombatModeProblem.MagicWithoutCaster"/>
/// and <see cref="CombatModeProblem.MissileWithoutLauncher"/>. The state is judged after each event that can change it: <see cref="CombatModeChanged"/>, every <see cref="InventoryChanged"/>, the player's whole description
/// (<see cref="QualitiesChanged"/> of kind Description) and the CreateObject of a wielded item (a weapon whose object arrives after its wield). A disagreement is counted <b>once per change</b>: the same
/// (problem, mode, item) seen again by the next event is the same disagreement, and one that ends (the mode or the weapon changes) and returns is counted again. A weapon whose object the table lacks cannot be judged
/// (<see cref="CombatModeProblem.CannotCheck"/>): it is no disagreement, and is counted apart in <see cref="Unchecked"/>, never guessed. A mode value of 0 (the server has said nothing yet) reads as peace.</para>
/// <para><b>Judged by the hunt.</b> The watch itself judges nothing: the legs print <see cref="Field"/> (<c>mode_mismatch=&lt;n&gt;</c>) beside their verdict fields. Since M4 4.2 J1 the journey's hunt station runs one (<see cref="HuntScenario.ModeWatch"/>, both hosts)
/// and <see cref="HuntLegVerdict.Compose"/> judges <c>mode_mismatch=0</c> (D23's rule; m4-plan.md section 4.9 row j).</para>
/// </remarks>
public sealed class CombatModeWatch : IDisposable
{
    /// <summary>The name of the verdict field.</summary>
    public const string FieldName = "mode_mismatch";

    private readonly List<IDisposable> _subscriptions = [];
    private IGameSessionView? _view;
    private CombatModeProblem _lastProblem;
    private int _lastMode;
    private uint _lastGuid;

    /// <summary>How many disagreements between the combat mode and the wielded weapon were seen, each counted once per change.</summary>
    public int Violations { get; private set; }

    /// <summary>How many times a wielded weapon's object was missing from the table while the mode was not peace, so the state could not be judged (neither counted in <see cref="Violations"/> nor guessed).</summary>
    public int Unchecked { get; private set; }

    /// <summary>The first disagreement as a text with the mode, the item and the session clock ("t=12.3s: melee mode while wielding the Wand (a caster)"), or null while none was seen.</summary>
    public string? FirstViolation { get; private set; }

    /// <summary>The verdict field, <c>mode_mismatch=&lt;n&gt;</c>.</summary>
    public string Field => FieldName + "=" + Violations.ToString(CultureInfo.InvariantCulture);

    /// <summary>True between <see cref="Start"/> and <see cref="Dispose"/>.</summary>
    public bool Running => _view is not null;

    /// <summary>Subscribes to the bus and judges the state it finds (a disagreement already present counts once); calling it again re-subscribes and keeps the counts and the memory of the last disagreement (the same one is not counted twice).</summary>
    /// <param name="view">The session.</param>
    public void Start(IGameSessionView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        Dispose();
        _view = view;
        _subscriptions.Add(view.Events.Subscribe<CombatModeChanged>(OnCombatMode));
        _subscriptions.Add(view.Events.Subscribe<InventoryChanged>(OnInventory));
        _subscriptions.Add(view.Events.Subscribe<QualitiesChanged>(OnQualities));
        _subscriptions.Add(view.Events.Subscribe<ObjectCreated>(OnCreated));
        Judge();
    }

    /// <summary>Unsubscribes; the counts stay readable.</summary>
    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        _view = null;
    }

    /// <summary>Zeroes the counts and forgets the last disagreement; the subscriptions stay, and the state now is judged again.</summary>
    public void Reset()
    {
        Violations = 0;
        Unchecked = 0;
        FirstViolation = null;
        _lastProblem = CombatModeProblem.None;
        Judge();
    }

    private void OnCombatMode(in CombatModeChanged e) => Judge();

    private void OnInventory(in InventoryChanged e) => Judge();

    private void OnQualities(in QualitiesChanged e)
    {
        if (e.Change == QualityChange.Description)
        {
            Judge();
        }
    }

    private void OnCreated(in ObjectCreated e)
    {
        if (_view is { } view && view.State.Self.Inventory.TryGetEquipped(e.Guid, out _))
        {
            Judge();
        }
    }

    /// <summary>The check every event runs: judges the state now (internal for the allocation test).</summary>
    internal void Judge()
    {
        if (_view is not { } view)
        {
            return;
        }

        var state = view.State;
        var mode = state.Self.Qualities.CombatMode;
        if (mode == 0)
        {
            mode = CombatModes.NonCombat;
        }

        var wielded = CombatModeGuard.Read(state);
        var problem = CombatModeGuard.Judge(mode, in wielded);
        if (problem is CombatModeProblem.None or CombatModeProblem.NotAMode)
        {
            _lastProblem = CombatModeProblem.None;
            return;
        }

        var culprit = CombatModeGuard.Culprit(problem, in wielded);
        var guid = problem == CombatModeProblem.CannotCheck ? wielded.Unknown : culprit?.Guid ?? 0;
        if (problem == _lastProblem && mode == _lastMode && guid == _lastGuid)
        {
            return; // the same disagreement the last event saw
        }

        _lastProblem = problem;
        _lastMode = mode;
        _lastGuid = guid;
        if (problem == CombatModeProblem.CannotCheck)
        {
            Unchecked++;
            return;
        }

        Violations++;
        FirstViolation ??= Text(view.Clock.Now, problem, mode, culprit);
    }

    private static string Text(double clock, CombatModeProblem problem, int mode, WorldObject? culprit)
    {
        var name = culprit is null ? "nothing" : "the " + culprit.Name;
        var what = problem switch
        {
            CombatModeProblem.MeleeWithCaster => "melee mode while wielding " + name + " (a caster)",
            CombatModeProblem.MeleeWithLauncher => "melee mode while wielding " + name + " (a missile launcher)",
            CombatModeProblem.MagicWithoutCaster => "magic mode with no caster wielded (wielding " + name + ")",
            CombatModeProblem.MissileWithoutLauncher => "missile mode with no missile launcher wielded (wielding " + name + ")",
            _ => "mode " + mode.ToString(CultureInfo.InvariantCulture) + " with " + problem,
        };
        return "t=" + clock.ToString("0.0", CultureInfo.InvariantCulture) + "s: " + what;
    }
}
