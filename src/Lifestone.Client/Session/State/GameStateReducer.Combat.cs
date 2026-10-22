using System.Globalization;
using System.Text;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.State;

/// <summary>
/// The combat and death notifications (M3 1.2 lane s3): the four combat notices and their chat lines, the death notices, and the combat mode.
/// </summary>
/// <remarks>
/// The lines are retail's, re-expressed from <c>ClientCombatSystem</c>'s handlers (11.6096 addresses beside each handler; the 11.4186 bodies were read only to learn how the line is
/// composed). Retail adds a newline to every line because its scroll is one text buffer; a <see cref="ChatLine"/> is a line already, so it is left off. Retail also skips a line when
/// <c>gmCCommunicationSystem::IsSquelched</c> says the other party's name is squelched for chat type 6; the client has no squelch list yet: TODO(m3-squelch-combat) stays a named TODO at the
/// 0x01B1 and 0x01B3 handlers below (no squelch list in M4; M4 1.2 s1 left it unchanged).
/// </remarks>
internal sealed partial class GameStateReducer
{
    private double _lastDeathAt = double.NegativeInfinity;
    private DeathNoticeSource _lastDeathSource;
    private bool _killerNoticeLogged;
    private bool _autoTargetNoAttackerLogged;
    private bool _attackWired;

    /// <summary>The player's attack sequence (<see cref="IGameSessionView.Attack"/>). Reading it wires the reducer's own-motion subscription (see <see cref="WireAttack"/>).</summary>
    internal AttackSequence Attack
    {
        get
        {
            WireAttack();
            return _attack;
        }
    }

    private readonly AttackSequence _attack = new();

    /// <summary>
    /// Subscribes, once, to the player's own F74C (<see cref="ObjectMotionChanged"/>, which the world reducer publishes after it stored the motion): an attack motion starts or continues the
    /// <see cref="Attack"/> sequence (M4 1.2 s1). It is done by subscription because the F74C handler lives in another file; every entry point of the attack state calls this, and <c>GameSession</c>'s constructors call it, so it is wired before the first swing and the
    /// bus handler count is the same from the start (a test counts handlers across a log off).
    /// </summary>
    internal void WireAttack()
    {
        if (_attackWired)
        {
            return;
        }

        _attackWired = true;
        _bus.Subscribe((in ObjectMotionChanged e) => OnOwnMotion(in e));
    }

    private void OnOwnMotion(in ObjectMotionChanged e)
    {
        if (!e.IsSelf || e.Motion.MovementType != MovementType.Interpreted || e.Motion.Forward is not { } forward)
        {
            return; // the MoveToObject / TurnToObject F74C that follows a send is a different movement type: not a swing
        }

        if (AttackMotions.IsMeleeSwing(forward))
        {
            PublishAttack(_attack.OnSwing(e.Motion.StickyGuid ?? 0));
        }
        else if (AttackMotions.IsMissileLaunch(forward))
        {
            PublishAttack(_attack.OnLaunchMotion(e.Motion.StickyGuid ?? 0));
        }
    }

    private void PublishAttack(bool changed)
    {
        if (changed)
        {
            Changed();
            Publish(_attack.Snapshot);
        }
    }

    /// <summary>
    /// GameEvent 0x01A7 AttackDone {WeenieError}: {0} ends one swing (ACE repeats by itself), 0x0036 or any other code ends the sequence (<see cref="AttackSequence"/>). ACE: W/Player_Melee.cs 226 and
    /// 275-280 (<c>OnAttackDone</c>); the retail handler is <c>TODO(m4-attack-done-retail-cite)</c>.
    /// </summary>
    public void OnAttackDone(in InboundContext context, in GameEventHeader header, in AttackDoneEvent message)
    {
        WireAttack();
        // Every 0x01A7 is published, changed or not (M4 1.2 s4b, closes TODO(m4-attack-done-event)): a tracker sees a refusal that follows a Done at once.
        if (_attack.OnAttackDone(message.WeenieError))
        {
            Changed();
        }

        Publish(_attack.Snapshot);
    }

    /// <summary>GameEvent 0x01B8 CombatCommenceAttack: ACE sends it before each repeat after the first (W/Player_Melee.cs 285-290): the next swing or launch is starting.</summary>
    public void OnCommenceAttack(in InboundContext context, in GameEventHeader header, in CommenceAttackEvent message)
    {
        WireAttack();
        PublishAttack(_attack.OnCommence());
    }

    /// <summary>Notes the defender of a 0x01B1 or 0x01B3 as the sequence's target name (and the nearest object of that name as its id when none is known).</summary>
    private void NoteAttackDefender(string name)
    {
        WireAttack();
        if (!_attack.Active)
        {
            return;
        }

        uint guid = 0;
        if (_attack.Target == 0 && _state.Objects.TryGet(_state.Self.Guid, out var self) && self.Position is { } at)
        {
            _state.Objects.NearestNamed(name, at, _state.Self.Guid, out guid);
        }

        PublishAttack(_attack.OnDefender(name, guid));
    }

    /// <summary>
    /// GameEvent 0x01AC VictimNotification: <c>ClientCombatSystem::HandleVictimNotificationEvent</c> (11.6096 0x0056D160): the server's text, chat type 0, nothing for an empty text. Then
    /// <see cref="PlayerDied"/>, unless the 0x019E for the same death came first (<see cref="DeathPairing"/>).
    /// </summary>
    public void OnVictimNotification(in InboundContext context, in GameEventHeader header, in VictimNotificationEvent message)
    {
        AddNoticeChat(CombatChatTypes.Broadcast, message.Text);
        RaisePlayerDied(message.Text, 0, DeathNoticeSource.VictimNotification);
    }

    /// <summary>
    /// GameEvent 0x01AD KillerNotification: the server's text in the chat, then <see cref="PlayerKilledSomething"/>. TODO(retail-handler-01AD): the retail handler is not in the symbol
    /// table; the chat type is assumed to be the death notices' (0), as <c>HandleVictimNotificationEvent</c> uses.
    /// </summary>
    public void OnKillerNotification(in InboundContext context, in GameEventHeader header, in KillerNotificationEvent message)
    {
        if (!_killerNoticeLogged)
        {
            _killerNoticeLogged = true;
            _log.Info(SessionLog.Category, "TODO(retail-handler-01AD): the retail handler of GameEvent 0x01AD is not known; its text is shown as chat type 0 like 0x01AC's");
        }

        AddNoticeChat(CombatChatTypes.Broadcast, message.Text);
        Publish(new PlayerKilledSomething(message.Text));
    }

    /// <summary>
    /// GameEvent 0x01B1 AttackerNotification: <c>ClientCombatSystem::HandleAttackerNotificationEvent</c> (11.6096 0x0056C160), chat type 0x16: "You hit X for 5 points of slashing damage!"
    /// (<see cref="CombatText.Attack"/>), then a <see cref="CombatEvent"/>.
    /// </summary>
    public void OnAttackerNotification(in InboundContext context, in GameEventHeader header, in AttackerNotificationEvent message)
    {
        // TODO(m3-squelch-combat): retail skips this line for a squelched defender name; there is no squelch list in M4.
        var text = CombatText.Attack(message.DefenderName, message.DamageType, message.Percent, message.Damage, message.CriticalHit, message.AttackConditions);
        AddChat(new ChatLine(_clock.Now, ChatKind.System, CombatChatTypes.CombatSelf, null, 0, text));
        Publish(new CombatEvent(CombatEventKind.AttackLanded, message.DefenderName, message.DamageType, message.Damage, message.Percent, message.CriticalHit, -1, false, message.AttackConditions, text));
        NoteAttackDefender(message.DefenderName);
    }

    /// <summary>
    /// GameEvent 0x01B2 DefenderNotification: <c>ClientCombatSystem::HandleDefenderNotificationEvent</c> (11.6096 0x0056D670), chat type 0x15: "Drudge slashes your head for 5 points of
    /// slashing damage!" (<see cref="CombatText.Defend"/>), then a <see cref="CombatEvent"/>. Then, as retail, <see cref="RequestAutoTarget"/>.
    /// </summary>
    public void OnDefenderNotification(in InboundContext context, in GameEventHeader header, in DefenderNotificationEvent message)
    {
        var text = CombatText.Defend(message.AttackerName, message.DamageType, message.Percent, message.Damage, message.DamageLocation, message.CriticalHit, message.AttackConditions);
        AddChat(new ChatLine(_clock.Now, ChatKind.System, CombatChatTypes.CombatEnemy, null, 0, text));
        Publish(new CombatEvent(CombatEventKind.DefenseHit, message.AttackerName, message.DamageType, message.Damage, message.Percent, message.CriticalHit, unchecked((int)message.DamageLocation), false, message.AttackConditions, text));
        RequestAutoTarget(message.AttackerName);
    }

    /// <summary>
    /// GameEvent 0x01B3 EvasionAttackerNotification: <c>ClientCombatSystem::HandleEvasionAttackerNotificationEvent</c> (11.6096 0x0056D4F0), chat type 0x16: "X evaded your attack.", then a
    /// <see cref="CombatEvent"/>.
    /// </summary>
    public void OnEvasionAttackerNotification(in InboundContext context, in GameEventHeader header, in EvasionAttackerNotificationEvent message)
    {
        // TODO(m3-squelch-combat): retail skips this line for a squelched defender name; there is no squelch list in M4.
        var text = message.DefenderName + " evaded your attack.";
        AddChat(new ChatLine(_clock.Now, ChatKind.System, CombatChatTypes.CombatSelf, null, 0, text));
        Publish(new CombatEvent(CombatEventKind.AttackEvaded, message.DefenderName, 0, 0, 0, false, -1, true, 0, text));
        NoteAttackDefender(message.DefenderName);
    }

    /// <summary>
    /// GameEvent 0x01B4 EvasionDefenderNotification: <c>ClientCombatSystem::HandleEvasionDefenderNotificationEvent</c> (11.6096 0x0056D370), chat type 0x15: "You evaded X!", then a
    /// <see cref="CombatEvent"/>. Then <see cref="RequestAutoTarget"/>, as for 0x01B2.
    /// </summary>
    public void OnEvasionDefenderNotification(in InboundContext context, in GameEventHeader header, in EvasionDefenderNotificationEvent message)
    {
        var text = "You evaded " + message.AttackerName + "!";
        AddChat(new ChatLine(_clock.Now, ChatKind.System, CombatChatTypes.CombatEnemy, null, 0, text));
        Publish(new CombatEvent(CombatEventKind.DefenseEvaded, message.AttackerName, 0, 0, 0, false, -1, true, 0, text));
        RequestAutoTarget(message.AttackerName);
    }

    /// <summary>
    /// Retail's handlers for 0x01B2 and 0x01B4 call <c>ClientCombatSystem::AutoTarget</c> (11.6096 0x0056C9D0) when the combat mode is melee or missile (not magic) and
    /// <c>PlayerModule::AutoTarget</c> (0x005D3E90) is on, and only when nothing is selected; the selection checks the last, so this raises <see cref="AutoTargetRequested"/> with the
    /// attacker. Retail takes the attacker from the player's CurrentAttacker instance id (ACE's private update, not decoded: <c>TODO(m3-autotarget-attacker-id)</c>), here the nearest
    /// living creature of the notice's name (M4 1.3 s2: <see cref="WorldObjectTable.NearestLivingCreatureNamed"/> skips a dead one and a non-creature). Retail's check that the object exists and one more flag test on it are not read (<c>TODO(m3-autotarget-attacker-flag)</c>).
    /// </summary>
    private void RequestAutoTarget(string attackerName)
    {
        var q = _state.Self.Qualities;
        if ((q.CombatMode != CombatModes.Melee && q.CombatMode != CombatModes.Missile) || !q.AutoTarget)
        {
            return;
        }

        uint attacker = 0;
        if (_state.Objects.TryGet(_state.Self.Guid, out var self) && self.Position is { } at)
        {
            _state.Objects.NearestLivingCreatureNamed(attackerName, at, _state.Self.Guid, out attacker);
        }

        if (attacker == 0 && !_autoTargetNoAttackerLogged)
        {
            _autoTargetNoAttackerLogged = true;
            _log.Info(SessionLog.Category, "TODO(m3-autotarget-selectnext): no living creature of the attacker's name is known; retail selects the closest compass item then, nothing is selected here");
        }

        Publish(new AutoTargetRequested(attacker));
    }

    /// <summary>
    /// GM 0x019E PlayerKilled, the broadcast: <c>ClientCombatSystem::HandlePlayerDeathEvent</c> (11.6096 0x0056D070) shows the text (chat type 0) only when the player is neither the victim
    /// nor the killer (a player in it is told by the 0x01AC or 0x01AD instead). When the player is the victim, <see cref="PlayerDied"/> is raised unless the 0x01AC came first
    /// (<see cref="DeathPairing"/>).
    /// </summary>
    public void OnPlayerKilled(in InboundContext context, in PlayerKilledMsg message)
    {
        var player = _state.Self.Guid;
        if (player != message.Victim && player != message.Killer)
        {
            AddNoticeChat(CombatChatTypes.Broadcast, message.Text);
        }

        if (player != 0 && player == message.Victim)
        {
            RaisePlayerDied(message.Text, message.Killer, DeathNoticeSource.PlayerKilledBroadcast);
        }
    }

    /// <summary>
    /// Raises <see cref="PlayerDied"/> once per death. ACE sends the victim 0x01AC and then the 0x019E broadcast (victim included): the first message raises the event and a message of the
    /// other kind within <see cref="DeathPairing.PairWindowSeconds"/> is the same death. Two messages of the same kind are two deaths.
    /// </summary>
    private void RaisePlayerDied(string text, uint killer, DeathNoticeSource source)
    {
        var now = _clock.Now;
        if (source != _lastDeathSource && now - _lastDeathAt <= DeathPairing.PairWindowSeconds)
        {
            _lastDeathAt = double.NegativeInfinity; // the pair is complete
            return;
        }

        _lastDeathAt = now;
        _lastDeathSource = source;
        Publish(new PlayerDied(text, killer, source));
    }

    /// <summary>A death notice's line: the text as sent, nothing when it is empty (retail tests the length).</summary>
    private void AddNoticeChat(uint chatType, string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            AddChat(new ChatLine(_clock.Now, ChatKind.System, chatType, null, 0, text));
        }
    }

    /// <summary>
    /// Applies a PrivateUpdatePropertyInt (the call replaces <see cref="OnPrivateUpdate"/>'s own store). A change of the combat mode (property 40,
    /// <see cref="CombatModes.Property"/>) raises <see cref="CombatModeChanged"/> once the new value is stored, ahead of the <see cref="QualitiesChanged"/> every int update raises.
    /// </summary>
    private void ApplyPrivateInt(PlayerQualities q, in PrivateUpdateMsg message)
    {
        var value = unchecked((int)message.Value);
        if (message.Key != CombatModes.Property)
        {
            var coinsBefore = q.CoinValue;
            q.Ints[message.Key] = value;
            NoteCoinValue(message.Key, coinsBefore, value); // M4 3.2 s1: GameStateReducer.Vendor.cs
            return;
        }

        WireAttack();
        var previous = q.CombatMode;
        q.Ints[message.Key] = value;
        if (previous != value)
        {
            Changed();
            Publish(new CombatModeChanged(previous, value));
        }
    }
}

/// <summary>
/// The chat lines of the combat notices, as retail composes them: <c>ClientCombatSystem::HandleAttackerNotificationEvent</c> (11.4186 0x0056B420, 11.6096 0x0056C160) and
/// <c>HandleDefenderNotificationEvent</c> (0x0056C920, 0x0056D670), with the verb of <c>CombatSystem::InqCombatHitAdjectives</c> (0x005C9040 in 11.6096), the type name of
/// <c>AppraisalSystem::DamageTypeToString</c> (0x005B4200) and the part name of <c>BodyPartEnumMapper::BodyPartToString</c> through <c>EmapToDisplay::LowerCaseRemoveUnderscores</c>.
/// </summary>
internal static class CombatText
{
    private const uint Slash = 0x1, Pierce = 0x2, Bludgeon = 0x4, Cold = 0x8, Fire = 0x10, Acid = 0x20, Electric = 0x40, Health = 0x80, Nether = 0x400;

    /// <summary>The buffer retail gives <c>DamageTypeToString</c> (64 bytes, the terminator included); a name that does not fit leaves the string empty.</summary>
    private const int DamageTypeBuffer = 64;

    /// <summary>"You hit X for 5 points of slashing damage!" with its prefixes and suffix (the attacker's 0x01B1).</summary>
    public static string Attack(string defender, uint damageType, double percent, uint damage, bool critical, ulong conditions)
    {
        var (verb, _) = HitAdjectives(damageType, percent);
        var line = new StringBuilder();
        if (critical)
        {
            line.Append("Critical hit!  "); // two spaces: the attacker's handler
        }

        if ((conditions & 4) != 0)
        {
            line.Append("Sneak Attack! ");
        }

        if ((conditions & 2) != 0)
        {
            line.Append("Recklessness! ");
        }

        line.Append("You ").Append(verb).Append(' ').Append(defender).Append(" for ").Append(Points(damage)).Append(" of ").Append(TypeWord(damageType)).Append("damage!");
        if ((conditions & 1) != 0)
        {
            line.Append(" Your target's Critical Protection augmentation allows them to avoid your critical hit!");
        }

        return line.ToString();
    }

    /// <summary>"Drudge slashes your head for 5 points of slashing damage!" (the defender's 0x01B2).</summary>
    public static string Defend(string attacker, uint damageType, double percent, uint damage, uint location, bool critical, ulong conditions)
    {
        var (_, plural) = HitAdjectives(damageType, percent);
        var line = new StringBuilder();
        if (critical)
        {
            line.Append("Critical hit! ");
        }

        if ((conditions & 4) != 0)
        {
            line.Append("Sneak Attack! ");
        }

        if ((conditions & 2) != 0)
        {
            line.Append("Reckless! ");
        }

        // Retail has a second composition without the part ("X hits you for ..."), taken when the part's name comes back empty; BodyPartToString always names a part ("Unknown" for the
        // rest), so it is never taken and is not reproduced.
        line.Append(attacker).Append(' ').Append(plural).Append(" your ").Append(BodyPartName(unchecked((int)location))).Append(" for ").Append(Points(damage)).Append(" of ").Append(TypeWord(damageType)).Append("damage!");
        if ((conditions & 1) != 0)
        {
            line.Append(" Your Critical Protection augmentation allows you to avoid a critical hit!");
        }

        return line.ToString();
    }

    /// <summary>"5 points" (a single point has no "s": retail tests <c>hp != 1</c>).</summary>
    private static string Points(uint damage)
    {
        var count = unchecked((int)damage);
        return string.Create(CultureInfo.InvariantCulture, $"{count} point") + (count != 1 ? "s" : string.Empty);
    }

    /// <summary>
    /// The damage type's name in lower case with a trailing space ("slashing "), or empty when it has none (retail's handlers add the space only to a non-empty name).
    /// </summary>
    internal static string TypeWord(uint damageType)
    {
        var name = DamageTypeName(damageType);
        return name.Length == 0 ? string.Empty : name.ToLowerInvariant() + " ";
    }

    /// <summary><c>AppraisalSystem::DamageTypeToString</c>: the names of the set bits in bit order, joined by "/"; health, stamina and mana have none.</summary>
    internal static string DamageTypeName(uint damageType)
    {
        ReadOnlySpan<(uint Bit, string Name)> names =
        [
            (Slash, "Slashing"), (Pierce, "Piercing"), (Bludgeon, "Bludgeoning"), (Cold, "Cold"), (Fire, "Fire"), (Acid, "Acid"),
            (Electric, "Electrical"), (Nether, "Nether"), (0x10000000, "Prismatic"),
        ];
        var text = new StringBuilder();
        foreach (var (bit, name) in names)
        {
            if ((damageType & bit) == 0)
            {
                continue;
            }

            if (text.Length > 0)
            {
                text.Append('/');
            }

            text.Append(name);
        }

        return text.Length + 1 > DamageTypeBuffer ? string.Empty : text.ToString();
    }

    /// <summary>
    /// <c>CombatSystem::InqCombatHitAdjectives</c>: the verb of a hit by the fraction of health it took (over 0.1, 0.25 and 0.5 step it up), singular for "You hit" and plural for "it hits".
    /// A negative fraction gives no verbs (retail returns before setting them); any type it has no verbs for gives "hit".
    /// </summary>
    internal static (string Single, string Plural) HitAdjectives(uint damageType, double percent)
    {
        if (percent < 0)
        {
            return (string.Empty, string.Empty);
        }

        var tier = percent > 0.5 ? 3 : percent > 0.25 ? 2 : percent > 0.1 ? 1 : 0;
        string[]? verbs = damageType switch
        {
            Slash => ["scratch", "cut", "slash", "mangle"],
            Pierce => ["nick", "stab", "impale", "gore"],
            Bludgeon => ["graze", "bash", "smash", "crush"],
            Cold => ["numb", "chill", "frost", "freeze"],
            Fire => ["singe", "scorch", "burn", "incinerate"],
            Acid => ["blister", "sear", "corrode", "dissolve"],
            Electric => ["spark", "shock", "jolt", "blast"],
            Health => ["drain", "exhaust", "siphon", "deplete"],
            Nether => ["scar", "twist", "wither", "eradicate"],
            _ => null,
        };
        if (verbs is null)
        {
            return ("hit", "hits");
        }

        var single = verbs[tier];
        return (single, single.EndsWith("sh", StringComparison.Ordinal) || single.EndsWith("ch", StringComparison.Ordinal) ? single + "es" : single + "s");
    }

    /// <summary>
    /// <c>BodyPartEnumMapper::BodyPartToString</c> then <c>LowerCaseRemoveUnderscores</c>: "upper arm". The numbers have gaps (11 and 14 are not named); they and everything past the table
    /// give "Unknown" as retail does.
    /// </summary>
    internal static string BodyPartName(int part) => part switch
    {
        -1 => "undefined",
        0 => "head",
        1 => "chest",
        2 => "abdomen",
        3 => "upper arm",
        4 => "lower arm",
        5 => "hand",
        6 => "upper leg",
        7 => "lower leg",
        8 => "foot",
        9 => "horn",
        10 => "front leg",
        12 => "front foot",
        13 => "rear leg",
        15 => "rear foot",
        16 => "torso",
        17 => "tail",
        18 => "arm",
        19 => "leg",
        20 => "claw",
        21 => "wings",
        22 => "breath",
        23 => "tentacle",
        24 => "upper tentacle",
        25 => "lower tentacle",
        26 => "cloak",
        27 => "num",
        _ => "unknown",
    };
}
