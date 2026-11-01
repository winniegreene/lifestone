using System.Collections.Immutable;

namespace Lifestone.Net.Messages;

/// <summary>
/// GameEvent 0x0295 SetTurbineChatChannels (M5 6 w1): the ten channel ids ACE sends after login and again with each channel notice
/// (about six times a recording), in ACE's writer order (<c>GameEventSetTurbineChatChannels.cs:10-19</c>, @95ef426d; the values come
/// from <c>TurbineChatChannel.cs</c>). The recordings hold one payload only: 0, 2, 3, 4, 5, 10, 0, 7, 8, 9. The client does not use them
/// in M5 (the reducer logs and ignores the event); retail's handler address is unknown (not in the 11.6096 symbol table).
/// </summary>
/// <param name="Allegiance">The player's allegiance chat channel, 0 without one (ACE's <c>allegiance</c> argument).</param>
/// <param name="General">General, 2.</param>
/// <param name="Trade">Trade, 3.</param>
/// <param name="Lfg">LFG, 4.</param>
/// <param name="Roleplay">Roleplay, 5.</param>
/// <param name="Olthoi">Olthoi, 10.</param>
/// <param name="Society">The player's society chat channel, 0 without one (ACE's <c>society</c> argument).</param>
/// <param name="SocietyCelestialHand">Celestial Hand, 7.</param>
/// <param name="SocietyEldrytchWeb">Eldrytch Web, 8.</param>
/// <param name="SocietyRadiantBlood">Radiant Blood, 9.</param>
public readonly record struct TurbineChatChannelsEvent(
    uint Allegiance,
    uint General,
    uint Trade,
    uint Lfg,
    uint Roleplay,
    uint Olthoi,
    uint Society,
    uint SocietyCelestialHand,
    uint SocietyEldrytchWeb,
    uint SocietyRadiantBlood);

/// <summary>
/// GameEvent 0x0029 CharacterTitle (M5 6 w1): ACE writes the literal <c>1u</c>, <c>CharacterTitleId ?? 0</c>, the title count, then that
/// many title ids (<c>GameEventCharacterTitle.cs:10-18</c>, @95ef426d). The recordings hold one payload only: 1, 1, 1, 1 (title 1 is the
/// current title and the one title the character has). The reducer logs and ignores the event; retail's handler address is unknown.
/// </summary>
/// <param name="Marker">The leading word, always the literal 1 in ACE (kept raw; retail's meaning is not documented).</param>
/// <param name="CharacterTitleId">The current title, 0 for none.</param>
/// <param name="Titles">The title ids the character has (ACE's <c>NumCharacterTitles</c> of them).</param>
public readonly record struct CharacterTitleEvent(uint Marker, uint CharacterTitleId, ImmutableArray<uint> Titles);

/// <summary>ACE's <c>GameEventFriendsListUpdate.FriendsUpdateTypeFlag</c> (<c>GameEventFriendsListUpdate.cs:12-19</c>, @95ef426d).</summary>
[Flags]
public enum FriendsUpdateType : uint
{
    /// <summary>0x0 FullList: the whole list (the login event).</summary>
    FullList = 0x0000,

    /// <summary>0x1 FriendAdded.</summary>
    FriendAdded = 0x0001,

    /// <summary>0x2 FriendRemoved.</summary>
    FriendRemoved = 0x0002,

    /// <summary>0x4 FriendStatusChanged.</summary>
    FriendStatusChanged = 0x0004,
}

/// <summary>One friend of a FriendsListUpdate (<c>GameEventFriendsListUpdate.cs:67-96</c>, @95ef426d).</summary>
/// <param name="FriendId">The friend's character GUID.</param>
/// <param name="IsOnline">ACE writes 1 when online, 0 when not (kept raw).</param>
/// <param name="AppearOffline">ACE writes the literal 0 ("whether the friend should appear to be offline"; kept raw).</param>
/// <param name="Name">The friend's name ("" when ACE cannot find the character).</param>
/// <param name="FriendsOfFriend">The friend's own friends (ACE writes an empty list, a TODO in its source).</param>
/// <param name="InverseFriends">The inverse list (ACE writes an empty list, a TODO in its source).</param>
public readonly record struct FriendEntry(
    uint FriendId,
    uint IsOnline,
    uint AppearOffline,
    string Name,
    ImmutableArray<uint> FriendsOfFriend,
    ImmutableArray<uint> InverseFriends);

/// <summary>
/// GameEvent 0x0021 FriendsListUpdate (M5 6 w1): ACE writes the friend count, each friend, then the update type
/// (<c>GameEventFriendsListUpdate.cs:65-98</c>, @95ef426d). The recordings hold one payload only: an empty full list (0, 0). The reducer
/// logs and ignores the event; retail's handler address is unknown.
/// </summary>
/// <param name="Friends">The friends this update names: the whole list for <see cref="FriendsUpdateType.FullList"/>, else the one friend.</param>
/// <param name="UpdateType">The raw u32 of <see cref="FriendsUpdateType"/>.</param>
public readonly record struct FriendsListUpdateEvent(ImmutableArray<FriendEntry> Friends, uint UpdateType);
