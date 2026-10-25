using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Data.Animation;
using Lifestone.Data.Models;
using Lifestone.Data.Motion;
using Lifestone.Data.Tests;
using Lifestone.Data.Textures;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>
/// The creatures of the hero takes' flicker (archer2 93.6 s and 91.0 s: a Crude Monouga and a Tusker Crimsonback by the looting Archer) play every attack
/// and hit reaction of their combat styles without a pose jump or flip (<see cref="AvatarJumpWatch"/>): the flicker is not in their animations
/// (docs/milestones/m4-hero-visual-bugs.md). The Setups, motion tables and scales are the world database's (weenies 2575 and 1627).
/// </summary>
public sealed class CreatureAttackPoseTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [RequiresDatsTheory]
    [InlineData(0x020002FFu, 0x09000027u, 1.62f)] // Crude Monouga
    [InlineData(0x02000964u, 0x0900000Cu, 1.1f)] // Tusker Crimsonback
    public void Every_attack_and_hit_reaction_plays_without_a_pose_jump(uint setup, uint table, float scale)
    {
        var dats = fixture.Dats;
        var sources = AvatarSources.FromDats(dats, new TextureLoader(dats), new GfxObjMeshCache(dats));
        var data = sources.GetMotionTable(table)!;
        var played = 0;
        foreach (var style in data.Links.Keys.Select(k => k >> 16).Distinct().Where(s => s != (MotionCommands.NonCombat & 0xFFFF)))
        {
            if (!data.Links.TryGetValue(MotionTableData.Key(style, MotionCommands.Ready), out var fromReady))
            {
                continue;
            }

            foreach (var motion in fromReady.Keys.Where(m => (m & 0xFF000000) == 0x10000000).Order())
            {
                var set = new AvatarSet(sources);
                Assert.Equal(AvatarApply.Applied, set.Create(AvatarRig.HumanAt(7, new Vector3(50, 50, 0), 180f) with { SetupId = setup, MotionTableId = table, Scale = scale, HasContact = true }, now: 0));
                var wireStyle = (ushort)style;
                var stance = WireMovement.Interpreted(wireStyle, new WireInterpretedMotion(wireStyle, null, null, null, null, null, null));
                Assert.Equal(AvatarApply.Applied, set.SetMovement(7, isSelf: false, autonomous: false, stance, []));
                var now = Advance(set, 0.0, frames: 60);

                // ACE swings at speeds near 1.5 (Creature.GetAnimSpeed); the action plays out and the creature returns to its combat Ready.
                Assert.Equal(AvatarApply.Applied, set.SetMovement(7, isSelf: false, autonomous: false, stance, [new WireMotionItem((ushort)motion, 1, false, 1.5f)]));
                Advance(set, now, frames: 150);
                Assert.True(set.Stats.CreatureJumps == 0 && set.Stats.CreatureFlips == 0, $"{MotionCommandTable.NameOf(0x10000000 | (motion & 0xFFFF))} in style 0x{style:X4}: jumps {set.Stats.CreatureJumps}, flips {set.Stats.CreatureFlips}");
                played++;
            }
        }

        Assert.True(played >= 3, $"only {played} actions played");
    }

    private static double Advance(AvatarSet set, double now, int frames)
    {
        var viewer = new AvatarViewer(AvatarRig.At(new Vector3(50, 45, 0)));
        for (var i = 0; i < frames; i++)
        {
            now += 1.0 / 60.0;
            set.Advance(now, viewer, long.MaxValue);
        }

        return now;
    }
}
