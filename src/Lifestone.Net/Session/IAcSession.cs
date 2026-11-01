using System.Numerics;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Net.Session;

/// <summary>
/// One connection to the localhost ACE: the session state machine of docs/protocol.md §4 over the reliable transport.
/// Created by <see cref="AcSession.Create"/>; the state track's <c>SessionBootstrap</c> is the only product caller.
/// </summary>
/// <remarks>
/// <para><b>Threading.</b> Single-threaded. The first <see cref="Pump"/> binds the session to its thread (the game
/// thread); after that, <see cref="Pump"/>, <see cref="Login"/> and every send throw
/// <see cref="InvalidOperationException"/> when called from another thread. Sink callbacks and session events happen
/// inside <see cref="Pump"/> only: an event caused by a call outside it (the phase change of <see cref="Login"/>,
/// <see cref="EnterWorld"/>, <see cref="LogOff"/> or <see cref="Quit"/>) is delivered at the start of the next pump, and
/// an event caused by a message follows that message's callback. <see cref="Pump"/> is not re-entrant; the sends may be
/// called from sink callbacks. The raw tap sees an inbound message just before its dispatch and an outbound message
/// inside the call that sends it. <see cref="WaitForInboundAsync"/> may be awaited from any thread.</para>
/// <para><b>Sends</b> are non-blocking: each seals its message and hands it to the socket at once (a UDP send to
/// loopback never waits for the server), keeping it for resends, and returns its <see cref="OutboundTicket"/>. Each
/// throws <see cref="InvalidOperationException"/> when the phase does not allow it (for example EnterWorld outside
/// character select, or a second character operation while one waits for its answer) and
/// <see cref="ObjectDisposedException"/> after disposal.</para>
/// <para><b>World sends</b> (MoveToState, AutonomousPosition, Jump, Use, QueryHealth, Talk, Tell) need
/// <see cref="SessionPhase.InWorld"/>; each takes the next GameAction sequence (restarted at 1 on each world entry) and
/// goes on the weenie queue. Input the wire cannot carry throws <see cref="ArgumentException"/> before anything is sent,
/// and no sequence is used.</para>
/// </remarks>
public interface IAcSession : IDisposable
{
    /// <summary>The current phase.</summary>
    SessionPhase Phase { get; }

    /// <summary>Why the session failed, once <see cref="Phase"/> is <see cref="SessionPhase.Failed"/>; else null.</summary>
    SessionFailure? Failure { get; }

    /// <summary>The latest F658 CharacterList, including the account string requests must echo; null before the first.</summary>
    CharacterListMsg? CharacterList { get; }

    /// <summary>The player's GUID from F746 PlayerCreate; null outside the world.</summary>
    uint? PlayerGuid { get; }

    /// <summary>A snapshot of the link counters.</summary>
    LinkStats Link { get; }

    /// <summary>
    /// Starts logging in (<see cref="SessionPhase.Idle"/> → <see cref="SessionPhase.Connecting"/>): opens a socket, sends
    /// the LoginRequest and returns; progress arrives through <see cref="Pump"/>. The session copies the password once,
    /// for its retries, and zeroes the copy on reaching character select, on failure, on <see cref="Quit"/> and on
    /// disposal; the caller still owns and disposes <paramref name="credentials"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The account or password cannot go into a LoginRequest (not ASCII, or a password
    /// longer than 255 characters); the message never contains the password.</exception>
    /// <exception cref="InvalidOperationException">The session is not idle.</exception>
    void Login(AcCredentials credentials);

    /// <summary>
    /// Runs the session for up to <paramref name="budget"/> of dispatch time: admits every waiting datagram, dispatches
    /// complete messages to the sink until the budget is spent, and always runs the transport sweeps (acks, NAKs,
    /// resends, the handshake timers, the timeouts and the watchdog), even when the budget is zero. The budget applies
    /// from <see cref="SessionPhase.EnteringWorld"/> on; before that every complete message is dispatched. At least one
    /// message is dispatched per pump, so a tiny budget still makes progress; the rest wait, in order, for the next pump
    /// (<see cref="PumpResult.Pending"/>). Game thread only; not re-entrant.
    /// </summary>
    PumpResult Pump(TimeSpan budget);

    /// <summary>
    /// Completes with true as soon as a datagram (or a message left by the budget) is waiting, or with false after
    /// <paramref name="max"/> or on cancellation: lets a host without a frame loop (the headless host) sleep between
    /// pumps. The handshake's timers need pumps even when nothing arrives, so keep <paramref name="max"/> short (the
    /// ConnectResponse is resent every 0.333 s). While a login retry waits, it sleeps until the retry is due (false
    /// then, earlier than <paramref name="max"/>, so the next pump starts the attempt). When nothing can arrive (an idle,
    /// closed or failed session, or a socket whose receive loop has ended) it still sleeps for <paramref name="max"/>, so
    /// a host that loops on Pump and this never spins; only a <see cref="Timeout.InfiniteTimeSpan"/> wait, which would
    /// never end, then completes with false at once. A host checks <see cref="Phase"/> to stop.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="max"/> is negative and not
    /// <see cref="Timeout.InfiniteTimeSpan"/>.</exception>
    /// <exception cref="ObjectDisposedException">The session was disposed before the call; a wait under way when it is
    /// disposed completes with false (at the latest after <paramref name="max"/>).</exception>
    ValueTask<bool> WaitForInboundAsync(TimeSpan max, CancellationToken cancellationToken);

    /// <summary>
    /// Sends F656 CharacterCreate (character select only). The request must pass
    /// <see cref="CharacterCreateRequest.Validate"/> (exactly 55 skills, an ASCII name), else
    /// <see cref="ArgumentException"/>. The answer is F643 (<see cref="Dispatch.IInboundMessageHandler.OnCharGenResponse"/>);
    /// on Ok the character is appended to <see cref="CharacterList"/> (ACE sends no new list), and ACE's second
    /// NameInUse for a taken name is folded into the first (<see cref="SessionWarning.DuplicateAnswerFolded"/>), even
    /// when a create sent from the first NameInUse's callback is already waiting.
    /// </summary>
    OutboundTicket CreateCharacter(in CharacterCreateRequest request);

    /// <summary>
    /// Sends F655 CharacterDelete for the character at <paramref name="listIndex"/> in <see cref="CharacterList"/>
    /// (range-checked: <see cref="ArgumentOutOfRangeException"/>, and nothing is sent). The server acknowledges with
    /// F655 and sends a fresh F658, in which the character is greyed out.
    /// </summary>
    OutboundTicket DeleteCharacter(int listIndex);

    /// <summary>
    /// Sends F7D9 CharacterRestore for <paramref name="guid"/> (<see cref="ArgumentException"/> when it is not in
    /// <see cref="CharacterList"/>), with empty name and account strings as retail's character screen sends it; the
    /// answer is F643, and an Ok clears the character's greyed-out word in <see cref="CharacterList"/> (ACE sends no new
    /// list).
    /// </summary>
    OutboundTicket RestoreCharacter(uint guid);

    /// <summary>
    /// Enters the world with <paramref name="guid"/> (<see cref="ArgumentException"/> when it is not in
    /// <see cref="CharacterList"/>): F7C8, then F657 when F7DF arrives; <see cref="SessionPhase.EnteringWorld"/> until
    /// F746 PlayerCreate, then <see cref="SessionPhase.InWorld"/>. The ticket is the F7C8's.
    /// </summary>
    OutboundTicket EnterWorld(uint guid);

    /// <summary>
    /// Sends GameAction 0x00A1 LoginComplete (in world only). Never sent automatically: the client sends it on leaving
    /// portal space. Sending it again with no PlayerCreate, self ObjectCreate or PlayerTeleport in between still sends
    /// it and raises a <see cref="SessionWarning.LoginCompleteRepeated"/> warning.
    /// </summary>
    OutboundTicket SendLoginComplete();

    /// <summary>
    /// Sends GameAction 0xF61C MoveToState (in world only): <paramref name="state"/> (fields equal to retail's defaults
    /// are left out; actions are 8 bytes each), the position exactly as given (the rotation is not normalised), the four
    /// echoed sequences, and the contact and standing-long-jump bits.
    /// </summary>
    /// <exception cref="ArgumentException">More than 31 actions (the count is 5 bits); nothing is sent.</exception>
    OutboundTicket SendMoveToState(in RawMotionStateWire state, in AcPosition position, in ObjSeqs sequences, bool contact, bool standingLongJump);

    /// <summary>Sends GameAction 0xF753 AutonomousPosition (in world only): the position, the four echoed sequences and the
    /// contact byte.</summary>
    OutboundTicket SendAutonomousPosition(in AcPosition position, in ObjSeqs sequences, bool contact);

    /// <summary>Sends GameAction 0xF61B Jump, the 56-byte retail JumpPack (in world only): extent, velocity, position and the
    /// four echoed sequences.</summary>
    OutboundTicket SendJump(float extent, Vector3 velocity, in AcPosition position, in ObjSeqs sequences);

    /// <summary>Sends GameAction 0x0036 Use (in world only); ACE answers with GameEvent 0x01C7 UseDone
    /// (<see cref="Dispatch.IInboundMessageHandler.OnUseDone"/>).</summary>
    OutboundTicket SendUse(uint guid);

    /// <summary>
    /// Sends GameAction 0x0035 UseWithTarget {u32 item, u32 target} (in world only), retail <c>CM_Inventory::Event_UseWithTargetEvent</c>
    /// (11.4186 0x006AC480, 11.6096 0x006AD3E0); the item comes first, as ACE reads it. ACE answers a healing kit with the heal's messages and GameEvent
    /// 0x01C7 UseDone (<see cref="Dispatch.IInboundMessageHandler.OnUseDone"/>); a kit not held or no target is a silent UseDone 0.
    /// </summary>
    /// <param name="item">The GUID of the item used (the healing kit).</param>
    /// <param name="target">The GUID of its target (the player's own GUID to heal itself).</param>
    OutboundTicket SendUseWithTarget(uint item, uint target);

    /// <summary>Sends GameAction 0x01BF QueryHealth (in world only); ACE answers with GameEvent 0x01C0 UpdateHealth
    /// (<see cref="Dispatch.IInboundMessageHandler.OnUpdateHealth"/>).</summary>
    OutboundTicket SendQueryHealth(uint guid);

    /// <summary>
    /// Sends GameAction 0x0008 TargetedMeleeAttack {u32 target, u32 height, f32 power} (in world only), retail
    /// <c>CM_Combat::Event_TargetedMeleeAttack</c> (11.4186 0x006A9C10, 11.6096 0x006AAB70); ACE answers with GameEvent 0x01B8 on a
    /// repeat, the swing, and 0x01A7 AttackDone (<see cref="Dispatch.IInboundMessageHandler.OnAttackDone"/>).
    /// </summary>
    /// <param name="target">The target's GUID.</param>
    /// <param name="height">The attack height: 1 High, 2 Medium, 3 Low.</param>
    /// <param name="power">The power bar, 0 to 1 (ACE clamps it; NaN and infinity throw <see cref="ArgumentException"/>).</param>
    OutboundTicket SendMeleeAttack(uint target, uint height, float power);

    /// <summary>
    /// Sends GameAction 0x000A TargetedMissileAttack {u32 target, u32 height, f32 accuracy} (in world only), retail
    /// <c>CM_Combat::Event_TargetedMissileAttack</c> (11.4186 0x006A9D60, 11.6096 0x006AACC0).
    /// </summary>
    /// <param name="target">The target's GUID.</param>
    /// <param name="height">The attack height: 1 High, 2 Medium, 3 Low.</param>
    /// <param name="accuracy">The accuracy bar, 0 to 1 (ACE clamps it; NaN and infinity throw <see cref="ArgumentException"/>).</param>
    OutboundTicket SendMissileAttack(uint target, uint height, float accuracy);

    /// <summary>
    /// Sends GameAction 0x01B7 CancelAttack (no payload; in world only), retail <c>CM_Combat::Event_CancelAttack</c> (11.4186
    /// 0x006A99E0, 11.6096 0x006AA940).
    /// </summary>
    OutboundTicket SendCancelAttack();

    /// <summary>
    /// Sends GameAction 0x0045 RaiseAttribute {u32 attribute, u32 xp} (in world only), retail <c>CM_Train::Event_TrainAttribute</c>
    /// (11.4186 0x006A7F30, 11.6096 0x006A8E90); ACE answers with the private attribute update 0x02E3.
    /// </summary>
    /// <param name="attribute">The attribute id (<c>PropertyAttribute</c>, 1 to 6).</param>
    /// <param name="xp">The experience to spend: an unsigned 32-bit value (ACE reads a UInt32; the XP to level 126, 4,286,609,098, is above <see cref="int.MaxValue"/>).</param>
    OutboundTicket SendRaiseAttribute(uint attribute, uint xp);

    /// <summary>
    /// Sends GameAction 0x0044 RaiseVital {u32 vital, u32 xp} (in world only), retail <c>CM_Train::Event_TrainAttribute2nd</c>
    /// (11.4186 0x006A8040, 11.6096 0x006A8FA0); ACE answers with the private vital update 0x02E7.
    /// </summary>
    /// <param name="vital">The vital's maximum id (<c>PropertyAttribute2nd</c>: 1 health, 3 stamina, 5 mana).</param>
    /// <param name="xp">The experience to spend (unsigned 32-bit).</param>
    OutboundTicket SendRaiseVital(uint vital, uint xp);

    /// <summary>
    /// Sends GameAction 0x0046 RaiseSkill {u32 skill, u32 xp} (in world only), retail <c>CM_Train::Event_TrainSkill</c>
    /// (11.4186 0x006A8150, 11.6096 0x006A90B0); ACE answers with the private skill update 0x02DD.
    /// </summary>
    /// <param name="skill">The skill id (<c>Skill</c>).</param>
    /// <param name="xp">The experience to spend (unsigned 32-bit).</param>
    OutboundTicket SendRaiseSkill(uint skill, uint xp);

    /// <summary>
    /// Sends GameAction 0x0047 TrainSkill {u32 skill, i32 credits} (in world only), retail <c>CM_Train::Event_TrainSkillAdvancementClass</c>
    /// (11.4186 0x006A8260, 11.6096 0x006A91C0); ACE answers with the private skill update 0x02DD and the credits in 0x02CD.
    /// </summary>
    /// <param name="skill">The skill id (<c>Skill</c>).</param>
    /// <param name="credits">The skill credits to spend: signed 32-bit (ACE reads an Int32).</param>
    OutboundTicket SendTrainSkill(uint skill, int credits);

    /// <summary>
    /// Sends GameAction 0x0048 CastUntargetedSpell {u32 spell} (in world only), retail <c>CM_Magic::Event_CastUntargetedSpell</c>
    /// (11.4186 0x006A3150, 11.6096 0x006A3F70); ACE answers with the enchantment, projectile, effect and fizzle messages of the cast.
    /// </summary>
    /// <param name="spellId">The spell id (<c>SpellTable</c>).</param>
    OutboundTicket SendCastUntargetedSpell(uint spellId);

    /// <summary>
    /// Sends GameAction 0x004A CastTargetedSpell {u32 target, u32 spell} (in world only), retail <c>CM_Magic::Event_CastTargetedSpell</c>
    /// (11.4186 0x006A3040, 11.6096 0x006A3E60); the target comes first, as ACE reads it.
    /// </summary>
    /// <param name="target">The target's guid.</param>
    /// <param name="spellId">The spell id (<c>SpellTable</c>).</param>
    OutboundTicket SendCastTargetedSpell(uint target, uint spellId);

    /// <summary>
    /// Sends GameAction 0x0053 ChangeCombatMode {u32 mode} (in world only), retail <c>CM_Combat::Event_ChangeCombatMode</c>
    /// (11.4186 0x006A9A70, 11.6096 0x006AA9D0); ACE answers with the private int update 0x02CD for property 40.
    /// </summary>
    /// <param name="mode">The mode (1 non-combat, 2 melee, 4 missile, 8 magic).</param>
    OutboundTicket SendChangeCombatMode(uint mode);

    /// <summary>
    /// Sends GameAction 0x001A GetAndWieldItem {u32 item, u32 equip mask} (in world only), retail <c>CM_Inventory::Event_GetAndWieldItem</c>
    /// (11.4186 0x006AB9F0, 11.6096 0x006AC950); ACE answers with the wield messages (GE 0x0023 and the container updates).
    /// </summary>
    /// <param name="item">The item's guid.</param>
    /// <param name="equipMask">The <c>EquipMask</c> location to wield it at.</param>
    OutboundTicket SendGetAndWieldItem(uint item, uint equipMask);

    /// <summary>
    /// Sends GameAction 0x0019 PutItemInContainer {u32 item, u32 container, u32 placement} (in world only), retail
    /// <c>CM_Inventory::Event_PutItemInContainer</c> (11.4186 0x006ABD20, 11.6096 0x006ACC80); ACE answers with the container messages.
    /// </summary>
    /// <param name="item">The item's guid.</param>
    /// <param name="container">The container's guid (the player's own guid is the main pack).</param>
    /// <param name="placement">The slot in the container (ACE reads an Int32; 0 is the first slot).</param>
    OutboundTicket SendPutItemInContainer(uint item, uint container, uint placement);

    /// <summary>
    /// Sends GameAction 0x0054 StackableMerge {u32 merge from, u32 merge to, i32 amount} (in world only), retail
    /// <c>CM_Inventory::Event_StackableMerge</c> (11.4186 0x006ABE70, 11.6096 0x006ACDD0); ACE answers with the target's 0197
    /// SetStackSize (and the source's 0197, or its 0024 removal when the whole source goes), or a 00A0 failure.
    /// </summary>
    /// <param name="mergeFrom">The source stack's guid.</param>
    /// <param name="mergeTo">The target stack's guid (the same weenie class).</param>
    /// <param name="amount">How many units to move from the source to the target (ACE refuses 0 or less).</param>
    OutboundTicket SendStackableMerge(uint mergeFrom, uint mergeTo, int amount);

    /// <summary>
    /// Sends GameAction 0x0195 NoLongerViewingContents {u32 container} (in world only): the player closed a container; ACE stops
    /// sending its changes (<c>HandleActionNoLongerViewingContents</c>). TODO(m4-item-move-retail-cites): no 11.6096 sender named yet.
    /// </summary>
    /// <param name="container">The container's guid.</param>
    OutboundTicket SendNoLongerViewingContents(uint container);

    /// <summary>
    /// Sends GameAction 0x001B DropItem {u32 item} (in world only): drop the item on the ground; ACE answers with the container
    /// messages and 019A (<c>HandleActionDropItem</c>). TODO(m4-item-move-retail-cites).
    /// </summary>
    /// <param name="item">The item's guid.</param>
    OutboundTicket SendDropItem(uint item);

    /// <summary>
    /// Sends GameAction 0x0055 StackableSplitToContainer {u32 stack, u32 container, i32 placement, i32 amount} (in world only);
    /// ACE refuses an amount of 0 or less. TODO(m4-item-move-retail-cites).
    /// </summary>
    /// <param name="stack">The stack's guid.</param>
    /// <param name="container">The container to put the new stack in.</param>
    /// <param name="placement">The slot in the container (an Int32; 0 is the first slot).</param>
    /// <param name="amount">How many units to split off.</param>
    OutboundTicket SendSplitToContainer(uint stack, uint container, int placement, int amount);

    /// <summary>
    /// Sends GameAction 0x0056 StackableSplitTo3D {u32 stack, i32 amount} (in world only): split units off onto the ground; ACE
    /// refuses an amount of 0 or less. TODO(m4-item-move-retail-cites).
    /// </summary>
    /// <param name="stack">The stack's guid.</param>
    /// <param name="amount">How many units to split off.</param>
    OutboundTicket SendSplitTo3D(uint stack, int amount);

    /// <summary>
    /// Sends GameAction 0x019B StackableSplitToWield {u32 stack, u32 EquipMask, i32 amount} (in world only); ACE refuses an
    /// amount of 0 or less. TODO(m4-item-move-retail-cites).
    /// </summary>
    /// <param name="stack">The stack's guid.</param>
    /// <param name="equipMask">The <c>EquipMask</c> location to wield the split units at.</param>
    /// <param name="amount">How many units to split off.</param>
    OutboundTicket SendSplitToWield(uint stack, uint equipMask, int amount);

    /// <summary>
    /// Sends GameAction 0x00CD GiveObjectRequest {u32 target, u32 item, i32 amount} (in world only): give an item (or units of a
    /// stack) to a creature or player; ACE refuses an amount of 0 or less. TODO(m4-item-move-retail-cites).
    /// </summary>
    /// <param name="target">The receiver's guid.</param>
    /// <param name="item">The item's guid.</param>
    /// <param name="amount">How many units to give.</param>
    OutboundTicket SendGiveObject(uint target, uint item, int amount);

    /// <summary>
    /// Sends GameAction 0x005F BuyItems {u32 vendor, u32 count, count x {i32 amount, u32 item}} (in world only), retail
    /// <c>CM_Vendor::Event_Buy</c> (11.4186 0x006AA0F0, 11.6096 0x006AB050); ACE (<c>Player.HandleActionBuyItem</c>) answers the spend
    /// (0024 or 0197, 02CD), each created item (F745, 0022, 02CD), the 0062 again and UseDone, or a 00A0 refusal; too few pyreals
    /// gives no text at all. The guids are the 0062's stock guids; ACE skips an unknown one silently.
    /// </summary>
    /// <param name="vendor">The vendor's guid.</param>
    /// <param name="items">The stock guids and how many of each (ACE reads the amount as an Int32; an amount above Int32.MaxValue throws).</param>
    OutboundTicket SendBuy(uint vendor, IReadOnlyList<(uint item, uint amount)> items);

    /// <summary>
    /// Sends GameAction 0x0060 SellItems {u32 vendor, u32 count, count x {i32 amount, u32 item}} (in world only), retail
    /// <c>CM_Vendor::Event_Sell</c> (11.4186 0x006AA000, 11.6096 0x006AAF60); ACE (<c>Player.HandleActionSellItem</c>) sells and pays on
    /// the WHOLE stack whatever the amount, then answers 02CD and 0022 per item, the 0062 and the coin stacks.
    /// </summary>
    /// <param name="vendor">The vendor's guid.</param>
    /// <param name="items">The player's item guids and the amounts (ACE reads the amount as an Int32; an amount above Int32.MaxValue throws).</param>
    OutboundTicket SendSell(uint vendor, IReadOnlyList<(uint item, uint amount)> items);

    /// <summary>
    /// Sends GameAction 0x00C8 IdentifyObject {u32 guid} (in world only), retail <c>CM_Item::Event_Appraise</c> (11.4186 0x006A8540,
    /// 11.6096 0x006A94A0); ACE answers GameEvent 0x00C9 (a guid it cannot find gets flags 0 and success 0); a guid of 0 clears the
    /// appraisal target and gets no reply.
    /// </summary>
    /// <param name="guid">The object's guid.</param>
    OutboundTicket SendIdentify(uint guid);

    /// <summary>
    /// Sends GameAction 0x019C AddShortCut {u32 index, u32 objectId, u16 spellId, u16 layer} (in world only), retail
    /// <c>CM_Character::Event_AddShortCut</c> (11.4186 0x006A0EB0, 11.6096 0x006A1CD0); ACE answers nothing and keeps only the object
    /// (the list comes back in the next login's player description). The index is 0-based.
    /// </summary>
    /// <param name="index">The slot, 0-based (retail's bar has 18).</param>
    /// <param name="objectId">The object's guid.</param>
    /// <param name="spellId">The layered spell word as the description lists it (spell id in the low 16 bits, layer in the high 16); 0 for an item.</param>
    OutboundTicket SendAddShortcut(uint index, uint objectId, uint spellId);

    /// <summary>
    /// Sends GameAction 0x019D RemoveShortCut {u32 index} (in world only), retail <c>CM_Character::Event_RemoveShortCut</c> (11.4186
    /// 0x006A17C0, 11.6096 0x006A25E0); ACE answers nothing.
    /// </summary>
    /// <param name="index">The slot, 0-based.</param>
    OutboundTicket SendRemoveShortcut(uint index);

    /// <summary>
    /// Sends GameAction 0x0279 Suicide (no payload; in world only), retail <c>CM_Character::Event_Suicide</c> (11.4186 0x006A1B00, 11.6096 0x006A2920); ACE
    /// <c>GameActionDie</c> answers with five speech lines 3 s apart and then the death, or WeenieError YoureTooBusy when the player is dead or teleporting.
    /// </summary>
    OutboundTicket SendSuicide();

    /// <summary>
    /// Sends GameAction 0x01E3 AddSpellFavorite {u32 spell, u32 index, u32 bar} (in world only), retail <c>CM_Character::Event_AddSpellFavorite</c> (11.4186 0x006A0F70,
    /// 11.6096 0x006A1D90); ACE answers nothing (it ignores a bar above 7 or a spell the player does not know).
    /// </summary>
    /// <param name="spellId">The spell id.</param>
    /// <param name="index">The position on the bar (0 is the first).</param>
    /// <param name="bar">The bar (0 to 7).</param>
    OutboundTicket SendAddSpellFavorite(uint spellId, uint index, uint bar);

    /// <summary>
    /// Sends GameAction 0x01E4 RemoveSpellFavorite {u32 spell, u32 bar} (in world only), retail <c>CM_Character::Event_RemoveSpellFavorite</c> (11.4186 0x006A1890, 11.6096 0x006A26B0);
    /// ACE answers nothing and ignores a spell that is not on the bar.
    /// </summary>
    /// <param name="spellId">The spell id.</param>
    /// <param name="bar">The bar (0 to 7).</param>
    OutboundTicket SendRemoveSpellFavorite(uint spellId, uint bar);

    /// <summary>Sends GameAction 0x0015 Talk (in world only). The text is the message's last field, so any Windows-1252
    /// character may be sent.</summary>
    /// <exception cref="ArgumentException">A character Windows-1252 lacks, or a text over 65,535 bytes; nothing is sent.</exception>
    OutboundTicket SendTalk(ReadOnlySpan<char> text);

    /// <summary>Sends GameAction 0x005D Tell (in world only): the message, then the target's name, as retail's
    /// <c>CM_Communication::Event_TalkDirectByName</c> (11.4186 0x006A4780, 11.6096 0x006A55A0) packs them.</summary>
    /// <exception cref="ArgumentException">An empty target, or a message or target that is not ASCII (ACE counts UTF-8
    /// characters, so anything else would shift the target); nothing is sent.</exception>
    OutboundTicket SendTell(ReadOnlySpan<char> target, ReadOnlySpan<char> text);

    /// <summary>
    /// Logs the character off (in world only): F653, then <see cref="SessionPhase.LoggingOff"/> until the server's F653
    /// and F658 bring the session back to character select (<see cref="SessionEventKind.LoggedOff"/>), about 6 s later.
    /// </summary>
    void LogOff();

    /// <summary>
    /// Ends the session at once: in the world, F653 then a cleartext Disconnect; in another connected phase the
    /// Disconnect only. Ends in <see cref="SessionPhase.Closed"/> (through <see cref="SessionPhase.Closing"/>); a closed or
    /// failed session stays as it is. <see cref="IDisposable.Dispose"/> also sends the Disconnect when the session is
    /// still connected.
    /// </summary>
    void Quit();

    // M5 6 w2: the first retail tranche's sends (the writers are slice w1's). Each has a throwing default, so a session that cannot send
    // it (the stand-in, a test fake) says so loudly instead of sending nothing.

    /// <summary>
    /// Sends GameAction 0x0275 ConfirmationResponse {u32 type, u32 context, u32 accepted} (in world only), retail
    /// <c>CM_Character::Event_ConfirmationResponse</c> (11.6096 0x006A2030); ACE's <c>ConfirmationManager.HandleResponse</c> (@95ef426d
    /// <c>WorldObjects/Managers/ConfirmationManager.cs:89-135</c>) acts on it and answers nothing of its own.
    /// </summary>
    /// <param name="type">The request's type word, as GameEvent 0x0274 carried it.</param>
    /// <param name="context">The request's context word, unchanged.</param>
    /// <param name="accepted">Yes or no.</param>
    OutboundTicket SendConfirmationResponse(uint type, uint context, bool accepted) =>
        throw new NotSupportedException("IAcSession.SendConfirmationResponse is not implemented by this session (M5 6)");

    /// <summary>
    /// Sends GameAction 0x01A1 SetCharacterOptions with the player module block (in world only), retail
    /// <c>CM_Character::Event_CharacterOptionsEvent</c> (11.6096 0x006A1EE0); ACE stores both option words and the gameplay options and
    /// answers nothing (<c>GameActionSetCharacterOptions.cs:45-190</c>, @95ef426d).
    /// </summary>
    /// <param name="block">The block.</param>
    /// <exception cref="ArgumentException">A block the writer cannot say (see <c>MagicActionCodecs.WriteSetCharacterOptions</c>); nothing is sent.</exception>
    OutboundTicket SendSetCharacterOptions(CharacterOptionsBlock block) =>
        throw new NotSupportedException("IAcSession.SendSetCharacterOptions is not implemented by this session (M5 6)");

    /// <summary>Sends GameAction 0x0063 TeleToLifestone (no payload; in world only), retail <c>CM_Character::Event_TeleToLifestone</c> (11.6096 0x006A29B0).</summary>
    OutboundTicket SendTeleToLifestone() =>
        throw new NotSupportedException("IAcSession.SendTeleToLifestone is not implemented by this session (M5 6)");

    /// <summary>Sends GameAction 0x028D TeleToMarketPlace (no payload; in world only), retail <c>CM_Character::Event_TeleToMarketplace</c> (11.6096 0x006A2A40).</summary>
    OutboundTicket SendTeleToMarketPlace() =>
        throw new NotSupportedException("IAcSession.SendTeleToMarketPlace is not implemented by this session (M5 6)");

    /// <summary>Sends GameAction 0x01DF Emote {String16L text} (in world only), retail <c>CM_Communication::Event_Emote</c> (11.6096 0x006A4F40).</summary>
    /// <exception cref="ArgumentException">A character Windows-1252 lacks, or a text that is too long; nothing is sent.</exception>
    OutboundTicket SendEmote(ReadOnlySpan<char> text) =>
        throw new NotSupportedException("IAcSession.SendEmote is not implemented by this session (M5 6)");

    /// <summary>Sends GameAction 0x00BF SetInscription {u32 item, String16L inscription} (in world only), retail <c>CM_Writing::Event_SetInscription</c> (11.6096 0x006AA810; M5 7 D137). ACE answers nothing.</summary>
    /// <exception cref="ArgumentException">A character Windows-1252 lacks, or a text that is too long; nothing is sent.</exception>
    OutboundTicket SendSetInscription(uint item, ReadOnlySpan<char> inscription) =>
        throw new NotSupportedException("IAcSession.SendSetInscription is not implemented by this session (M5 7)");

    /// <summary>Sends GameAction 0x000F SetAfkMode {u32 afk} (in world only), retail <c>CM_Communication::Event_SetAFKMode</c> (11.6096 0x006A4D00).</summary>
    OutboundTicket SendSetAfkMode(bool afk) =>
        throw new NotSupportedException("IAcSession.SendSetAfkMode is not implemented by this session (M5 6)");

    /// <summary>Sends GameAction 0x0010 SetAfkMessage {String16L message} (in world only), retail <c>CM_Communication::Event_SetAFKMessage</c> (11.6096 0x006A5260).</summary>
    /// <exception cref="ArgumentException">A character Windows-1252 lacks, or a text that is too long; nothing is sent.</exception>
    OutboundTicket SendSetAfkMessage(ReadOnlySpan<char> message) =>
        throw new NotSupportedException("IAcSession.SendSetAfkMessage is not implemented by this session (M5 6)");
}
