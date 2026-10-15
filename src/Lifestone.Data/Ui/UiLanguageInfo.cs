using System.Text;
using DatReaderWriter.DBObjs;
using Lifestone.Data.Dat;

namespace Lifestone.Data.Ui;

/// <summary>
/// The language's number formatting, from LanguageInfo 0x41000000 in <c>client_local_English.dat</c> (STRINGTABLE enum 1,
/// <c>Language</c>): what <c>CLanguageInfoInterface</c> hands <c>StringInfoDataHelper::NumToString</c>.
/// </summary>
/// <param name="Base">The number base (10).</param>
/// <param name="NumDecimalDigits">The minimum digit count when <paramref name="LeadingZero"/> is set (5; unused in English).</param>
/// <param name="LeadingZero">Whether numbers are padded with leading zeros (false).</param>
/// <param name="GroupingSize">Digits per group (3).</param>
/// <param name="Numerals">The digit characters, indexed by digit value.</param>
/// <param name="DecimalSeparator">The decimal separator (".").</param>
/// <param name="GroupingSeparator">The group separator (",").</param>
/// <param name="NegativeNumberFormat">The printf format a negative number's digits go through ("-%s").</param>
public sealed record UiLanguageInfo(
    int Base,
    int NumDecimalDigits,
    bool LeadingZero,
    int GroupingSize,
    string Numerals,
    string DecimalSeparator,
    string GroupingSeparator,
    string NegativeNumberFormat)
{
    /// <summary>The LanguageInfo's DID.</summary>
    public const uint DataId = 0x41000000u;

    /// <summary>Reads LanguageInfo 0x41000000.</summary>
    /// <exception cref="InvalidDataException">The language dat does not hold it.</exception>
    public static UiLanguageInfo Load(DatSet dats)
    {
        ArgumentNullException.ThrowIfNull(dats);
        dats.RequireLocal("The retail interface's strings");
        if (!dats.TryGetLocal<LanguageInfo>(DataId, out var info))
        {
            throw new InvalidDataException($"TODO(retailui-data): LanguageInfo 0x{DataId:X8} is not in {DatSet.LocalFileName}.");
        }

        return new UiLanguageInfo(
            info.Base, info.NumDecimalDigits, info.LeadingZero, info.GroupingSize, info.Numerals.Value, info.DecimalSeperator.Value,
            info.GroupingSeperator.Value, info.NegativeNumberFormat.Value);
    }

    /// <summary>
    /// <c>StringInfoDataHelper::NumToString</c> (11.4186 0x0042F280, 11.6096 0x0042F4A0) for an integer, as
    /// <c>LInt_StringInfoData::ToString</c> (11.4186 0x0042F7E0, 11.6096 0x0042FA00) calls it: precision 0, grouping on,
    /// the language's base. Digits are produced from the lowest place up through the double arithmetic retail uses (exact
    /// below 2^53), a group separator goes before every <see cref="GroupingSize"/>th digit, base 16 is prefixed
    /// <c>0x</c>, and a negative number goes through <see cref="NegativeNumberFormat"/>.
    /// </summary>
    /// <exception cref="InvalidDataException">The base is not above 1 or has no numerals (retail returns an empty string; a loud stub here).</exception>
    public string FormatInteger(long value)
    {
        if (Base <= 1 || Numerals.Length < Base)
        {
            throw new InvalidDataException(
                $"TODO(retailui-data): LanguageInfo base {Base} with {Numerals.Length} numerals cannot format a number (NumToString returns an empty string).");
        }

        var absVal = Math.Abs((double)value);
        var limit = absVal < 1.0 ? 1.0 : absVal;
        var remaining = absVal;
        var place = 1.0;
        var digits = new StringBuilder();
        var count = 0;
        while (true)
        {
            var digit = (int)((ulong)((Math.Pow(Base, -(double)count) * remaining) + 0.5) % (ulong)Base);
            digits.Insert(0, Numerals[digit]);
            count++;
            remaining -= digit * place;
            place *= Base;
            if (place > limit && (!LeadingZero || count >= NumDecimalDigits))
            {
                break;
            }

            if (GroupingSize > 0 && count % GroupingSize == 0)
            {
                digits.Insert(0, GroupingSeparator);
            }
        }

        var text = digits.ToString();
        if (value < 0)
        {
            var at = NegativeNumberFormat.IndexOf("%s", StringComparison.Ordinal);
            return at < 0
                ? throw new InvalidDataException($"TODO(retailui-data): the negative number format '{NegativeNumberFormat}' has no %s.")
                : string.Concat(NegativeNumberFormat.AsSpan(0, at), text, NegativeNumberFormat.AsSpan(at + 2));
        }

        return Base == 16 ? "0x" + text : text;
    }
}
