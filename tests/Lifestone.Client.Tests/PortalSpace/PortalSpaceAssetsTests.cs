using System.Reflection;
using Lifestone.Client.PortalSpace;
using Lifestone.Data.Dat;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.PortalSpace;

/// <summary>
/// Step PS1: portal space finds its records through the UIASSET enum group (<c>DBObj::GetDIDByEnum(enum, 7)</c>), and
/// <see cref="UiAssetIds"/> mirrors that group entry for entry (m2-log-in.md §4.2).
/// </summary>
public sealed class PortalSpaceAssetsTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    /// <summary>The aliases of <see cref="UiAssetIds"/>: constants that name a range, not a map entry.</summary>
    private static readonly string[] Aliases = [nameof(UiAssetIds.IdleEmoteFirst), nameof(UiAssetIds.IdleEmoteLast)];

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Portal_space_resolves_the_tunnel_its_spin_and_the_ui_sound_table()
    {
        var assets = PortalSpaceAssets.Resolve(fixture.Dats.EnumMaps);

        Assert.Equal(new PortalSpaceAssets(0x02000306u, 0x030005ACu, 0x2000004Bu), assets);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void The_resolved_records_exist_in_the_portal_dat()
    {
        var assets = PortalSpaceAssets.Resolve(fixture.Dats.EnumMaps);

        Assert.True(fixture.Dats.TryGetPortal<DatReaderWriter.DBObjs.Setup>(assets.TunnelSetupId, out _));
        Assert.True(fixture.Dats.TryGetPortal<DatReaderWriter.DBObjs.Animation>(assets.TunnelAnimationId, out _));
        Assert.True(fixture.Dats.TryGetPortal<DatReaderWriter.DBObjs.SoundTable>(assets.UiSoundTableId, out _));
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Every_UIASSET_entry_has_one_constant_named_as_the_map_names_it()
    {
        var maps = fixture.Dats.EnumMaps;
        var constants = PrimaryConstants();
        var entries = maps.Entries(EnumGroup.UiAsset);

        Assert.Equal(20, entries.Count);
        Assert.Equal(entries.Keys.Order(), constants.Values.Order());
        foreach (var (name, value) in constants)
        {
            var mapName = maps.NameOf(EnumGroup.UiAsset, value);
            Assert.NotNull(mapName);
            Assert.True(
                string.Equals(Fold(name), Fold(mapName), StringComparison.Ordinal),
                $"UiAssetIds.{name} = 0x{value:X8}, which the UIASSET map names '{mapName}'.");
        }
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void The_idle_emotes_are_the_five_animations_between_first_and_last()
    {
        var maps = fixture.Dats.EnumMaps;
        var emotes = new List<uint>();
        for (var e = UiAssetIds.IdleEmoteFirst; e <= UiAssetIds.IdleEmoteLast; e++)
        {
            emotes.Add(maps.Resolve(EnumGroup.UiAsset, e));
        }

        Assert.Equal([0x030000ACu, 0x03000540u, 0x03000508u, 0x03000523u, 0x0300052Eu], emotes);
        Assert.Equal(UiAssetIds.CharGenAnimationWave, UiAssetIds.IdleEmoteFirst);
        Assert.Equal(UiAssetIds.CharGenAnimationTapFoot, UiAssetIds.IdleEmoteLast);
    }

    [Fact]
    public void Resolve_needs_the_maps()
    {
        Assert.Throws<ArgumentNullException>(() => PortalSpaceAssets.Resolve(null!));
    }

    [Fact]
    public void The_constants_are_distinct_apart_from_the_aliases()
    {
        var constants = PrimaryConstants();

        Assert.Equal(constants.Count, constants.Values.Distinct().Count());
        Assert.Equal(Enumerable.Range(0, constants.Count).Select(i => 0x10000001u + (uint)i), constants.Values.Order());
    }

    private static Dictionary<string, uint> PrimaryConstants() =>
        typeof(UiAssetIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && !Aliases.Contains(f.Name))
            .ToDictionary(f => f.Name, f => (uint)f.GetRawConstantValue()!);

    /// <summary>Folds <c>portalspace_background</c> and <c>PortalSpaceBackground</c> (or <c>UISoundTable</c> and <c>UiSoundTable</c>) together.</summary>
    private static string Fold(string name) => name.Replace("_", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
}
