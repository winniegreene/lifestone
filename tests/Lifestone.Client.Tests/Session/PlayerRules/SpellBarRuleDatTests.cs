using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.PlayerRules;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.Session.PlayerRules;

/// <summary>
/// The player's rule for spells against the retail SpellTable (0x0E00000E; M6 item 15): the spells of the mage's profile go on the same tab whether the table or the profile describes them (a host without dats lays the bars out
/// as a host with them), the levels of one profile line are one line of the table and no two profile lines share one (a higher level replaces its own line's lower level, never another line's spell), and the power rises with the level along a buff line.
/// </summary>
public sealed class SpellBarRuleDatTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static IEnumerable<ProfileLine> ProfileLines => ArenaProfile.Lines.Concat(ArenaProfile.Extras);

    [RequiresDatsFact]
    public void The_profiles_spells_go_on_the_same_tab_by_the_SpellTable_as_by_the_profile()
    {
        var table = SpellTableData.Load(fixture.Dats).Spells;
        foreach (var line in ProfileLines)
        {
            foreach (var spell in line.Spells)
            {
                var byProfile = SpellBarRule.Describe(spell.Id, null);
                var byTable = SpellBarRule.Describe(spell.Id, table[spell.Id]);
                Assert.True(byProfile.Tab == byTable.Tab, $"{line.Name}: {table[spell.Id].Name} ({spell.Id}) is on {SpellBarRule.TabLabel(byProfile.Tab)} by the profile and {SpellBarRule.TabLabel(byTable.Tab)} by the SpellTable");
            }
        }

        foreach (var id in VitalsMeans.HealSelf.Concat(VitalsMeans.RevitalizeSelf).Concat(VitalsMeans.StaminaToManaSelf).Concat(VitalsMeans.HarmSelf))
        {
            Assert.Equal(SpellBarRule.VitalsTab, SpellBarRule.Describe(id, table[id]).Tab);
            Assert.Equal(SpellBarRule.VitalsTab, SpellBarRule.Describe(id, null).Tab);
        }
    }

    [RequiresDatsFact]
    public void The_levels_of_a_profile_line_are_one_line_of_the_SpellTable_in_the_order_of_its_power_and_no_two_profile_lines_share_one()
    {
        var table = SpellTableData.Load(fixture.Dats).Spells;
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in ProfileLines)
        {
            var keys = line.Spells.Select(s => SpellBarRule.Describe(s.Id, table[s.Id]).Line).Distinct().ToList();
            if (line.Role == ProfileRole.Travel)
            {
                Assert.Null(Assert.Single(keys)); // a recall or a tie has no line: it never takes another's place (Lifestone Recall and Primary Portal Recall share the table's category)
                continue;
            }

            var key = Assert.Single(keys);
            Assert.NotNull(key);
            Assert.True(seen.TryAdd(key!, line.Name), $"{line.Name} and {seen.GetValueOrDefault(key!)} are one line by the SpellTable ({key})");
            // The level is the power: it rises along a buff line and a mana line (the lines whose higher level replaces the lower one as she grows). The war lines' order is the next test's (the profile lists a volley's
            // first two levels twice, under two ids each, so its list is not in the order of the power).
            if (line.IsBuff || line.IsManaLine)
            {
                var powers = line.Spells.Select(s => SpellBarRule.Describe(s.Id, table[s.Id]).Power).ToList();
                Assert.True(powers.SequenceEqual(powers.Order()) && powers.Count == powers.Distinct().Count(), $"{line.Name}: the power does not rise with the level: {string.Join(", ", line.Spells.Select(s => $"{table[s.Id].Name} {table[s.Id].Power}"))}");
            }
        }
    }

    [RequiresDatsFact]
    public void The_war_shapes_are_lines_of_their_own_a_bolt_is_never_a_streaks_or_an_arcs_level()
    {
        // Every war spell the picker knows (WarSpellData: the bolts, streaks, arcs, volleys, blasts and the ring, of every element): by the SpellTable's line a line holds one shape only, and one spell a level.
        var table = SpellTableData.Load(fixture.Dats).Spells;
        var lines = WarSpellData.Rows.GroupBy(r => SpellBarRule.Describe(r.Id, table[r.Id]).Line ?? "none:" + r.Id);
        foreach (var line in lines)
        {
            Assert.True(line.Select(r => r.Kind).Distinct().Count() == 1, $"line {line.Key} holds {string.Join(", ", line.Select(r => $"{table[r.Id].Name} ({r.Kind})"))}");
            Assert.True(line.Select(r => r.Level).Distinct().Count() == line.Count(), $"line {line.Key} holds a level twice: {string.Join(", ", line.Select(r => $"{table[r.Id].Name} (level {r.Level})"))}");
            var byLevel = line.OrderBy(r => r.Level).Select(r => table[r.Id].Power).ToList();
            Assert.True(byLevel.SequenceEqual(byLevel.Order()), $"line {line.Key}: the power does not rise with the level: {string.Join(", ", line.OrderBy(r => r.Level).Select(r => $"{table[r.Id].Name} {table[r.Id].Power}"))}");
        }

        Assert.All(WarSpellData.Rows, r => Assert.Equal(SpellBarRule.FightTab, SpellBarRule.TabOf(table[r.Id])));
    }

    [RequiresDatsFact]
    public void The_travel_spells_are_on_the_utility_tab_and_the_opener_and_the_drains_where_a_player_keeps_them()
    {
        var table = SpellTableData.Load(fixture.Dats).Spells;
        Assert.All(new uint[] { 1635, 47, 48, 2644 }, id => Assert.Equal(SpellBarRule.UtilityTab, SpellBarRule.TabOf(table[id]))); // Lifestone Recall, Primary Portal Tie, Primary Portal Recall, Lifestone Tie
        Assert.Equal(SpellBarRule.FightTab, SpellBarRule.TabOf(table[21])); // Fire Vulnerability Other I
        Assert.Equal(SpellBarRule.VitalsTab, SpellBarRule.TabOf(table[1237])); // Drain Health Other I
        Assert.Equal(SpellBarRule.CreatureTab, SpellBarRule.TabOf(table[2])); // Strength Self I
        Assert.Equal(SpellBarRule.LifeTab, SpellBarRule.TabOf(table[24])); // Armor Self I
        Assert.Equal("Strength Self I", table[2].Name);
        Assert.Equal("Armor Self I", table[24].Name);
    }
}
