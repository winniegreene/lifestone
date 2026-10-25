namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>
/// How near a creature notices the player (nav-creature-avoid, docs/wayfinding.md "The creature layer"): ACE's <c>VisualAwarenessRange</c>
/// (PropertyFloat 31) by weenie class id. The client is not sent the property, so the values of the hunt grounds' creatures (the Low B belt's and
/// the low north forest's tables, <c>HuntGround.LowBTable</c> and <c>HuntGround.LowNorthForestTable</c>: 74 classes, 8 to 35 m) are pinned here,
/// read by a read-only SELECT of the bundled world database (2026-10-24; every one of them sets the property); any other creature takes ACE's
/// default, 18 m (<c>VisualAwarenessRange_Default</c>, ACE @95ef426d Source/ACE.Server/WorldObjects/Monster_Awareness.cs:377, "the most common
/// value from retail"). ACE scales both by the server property <c>mob_awareness_range</c>, 1 on the bundled shard (<see cref="ServerScale"/>).
/// </summary>
public static class CreatureAwareness
{
    /// <summary>ACE's awareness for a creature without the property, in metres (<c>VisualAwarenessRange_Default</c>, Monster_Awareness.cs:377).</summary>
    public const float DefaultMetres = 18f;

    /// <summary>The server's <c>mob_awareness_range</c> (PropertyManager.cs:658, default 1.0; the bundled shard's config_properties_double row is 1).</summary>
    public const float ServerScale = 1f;

    private static readonly Dictionary<uint, float> ByWcid = new()
    {
        [3] = 20f, // Olthoi Worker
        [6] = 22f, // Banderling Scout
        [7] = 10f, // Drudge Skulker
        [12] = 10f, // Red Phyntos Wasp
        [18] = 24f, // Reedshark Elder
        [20] = 22f, // Auroch Bull
        [181] = 22f, // Auroch Cow
        [182] = 22f, // Auroch Yearling
        [192] = 12f, // Drudge Prowler
        [193] = 10f, // Drudge Slinker
        [194] = 25f, // Copper Golem
        [211] = 24f, // Mudlurk Mosswart
        [213] = 18f, // Olthoi Nymph
        [215] = 10f, // Blue Phyntos Wasp
        [217] = 30f, // Gold Phyntos Wasp
        [221] = 22f, // Adult Reedshark
        [222] = 24f, // Veteran Reedshark
        [223] = 22f, // Reedshark Pup
        [233] = 15f, // Tumerok Warrior
        [235] = 25f, // Goldenback Tusker
        [937] = 22f, // Banderling Guard
        [940] = 10f, // Drudge Sneaker
        [946] = 18f, // Mite Squire
        [947] = 18f, // Barker Mosswart
        [948] = 25f, // Mosswart Feeder
        [1464] = 20f, // Drudge Robber
        [1608] = 24f, // Drudge Lurker
        [1609] = 24f, // Drudge Stalker
        [1610] = 24f, // Drudge Ravener
        [1622] = 30f, // Mire Phyntos Wasp
        [1627] = 24f, // Tusker Crimsonback
        [1630] = 18f, // Lich Lord
        [1631] = 17f, // Drudge Servant
        [1632] = 18f, // Drudge Slave
        [1756] = 17f, // Shadow Child
        [1757] = 25f, // Shadow Lieutenant
        [1758] = 30f, // Shadow
        [1760] = 24f, // Skeleton Warrior
        [1761] = 24f, // Skeleton Captain
        [1762] = 16f, // Skeleton Lord
        [1989] = 35f, // Shadow Wisp
        [2574] = 21f, // Rough Monouga
        [2575] = 23f, // Crude Monouga
        [2576] = 24f, // Wild Monouga
        [2612] = 17f, // Outcast Monouga
        [4110] = 8f, // Blood Shreth
        [4111] = 10f, // Hunter Shreth
        [4253] = 28f, // Panumbris Shadow
        [6535] = 17f, // Small Shadow Child
        [7128] = 25f, // Sufut Zefir
        [7978] = 10f, // Scrawed Grievver
        [7979] = 10f, // Virulent Grievver
        [7990] = 24f, // Field Ursuin
        [8010] = 12f, // Broken Fragment
        [9251] = 21f, // Brutish Monouga
        [9253] = 21f, // Wily Monouga
        [11528] = 13f, // Elaniwood Golem
        [11537] = 16f, // Virtuous Doll
        [11989] = 12f, // Drudge Vagabond
        [12004] = 17f, // Outcast Lord
        [12017] = 10f, // Red Phyntos Drone
        [22009] = 24f, // Olthoi Drone
        [22010] = 24f, // Olthoi Servant
        [24940] = 23f, // Gotrok Gigas
        [24942] = 22f, // Gotrok Lithos
        [24959] = 28f, // Olthoi Needler
        [26684] = 8f, // Blood Shrethlet
        [26687] = 10f, // Hunter Shrethlet
        [26689] = 14f, // Field Ursuin Cub
        [27254] = 13f, // Copper Golem Kingpin
        [28877] = 18f, // Nasty Scarecrow
        [28878] = 18f, // Naughty Scarecrow
        [28879] = 18f, // Nefarious Scarecrow
        [34102] = 18f, // Old Zombie
    };

    /// <summary>The number of weenie classes pinned.</summary>
    public static int Count => ByWcid.Count;

    /// <summary>The awareness of weenie class <paramref name="wcid"/> in metres: its pinned value, else <see cref="DefaultMetres"/>, times <see cref="ServerScale"/>.</summary>
    /// <param name="wcid">The creature's weenie class id (<c>CreateObject</c>'s <c>WeenieClassId</c>).</param>
    public static float MetresOf(uint wcid) => (ByWcid.TryGetValue(wcid, out var metres) ? metres : DefaultMetres) * ServerScale;

    /// <summary>True when <paramref name="wcid"/>'s awareness is pinned (not the default).</summary>
    /// <param name="wcid">The weenie class id.</param>
    public static bool IsPinned(uint wcid) => ByWcid.ContainsKey(wcid);
}
