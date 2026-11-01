namespace Lifestone.Client.Session.Actions;

/// <summary>
/// Which queue an action waits in (docs/session.md §7). Actions of one lane leave in call order; lanes are independent
/// of each other.
/// </summary>
internal enum ActionLane : byte
{
    /// <summary>Login, the character operations, entering the world and logoff: one in flight at a time, in call order.</summary>
    Session,

    /// <summary>Talk and Tell: rate-limited together.</summary>
    Chat,

    /// <summary>Use: rate-limited.</summary>
    Use,

    /// <summary>QueryHealth: rate-limited.</summary>
    Query,

    /// <summary>The player tools' requests: rate-limited.</summary>
    Tools,

    /// <summary>Automation's movement intents: rate-limited.</summary>
    Move,

    /// <summary>
    /// RaiseAttribute, RaiseVital, RaiseSkill and TrainSkill: one in flight at a time, in call order (M3 s1b). The server's answers
    /// to a raise (the experience update, the stat update, the refusal line) carry no request id, and every raise spends from the same
    /// available experience, so the next raise waits for the previous one's answer, which also keeps each stat's update unambiguous.
    /// </summary>
    Stats,

    /// <summary>
    /// Cast (both forms) and SetCombatMode (M3 1.2 s4b): one in flight at a time, in call order, and the lane stays busy after a cast's outcome until the server ends the cast
    /// (ACE's UseDone 0x01C7). ACE handles one cast at a time (a second one while the player is busy is answered "You're too busy"), its answers carry no request id, and a
    /// change of combat mode fails a cast in progress, so a combat-mode change waits behind a cast, which is why it shares the lane.
    /// </summary>
    Magic,

    /// <summary>
    /// Wield, Unwield and MoveToPack (M3 1.2 s4b): one in flight at a time, in call order. The server's answers (0x0022, 0x0023, 0x00A0) carry no request id, and ACE
    /// answers a refusal with an item and an error of 0, so only one request at a time keeps each answer unambiguous.
    /// </summary>
    Inventory,

    /// <summary>
    /// Attack, MissileAttack and CancelAttack (M4 1.2 s2): one in flight at a time, in call order. ACE answers an attack with no message of its own (the approach, the swing, the combat notices; a refusal is a WeenieError and a 0x01A7)
    /// and carries no request id, so a second attack sent before the first is confirmed could not be told from it. An attack is complete at its confirmation, not at the end of the run it starts (that is the attack sequence).
    /// </summary>
    Attack,

    /// <summary>
    /// UseWithTarget and Drink (M4 3.1 s1): one in flight at a time, in call order, and the lane stays busy after a heal's UseDone until the player's own Ready F74C has arrived and 0.1 s have passed. ACE is busy until that Ready
    /// (capture.md C2: a repeat sent at the UseDone, 8 ms before the Ready, was refused 0x001D), its answers carry no request id, and a kit's and a potion's answers differ, so only one heal at a time keeps each answer unambiguous.
    /// </summary>
    Heal,

    /// <summary>LoginComplete and Quit: neither serialised nor rate-limited.</summary>
    Immediate,
}

/// <summary>
/// The Action API's timeouts and rate limits (owner decision D49: documented defaults; retail has no equivalents, so
/// they are Lifestone's choices). Times are <see cref="Contracts.SessionClock"/> seconds and run from the moment an
/// action is sent.
/// </summary>
internal static class ActionPolicy
{
    /// <summary>The number of <see cref="ActionLane"/> values.</summary>
    internal const int LaneCount = (int)ActionLane.Immediate + 1;

    /// <summary>Login: until the data check is complete and the character list is in.</summary>
    internal const double LoginSeconds = 30;

    /// <summary>CreateCharacter: until F643.</summary>
    internal const double CreateSeconds = 15;

    /// <summary>DeleteCharacter: until the F655 acknowledgement and the F658 after it.</summary>
    internal const double DeleteSeconds = 10;

    /// <summary>RestoreCharacter: until F643.</summary>
    internal const double RestoreSeconds = 10;

    /// <summary>
    /// EnterWorld: until F746 and the player's CreateObject. The network session enforces its own 30 s cap on each try (<c>SessionTimeouts.EnterWorld</c>) and fails the request when it is spent; this is the action's backstop, which
    /// adds the wait the default retry policy gives a character ACE still holds online (0x0D, <c>LoginRetryPolicy.CharacterInWorldWait</c>), so the wait is the session's to end and not a silent timeout here.
    /// </summary>
    internal const double EnterWorldSeconds = 30 + Lifestone.Net.Session.LoginRetryPolicy.DefaultCharacterInWorldWaitSeconds + EnterWorldSlackSeconds;

    /// <summary>
    /// The seconds the EnterWorld backstop outlasts the session's own named failure (M4 4.1 fix2b, review R13): the session sends up to 15 resends 5 s apart and each restarts its 30 s cap at its due time, so the last allowed try can end a
    /// few seconds past 30 + the wait (the round trips of the 0x0D answers); without the slack the generic tracker timeout would fire a moment before "refused after waiting 75 s".
    /// </summary>
    internal const double EnterWorldSlackSeconds = 5;

    /// <summary>LoginComplete: until the server clears Hidden.</summary>
    internal const double LoginCompleteSeconds = 15;

    /// <summary>
    /// LoginComplete's wait for the player's description and contained objects (M3 1.2 s5; retail's <c>CPlayerSystem::SendLoginCompleteNotification</c> waits without a limit): the most it waits on the session clock, counted from the call,
    /// before it is sent anyway with a WARNING naming how many objects were still missing, so a lost object never strands the player in portal space.
    /// </summary>
    internal const double LoginCompleteContentsCapSeconds = 3;

    /// <summary>Use: until UseDone (ACE walks the player to the object first).</summary>
    internal const double UseSeconds = 20;

    /// <summary>
    /// UseWithTarget (a healing kit) and Drink (a potion or food), M4 3.1 s1: until the UseDone. A use takes 1.47-2.1 s from the send to the UseDone (capture.md C1 to C3: the kit's motion 1.48 s, the potion's effect at 0.74 s and its UseDone at
    /// 1.48 s), so the limit is the Use's; ACE walks nobody for a self heal, but a kit used on another player walks to them first.
    /// </summary>
    internal const double HealSeconds = UseSeconds;

    /// <summary>Attack (melee): until the first sign ACE took it; the server's approach can take up to 15 s (W/Player_Melee.cs 204-226), so the limit is the Use's.</summary>
    internal const double AttackSeconds = UseSeconds;

    /// <summary>MissileAttack: until the first launch or a refusal.</summary>
    internal const double MissileAttackSeconds = 10;

    /// <summary>CancelAttack: until the sequence's end (the swing in progress finishes first).</summary>
    internal const double CancelAttackSeconds = 5;

    /// <summary>QueryHealth: until UpdateHealth (ACE answers only for a creature).</summary>
    internal const double QueryHealthSeconds = 5;

    /// <summary>Talk: until the server echoes the player's speech.</summary>
    internal const double TalkSeconds = 5;

    /// <summary>Tell: until the server's confirmation line.</summary>
    internal const double TellSeconds = 5;

    /// <summary>LogOff: until the server's F653 and F658.</summary>
    internal const double LogOffSeconds = 30;

    /// <summary>
    /// RaiseAttribute, RaiseVital, RaiseSkill, TrainSkill: until the stat's private update (ACE answers at once; a request it drops
    /// without a word, such as more experience than the character has, ends here as a timeout).
    /// </summary>
    internal const double StatSeconds = 5;

    /// <summary>
    /// Cast: until the spell's outcome (the enchantment, the projectile, a fizzle or a refusal). A cast takes the windup gesture, the cast gesture and the
    /// recoil, which ACE paces in seconds (a high-level spell's windup is the longest), so the limit is twice the other world actions'.
    /// </summary>
    internal const double CastSeconds = 10;

    /// <summary>SetCombatMode: until the combat-mode update (ACE changes the mode after the stance animation).</summary>
    internal const double CombatModeSeconds = 5;

    /// <summary>Wield, Unwield and MoveToPack: until the inventory message (0x0023, 0x0022) or the refusal (0x00A0).</summary>
    internal const double InventorySeconds = 5;

    /// <summary>OpenContainer (M4 2.1 s2): until the 0196 and the contents' objects; ACE walks the player to the container first (the Use's 15 s walk), as <see cref="UseSeconds"/>.</summary>
    internal const double OpenContainerSeconds = UseSeconds;

    /// <summary>
    /// CloseContainer: until the 0052. Derivation (2.1-R11): the chest's lid takes 0.42 s to close (capture C3) and a corpse answers at once (capture C2), so 5 s holds the 0.42 s animation more than ten times over,
    /// which is the plan row's "5 s plus half a chest animation" with the margin made flat; the close needs no walk.
    /// </summary>
    internal const double CloseContainerSeconds = 5;

    /// <summary>
    /// PickUp (M4 2.1 s2): until the 0022. Derivation: ACE walks the player to the item first (the 15 s walk of <c>Player_Move</c>), then plays the pickup motion before the 0022 (<see cref="BusyChainContainerSeconds"/>,
    /// the longest captured), so 15 + 1.35 + 0.74 (a ground item's second length) is about 17.1 s; 25 s keeps a margin of 8 s (the spec's "about 25 s").
    /// </summary>
    internal const double PickUpSeconds = 25;

    /// <summary>
    /// Give: until the 0024 or 0022. Derivation (2.1-R11), as <see cref="PickUpSeconds"/>: ACE walks the player to the receiver first (the 15 s walk of <c>Player_Move</c>), then turns toward it (0.70 s, capture C4 run 4),
    /// so 15 + 0.70 is about 15.7 s; 20 s keeps a margin of 4.3 s.
    /// </summary>
    internal const double GiveSeconds = 20;

    /// <summary>Drop and the three splits: until the answer (no walk; the drop and the 3D split play one pickup motion, 0.74 s); <see cref="InventorySeconds"/>.</summary>
    internal const double ItemMoveSeconds = InventorySeconds;

    /// <summary>
    /// The pickup motion's length from the ground, from a 3D split and for a drop (capture C4: 0.74 s, 0.73 s). ACE refuses a move sent while the previous move's chain runs (the pickup motion, the 0022, the
    /// return stance and one more motion length): the Inventory lane holds the next move until the own return-stance F74C has arrived and this long has passed.
    /// </summary>
    internal const double BusyChainGroundSeconds = 0.74;

    /// <summary>The same from an open corpse or chest (capture C2: 1.35 s; the item's height picks the motion).</summary>
    internal const double BusyChainContainerSeconds = 1.35;

    /// <summary>
    /// A give's chain after its confirmation: the turn toward the receiver took 0.70 s (capture C4 run 4). Whether ACE keeps the player busy after an accepted give was not captured; a give is held like a pick-up as the
    /// cautious reading (<c>TODO(m4-give-chain)</c>), and a refused give holds nothing.
    /// </summary>
    internal const double BusyChainGiveSeconds = 0.70;

    /// <summary>
    /// Added to every busy-chain hold (2.1-R8). ACE clears its busy state at the end of the motion on its own tick, and the measured lengths (<see cref="BusyChainGroundSeconds"/>, <see cref="BusyChainContainerSeconds"/>)
    /// are rounded readings (the drop measured 0.73 s), so a move sent exactly at the measured end can reach ACE first and draw a 0x001D. 0.1 s is a few server ticks; the live moves test (two moves 1.5 s apart) did not
    /// exercise the boundary, so the value is a margin, not a measurement.
    /// </summary>
    internal const double BusyChainMarginSeconds = 0.1;

    /// <summary>
    /// How long after a move's confirmation the lane waits for the own return-stance F74C before it counts the length from the confirmation (capture C2: the F74C came 31 ms after the 0022); a recording or a fake
    /// without the F74C then still frees the lane.
    /// </summary>
    internal const double ReturnStanceWaitSeconds = 0.25;

    /// <summary>
    /// How long a give or a move waits, from an own teleport's arrival, for the client's post-teleport LoginComplete to go out (M4 2.1-R2). ACE refuses a give or a move while <c>WorldObject.Teleporting</c> is set
    /// (the mod's <c>AceFacts.TeleportBusy</c> reads the same flag; ACE 95ef426d clears it in <c>Player.OnTeleportComplete</c>, reached from the LoginComplete handler), and the live chest recording shows the flag still set
    /// 4.7 s after the teleport's reply, with the client's 0x00A1 sent only when portal space ended, 5.6 s after the place: it is the LoginComplete, not a fixed time, that clears it (capture C4's "within about 3 s" came from runs
    /// that sent LoginComplete at once). The cap (the tunnel's 5.6 s plus a margin, under <see cref="LoginCompleteSeconds"/>) is for a session that never sends one (the offline stand-in): the wait then ends with a WARNING.
    /// </summary>
    internal const double TeleportLoginCompleteCapSeconds = 10;

    /// <summary>
    /// How long after the post-teleport LoginComplete's send a give or a move still waits (M4 2.1-R2): ACE handles the 0x00A1 on its own tick and clears the teleporting flag there, so a move sent in the same instant
    /// can still reach it first. 0.5 s is a margin of a few server ticks (the live run measured nothing finer; a 0x001D after it would show in the moves test).
    /// </summary>
    internal const double LoginCompleteBusyMarginSeconds = 0.5;

    /// <summary>How long a give's 028B refusal (0x046A, 0x03EF, 0x04CE) waits for its 00A0 before it ends the give by itself (capture C4: the 00A0 followed in 10 to 430 ms).</summary>
    internal const double GiveRefusalGraceSeconds = 0.5;

    /// <summary>
    /// Suicide (M3 3.2 s0): until the player's death is seen, on the session clock from the send. ACE speaks five lines 3 s apart and then kills the player, so the death comes 15 s after the send; the limit is twice that.
    /// </summary>
    internal const double SuicideSeconds = 30;

    /// <summary>A player-tool request: until the mod's reply (m2-plan §4.12).</summary>
    internal const double ToolSeconds = 5;

    /// <summary>
    /// M5 8 (<c>m5-8-place-not-in-world</c>, D141, D152): how long a player-scope tool request waits for the world, once before sending (the client's own state says the player is not in the world) and once after the mod answered
    /// <c>not-in-world</c> (the mod's <c>where</c> is asked every <see cref="ToolWorldProbeSeconds"/> until the player is in a landblock), before it fails with the named reason.
    /// </summary>
    internal const double ToolWorldWaitSeconds = 20;

    /// <summary>M5 8: the spacing of the <c>where</c> probes while a request waits for the mod's landblock check to pass.</summary>
    internal const double ToolWorldProbeSeconds = 1;

    /// <summary>Talk and Tell together: 4 a second, bursts of 8.</summary>
    internal const double ChatPerSecond = 4;

    /// <summary>The burst of <see cref="ChatPerSecond"/>.</summary>
    internal const int ChatBurst = 8;

    /// <summary>Use: 4 a second.</summary>
    internal const double UsePerSecond = 4;

    /// <summary>QueryHealth: 4 a second.</summary>
    internal const double QueryPerSecond = 4;

    /// <summary>The player tools: 2 a second.</summary>
    internal const double ToolsPerSecond = 2;

    /// <summary>Automation's movement intents: 20 a second (a keyboard's worth), bursts of 20.</summary>
    internal const double MovePerSecond = 20;

    /// <summary>The rate limiter of <paramref name="lane"/>, or null for a lane without one.</summary>
    internal static RateLimiter? CreateLimiter(ActionLane lane) => lane switch
    {
        ActionLane.Chat => new RateLimiter(ChatPerSecond, ChatBurst),
        ActionLane.Use => new RateLimiter(UsePerSecond, (int)UsePerSecond),
        ActionLane.Query => new RateLimiter(QueryPerSecond, (int)QueryPerSecond),
        ActionLane.Tools => new RateLimiter(ToolsPerSecond, (int)ToolsPerSecond),
        ActionLane.Move => new RateLimiter(MovePerSecond, (int)MovePerSecond),
        _ => null,
    };

    /// <summary>
    /// Identify (M4 3.2 s2): until the 0x00C9. ACE answers within 1 to 10 ms (capture.md C3) and with no walk, range or busy check (spec 5.6), so 5 s is the Query lane's limit
    /// (<see cref="QueryHealthSeconds"/>) and holds the answer more than a hundred times over.
    /// </summary>
    internal const double IdentifySeconds = QueryHealthSeconds;

    /// <summary>
    /// ACE's cooldown after a failed appraisal (<c>Player.cs:274-299</c>, ACE 95ef426d): a repeat within this long of a failed request on the same guid is answered failed with no roll. The failed roll was not seen live
    /// (<c>TODO(m4-appraise-fail-live)</c>, capture.md C3), so the number is read from source.
    /// </summary>
    internal const double IdentifyFailCooldownSeconds = 5;

    /// <summary>OpenVendor (M4 3.2 s1): until the 0062 or the refusal; ACE turns the player to the vendor first (UseDone at +0.83 to 0.96 s, capture.md C1, C2), so the limit is the Use's.</summary>
    internal const double OpenVendorSeconds = UseSeconds;

    /// <summary>
    /// How long after a vendor's UseDone 0 the 0062 is still awaited before the open is Refused "vendor did not open". Derivation: the 0062 followed the UseDone by 1.04 s (Thelnoth) and 0.65 s (Renald) in the live capture, so 3 s is
    /// about three times the longest; a shop that is closed or a walk that failed is a bare UseDone 0 and ends here.
    /// </summary>
    internal const double OpenVendorMarginSeconds = 3;

    /// <summary>How long after a vendor trade's UseDone the F745 of each announced item is awaited (capture.md C1, C2: 3 to 5 ms after the UseDone; the spec's 5 s as the limit).</summary>
    internal const double TradeObjectsSeconds = 5;

    /// <summary>Buy and Sell (M4 3.2 s1): until the UseDone (ACE answers within 20 ms, capture.md C1, C2) plus <see cref="TradeObjectsSeconds"/> for the objects.</summary>
    internal const double TradeSeconds = 5 + TradeObjectsSeconds;
}

/// <summary>
/// A token bucket on the session clock: <see cref="Burst"/> tokens to start with, refilled at <see cref="PerSecond"/>,
/// one taken per action sent. An action that finds no token waits in its lane's queue; nothing is dropped.
/// </summary>
internal sealed class RateLimiter
{
    private double _tokens;
    private double _last = double.NaN;

    /// <summary>A full bucket.</summary>
    /// <param name="perSecond">Tokens added per second.</param>
    /// <param name="burst">The most tokens the bucket holds.</param>
    internal RateLimiter(double perSecond, int burst)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(perSecond);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(burst);
        PerSecond = perSecond;
        Burst = burst;
        _tokens = burst;
    }

    /// <summary>Tokens added per second.</summary>
    internal double PerSecond { get; }

    /// <summary>The most tokens the bucket holds.</summary>
    internal int Burst { get; }

    /// <summary>Takes a token at <paramref name="now"/> (session-clock seconds) when one is there.</summary>
    /// <returns>True when a token was taken.</returns>
    internal bool TryTake(double now)
    {
        if (double.IsNaN(_last))
        {
            _last = now;
        }
        else if (now > _last)
        {
            _tokens = Math.Min(Burst, _tokens + ((now - _last) * PerSecond));
            _last = now;
        }

        if (_tokens < 1)
        {
            return false;
        }

        _tokens -= 1;
        return true;
    }

    /// <summary>Back to a full bucket (a reconnect).</summary>
    internal void Reset()
    {
        _tokens = Burst;
        _last = double.NaN;
    }
}
