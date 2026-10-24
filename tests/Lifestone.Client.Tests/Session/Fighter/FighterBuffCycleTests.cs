using System.Collections.Immutable;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// The buff cycle over a fighter's profile (M4 P5a lane C1, owner decision D33): <see cref="BuffCycle"/> takes an <see cref="IBuffProfile"/>, so a Brawler or an Archer is buffed with its own twelve lines in the cycle's order, each at
/// the highest level whose cast chance (<see cref="GearCasts.Chance"/>) reaches <see cref="BuffOptions.CastChance"/>: level IV at Creature Enchantment and Life Magic 182 and over, level III from 132, nothing below.
/// The model is a WorldActionRig whose server answers every cast with UseDone (no enchantment event: the cycle's level choice and cast order are what is judged here, not the stat arithmetic).
/// </summary>
public sealed class FighterBuffCycleTests
{
    private sealed class Rig
    {
        public Rig(FighterProfile profile, uint creatureSkill, uint lifeSkill)
        {
            R = new WorldActionRig();
            Table = [];
            var category = 200u;
            foreach (var line in profile.Lines)
            {
                category++;
                foreach (var spell in line.Spells)
                {
                    var meta = line.Role switch
                    {
                        ProfileRole.ManaTransfer => SpellType.Transfer,
                        ProfileRole.StaminaRestore or ProfileRole.HarmSelf or ProfileRole.HealSelf => SpellType.Boost,
                        _ => SpellType.Enchantment,
                    };
                    Table[spell.Id] = WorldActionRig.Spell(spell.Id, meta, SpellFlags.Beneficial | SpellFlags.SelfTargeted) with
                    {
                        Name = spell.Name,
                        School = line.School,
                        Power = spell.Power,
                        Duration = spell.DurationSeconds,
                        BaseMana = 10,
                        Category = category,
                    };
                }
            }

            R.Spells([.. Table.Values]);
            var attributes = ImmutableArray.CreateBuilder<AttributeDesc>();
            for (uint id = 1; id <= 6; id++)
            {
                attributes.Add(new AttributeDesc(id, 0, 100, 0));
            }

            // Two schools trained with ranks so that the current skill is the figure asked for; the formula part (Focus 100 + Self 100) / 4 = 50 is the attributes'.
            var skills = ImmutableArray.CreateBuilder<SkillDesc>();
            skills.Add(new SkillDesc(FighterTemplate.CreatureEnchantment, (ushort)(creatureSkill - 50), 1, 2, 0, 0, 0, 0));
            skills.Add(new SkillDesc(FighterTemplate.LifeMagic, (ushort)(lifeSkill - 50), 1, 2, 0, 0, 0, 0));
            var vitals = ImmutableArray.CreateBuilder<VitalDesc>();
            vitals.Add(new VitalDesc(FighterTemplate.MaxHealth, 0, 0, 0, 200));
            vitals.Add(new VitalDesc(FighterTemplate.MaxStamina, 0, 0, 0, 275));
            vitals.Add(new VitalDesc(MageTemplate.MaxMana, 0, 0, 0, 1000));
            R.H.Describe(new PlayerDescriptionMsg { Attributes = attributes.ToImmutable(), Skills = skills.ToImmutable(), Vitals = vitals.ToImmutable() });
            R.CombatModeIs(CombatModes.Magic);
            Cycle = new BuffCycle(R.Api, R.H.State, Table, new BuffOptions(), R.H.Log, profile: profile);
            Profile = profile;
        }

        public WorldActionRig R { get; }

        public Dictionary<uint, SpellInfo> Table { get; }

        public BuffCycle Cycle { get; }

        public FighterProfile Profile { get; }

        public List<uint> Casts { get; } = [];

        public BuffStatus Run(double maxSeconds = 600)
        {
            var seen = 0;
            Assert.True(Cycle.Start(R.H.R.Clock.Now));
            var status = BuffStatus.Running;
            var end = R.H.R.Clock.Now + maxSeconds;
            while (status == BuffStatus.Running && R.H.R.Clock.Now < end)
            {
                R.Tick(0.25);
                var sends = R.H.Net.Sends;
                while (seen < sends.Count)
                {
                    var send = sends[seen++];
                    if (send.Kind == FakeSendKind.CastUntargetedSpell)
                    {
                        Casts.Add((uint)send.Guid);
                        R.UseDone();
                    }
                    else if (send.Kind == FakeSendKind.ChangeCombatMode)
                    {
                        R.CombatModeIs((int)send.Amount);
                    }
                }

                R.Tick();
                status = Cycle.Step(R.H.R.Clock.Now);
            }

            return status;
        }

        /// <summary>The (line name, level position) of every buff-line cast, in order.</summary>
        public List<(string Line, int Position)> Buffs() =>
            [.. Casts.Select(id => Profile.Lines.First(l => l.Spells.Any(s => s.Id == id))).Zip(Casts, (line, id) => (line, id)).Where(x => x.line.IsBuff).Select(x => (x.line.Name, x.line.Spells.First(s => s.Id == x.id).Position))];
    }

    [Theory]
    [InlineData("Holtburg Brawler", 183u, 182u, 4)]
    [InlineData("Holtburg Archer", 183u, 183u, 4)]
    [InlineData("Holtburg Brawler", 140u, 140u, 3)]
    [InlineData("Holtburg Archer", 132u, 190u, 3)]
    public void The_cycle_casts_the_fighters_twelve_lines_at_the_highest_level_the_chance_rule_allows_M4_P5a(string name, uint creature, uint life, int expectedPosition)
    {
        var profile = FighterProfile.ForName(name)!;
        var t = new Rig(profile, creature, life);
        Assert.Equal(12, t.Cycle.Wanted);
        Assert.Same(profile, t.Cycle.Profile);

        var status = t.Run();
        Assert.Equal(BuffStatus.Done, status);

        // Each line was cast; the level follows the school's skill (Creature Enchantment lines and Life Magic lines are judged by their own school), never above what the 0.9 rule allows.
        var buffs = t.Buffs();
        Assert.Equal(profile.Lines.Where(l => l.IsBuff).Select(l => l.Name).Order(), buffs.Select(b => b.Line).Distinct().Order());
        foreach (var (lineName, position) in buffs)
        {
            var line = profile.LineNamed(lineName)!;
            var skill = line.School == MagicSchool.LifeMagic ? life : creature;
            var allowed = line.Spells.Where(s => GearCasts.Chance(skill, s.Power) >= 0.9).Max(s => s.Position);
            Assert.True(position <= allowed, $"{lineName} cast at level {position}, the rule allows {allowed} at skill {skill}");
        }

        // The first two casts are Focus and Willpower (they raise the skills every later cast rolls against), then the weapon mastery.
        Assert.Equal(["Focus Self", "Willpower Self", profile.WeaponMastery.Name], buffs.Select(b => b.Line).Distinct().Take(3));
        Assert.Equal(expectedPosition, buffs.Last(b => b.Line == "Strength Self").Position);
    }

    [Fact]
    public void A_skill_under_the_level_III_floor_casts_level_II_M4_P5a()
    {
        var low = new Rig(FighterProfile.Brawler, 120, 120);
        Assert.Equal(BuffStatus.Done, low.Run());
        Assert.All(low.Buffs(), b => Assert.True(b.Position <= 2, $"{b.Line} at {b.Position}")); // chance at skill 120: power 50 (II) 0.97, power 100 (III) 0.80
    }

    [Fact]
    public void The_profile_reaches_the_cycle_through_the_host_logic_by_character_name_M4_P5a()
    {
        Assert.True(BuffProfiles.Has("BSD Mage"));
        Assert.True(BuffProfiles.Has("holtburg brawler"));
        Assert.True(BuffProfiles.Has("Holtburg Archer"));
        Assert.False(BuffProfiles.Has("Holtburg Hunter"));
        Assert.False(BuffProfiles.Has(null));
        Assert.Same(ArenaProfile.Buffs, BuffProfiles.For("BSD Mage"));
        Assert.Same(FighterProfile.Brawler, BuffProfiles.For("Holtburg Brawler"));
        Assert.Same(FighterProfile.Archer, BuffProfiles.For("HOLTBURG ARCHER"));
        Assert.Null(BuffProfiles.For("Lifestone Smoke"));
    }
}
