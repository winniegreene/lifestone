using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// What a death did, counted from the player's own messages on the session bus (M3 3.2 s0; plan section 4.8; spec section 5.4, the "death" and "melee on the player" rows): the victim notice (GameEvent 0x01AC), the
/// Dead motion (F74C), the NumDeaths update (0x02CD, property 43), the vitae (spell 666), the enchantment purges (0x02C6, 0x0312), the corpse (F745 with <see cref="WorldObjectTable.CorpseFlag"/>), the death's teleport
/// (F751) and the arrival (the position after it), the lifestone protection (0x028A, WeenieError 0x0502), the vitals after the arrival (0x02E9; the judged share is read at the end of the respawn's vitals wait, <see cref="SampleVitalsAfterArrival"/>) and the lowest health share, with an embedded <see cref="FightTally"/> for the
/// blows taken. <see cref="Start"/> subscribes and takes the baselines; <see cref="Dispose"/> unsubscribes (the counters stay readable); <see cref="Reset"/> zeroes them. Nothing allocates per message.
/// </summary>
/// <remarks>
/// <para><b>ACE's order</b> (<c>Player_Death.cs</c> <c>OnDeath</c> 34-93, <c>Die</c> 150-242, <c>ThreadSafeTeleportOnDeath</c> 247-292): for a death by a monster the victim notice 0x01AC comes first (a suicide has none), then the Dead
/// motion F74C, the health update 0x02E9 to 0 and the NumDeaths update 0x02CD, the vitae 0x02C2 (DeathLevel and VitaeCpPool before it), the purge 0x02C6, and after the Dead animation plus 1 s the corpse F745 and the teleport F751
/// (the position after it is the arrival); 3 s after the arrival the vitals go to 75% (three 0x02E9). A blow while the lifestone protection holds is the 0x028A with WeenieError
/// <see cref="LifestoneProtectsYou"/> (<c>WeenieError.LifestoneMagicProtectsYou</c>, ACE.Entity <c>WeenieError.cs</c> line 968, sent by <c>HandleLifestoneProtection</c>, <c>Player_Death.cs</c> 913-917); ACE ends the protection with one
/// of two Magic chat lines (<see cref="Protection"/>).</para>
/// <para><b>The corpse.</b> Only between a death signal and that death's arrival (<see cref="CorpsesSeen"/>; the others are <see cref="StaleCorpses"/>). A CreateObject whose description flags contain <see cref="WorldObjectTable.CorpseFlag"/> and whose name is exactly "Corpse of " followed by the player's name (ordinal comparison against
/// <see cref="SelfState.Name"/>; ACE writes <c>$"{prefix} of {Name}"</c>, <c>Creature_Death.cs</c> line 496). A corpse of another name is not counted, so a tusker's corpse in the den does not read as the player's.</para>
/// <para><b>Deaths.</b> <see cref="Deaths"/> is the NumDeaths delta (a suicide counts; <see cref="VictimNotices"/> does not), so a run with a suicide and a tusker death reads <c>Deaths=2 VictimNotices=1</c>.</para>
/// </remarks>
public sealed class DeathTally : IDisposable
{
    /// <summary>ACE's <c>PropertyInt.NumDeaths</c> (43, <c>PropertyInt.cs</c> line 72), kept current by 0x02CD.</summary>
    public const uint NumDeathsProperty = 43;

    /// <summary>ACE's <c>WeenieError.LifestoneMagicProtectsYou</c> (0x0502).</summary>
    public const uint LifestoneProtectsYou = 0x0502;

    /// <summary>The spell id of the vitae (666, 0x029A; <see cref="EnchantmentRules.VitaeSpellId"/>).</summary>
    public const uint VitaeSpell = EnchantmentRules.VitaeSpellId;

    /// <summary>ACE's <c>ChatMessageType.Magic</c> (0x07, <c>ChatMessageType.cs</c> line 98): the type of the two lifestone-protection lines.</summary>
    public const uint MagicChatType = 7;

    /// <summary>ACE's line when the protection is dispelled by an action (<c>Player_Death.cs</c> 942, <c>LifestoneProtectionDispel</c>; every teleport does it, <c>Player_Location.cs</c> 702-703).</summary>
    public const string ProtectionDispelledLine = "Your actions have dispelled the Lifestone's magic!";

    /// <summary>ACE's line when the protection runs out after a minute (<c>Player_Death.cs</c> 934, <c>LifestoneProtectionTick</c>).</summary>
    public const string ProtectionExpiredLine = "You're no longer protected by the Lifestone's magic!";

    private const string CorpsePrefix = "Corpse of ";

    private readonly List<IDisposable> _subscriptions = [];
    private IGameSessionView? _view;
    private int _deathsBaseline;
    private bool _deathsBaselinePending;
    private bool _afterDeath;
    private bool _awaitingArrival;
    private bool _afterArrival;
    private bool _healthSeen;
    private bool _staminaSeen;
    private bool _manaSeen;
    private bool _vitalsSampled;

    /// <summary>An empty tally with its own <see cref="Fight"/>.</summary>
    public DeathTally()
    {
        Reset();
    }

    /// <summary>The embedded fight tally (started and reset with this one): the blows taken.</summary>
    public FightTally Fight { get; } = new();

    /// <summary>How many GameEvent 0x01AC VictimNotification arrived (a death by a monster; a suicide sends none).</summary>
    public int VictimNotices { get; private set; }

    /// <summary>How many times the player's own F74C carried the forward motion Dead.</summary>
    public int DeadMotions { get; private set; }

    /// <summary>The NumDeaths delta since <see cref="Start"/> (or <see cref="Reset"/>): every death, a suicide included.</summary>
    public int Deaths { get; private set; }

    /// <summary>The vitae as a percentage ((1 - the multiplier of spell 666) x 100, rounded); 0 when the registry holds none.</summary>
    public int VitaePercent { get; private set; }

    /// <summary>How many enchantment purges arrived (0x02C6, and 0x0312 for the harmful ones).</summary>
    public int Purges { get; private set; }

    /// <summary>
    /// How many corpses of the player (named "Corpse of &lt;the player's name&gt;") were created between a death signal and that death's arrival: the death's own corpse (ACE makes it after the Dead motion and before the
    /// teleport, <c>Player_Death.cs</c> 247-292). The den keeps the earlier runs' corpses and re-creates them for the client at every arrival, so a corpse outside that window is counted in <see cref="StaleCorpses"/> (M3 3.2 review R1).
    /// </summary>
    public int CorpsesSeen { get; private set; }

    /// <summary>How many CreateObjects of a corpse of the player came outside a death's window (an earlier death's corpse coming into view again; report-only, <c>corpses_stale=</c>).</summary>
    public int StaleCorpses { get; private set; }

    /// <summary>The guid of the last corpse counted in <see cref="CorpsesSeen"/>, or 0.</summary>
    public uint LastCorpseGuid { get; private set; }

    /// <summary>
    /// M4 2.2 s1: the cell and the position (global frame of the cell) the last counted corpse had in its CreateObject, the place the mage died (the object table forgets the corpse after the teleport away), or null until one
    /// was counted. The corpse trip teleports here.
    /// </summary>
    public CellFrame? LastCorpsePosition { get; private set; }

    /// <summary>M4 2.2 s1: the session clock's seconds when the last counted corpse's CreateObject (the F745) arrived, or NaN: the corpse's age is measured from it (ACE's rot floor, <c>PlayerCorpseMinRotSeconds</c>).</summary>
    public double LastCorpseAt { get; private set; } = double.NaN;

    /// <summary>How many times ACE said the lifestone protection was dispelled by an action (<see cref="ProtectionDispelledLine"/>).</summary>
    public int ProtectionDispels { get; private set; }

    /// <summary>How many times ACE said the lifestone protection had run out (<see cref="ProtectionExpiredLine"/>).</summary>
    public int ProtectionExpiries { get; private set; }

    /// <summary>
    /// How the last lifestone protection ended, from ACE's two Magic lines: <c>dispelled</c> (the first teleport after the respawn does it; this is what a live run sees), <c>expired</c> (60 s with no teleport) or
    /// <c>none</c> (neither line came). The last line wins. Report-only (<c>protection=</c>): the blow met by the protection (<see cref="ProtectedHits"/>) cannot occur on the live path, M3 3.2 review R2.
    /// </summary>
    public string Protection { get; private set; } = ProtectionNone;

    /// <summary>The <see cref="Protection"/> text when no line came.</summary>
    public const string ProtectionNone = "none";

    /// <summary>How many death teleports (the first F751 after a death) arrived.</summary>
    public int DeathTeleports { get; private set; }

    /// <summary>True once the position after a death teleport arrived.</summary>
    public bool HasArrival { get; private set; }

    /// <summary>The cell id of the arrival after the death (0 until <see cref="HasArrival"/>).</summary>
    public uint ArrivalCell { get; private set; }

    /// <summary>The frame of the arrival after the death (default until <see cref="HasArrival"/>).</summary>
    public AcFrame ArrivalFrame { get; private set; }

    /// <summary>How many blows met the lifestone protection (0x028A with WeenieError 0x0502).</summary>
    public int ProtectedHits { get; private set; }

    /// <summary>
    /// Health current over maximum, in percent, as the scenario read it at the end of the respawn's vitals wait (<see cref="SampleVitalsAfterArrival"/>); NaN until then. M4 2.2 fix R1: this is the value ACE's 75% restore
    /// has left by then, not the first 0x02E9 after the arrival (see <see cref="FirstHealthAfterArrivalPercent"/>).
    /// </summary>
    public double HealthAfterArrivalPercent { get; private set; }

    /// <summary>Stamina current over maximum, in percent, at the end of the respawn's vitals wait (<see cref="SampleVitalsAfterArrival"/>); NaN until then.</summary>
    public double StaminaAfterArrivalPercent { get; private set; }

    /// <summary>Mana current over maximum, in percent, at the end of the respawn's vitals wait (<see cref="SampleVitalsAfterArrival"/>); NaN until then.</summary>
    public double ManaAfterArrivalPercent { get; private set; }

    /// <summary>
    /// The vitals after the arrival as one share: the lowest of <see cref="HealthAfterArrivalPercent"/>, <see cref="StaminaAfterArrivalPercent"/> and <see cref="ManaAfterArrivalPercent"/>, each read from the qualities at the
    /// end of the respawn's vitals wait by <see cref="SampleVitalsAfterArrival"/>; NaN until then, so a partial restore never reads as a complete one. ACE restores each vital to 75% three seconds after the teleport
    /// (<c>Player_Death.cs</c> 262-282, <c>AddDelaySeconds(3.0)</c>), so the first 0x02E9 of a vital after the arrival can be a regeneration or a drain tick that came before it (M4 2.2 live round 1: <c>vitals_pct=0.0</c>
    /// from a stamina or mana update of 0 sent after 30 evades): the value at the end of the wait is the one the judge asked for (the live test's 60 to 90). Later blows, teleports and regeneration never move it; a new
    /// death's teleport clears it.
    /// </summary>
    public double VitalsAfterArrivalPercent =>
        _vitalsSampled ? Math.Min(HealthAfterArrivalPercent, Math.Min(StaminaAfterArrivalPercent, ManaAfterArrivalPercent)) : double.NaN;

    /// <summary>Health current over maximum, in percent, as of the first 0x02E9 for health after the arrival; NaN until one landed. Report-only (the respawn log line's <c>first health=</c>).</summary>
    public double FirstHealthAfterArrivalPercent { get; private set; }

    /// <summary>Stamina current over maximum, in percent, as of the first 0x02E9 for stamina after the arrival; NaN until one landed. Report-only.</summary>
    public double FirstStaminaAfterArrivalPercent { get; private set; }

    /// <summary>Mana current over maximum, in percent, as of the first 0x02E9 for mana after the arrival; NaN until one landed. Report-only.</summary>
    public double FirstManaAfterArrivalPercent { get; private set; }

    /// <summary>True once all three vitals had a 0x02E9 after the arrival (the three <c>First...AfterArrivalPercent</c> are set): the play-check's wait for ACE's restore on the stand-in.</summary>
    public bool FirstVitalsSeen => _healthSeen && _staminaSeen && _manaSeen;

    /// <summary>The lowest health current over maximum, in percent, seen since <see cref="Start"/> (the baseline counts); NaN while health is unknown.</summary>
    public double HealthMinPercent { get; private set; }

    /// <summary>The melee blows that landed on the player (<see cref="FightTally.HitsTaken"/>).</summary>
    public int HitsTaken => Fight.HitsTaken;

    /// <summary>The damage taken (<see cref="FightTally.DamageTaken"/>).</summary>
    public long DamageTaken => Fight.DamageTaken;

    /// <summary>The attacks the player evaded (<see cref="FightTally.Evades"/>).</summary>
    public int Evades => Fight.Evades;

    /// <summary>True between <see cref="Start"/> and <see cref="Dispose"/>.</summary>
    public bool Running => _view is not null;

    /// <summary>Subscribes to the bus and takes the baselines; calling it again re-subscribes.</summary>
    /// <param name="view">The session.</param>
    public void Start(IGameSessionView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        Dispose();
        _view = view;
        _subscriptions.Add(view.Events.Subscribe<PlayerDied>(OnDied));
        _subscriptions.Add(view.Events.Subscribe<ObjectMotionChanged>(OnMotion));
        _subscriptions.Add(view.Events.Subscribe<QualitiesChanged>(OnQualities));
        _subscriptions.Add(view.Events.Subscribe<EnchantmentChanged>(OnEnchantment));
        _subscriptions.Add(view.Events.Subscribe<ObjectCreated>(OnCreated));
        _subscriptions.Add(view.Events.Subscribe<PlayerTeleportAnnounced>(OnTeleport));
        _subscriptions.Add(view.Events.Subscribe<ObjectMoved>(OnMoved));
        _subscriptions.Add(view.Events.Subscribe<ChatReceived>(OnChat));
        Fight.Start(view);
        TakeBaselines();
    }

    /// <summary>Unsubscribes; the counters stay readable.</summary>
    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        _view = null;
        Fight.Dispose();
    }

    /// <summary>Zeroes every counter and takes the baselines again (the subscriptions stay).</summary>
    public void Reset()
    {
        VictimNotices = 0;
        DeadMotions = 0;
        Deaths = 0;
        VitaePercent = 0;
        Purges = 0;
        CorpsesSeen = 0;
        StaleCorpses = 0;
        LastCorpseGuid = 0;
        LastCorpsePosition = null;
        LastCorpseAt = double.NaN;
        ProtectionDispels = 0;
        ProtectionExpiries = 0;
        Protection = ProtectionNone;
        DeathTeleports = 0;
        HasArrival = false;
        ArrivalCell = 0;
        ArrivalFrame = default;
        ProtectedHits = 0;
        ResetVitalsAfterArrival();
        HealthMinPercent = double.NaN;
        _afterDeath = false;
        _awaitingArrival = false;
        _afterArrival = false;
        Fight.Reset();
        TakeBaselines();
    }

    private void TakeBaselines()
    {
        if (_view is null)
        {
            return;
        }

        var q = _view.State.Self.Qualities;
        if (q.IsKnown)
        {
            _deathsBaseline = q.TryGetInt(NumDeathsProperty, out var deaths) ? deaths : 0;
            _deathsBaselinePending = false;
        }
        else
        {
            // The description has not arrived: the first NumDeaths we see (or the description's value) is the baseline.
            _deathsBaseline = 0;
            _deathsBaselinePending = true;
        }

        Deaths = 0;
        VitaePercent = ReadVitae(q);
        NoteHealth(q);
    }

    private static int ReadVitae(PlayerQualities q) =>
        q.TryGetVitae(out var vitae) ? (int)Math.Round((1.0 - vitae.Enchantment.StatModValue) * 100.0, MidpointRounding.AwayFromZero) : 0;

    private void NoteHealth(PlayerQualities q)
    {
        if (TryShare(q, 1, out var percent) && (double.IsNaN(HealthMinPercent) || percent < HealthMinPercent))
        {
            HealthMinPercent = percent;
        }
    }

    private static bool TryShare(PlayerQualities q, uint maxId, out double percent)
    {
        percent = 0;
        if (!q.TryGetVitalLevels(maxId, out var current, out var max) || max == 0)
        {
            return false;
        }

        percent = current * 100.0 / max;
        return true;
    }

    private void NoteDeathSignal()
    {
        _afterDeath = true;
    }

    private void OnDied(in PlayerDied e)
    {
        if (e.Source != DeathNoticeSource.VictimNotification)
        {
            return;
        }

        VictimNotices++;
        NoteDeathSignal();
    }

    private void OnMotion(in ObjectMotionChanged e)
    {
        if (!e.IsSelf || e.Motion.Forward != WorldObjectTable.DeadMotionLowWord)
        {
            return;
        }

        DeadMotions++;
        NoteDeathSignal();
    }

    private void OnQualities(in QualitiesChanged e)
    {
        if (_view is null)
        {
            return;
        }

        var q = _view.State.Self.Qualities;
        switch (e.Change)
        {
            case QualityChange.Description:
                if (_deathsBaselinePending)
                {
                    _deathsBaseline = q.TryGetInt(NumDeathsProperty, out var first) ? first : 0;
                    _deathsBaselinePending = false;
                    Deaths = 0;
                }

                VitaePercent = ReadVitae(q);
                NoteHealth(q);
                break;
            case QualityChange.Int when e.Key == NumDeathsProperty && q.TryGetInt(NumDeathsProperty, out var now):
                if (_deathsBaselinePending)
                {
                    // No description yet: this update is the first value we see, so it is the baseline (delta 0), as FightTally takes its first total experience. A baseline of 0 would read the character's lifetime count
                    // as this run's deaths and open the corpse window with no death.
                    _deathsBaseline = now;
                    _deathsBaselinePending = false;
                }

                var delta = now - _deathsBaseline;
                if (delta > Deaths)
                {
                    NoteDeathSignal();
                }

                Deaths = delta;
                break;
            case QualityChange.VitalCurrent:
                OnVitalCurrent(e.Key, q);
                break;
        }
    }

    private void OnVitalCurrent(uint key, PlayerQualities q)
    {
        // PrivateUpdateAttribute2ndLevel names a vital by its current's id (2 health, 4 stamina, 6 mana) or its maximum's (1, 3, 5); the state keeps it under the maximum's.
        var maxId = key is 2 or 4 or 6 ? key - 1 : key;
        if (maxId == 1)
        {
            NoteHealth(q);
        }

        if (!_afterArrival || !TryShare(q, maxId, out var percent))
        {
            return;
        }

        // The first 0x02E9 of each vital after the arrival is kept as a report-only figure (M3 3.2 review R8: not the regeneration that follows). It is NOT the judged value: ACE's 75% comes three seconds after the teleport
        // (Player_Death.cs 262-282) and a regeneration or drain tick can come before it (M4 2.2 fix R1); the judged value is read at the end of the vitals wait (SampleVitalsAfterArrival).
        switch (maxId)
        {
            case 1 when !_healthSeen:
                FirstHealthAfterArrivalPercent = percent;
                _healthSeen = true;
                break;
            case 3 when !_staminaSeen:
                FirstStaminaAfterArrivalPercent = percent;
                _staminaSeen = true;
                break;
            case 5 when !_manaSeen:
                FirstManaAfterArrivalPercent = percent;
                _manaSeen = true;
                break;
        }
    }

    /// <summary>
    /// Reads the three vitals from the player's qualities now and keeps them as <see cref="HealthAfterArrivalPercent"/>, <see cref="StaminaAfterArrivalPercent"/> and <see cref="ManaAfterArrivalPercent"/>: the death scenario calls this at the
    /// end of the respawn's vitals wait (and of the suicide's), so <see cref="VitalsAfterArrivalPercent"/> is what ACE's restore left, whatever came before it. False, and nothing kept, before a death's arrival or when a vital's current or
    /// maximum is unknown.
    /// </summary>
    /// <returns>True when all three were read.</returns>
    public bool SampleVitalsAfterArrival()
    {
        if (_view is null || !_afterArrival)
        {
            return false;
        }

        var q = _view.State.Self.Qualities;
        if (!TryShare(q, 1, out var health) || !TryShare(q, 3, out var stamina) || !TryShare(q, 5, out var mana))
        {
            return false;
        }

        HealthAfterArrivalPercent = health;
        StaminaAfterArrivalPercent = stamina;
        ManaAfterArrivalPercent = mana;
        _vitalsSampled = true;
        return true;
    }

    private void ResetVitalsAfterArrival()
    {
        HealthAfterArrivalPercent = double.NaN;
        StaminaAfterArrivalPercent = double.NaN;
        ManaAfterArrivalPercent = double.NaN;
        FirstHealthAfterArrivalPercent = double.NaN;
        FirstStaminaAfterArrivalPercent = double.NaN;
        FirstManaAfterArrivalPercent = double.NaN;
        _healthSeen = false;
        _staminaSeen = false;
        _manaSeen = false;
        _vitalsSampled = false;
    }

    private void OnEnchantment(in EnchantmentChanged e)
    {
        if (e.Change is EnchantmentChange.Purged or EnchantmentChange.PurgedBad)
        {
            Purges++;
        }

        if (_view is not null)
        {
            VitaePercent = ReadVitae(_view.State.Self.Qualities);
        }
    }

    private void OnCreated(in ObjectCreated e)
    {
        if (_view is null || e.IsSelf || (e.Recreated && e.Guid == LastCorpseGuid) || !_view.State.Objects.TryGet(e.Guid, out var obj) || (obj.DescriptionFlags & WorldObjectTable.CorpseFlag) == 0)
        {
            return;
        }

        var name = _view.State.Self.Name;
        var corpse = obj.Name.AsSpan();
        if (name is null || !corpse.StartsWith(CorpsePrefix, StringComparison.Ordinal) || !corpse[CorpsePrefix.Length..].SequenceEqual(name))
        {
            return;
        }

        if (!_afterDeath && !_awaitingArrival)
        {
            StaleCorpses++; // an earlier death's corpse coming into view (the den keeps them), not this death's
            return;
        }

        CorpsesSeen++;
        LastCorpseGuid = e.Guid;
        LastCorpsePosition = obj.Position;
        LastCorpseAt = _view.Clock.Now;
    }

    private void OnTeleport(in PlayerTeleportAnnounced e)
    {
        if (!_afterDeath)
        {
            return;
        }

        _afterDeath = false;
        _awaitingArrival = true;
        _afterArrival = false;
        ResetVitalsAfterArrival(); // a new death: its own restore is read after its own arrival
        DeathTeleports++;
    }

    private void OnMoved(in ObjectMoved e)
    {
        if (!_awaitingArrival || !e.IsSelf || e.Verdict != PositionVerdict.SelfTeleport)
        {
            return;
        }

        _awaitingArrival = false;
        _afterArrival = true;
        HasArrival = true;
        ArrivalCell = e.Position.CellId;
        ArrivalFrame = e.Position.Frame;
    }

    private void OnChat(in ChatReceived e)
    {
        if (e.Line.Kind == ChatKind.WeenieError && e.Line.ChatType == LifestoneProtectsYou)
        {
            ProtectedHits++;
        }
        else if (e.Line.Kind == ChatKind.System && e.Line.ChatType == MagicChatType)
        {
            if (string.Equals(e.Line.Text, ProtectionDispelledLine, StringComparison.Ordinal))
            {
                ProtectionDispels++;
                Protection = "dispelled";
            }
            else if (string.Equals(e.Line.Text, ProtectionExpiredLine, StringComparison.Ordinal))
            {
                ProtectionExpiries++;
                Protection = "expired";
            }
        }
    }
}
