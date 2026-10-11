// Partly ported from ACEmulator/ACE@95ef426d Source/ACE.DatLoader/FileTypes/SpellComponentsTable.cs (AGPL-3.0)
// [with changes: only GetSpellWords is ACE code (server-side chant), done in one pass where the last component of each
// type wins, as in ACE's three passes; invariant-culture casing; an unknown component throws with the id. The
// component decoding itself is retail SpellComponentBase via DatReaderWriter].
using System.Text;
using DatReaderWriter.Enums;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using DatComponentTable = DatReaderWriter.DBObjs.SpellComponentTable;

namespace Lifestone.Data.Magic;

/// <summary>
/// One spell component (retail <c>SpellComponentBase</c>, AC/acmagic/SpellComponentBase.c).
/// </summary>
/// <param name="Id">Component id (the value used in spell formulas).</param>
/// <param name="Name">Display name, e.g. "Lead Scarab".</param>
/// <param name="Category">Component category (0 scarab, 1 herb, 2 powder, 3 potion, 4 talisman, 5 taper, …).</param>
/// <param name="IconId">RenderSurface (0x06) icon.</param>
/// <param name="Type">Component type (scarab, herb, powder, potion, talisman, taper; peas share values).</param>
/// <param name="GestureMotion">
/// Motion command played for this component during casting: a <c>MagicPowerUpNN</c> wind-up action for scarabs, a
/// <c>Magic*</c> cast substate for talismans, <see cref="MotionCommands.RetailInvalid"/> (0x80000000) for none.
/// End-of-retail numbering (<see cref="MotionCommands"/>).
/// </param>
/// <param name="GestureTime">
/// Seconds of the gesture at speed 1.0. In the dats it equals Σ (high − low) / |framerate| (ACE's
/// <c>MotionTable.GetAnimationLength</c> formula) over the human MotionTable's Magic-stance links: Ready → wind-up for
/// scarabs (Iron Scarab: 15/24 + 15/33 = 1.0795), Ready → gesture → Ready for talismans. Exceptions: Pyreal Scarab
/// (unclamped frame range), Hazel Talisman (24 vs 30 fps) and Dark Scarab (see SpellVisualTests).
/// </param>
/// <param name="Word">Spoken syllable (herbs, powders, potions; empty for others).</param>
/// <param name="Cdm">Component destruction modifier (burn chance factor).</param>
public sealed record SpellComponentInfo(
    uint Id, string Name, uint Category, uint IconId, ComponentType Type, uint GestureMotion, float GestureTime, string Word, float Cdm)
{
    /// <summary>True when the component plays a gesture (its motion is neither 0 nor the retail invalid command).</summary>
    public bool HasGesture => GestureMotion != 0 && GestureMotion != MotionCommands.RetailInvalid;
}

/// <summary>The SpellComponentTable (0x0E00000F), decoded through DatReaderWriter (no encryption involved).</summary>
public sealed class SpellComponentTableData
{
    public const uint FileId = 0x0E00000F;

    private SpellComponentTableData(IReadOnlyDictionary<uint, SpellComponentInfo> components) => Components = components;

    public IReadOnlyDictionary<uint, SpellComponentInfo> Components { get; }

    public static SpellComponentTableData Load(DatSet dats)
    {
        if (!dats.TryGetPortal<DatComponentTable>(FileId, out var table))
        {
            throw new InvalidDataException($"SpellComponentTable 0x{FileId:X8} not found.");
        }

        return FromDat(table);
    }

    public static SpellComponentTableData FromDat(DatComponentTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return new SpellComponentTableData(table.Components.ToDictionary(
            kv => kv.Key,
            kv => new SpellComponentInfo(
                kv.Key,
                kv.Value.Name.Value ?? string.Empty,
                kv.Value.Category,
                kv.Value.Icon.DataId,
                kv.Value.Type,
                kv.Value.Gesture,
                kv.Value.Time,
                kv.Value.Text.Value ?? string.Empty,
                kv.Value.CDM)));
    }

    /// <summary>
    /// The incantation spoken while casting: herb word, then the powder word followed by the lower-cased potion word,
    /// that second word capitalised (e.g. "Malar Quaril" from Hyssop + Powdered Moonstone + Realgar). This is ACE's
    /// server-side <c>SpellComponentsTable.GetSpellWords</c> (the retail client only displays what the server sends);
    /// for each type the <em>last</em> matching component wins, as in ACE.
    /// </summary>
    public string GetSpellWords(IEnumerable<uint> formula)
    {
        string herb = string.Empty, powder = string.Empty, potion = string.Empty;
        foreach (var id in formula)
        {
            if (id == 0)
            {
                continue;
            }

            if (!Components.TryGetValue(id, out var comp))
            {
                throw new KeyNotFoundException($"Spell formula component {id} is not in the SpellComponentTable.");
            }

            switch ((uint)comp.Type)
            {
                case 2: herb = comp.Word; break;   // Herb
                case 3: powder = comp.Word; break; // Powder
                case 4: potion = comp.Word; break; // Potion
            }
        }

        var second = new StringBuilder(powder + potion.ToLowerInvariant());
        if (second.Length > 0)
        {
            second[0] = char.ToUpperInvariant(second[0]);
        }

        return $"{herb} {second}".Trim();
    }
}
