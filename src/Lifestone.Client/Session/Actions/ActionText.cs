using System.Globalization;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Actions;

/// <summary>The Action API's text helpers: summaries and details in invariant culture, never holding a secret or typed text.</summary>
internal static class ActionText
{
    /// <summary>An object id as the logs show it (<c>0x50000001</c>).</summary>
    internal static string Hex(uint guid) => "0x" + guid.ToString("X8", CultureInfo.InvariantCulture);

    /// <summary>A count in invariant culture.</summary>
    internal static string Count(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The outcome of a character error that ended an action: a <see cref="ActionOutcomeKind.ServerError"/> with its code.</summary>
    internal static ActionOutcome CharacterError(in CharacterErrorReceived e) =>
        new(ActionOutcomeKind.ServerError, e.Code, "character error " + (e.Known?.ToString() ?? Hex(e.Code)));
}
