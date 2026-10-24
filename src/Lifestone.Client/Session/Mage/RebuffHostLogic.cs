using System.Globalization;
using System.Text;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.State;
using Lifestone.Data.Magic;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// Which characters have a buff profile (M4 4.1 lane c; M4 P5a lane C1, owner decision D33): BSD Mage (<see cref="ArenaProfile.Buffs"/>), Holtburg Brawler and Holtburg Archer (<see cref="Fighter.FighterProfile"/>). Holtburg Hunter has none
/// (it is left as it was built); any other character gets the "no buff profile" notice.
/// </summary>
public static class BuffProfiles
{
    /// <summary>True when the character named <paramref name="characterName"/> has a buff profile (the cycle and the driver know how to buff it).</summary>
    /// <param name="characterName">The character's name as the read model holds it, or null before it is known.</param>
    public static bool Has(string? characterName) => For(characterName) is not null;

    /// <summary>The buff profile of the character named <paramref name="characterName"/> (case-insensitive), or null for a character without one.</summary>
    /// <param name="characterName">The character's name as the read model holds it, or null before it is known.</param>
    public static IBuffProfile? For(string? characterName)
    {
        if (string.Equals(characterName, MageTemplate.CharacterName, StringComparison.OrdinalIgnoreCase))
        {
            return ArenaProfile.Buffs;
        }

        return Fighter.FighterProfile.ForName(characterName);
    }
}

/// <summary>
/// The Lifestone-labelled chat notices (M4 4.1 lane c): a client-composed line in the chat window (<see cref="ChatKind.Notice"/>, the way retail's own client lines such as the log-off line are added), whose text starts
/// with <see cref="Prefix"/> so the player can tell it is Lifestone's and not the server's. A <b>placeholder</b> only in the sense that retail has no auto-buff to speak about.
/// </summary>
public static class LifestoneNotices
{
    /// <summary>The text every Lifestone notice starts with.</summary>
    public const string Prefix = "[Lifestone] ";

    /// <summary>The chat sink of a session: a function that adds one notice line to the session's chat (a view without a real Action API has none: the lines are dropped, the callers log them too).</summary>
    /// <param name="session">The session view.</param>
    public static Action<string> ChatSink(IGameSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Actions is ActionApi api)
        {
            return text => api.ApplyLocal(SessionNotices.ClientChatLine, 0, Label(text));
        }

        return static _ => { };
    }

    /// <summary>The text with <see cref="Prefix"/> in front when it does not start with it.</summary>
    /// <param name="text">The notice text.</param>
    public static string Label(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.StartsWith(Prefix, StringComparison.Ordinal) ? text : Prefix + text;
    }
}

/// <summary>
/// Says what a <see cref="RebuffDriver"/> is doing in the chat (M4 4.1 lane c): "[Lifestone] re-buffing &lt;lines&gt;" when a pass starts and "[Lifestone] re-buffed &lt;n&gt; lines" (or "re-buff failed: &lt;why&gt;") when it ends.
/// The play host and the buff leg both use it, so the notice the owner reads in the chat window is the notice the review frame shows. <see cref="Observe"/> compares counters; strings are built when a pass starts or ends.
/// </summary>
public sealed class RebuffPassNotices
{
    /// <summary>The most line names a start notice spells out; the rest are counted.</summary>
    public const int MaxNamedLines = 6;

    private readonly RebuffDriver _driver;
    private readonly BuffCycle _cycle;
    private readonly double _margin;
    private readonly Action<string> _notice;
    private int _seenPasses;
    private bool _announced;

    /// <summary>Notices for <paramref name="driver"/>'s passes.</summary>
    /// <param name="driver">The driver watched.</param>
    /// <param name="cycle">The cycle it drives (asked once per pass which lines were due).</param>
    /// <param name="marginSeconds">The driver's margin (its options').</param>
    /// <param name="notice">Where a notice line goes (without the prefix: <see cref="LifestoneNotices.Label"/> adds it).</param>
    public RebuffPassNotices(RebuffDriver driver, BuffCycle cycle, double marginSeconds, Action<string> notice)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(cycle);
        ArgumentNullException.ThrowIfNull(notice);
        _driver = driver;
        _cycle = cycle;
        _margin = marginSeconds;
        _notice = notice;
        _seenPasses = driver.Passes;
    }

    /// <summary>The lines the last start notice named (the number of lines due when the pass began), for the leg's verdict.</summary>
    public int LastDueCount { get; private set; }

    /// <summary>Call right after every <see cref="RebuffDriver.Tick"/>. Allocation-free unless a pass has just started or ended.</summary>
    /// <param name="now">The session clock's time.</param>
    public void Observe(double now)
    {
        if (_driver.Passes != _seenPasses)
        {
            _seenPasses = _driver.Passes;
            _announced = true;
            _notice(LifestoneNotices.Label("re-buffing " + DueLines(now)));
        }

        if (_announced && _driver.State != RebuffDriverState.Running)
        {
            _announced = false;
            _notice(LifestoneNotices.Label(
                _driver.LastFailure is { } why
                    ? "re-buff failed: " + why
                    : string.Create(CultureInfo.InvariantCulture, $"re-buffed {_driver.LastResult?.Cast ?? 0} lines")));
        }
    }

    /// <summary>The names of the lines due at the pass's start (the cycle has cast nothing yet when this is called: the notice is made in the tick that started the pass).</summary>
    private string DueLines(double now)
    {
        var due = _cycle.RebuffDue(now, _margin);
        LastDueCount = due.Count;
        if (due.Count == 0)
        {
            return "(the lines that came due)";
        }

        var text = new StringBuilder();
        for (var i = 0; i < due.Count && i < MaxNamedLines; i++)
        {
            if (i > 0)
            {
                text.Append(", ");
            }

            text.Append(BuffCycle.ShortNameOf(due[i]));
        }

        if (due.Count > MaxNamedLines)
        {
            text.Append(CultureInfo.InvariantCulture, $" and {due.Count - MaxNamedLines} more");
        }

        return text.ToString();
    }
}

/// <summary>
/// The play host's logic for the re-buff driver (M4 4.1 lane c; the Godot shell <c>RebuffDriverHost</c> only loads the SpellTable, ticks this once a frame and forwards the key): the switch (<c>--buff-auto on</c> or the
/// Lifestone key), the character's profile, the driver and its cycle on the per-login <see cref="ArmourBuffLedger"/>, the chat notices, and the ledger's save at log-off. The class is engine-free so its rules are tested without Godot.
/// </summary>
/// <remarks>
/// <para><b>The switch.</b> It starts as <see cref="BuffOptions.Auto"/>. A character without a buff profile (<see cref="BuffProfiles.Has"/>) gets "[Lifestone] no buff profile for this character", once per login for the option and once per key press,
/// and the driver stays off. Turning it on or off says so in the chat. The driver is made when the character is in the world, has a profile, the switch is on and the SpellTable is in (<see cref="SetSpells"/>; <see cref="NeedsSpells"/>
/// tells the shell to load it). Turning it off lets a pass already under way finish.</para>
/// <para><b>The ledger.</b> The cycle and the driver share <c>ArmourBuffLedger.For(state)</c>; the <see cref="IArmourLedgerStore"/> (a file per character guid) is read by the cycle's seed after a login and written by <see cref="Save"/>
/// when the phase leaves the world (and from the shell's exit) (closed <c>TODO(m4-buff-armour-relog-time)</c>): ACE stops an item enchantment's clock while the owner is offline, so the seconds left at log-off are the seconds left at the
/// next login.</para>
/// <para><b>One-shot.</b> (M4 4.1 fix, review R3.) The saved seconds are read and cleared at the login of a profile character, whether or not the switch is on, and handed to the cycle's seed once; the ledger records the login's clock
/// (<see cref="ArmourBuffLedger.LoggedInAt"/>) so a restore counts the seconds played since. <see cref="Save"/> writes only when the driver was made this login and its ledger was seeded or cast into, so time played without the driver is never counted as kept.</para>
/// <para><b>Hand runs of the legs.</b> (M4 4.1 fix2b, review R14.) Only the play host and the buff leg consume the store. A <c>bsd-fight</c> or <c>bsd-death</c> leg run by hand without <c>-IsolateUserData</c> plays the profile character
/// online without clearing it, so the next play login would restore seconds overstated by that run. Automated runs always isolate the user data and never reach this; a hand run of a leg must too (<c>tools\godot.ps1 -IsolateUserData</c>):
/// <c>TODO(m4-buff-ledger-hand-legs)</c> (clearing the store at every leg's login is the alternative, not built).</para>
/// <para><b>Per frame</b> <see cref="Tick"/> compares the phase and a few fields and ticks the driver; it allocates only when a state changes (a login, a log-off, a pass).</para>
/// </remarks>
public sealed class RebuffHostLogic : IDisposable
{
    /// <summary>The notice when the switch is turned on.</summary>
    public const string OnNotice = "auto-buff on";

    /// <summary>The notice when the switch is turned off.</summary>
    public const string OffNotice = "auto-buff off";

    /// <summary>The notice for a character without a buff profile (the driver stays off).</summary>
    public const string NoProfileNotice = "no buff profile for this character";

    private readonly BuffOptions _options;
    private readonly ILogSink _log;
    private readonly IArmourLedgerStore? _store;
    private IReadOnlyDictionary<uint, SpellInfo>? _spells;
    private bool _want;
    private bool _bound;
    private bool _hasProfile;
    private IBuffProfile? _profile;
    private string? _characterName;
    private uint _guid;
    private BuffCycle? _cycle;
    private RebuffDriver? _driver;
    private RebuffPassNotices? _notices;
    private Action<string>? _notice;
    private ArmourBuffLedger? _ledger;
    private IArmourLedgerStore? _cycleStore;

    /// <summary>The logic for a play session.</summary>
    /// <param name="options">The buff options (<see cref="BuffOptions.Auto"/> is the switch's start, the margin and the check interval the driver's).</param>
    /// <param name="log">Where the host logs (<c>[buff] ...</c>).</param>
    /// <param name="store">Where the armour ledger is kept across a log-off, or null (the conservative rule then runs: a line found on a piece with no known time is due at once).</param>
    public RebuffHostLogic(BuffOptions options, ILogSink log, IArmourLedgerStore? store = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);
        _options = options with { Auto = true }; // the driver is made enabled; the host's switch decides
        _log = log;
        _store = store;
        _want = options.Auto;
    }

    /// <summary>True while the switch is on (the option, or the key).</summary>
    public bool On => _want;

    /// <summary>True while the character is in the world and has a buff profile (known once the character has been bound).</summary>
    public bool HasProfile => _bound && _hasProfile;

    /// <summary>True when the switch is on and the SpellTable has not been given: the shell loads it (on a worker) and calls <see cref="SetSpells"/>.</summary>
    public bool NeedsSpells => _want && _spells is null;

    /// <summary>The driver, once made (null before the world is entered, without a profile, or without the SpellTable).</summary>
    public RebuffDriver? Driver => _driver;

    /// <summary>The cycle the driver drives, once made.</summary>
    public BuffCycle? Cycle => _cycle;

    /// <summary>Gives the SpellTable's spells (the cycle's costs and durations).</summary>
    /// <param name="spells">The spells.</param>
    public void SetSpells(IReadOnlyDictionary<uint, SpellInfo> spells)
    {
        ArgumentNullException.ThrowIfNull(spells);
        _spells = spells;
    }

    /// <summary>The SpellTable could not be read: the switch goes off, and the chat and the log say why (a loud failure, never a driver that silently does nothing).</summary>
    /// <param name="why">The reason.</param>
    public void SpellsFailed(string why)
    {
        _log.Error(BuffCycle.LogCategory, "auto-buff: the SpellTable could not be read, so the driver cannot run: " + why);
        _want = false;
        _notice?.Invoke(LifestoneNotices.Label("auto-buff is off: the spell table could not be read"));
    }

    /// <summary>The Lifestone key: flips the switch and says so in the chat; a character without a profile gets the notice and stays off.</summary>
    /// <param name="session">The session.</param>
    public void Toggle(IGameSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Bind(session);
        if (!_bound)
        {
            _log.Warn(BuffCycle.LogCategory, "auto-buff key: pressed outside the world, ignored");
            return;
        }

        if (!_hasProfile)
        {
            _want = false;
            Say(NoProfileNotice);
            return;
        }

        _want = !_want;
        _log.Info(BuffCycle.LogCategory, "auto-buff: the key turned it " + (_want ? "on" : "off"));
        Say(_want ? OnNotice : OffNotice);
        if (_driver is not null)
        {
            _driver.Enabled = _want;
        }
    }

    /// <summary>Once per frame: binds the character when it arrives, makes the driver when everything is there, ticks it and its notices, and saves the ledger when the world is left.</summary>
    /// <param name="session">The session.</param>
    public void Tick(IGameSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.State.Phase != SessionPhase.InWorld)
        {
            if (_bound)
            {
                Unbind(session, "the world was left");
            }

            return;
        }

        if (!_bound)
        {
            Bind(session);
            if (!_bound)
            {
                return;
            }
        }

        if (!_hasProfile || (!_want && _driver is null))
        {
            return;
        }

        if (_driver is null)
        {
            if (_spells is null)
            {
                return; // the shell is loading the SpellTable (NeedsSpells)
            }

            Make(session);
        }

        var driver = _driver!;
        driver.Enabled = _want;
        var now = session.Clock.Now;
        driver.Tick(now);
        _notices!.Observe(now);
    }

    /// <summary>Keeps the ledger's remaining seconds in the store (the world was left, or the game is closing); nothing without a cycle or a store.</summary>
    /// <param name="session">The session (its clock gives the time of the log-off).</param>
    public void Save(IGameSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_store is null || _ledger is null || _guid == 0)
        {
            return; // no store, or the driver never ran this login: nothing is written (review R3), so time played without the driver is never counted as kept
        }

        if (!_ledger.Seeded && _ledger.Count == 0)
        {
            _log.Info(BuffCycle.LogCategory, "armour memory: the ledger was never seeded or cast into this login, so nothing is kept");
            return;
        }

        _store.Save(_guid, _ledger.Export(_guid, session.Clock.Now));
        _log.Info(BuffCycle.LogCategory, "armour memory: the ledger's remaining seconds are kept for the next login");
    }

    /// <summary>Unsubscribes the driver.</summary>
    public void Dispose()
    {
        _driver?.Dispose();
        _driver = null;
    }

    private void Bind(IGameSessionView session)
    {
        if (_bound || session.State.Phase != SessionPhase.InWorld)
        {
            return;
        }

        var self = session.State.Self;
        if (self.Travel != SelfTravel.Present || self.Guid == 0 || self.Name is null)
        {
            return;
        }

        _bound = true;
        _guid = self.Guid;
        _profile = BuffProfiles.For(self.Name);
        _characterName = self.Name;
        _hasProfile = _profile is not null;
        _cycleStore = _store;
        if (_hasProfile)
        {
            var ledger = ArmourBuffLedger.For(session.State);
            if (double.IsNaN(ledger.LoggedInAt))
            {
                ledger.LoggedInAt = session.Clock.Now; // the restore counts the seconds played from here: ACE's clock runs while the character is online
            }

            if (_store is not null)
            {
                // One-shot (review R3): the saved seconds are read and cleared at the login, whether or not the switch is on, so a session played without the driver cannot leave them to a later login.
                var text = _store.Load(self.Guid);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    _store.Save(self.Guid, ArmourBuffLedger.EmptySnapshot(self.Guid));
                    _cycleStore = new CarriedStore(_store, text);
                }
            }
        }

        _notice = LifestoneNotices.ChatSink(session);
        _log.Info(BuffCycle.LogCategory, "auto-buff: the character is in the world; " + (_hasProfile ? "it has a buff profile" : "it has no buff profile") + "; the switch is " + (_want ? "on" : "off"));
        if (!_hasProfile && _want)
        {
            _want = false;
            Say(NoProfileNotice);
        }
        else if (_hasProfile && _want)
        {
            Say(OnNotice);
        }
    }

    private void Make(IGameSessionView session)
    {
        _ledger = ArmourBuffLedger.For(session.State);
        _cycle = new BuffCycle(session, _spells!, _options, _log, _ledger, _cycleStore, _profile);
        _driver = new RebuffDriver(_cycle, session.Actions, session, _options, _log, Fighter.CasterSwap.ForCharacter(_characterName, session.Actions, session.State, _log)); // M4 P5a C1b: a fighter swaps its weapon for the caster around a pass; the mage gets none
        _notices = new RebuffPassNotices(_driver, _cycle, _options.RebuffMarginSeconds, _notice ?? LifestoneNotices.ChatSink(session));
    }

    private void Unbind(IGameSessionView session, string why)
    {
        _log.Info(BuffCycle.LogCategory, "auto-buff: " + why + "; the driver stops");
        Save(session);
        _driver?.Dispose();
        _driver = null;
        _cycle = null;
        _profile = null;
        _notices = null;
        _ledger = null;
        _cycleStore = null;
        _notice = null;
        _bound = false;
        _guid = 0;
    }

    /// <summary>The snapshot read at the login, given to the cycle's seed once; saves go straight to the store.</summary>
    private sealed class CarriedStore(IArmourLedgerStore inner, string text) : IArmourLedgerStore
    {
        private string? _text = text;

        public string? Load(uint characterGuid)
        {
            var t = _text;
            _text = null;
            return t;
        }

        public void Save(uint characterGuid, string json) => inner.Save(characterGuid, json);
    }

    private void Say(string text)
    {
        _log.Info(BuffCycle.LogCategory, "auto-buff notice: " + text);
        _notice?.Invoke(LifestoneNotices.Label(text));
    }
}
