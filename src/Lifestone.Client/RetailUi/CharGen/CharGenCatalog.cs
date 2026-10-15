using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Dat;

namespace Lifestone.Client.RetailUi.CharacterCreation;

/// <summary>
/// What character creation reads from the dats: the CharGen record, the SkillTable and the weenie classes a new character
/// is created as. Immutable and safe from any thread; one instance serves every <see cref="CharGenState"/>.
/// </summary>
/// <remarks>
/// <c>CharGenState::GetCharGenResult</c> (11.4186 0x005C4030, 11.6096 0x005C5010) names the class through the WCLASSID
/// group (12, map 0x25000015): enum 0x10000003 (<c>HUMAN</c>) for every heritage but the Olthoi, 0x10000090
/// (<c>OLTHOIPLAYER</c>) for heritage 12 and 0x10000091 (<c>OLTHOIACIDPLAYER</c>) for 13. Retail's administrator variants
/// (0x10000004, 0x10000092, 0x10000093, chosen when the account may create an administrator) are not offered: Lifestone
/// never creates administrator characters.
/// </remarks>
/// <param name="Data">The CharGen record.</param>
/// <param name="Skills">The SkillTable.</param>
/// <param name="HumanClassId">The class of WCLASSID <c>HUMAN</c>.</param>
/// <param name="OlthoiClassId">The class of WCLASSID <c>OLTHOIPLAYER</c>.</param>
/// <param name="OlthoiAcidClassId">The class of WCLASSID <c>OLTHOIACIDPLAYER</c>.</param>
public sealed record CharGenCatalog(
    CharGenData Data,
    SkillTableData Skills,
    uint HumanClassId,
    uint OlthoiClassId,
    uint OlthoiAcidClassId)
{
    /// <summary>The WCLASSID enum group.</summary>
    public static readonly EnumGroup WeenieClassGroup = new(12);

    /// <summary>WCLASSID <c>HUMAN</c>.</summary>
    public const uint HumanEnum = 0x10000003;

    /// <summary>WCLASSID <c>OLTHOIPLAYER</c>.</summary>
    public const uint OlthoiEnum = 0x10000090;

    /// <summary>WCLASSID <c>OLTHOIACIDPLAYER</c>.</summary>
    public const uint OlthoiAcidEnum = 0x10000091;

    /// <summary>The Olthoi heritage (60 attribute credits, one template).</summary>
    public const uint OlthoiHeritage = 12;

    /// <summary>The acid Olthoi heritage.</summary>
    public const uint OlthoiAcidHeritage = 13;

    /// <summary>Reads everything from <paramref name="dats"/> (portal only).</summary>
    /// <exception cref="InvalidDataException">A record or a WCLASSID enum is missing.</exception>
    public static CharGenCatalog Load(DatSet dats)
    {
        ArgumentNullException.ThrowIfNull(dats);
        uint Class(uint e) => dats.EnumMaps.Resolve(WeenieClassGroup, e);
        return new CharGenCatalog(
            CharGenData.Load(dats),
            SkillTableData.Load(dats),
            Class(HumanEnum),
            Class(OlthoiEnum),
            Class(OlthoiAcidEnum));
    }

    /// <summary>The class a new (non-administrator) character of <paramref name="heritage"/> is created as.</summary>
    public uint ClassFor(uint heritage) => heritage switch
    {
        OlthoiHeritage => OlthoiClassId,
        OlthoiAcidHeritage => OlthoiAcidClassId,
        _ => HumanClassId,
    };
}
