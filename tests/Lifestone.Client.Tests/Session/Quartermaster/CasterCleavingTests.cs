using System.Collections.Immutable;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using static Lifestone.Client.Tests.Session.Quartermaster.QuartersState;

namespace Lifestone.Client.Tests.Session.Quartermaster;

/// <summary>
/// M6 item 16, slice E5e3: the caster score's war term is what ACE multiplies her bolt by for the caster in her hands (<see cref="GearScore.Caster"/>): the elemental bonus it counted, and the resistance cleaving and the
/// mastery bonus it did not. The state and what it assumes: <see cref="QuartersState"/>. The judge is the gear keeps' of an element asked (<see cref="Wardrobe.KeepJudges"/>), so the test stands whatever names the element.
/// </summary>
public sealed class CasterCleavingTests
{
    [Fact]
    public void The_war_term_is_what_ACE_multiplies_her_bolt_by_the_cleaving_and_the_mastery_bonus_with_the_elemental_bonus()
    {
        // tools/fight-sim.ps1 matchup 1628, with and without --wield "Minor Smoldering Atlan Wand" (2026-11-18): her fire line's VII on a Tusker Slave, mean plain hit 169.2 with the sceptre and 381.5 with the wand, x2.25.
        var quarters = GroundCatalog.Get("tusker-quarters");
        var rig = MageOf(82, 379, Book(6, fireSeven: true));
        var spells = Spells();
        var sceptre = Sceptre(rig);
        var wand = AtlanWand(rig);
        var baton = Caster(rig, "Fire Baton", false, Wield290(Fire), new() { [AppraisalIds.ElementalDamageMod] = 1.15 }, []); // illustrative: a plain fire caster of a larger elemental bonus than the wand's
        var skills = new QualitiesSkills(rig.State.Self.Qualities);
        var wardrobe = Wardrobe.For(rig.State);
        var judge = wardrobe.KeepJudges(quarters, spells)(Fire);
        var context = judge.Context(skills);
        GearScoreResult Score(uint guid) => GearScore.Caster(guid, "caster", rig.State.Appraisals.TryGet(guid, out var a) ? a : throw new InvalidOperationException("not appraised"), context);

        // The wand on fire: its x1.12 and her Spirit Drinker's 0.06, times the cleaving's x2, without the x1.05 every caster she has mastered gives: x2.248 of a plain caster, the simulator's x2.25.
        var onWand = Score(wand);
        Assert.Equal((((1.12 + 0.06) * 2.0 / 1.05) - 1.0) * 100.0, onWand.WarPercent, 6);
        var note = onWand.Parts.Single(p => p.Name == "war").ToString();
        Assert.Contains("resistance cleaving x2 on it", note, StringComparison.Ordinal);
        Assert.Contains("tough to master: no x1.05 mastery bonus", note, StringComparison.Ordinal);

        // A caster with neither is scored as before: the sceptre's pierce bonus counts nothing on fire, the baton its elemental bonus and her aura.
        Assert.Equal(0.0, Score(sceptre).WarPercent, 9);
        Assert.Equal(((1.15 - 1.0) + 0.06) * 100.0, Score(baton).WarPercent, 9);

        // By the elemental bonus alone the baton (21) outranked the wand (18); by what ACE does to the bolt the wand is far ahead, and is the pick.
        Assert.True(Score(baton).WarPercent > ((1.12 - 1.0) + 0.06) * 100.0);
        Assert.True(onWand.CasterScore > Score(baton).CasterScore);
        var pick = judge.BestCaster(PackCasters(rig), SelfEquipJudge.Worn(rig.State), skills, out _);
        Assert.Equal(wand, pick!.Guid);

        // Off its element the wand cleaves nothing and still lacks the mastery bonus: every spell at 1/1.05 of another caster's.
        var onCold = GearScore.Caster(wand, "caster", rig.State.Appraisals.TryGet(wand, out var w) ? w : throw new InvalidOperationException("not appraised"), wardrobe.KeepJudges(quarters, spells)(0x8).Context(skills));
        Assert.Equal(((1.0 / 1.05) - 1.0) * 100.0, onCold.WarPercent, 6);
    }
}
