using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;
using Lifestone.Data.Audio;
using Lifestone.Data.World;

namespace Lifestone.Data.Tests.World;

/// <summary>
/// M3 1.3 s2b: <see cref="RegionSoundData"/>, the projection of RegionDesc 0x13000000's ambient sound tables (retail
/// <c>CSoundDesc</c>, <c>AmbientSTBDesc</c>, <c>AmbientSoundDesc</c>) and of the terrain-to-table lookup (<c>CRegionDesc::GetSTBDesc</c>).
/// </summary>
public sealed class RegionSoundDataTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [RequiresDatsFact]
    public void The_end_of_retail_region_has_37_ambient_tables_and_32_terrain_types_and_the_first_table_is_pinned()
    {
        var data = RegionSoundData.FromRegion(fixture.Dats.Region);

        Assert.Equal(37, data.Tables.Count);
        Assert.Equal(32, data.TerrainTypeCount);

        var first = data.Tables[0];
        Assert.Equal(0x20000017u, first.SoundTableId);
        Assert.Equal(9, first.Sounds.Count);
        Assert.Equal(new AmbientSoundEntry(0x46, 0.6f, 0.5f, 1.4f, 10f), first.Sounds[0]);
        Assert.False(first.Sounds[0].IsConstant);

        // The last two of table 0 are a constant pair and an intermittent sound of the same type (the dat's own layout).
        Assert.True(first.Sounds[6].IsConstant);
        Assert.Equal(new AmbientSoundEntry(0x4D, 0.15f, 0f, 9.04f, 9.04f), first.Sounds[6]);
        Assert.False(first.Sounds[8].IsConstant);
    }

    [RequiresDatsFact]
    public void Every_playable_table_names_a_sound_table_the_dat_holds_and_every_constant_sound_has_a_repeat_interval()
    {
        var data = RegionSoundData.FromRegion(fixture.Dats.Region);
        var source = new DatAudioSource(fixture.Dats);

        foreach (var table in data.Tables)
        {
            Assert.True(table.IsPlayable);
            Assert.NotNull(source.GetSoundTable(table.SoundTableId));
            foreach (var sound in table.Sounds)
            {
                // A constant sound repeats every MinRate (ConstantSound::GetPlayInterval); an intermittent one draws between the two.
                var rated = sound.Volume > 0f && sound.MinRate >= 0f && (sound.IsConstant ? sound.MinRate > 0f : sound.MaxRate >= sound.MinRate);
                Assert.True(rated, $"table 0x{table.SoundTableId:X8} type 0x{sound.SoundType:X2}: volume {sound.Volume}, rates {sound.MinRate} to {sound.MaxRate}");
                Assert.InRange(sound.BaseChance, 0f, 1f);
            }
        }
    }

    [RequiresDatsFact]
    public void The_terrain_lookup_follows_the_scene_slot_to_its_table()
    {
        var data = RegionSoundData.FromRegion(fixture.Dats.Region);

        // Terrain type 0, slot 0: the dat's scene type 6's table 6; type 2 slot 0 is table 14; type 20 is table 4 throughout.
        Assert.Equal(32, data.SceneSlotCount(0));
        Assert.Equal(6, data.TableIndexOf(0, 0));
        Assert.Equal(14, data.TableIndexOf(2, 0));
        Assert.Equal(4, data.TableIndexOf(20, 31));
        for (var t = 0; t < data.TerrainTypeCount; t++)
        {
            for (var slot = 0; slot < data.SceneSlotCount(t); slot++)
            {
                Assert.InRange(data.TableIndexOf(t, slot), -1, data.Tables.Count - 1);
            }
        }

        // Outside the lists: none, as retail's GetSTBDesc.
        Assert.Equal(-1, data.TableIndexOf(32, 0));
        Assert.Equal(-1, data.TableIndexOf(0, data.SceneSlotCount(0)));
        Assert.Equal(-1, data.TableIndexOf(-1, 0));
        Assert.Equal(0, data.SceneSlotCount(99));
    }

    [Fact]
    public void A_region_with_no_sound_info_has_no_tables_and_a_minus_one_scene_or_table_index_is_none()
    {
        var region = new Region
        {
            SceneInfo = new SceneDesc { SceneTypes = [new SceneType { StbIndex = uint.MaxValue }, new SceneType { StbIndex = 0 }] },
            TerrainInfo = new TerrainDesc { TerrainTypes = [new TerrainType { SceneTypes = [uint.MaxValue, 0] }] },
        };

        var none = RegionSoundData.FromRegion(region);
        Assert.Empty(none.Tables);
        Assert.Equal(-1, none.TableIndexOf(0, 0));
        Assert.Equal(-1, none.TableIndexOf(0, 1));
    }

    [Fact]
    public void An_index_beyond_its_list_refuses_the_region()
    {
        var region = new Region
        {
            SoundInfo = new SoundDesc { STBDesc = [new AmbientSTBDesc { STBId = 0x20000001 }] },
            SceneInfo = new SceneDesc { SceneTypes = [new SceneType { StbIndex = 1 }] },
            TerrainInfo = new TerrainDesc { TerrainTypes = [new TerrainType { SceneTypes = [0] }] },
        };
        Assert.Throws<InvalidDataException>(() => RegionSoundData.FromRegion(region));

        region.SceneInfo = new SceneDesc { SceneTypes = [new SceneType { StbIndex = 0 }] };
        region.TerrainInfo = new TerrainDesc { TerrainTypes = [new TerrainType { SceneTypes = [5] }] };
        Assert.Throws<InvalidDataException>(() => RegionSoundData.FromRegion(region));
    }
}
