using Lifestone.Client.Session.Fighter;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>The names of the fighters' gear weapons (M4 P5a C6), for the swap and buff tests that follow the kit's weapon, not a literal.</summary>
internal static class C6Names
{
    /// <summary>The Brawler's wielded weapon (the magic gear's, not the plain Mace).</summary>
    public static readonly string BrawlerWeapon = FighterKit.Melee.ItemOf(FighterKit.Melee.WeaponWcid)!.Item.Name;

    /// <summary>The Archer's bow (the magic gear's, not the plain Longbow).</summary>
    public static readonly string ArcherBow = FighterKit.Archer.ItemOf(FighterKit.Archer.LauncherWcid)!.Item.Name;
}
