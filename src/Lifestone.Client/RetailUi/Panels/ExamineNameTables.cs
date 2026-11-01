using Lifestone.Client.RetailUi.Text;
using Lifestone.Data.Dat;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>
/// The creature window's names from the user's dats (M5 7, D137): the enum-to-name maps of the portal dat (<see cref="EnumMapperNames"/>, retail's <c>EnumMapper::GetString</c>, 11.6096 0x0041B060) and the language dat's string
/// tables through the panel's string source (<see cref="IUiStringSource"/>, retail's <c>StringInfo::GetString</c>, 11.6096 0x0042E980). Wiring: <c>new ExamineNameTables(new EnumMapperNames(dats), services.Strings)</c>.
/// </summary>
/// <param name="maps">The enum-to-name maps.</param>
/// <param name="strings">The string source, or null when the panel has none (a title then has no text: a named TODO in the producer).</param>
public sealed class ExamineNameTables(EnumMapperNames maps, IUiStringSource? strings) : IExamineNames
{
    private readonly EnumMapperNames _maps = maps ?? throw new ArgumentNullException(nameof(maps));

    /// <inheritdoc/>
    public string? EnumName(uint mapId, int value) => _maps.GetString(mapId, unchecked((uint)value));

    /// <inheritdoc/>
    /// <remarks>A string the table lacks renders retail's error text, as <c>CharacterTitleTable::GetCharacterTitleFromID</c> shows whatever <c>StringInfo::GetString</c> returns.</remarks>
    public string? StringText(uint tableEnum, string token)
    {
        if (strings is null || !strings.TryReference(tableEnum, token, out var reference))
        {
            return null;
        }

        strings.TryGetString(reference, out var text);
        return text;
    }
}
