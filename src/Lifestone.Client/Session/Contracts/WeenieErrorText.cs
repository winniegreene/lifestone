// Ported from ACEmulator/ACE@95ef426d Source/ACE.Entity/Enum/WeenieError.cs (AGPL-3.0)
// with changes: the table of the message text above each member (and of WeenieErrorWithString.cs, same directory), only members whose summary holds one line; "_" and the blank gap of a WithString text become the {0} slot; the text types are numbers read from the 11.6096 client's ClientCommunicationSystem::HandleFailureEvent (0x00572760).
using System.Globalization;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The text of a WeenieError (GameEvent 0x028A) or WeenieErrorWithString (0x028B) and the chat type retail prints it in. Retail's
/// <c>ClientCommunicationSystem::Handle_Communication__WeenieError</c> (11.6096 0x0057BBC0) and <c>Handle_Communication__WeenieErrorWithString</c>
/// (0x0057D960) both hand the code to <c>HandleFailureEvent</c> (0x00572760), a switch that holds the strings in the client (not in the dats) and
/// adds each to the scroll with a text type: 0x1A (the spew box), 7 (Magic) or 0. ACE sends only the code (and the string of 0x028B), so the table
/// is ACE's enum comments (the wording was tested against 11.6096, ACE's note); the types are retail's.
/// </summary>
/// <remarks>
/// M5 7 D124 (S1b): a code the client prints no line for (<see cref="RetailShows"/> false) is shown nothing and logged; a code it prints and the table lacks shows <c>WeenieError 0x%04X</c> and is logged once per code
/// (<c>TODO(m3-weenie-error-text)</c>). A shown code whose type the extraction could not read (<see cref="TypeUnknown"/>) is shown in type 0 and logged once (<c>TODO(m3-weenie-error-type)</c>).
/// </remarks>
public static class WeenieErrorText
{
    /// <summary>The type of an entry whose retail text type is not known.</summary>
    public const int TypeUnknown = -1;

    /// <summary>The chat type of a code with no text (the unknown code prints in the default type).</summary>
    public const int DefaultType = 0;

    /// <summary>The code and its text of "You're too busy!" (ACE <c>YoureTooBusy</c>).</summary>
    public const uint YoureTooBusy = 0x001D;

    /// <summary>"Your spell fizzled." (ACE <c>YourSpellFizzled</c>).</summary>
    public const uint YourSpellFizzled = 0x0402;

    /// <summary>"You don't have enough Mana to cast this spell." (ACE <c>YouDontHaveEnoughManaToCast</c>).</summary>
    public const uint YouDontHaveEnoughManaToCast = 0x0401;

    /// <summary>
    /// The line a code shows: its text with <paramref name="argument"/> in the slot, and the chat type retail uses. A code with no text answers
    /// <c>WeenieError 0x%04X</c> in <see cref="DefaultType"/> and false.
    /// </summary>
    /// <param name="code">The WeenieError code.</param>
    /// <param name="argument">The string of a 0x028B message, or null or empty.</param>
    /// <param name="chatType">The chat type (0 to 33).</param>
    /// <param name="text">The line.</param>
    /// <returns>True when the table holds the code.</returns>
    public static bool TryFormat(uint code, string? argument, out int chatType, out string text)
    {
        if (!Table.TryGetValue(code, out var entry))
        {
            chatType = DefaultType;
            text = NameOf(code) is { } name
                ? string.Create(CultureInfo.InvariantCulture, $"WeenieError 0x{code:X4} ({name})")
                : string.Create(CultureInfo.InvariantCulture, $"WeenieError 0x{code:X4}");
            return false;
        }

        chatType = entry.Type == TypeUnknown ? DefaultType : entry.Type;
        text = entry.Text.Contains("{0}", StringComparison.Ordinal) ? entry.Text.Replace("{0}", argument ?? string.Empty, StringComparison.Ordinal) : entry.Text;
        return true;
    }

    /// <summary>True when the code is in the table and its type is read from retail.</summary>
    /// <param name="code">The WeenieError code.</param>
    public static bool HasKnownType(uint code) => Table.TryGetValue(code, out var entry) && entry.Type != TypeUnknown;

    /// <summary>
    /// M5 7 F30: the ACE name (@95ef426d <c>WeenieError.cs</c>) of a code that has no message text in ACE's enum comments, so none the table can hold (the physics codes the client's MoveTo ends with): a line for such a code reads
    /// <c>WeenieError 0x003C (ITeleported)</c>, and the reducer's <c>TODO(m3-weenie-error-text)</c> carries the name. 0x003C is what ACE sent when the owner walked out of the den (a teleport cancelled the MoveTo in flight, soak round 7).
    /// </summary>
    /// <param name="code">The WeenieError code.</param>
    /// <returns>The ACE name, or null when the code has no name here.</returns>
    public static string? NameOf(uint code) => Names.TryGetValue(code, out var name) ? name : null;

    /// <summary>True when the table holds <paramref name="code"/>.</summary>
    /// <param name="code">The WeenieError code.</param>
    public static bool Knows(uint code) => Table.ContainsKey(code);

    /// <summary>
    /// M5 7 D124 (S1b, the owner: a line retail never prints is not retail): true when the 11.6096 client prints a chat line for <paramref name="code"/>. <c>ClientCommunicationSystem::HandleFailureEvent</c> (11.6096 0x00572760)
    /// is a switch on the code; a code with no case of its own goes to the shared exit (0x0057621A), which only releases the function's locals and returns, so nothing reaches the chat. The cases were read off the switch for
    /// every code 0x0000 to 0x06FF: every code of the table has a case except 0x0426, 0x04DC and 0x051D (<see cref="NoRetailCase"/>); four codes have a case and no text here (<see cref="RetailTextUnknown"/>); every other code
    /// (0x003B, 0x003C and 0x042C among them) has none. A code the client does not print is shown nothing; the log keeps it (<c>ChatLogTap</c>).
    /// </summary>
    /// <param name="code">The WeenieError code.</param>
    public static bool RetailShows(uint code) => (Table.ContainsKey(code) && !NoRetailCase.Contains(code)) || RetailTextUnknown.Contains(code);

    /// <summary>M5 7 D124 (S1b): true for a code the 11.6096 client prints but whose text the table does not hold (it shows <c>WeenieError 0x%04X</c>, <c>TODO(m3-weenie-error-text)</c>).</summary>
    /// <param name="code">The WeenieError code.</param>
    public static bool IsRetailTextUnknown(uint code) => RetailTextUnknown.Contains(code);

    /// <summary>The codes of the table that <c>HandleFailureEvent</c> (11.6096 0x00572760) has no case for: their summaries in ACE are no client text (0x04DC is a note on its use).</summary>
    private static readonly HashSet<uint> NoRetailCase = [0x0426, 0x04DC, TurbineChatIsEnabled];

    /// <summary>The codes <c>HandleFailureEvent</c> has a case for and the table no text: 0x0049 (ACE CantJumpLoadedDown, a string the client builds at run time) and 0x04B8 to 0x04BA (no summary in ACE).</summary>
    private static readonly HashSet<uint> RetailTextUnknown = [0x0049, 0x04B8, 0x04B9, 0x04BA];

    /// <summary>"You have entered the {0} channel." (ACE <c>WeenieErrorWithString.YouHaveEnteredThe_Channel</c>, WeenieErrorWithString.cs:319 @95ef426d).</summary>
    public const uint ChannelEntered = 0x051B;

    /// <summary>"You have left the {0} channel." (ACE <c>WeenieErrorWithString.YouHaveLeftThe_Channel</c>, WeenieErrorWithString.cs:324).</summary>
    public const uint ChannelLeft = 0x051C;

    /// <summary>"Turbine Chat is enabled." (ACE <c>WeenieError.TurbineChatIsEnabled</c>, WeenieError.cs:1035).</summary>
    public const uint TurbineChatIsEnabled = 0x051D;

    /// <summary>
    /// M5 7 D124: true for the chat channels' notices, which ride the failure events but are no failure. At login ACE sends 0x051D (<c>Player_Networking.cs</c>:85-88 @95ef426d, with <c>use_turbine_chat</c> on), then 0x051B with
    /// each channel's name (General, Trade, LFG; 182); 0x051C when a channel is left (209, at the logoff). The reducer files them as <see cref="ChatKind.ChannelNotice"/> lines: retail's join and leave lines
    /// ("You have entered the General channel."; the 11.6096 client holds these two texts among its failure strings, the same words as ACE's summaries), never as error lines.
    /// </summary>
    /// <param name="code">The WeenieError code.</param>
    public static bool IsChannelNotice(uint code) => code is ChannelEntered or ChannelLeft or TurbineChatIsEnabled;

    private static readonly Dictionary<uint, string> Names = new()
    {
        [0x003B] = "ILeftTheWorld",
        [0x003C] = "ITeleported",

        // M5 7 D124: TargetNotAcquired (WeenieError.cs:389 @95ef426d, no summary). Neither ACE nor the 11.6096 client holds a text for it, and HandleFailureEvent (0x00572760) has no case for it (RetailShows): the chat shows
        // nothing, the log keeps the name. ACE sends it when a cast's target is gone (Player_Magic.cs:137-140, 173-179, 197-203, 751-757): the cast path reads it as the target dying mid-cast.
        [0x042C] = "TargetNotAcquired",
    };

    private static readonly Dictionary<uint, (int Type, string Text)> Table = new()
    {
        [0x0017] = (26, "You failed to go to non-combat mode."),
        [0x001D] = (26, "You're too busy!"),
        [0x001E] = (-1, "{0} is too busy to accept gifts right now."),
        [0x0020] = (26, "You must control both objects!"),
        [0x0023] = (26, "Unable to move to object!"),
        [0x0024] = (26, "You can't jump while in the air"),
        [0x0026] = (26, "That is not a valid command."),
        [0x0028] = (26, "The item is under someone else's control!"),
        [0x0029] = (26, "You cannot pick that up!"),
        [0x002A] = (26, "You are too encumbered to carry that!"),
        [0x002B] = (0, "{0} cannot carry anymore."),
        [0x0036] = (26, "Action cancelled!"),
        [0x0037] = (26, "Unable to move to object!"),
        [0x0038] = (26, "Unable to move to object!"),
        [0x0039] = (26, "Unable to move to object!"),
        [0x003A] = (26, "You can't do that... you're dead! or You can't do that... its dead!"),
        [0x003D] = (26, "You charged too far!"),
        [0x003E] = (26, "You are too tired to do that!"),
        [0x0048] = (26, "You can't jump from this position"),
        [0x004A] = (0, "Ack! You killed yourself!"),
        [0x004D] = (26, "Invalid PK Status!"),
        [0x004E] = (7, "You fail to affect {0} because you cannot affect anyone!"),
        [0x004F] = (-1, "You fail to affect {0} because $s cannot be harmed!"),
        [0x0050] = (-1, "You fail to affect {0} because beneficial spells do not affect !"),
        [0x0051] = (-1, "You fail to affect {0} because you are not a player killer!"),
        [0x0052] = (-1, "You fail to affect {0} because  is not a player killer!"),
        [0x0053] = (-1, "You fail to affect {0} because you are not the same sort of player killer as !"),
        [0x0054] = (26, "You fail to affect {0} because you are acting across a house boundary!"),
        [0x03EE] = (-1, "The container is closed!"),
        [0x03EF] = (0, "{0} is not accepting gifts right now."),
        [0x03F1] = (26, "You failed to go to non-combat mode."),
        [0x03F7] = (26, "You are too fatigued to attack!"),
        [0x03F8] = (26, "You are out of ammunition!"),
        [0x03F9] = (26, "Your missile attack misfired!"),
        [0x03FA] = (26, "You've attempted an impossible spell path!"),
        [0x03FE] = (26, "You don't know that spell!"),
        [0x03FF] = (26, "Incorrect target type"),
        [0x0400] = (26, "You don't have all the components for this spell."),
        [0x0401] = (26, "You don't have enough Mana to cast this spell."),
        [0x0402] = (7, "Your spell fizzled."),
        [0x0403] = (26, "Your spell's target is missing!"),
        [0x0404] = (26, "Your projectile spell mislaunched!"),
        [0x0407] = (26, "Your spell cannot be cast outside"),
        [0x0408] = (-1, "Your spell cannot be cast inside"),
        [0x040A] = (26, "You are unprepared to cast a spell"),
        [0x040B] = (26, "You've already sworn your Allegiance"),
        [0x040C] = (26, "You don't have enough experience available to swear Allegiance"),
        [0x0413] = (26, "{0} is already one of your followers"),
        [0x0414] = (26, "You are not in an allegiance!"),
        [0x0416] = (26, "{0} cannot have any more Vassals"),
        [0x041D] = (26, "You must be the leader of a Fellowship"),
        [0x041E] = (26, "Your Fellowship is full"),
        [0x041F] = (26, "That Fellowship name is not permitted"),
        [0x0422] = (26, "That channel doesn't exist."),
        [0x0423] = (26, "You can't use that channel."),
        [0x0424] = (26, "You're already on that channel."),
        [0x0425] = (26, "You're not currently on that channel."),
        [0x0426] = (-1, "_ can't be dropped"),
        [0x0427] = (26, "You cannot merge different stacks!"),
        [0x0428] = (26, "You cannot merge enchanted items!"),
        [0x0429] = (26, "You must control at least one stack!"),
        [0x0432] = (26, "Your craft attempt fails."),
        [0x0433] = (26, "Your craft attempt fails."),
        [0x0434] = (26, "Given that number of items, you cannot craft anything."),
        [0x0435] = (26, "Your craft attempt fails."),
        [0x0437] = (26, "Either you or one of the items involved does not pass the requirements for this craft interaction."),
        [0x0438] = (26, "You do not have all the neccessary items."),
        [0x0439] = (26, "Not all the items are avaliable."),
        [0x043A] = (26, "You must be at rest in peace mode to do trade skills."),
        [0x043B] = (26, "You are not trained in that trade skill."),
        [0x043C] = (26, "Your hands must be free."),
        [0x043D] = (7, "You cannot link to that portal!"),
        [0x043E] = (0, "You have solved this quest too recently!"),
        [0x043F] = (0, "You have solved this quest too many times!"),
        [0x0445] = (0, "This item requires you to complete a specific quest before you can pick it up!"),
        [0x045C] = (7, "Player killers may not interact with that portal!"),
        [0x045D] = (7, "Non-player killers may not interact with that portal!"),
        [0x045E] = (26, " You do not own a house!"),
        [0x045F] = (26, " You do not own a house!"),
        [0x0466] = (7, "You must purchase Asheron's Call: Dark Majesty to interact with that portal."),
        [0x0469] = (0, "You have used all the hooks you are allowed to use for this house."),
        [0x046A] = (0, "{0} doesn't know what to do with that."),
        [0x0474] = (7, "You must complete a quest to interact with that portal."),
        [0x047F] = (26, "You must own a house to use this command."),
        [0x0480] = (26, "Your monarch does not own a mansion or a villa!"),
        [0x0481] = (26, "Your monarch does not own a mansion or a villa!"),
        [0x0482] = (26, "Your monarch has closed the mansion to the Allegiance."),
        [0x0488] = (0, "You must be above level {0} to purchase this dwelling."),
        [0x0489] = (0, "You must be at or below level {0} to purchase this dwelling."),
        [0x048A] = (-1, "You must be a monarch to purchase this dwelling."),
        [0x048B] = (0, "You must be above allegiance rank {0} to purchase this dwelling."),
        [0x048C] = (0, "You must be at or below allegiance rank {0} to purchase this dwelling."),
        [0x048E] = (26, "Your offer of Allegiance has been ignored."),
        [0x048F] = (26, "You are already involved in something!"),
        [0x0490] = (26, "You must be a monarch to use this command."),
        [0x0491] = (26, "You must specify a character to boot. (From allegiance)"),
        [0x0492] = (26, "You can't boot yourself! (From allegiance)"),
        [0x0493] = (26, "That character does not exist."),
        [0x0494] = (26, "That person is not a member of your Allegiance!"),
        [0x0495] = (26, "No patron from which to break!"),
        [0x0496] = (0, "Your Allegiance has been dissolved!"),
        [0x0497] = (0, "Your patron's Allegiance to you has been broken!"),
        [0x0498] = (26, "You have moved too far!"),
        [0x0499] = (26, "That is not a valid destination!"),
        [0x049A] = (26, "You must purchase Asheron's Call -- Dark Majesty to use this function."),
        [0x049B] = (7, "You fail to link with the lifestone!"),
        [0x049C] = (7, "You wandered too far to link with the lifestone!"),
        [0x049D] = (7, "You successfully link with the lifestone!"),
        [0x049E] = (7, "You must have linked with a lifestone in order to recall to it!"),
        [0x049F] = (7, "You fail to recall to the lifestone!"),
        [0x04A0] = (7, "You fail to link with the portal!"),
        [0x04A1] = (7, "You successfully link with the portal!"),
        [0x04A2] = (7, "You fail to recall to the portal!"),
        [0x04A3] = (7, "You must have linked with a portal in order to recall to it!"),
        [0x04A4] = (7, "You fail to summon the portal!"),
        [0x04A5] = (7, "You must have linked with a portal in order to summon it!"),
        [0x04A6] = (7, "You fail to teleport!"),
        [0x04A7] = (7, "You have been teleported too recently!"),
        [0x04A8] = (7, "You must be an Advocate to interact with that portal."),
        [0x04AA] = (7, "Players may not interact with that portal."),
        [0x04AB] = (7, "You are not powerful enough to interact with that portal!"),
        [0x04AC] = (7, "You are too powerful to interact with that portal!"),
        [0x04AD] = (7, "You cannot recall to that portal!"),
        [0x04AE] = (7, "You cannot summon that portal!"),
        [0x04AF] = (26, "The lock is already unlocked."),
        [0x04B0] = (26, "You can't lock or unlock that!"),
        [0x04B1] = (26, "You can't lock or unlock what is open!"),
        [0x04B2] = (0, "The key doesn't fit this lock."),
        [0x04B3] = (26, "The lock has been used too recently."),
        [0x04B4] = (26, "You aren't trained in lockpicking!"),
        [0x04B5] = (26, "You must specify a character to query."),
        [0x04B6] = (26, "Please use the allegiance panel to view your own information."),
        [0x04B7] = (26, "You have used that command too recently."),
        [0x04BD] = (0, "You do not own that salvage tool!"),
        [0x04BE] = (0, "You do not own that item!"),
        [0x04BF] = (26, "The {0} was not suitable for salvaging."),
        [0x04C0] = (26, "The {0} contains the wrong material."),
        [0x04C1] = (0, "The material cannot be created."),
        [0x04C2] = (0, "The list of items you are attempting to salvage is invalid."),
        [0x04C3] = (0, "You cannot salvage items that you are trading!"),
        [0x04C4] = (7, "You must be a guest in this house to interact with that portal."),
        [0x04C5] = (26, "Your Allegiance Rank is too low to use that item's magic."),
        [0x04C6] = (26, "You must be {0} to use that item's magic."),
        [0x04C7] = (26, "Your Arcane Lore skill is too low to use that item's magic."),
        [0x04C8] = (26, "That item doesn't have enough Mana."),
        [0x04C9] = (26, "Your {0} is too low to use that item's magic."),
        [0x04CA] = (26, "Only {0} may use that item's magic."),
        [0x04CB] = (26, "You must have {0} specialized to use that item's magic."),
        [0x04CC] = (7, "You have been involved in a player killer battle too recently to do that!"),
        [0x04CE] = (0, "{0} is too busy to accept gifts right now."),
        [0x04CF] = (0, "{0} cannot accept stacked objects. Try giving one at a time."),
        [0x04D0] = (0, "You have failed to alter your skill."),
        [0x04D1] = (0, "Your {0} skill must be trained, not untrained or specialized, in order to be altered in this way!"),
        [0x04D2] = (0, "You do not have enough skill credits to specialize your {0} skill."),
        [0x04D3] = (0, "You have too many available experience points to be able to absorb the experience points from your {0} skill. Please spend some of your experience points and try again."),
        [0x04D4] = (0, "Your {0} skill is already untrained!"),
        [0x04D5] = (0, "You are currently wielding items which require a certain level of . {0} Your  skill cannot be lowered while you are wielding these items.  Please remove these items and try again."),
        [0x04D6] = (0, "You have succeeded in specializing your {0} skill!"),
        [0x04D7] = (0, "You have succeeded in lowering your {0} skill from specialized to trained!"),
        [0x04D8] = (0, "You have succeeded in untraining your {0} skill!"),
        [0x04D9] = (0, "Although you cannot untrain your {0} skill, you have succeeded in recovering all the experience you had invested in it."),
        [0x04DA] = (0, "You have too many credits invested in specialized skills already! Before you can specialize your {0} skill, you will need to unspecialize some other skill."),
        [0x04DC] = (-1, "Appears to be sent to a recruiter 30 seconds after the initial recruit message when the recruitee doesn't respond."),
        [0x04DD] = (0, "You have failed to alter your attributes."),
        [0x04DE] = (0, "Sends {0} to the chat window. Unknown usage."),
        [0x04DF] = (0, "Sends {0} to the chat window. Unknown usage."),
        [0x04E0] = (0, "You are currently wielding items which require a certain level of skill. Your attributes cannot be transferred while you are wielding these items. Please remove these items and try again."),
        [0x04E1] = (0, "You have succeeded in transferring your attributes!"),
        [0x04E2] = (0, "This hook is a duplicated housing object. You may not add items to a duplicated housing object. Please empty the hook and allow it to reset."),
        [0x04E3] = (0, "That item is of the wrong type to be placed on this hook."),
        [0x04E4] = (0, "This chest is a duplicated housing object. You may not add items to a duplicated housing object. Please empty everything -- including backpacks -- out of the chest and allow the chest to reset."),
        [0x04E5] = (0, "This hook was a duplicated housing object. Since it is now empty, it will be deleted momentarily. Once it is gone, it is safe to use the other, non-duplicated hook that is here."),
        [0x04E6] = (0, "This chest was a duplicated housing object. Since it is now empty, it will be deleted momentarily. Once it is gone, it is safe to use the other, non-duplicated chest that is here."),
        [0x04E7] = (0, "You cannot swear allegiance to anyone because you own a monarch-only house. Please abandon your house and try again."),
        [0x04E8] = (-1, "The {0} cannot be used while on a hook and only the owner may open the hook."),
        [0x04E9] = (0, "The {0} cannot be used while on a hook, use the '@house hooks on' command to make the hook openable."),
        [0x04EA] = (0, "The {0} can only be used while on a hook."),
        [0x04EB] = (26, "You can't do that while in the air!"),
        [0x04EC] = (0, "You cannot modify your player killer status while you are recovering from a PK death."),
        [0x04ED] = (0, "Advocates may not change their player killer status!"),
        [0x04EE] = (0, "Your level is too low to change your player killer status with this object."),
        [0x04EF] = (0, "Your level is too high to change your player killer status with this object."),
        [0x04F0] = (0, "You feel a harsh dissonance, and you sense that an act of killing you have committed recently is interfering with the conversion."),
        [0x04F1] = (0, "Bael'Zharon's power flows through you again. You are once more a player killer."),
        [0x04F2] = (0, "Bael'Zharon has granted you respite after your moment of weakness. You are temporarily no longer a player killer."),
        [0x04F3] = (7, "Lite Player Killers may not interact with that portal!"),
        [0x04F4] = (-1, "{0} fails to affect you because $s cannot affect anyone!"),
        [0x04F5] = (7, "{0} fails to affect you because you cannot be harmed!"),
        [0x04F6] = (-1, " fails to affect you because {0} is not a player killer!"),
        [0x04F7] = (7, "{0} fails to affect you because you are not a player killer!"),
        [0x04F8] = (-1, "{0} fails to affect you because you are not the same sort of player killer as !"),
        [0x04F9] = (7, "{0} fails to affect you across a house boundary!"),
        [0x04FA] = (-1, "{0} is an invalid target."),
        [0x04FB] = (-1, "You are an invalid target for the spell of {0}."),
        [0x04FC] = (26, "You aren't trained in healing!"),
        [0x04FD] = (26, "You don't own that healing kit!"),
        [0x04FE] = (26, "You can't heal that!"),
        [0x04FF] = (26, "{0} is already at full health!"),
        [0x0500] = (26, "You aren't ready to heal!"),
        [0x0501] = (26, "You can only use Healing Kits on player characters."),
        [0x0502] = (7, "The Lifestone's magic protects you from the attack!"),
        [0x0503] = (7, "The portal's residual energy protects you from the attack!"),
        [0x0504] = (0, "You are enveloped in a feeling of warmth as you are brought back into the protection of the Light. You are once again a Non-Player Killer."),
        [0x0505] = (26, "You're too close to your sanctuary!"),
        [0x0506] = (26, "You can't do that -- you're trading!"),
        [0x0507] = (0, "Only Non-Player Killers may enter PK Lite. Please see @help pklite for more details about this command."),
        [0x0508] = (0, "A cold wind touches your heart. You are now a Player Killer Lite."),
        [0x0509] = (7, "{0} has no appropriate targets equipped for this spell."),
        [0x050A] = (-1, "You have no appropriate targets equipped for {0}'s spell."),
        [0x050B] = (0, "{0} is now an open fellowship; anyone may recruit new members."),
        [0x050C] = (0, "{0} is now a closed fellowship."),
        [0x050D] = (0, "{0} is now the leader of this fellowship."),
        [0x050E] = (0, "You have passed leadership of the fellowship to {0}"),
        [0x050F] = (26, "You do not belong to a Fellowship."),
        [0x0510] = (0, "You may not hook any more {0} on your house.  You already have the maximum number of  hooked or you are not permitted to hook any on your type of house."),
        [0x0512] = (0, "You are now using the maximum number of hooks.  You cannot use another hook until you take an item off one of your hooks."),
        [0x0513] = (0, "You are no longer using the maximum number of hooks.  You may again add items to your hooks."),
        [0x0514] = (0, "You now have the maximum number of {0} hooked.  You cannot hook any additional  until you remove one or more from your house."),
        [0x0515] = (0, "You no longer have the maximum number of {0} hooked.  You may hook additional ."),
        [0x0516] = (0, "You are not permitted to use that hook."),
        [0x0517] = (0, "{0} is not close enough to your level."),
        [0x0518] = (0, "This fellowship is locked; {0} cannot be recruited into the fellowship."),
        [0x0519] = (0, "The fellowship is locked, you were not added to the fellowship."),
        [0x051A] = (26, "Only the original owner may use that item's magic."),
        [0x051B] = (0, "You have entered the {0} channel."),
        [0x051C] = (0, "You have left the {0} channel."),
        [0x051D] = (-1, "Turbine Chat is enabled."),
        [0x051E] = (0, "{0} will not receive your message, please use urgent assistance to speak with an in-game representative"),
        [0x051F] = (26, "Message Blocked: {0}"),
        [0x0520] = (0, "You cannot add anymore people to the list of players that you can hear."),
        [0x0521] = (0, "{0} has been added to the list of people you can hear."),
        [0x0522] = (0, "{0} has been removed from the list of people you can hear."),
        [0x0523] = (0, "You are now deaf to player's screams."),
        [0x0524] = (0, "You can hear all players once again."),
        [0x0525] = (0, "You fail to remove {0} from your loud list."),
        [0x0526] = (26, "You chicken out."),
        [0x0527] = (26, "You cannot posssibly succeed."),
        [0x0528] = (0, "The fellowship is locked; you cannot open locked fellowships."),
        [0x0529] = (26, "Trade Complete!"),
        [0x052A] = (26, "That is not a salvaging tool."),
        [0x052B] = (26, "That person is not available now."),
        [0x052C] = (0, "You are now snooping on {0}."),
        [0x052D] = (0, "You are no longer snooping on {0}."),
        [0x052E] = (0, "You fail to snoop on {0}."),
        [0x052F] = (0, "{0} attempted to snoop on you."),
        [0x0530] = (0, "{0} is already being snooped on, only one person may snoop on another at a time."),
        [0x0531] = (0, "{0} is in limbo and cannot receive your message."),
        [0x0532] = (0, "You must wait 30 days after purchasing a house before you may purchase another with any character on the same account. This applies to all housing except apartments."),
        [0x0533] = (0, "You have been booted from your allegiance chat room. Use \"@allegiance chat on\" to rejoin. ({0})."),
        [0x0534] = (0, "{0} has been booted from the allegiance chat room."),
        [0x0535] = (0, "You do not have the authority within your allegiance to do that."),
        [0x0536] = (0, "The account of {0} is already banned from the allegiance."),
        [0x0537] = (0, "The account of {0} is not banned from the allegiance."),
        [0x0538] = (0, "The account of {0} was not unbanned from the allegiance."),
        [0x0539] = (0, "The account of {0} has been banned from the allegiance."),
        [0x053A] = (0, "The account of {0} is no longer banned from the allegiance."),
        [0x053B] = (0, "Banned Characters: {0}"),
        [0x053E] = (0, "{0} is banned from the allegiance!"),
        [0x053F] = (0, "You are banned from {0}'s allegiance!"),
        [0x0540] = (0, "You have the maximum number of accounts banned.!"),
        [0x0541] = (0, "{0} is now an allegiance officer."),
        [0x0542] = (0, "An unspecified error occurred while attempting to set {0} as an allegiance officer."),
        [0x0543] = (0, "{0} is no longer an allegiance officer."),
        [0x0544] = (0, "An unspecified error occurred while attempting to remove {0} as an allegiance officer."),
        [0x0545] = (0, "You already have the maximum number of allegiance officers. You must remove some before you add any more."),
        [0x0546] = (0, "Your allegiance officers have been cleared."),
        [0x0547] = (0, "You must wait {0} before communicating again!"),
        [0x0548] = (0, "You cannot join any chat channels while gagged."),
        [0x0549] = (0, "Your allegiance officer status has been modified. You now hold the position of: {0}."),
        [0x054A] = (0, "You are no longer an allegiance officer."),
        [0x054B] = (0, "{0} is already an allegiance officer of that level."),
        [0x054C] = (0, "Your allegiance does not have a hometown."),
        [0x054D] = (26, "The {0} is currently in use."),
        [0x054E] = (0, "The hook does not contain a usable item. You cannot open the hook because you do not own the house to which it belongs."),
        [0x054F] = (0, "The hook does not contain a usable item. Use the '@house hooks on'command to make the hook openable."),
        [0x0550] = (26, "Out of Range!"),
        [0x0551] = (0, "You are not listening to the {0} channel!"),
        [0x0552] = (26, "You must purchase Asheron's Call -- Throne of Destiny to use this function."),
        [0x0553] = (26, "You must purchase Asheron's Call -- Throne of Destiny to use this item."),
        [0x0554] = (26, "You must purchase Asheron's Call -- Throne of Destiny to use this portal."),
        [0x0555] = (26, "You must purchase Asheron's Call -- Throne of Destiny to access this quest."),
        [0x0556] = (0, "You have failed to complete the augmentation."),
        [0x0557] = (0, "You have used this augmentation too many times already."),
        [0x0558] = (0, "You have used augmentations of this type too many times already."),
        [0x0559] = (0, "You do not have enough unspent experience available to purchase this augmentation."),
        [0x055A] = (0, "Sends \"{0}\" to the chat window. Unknown usage"),
        [0x055B] = (0, "Congratulations! You have succeeded in acquiring the {0} augmentation."),
        [0x055C] = (0, "Although your augmentation will not allow you to untrain your {0} skill, you have succeeded in recovering all the experience you had invested in it."),
        [0x055D] = (0, "You must exit the Training Academy before that command will be available to you."),
        [0x055E] = (0, "Sends \"{0}\" to the chat window. Unknown usage"),
        [0x055F] = (0, "Only Player Killer characters may use this command!"),
        [0x0560] = (0, "Only Player Killer Lite characters may use this command!"),
        [0x0561] = (26, "You may only have a maximum of 50 friends at once. If you wish to add more friends, you must first remove some."),
        [0x0562] = (0, "{0} is already on your friends list!"),
        [0x0563] = (0, "That character is not on your friends list!"),
        [0x0564] = (0, "Only the character who owns the house may use this command."),
        [0x0565] = (0, "That allegiance name is invalid because it is empty. Please use the @allegiance name clear command to clear your allegiance name."),
        [0x0566] = (0, "That allegiance name is too long. Please choose another name."),
        [0x0567] = (0, "That allegiance name contains illegal characters. Please choose another name using only letters, spaces, - and '."),
        [0x0568] = (0, "That allegiance name is not appropriate. Please choose another name."),
        [0x0569] = (0, "That allegiance name is already in use. Please choose another name."),
        [0x056A] = (0, "You may only change your allegiance name once every 24 hours. You may change your allegiance name again in {0}."),
        [0x056B] = (0, "Your allegiance name has been cleared."),
        [0x056C] = (0, "That is already the name of your allegiance!"),
        [0x056D] = (0, "{0} is the monarch and cannot be promoted or demoted."),
        [0x056E] = (0, "That level of allegiance officer is now known as: {0}."),
        [0x056F] = (0, "That is an invalid officer level."),
        [0x0570] = (0, "That allegiance officer title is not appropriate."),
        [0x0571] = (0, "That allegiance name is too long. Please choose another name."),
        [0x0572] = (0, "All of your allegiance officer titles have been cleared."),
        [0x0573] = (0, "That allegiance title contains illegal characters. Please choose another name using only letters, spaces, - and '."),
        [0x0574] = (0, "Your allegiance is currently: {0}."),
        [0x0575] = (0, "Your allegiance is now: {0}."),
        [0x0576] = (0, "You may not accept the offer of allegiance from {0} because your allegiance is locked."),
        [0x0577] = (0, "You may not swear allegiance at this time because the allegiance of {0} is locked."),
        [0x0578] = (0, "You have pre-approved {0} to join your allegiance."),
        [0x0579] = (0, "You have not pre-approved any vassals to join your allegiance."),
        [0x057A] = (0, "{0} is already a member of your allegiance!"),
        [0x057B] = (0, "{0} has been pre-approved to join your allegiance."),
        [0x057C] = (0, "You have cleared the pre-approved vassal for your allegiance."),
        [0x057D] = (0, "That character is already gagged!"),
        [0x057E] = (0, "That character is not currently gagged!"),
        [0x057F] = (0, "Your allegiance chat privileges have been temporarily removed by {0}. Until they are restored, you may not view or speak in the allegiance chat channel."),
        [0x0580] = (0, " is now temporarily unable to view or speak in allegiance chat. The gag will run out in 5 minutes, or {0} may be explicitly ungagged before then."),
        [0x0581] = (0, "Your allegiance chat privileges have been restored."),
        [0x0582] = (0, "Your allegiance chat privileges have been restored by {0}."),
        [0x0583] = (0, "You have restored allegiance chat privileges to {0}."),
        [0x0584] = (26, "You cannot pick up more of that item!"),
        [0x0585] = (26, "You are restricted to clothes and armor created for your race."),
        [0x0586] = (26, "That item was specifically created for another race."),
        [0x0587] = (7, "Olthoi cannot interact with that!"),
        [0x0588] = (7, "Olthoi cannot use regular lifestones! Asheron would not allow it!"),
        [0x0589] = (7, "The vendor looks at you in horror!"),
        [0x058A] = (0, "{0} cowers from you!"),
        [0x058B] = (7, "As a mindless engine of destruction an Olthoi cannot join a fellowship!"),
        [0x058C] = (7, "The Olthoi only have an allegiance to the Olthoi Queen!"),
        [0x058D] = (7, "You cannot use that item!"),
        [0x058E] = (7, "This person will not interact with you!"),
        [0x058F] = (7, "Only Olthoi may pass through this portal!"),
        [0x0590] = (7, "Olthoi may not pass through this portal!"),
        [0x0591] = (7, "You may not pass through this portal while Vitae weakens you!"),
        [0x0592] = (7, "This character must be two weeks old or have been created on an account at least two weeks old to use this portal!"),
        [0x0593] = (7, "Olthoi characters can only use Lifestone and PK Arena recalls!"),
    };
}
