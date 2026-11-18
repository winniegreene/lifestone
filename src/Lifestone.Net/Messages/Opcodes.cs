namespace Lifestone.Net.Messages;

/// <summary>
/// The top-level message opcodes M2 speaks (docs/protocol.md §5.1, plus the chat, event and self opcodes m2-plan §1.6
/// item 24 adds). The first u32 of every reassembled message.
/// </summary>
public static class MessageOpcodes
{
    /// <summary>S→C EmoteText.</summary>
    public const uint EmoteText = 0x01E0;

    /// <summary>S→C SoulEmote.</summary>
    public const uint SoulEmote = 0x01E2;

    /// <summary>S→C HearSpeech.</summary>
    public const uint HearSpeech = 0x02BB;

    /// <summary>S→C HearRangedSpeech.</summary>
    public const uint HearRangedSpeech = 0x02BC;

    /// <summary>S→C PrivateUpdatePropertyInt.</summary>
    public const uint PrivateUpdatePropertyInt = 0x02CD;

    /// <summary>S→C PrivateUpdatePropertyInt64.</summary>
    public const uint PrivateUpdatePropertyInt64 = 0x02CF;

    /// <summary>S→C PrivateUpdateSkill.</summary>
    public const uint PrivateUpdateSkill = 0x02DD;

    /// <summary>S→C PrivateUpdateAttribute.</summary>
    public const uint PrivateUpdateAttribute = 0x02E3;

    /// <summary>S→C PrivateUpdateVital (ranks, start, experience and current value).</summary>
    public const uint PrivateUpdateVital = 0x02E7;

    /// <summary>S→C PrivateUpdateAttribute2ndLevel (a vital's current value only).</summary>
    public const uint PrivateUpdateAttribute2ndLevel = 0x02E9;

    /// <summary>S→C InventoryRemoveObject (ACE <c>GameMessageInventoryRemoveObject</c>; M3 1.2 s0).</summary>
    public const uint InventoryRemoveObject = 0x0024;

    /// <summary>S→C SetStackSize (ACE <c>GameMessageSetStackSize</c>; M3 1.2 s0).</summary>
    public const uint SetStackSize = 0x0197;

    /// <summary>S→C PlayerKilled (ACE <c>GameMessagePlayerKilled</c>; M3 1.2 s0).</summary>
    public const uint PlayerKilled = 0x019E;

    /// <summary>S→C PublicUpdatePropertyInt (ACE <c>GameMessagePublicUpdatePropertyInt</c>; M3 1.2 s0).</summary>
    public const uint PublicUpdatePropertyInt = 0x02CE;

    /// <summary>S→C PublicUpdateInstanceId (ACE <c>GameMessagePublicUpdateInstanceID</c>; M3 1.2 s0).</summary>
    public const uint PublicUpdateInstanceId = 0x02DA;

    /// <summary>S→C PublicUpdatePropertyInt64 {u8 sequence, u32 object, u32 key, i64 value} (ACE <c>GameMessagePublicUpdatePropertyInt64</c>; M5 6 w1).</summary>
    public const uint PublicUpdatePropertyInt64 = 0x02D0;

    /// <summary>S→C PrivateUpdatePropertyBool {u8 sequence, u32 key, u32 value} (ACE <c>GameMessagePrivateUpdatePropertyBool</c>; M5 6 w1).</summary>
    public const uint PrivateUpdatePropertyBool = 0x02D1;

    /// <summary>S→C PublicUpdatePropertyBool {u8 sequence, u32 object, u32 key, u32 value} (ACE <c>GameMessagePublicUpdatePropertyBool</c>; M5 6 w1).</summary>
    public const uint PublicUpdatePropertyBool = 0x02D2;

    /// <summary>S→C PrivateUpdatePropertyFloat. ACE never sends it (the class <c>GameMessagePrivateUpdatePropertyFloat</c> has no caller at 95ef426d), so there is no decoder: it reaches OnUnknown (M5 6 w1).</summary>
    public const uint PrivateUpdatePropertyFloat = 0x02D3;

    /// <summary>S→C PublicUpdatePropertyFloat {u8 sequence, u32 object, u32 key, f64 value} (ACE <c>GameMessagePublicUpdatePropertyFloat</c>; M5 6 w1).</summary>
    public const uint PublicUpdatePropertyFloat = 0x02D4;

    /// <summary>S→C PrivateUpdatePropertyString. ACE never sends it (the class <c>GameMessagePrivateUpdatePropertyString</c> has no caller at 95ef426d), so there is no decoder: it reaches OnUnknown (M5 6 w1).</summary>
    public const uint PrivateUpdatePropertyString = 0x02D5;

    /// <summary>S→C PublicUpdatePropertyString {u8 sequence, u32 key, u32 object, pad, String16L value}: the key comes BEFORE the object here, unlike the other public updates (ACE <c>GameMessagePublicUpdatePropertyString</c>; M5 6 w1).</summary>
    public const uint PublicUpdatePropertyString = 0x02D6;

    /// <summary>S→C PrivateUpdatePropertyDataId {u8 sequence, u32 key, u32 value} (ACE <c>GameMessagePrivateUpdateDataID</c>, sent for a player's MotionTable at <c>Player_Character.cs:556</c>; M5 6 w1).</summary>
    public const uint PrivateUpdatePropertyDataId = 0x02D7;

    /// <summary>S→C PublicUpdatePropertyDataId {u8 sequence, u32 object, u32 key, u32 value} (ACE <c>GameMessagePublicUpdatePropertyDataID</c>, sent by <c>Player.UpdateProperty(obj, PropertyDataId, ...)</c>, <c>Player_Properties.cs:1165</c>, for tailoring and core plating; the lead's capture note said ACE never sends it, but it does; M5 6 w1).</summary>
    public const uint PublicUpdatePropertyDataId = 0x02D8;

    /// <summary>S→C PrivateUpdatePosition {u8 sequence, u32 position type, 32-byte position} (ACE <c>GameMessagePrivateUpdatePosition</c>; seen in the recordings once, LastOutsideDeath; M5 6 w1).</summary>
    public const uint PrivateUpdatePosition = 0x02DB;

    /// <summary>S→C PrivateUpdatePropertyInstanceId {u8 sequence, u32 key, u32 value} (ACE <c>GameMessagePrivateUpdateInstanceID</c>; CurrentAttacker in a fight; M5 6 w1).</summary>
    public const uint PrivateUpdatePropertyInstanceId = 0x02D9;

    /// <summary>S→C AdminEnvirons (ACE <c>GameMessageAdminEnvirons</c>; M3 1.2 s0).</summary>
    public const uint AdminEnvirons = 0xEA60;

    /// <summary>S→C ObjDescEvent.</summary>
    public const uint ObjDescEvent = 0xF625;

    /// <summary>S→C CharacterCreateResponse, also the answer to CharacterRestore.</summary>
    public const uint CharacterCreateResponse = 0xF643;

    /// <summary>C→S and S→C CharacterLogOff.</summary>
    public const uint CharacterLogOff = 0xF653;

    /// <summary>C→S CharacterDelete {account, list index}; S→C the same opcode, empty, as the acknowledgement.</summary>
    public const uint CharacterDelete = 0xF655;

    /// <summary>C→S CharacterCreate.</summary>
    public const uint CharacterCreate = 0xF656;

    /// <summary>C→S CharacterEnterWorld.</summary>
    public const uint CharacterEnterWorld = 0xF657;

    /// <summary>S→C CharacterList.</summary>
    public const uint CharacterList = 0xF658;

    /// <summary>S→C CharacterError.</summary>
    public const uint CharacterError = 0xF659;

    /// <summary>S→C ObjectCreate.</summary>
    public const uint ObjectCreate = 0xF745;

    /// <summary>S→C PlayerCreate.</summary>
    public const uint PlayerCreate = 0xF746;

    /// <summary>S→C ObjectDelete.</summary>
    public const uint ObjectDelete = 0xF747;

    /// <summary>S→C UpdatePosition.</summary>
    public const uint UpdatePosition = 0xF748;

    /// <summary>S→C ParentEvent.</summary>
    public const uint ParentEvent = 0xF749;

    /// <summary>S→C PickupEvent.</summary>
    public const uint PickupEvent = 0xF74A;

    /// <summary>S→C SetState.</summary>
    public const uint SetState = 0xF74B;

    /// <summary>S→C UpdateMotion (MovementEvent).</summary>
    public const uint UpdateMotion = 0xF74C;

    /// <summary>S→C VectorUpdate.</summary>
    public const uint VectorUpdate = 0xF74E;

    /// <summary>S→C Sound.</summary>
    public const uint Sound = 0xF750;

    /// <summary>S→C PlayerTeleport.</summary>
    public const uint PlayerTeleport = 0xF751;

    /// <summary>S→C PlayEffect (a physics script type and intensity on an object; retail's PlayScriptType).</summary>
    public const uint PlayEffect = 0xF755;

    /// <summary>S→C GameEvent envelope.</summary>
    public const uint GameEvent = 0xF7B0;

    /// <summary>C→S GameAction envelope.</summary>
    public const uint GameAction = 0xF7B1;

    /// <summary>S→C AccountBanned.</summary>
    public const uint AccountBanned = 0xF7C1;

    /// <summary>C→S CharacterEnterWorldRequest (opcode only).</summary>
    public const uint CharacterEnterWorldRequest = 0xF7C8;

    /// <summary>C→S CharacterRestore {GUID}; answered with <see cref="CharacterCreateResponse"/>.</summary>
    public const uint CharacterRestore = 0xF7D9;

    /// <summary>S→C UpdateObject (a forced re-create; the same body as <see cref="ObjectCreate"/>).</summary>
    public const uint UpdateObject = 0xF7DB;

    /// <summary>S→C AccountBoot.</summary>
    public const uint AccountBoot = 0xF7DC;

    /// <summary>S→C CharacterEnterWorldServerReady (opcode only).</summary>
    public const uint CharacterEnterWorldServerReady = 0xF7DF;

    /// <summary>S→C ServerMessage (system chat; carries the server mod's tool replies).</summary>
    public const uint ServerMessage = 0xF7E0;

    /// <summary>S→C ServerName.</summary>
    public const uint ServerName = 0xF7E1;

    /// <summary>S→C DDD_Interrogation.</summary>
    public const uint DddInterrogation = 0xF7E5;

    /// <summary>C→S DDD_InterrogationResponse.</summary>
    public const uint DddInterrogationResponse = 0xF7E6;

    /// <summary>S→C DDD_BeginDDD (a data mismatch: the session fails fast).</summary>
    public const uint DddBegin = 0xF7E7;

    /// <summary>S→C DDD_EndDDD (the data check is complete).</summary>
    public const uint DddEnd = 0xF7EA;
}

/// <summary>The GameEvent types (inside <see cref="MessageOpcodes.GameEvent"/>) M2 decodes.</summary>
public static class GameEventTypes
{
    /// <summary>PopupString.</summary>
    public const uint PopupString = 0x0004;

    /// <summary>PlayerDescription.</summary>
    public const uint PlayerDescription = 0x0013;

    /// <summary>UpdateHealth (the answer to QueryHealth).</summary>
    public const uint UpdateHealth = 0x01C0;

    /// <summary>UseDone.</summary>
    public const uint UseDone = 0x01C7;

    /// <summary>WeenieError.</summary>
    public const uint WeenieError = 0x028A;

    /// <summary>WeenieErrorWithString.</summary>
    public const uint WeenieErrorWithString = 0x028B;

    /// <summary>Tell.</summary>
    public const uint Tell = 0x02BD;

    /// <summary>CommunicationTransientString.</summary>
    public const uint TransientString = 0x02EB;

    /// <summary>FriendsListUpdate (ACE <c>GameEventFriendsListUpdate</c>; M5 6 w1).</summary>
    public const uint FriendsListUpdate = 0x0021;

    /// <summary>CharacterTitle (ACE <c>GameEventCharacterTitle</c>; M5 6 w1).</summary>
    public const uint CharacterTitle = 0x0029;

    /// <summary>CharacterConfirmationRequest {u32 ConfirmationType, u32 context, String16L text} (ACE <c>GameEventConfirmationRequest</c>; M5 6 w1).</summary>
    public const uint CharacterConfirmationRequest = 0x0274;

    /// <summary>CharacterConfirmationDone {u32 ConfirmationType, u32 context} (ACE <c>GameEventConfirmationDone</c>; M5 6 w1).</summary>
    public const uint CharacterConfirmationDone = 0x0276;

    /// <summary>SetTurbineChatChannels, ten u32 (ACE <c>GameEventSetTurbineChatChannels</c>; M5 6 w1).</summary>
    public const uint SetTurbineChatChannels = 0x0295;

    /// <summary>InventoryPutObjInContainer (ACE <c>GameEventItemServerSaysContainId</c>; M3 1.2 s0).</summary>
    public const uint InventoryPutObjInContainer = 0x0022;

    /// <summary>WieldObject (ACE <c>GameEventWieldItem</c>; M3 1.2 s0).</summary>
    public const uint WieldObject = 0x0023;

    /// <summary>InventoryServerSaysFailed (ACE <c>GameEventType.InventoryServerSaveFailed</c>; M3 1.2 s0).</summary>
    public const uint InventoryServerSaysFailed = 0x00A0;

    /// <summary>InventoryPutObjectIn3D (ACE <c>GameEventItemServerSaysMoveItem</c>; M3 1.2 s0).</summary>
    public const uint InventoryPutObjectIn3D = 0x019A;

    /// <summary>VictimNotification (the player died; M3 1.2 s0).</summary>
    public const uint VictimNotification = 0x01AC;

    /// <summary>KillerNotification (the player killed something; M3 1.2 s0).</summary>
    public const uint KillerNotification = 0x01AD;

    /// <summary>AttackDone {u32 WeenieError} (ACE <c>GameEventAttackDone</c>; M4 1.2 s0).</summary>
    public const uint AttackDone = 0x01A7;

    /// <summary>CombatCommenceAttack (no payload; ACE <c>GameEventCombatCommenceAttack</c>; M4 1.2 s0).</summary>
    public const uint CombatCommenceAttack = 0x01B8;

    /// <summary>AttackerNotification (the player's own hit; M3 1.2 s0).</summary>
    public const uint AttackerNotification = 0x01B1;

    /// <summary>DefenderNotification (a hit on the player; M3 1.2 s0).</summary>
    public const uint DefenderNotification = 0x01B2;

    /// <summary>EvasionAttackerNotification (the player's attack was evaded; M3 1.2 s0).</summary>
    public const uint EvasionAttackerNotification = 0x01B3;

    /// <summary>EvasionDefenderNotification (the player evaded an attack; M3 1.2 s0).</summary>
    public const uint EvasionDefenderNotification = 0x01B4;

    /// <summary>MagicUpdateSpell (a spell added to the spellbook; M3 1.2 s0).</summary>
    public const uint MagicUpdateSpell = 0x02C1;

    /// <summary>MagicUpdateEnchantment (M3 1.2 s0).</summary>
    public const uint MagicUpdateEnchantment = 0x02C2;

    /// <summary>MagicRemoveEnchantment (M3 1.2 s0).</summary>
    public const uint MagicRemoveEnchantment = 0x02C3;

    /// <summary>MagicUpdateMultipleEnchantments (ACE never sends it; M3 1.2 s0).</summary>
    public const uint MagicUpdateMultipleEnchantments = 0x02C4;

    /// <summary>MagicRemoveMultipleEnchantments (ACE never sends it; M3 1.2 s0).</summary>
    public const uint MagicRemoveMultipleEnchantments = 0x02C5;

    /// <summary>MagicPurgeEnchantments (M3 1.2 s0).</summary>
    public const uint MagicPurgeEnchantments = 0x02C6;

    /// <summary>MagicDispelEnchantment (M3 1.2 s0).</summary>
    public const uint MagicDispelEnchantment = 0x02C7;

    /// <summary>MagicDispelMultipleEnchantments (M3 1.2 s0).</summary>
    public const uint MagicDispelMultipleEnchantments = 0x02C8;

    /// <summary>MagicPurgeBadEnchantments (M3 1.2 s0).</summary>
    public const uint MagicPurgeBadEnchantments = 0x0312;

    /// <summary>CloseGroundContainer {u32 container} (ACE <c>GameEventCloseGroundContainer</c>; M4 2.1 s0).</summary>
    public const uint CloseGroundContainer = 0x0052;

    /// <summary>ViewContents {u32 container, u32 count, count x {u32 item, u32 ContainerType}} (ACE <c>GameEventViewContents</c>; M4 2.1 s0).</summary>
    public const uint ViewContents = 0x0196;

    /// <summary>ApproachVendor {vendor, type mask, value range, deals magic, rates, alternate currency, stock} (ACE <c>GameEventApproachVendor</c>; M4 3.2 s0).</summary>
    public const uint ApproachVendor = 0x0062;

    /// <summary>IdentifyObjectResponse {guid, flags, success, the flagged appraisal sections} (ACE <c>GameEventIdentifyObjectResponse</c>; M4 3.2 s0).</summary>
    public const uint IdentifyObjectResponse = 0x00C9;

    /// <summary>HouseProfile {guardian, the dwelling's terms, the buy and rent lists} (ACE <c>GameEventHouseProfile</c>; M6 villa).</summary>
    public const uint HouseProfile = 0x021D;

    /// <summary>HouseData {buy time, rent time, type, free, the buy and rent lists, the position} (ACE <c>GameEventHouseData</c>; M6 villa).</summary>
    public const uint HouseData = 0x0225;

    /// <summary>HouseStatus {u32 notice} (ACE <c>GameEventHouseStatus</c>; M6 villa).</summary>
    public const uint HouseStatus = 0x0226;

    /// <summary>UpdateRentTime {u32 rent time} (ACE <c>GameEventHouseUpdateRentTime</c>; M6 villa).</summary>
    public const uint UpdateRentTime = 0x0227;

    /// <summary>UpdateRentPayment {the rent list} (ACE <c>GameEventHouseUpdateRentPayment</c>; M6 villa).</summary>
    public const uint UpdateRentPayment = 0x0228;

    /// <summary>HouseUpdateRestrictions {u8 sequence, object, RestrictionDB} (ACE <c>GameEventHouseUpdateRestrictions</c>; M6 villa).</summary>
    public const uint HouseUpdateRestrictions = 0x0248;

    /// <summary>HouseTransaction {u32 notice} (ACE <c>GameEventHouseTransaction</c>; M6 villa).</summary>
    public const uint HouseTransaction = 0x0259;

    /// <summary>AvailableHouses {type, locations, total} (ACE <c>GameEventHouseAvailableHouses</c>; M6 villa).</summary>
    public const uint AvailableHouses = 0x0271;
}

/// <summary>The GameAction types (inside <see cref="MessageOpcodes.GameAction"/>) M2 sends.</summary>
public static class GameActionTypes
{
    /// <summary>Talk {String16L text}.</summary>
    public const uint Talk = 0x0015;

    /// <summary>Use {u32 GUID}.</summary>
    public const uint Use = 0x0036;

    /// <summary>UseWithTarget {u32 item, u32 target}: the item first (<c>CM_Inventory::Event_UseWithTargetEvent</c>, 11.4186 0x006AC480, 11.6096
    /// 0x006AD3E0; ACE <c>GameActionUseWithTarget</c>; M4 3.1 s0).</summary>
    public const uint UseWithTarget = 0x0035;

    /// <summary>Tell {String16L text, String16L target name}: the message comes first (retail
    /// <c>CM_Communication::Event_TalkDirectByName</c>, 11.4186 0x006A4780, 11.6096 0x006A55A0; ACE
    /// <c>GameActionTell</c>).</summary>
    public const uint Tell = 0x005D;

    /// <summary>TalkDirect {String16L text, u32 target object id}: the message comes first (ACE <c>GameActionTalkDirect</c>,
    /// <c>GameActionTalkDirect.cs:18-19</c>, @95ef426d; M6 14). Retail's sender is the <c>Event_TalkDirect</c> docs/retail-ui.md names
    /// (the chat's Selected focus); its body and address were not read.</summary>
    public const uint TalkDirect = 0x0032;

    /// <summary>LoginComplete (no payload), sent when the client leaves portal space.</summary>
    public const uint LoginComplete = 0x00A1;

    /// <summary>QueryHealth {u32 GUID}.</summary>
    public const uint QueryHealth = 0x01BF;

    /// <summary>TargetedMeleeAttack {u32 target, u32 height, f32 power} (<c>CM_Combat::Event_TargetedMeleeAttack</c>, 11.4186
    /// 0x006A9C10, 11.6096 0x006AAB70; ACE <c>GameActionTargetedMeleeAttack</c>; M4 1.2 s0).</summary>
    public const uint TargetedMeleeAttack = 0x0008;

    /// <summary>TargetedMissileAttack {u32 target, u32 height, f32 accuracy} (<c>CM_Combat::Event_TargetedMissileAttack</c>, 11.4186
    /// 0x006A9D60, 11.6096 0x006AACC0; ACE <c>GameActionTargetedMissileAttack</c>; M4 1.2 s0).</summary>
    public const uint TargetedMissileAttack = 0x000A;

    /// <summary>CancelAttack (no payload; <c>CM_Combat::Event_CancelAttack</c>, 11.4186 0x006A99E0, 11.6096 0x006AA940; ACE
    /// <c>GameActionCancelAttack</c>; M4 1.2 s0).</summary>
    public const uint CancelAttack = 0x01B7;

    /// <summary>RaiseVital {u32 vital, u32 xp} (ACE <c>GameActionRaiseVital</c>).</summary>
    public const uint RaiseVital = 0x0044;

    /// <summary>RaiseAttribute {u32 attribute, u32 xp} (ACE <c>GameActionRaiseAttribute</c>).</summary>
    public const uint RaiseAttribute = 0x0045;

    /// <summary>RaiseSkill {u32 skill, u32 xp} (ACE <c>GameActionRaiseSkill</c>).</summary>
    public const uint RaiseSkill = 0x0046;

    /// <summary>TrainSkill {u32 skill, i32 credits} (ACE <c>GameActionTrainSkill</c>).</summary>
    public const uint TrainSkill = 0x0047;

    /// <summary>PutItemInContainer {u32 item, u32 container, u32 placement} (ACE <c>GameActionPutItemInContainer</c>).</summary>
    public const uint PutItemInContainer = 0x0019;

    /// <summary>GetAndWieldItem {u32 item, u32 equip mask} (ACE <c>GameActionGetAndWieldItem</c>).</summary>
    public const uint GetAndWieldItem = 0x001A;

    /// <summary>StackableMerge {u32 merge from, u32 merge to, i32 amount} (ACE <c>GameActionStackableMerge</c>).</summary>
    public const uint StackableMerge = 0x0054;

    /// <summary>DropItem {u32 item} (ACE <c>GameActionDropItem</c>; M4 2.1 s0).</summary>
    public const uint DropItem = 0x001B;

    /// <summary>StackableSplitToContainer {u32 stack, u32 container, i32 placement, i32 amount} (ACE <c>GameActionStackableSplitToContainer</c>; M4 2.1 s0).</summary>
    public const uint StackableSplitToContainer = 0x0055;

    /// <summary>StackableSplitTo3D {u32 stack, i32 amount} (ACE <c>GameActionStackableSplitTo3D</c>; M4 2.1 s0).</summary>
    public const uint StackableSplitTo3D = 0x0056;

    /// <summary>GiveObjectRequest {u32 target, u32 item, i32 amount} (ACE <c>GameActionGiveObjectRequest</c>; M4 2.1 s0).</summary>
    public const uint GiveObjectRequest = 0x00CD;

    /// <summary>NoLongerViewingContents {u32 container} (ACE <c>GameActionNoLongerViewingContents</c>; M4 2.1 s0).</summary>
    public const uint NoLongerViewingContents = 0x0195;

    /// <summary>StackableSplitToWield {u32 stack, u32 EquipMask, i32 amount} (ACE <c>GameActionStackableSplitToWield</c>; M4 2.1 s0).</summary>
    public const uint StackableSplitToWield = 0x019B;

    /// <summary>BuyItems {u32 vendor, u32 count, count x {i32 amount, u32 item}} (<c>CM_Vendor::Event_Buy</c>, 11.4186 0x006AA0F0, 11.6096
    /// 0x006AB050; ACE <c>GameActionBuyItems</c>; M4 3.2 s0).</summary>
    public const uint BuyItems = 0x005F;

    /// <summary>SellItems {u32 vendor, u32 count, count x {i32 amount, u32 item}} (<c>CM_Vendor::Event_Sell</c>, 11.4186 0x006AA000, 11.6096
    /// 0x006AAF60; ACE <c>GameActionSellItems</c>; M4 3.2 s0).</summary>
    public const uint SellItems = 0x0060;

    /// <summary>IdentifyObject {u32 guid} (<c>CM_Item::Event_Appraise</c>, 11.4186 0x006A8540, 11.6096 0x006A94A0; ACE
    /// <c>GameActionIdentifyObject</c>; M4 3.2 s0).</summary>
    public const uint IdentifyObject = 0x00C8;

    /// <summary>AddShortCut {u32 index, u32 object, u16 spell, u16 layer} (<c>CM_Character::Event_AddShortCut</c>, 11.4186 0x006A0EB0, 11.6096
    /// 0x006A1CD0; ACE <c>GameActionAddShortcut</c>; M4 3.2 s0).</summary>
    public const uint AddShortCut = 0x019C;

    /// <summary>RemoveShortCut {u32 index} (<c>CM_Character::Event_RemoveShortCut</c>, 11.4186 0x006A17C0, 11.6096 0x006A25E0; ACE
    /// <c>GameActionRemoveShortcut</c>; M4 3.2 s0).</summary>
    public const uint RemoveShortCut = 0x019D;

    /// <summary>CastUntargetedSpell {u32 spell id} (ACE <c>GameActionMagicCastUnTargetedSpell</c>).</summary>
    public const uint CastUntargetedSpell = 0x0048;

    /// <summary>CastTargetedSpell {u32 target, u32 spell id} (ACE <c>GameActionMagicCastTargetedSpell</c>).</summary>
    public const uint CastTargetedSpell = 0x004A;

    /// <summary>ChangeCombatMode {u32 mode} (ACE <c>GameActionChangeCombatMode</c>).</summary>
    public const uint ChangeCombatMode = 0x0053;

    /// <summary>Suicide (no payload; ACE <c>GameActionDie</c>, retail <c>CM_Character::Event_Suicide</c>, 11.6096 0x006A2920).</summary>
    public const uint Suicide = 0x0279;

    /// <summary>AddSpellFavorite {u32 spell, u32 index, u32 bar} (ACE <c>GameActionAddSpellFavorite</c>, retail <c>CM_Character::Event_AddSpellFavorite</c>, 11.6096 0x006A1D90).</summary>
    public const uint AddSpellFavorite = 0x01E3;

    /// <summary>RemoveSpellFavorite {u32 spell, u32 bar} (ACE <c>GameActionRemoveSpellFavorite</c>, retail <c>CM_Character::Event_RemoveSpellFavorite</c>, 11.4186 0x006A1890, 11.6096 0x006A26B0).</summary>
    public const uint RemoveSpellFavorite = 0x01E4;

    /// <summary>SetSingleCharacterOption {u32 option, u32 value} (<c>CM_Character::Event_PlayerOptionChangedEvent</c>, 11.6096 0x006A2330; ACE <c>GameActionSetSingleCharacterOption</c>; M5 6 w1).</summary>
    public const uint SetSingleCharacterOption = 0x0005;

    /// <summary>SetAfkMode {u32 afk} (<c>CM_Communication::Event_SetAFKMode</c>, 11.6096 0x006A4D00; ACE <c>GameActionSetAFKMode</c>; M5 6 w1).</summary>
    public const uint SetAfkMode = 0x000F;

    /// <summary>SetAfkMessage {String16L message} (<c>CM_Communication::Event_SetAFKMessage</c>, 11.6096 0x006A5260; ACE <c>GameActionSetAFKMessage</c>; M5 6 w1).</summary>
    public const uint SetAfkMessage = 0x0010;

    /// <summary>TeleToLifestone (no payload; <c>CM_Character::Event_TeleToLifestone</c>, 11.6096 0x006A29B0; ACE <c>GameActionTeleToLifestone</c>; M5 6 w1).</summary>
    public const uint TeleToLifestone = 0x0063;

    /// <summary>SetCharacterOptions, the player module block (<c>CM_Character::Event_CharacterOptionsEvent</c>, 11.6096 0x006A1EE0; ACE <c>GameActionSetCharacterOptions</c>; M5 6 w1).</summary>
    public const uint SetCharacterOptions = 0x01A1;

    /// <summary>Emote {String16L text} (<c>CM_Communication::Event_Emote</c>, 11.6096 0x006A4F40; ACE <c>GameActionEmote</c>; M5 6 w1).</summary>
    public const uint Emote = 0x01DF;

    /// <summary>SetInscription {u32 item, String16L inscription} (M5 7 D137): retail <c>CM_Writing::Event_SetInscription</c> (11.6096 0x006AA810); ACE <c>GameActionSetInscription</c>.</summary>
    public const uint SetInscription = 0x00BF;

    /// <summary>ConfirmationResponse {u32 type, u32 context, u32 accepted} (<c>CM_Character::Event_ConfirmationResponse</c>, 11.6096 0x006A2030; ACE <c>GameActionConfirmationResponse</c>; M5 6 w1).</summary>
    public const uint ConfirmationResponse = 0x0275;

    /// <summary>TeleToMarketPlace (no payload; <c>CM_Character::Event_TeleToMarketplace</c>, 11.6096 0x006A2A40; ACE <c>GameActionTeleToMarketPlace</c>; M5 6 w1).</summary>
    public const uint TeleToMarketPlace = 0x028D;

    /// <summary>BuyHouse {u32 guardian, u32 count, count x u32 item} (ACE <c>GameActionHouseBuyHouse</c>; M6 villa).</summary>
    public const uint BuyHouse = 0x021C;

    /// <summary>HouseQuery (no payload; ACE <c>GameActionHouseQuery</c>; M6 villa).</summary>
    public const uint HouseQuery = 0x021E;

    /// <summary>AbandonHouse (no payload; ACE <c>GameActionHouseAbandon</c>; M6 villa).</summary>
    public const uint AbandonHouse = 0x021F;

    /// <summary>RentHouse {u32 guardian, u32 count, count x u32 item} (ACE <c>GameActionHouseRentHouse</c>; M6 villa).</summary>
    public const uint RentHouse = 0x0221;

    /// <summary>QueryLord {u32 guardian} (ACE <c>GameActionHouseQueryLord</c>, which reads it and does nothing; M6 villa).</summary>
    public const uint QueryLord = 0x0258;

    /// <summary>TeleToHouse (no payload; <c>/house recall</c>; ACE <c>GameActionTeleToHouse</c>; M6 villa).</summary>
    public const uint TeleToHouse = 0x0262;

    /// <summary>Jump (56 bytes, retail <c>JumpPack</c>).</summary>
    public const uint Jump = 0xF61B;

    /// <summary>MoveToState.</summary>
    public const uint MoveToState = 0xF61C;

    /// <summary>AutonomousPosition.</summary>
    public const uint AutonomousPosition = 0xF753;
}
