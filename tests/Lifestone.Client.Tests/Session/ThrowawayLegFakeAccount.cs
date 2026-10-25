using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Bus;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Types;
using Lifestone.Data.World;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// A scripted account for <see cref="ThrowawayLegScenario"/>'s tests: one object that is the session view (a real <see cref="GameState"/>, clock and event bus), the Action API
/// (select, delete, restore, enter, log off and quit take their time on the manual clock) and the screen-action seam (character creation with ACE's answers, the tools panel,
/// the character list), plus the purge. Every behaviour a failure test needs is a public knob. It is a model of what the leg expects of the game and ACE, not of either.
/// </summary>
internal sealed class ThrowawayLegFakeAccount : IGameSessionView, IActionApi, IThrowawayScreens
{
    public const string Loopback = "Lifestone Loopback";
    public const uint SmokeGuid = 0x50000001;
    public const uint LoopbackGuid = 0x50000002;
    public const uint AcademyCell = 0x860201AD;
    public const uint HoltburgCell = 0xA9B40019;

    private readonly ManualTimeProvider _time = new();
    private readonly EventBus _bus = new(NullLogSink.Instance);
    private readonly List<(double Due, Action Run)> _scheduled = [];
    private readonly HashSet<uint> _purged = [];
    private long _nextHandle = 1;
    private uint _nextGuid = 0x50000010;

    public ThrowawayLegFakeAccount(bool smoke = true)
    {
        Clock = new SessionClock(_time);
        State = new GameState { Phase = SessionPhase.CharacterSelect, DataCheck = DataCheckState.Complete };
        if (smoke)
        {
            Add(SmokeGuid, ThrowawayLegScenario.Smoke, pending: false);
        }

        Add(LoopbackGuid, Loopback, pending: false);
    }

    // ----------------------------------------------------------------------------------------------- knobs

    public double WhereMetres { get; set; } = 0.12;
    public long XpReturned { get; set; } = ThrowawayLegScenario.XpAmount;
    public uint PlaceCell { get; set; } = HoltburgCell;
    public bool PlaceSendsNoTeleport { get; set; }
    public bool NeverUnhides { get; set; }
    public bool AcceptTakenName { get; set; }
    public bool AcceptInvalidName { get; set; }
    public bool RefuseThrowawayName { get; set; }
    public bool EnterAsOtherName { get; set; }
    public bool RenameAfterArrival { get; set; }
    public bool DeleteDoesNothing { get; set; }
    public bool RestoreDoesNothing { get; set; }
    public bool PurgeRefused { get; set; }
    public bool PurgeUnknownVerb { get; set; }
    public bool PurgeRemovesNothingFromTheList { get; set; }

    /// <summary>How many stale expired rows of the same name the purge also finds after the visible one (each purge confirms, then the verb answers unknown-character).</summary>
    public int StaleRows { get; set; }

    /// <summary>The purge never answers unknown-character (it confirms every time).</summary>
    public bool PurgeNeverEnds { get; set; }

    /// <summary>The invalid name's refusal shows the dialog of a taken name.</summary>
    public bool InvalidNameSaysInUse { get; set; }
    public bool OpenCharGenNeverEnds { get; set; }
    public bool LogOffNeverReturns { get; set; }
    public bool FailToolOps { get; set; }

    // ----------------------------------------------------------------------------------------------- what happened

    public int XpPresses { get; private set; }
    public string? XpTarget { get; private set; }
    public int ScreenDeletes { get; private set; }
    public int ScreenRestores { get; private set; }
    public int Quits { get; private set; }
    public int Purges { get; private set; }
    public int ConfirmedPurges { get; private set; }
    private readonly Dictionary<uint, int> _purgeCounts = [];
    public int LogOffs { get; private set; }
    public List<string> Entered { get; } = [];
    public List<string> Deleted { get; } = [];
    public List<string> Finished { get; } = [];
    public bool ToolsOpen { get; private set; }

    public IReadOnlyList<ListedCharacter> Listed => [.. State.Characters.Entries.Select(c => new ListedCharacter(c.Name, c.PendingDeletion))];

    public CharacterSlot? Find(string name) => State.Characters.Entries.Cast<CharacterSlot?>().FirstOrDefault(c => string.Equals(c!.Value.Name, name, StringComparison.OrdinalIgnoreCase));

    public void Add(uint guid, string name, bool pending)
    {
        State.Characters.Mutable.Add(new CharacterSlot(guid, name, pending ? 1u : 0u));
        State.Characters.Version++;
    }

    // ----------------------------------------------------------------------------------------------- the session view

    public GameState State { get; }
    public IEventBus Events => _bus;
    public IActionApi Actions => this;
    public PlayerTools Tools => null!;
    public SessionClock Clock { get; }
    public ActionLog ActionLog { get; } = new();
    public Lifestone.Net.Diagnostics.LinkStats Link => default;

    /// <summary>Advances the clock and runs what came due.</summary>
    public void Tick(double seconds)
    {
        _time.Advance(TimeSpan.FromSeconds(seconds));
        var now = Clock.Now;
        foreach (var item in _scheduled.Where(s => s.Due <= now).OrderBy(s => s.Due).ToList())
        {
            _scheduled.Remove(item);
            item.Run();
        }
    }

    private void After(double seconds, Action run) => _scheduled.Add((Clock.Now + seconds, run));

    private ActionHandle Handle(ActionKind kind, ActionSource source, string summary, ActionOutcomeKind? done = ActionOutcomeKind.Confirmed)
    {
        var handle = new ActionHandle(_nextHandle++, kind, source, summary, Clock.Now);
        if (done is { } outcome)
        {
            handle.TryComplete(new ActionOutcome(outcome), Clock.Now, (_, _) => { });
        }

        return handle;
    }

    private void Complete(ActionHandle handle, ActionOutcomeKind kind, string? detail = null) =>
        handle.TryComplete(new ActionOutcome(kind, 0, detail), Clock.Now, (_, _) => { });

    private void Replace(uint guid, Func<CharacterSlot, CharacterSlot> change)
    {
        var list = State.Characters.Mutable;
        var index = list.FindIndex(c => c.Guid == guid);
        if (index >= 0)
        {
            list[index] = change(list[index]);
            State.Characters.Version++;
        }
    }

    // ----------------------------------------------------------------------------------------------- the Action API

    public ActionHandle Login(ActionSource source = ActionSource.Host) => Handle(ActionKind.Login, source, "login");

    public ActionHandle SelectCharacter(uint guid, ActionSource source)
    {
        State.Characters.SelectedGuid = guid;
        return Handle(ActionKind.SelectCharacter, source, "select");
    }

    /// <summary>The characters made through the Action API (M4 D43: only the purge caller; the leg creates the throwaway through the screens).</summary>
    public List<string> CreatedByApi { get; } = [];

    public ActionHandle CreateCharacter(CharacterCreateIntent intent, ActionSource source)
    {
        CreatedByApi.Add(intent.Name);
        Add(_nextGuid++, intent.Name, pending: false);
        return Handle(ActionKind.CreateCharacter, source, "create");
    }

    public ActionHandle DeleteCharacter(uint guid, ActionSource source)
    {
        var handle = Handle(ActionKind.DeleteCharacter, source, "delete", done: null);
        After(0.3, () =>
        {
            if (Find(guid) is { } slot)
            {
                Deleted.Add(slot.Name);
            }

            Replace(guid, c => c with { DeleteTimeout = 1 });
            Complete(handle, ActionOutcomeKind.Confirmed);
        });
        return handle;
    }

    private CharacterSlot? Find(uint guid) => State.Characters.Entries.Cast<CharacterSlot?>().FirstOrDefault(c => c!.Value.Guid == guid);

    public ActionHandle RestoreCharacter(uint guid, ActionSource source) => throw new NotSupportedException("the leg restores through the screens");

    public ActionHandle EnterWorld(uint guid, ActionSource source)
    {
        var handle = Handle(ActionKind.EnterWorld, source, "enter", done: null);
        var slot = Find(guid);
        if (slot is not { PendingDeletion: false } || State.Phase != SessionPhase.CharacterSelect)
        {
            Complete(handle, ActionOutcomeKind.Refused, "not a ready character at the list");
            return handle;
        }

        Entered.Add(slot.Value.Name);
        State.Phase = SessionPhase.EnteringWorld;
        After(0.5, () =>
        {
            State.Phase = SessionPhase.InWorld;
            State.Self.Guid = guid;
            State.Self.Name = EnterAsOtherName && slot.Value.Name == ThrowawayLegScenario.Throwaway ? "Someone Else" : slot.Value.Name;
            State.Self.Travel = SelfTravel.Arriving;
            State.Self.PhysicsState = PhysicsState.Hidden;
            State.Self.Position = new CellFrame(AcademyCell, default);
            Complete(handle, ActionOutcomeKind.Confirmed);
            if (!NeverUnhides)
            {
                After(1.5, () =>
                {
                    State.Self.Travel = SelfTravel.Present;
                    State.Self.PhysicsState = PhysicsState.None;
                });
            }
        });
        return handle;
    }

    public ActionHandle LoginComplete(ActionSource source = ActionSource.Client) => Handle(ActionKind.LoginComplete, source, "login complete");

    public ActionHandle Use(uint guid, ActionSource source) => throw new NotSupportedException();

    public ActionHandle QueryHealth(uint guid, ActionSource source) => throw new NotSupportedException();

    public ActionHandle RaiseAttribute(uint attribute, uint xpToSpend, ActionSource source) => throw new NotSupportedException();

    public ActionHandle RaiseVital(uint vital, uint xpToSpend, ActionSource source) => throw new NotSupportedException();

    public ActionHandle RaiseSkill(uint skill, uint xpToSpend, ActionSource source) => throw new NotSupportedException();

    public ActionHandle TrainSkill(uint skill, int creditsToSpend, ActionSource source) => throw new NotSupportedException();

    public ActionHandle Cast(uint spellId, ActionSource source = ActionSource.Host) => throw new NotSupportedException();

    public ActionHandle Cast(uint spellId, uint target, ActionSource source = ActionSource.Host) => throw new NotSupportedException();

    public ActionHandle SetCombatMode(CombatMode mode, ActionSource source = ActionSource.Host) => throw new NotSupportedException();

    public ActionHandle Wield(uint item, EquipMask location, ActionSource source = ActionSource.Host) => throw new NotSupportedException();

    public ActionHandle Unwield(uint item, ActionSource source = ActionSource.Host) => throw new NotSupportedException();

    public ActionHandle MoveToPack(uint item, uint container = 0, uint placement = 0, ActionSource source = ActionSource.Host) => throw new NotSupportedException();

    public ActionHandle MergeStack(uint from, uint to, int amount, ActionSource source = ActionSource.Host) => throw new NotSupportedException();

    public ActionHandle Suicide(ActionSource source = ActionSource.Host) => throw new NotSupportedException();

    public ActionHandle AddSpellFavorite(uint spellId, int index, int bar, ActionSource source = ActionSource.Host) => throw new NotSupportedException();
    public ActionHandle RemoveSpellFavorite(uint spellId, int bar, ActionSource source = ActionSource.Host) => throw new NotSupportedException();
    public ActionHandle Attack(uint target, AttackHeight height, float power, ActionSource source = ActionSource.Host) => throw new NotSupportedException();
    public ActionHandle MissileAttack(uint target, AttackHeight height, float accuracy, ActionSource source = ActionSource.Host) => throw new NotSupportedException();
    public ActionHandle CancelAttack(ActionSource source = ActionSource.Host) => throw new NotSupportedException();
    public ActionHandle OpenContainer(uint container, ActionSource source = ActionSource.Host) => throw new NotSupportedException();
    public ActionHandle CloseContainer(uint container, ActionSource source = ActionSource.Host) => throw new NotSupportedException();
    public ActionHandle PickUp(uint item, uint container = 0, int placement = 0, ActionSource source = ActionSource.Host) => throw new NotSupportedException();
    public ActionHandle Drop(uint item, ActionSource source = ActionSource.Host) => throw new NotSupportedException();
    public ActionHandle SplitToContainer(uint stack, uint container, int placement, int amount, ActionSource source = ActionSource.Host) => throw new NotSupportedException();
    public ActionHandle SplitTo3D(uint stack, int amount, ActionSource source = ActionSource.Host) => throw new NotSupportedException();
    public ActionHandle SplitToWield(uint stack, EquipMask location, int amount, ActionSource source = ActionSource.Host) => throw new NotSupportedException();
    public ActionHandle Give(uint target, uint item, int amount, ActionSource source = ActionSource.Host) => throw new NotSupportedException();
    public HealHandle UseWithTarget(uint item, uint target, ActionSource source = ActionSource.Host) => throw new NotSupportedException();
    public HealHandle Drink(uint item, ActionSource source = ActionSource.Host) => throw new NotSupportedException();
    public ActionHandle AddShortcut(int slot, uint item, ActionSource source = ActionSource.Host) => throw new NotSupportedException();
    public ActionHandle RemoveShortcut(int slot, ActionSource source = ActionSource.Host) => throw new NotSupportedException();
    public ActionHandle UseShortcut(int slot, ActionSource source = ActionSource.Host) => throw new NotSupportedException();
    public IdentifyHandle Identify(uint guid, ActionSource source = ActionSource.Host) => throw new NotSupportedException();

    public ActionHandle Talk(string text, ActionSource source) => throw new NotSupportedException();

    public ActionHandle Tell(string target, string text, ActionSource source) => throw new NotSupportedException();

    public ActionHandle LogOff(ActionSource source)
    {
        LogOffs++;
        var handle = Handle(ActionKind.LogOff, source, "log off", done: null);
        if (State.Phase != SessionPhase.InWorld)
        {
            Complete(handle, ActionOutcomeKind.Refused, "not in the world");
            return handle;
        }

        State.Phase = SessionPhase.LoggingOff;
        if (!LogOffNeverReturns)
        {
            After(0.5, () =>
            {
                State.Self.Guid = 0;
                State.Self.Name = null;
                State.Self.Travel = SelfTravel.NotInWorld;
                State.Characters.Mutable.RemoveAll(c => _purged.Contains(c.Guid));
                State.Characters.Version++;
                State.Phase = SessionPhase.CharacterSelect;
                Complete(handle, ActionOutcomeKind.Confirmed);
            });
        }

        return handle;
    }

    public void SetSessionOption(int word, uint mask, bool value)
    {
    }

    public ActionHandle Quit(ActionSource source)
    {
        Quits++;
        return Handle(ActionKind.Quit, source, "quit", ActionOutcomeKind.Sent);
    }

    public IMovementSink Movement => null!;

    public MoveIntents Move => null!;

    public bool KillSwitch { get; set; }

    public bool DryRun { get; set; }

    public string? BusyReason => null;

    /// <summary>The purge the leg asks for: the throwaway must be pending deletion, and it leaves the list at the next log off (as ACE sends the list then).</summary>
    public ToolHandle<CharPurgeResult> Purge(IGameSessionView session, string name)
    {
        Purges++;
        var handle = new ToolHandle<CharPurgeResult>(_nextHandle++, ActionKind.ToolCharPurge, ActionSource.Host, "purge", Clock.Now);
        if (PurgeUnknownVerb)
        {
            handle.TryComplete(null, new ActionOutcome(ActionOutcomeKind.ServerError, 0, "unknown-verb: the mod has no such verb"), Clock.Now, (_, _) => { });
        }
        else if (PurgeRefused || Find(name) is not { PendingDeletion: true } slot)
        {
            handle.TryComplete(null, new ActionOutcome(ActionOutcomeKind.ServerError, 0, "not-pending-deletion: not marked for deletion"), Clock.Now, (_, _) => { });
        }
        else if (!PurgeNeverEnds && _purgeCounts.GetValueOrDefault(slot.Guid) >= 1 + StaleRows)
        {
            handle.TryComplete(null, new ActionOutcome(ActionOutcomeKind.ServerError, 0, "unknown-character: no character named \"" + name + "\" is on your account"), Clock.Now, (_, _) => { });
        }
        else
        {
            _purgeCounts[slot.Guid] = _purgeCounts.GetValueOrDefault(slot.Guid) + 1;
            ConfirmedPurges++;
            if (!PurgeRemovesNothingFromTheList)
            {
                _purged.Add(slot.Guid);
            }

            handle.TryComplete(new CharPurgeResult(name, 1, 1, 14), new ActionOutcome(ActionOutcomeKind.Confirmed), Clock.Now, (_, _) => { });
        }

        return handle;
    }

    // ----------------------------------------------------------------------------------------------- the screens

    private sealed class TimedOp(int ticks, Action? effect, string? text, double number, string? failure) : ScreenOp
    {
        private int _ticks = ticks;

        public override void Advance(double now)
        {
            if (Done || --_ticks > 0)
            {
                return;
            }

            if (failure is not null)
            {
                Fail(failure);
                return;
            }

            effect?.Invoke();
            Complete(text, number);
        }
    }

    private sealed class NeverOp : ScreenOp
    {
    }

    private ScreenOp Op(int ticks, Action? effect = null, string? text = null, double number = double.NaN, string? failure = null) => new TimedOp(ticks, effect, text, number, failure);

    private static string? NeedPhase(GameState state, SessionPhase phase) => state.Phase == phase ? null : $"the session is at {state.Phase}, not {phase}";

    public ScreenOp OpenCharGenSummary() =>
        OpenCharGenNeverEnds ? new NeverOp() : Op(3, failure: NeedPhase(State, SessionPhase.CharacterSelect));

    public ScreenOp Finish(string name)
    {
        Finished.Add(name);
        if (name == ThrowawayLegScenario.Smoke && !AcceptTakenName)
        {
            return Op(2, text: "That name is already in use.");
        }

        if (name == ThrowawayLegScenario.InvalidName && !AcceptInvalidName)
        {
            return Op(2, text: InvalidNameSaysInUse ? "That name is already in use." : "That name is not allowed.");
        }

        if (name == ThrowawayLegScenario.Throwaway && RefuseThrowawayName)
        {
            return Op(2, text: "The name database is down.");
        }

        return Op(2, () =>
        {
            var guid = _nextGuid++;
            Add(guid, name, pending: false);
            After(0.2, () => EnterWorld(guid, ActionSource.Ui)); // character creation logs the new character in at once (D65)
        });
    }

    public ScreenOp DismissErrorDialog() => Op(1);

    public ScreenOp OpenToolsPanel() => Op(2, () => ToolsOpen = true, failure: FailToolOps ? "tool operations are set to fail" : NeedPhase(State, SessionPhase.InWorld));

    public ScreenOp CloseToolsPanel() => Op(1, () => ToolsOpen = false);

    public ScreenOp PressWhere() => Op(2, text: "server and client agree", number: WhereMetres);

    public ScreenOp PressAddXp(string expectedCharacter, long amount)
    {
        if (RenameAfterArrival)
        {
            State.Self.Name = "Someone Else";
        }

        if (!string.Equals(State.Self.Name, expectedCharacter, StringComparison.OrdinalIgnoreCase))
        {
            return Op(1, failure: "Add XP refused: the character in the world is not the expected one");
        }

        return Op(2, () =>
        {
            XpPresses++;
            XpTarget = State.Self.Name;
        }, text: "Added", number: XpReturned);
    }

    public ScreenOp PressPlace(string query) =>
        Op(2, () =>
        {
            if (PlaceSendsNoTeleport)
            {
                return;
            }

            _bus.Publish(new PlayerTeleportAnnounced(2));
            State.Self.Travel = SelfTravel.TeleportAnnounced;
            State.Self.PhysicsState = PhysicsState.Hidden;
            After(1.0, () =>
            {
                State.Self.Position = new CellFrame(PlaceCell, default);
                State.Self.Travel = SelfTravel.Present;
                State.Self.PhysicsState = PhysicsState.None;
            });
        });

    public ScreenOp SelectCharacter(string name)
    {
        if (Find(name) is not { } slot)
        {
            return Op(1, failure: "no such row");
        }

        return Op(1, () => State.Characters.SelectedGuid = slot.Guid);
    }

    public ScreenOp DeleteSelected() =>
        Op(3, () =>
        {
            ScreenDeletes++;
            if (DeleteDoesNothing)
            {
                return;
            }

            if (Find(State.Characters.SelectedGuid) is { } slot)
            {
                Deleted.Add(slot.Name);
            }

            Replace(State.Characters.SelectedGuid, c => c with { DeleteTimeout = 1 });
        });

    public ScreenOp RestoreSelected() =>
        Op(3, () =>
        {
            ScreenRestores++;
            if (!RestoreDoesNothing)
            {
                Replace(State.Characters.SelectedGuid, c => c with { DeleteTimeout = 0 });
            }
        });
}
