namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// Every action a human or a bot can take in a session (A0's Action API, automation-vision.md §1.3): the retail
/// screens, the portal bridge, the tools panel, the scenarios and the automation engines all go through it, so the UI
/// and a bot take the same path (R2). Built by steps ST9 to ST11.
/// </summary>
/// <remarks>
/// <para><b>Handles.</b> Each call returns an <see cref="ActionHandle"/> at once and never blocks or throws for a
/// rejected request: a wrong phase, a bad argument or an offline session completes the handle with
/// <see cref="ActionOutcomeKind.Refused"/>, and an action that is not built yet with
/// <see cref="ActionOutcomeKind.NotImplemented"/> (a loud stub, logged as an ERROR). A handle completes on the server's
/// confirmation, the server's error or its timeout, inside <c>GameSession.Tick</c> (docs/session.md, "Actions").</para>
/// <para><b>Order and limits.</b> Session-phase actions (login, character operations, entering the world, logoff,
/// quit) run one at a time, in call order. Chat, use and tool requests are rate-limited, and wait in the queue rather
/// than being dropped. Stat raises, the magic lane (casts and combat-mode changes) and the inventory lane (wield, unwield, move) each
/// run one at a time, in call order, because the server's answers carry no request id; a cast also keeps the magic lane busy after its outcome until the server
/// ends the cast (UseDone), as ACE is busy until then. The defaults are in docs/session.md (owner decision D49).</para>
/// <para><b>Safety.</b> <see cref="KillSwitch"/> and <see cref="DryRun"/> apply to <see cref="ActionSource.Automation"/>
/// only. Automation actions also need a loopback server (R1); an offline session refuses every server action.</para>
/// <para>Game thread only.</para>
/// </remarks>
public interface IActionApi
{
    /// <summary>
    /// Logs in with the account the session was created for (the host chose it; no secret passes through here). Completes
    /// when the data check is complete and the character list is in; fails on a boot, a data mismatch, a character error
    /// after its retries, or a timeout.
    /// </summary>
    ActionHandle Login(ActionSource source = ActionSource.Host);

    /// <summary>
    /// Selects a character in the list, or none with 0 (<see cref="CharacterListState.SelectedGuid"/>). Local: no message is
    /// sent; completes at once with <see cref="ActionOutcomeKind.Local"/>.
    /// </summary>
    ActionHandle SelectCharacter(uint guid, ActionSource source);

    /// <summary>
    /// Creates a character (F656). Refused before sending unless the intent validates
    /// (<see cref="CharacterCreateIntent.TryValidate"/>: exactly 55 skills, a printable-ASCII name). Completes on F643
    /// <c>Ok</c>, or with <see cref="ActionOutcomeKind.ServerError"/> and the CharGenResult code.
    /// </summary>
    ActionHandle CreateCharacter(CharacterCreateIntent intent, ActionSource source);

    /// <summary>
    /// Deletes a character (F655 with its index in the server's list, which this finds and range-checks; ACE does not).
    /// Refused for an unknown character or one already pending deletion. Completes on the F655 acknowledgement and the
    /// list that follows it.
    /// </summary>
    ActionHandle DeleteCharacter(uint guid, ActionSource source);

    /// <summary>Restores a character pending deletion (F7D9). Completes on F643 for that character.</summary>
    ActionHandle RestoreCharacter(uint guid, ActionSource source);

    /// <summary>
    /// Enters the world with a character (F7C8, then F657 when F7DF arrives). Refused for a character pending deletion
    /// (retail ghosts Enter). Completes when F746 PlayerCreate and the player's CreateObject have arrived.
    /// </summary>
    ActionHandle EnterWorld(uint guid, ActionSource source);

    /// <summary>
    /// Sends LoginComplete (GameAction 0x00A1) when portal space ends, as retail's
    /// <c>CPlayerSystem::SendLoginCompleteNotification</c> does (11.4186 0x00562E90, 11.6096 0x00563C30). Allowed only while
    /// the player is <see cref="SelfTravel.Arriving"/> or <see cref="SelfTravel.TeleportArrived"/>, and once per arrival: a
    /// second call is refused loudly. Completes when the server clears Hidden (<see cref="SelfTravel.Present"/>).
    /// </summary>
    ActionHandle LoginComplete(ActionSource source = ActionSource.Client);

    /// <summary>Uses an object (GameAction 0x0036). Completes on GameEvent 0x01C7 UseDone: code 0 confirms, any other is a server error.</summary>
    ActionHandle Use(uint guid, ActionSource source);

    /// <summary>Asks for an object's health (GameAction 0x01BF). Completes on GameEvent 0x01C0 UpdateHealth for it.</summary>
    ActionHandle QueryHealth(uint guid, ActionSource source);

    /// <summary>Says <paramref name="text"/> aloud (GameAction 0x0015). Completes on the server's echo of the player's own speech.</summary>
    ActionHandle Talk(string text, ActionSource source);

    /// <summary>
    /// Tells <paramref name="text"/> to the character named <paramref name="target"/> (GameAction 0x005D). Completes on the
    /// server's confirmation line, or with a server error when the character is not available.
    /// </summary>
    ActionHandle Tell(string target, string text, ActionSource source);

    /// <summary>
    /// Spends <paramref name="xpToSpend"/> experience on raising one of the character's attributes (GameAction 0x0045,
    /// retail <c>CM_Train::Event_TrainAttribute</c>; wire in <c>StatActionCodecs</c>). ACE answers with the private
    /// attribute update 0x02E3 and the line "Your base X is now N!".
    /// Lane Stats: one stat action in flight at a time, in call order; timeout 5 s from the send. Completes <see cref="ActionOutcomeKind.Confirmed"/> on the
    /// attribute's private update with more experience spent; ACE's told refusal ("Your attempt to raise X has failed.") is a <see cref="ActionOutcomeKind.ServerError"/>;
    /// more experience than the character has is dropped without a word and times out.
    /// </summary>
    /// <param name="attribute">The attribute id (<c>PropertyAttribute</c>: 1 Strength, 2 Endurance, 3 Coordination, 4 Quickness, 5 Focus, 6 Self; the key of <see cref="PlayerQualities.TryGetAttribute"/>).</param>
    /// <param name="xpToSpend">The experience to spend. The wire carries it as an unsigned 32-bit value: the XP to level 126 (4,286,609,098) is above <see cref="int.MaxValue"/> but fits.</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle RaiseAttribute(uint attribute, uint xpToSpend, ActionSource source);

    /// <summary>
    /// Spends <paramref name="xpToSpend"/> experience on raising one of the character's vitals (GameAction 0x0044, retail
    /// <c>CM_Train::Event_TrainAttribute2nd</c>). ACE answers with the private vital update 0x02E7 and the "Your base X is now N!" line.
    /// Lane Stats. Confirmed on the vital's own update (0x02E7, not the current value's 0x02E9); more experience than the character has gets ACE's refusal line (a ServerError).
    /// </summary>
    /// <param name="vital">The vital's maximum id (<c>PropertyAttribute2nd</c>: 1 health, 3 stamina, 5 mana; the key of <see cref="PlayerQualities.TryGetVital"/>).</param>
    /// <param name="xpToSpend">The experience to spend (unsigned 32-bit on the wire).</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle RaiseVital(uint vital, uint xpToSpend, ActionSource source);

    /// <summary>
    /// Spends <paramref name="xpToSpend"/> experience on raising a trained or specialised skill (GameAction 0x0046,
    /// retail <c>CM_Train::Event_TrainSkill</c>). ACE answers with the private skill update 0x02DD and the "Your base X is now N!" line.
    /// Lane Stats. Confirmed on the skill's update with more experience spent; ACE tells no refusal (an untrained skill, too much experience, the rank cap), so those time out.
    /// </summary>
    /// <param name="skill">The skill id (<c>Skill</c>, the key of <see cref="PlayerQualities.TryGetSkill"/>).</param>
    /// <param name="xpToSpend">The experience to spend (unsigned 32-bit on the wire).</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle RaiseSkill(uint skill, uint xpToSpend, ActionSource source);

    /// <summary>
    /// Trains an untrained skill, spending <paramref name="creditsToSpend"/> skill credits (GameAction 0x0047, retail
    /// <c>CM_Train::Event_TrainSkillAdvancementClass</c>). ACE answers with the private skill update 0x02DD, the credits (0x02CD) and "X trained.".
    /// Lane Stats. Confirmed when the skill's update shows it trained and the credits' update has arrived; an already-trained skill gets ACE's "Failed to train X!" line (a ServerError);
    /// credits that differ from the skill's cost, or more than the character has, are dropped without a word and time out.
    /// </summary>
    /// <param name="skill">The skill id (<c>Skill</c>).</param>
    /// <param name="creditsToSpend">The credits to spend: a signed 32-bit value on the wire (ACE reads an Int32).</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle TrainSkill(uint skill, int creditsToSpend, ActionSource source);

    /// <summary>
    /// Casts a spell that needs no target: a self, item-less or area spell (GameAction 0x0048, retail <c>CM_Magic::Event_CastUntargetedSpell</c>, 11.6096 0x006A3F70).
    /// Lane Magic (one cast or combat-mode change in flight at a time, in call order; the lane stays busy after the outcome until ACE's UseDone 0x01C7, because ACE answers a
    /// cast sent sooner with "You're too busy"). Confirmed on the enchantment the spell adds to or refreshes on the player (a self spell: <see cref="EnchantmentChanged"/>) or the projectile's
    /// CreateObject from the player (a war spell; the wire does not name the caster, so it is the first Missile object that started within 5 m of the player, flies away from the player along the aim and wears the spell's Setup where the host knows it, <c>TODO(m3-cast-projectile-attribution)</c>). A Use and a Cast are never out together (ACE's UseDone names no request): the one waits for the other, and a UseDone owed by an action that ended without one is dropped for 5 s. Which a
    /// spell is comes from the SpellTable (<c>SpellInfo.MetaSpellType</c>, <c>SpellInfo.Flags</c> SelfTargeted); a spell that is neither (a buff on another target) has no proof on the wire and
    /// completes as <see cref="ActionOutcomeKind.Sent"/> when ACE ends the cast (<c>TODO(m3-cast-other-target-proof)</c>). A fizzle (GameEvent 0x028A, code 0x0402), a refusal (UseDone
    /// 0x0400 components, 0x0401 mana, any other code) or a UseDone 0 that ends a self or projectile cast without its effect is a <see cref="ActionOutcomeKind.ServerError"/> with the code;
    /// 10 s timeout. Refused at once for spell 0 (no spell) and when the player is not in the world. Casting needs the magic combat mode (<see cref="SetCombatMode"/>): ACE ends a cast made
    /// in another mode with UseDone 0 and no effect.
    /// </summary>
    /// <param name="spellId">The spell id (<c>SpellTable</c>, a spell the character knows).</param>
    /// <param name="source">Who is asking.</param>
    /// <remarks>A literal <c>Cast(id, 0)</c> binds this overload (0 converts to <see cref="ActionSource"/>) unless the int overload below takes it: see <see cref="Cast(uint, int, ActionSource)"/>.</remarks>
    ActionHandle Cast(uint spellId, ActionSource source = ActionSource.Host);

    /// <summary>
    /// Casts a spell at a target (GameAction 0x004A {target, spell}, retail <c>CM_Magic::Event_CastTargetedSpell</c>, 11.6096 0x006A3E60). Lane Magic.
    /// Completes as <see cref="Cast(uint, ActionSource)"/> does (a bolt or streak has a projectile, a self spell cast at the player's own guid an enchantment, a buff on another
    /// target completes as <see cref="ActionOutcomeKind.Sent"/>); 10 s timeout. Refused at once for spell 0 or target 0.
    /// </summary>
    /// <param name="spellId">The spell id (<c>SpellTable</c>).</param>
    /// <param name="target">The target's guid (a creature, an item or the player's own guid).</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle Cast(uint spellId, uint target, ActionSource source = ActionSource.Host);

    /// <summary>
    /// The same targeted cast for an <see cref="int"/> target. It exists for one reason: a literal 0 or any int literal as the second argument would otherwise bind
    /// <see cref="Cast(uint, ActionSource)"/> (a constant 0 converts to the enumeration) and send an untargeted cast; an <see cref="int"/> parameter is an identity match and wins, so
    /// <c>Cast(id, 0)</c> is the refused "no target" and <c>Cast(id, 5)</c> is a cast at object 5. Only 0 is the refused "no target"; every other value, negative ones included, is the guid's bits
    /// passed through unchecked as the <see cref="uint"/> of <see cref="Cast(uint, uint, ActionSource)"/> (M3 1.2 review R7: a guid of 0x80000000 or more, every monster and every item the server
    /// creates, is a negative <see cref="int"/>, and refusing it made it the "no target"). A default interface method: an implementation need not provide it.
    /// </summary>
    /// <param name="spellId">The spell id.</param>
    /// <param name="target">The target's guid as an int (refused when 0; a negative value is a guid of 0x80000000 or more).</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle Cast(uint spellId, int target, ActionSource source = ActionSource.Host) =>
        target != 0 ? Cast(spellId, unchecked((uint)target), source) : Cast(spellId, 0u, source);

    /// <summary>
    /// Changes the player's combat mode (GameAction 0x0053, retail <c>CM_Combat::Event_ChangeCombatMode</c>, 11.6096 0x006AA9D0). ACE answers with the private int update 0x02CD
    /// for property 40. Lane Magic (the same lane as <see cref="Cast(uint, ActionSource)"/>: ACE fails a cast in progress when the mode changes, and a cast needs the mode, so the change
    /// waits behind a cast and a cast behind the change). Confirmed on <see cref="CombatModeChanged"/> to the mode asked for; completes at once as <see cref="ActionOutcomeKind.Local"/>,
    /// sending nothing, when <see cref="PlayerQualities.CombatMode"/> already holds it. ACE's one told refusal (a weapon that does not fit the mode: WeenieError ActionCancelled) is a
    /// <see cref="ActionOutcomeKind.ServerError"/>; any other missing update is a <see cref="ActionOutcomeKind.TimedOut"/>; 5 s timeout.
    /// Refused at once for a value that is not one of <see cref="CombatMode"/>.
    /// </summary>
    /// <param name="mode">The mode wanted.</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle SetCombatMode(CombatMode mode, ActionSource source = ActionSource.Host);

    /// <summary>
    /// Wields or wears an item the character carries (GameAction 0x001A {item, location}, retail <c>CM_Inventory::Event_GetAndWieldItem</c>, 11.6096 0x006AC950).
    /// Lane Inventory (one wield, unwield or move in flight at a time, in call order). Confirmed on the wield message for the item (GameEvent 0x0023, <see cref="InventoryChangeKind.Wielded"/>;
    /// ACE's 0x02DA and 0x02CE zeros before it are not the answer); ACE's refusal (GameEvent 0x00A0, <see cref="InventoryFailed"/>: the item and a weenie error, 0 for most refusals) is a
    /// <see cref="ActionOutcomeKind.ServerError"/>; 5 s timeout.
    /// Refused at once for item 0, an empty <paramref name="location"/>, an item the player does not hold (not in the pack or a side pack), or one already wielded at that location.
    /// </summary>
    /// <param name="item">The item's guid.</param>
    /// <param name="location">Where to wield it: the <see cref="EquipMask"/> slot (the weapon's own slot for a weapon).</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle Wield(uint item, EquipMask location, ActionSource source = ActionSource.Host);

    /// <summary>
    /// Takes a wielded or worn item off into the main pack (unwielding is a put into the player's own pack: GameAction 0x0019 {item, the player's guid, placement 0},
    /// <c>CM_Inventory::Event_PutItemInContainer</c>, 11.6096 0x006ACC80). Lane Inventory. Confirmed on the item's move into the pack
    /// (<see cref="InventoryChangeKind.Unwielded"/>); a refusal (GameEvent 0x00A0) is a <see cref="ActionOutcomeKind.ServerError"/>; 5 s timeout.
    /// Refused at once for item 0 or an item the inventory model does not have equipped.
    /// </summary>
    /// <param name="item">The item's guid.</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle Unwield(uint item, ActionSource source = ActionSource.Host);

    /// <summary>
    /// Moves an item the player holds into a pack (GameAction 0x0019 {item, container, placement}, retail <c>CM_Inventory::Event_PutItemInContainer</c>, 11.6096 0x006ACC80):
    /// from one pack to another, from a side pack to the main pack, or off the body. Lane Inventory. Confirmed on the item's put into the container
    /// (GameEvent 0x0022: <see cref="InventoryChangeKind.Moved"/>, or <see cref="InventoryChangeKind.Unwielded"/> for a worn item); a refusal (GameEvent 0x00A0) is a
    /// <see cref="ActionOutcomeKind.ServerError"/>; 5 s timeout.
    /// Refused at once for item 0 or an item the player does not hold (a pickup from the ground is not built: <c>TODO(m3-pickup)</c>).
    /// </summary>
    /// <param name="item">The item's guid.</param>
    /// <param name="container">The pack's guid; 0 is the main pack (the player's own guid, filled in when it is sent).</param>
    /// <param name="placement">The slot in the pack (0 is the first).</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle MoveToPack(uint item, uint container = 0, uint placement = 0, ActionSource source = ActionSource.Host);

    /// <summary>
    /// Merges <paramref name="amount"/> units of one stack the player holds into another stack of the same item (GameAction 0x0054 {from, to, amount}, retail
    /// <c>CM_Inventory::Event_StackableMerge</c>, 11.6096 0x006ACDD0; ACE <c>Player.HandleActionStackableMerge</c>). Lane Inventory. Confirmed when the target's stack size
    /// (GameMessage 0x0197, <see cref="InventoryChangeKind.StackChanged"/>) is the size it had plus <paramref name="amount"/> and the source is gone (the whole stack: ACE's 0x0024 comes
    /// first) or has shrunk by <paramref name="amount"/> (its own 0x0197 comes first); a refusal (GameEvent 0x00A0) is a <see cref="ActionOutcomeKind.ServerError"/>; 5 s timeout.
    /// Refused at once for a guid of 0, the same stack twice, an amount of 0 or less or above the source's stack, a stack the player does not hold, two different items, a stack the model has no
    /// size for, or a target that would pass its maximum stack.
    /// </summary>
    /// <param name="from">The source stack's guid.</param>
    /// <param name="to">The target stack's guid (the same weenie class).</param>
    /// <param name="amount">How many units to move.</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle MergeStack(uint from, uint to, int amount, ActionSource source = ActionSource.Host);

    /// <summary>
    /// Kills the player on purpose (GameAction 0x0279, no payload; retail <c>CM_Character::Event_Suicide</c>, 11.6096 0x006A2920; ACE <c>GameActionDie</c>). ACE has the player say five lines 3 s apart ("I feel faint..." and on), then
    /// the death without a victim notice (no 0x01AC): the Dead motion, health 0x02E9 to 0, NumDeaths 0x02CD up, the vitae, the purge of every enchantment, the corpse and the lifestone teleport. Completes
    /// <see cref="ActionOutcomeKind.Confirmed"/> when the player's death is seen (health 0 and NumDeaths up, in either order); the player already dead or teleporting gets WeenieError YoureTooBusy, which completes
    /// it <see cref="ActionOutcomeKind.Refused"/> with code 0x001D. Time limit 30 s on the session clock from the send (<see cref="ActionOutcomeKind.TimedOut"/>). Refused at once when the player is not in the world.
    /// </summary>
    /// <param name="source">Who is asking.</param>
    ActionHandle Suicide(ActionSource source = ActionSource.Host);

    /// <summary>
    /// Puts a spell on one of the character's spell bars (GameAction 0x01E3 {spell, index, bar}; retail <c>CM_Character::Event_AddSpellFavorite</c>, 11.6096 0x006A1D90; ACE <c>GameActionAddSpellFavorite</c>). ACE answers nothing
    /// (it ignores a bar above 7 and a spell the player does not know), so this completes as <see cref="ActionOutcomeKind.Sent"/> when it is sent. Refused at once for spell 0, a negative index, a bar outside 0 to 7, or when
    /// the player is not in the world.
    /// </summary>
    /// <param name="spellId">The spell id.</param>
    /// <param name="index">The position on the bar (0 is the first).</param>
    /// <param name="bar">The bar, 0 to 7.</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle AddSpellFavorite(uint spellId, int index, int bar, ActionSource source = ActionSource.Host);

    /// <summary>
    /// Logs the character off: adds "Logging off..." to the chat as retail does, sends F653 (the only sender), and
    /// publishes <see cref="LogOffRequested"/> for portal space. Completes when the server's F653 and the character list
    /// have arrived.
    /// </summary>
    ActionHandle LogOff(ActionSource source);

    /// <summary>
    /// Changes one of the character's option bits for this session only (the options panel; D42): the reducer applies it as a session-local override over the PlayerDescription's words
    /// (<see cref="PlayerQualities.CharacterOptions1"/> and <see cref="PlayerQualities.CharacterOptions2"/>), publishes <see cref="QualitiesChanged"/> (<see cref="QualityChange.Options"/>)
    /// and records it. Nothing goes to the server (<c>TODO(g6-options-save)</c>); it applies at once and is not a handle. Game thread only.
    /// </summary>
    /// <param name="word">1 or 2: which option word.</param>
    /// <param name="mask">The option's bit in that word.</param>
    /// <param name="value">Whether the option is on.</param>
    void SetSessionOption(int word, uint mask, bool value);

    /// <summary>
    /// Ends the session: in the world F653 then a Disconnect at once, otherwise the Disconnect only (DoD 11). Completes
    /// with <see cref="ActionOutcomeKind.Sent"/>, since ACE never answers a Disconnect.
    /// </summary>
    ActionHandle Quit(ActionSource source);

    /// <summary>
    /// Where the player's movement controller (physics) sends MoveToState, AutonomousPosition and Jump. The sink stamps
    /// each with the player's current sequences; it has no handles and allocates nothing per send.
    /// </summary>
    IMovementSink Movement { get; }

    /// <summary>The automation path into the same movement controller the keyboard drives (R2).</summary>
    MoveIntents Move { get; }

    /// <summary>
    /// True refuses every new <see cref="ActionSource.Automation"/> action and cancels the queued ones
    /// (<see cref="ActionOutcomeKind.Cancelled"/>). Other sources are unaffected.
    /// </summary>
    bool KillSwitch { get; set; }

    /// <summary>
    /// True logs <see cref="ActionSource.Automation"/> actions with what they would have sent and completes them with
    /// <see cref="ActionOutcomeKind.DryRun"/>; nothing is sent. Other sources are unaffected.
    /// </summary>
    bool DryRun { get; set; }
}
