using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;
using Lifestone.Data.Models;
using Lifestone.Data.Tests;
using Lifestone.Data.Textures;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>
/// M3 4.2 x2, item 2 (cluster-k3 gap 1: the buff leg printed <c>cast_scripts=2</c>). ACE's <c>DoSpellEffects</c> (<c>WorldObject_Magic.cs</c> 356-365) broadcasts the spell's CasterEffect on the caster and its
/// TargetEffect on the target by the target's guid; every one of the 24 buff spells has no CasterEffect and a TargetEffect, so a self cast is one F755 on the player and an armour cast is one F755 on the armour piece,
/// which carries no PhysicsScriptTable (retail's play_script is a no-op without a table). This runs the cycle's casts (every buff line's top spell: the self lines once, the armour lines on each of five
/// pieces) through the avatar set with the human's real table: no self cast's script is dropped, and the armour pieces' are the missing ones, so the count the leg prints is the self casts, not a loss in the avatar.
/// </summary>
public sealed class BuffCastScriptsTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint HumanSetup = 0x02000001;
    private const uint HumanTable = 0x09000001;
    private const uint HumanScriptTable = 0x34000004;
    private const uint Player = 0x50000001;
    private const uint FirstArmour = 0x80000100;
    private const int Pieces = 5;
    private static readonly AvatarViewer Viewer = new(AvatarRig.At(new Vector3(96, 96, 0)));

    [RequiresDatsFact]
    public void The_cycles_casts_play_every_self_cast_script_on_the_player_and_the_armour_casts_find_no_table()
    {
        var dats = fixture.Dats;
        var spells = SpellTableData.Load(dats);
        var set = new AvatarSet(AvatarSources.FromDats(dats, new TextureLoader(dats), new GfxObjMeshCache(dats)));
        var self = AvatarRig.HumanAt(Player, new Vector3(100, 100, 0)) with { IsSelf = true, SetupId = HumanSetup, MotionTableId = HumanTable, PhysicsScriptTableId = HumanScriptTable };
        Assert.Equal(AvatarApply.Applied, set.Create(self, now: 0));
        for (uint i = 0; i < Pieces; i++)
        {
            var piece = AvatarRig.HumanAt(FirstArmour + i, new Vector3(103 + i, 100, 0)) with { IsCreature = false, SetupId = HumanSetup, MotionTableId = 0, PhysicsScriptTableId = 0 };
            Assert.Equal(AvatarApply.Applied, set.Create(piece, now: 0));
        }

        var selfCasts = 0;
        var armourCasts = 0;
        foreach (var line in ArenaProfile.Lines.Where(l => l.Role is ProfileRole.CreatureBuff or ProfileRole.LifeBuff or ProfileRole.ItemBuffArmor or ProfileRole.ItemBuffWand))
        {
            var spell = spells.Spells[line.Spells[^1].Id];
            Assert.Equal(DatReaderWriter.Enums.PlayScript.Invalid, spell.CasterEffect); // no buff spell has a caster effect: one F755 per cast
            Assert.NotEqual(DatReaderWriter.Enums.PlayScript.Invalid, spell.TargetEffect);
            var self_ = (spell.Flags & SpellFlags.SelfTargeted) != 0;
            var casts = self_ ? 1 : Pieces;
            for (uint c = 0; c < casts; c++)
            {
                set.OpenCastWindow();
                set.PlayScript(self_ ? Player : FirstArmour + c, (uint)spell.TargetEffect, 1f);
                set.Advance(1.0 / 60.0, Viewer, long.MaxValue);
                set.CloseCastWindow();
                set.Advance(1.0 / 60.0, Viewer, long.MaxValue);
                if (self_)
                {
                    selfCasts++;
                }
                else
                {
                    armourCasts++;
                }
            }
        }

        Assert.Equal(selfCasts + armourCasts, set.Stats.CastWindows);
        Assert.InRange(selfCasts + armourCasts, 24, 40);
        Assert.Equal(selfCasts, set.Stats.CastScripts); // every self cast's script played in its window
        Assert.Equal(selfCasts, set.Stats.ServerScriptsPlayed);
        Assert.Equal(armourCasts, set.Stats.ServerScriptsMissing); // the armour pieces carry no PhysicsScriptTable: a silent no-op in retail, counted here
        Assert.Equal(0, set.Stats.ServerEffectsDropped);
    }
}
