using DatReaderWriter.Enums;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session.Quartermaster;

/// <summary>
/// M6 16 E5d: the floor-only lines over the real SpellTable. Before the slice four sites stated them, each from its own reading of the foci she holds: the outer loop (the climber: War and Life held, the others not; any
/// other character: all four), the fight (the kit's foci, War and Life whatever the kit), the logistics station and the buy-trip (the pack as it stands; with no account name, the foci formula for every school). A school
/// whose focus is not held is read on the account's full formula. What this proves: on every ground of the catalog the set is the same whichever of those readings is taken, for two made-up account names, so the session's
/// one statement (the job's, <see cref="Lifestone.Client.Session.Quartermaster.TripDesk"/>) changes no site's answer. The two formulas share their scarabs; they differ in the tapers, the herb, the powder, the potion and
/// the talisman, and no ground carries one of those as a line no vendor sells.
/// </summary>
public sealed class TripDeskFloorDatTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [RequiresDatsFact]
    public void On_every_ground_of_the_catalog_the_floor_only_lines_are_the_same_whichever_foci_she_is_read_to_hold()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var readings = new (string Name, Func<MagicSchool, bool> Holds)[]
        {
            ("no focus held", _ => false),
            ("War and Life held (the outer loop's reading of the climber)", s => s is MagicSchool.WarMagic or MagicSchool.LifeMagic),
            ("every focus held (the outer loop's reading of any other character)", _ => true),
            ("War, Life and Creature held", s => s is MagicSchool.WarMagic or MagicSchool.LifeMagic or MagicSchool.CreatureEnchantment),
            ("War, Life and Item held", s => s is MagicSchool.WarMagic or MagicSchool.LifeMagic or MagicSchool.ItemEnchantment),
        };
        var withFloorOnly = new List<string>();
        foreach (var ground in GroundCatalog.All)
        {
            var foci = Sorted(ComponentFloor.FloorOnly(ground, spells)); // no account name: the foci formula for every school
            if (foci.Count > 0)
            {
                withFloorOnly.Add($"{ground.Key}: {string.Join(", ", foci)}");
            }

            foreach (var account in new[] { "a-made-up-account", "another-made-up-name" })
            {
                foreach (var (name, holds) in readings)
                {
                    var set = Sorted(ComponentFloor.FloorOnly(ground, spells, account, holds));
                    Assert.True(foci.SequenceEqual(set), $"{ground.Key}: the floor-only lines read with {name} are {string.Join(", ", set)}, with the foci formula {string.Join(", ", foci)}");
                }
            }
        }

        Assert.Contains(withFloorOnly, g => g.StartsWith("black-spawn-den-aluvian: ", StringComparison.Ordinal) && g.Contains("7299", StringComparison.Ordinal)); // the den's Diamond Scarab (D148): the test reads real floor-only lines
    }

    private static List<uint> Sorted(IReadOnlyDictionary<uint, IReadOnlyList<string>> floorOnly) => [.. floorOnly.Keys.Order()];
}
