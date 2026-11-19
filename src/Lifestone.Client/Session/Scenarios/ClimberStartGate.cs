using System.Globalization;
using System.Text.RegularExpressions;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 onscreen start (owner ruling 2026-11-19: the sprint's run in one game window): the rule that lets the level up job start in the same process after the climber's creation and her first journey. It is <c>tools\climb.ps1</c>'s rule between its steps
/// (<c>Test-ClimbMayLevelUp</c> over <c>Get-ClimbCreateVerdict</c> and <c>Get-ClimbJourneyVerdict</c>, tools/lib/Climb.psm1, M6 item 0 F5, review 0-R3) enforced in the game: the creation finished, its verdict line read <c>template=ok</c> and <c>granted=0</c>, and the
/// journey's done line read <c>ok=true</c> and its climber line read <c>cheat_free=yes granted=0 place=0 recovery_place=0</c>. It reads the very lines the run logged (key=value fields, as the script reads them), so the gate and the log never
/// disagree; a count a line did not give is a refusal, never a zero.
/// </summary>
public static class ClimberStartGate
{
    /// <summary>The log line's start (<c>climber start: gate level_up=allowed|refused ...</c>): the run's log and climb.ps1 read it.</summary>
    public const string LinePrefix = "climber start: gate ";

    private static readonly Regex FieldPattern = new(@"(?<![\w.])(?<k>[A-Za-z_]\w*)=(?<v>\S*)", RegexOptions.CultureInvariant);

    /// <summary>What the gate decided.</summary>
    /// <param name="Allowed">True when the level up job may start.</param>
    /// <param name="Why">Every reason it may not, joined by "; " (empty when allowed).</param>
    public sealed record Decision(bool Allowed, string Why)
    {
        /// <summary>The one log line: <c>climber start: gate level_up=allowed</c> with the counts it read, or <c>level_up=refused why=...</c>.</summary>
        public string Line(string? creationVerdict, string? journeyClimber) => Allowed
            ? LinePrefix + "level_up=allowed creation=[" + Strip(creationVerdict) + "] journey=[" + Strip(journeyClimber) + "]"
            : LinePrefix + "level_up=refused why=" + Why;

        private static string Strip(string? line) => line is null ? "none" : line.StartsWith("verdict ", StringComparison.Ordinal) ? line["verdict ".Length..] : line;
    }

    /// <summary>The <c>key=value</c> tokens of a line (the value runs to the next space; the last of a key wins), as <c>ConvertFrom-ClimbFields</c> reads them.</summary>
    /// <param name="line">The line, or null.</param>
    public static IReadOnlyDictionary<string, string> Fields(string? line)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (line is null)
        {
            return fields;
        }

        foreach (Match m in FieldPattern.Matches(line))
        {
            fields[m.Groups["k"].Value] = m.Groups["v"].Value;
        }

        return fields;
    }

    /// <summary>The gate over the run's own lines.</summary>
    /// <param name="createdOk">True when the creation scenario ended well (its status Succeeded).</param>
    /// <param name="creationVerdict">The creation's verdict line (<c>verdict fresh= purged= granted= template= level=</c>), or null when none was logged.</param>
    /// <param name="journeyDone">The journey's done line (<c>journey done ok= ...</c>), or null.</param>
    /// <param name="journeyClimber">The journey's climber line (<c>journey climber cheat_free= level= granted= place= recovery_place= stop=</c>), or null.</param>
    public static Decision Decide(bool createdOk, string? creationVerdict, string? journeyDone, string? journeyClimber)
    {
        var why = new List<string>();
        var created = Fields(creationVerdict);
        if (!createdOk)
        {
            why.Add("the creation did not finish ok");
        }

        if (creationVerdict is null)
        {
            why.Add("no creation verdict line");
        }
        else
        {
            if (!(created.TryGetValue("template", out var template) && template == "ok"))
            {
                why.Add("the creation read template=" + (template ?? "unread"));
            }

            if (Count(created, "granted") is not 0)
            {
                why.Add("the creation read granted=" + Show(created, "granted") + ", not 0");
            }
        }

        if (journeyDone is null)
        {
            why.Add("no 'journey done' line");
        }
        else if (!(Fields(journeyDone).TryGetValue("ok", out var ok) && ok == "true"))
        {
            why.Add("the journey done line is not ok=true");
        }

        if (journeyClimber is null)
        {
            why.Add("no 'journey climber' line");
        }
        else
        {
            var climber = Fields(journeyClimber);
            foreach (var key in (ReadOnlySpan<string>)["granted", "place", "recovery_place"])
            {
                if (Count(climber, key) is not 0)
                {
                    why.Add("the journey read " + key + "=" + Show(climber, key) + ", not 0");
                }
            }

            if (!(climber.TryGetValue("cheat_free", out var free) && free == "yes"))
            {
                why.Add("the journey read cheat_free=" + (free ?? "unread") + ", not yes");
            }
        }

        return new Decision(why.Count == 0, string.Join("; ", why));
    }

    /// <summary>An integer field, or null when it is absent or not an integer (unread, never zero).</summary>
    private static int? Count(IReadOnlyDictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var text) && int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static string Show(IReadOnlyDictionary<string, string> fields, string key) => fields.TryGetValue(key, out var text) && text.Length > 0 ? text : "unread";
}
