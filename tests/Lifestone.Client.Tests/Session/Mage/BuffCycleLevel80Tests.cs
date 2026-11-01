using System.Globalization;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 7 F12 (D90, the owner's point 3, optimal buffs): the level-80 soak mage's buff cycle, computed from the dats. The mage (D87: War and Life specialised, Creature, Item and Mana Conversion trained, no Melee Defense; the
/// spending plan's ranks at level 80) casts the profile's <see cref="IBuffProfile.BuffOrder"/> in one ordered pass and one upgrade pass (as <see cref="BuffCycle"/> does), each line at the highest level whose cast chance reaches
/// <see cref="BuffOptions.DefaultCastChance"/> from the skill the earlier casts left <b>and</b> whose foci-formula components the den profile's pack holds (<see cref="LevelFloor"/>: Pyreal Scarab 690 for VI, Platinum Scarab 8897 for VII,
/// Copper Scarab 686 for III, Prismatic Tapers 20631). The numbers are computed, not measured (the buff cycle's own live behaviour is the lead's).
/// </summary>
public sealed class BuffCycleLevel80Tests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>The den profile's pack for buffs: what the supplies carry (black-spawn-den-aluvian.json: Pyreal 300, Platinum 100, Copper 20, Prismatic Taper 1,000).</summary>
    internal static readonly Dictionary<uint, int> DenPack = new() { [686] = 20, [690] = 300, [8897] = 100, [20631] = 1000 };

    /// <summary>One line's final cast: the line, the spell chosen, the skill it was chosen from.</summary>
    internal sealed record Pick(ProfileLine Line, ProfileSpell Spell, uint Skill, double Chance, SpellInfo Info, string Skipped);

    /// <summary>The level-80 chain: the ordered pass then one upgrade pass over <paramref name="order"/> with <paramref name="pack"/> (null: no pack floor).</summary>
    internal static List<Pick> Chain(DatSet dats, IReadOnlyList<ProfileLine> order, Dictionary<uint, int>? pack, out uint war, out uint life, out uint creature, out uint item)
    {
        var spells = SpellTableData.Load(dats).Spells;
        var xp = XpTableData.Load(dats);
        var plan = MageSpendingPlanner.Compute(xp, xp.XpForLevel(80));
        StatTarget Target(MageStatKind kind, uint id) => plan.Targets.Single(t => t.Kind == kind && t.Id == id);
        var focusBase = MageTemplate.HighAttribute + (uint)Target(MageStatKind.Attribute, MageTemplate.Focus).Ranks;
        var selfBase = MageTemplate.HighAttribute + (uint)Target(MageStatKind.Attribute, MageTemplate.Self).Ranks;
        uint InitPlusRanks(uint skillId)
        {
            var t = Target(MageStatKind.Skill, skillId);
            return (t.Specialized ? 10u : 0u) + (uint)t.Ranks;
        }

        // Each line's currently active level (none yet): the skill adds are the sum of the active levels' mods, so an upgrade replaces the lower level of its line.
        var active = new Dictionary<string, ProfileSpell>();
        uint Current(uint skillId)
        {
            int focusAdd = 0, selfAdd = 0, skillAdd = 0;
            foreach (var spell in active.Values)
            {
                var mod = BuffStatMods.BySpell[spell.Id];
                if (mod.IsAttribute && mod.IsAdditive)
                {
                    focusAdd += mod.Key == MageTemplate.Focus ? (int)mod.Value : 0;
                    selfAdd += mod.Key == MageTemplate.Self ? (int)mod.Value : 0;
                }
                else if (mod.IsSkill && mod.IsAdditive && mod.Key == skillId)
                {
                    skillAdd += (int)mod.Value;
                }
            }

            return GearCasts.SkillValue(focusBase + (uint)focusAdd, selfBase + (uint)selfAdd, InitPlusRanks(skillId), 1.0, null, skillAdd);
        }

        var picks = new Dictionary<string, Pick>();
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var line in order)
            {
                var skillId = GearCasts.SkillOf(line.School);
                var skill = Current(skillId);
                ProfileSpell? best = null;
                string skipped = string.Empty;
                for (var i = line.Spells.Count - 1; i >= 0; i--)
                {
                    var s = line.Spells[i];
                    if (GearCasts.Chance(skill, s.Power) < BuffOptions.DefaultCastChance)
                    {
                        continue;
                    }

                    if (pack is not null && LevelFloor.ShortOf(pack, spells[s.Id], out var need))
                    {
                        skipped = skipped.Length == 0 ? LevelFloor.SkipText(s, need) : skipped;
                        continue;
                    }

                    best = s;
                    break;
                }

                if (best is null || (active.TryGetValue(line.Name, out var have) && have.Power >= best.Power))
                {
                    continue; // none payable, or the upgrade pass finds nothing higher (the cycle casts a line again only for a higher level)
                }

                active[line.Name] = best;
                picks[line.Name] = new Pick(line, best, skill, GearCasts.Chance(skill, best.Power), spells[best.Id], skipped);
            }
        }

        war = Current(MageTemplate.WarMagic);
        life = Current(MageTemplate.LifeMagic);
        creature = Current(MageTemplate.CreatureEnchantment);
        item = Current(MageTemplate.ItemEnchantment);
        return [.. order.Where(l => picks.ContainsKey(l.Name)).Select(l => picks[l.Name])];
    }

    [RequiresDatsFact]
    public void The_level_80_chain_prints_every_line_at_the_level_the_dens_pack_pays()
    {
        var order = ArenaProfile.Buffs.BuffOrder.Where(l => !l.ByPlay).ToList(); // M5 7 P7j: the build's lines (Spirit Drinker, learned by play, is IV only: BuffCycleTests pins its gate)
        var picks = Chain(fixture.Dats, order, DenPack, out var war, out var life, out var creature, out var item);
        var ideal = Chain(fixture.Dats, order, null, out _, out _, out _, out _);
        foreach (var p in picks)
        {
            var best = ideal.Single(i => i.Line == p.Line);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[measure] f12-buff line={p.Line.Name} spell={p.Spell.Name} id={p.Spell.Id} level={LevelFloor.NumeralOf(p.Spell)} duration_s={p.Spell.DurationSeconds} power={p.Spell.Power} chance={p.Chance:0.000} skill={p.Skill} base_mana={p.Info.BaseMana} ideal_level={LevelFloor.NumeralOf(best.Spell)} skipped=\"{p.Skipped}\""));
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[measure] f12-buff lines={picks.Count} war={war} life={life} creature={creature} item={item} base_mana_total={picks.Sum(p => (long)p.Info.BaseMana)}"));
        Assert.Equal(24, picks.Count);

        // The finding: at level 80 every line is Level VI (power 250, 2,700 s), the level the Pyreal Scarab pays; the pack costs no level (the ideal chain is the same), and Level VII (power 300, the Platinum Scarab) is out of reach of
        // the Creature, Item and Life skills (325 to 326 buffed) at the 0.9 chance D10 asks: it needs 332. The masteries VII would add 5 points for 900 s more and 15% fizzles of a Platinum Scarab each: not worth the owner's 0.9 rule.
        Assert.All(picks, p => Assert.Equal(6, p.Spell.Position));
        Assert.All(picks, p => Assert.Equal(2700.0, p.Spell.DurationSeconds));
        Assert.All(picks, p => Assert.Equal(ideal.Single(i => i.Line == p.Line).Spell.Id, p.Spell.Id));
        Assert.All(picks, p => Assert.Equal(string.Empty, p.Skipped));
        var needForVii = 0u;
        while (GearCasts.Chance(needForVii, 300) < BuffOptions.DefaultCastChance)
        {
            needForVii++;
        }

        Assert.Equal(332u, needForVii);
        Assert.InRange(creature, 320u, needForVii - 1);
        Assert.InRange(item, 320u, needForVii - 1);
        Assert.InRange(life, 320u, needForVii - 1);
        Assert.InRange(war, 355u, 375u);
        Assert.Equal(["Focus Self", "Willpower Self", "Creature Enchantment Mastery Self"], picks.Take(3).Select(p => p.Line.Name));
        Assert.All(new[] { "War Magic Mastery Self", "Regeneration Self", "Rejuvenation Self", "Mana Renewal Self", "Bludgeon Protection Self", "Bludgeon Bane", "Impenetrability", "Hermetic Link", "Defender" }, key => Assert.Contains(picks, p => p.Line.Name == key));
    }
}
