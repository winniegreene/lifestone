using DatReaderWriter.DBObjs;
using Lifestone.Data.Animation;
using DatAnimation = DatReaderWriter.DBObjs.Animation;

namespace Lifestone.Data.Tests.Animation;

/// <summary>Decodes every Animation (0x03) and MotionTable (0x09) in the user's portal.</summary>
public sealed class AnimationDataTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_animation_decodes_exactly_and_matches_DatReaderWriter()
    {
        var dats = fixture.Dats;
        var source = new DatAnimationSource(dats);
        var ids = source.AllAnimationIds();
        Assert.Equal(2066, ids.Count); // docs/research/dat-format.md §0

        var hookTypes = new SortedSet<AnimHookType>();
        var withPos = 0;
        foreach (var id in ids)
        {
            var anim = source.GetAnimation(id)!; // AnimationData.Decode throws unless every byte is consumed
            Assert.Equal(anim.NumFrames, anim.PartFrames.Count);
            Assert.Equal((anim.Flags & AnimationData.FlagPosFrames) != 0, anim.PosFrames is not null);
            if (anim.PosFrames is not null)
            {
                withPos++;
                Assert.Equal(anim.NumFrames, anim.PosFrames.Count);
            }

            Assert.True(dats.TryGetPortal<DatAnimation>(id, out var drw), $"DatReaderWriter cannot read 0x{id:X8}");
            Assert.Equal((uint)drw.Flags, anim.Flags);
            Assert.Equal(drw.NumParts, (uint)anim.NumParts);
            Assert.Equal(drw.PartFrames.Count, anim.NumFrames);
            for (var f = 0; f < anim.NumFrames; f++)
            {
                var mine = anim.PartFrames[f];
                var theirs = drw.PartFrames[f];
                Assert.Equal(anim.NumParts, mine.Frames.Count);
                for (var p = 0; p < anim.NumParts; p++)
                {
                    Assert.Equal(theirs.Frames[p].Origin, mine.Frames[p].Origin);
                    Assert.Equal(theirs.Frames[p].Orientation, mine.Frames[p].Orientation);
                    var q = mine.Frames[p].Orientation;
                    Assert.InRange(q.Length(), 0.99f, 1.01f);
                }

                Assert.Equal(theirs.Hooks.Select(h => (uint)h.HookType), mine.Hooks.Select(h => (uint)h.Type));
                Assert.Equal(theirs.Hooks.Select(h => (int)(uint)h.Direction), mine.Hooks.Select(h => (int)h.Direction));
                foreach (var h in mine.Hooks)
                {
                    hookTypes.Add(h.Type);
                }
            }
        }

        Assert.Equal(357, withPos);

        // Hook types used by animations at end of retail (13 of the 27 retail types).
        Assert.Equal(
            new[]
            {
                AnimHookType.Sound, AnimHookType.SoundTable, AnimHookType.Attack, AnimHookType.ReplaceObject, AnimHookType.Ethereal,
                AnimHookType.TransparentPart, AnimHookType.CreateParticle, AnimHookType.StopParticle, AnimHookType.DefaultScript,
                AnimHookType.CallPES, AnimHookType.Transparent, AnimHookType.SoundTweaked, AnimHookType.SetOmega,
            }.Order(),
            hookTypes);
    }

    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_motion_table_converts_and_references_existing_animations()
    {
        var dats = fixture.Dats;
        var source = new DatAnimationSource(dats);
        var ids = source.AllMotionTableIds();
        Assert.Equal(436, ids.Count);

        var missing = new SortedSet<uint>();
        foreach (var id in ids)
        {
            var table = source.GetMotionTable(id)!;
            Assert.Equal(id, table.Id);
            Assert.True(table.StyleDefaults.ContainsKey(table.DefaultStyle), $"0x{id:X8}: no default substate for its default style");
            foreach (var data in table.Cycles.Values.Concat(table.Modifiers.Values).Concat(table.Links.Values.SelectMany(l => l.Values)))
            {
                Assert.True(float.IsFinite(data.Velocity.X) && float.IsFinite(data.Omega.Z));
                foreach (var anim in data.Anims)
                {
                    Assert.True(float.IsFinite(anim.Framerate));
                    if (source.GetAnimation(anim.AnimId) is null)
                    {
                        missing.Add(anim.AnimId);
                    }
                }
            }
        }

        // AnimSequence would drop nodes whose animation is missing (retail AnimSequenceNode::has_anim); none are.
        Assert.Empty(missing);
    }

    [RequiresDatsFact]
    public void Human_male_tables_come_from_CharGen()
    {
        Assert.True(fixture.Dats.TryGetPortal<CharGen>(0x0E000002, out var cg));
        var male = cg.HeritageGroups[1].Genders[1];
        Assert.Equal("Aluvian", cg.HeritageGroups[1].Name.Value);
        Assert.Equal(0x02000001u, male.SetupId.DataId);
        Assert.Equal(0x09000001u, male.MotionTable.DataId);
        Assert.Equal(0x34000004u, male.PhysicsTable.DataId);
        Assert.Equal(100u, male.Scale);
    }
}
