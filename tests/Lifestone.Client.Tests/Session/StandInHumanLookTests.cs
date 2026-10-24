using Lifestone.Client.Avatars;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.CharacterCreation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Recording;
using Lifestone.Client.Session.StandIn;
using Lifestone.Data.Appearance;
using Lifestone.Data.Models;
using Lifestone.Data.Textures;
using Lifestone.Data.Tests;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Types;
using AnimPartChange = Lifestone.Net.Types.AnimPartChange;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M3 4.2 p6: the stand-in's humans are dressed. The stand-in used to send <c>ModelData.Empty</c> for every human, so the player, the placeholder NPCs and the corpse were the bare base body (the naked figures of the
/// Holtburg frames); now it sends the look the host composed (<see cref="StandInHumanLook"/>), the way a server sends a human's CreateObject. A real recording's clothed NPC is the reference for what dressed means.
/// </summary>
public sealed class StandInHumanLookTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private static readonly ModelData PlayerLook = new(0x04000001, [new SubPaletteSwap(0x04000002, 24, 8)], [new TextureChange(0, 0x05000010, 0x05000011)], [new AnimPartChange(3, 0x01000020)]);
    private static readonly ModelData GuardLook = new(0x04000001, [new SubPaletteSwap(0x04000003, 64, 0)], [new TextureChange(9, 0x05000012, 0x05000013)], [new AnimPartChange(4, 0x01000021)]);

    private static void AssertSame(ModelData expected, ModelData actual)
    {
        Assert.Equal(expected.BasePaletteId, actual.BasePaletteId);
        Assert.Equal(expected.SubPalettes.ToArray(), actual.SubPalettes.ToArray());
        Assert.Equal(expected.TextureChanges.ToArray(), actual.TextureChanges.ToArray());
        Assert.Equal(expected.AnimPartChanges.ToArray(), actual.AnimPartChanges.ToArray());
    }

    /// <summary>The GfxObj each part ends on: a later change of the same part replaces an earlier one.</summary>
    private static Dictionary<int, uint> LastPartChanges(ModelData model)
    {
        var last = new Dictionary<int, uint>();
        foreach (var change in model.AnimPartChanges)
        {
            last[change.PartIndex] = change.GfxObjId;
        }

        return last;
    }

    private static ModelData ModelOf(WorldObjectTable objects, uint guid) => objects.TryGet(guid, out var obj) ? obj.Model : throw new InvalidOperationException($"no object 0x{guid:X8}");

    [Fact]
    public void An_ObjDesc_converts_to_the_wire_and_back_without_loss()
    {
        AssertSame(PlayerLook, StandInHumanLook.FromObjDesc(AvatarWire.ToObjDesc(PlayerLook)));
        AssertSame(GuardLook, StandInHumanLook.FromObjDesc(AvatarWire.ToObjDesc(GuardLook))); // length byte 0 is the whole 2,048-colour palette
        Assert.Same(ModelData.Empty, StandInHumanLook.FromObjDesc(ObjDesc.Empty));
    }

    [Fact]
    public void The_player_and_every_human_object_are_created_in_their_looks_and_a_non_human_in_none()
    {
        var visit = new StandInVisit
        {
            EnterAfter = 0.2,
            UnhideAfter = 1.0,
            HumanModel = PlayerLook,
            HumanLooks = new Dictionary<string, ModelData> { ["Guard"] = GuardLook },
            Objects =
            [
                new StandInObject(0x80000D01, "Guard", default),
                new StandInObject(0x80000D02, "Statue", default),
                new StandInObject(0x80000D03, "Beast", default, Setup: 0x02000964),
            ],
        };
        using var rig = new CastRig(visit);
        rig.Begin();
        rig.Run(3.0);
        var objects = rig.Session.State.Objects;
        Assert.Equal(PlayerLook, ModelOf(objects, rig.Session.State.Self.Guid));
        Assert.Equal(GuardLook, ModelOf(objects, 0x80000D01));
        Assert.Equal(PlayerLook, ModelOf(objects, 0x80000D02)); // no look of its own: the default
        Assert.Equal(ModelData.Empty, ModelOf(objects, 0x80000D03)); // not a human Setup
    }

    [Fact]
    public void A_visit_without_a_look_still_sends_the_base_body_and_the_corpse_wears_what_the_player_wore()
    {
        using var bare = new CastRig(new StandInVisit { EnterAfter = 0.2, UnhideAfter = 1.0 });
        bare.Begin();
        bare.Run(2.0);
        Assert.Equal(ModelData.Empty, ModelOf(bare.Session.State.Objects, bare.Session.State.Self.Guid));

        using var rig = new CastRig(new StandInVisit { EnterAfter = 0.2, UnhideAfter = 1.0, HumanModel = PlayerLook, Stats = new StandInPlayerStats { Health = 5, Level = 30 } });
        var tally = new DeathTally();
        tally.Start(rig.Session);
        rig.Begin();
        rig.Run(1.0);
        var suicide = rig.Session.Actions.Suicide(ActionSource.Host);
        rig.Run(35.0);
        Assert.True(suicide.IsCompleted, suicide.Summary);
        Assert.Equal(1, tally.CorpsesSeen);
        Assert.Equal(PlayerLook, ModelOf(rig.Session.State.Objects, tally.LastCorpseGuid));
        tally.Dispose();
    }

    [RequiresDatsFact]
    public void The_composed_stand_in_look_round_trips_and_dresses_an_avatar_part_by_part()
    {
        var dats = fixture.Dats;
        var data = Cg3DViewData.FromDats(dats, new TextureLoader(dats), new GfxObjMeshCache(dats));
        var composition = CharGenAppearance.Compose(data.Record, OfflinePlayerLook.Create(data.Record, data.Dats), data.Dats);
        var model = StandInHumanLook.FromObjDesc(composition.ObjDesc);
        Assert.Equal(composition.ObjDesc.CacheKey, AvatarWire.ToObjDesc(model).CacheKey);
        Assert.NotEmpty(model.AnimPartChanges);
        Assert.NotEmpty(model.TextureChanges);
        Assert.NotEmpty(model.SubPalettes);

        var guard = CharGenAppearance.Compose(data.Record, OfflinePlayerLook.Create(data.Record, data.Dats, 1, 0.2), data.Dats);
        Assert.NotEqual(composition.ObjDesc.CacheKey, guard.ObjDesc.CacheKey); // another role wears another garment
        Assert.True(guard.Result.AllApplied, string.Join("; ", guard.Result.Problems));

        var avatars = new AvatarSet(AvatarSources.FromDats(dats, new TextureLoader(dats), new GfxObjMeshCache(dats)));
        var create = new AvatarCreate { Guid = 0x80000D10, IsCreature = true, SetupId = composition.SetupId, MotionTableId = composition.MotionTableId, ObjDesc = AvatarWire.ToObjDesc(model) };
        Assert.Equal(AvatarApply.Applied, avatars.Create(create, now: 0.0));
        Assert.True(avatars.TryGet(0x80000D10, out var avatar));
        var parts = avatar.Body.Appearance.Parts;
        foreach (var (index, gfxObj) in LastPartChanges(model))
        {
            Assert.Equal(gfxObj, parts[index].Chain.RootGfxObjId);
        }

        foreach (var texture in model.TextureChanges)
        {
            Assert.Contains(texture.NewTexture, parts[texture.PartIndex].SurfaceTextureBySurfaceId.Values);
        }

        Assert.True(parts.Count(p => p.Palette is not null) >= 10, "the skin, hair and garments are palette-shifted parts");
    }

    [RequiresRecordingAndDatsFact]
    public void A_recorded_clothed_human_is_dressed_part_by_part_the_reference_for_the_stand_in()
    {
        var dats = fixture.Dats;
        var seen = 0;
        foreach (var path in RealRecordings.Find())
        {
            var avatars = new AvatarSet(AvatarSources.FromDats(dats, new TextureLoader(dats), new GfxObjMeshCache(dats)));
            var replayer = new SessionReplayer();
            using var adapter = new SessionAvatarAdapter(replayer.Events, replayer.State, avatars, () => replayer.Clock.Now, immediate: true);
            using var watch = replayer.Events.Subscribe((in SelfTravelChanged e) =>
            {
                if (e.Travel != SelfTravel.Present)
                {
                    return;
                }

                foreach (var obj in replayer.State.Objects.All)
                {
                    if (obj.Physics.SetupId != StandInVisit.HumanSetup || obj.Model.AnimPartChanges.IsEmpty)
                    {
                        continue;
                    }

                    Assert.True(avatars.TryGet(obj.Guid, out var avatar), obj.Name);
                    var parts = avatar.Body.Appearance.Parts;
                    foreach (var (index, gfxObj) in LastPartChanges(obj.Model))
                    {
                        Assert.Equal(gfxObj, parts[index].Chain.RootGfxObjId);
                    }

                    Assert.Contains(parts, p => p.SurfaceTextureBySurfaceId.Count > 0);
                    Assert.Contains(parts, p => p.Palette is not null);
                    seen++;
                    output.WriteLine($"{obj.Name}: {obj.Model.AnimPartChanges.Length} part changes, {obj.Model.TextureChanges.Length} texture changes, {obj.Model.SubPalettes.Length} sub-palettes, {parts.Count(p => p.Palette is not null)} parts with a palette");
                }
            });
            replayer.Replay(new MemoryStream(File.ReadAllBytes(path)));
        }

        Assert.True(seen > 0, "no clothed human in any recording");
    }
}
