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
/// than being dropped. Stat raises, the magic lane (casts and combat-mode changes), the inventory lane (wield, unwield, move) and the heal lane (a kit, a potion) each
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

    /// <summary>The object the last targeted cast, melee attack or missile attack aimed at (not the player), or 0 before any (M5 7: what <c>--examine-capture-creatures</c> follows).</summary>
    uint LastAttackTarget => 0;

    /// <summary>When <see cref="LastAttackTarget"/> was last set, on the action layer's frame clock, or NaN.</summary>
    double LastAttackTargetAt => double.NaN;

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
    /// Refused at once for a value that is not one of <see cref="CombatMode"/>. A mode the wielded weapon forbids (melee with a caster or a launcher in hand, magic with no caster, missile with no launcher) is
    /// <see cref="ActionOutcomeKind.Refused"/> at once and never sent: the client-side guard (M4 3.1 lane g, <see cref="CombatModeGuard"/>; the owner's rule D23): ACE reverts such a mode itself (silently for one weapon, with ActionCancelled for an illegal pair).
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
    /// Refused at once for item 0. An item the player does not hold is picked up: <c>TODO(m3-pickup)</c> is closed (M4 2.1 s2) and this call is then <see cref="PickUp"/> under the kind
    /// <see cref="ActionKind.MoveToPack"/> (the same GameAction, the pick-up tracker, its 25 s timeout and its busy chain).
    /// </summary>
    /// <param name="item">The item's guid.</param>
    /// <param name="container">The pack's guid; 0 is the main pack (the player's own guid, filled in when it is sent).</param>
    /// <param name="placement">The slot in the pack (0 is the first).</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle MoveToPack(uint item, uint container = 0, uint placement = 0, ActionSource source = ActionSource.Host);

    /// <summary>
    /// Opens a corpse or chest (GameAction 0x0036 Use, retail <c>CM_Inventory::Event_UseEvent</c> 11.6096 0x006AD310; the sender of 0x0195 and the handlers of GameEvent 0x0196 and 0x0052 are unknown in retail, named
    /// <c>TODO(m4-retail-handler-0196)</c>). Lane Use. Confirmed on the 0196 for <paramref name="container"/> once every listed entry's world object is known (at most
    /// 3 s after the 0196; past it Confirmed with the missing guids logged). ACE's UseDone 0 comes between the 0196 and the contents' F745s and is not the end.
    /// A <see cref="ActionOutcomeKind.ServerError"/> when a UseDone arrives with no 0196 since the send: the detail carries the 02EB text, or "locked" when the container played its sound 0x94 (a locked chest sends no text).
    /// A container that is already open: Confirmed at once, nothing sent. 20 s timeout. Refused at once for guid 0.
    /// </summary>
    /// <param name="container">The corpse's or chest's guid.</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle OpenContainer(uint container, ActionSource source = ActionSource.Host);

    /// <summary>
    /// Closes the open ground container (GameAction 0x0195 NoLongerViewingContents). Lane Use. Confirmed on the 0052 for it (at once for a corpse, 0.42 s later for a chest); a container that is not open: Confirmed
    /// at once, nothing sent. 5 s timeout. Refused at once for guid 0.
    /// </summary>
    /// <param name="container">The container's guid.</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle CloseContainer(uint container, ActionSource source = ActionSource.Host);

    /// <summary>
    /// Picks an item up from the ground or an open container into the player's pack or a side pack (GameAction 0x0019 {item, container, placement}, retail <c>CM_Inventory::Event_PutItemInContainer</c> 0x006ACC80;
    /// ACE walks the player to the item first). Lane Inventory. Confirmed on the 0022 {item, container == the requested one}; a <see cref="ActionOutcomeKind.ServerError"/> on the 0x00A0 for the item, its code the
    /// preceding 028A's (0x001D busy, 0x0029) and its detail the 028A's or 02EB's text ("Source item not found!", "You are too encumbered ..."). 25 s timeout. After the confirmation the lane holds the next move
    /// until the pickup chain ends (the own return-stance F74C and one more motion length: 0.74 s from the ground, 1.35 s from a container), and until 3 s after an own teleport.
    /// </summary>
    /// <param name="item">The item's guid.</param>
    /// <param name="container">The pack's guid; 0 is the main pack (the player's own guid).</param>
    /// <param name="placement">The slot in the pack.</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle PickUp(uint item, uint container = 0, int placement = 0, ActionSource source = ActionSource.Host);

    /// <summary>
    /// Drops an item the player holds to the ground (GameAction 0x001B DropItem). Lane Inventory. Confirmed on the 0x019A for the item; a refusal (0x00A0) is a <see cref="ActionOutcomeKind.ServerError"/>. 5 s timeout.
    /// The lane holds the next move for the drop's chain (0.74 s).
    /// </summary>
    /// <param name="item">The item's guid.</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle Drop(uint item, ActionSource source = ActionSource.Host);

    /// <summary>
    /// Splits <paramref name="amount"/> from a stack the player holds into a pack (GameAction 0x0055). Lane Inventory. Confirmed on the 0022 of a new guid into that container (it precedes the new stack's F745 and the old
    /// stack's 0197). 5 s timeout.
    /// </summary>
    /// <param name="stack">The stack's guid.</param>
    /// <param name="container">The pack's guid; 0 is the main pack.</param>
    /// <param name="placement">The slot in the pack.</param>
    /// <param name="amount">How many to split off.</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle SplitToContainer(uint stack, uint container, int placement, int amount, ActionSource source = ActionSource.Host);

    /// <summary>
    /// Splits <paramref name="amount"/> from a stack the player holds onto the ground (GameAction 0x0056). Lane Inventory. Confirmed on the old stack's 0197, then the new ground stack's F745 (at most 3 s later). 5 s timeout.
    /// The lane holds the next move for the split's chain (0.74 s).
    /// </summary>
    /// <param name="stack">The stack's guid.</param>
    /// <param name="amount">How many to split off.</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle SplitTo3D(uint stack, int amount, ActionSource source = ActionSource.Host);

    /// <summary>
    /// Splits <paramref name="amount"/> from a stack the player holds and wields it (GameAction 0x019B). Lane Inventory. Confirmed on the 0x0023 of a new guid at <paramref name="location"/>; a
    /// <see cref="ActionOutcomeKind.ServerError"/> on the 02EB text and the 0x00A0 (the old stack's 0197 is restored by the server). 5 s timeout.
    /// </summary>
    /// <param name="stack">The stack's guid.</param>
    /// <param name="location">The <see cref="EquipMask"/> to wield it at.</param>
    /// <param name="amount">How many to split off.</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle SplitToWield(uint stack, EquipMask location, int amount, ActionSource source = ActionSource.Host);

    /// <summary>
    /// Gives an item the player holds to a player or an NPC (GameAction 0x00CD {target, item, amount}). Lane Inventory. Confirmed on the 0x0024 for the item, or the 0x0022 naming the target, or the 0x0197 of a part stack;
    /// a <see cref="ActionOutcomeKind.ServerError"/> on the 028B 0x046A (an NPC with no Give emote for the item: ACE answers it also for an NPC with other emotes), 0x03EF, 0x04CE, or the 0x00A0 (0x04CD), each with its
    /// text; 028A 0x001D when the server is busy (a move or give before the post-teleport LoginComplete, or during a pick-up's chain). 20 s timeout.
    /// </summary>
    /// <param name="target">The receiver's guid.</param>
    /// <param name="item">The item's guid.</param>
    /// <param name="amount">How many to give (1 for an item that does not stack).</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle Give(uint target, uint item, int amount, ActionSource source = ActionSource.Host);

    /// <summary>
    /// Uses an item on a target (GameAction 0x0035 {item, target}, retail <c>CM_Inventory::Event_UseWithTargetEvent</c>, 11.6096 0x006AD3E0): a healing kit on the player's own guid is the hunt's heal
    /// (ACE <c>Healer.DoHealing</c>, spec §5.7). Lane Heal: one heal in flight at a time, in call order, and the lane holds the next heal until this one's UseDone AND the player's own Ready F74C AND 0.1 s have passed (ACE is
    /// busy until the Ready: a repeat at the UseDone is refused 0x001D, capture.md C2). The handle is a <see cref="HealHandle"/>; its <see cref="HealHandle.Result"/> says what the F7E0 line read.
    /// <para>Confirmed iff the UseDone 0x01C7 came with code 0 and the kit's 0x02CE Structure (property 92) fell since the send, or the kit was consumed (0x0024): a won roll (<see cref="HealResultKind.Healed"/>) and a lost roll
    /// (<see cref="HealResultKind.FailedRoll"/>: a use spent, nothing healed, never an error) both confirm. A UseDone with a code is a <see cref="ActionOutcomeKind.ServerError"/> with that code (0x04FC untrained in Healing, 0x001D busy,
    /// 0x04FE not a player target, 0x04EB jumping); a 0x028B 0x04FF (a full vital) or a 0x02EB text is a refusal read the same way (<c>TODO(m4-heal-full-vital-live)</c>: not captured live); a silent UseDone 0 with no Structure change
    /// (a kit not held, no target) is a <see cref="ActionOutcomeKind.ServerError"/> "nothing happened". A use takes about 1.5 s; 20 s timeout. Refused at once for item 0 or target 0, and offline.</para>
    /// </summary>
    /// <param name="item">The kit's guid.</param>
    /// <param name="target">The target's guid (the player's own for a self heal).</param>
    /// <param name="source">Who is asking.</param>
    HealHandle UseWithTarget(uint item, uint target, ActionSource source = ActionSource.Host);

    /// <summary>
    /// Drinks a potion or eats food (GameAction 0x0036 Use, as <see cref="Use"/>), with the potion's own tracker. Lane Heal (the same lane and hold as <see cref="UseWithTarget"/>: ACE's chain is busy until its UseDone, and
    /// its F74C Ready comes before it). Confirmed iff the UseDone came with code 0 and the potion's stack fell (0x0197 with one fewer) or the item was removed (0x0024, the last one); the restored amount is read from the F7E0 line
    /// "The &lt;name&gt; restores N points of your &lt;Vital&gt;." (<see cref="HealResultKind.Drank"/>); N of 0 is <see cref="HealResultKind.Wasted"/>, still Confirmed, because ACE spends a potion drunk at a full vital (capture.md C3).
    /// A refusal (0x028A 0x001D busy, 0x04EB jumping) ends at the UseDone as a <see cref="ActionOutcomeKind.ServerError"/> with its code, as does a UseDone with a code. The effect comes about 0.75 s after the send and the UseDone about 1.5 s;
    /// 20 s timeout. Refused at once for item 0, and offline.
    /// </summary>
    /// <param name="item">The potion's or food's guid.</param>
    /// <param name="source">Who is asking.</param>
    HealHandle Drink(uint item, ActionSource source = ActionSource.Host);

    /// <summary>
    /// Uses a charged mana stone (a Mana Charge) on the player or on one of the player's items (GameAction 0x0035 {stone, target}, retail <c>CM_Inventory::Event_UseWithTargetEvent</c>, 11.6096 0x006AD3E0; closes <c>TODO(m4-gear-mana)</c>).
    /// ACE (<c>ManaStone.HandleActionUseOnTarget</c>, ACE 95ef426d <c>ManaStone.cs:59-233</c>): on the player the stone's mana goes to every equipped item below its maximum, rationed evenly until the stone is empty or every item full (:121-190), with the line
    /// "The Mana Stone gives N points of mana to the following items: ...", then "Your items are fully charged." or "You need M more mana ..."; on an item it fills that item (:192-225, "The Mana Stone gives N points of mana to the X."). Then the stone's destroy roll
    /// (:235-252; a retail charge's ManaStoneDestroyChance is 1, so it is consumed: 0x0024) and UseDone 0. No motion, no busy chain. Lane Heal (one use with a UseDone at a time; it holds nothing after its UseDone).
    /// <para>Confirmed iff the UseDone came with code 0 and the "gives" line was read or the stone was removed; the detail names the mana given and what is still needed. A UseDone with a code is a <see cref="ActionOutcomeKind.ServerError"/> with it and the line before it
    /// (0x0036 ActionCancelled with "You have no items equipped that need mana."; 0x04C8 ItemDoesntHaveEnoughMana; 0x04BE YouDoNotOwnThatItem); "The X is already full of mana." before a UseDone 0 is a ServerError too (nothing was given; the stone is kept).
    /// <b>An empty stone used on an item destroys that item</b> to drain it (:84-113): the caller checks the stone's ItemCurMana (an appraisal) first. 20 s timeout. Refused at once for stone 0 or target 0, and offline.
    /// The default body throws: a view that cannot send it does not implement it.</para>
    /// </summary>
    /// <param name="stone">The charged stone's guid.</param>
    /// <param name="target">The player's own guid (every equipped item), or an item's.</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle UseManaStone(uint stone, uint target, ActionSource source = ActionSource.Host) => throw new NotSupportedException("IActionApi.UseManaStone is not implemented by this view");

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
    /// Takes a spell off one of the character's spell bars (GameAction 0x01E4 {spell, bar}; retail <c>CM_Character::Event_RemoveSpellFavorite</c>, 11.6096 0x006A26B0; ACE <c>GameActionRemoveSpellFavorite</c>). ACE answers
    /// nothing (a spell that is not on the bar is ignored), so this completes as <see cref="ActionOutcomeKind.Sent"/> when it is sent. Refused at once for spell 0, a bar outside 0 to 7, or when the player is not in the world.
    /// </summary>
    /// <param name="spellId">The spell id.</param>
    /// <param name="bar">The bar, 0 to 7.</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle RemoveSpellFavorite(uint spellId, int bar, ActionSource source = ActionSource.Host);

    /// <summary>
    /// Starts a melee attack on <paramref name="target"/>: GameAction 0x0008 TargetedMeleeAttack {target, height, power} (retail <c>CM_Combat::Event_TargetedMeleeAttack</c>; ACE <c>Player.HandleActionTargetedMeleeAttack</c>,
    /// W/Player_Melee.cs). ACE walks the player to the target, swings, and repeats by itself while AutoRepeatAttacks is on, so ONE call gives a whole run of swings: the outcome says whether ACE took the attack,
    /// and the run's end is <see cref="IGameSessionView.Attack"/> (Done on GameEvent 0x01A7 {0x0036}). Completes <see cref="ActionOutcomeKind.Confirmed"/> on the first sign ACE took it (its approach, the swing, a combat notice for the target), also
    /// at once with the detail "queued" when a sequence is already running (ACE queues the power and answers nothing); <see cref="ActionOutcomeKind.ServerError"/> on its refusals; a 0x01A7 with a code and no start is a silent refusal
    /// (<see cref="ActionOutcomeKind.ServerError"/>, the code kept). Time limit 20 s on the session clock from the send (the approach can take up to 15 s). Refused at once for a power outside 0 to 1 (or not finite), height outside 1 to 3, or no target.
    /// </summary>
    /// <param name="target">The target's object id.</param>
    /// <param name="height">The attack height.</param>
    /// <param name="power">The power bar, 0 to 1 (ACE clamps it).</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle Attack(uint target, AttackHeight height, float power, ActionSource source = ActionSource.Host);

    /// <summary>
    /// Starts a missile attack on <paramref name="target"/>: GameAction 0x000A TargetedMissileAttack {target, height, accuracy} (ACE W/Player_Missile.cs). Like <see cref="Attack"/> one call gives a run of shots (capture.md C3).
    /// Completes <see cref="ActionOutcomeKind.Confirmed"/> on the first launch (the sequence's start, the held ammunition's stack falling, or the arrow's F745 taken for ours by the war-spell projectile rule),
    /// <see cref="ActionOutcomeKind.ServerError"/> on a refusal (out of range, out of ammunition, busy). Time limit 10 s. Refused at once for an accuracy outside 0 to 1 (or not finite), height outside 1 to 3, or no target.
    /// </summary>
    /// <param name="target">The target's object id.</param>
    /// <param name="height">The attack height.</param>
    /// <param name="accuracy">The accuracy bar, 0 to 1 (ACE clamps it).</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle MissileAttack(uint target, AttackHeight height, float accuracy, ActionSource source = ActionSource.Host);

    /// <summary>
    /// Cancels the attack sequence: GameAction 0x01B7 CancelAttack (ACE <c>Player.HandleActionCancelAttack</c>, W/Player_Melee.cs 240-250). The swing in progress finishes first; the sequence ends with GameEvent 0x01A7 {0x0036}, which completes
    /// this <see cref="ActionOutcomeKind.Confirmed"/>. With no sequence running ACE answers nothing and this completes at once as <see cref="ActionOutcomeKind.Sent"/>. Time limit 5 s.
    /// </summary>
    /// <param name="source">Who is asking.</param>
    ActionHandle CancelAttack(ActionSource source = ActionSource.Host);

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

    /// <summary>
    /// Why ACE would answer a new action "You're too busy" (WeenieError 0x001D) now, by this API's own lane rule, or null when nothing holds (M4 P5b fix FC-a1). ACE's busy state is the player's, not a lane's
    /// (<c>Player.IsBusy</c>, @95ef426d <c>Player_Inventory.cs</c> 759-782: a pickup sets it until the return stance and one more motion length; <c>Player_Magic.cs</c> 375-383 <c>VerifyBusy</c>), while the lanes serialise only their own
    /// actions: a cast sent while the Inventory lane still holds a corpse pickup's chain was refused 0x001D live (P5b gate round 1, the mage's death station). The reasons, in order: an action of the Inventory, Heal, Use or Magic lane
    /// in flight; one of those lanes held after its outcome (a move's chain, 1.35 s after a corpse pickup's return stance, a heal's Ready, a container's or a cast's UseDone); the wait after an own teleport until the LoginComplete.
    /// A caller that must not send into a busy chain (<c>RecallHome</c>) waits on it, bounded; it never retries a refusal.
    /// </summary>
    string? BusyReason { get; }

    // ---- M4 3.2 h1: the shortcut bar's actions (appended) ----

    /// <summary>
    /// Puts an item the player holds on the shortcut bar (GameAction 0x019C {slot, item, spell 0}; retail <c>gmToolbarUI::AddShortcut</c>, 11.6096 0x004BE630, through <c>CreateShortcutToItem</c> 0x004BE750; ACE <c>Player.HandleActionAddShortcut</c>).
    /// ACE answers nothing and keeps the list for the next login, so the action completes as <see cref="ActionOutcomeKind.Sent"/> when it is sent and <see cref="SelfState.Shortcuts"/> changes at once. Retail's rules: an item already on the bar
    /// moves (RemoveShortcut for its old slot is sent first); a slot that is occupied is cleared first and its occupant goes to the first empty slot to the right (<c>GetFirstEmptyShortcutToTheRightOf</c> 0x004BE1F0; with none free it leaves the bar, and a
    /// RemoveShortcut for the slot is the only send); the item already in that slot is left alone (nothing is sent). Lane Immediate, no limit.
    /// <para>Refused at once for item 0, a slot outside 0 to 17 other than <see cref="ShortcutBar.AutoSlot"/> (the first empty slot; refused when there is none: "There are no free shortcut slots"), and an item the player does not hold or a pyreal stack
    /// (<c>gmToolbarUI::IsShortcutEligible</c> 0x004BDE60 and <c>ACCWeenieObject::IsCoinstack</c> 0x004BDEE0; the rest of eligibility is unread: <c>TODO(m4-shortcut-eligible-flags)</c>), and offline.</para>
    /// </summary>
    /// <param name="slot">The slot, 0 to 17 (<see cref="ShortcutBar.AutoSlot"/> for the first empty one).</param>
    /// <param name="item">The item's guid.</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle AddShortcut(int slot, uint item, ActionSource source = ActionSource.Host);

    /// <summary>
    /// Empties a shortcut slot (GameAction 0x019D {slot}; retail <c>gmToolbarUI::RemoveShortcut</c>, 0x004BE0E0 and <c>RemoveShortcutInSlotNum</c> 0x004BE180). ACE answers nothing, so the action completes as <see cref="ActionOutcomeKind.Sent"/> when it
    /// is sent and <see cref="SelfState.Shortcuts"/> changes at once. A slot that is already empty is refused (nothing to remove; no send). Refused at once for a slot outside 0 to 17, and offline. Lane Immediate, no limit.
    /// </summary>
    /// <param name="slot">The slot, 0 to 17.</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle RemoveShortcut(int slot, ActionSource source = ActionSource.Host);

    /// <summary>
    /// Uses the item on a shortcut slot (<c>gmToolbarUI::UseShortcut</c>, 11.6096 0x004BDFE0, which uses the slot's object as a click on it does). It routes to the action that does the job and returns that action's handle, so the caller sees its outcome:
    /// a healing kit is <see cref="UseWithTarget"/> on the player, a potion or food is <see cref="Drink"/>, anything else is <see cref="Use"/> (a wieldable item: whatever Use does today). The client has no WeenieType, so the kit and the potion are told by
    /// their weenie class and name (<c>ShortcutRouting</c>, <c>TODO(m4-shortcut-classify)</c>). An empty slot, a slot out of range and an object the player does not hold are refused at once; a spell shortcut (a spell and no object) is
    /// <see cref="ActionOutcomeKind.NotImplemented"/> (<c>TODO(m4-shortcut-spell)</c>). The handle of a refusal has kind <see cref="ActionKind.UseShortcut"/>.
    /// </summary>
    /// <param name="slot">The slot, 0 to 17.</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle UseShortcut(int slot, ActionSource source = ActionSource.Host);
    /// <summary>
    /// Asks the server to appraise an object (GameAction 0x00C8 Identify; M4 3.2 s2), the item list's examine and the paperdoll's figure click. ACE has no range or busy check: it finds the object in the pack, worn,
    /// on the landblock, in the open container or vendor stock, and answers with one GameEvent 0x00C9 1 to 10 ms later (capture.md C3), so the Query lane (rate-limited, no hold) carries it. Completes
    /// <see cref="ActionOutcomeKind.Confirmed"/> on the next 0x00C9 for the guid, whatever it says: <see cref="IdentifyHandle.Result"/> reads <see cref="IdentifyResultKind.Appraised"/>, <see cref="IdentifyResultKind.Failed"/>
    /// (a creature's failed roll: the detail says "the roll failed", the tables are kept, the profiles are withheld) or <see cref="IdentifyResultKind.NotFound"/> (flags 0, success 0), and
    /// <see cref="IGameSessionView.TryGetAppraisal"/> holds the answer. A repeat within 5 s of a failed answer on the same guid is answered failed by ACE with no roll (<see cref="IdentifyResult.Cooldown"/>); a repeat on the
    /// current successful target is answered again with no roll. Two Identifies of one guid leave one after the other, so each takes its own answer. 5 s timeout. Refused at once for guid 0 (ACE clears its target and answers
    /// nothing, spec 5.6) and offline.
    /// </summary>
    /// <param name="guid">The object's guid.</param>
    /// <param name="source">Who is asking.</param>
    IdentifyHandle Identify(uint guid, ActionSource source = ActionSource.Host);

    /// <summary>
    /// Opens a vendor (M4 3.2 s1): GameAction 0x0036 Use on the vendor, as <see cref="Use"/> (lane Use; one vendor action at a time). ACE turns the player to the vendor, answers UseDone 0 about 0.8 s later and sends the stock
    /// (GameEvent 0x0062) 0.65 to 1.04 s after that (capture.md C1, C2), so it is <see cref="ActionOutcomeKind.Confirmed"/> on the 0062 for <paramref name="vendor"/> (<see cref="IGameSessionView.Vendor"/> then holds it), not on the UseDone.
    /// A UseDone with a code is a <see cref="ActionOutcomeKind.ServerError"/>; a UseDone 0 with no 0062 for the vendor within <c>ActionPolicy.OpenVendorMarginSeconds</c> is <see cref="ActionOutcomeKind.Refused"/>
    /// "vendor did not open" (a closed shop, or a walk that failed: both a bare UseDone 0). 20 s. The player must be within the vendor's use radius: this does not walk through a door (<c>TODO(m4-vendor-approach)</c>).
    /// The default body throws: a view that keeps no vendor (a test fake) does not implement it.
    /// </summary>
    /// <param name="vendor">The vendor's object id.</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle OpenVendor(uint vendor, ActionSource source = ActionSource.Host) => throw new NotSupportedException("IActionApi.OpenVendor is not implemented by this view (M4 3.2 s1)");

    /// <summary>
    /// Buys from a vendor (M4 3.2 s1): GameAction 0x005F {vendor, count, count x {i32 amount, u32 item}}, the items named by the guids of <see cref="VendorStock.Entries"/>. Lane Use (one vendor action at a time). Complete at ACE's UseDone:
    /// <see cref="ActionOutcomeKind.Confirmed"/> iff no GameEvent 0x00A0 arrived since the send, then the pyreals paid (CoinValue before minus after) are compared with <see cref="VendorStock.PriceToBuy(uint, uint)"/> over the open stock and the F745 of each
    /// item the server announced is awaited for 5 s; a mismatch or a missing object is a WARN in the outcome's detail and in <see cref="VendorTradeHandle.Result"/>, never a failure. A 00A0 is a <see cref="ActionOutcomeKind.ServerError"/> with code 0 and
    /// the reason (the last 02EB, 028B or F7E0 line; none for too few pyreals: the detail then names the shortfall); a UseDone with a code (0x001D busy, 0x0038 no vendor, 0x0506 trading) is a ServerError with it. 10 s. ACE rejects the whole buy for
    /// an invalid amount and skips an unknown guid silently. Refused at once for no vendor, no item, a guid of 0 or an amount of 0.
    /// The default body throws: a view that keeps no vendor does not implement it.
    /// </summary>
    /// <param name="vendor">The vendor's object id.</param>
    /// <param name="items">What to buy: the stock guid and how many.</param>
    /// <param name="source">Who is asking.</param>
    VendorTradeHandle Buy(uint vendor, IReadOnlyList<(uint item, uint amount)> items, ActionSource source = ActionSource.Host) => throw new NotSupportedException("IActionApi.Buy is not implemented by this view (M4 3.2 s1)");

    /// <summary>
    /// Sells to a vendor (M4 3.2 s1): GameAction 0x0060, the same layout as <see cref="Buy"/> with the player's items; the amount sent is each item's whole stack, because ACE sells and pays the whole stack whatever the amount says
    /// (an item the model does not hold is sent with 1). Lane Use. Complete at the UseDone, as <see cref="Buy"/>: Confirmed iff no 00A0 since the send, the payout (CoinValue after minus before) compared with the sum of
    /// <see cref="VendorStock.PayoutFor(WorldObject)"/> over the items the vendor's mask takes. An item ACE will not take is dropped with a 02EB line ("The X is unsellable.") and the sale goes on; a sale of nothing is the 00A0 (ServerError, code 0,
    /// the reason). Refused at once for no vendor, no item or a guid of 0. The default body throws: a view that keeps no vendor does not implement it.
    /// </summary>
    /// <param name="vendor">The vendor's object id.</param>
    /// <param name="items">The player's items to sell.</param>
    /// <param name="source">Who is asking.</param>
    VendorTradeHandle Sell(uint vendor, IReadOnlyList<uint> items, ActionSource source = ActionSource.Host) => throw new NotSupportedException("IActionApi.Sell is not implemented by this view (M4 3.2 s1)");

    // ---- M5 6 w2: the first retail tranche (DoD 11). Each has a throwing default: a view that cannot send it does not implement it. ----

    /// <summary>
    /// Answers the server's open question (<see cref="GameState.PendingConfirmation"/>; GameAction 0x0275 {type, context, accepted}, retail <c>CM_Character::Event_ConfirmationResponse</c>,
    /// 11.6096 0x006A2030). ACE sends nothing back for an answer, so it completes as <see cref="ActionOutcomeKind.Sent"/>, and the question closes locally at once
    /// (<see cref="ConfirmationClosed"/>, <see cref="ConfirmationCloseReason.Answered"/>). Refused at once outside the world, and when no question of that type and context is open. Lane Immediate.
    /// </summary>
    /// <param name="type">The question's type word (<see cref="PendingConfirmation.Type"/>).</param>
    /// <param name="context">The question's context word (<see cref="PendingConfirmation.Context"/>).</param>
    /// <param name="accepted">Yes or no.</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle RespondToConfirmation(uint type, uint context, bool accepted, ActionSource source = ActionSource.Host) =>
        throw new NotSupportedException("IActionApi.RespondToConfirmation is not implemented by this view (M5 6)");

    /// <summary>
    /// Sets both character option words (D32; GameAction 0x01A1, retail <c>CM_Character::Event_CharacterOptionsEvent</c>, 11.6096 0x006A1EE0): the block goes out built from the read model
    /// (the filters, the shortcut bar, the favourite bars, the desired components), and once it is sent the reducer applies the words as the character's own
    /// (<see cref="PlayerQualities.CharacterOptions1"/>, <see cref="PlayerQualities.CharacterOptions2"/>; any <see cref="SetSessionOption"/> override is dropped) and publishes
    /// <see cref="QualitiesChanged"/> (<see cref="QualityChange.Options"/>). ACE stores the words and answers nothing; the next PlayerDescription returns them. Completes as
    /// <see cref="ActionOutcomeKind.Sent"/>. Refused at once outside the world and before the description arrived. Lane Immediate.
    /// </summary>
    /// <param name="options1">The first word (retail's <c>PlayerModule::options_</c>).</param>
    /// <param name="options2">The second word.</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle SetCharacterOptions(uint options1, uint options2, ActionSource source = ActionSource.Host) =>
        throw new NotSupportedException("IActionApi.SetCharacterOptions is not implemented by this view (M5 6)");

    /// <summary>
    /// <c>/lifestone</c>: GameAction 0x0063 TeleToLifestone (retail <c>CM_Character::Event_TeleToLifestone</c>, 11.6096 0x006A29B0). ACE plays the recall motion and then teleports the
    /// player to the sanctuary (<c>Player_Location.cs:132-195</c>, @95ef426d): <see cref="ActionOutcomeKind.Confirmed"/> on the teleport; a refusal (WeenieError YoureTooBusy,
    /// YouHaveBeenInPKBattleTooRecently, ExitTrainingAcademyToUseCommand or YouHaveMovedTooFar, or the line "Your spirit has not been attuned to a sanctuary location.") is a
    /// <see cref="ActionOutcomeKind.ServerError"/> naming it; 38 s (ACE's delay is the motion's length, not read here: <c>TODO(m5-6-recall-anim-length)</c>). Never retried. Lane Magic.
    /// </summary>
    /// <param name="source">Who is asking.</param>
    ActionHandle RecallLifestone(ActionSource source = ActionSource.Host) =>
        throw new NotSupportedException("IActionApi.RecallLifestone is not implemented by this view (M5 6)");

    /// <summary>
    /// <c>/marketplace</c>: GameAction 0x028D TeleToMarketPlace (retail <c>CM_Character::Event_TeleToMarketplace</c>, 11.6096 0x006A2A40). ACE plays the recall motion, waits 14 s and
    /// teleports the player to the marketplace (<c>Player_Location.cs:199-262</c>, @95ef426d): <see cref="ActionOutcomeKind.Confirmed"/> on the teleport; a refusal (as
    /// <see cref="RecallLifestone"/>'s WeenieErrors, plus OlthoiCanOnlyRecallToLifestone) is a <see cref="ActionOutcomeKind.ServerError"/> naming it; 24 s. Never retried. Lane Magic.
    /// </summary>
    /// <param name="source">Who is asking.</param>
    ActionHandle RecallMarketplace(ActionSource source = ActionSource.Host) =>
        throw new NotSupportedException("IActionApi.RecallMarketplace is not implemented by this view (M5 6)");

    /// <summary>
    /// <c>/e</c>: GameAction 0x01DF Emote (retail <c>CM_Communication::Event_Emote</c>, 11.6096 0x006A4F40). ACE echoes the emote to the player as GameMessage 0x01E0
    /// (<c>Player.cs:822-831</c>, @95ef426d), which makes it <see cref="ActionOutcomeKind.Confirmed"/>; the gag line is a <see cref="ActionOutcomeKind.ServerError"/>. 5 s. Refused at once for an
    /// empty text. Lane Chat (rate-limited with Talk and Tell).
    /// </summary>
    /// <param name="text">The emote, as typed after <c>/e</c>.</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle Emote(string text, ActionSource source = ActionSource.Host) =>
        throw new NotSupportedException("IActionApi.Emote is not implemented by this view (M5 6)");

    /// <summary>
    /// <c>/afk</c>: away with <paramref name="message"/> sends GameAction 0x0010 SetAfkMessage then 0x000F SetAfkMode 1; away without one, or back, sends 0x000F alone (retail
    /// <c>CM_Communication::Event_SetAFKMessage</c> 11.6096 0x006A5260, <c>Event_SetAFKMode</c> 0x006A4D00). ACE answers every 0x000F with GameMessage 0x02D1 Afk
    /// (<c>Player_Networking.cs:438-443</c>, @95ef426d): <see cref="ActionOutcomeKind.Confirmed"/> when <see cref="PlayerQualities.Afk"/> reads <paramref name="afk"/> after it. 5 s. Lane Chat.
    /// </summary>
    /// <param name="afk">True to go away, false to come back.</param>
    /// <param name="message">The away message, or null for none (ACE then keeps its default line); ignored when coming back.</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle SetAfk(bool afk, string? message, ActionSource source = ActionSource.Host) =>
        throw new NotSupportedException("IActionApi.SetAfk is not implemented by this view (M5 6)");

    /// <summary>
    /// Writes (or with an empty <paramref name="inscription"/>, clears) the inscription of <paramref name="item"/>: GameAction 0x00BF SetInscription (retail <c>CM_Writing::Event_SetInscription</c>, 11.6096 0x006AA810, sent by the
    /// examination window's inscription box when it loses the focus, <c>ItemExamineUI::HandleInscriptionLosingFocus</c> 0x004AE9A0; M5 7 D137). ACE answers nothing (<c>Player_Inventory.cs</c> 3634ff
    /// <c>HandleActionSetInscription</c>, @95ef426d: an inscribable item in the player's inventory or equipped, uninscribed or inscribed by this character), so it completes <see cref="ActionOutcomeKind.Sent"/>; the next appraisal shows it.
    /// Refused at once for item 0. Lane Immediate.
    /// </summary>
    /// <param name="item">The item's guid.</param>
    /// <param name="inscription">The text; empty clears the inscription and the scribe.</param>
    /// <param name="source">Who is asking.</param>
    ActionHandle SetInscription(uint item, string inscription, ActionSource source = ActionSource.Ui) =>
        throw new NotSupportedException("IActionApi.SetInscription is not implemented by this view (M5 7)");
}
