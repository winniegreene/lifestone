using Lifestone.Client.Session;
using Lifestone.Client.Session.Recording;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Dat;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// <see cref="QualitiesWeenie"/> over the real PlayerDescriptions in <c>cache/sessions</c> (D14: regenerated locally, never committed): what the live ACE sends in a
/// skill's record, so the rule "formula + init + ranks, no class bonus" is checked against the server and not against a hand-made table. Skipped without recordings.
/// </summary>
public sealed class QualitiesWeenieRecordingTests(ITestOutputHelper output)
{
    private const uint Endurance = 2;
    private const uint Quickness = 3;

    // The live record of the test characters (created with the headless character's skill classes: Run and Jump trained): their Endurance, Quickness, Run level and run rate.
    private const uint LiveEndurance = 55;
    private const uint LiveQuickness = 55;
    private const int LiveRunSkill = 60;
    private const float LiveRunRate = 1.635f;

    /// <summary>Run: (Endurance + Quickness) / 3 (the shape of the retail table's rule, with stand-in numbers; the class gate is the same as the table's).</summary>
    private static readonly SkillRule RunRule = new(new SkillTableFormula(0, 1, 1, 3, Endurance, Quickness), MinLevel: 1);

    [RequiresRecordingFact]
    public void A_trained_skill_in_a_real_PlayerDescription_holds_its_creation_bonus_in_its_own_record()
    {
        var seen = 0;
        foreach (var path in RealRecordings.Find())
        {
            // The qualities as the PlayerDescription set them: the recording's logoff clears the replayed state afterwards.
            var qualities = RealRecordings.QualitiesAtDescription(path);
            if (qualities is not { IsKnown: true })
            {
                continue;
            }

            foreach (var id in new[] { QualitiesWeenie.RunSkillId, QualitiesWeenie.JumpSkillId })
            {
                if (!qualities.TryGetSkill(id, out var skill))
                {
                    continue;
                }

                output.WriteLine($"{Path.GetFileName(path)}: skill {id} class {skill.AdvancementClass} ranks {skill.Ranks} init {skill.Init} experience {skill.Experience}");
                if (skill.AdvancementClass is 2 or 3)
                {
                    // Trained is 5 and specialised 10 at creation; ACE keeps that value in the record (ranks or init), so class + record never both count it.
                    Assert.True(skill.Ranks + skill.Init >= (skill.AdvancementClass == 3 ? 10u : 5u), $"skill {id} (class {skill.AdvancementClass}) has no creation bonus in its record");
                    seen++;
                    if (id == QualitiesWeenie.RunSkillId && qualities.TryGetAttribute(Endurance, out var endurance) && qualities.TryGetAttribute(Quickness, out var quickness))
                    {
                        // CACQualities::InqSkill: formula + init + ranks, the class adds nothing.
                        Assert.True(RunRule.Formula.TryCalculate(endurance.Start + endurance.Ranks, quickness.Start + quickness.Ranks, out var formula));
                        Assert.Equal((int)formula + (int)skill.Init + skill.Ranks, new QualitiesWeenie(qualities, RunRule, RunRule).RunSkill);
                    }
                }
            }
        }

        output.WriteLine($"{seen} trained or specialised skill record(s) read");
        Assert.True(seen > 0, "no recording holds a trained or specialised Run or Jump: the rule was not checked against any server record");
    }

    /// <summary>
    /// The circular check above cannot catch a wrong rule, so one absolute value from the live record is pinned, over the retail SkillTable: the test characters' Run, trained at
    /// creation (class 2, 5 ranks, init 0) over their recorded Endurance and Quickness (55 each), is level 60 and runs at 1.635. Adding the class bonus (5) would make it 65 and 1.675.
    /// </summary>
    [RequiresRecordingAndDatsFact]
    public void The_run_level_and_rate_of_a_recorded_test_character_are_the_values_seen_live()
    {
        using var dats = DatSet.Open();
        var skills = SkillTableData.Load(dats);
        var pinned = 0;
        foreach (var path in RealRecordings.Find())
        {
            // The qualities as the PlayerDescription set them: the recording's logoff clears the replayed state afterwards.
            var qualities = RealRecordings.QualitiesAtDescription(path);
            if (qualities is not { IsKnown: true }
                || !qualities.TryGetSkill(QualitiesWeenie.RunSkillId, out var run)
                || !qualities.TryGetAttribute(Endurance, out var endurance)
                || !qualities.TryGetAttribute(Quickness, out var quickness))
            {
                continue;
            }

            var (e, q) = (endurance.Start + endurance.Ranks, quickness.Start + quickness.Ranks);
            var weenie = QualitiesWeenie.FromSkillTable(qualities, skills);
            Assert.True(weenie.TryGetRunRate(out var rate));
            output.WriteLine($"{Path.GetFileName(path)}: class {run.AdvancementClass} ranks {run.Ranks} init {run.Init}, endurance {e} quickness {q}: Run {weenie.RunSkill}, run rate {rate:0.000}");
            if (run is { AdvancementClass: 2, Ranks: 5, Init: 0 } && (e, q) == (LiveEndurance, LiveQuickness))
            {
                Assert.Equal(LiveRunSkill, weenie.RunSkill);
                Assert.Equal(LiveRunRate, rate, 3);
                pinned++;
            }
        }

        Assert.True(pinned > 0, $"no recording holds a Run trained at creation over Endurance {LiveEndurance} and Quickness {LiveQuickness}: the pinned live values (Run {LiveRunSkill}, rate {LiveRunRate}) were not compared; regenerate the recordings with the test characters or update the pin from a live record");
    }

    /// <summary>
    /// A fresh character's vitals in a real PlayerDescription: the server's current values at login are its maximums (nothing is spent, hurt or buffed), so
    /// <see cref="PlayerQualities.TryGetVitalLevels"/>'s formula must give exactly them (28, 55, 55 for the test characters' Endurance and Self of 55). A recording of a character that was
    /// changed since (a buff, raised ranks) is skipped; the level-1 test characters' recordings are the ones that count.
    /// </summary>
    [RequiresRecordingFact]
    public void A_fresh_characters_vital_maximums_in_a_real_PlayerDescription_equal_the_currents_the_server_sent()
    {
        var checkedCount = 0;
        foreach (var path in RealRecordings.Find())
        {
            // The qualities as the PlayerDescription set them: the recording's logoff clears the replayed state afterwards.
            var qualities = RealRecordings.QualitiesAtDescription(path);
            if (qualities is not { IsKnown: true } || !qualities.TryGetInt(25, out var level) || level != 1)
            {
                continue;
            }

            foreach (var id in new uint[] { 1, 3, 5 })
            {
                if (!qualities.TryGetVital(id, out var vital) || vital.Ranks != 0 || vital.Start != 0)
                {
                    continue;
                }

                Assert.True(qualities.TryGetVitalLevels(id, out var current, out var max));
                output.WriteLine($"{Path.GetFileName(path)}: vital {id} current {current} maximum {max}");
                Assert.Equal(current, max);
                checkedCount++;
            }
        }

        Assert.True(checkedCount > 0, "no recording holds a fresh level-1 character's vitals: the formula was not compared with any server record");
    }
}
