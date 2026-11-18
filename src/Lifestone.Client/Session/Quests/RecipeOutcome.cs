using System.Globalization;
using System.Text.RegularExpressions;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Quests;

/// <summary>What one recipe use came to (<see cref="RecipeOutcomes.Read"/>).</summary>
public enum RecipeVerdict
{
    /// <summary>The evidence does not settle it yet.</summary>
    Pending = 0,

    /// <summary>The expected result arrived.</summary>
    Success = 1,

    /// <summary>The roll was lost: the fail result or the fail line arrived.</summary>
    FailedRoll = 2,

    /// <summary>The server refused the use, or answered nothing a recipe would; <see cref="RecipeOutcome.Name"/> says which.</summary>
    Refused = 3,
}

/// <summary>One held object as a recipe's before and after see it.</summary>
/// <param name="Guid">The object.</param>
/// <param name="Wcid">Its weenie class.</param>
/// <param name="Name">Its name.</param>
/// <param name="Stack">Its stack size (1 for an object that does not stack).</param>
public sealed record RecipeHeldItem(uint Guid, uint Wcid, string Name, int Stack);

/// <summary>What left the pack or arrived in it over one use.</summary>
/// <param name="Guid">The object.</param>
/// <param name="Wcid">Its weenie class.</param>
/// <param name="Name">Its name.</param>
/// <param name="Amount">How many of its stack (the whole object when it is gone or new).</param>
public sealed record RecipeChange(uint Guid, uint Wcid, string Name, int Amount)
{
    /// <summary><c>2 Pyreal Bar (6329)</c>.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Amount} {Name} ({Wcid})");
}

/// <summary>What a recipe step's file says the use should show.</summary>
/// <param name="ResultWcid">The result's weenie class (<see cref="QuestExpect.Item"/>).</param>
/// <param name="SuccessLine">The recipe row's whole success line (<see cref="QuestExpect.Reply"/>).</param>
/// <param name="FailWcid">The recipe row's fail result, or null (<see cref="QuestStep.FailItem"/>). It may be the result's own wcid (recipes 6027 and 6068 create the same item on both branches).</param>
/// <param name="FailLine">The recipe row's whole fail line, or null (<see cref="QuestStep.FailReply"/>). It may be the success line's own text (recipe 6068).</param>
public sealed record RecipeExpectation(uint ResultWcid, string SuccessLine, uint? FailWcid, string? FailLine);

/// <summary>What the client saw of one use.</summary>
/// <param name="Before">Every held object before the use.</param>
/// <param name="After">Every held object now.</param>
/// <param name="Lines">The chat lines since the use was sent.</param>
/// <param name="ServerCode">The code the use ended with (<see cref="ActionOutcome.Code"/>), or null when the server has not ended it.</param>
/// <param name="Ended">True when nothing more will come: the UseDone was seen with no dialog open, or the wait is over. A verdict from a line alone waits for it; the items and the refusals do not.</param>
public sealed record RecipeEvidence(IReadOnlyList<RecipeHeldItem> Before, IReadOnlyList<RecipeHeldItem> After, IReadOnlyList<ChatLine> Lines, uint? ServerCode, bool Ended);

/// <summary>The reader's answer for one use.</summary>
/// <param name="Verdict">What it came to.</param>
/// <param name="Name">The name the logs and the stop use: <see cref="RecipeOutcomes.Success"/>, <see cref="RecipeOutcomes.FailedRoll"/>, a refusal's, or <see cref="RecipeOutcomes.Pending"/>.</param>
/// <param name="Why">The evidence, in words.</param>
/// <param name="Consumed">What left the pack.</param>
/// <param name="Created">What arrived.</param>
/// <param name="Line">The success or fail line seen, or null.</param>
public sealed record RecipeOutcome(RecipeVerdict Verdict, string Name, string Why, IReadOnlyList<RecipeChange> Consumed, IReadOnlyList<RecipeChange> Created, string? Line)
{
    /// <summary>True when the reader has an answer.</summary>
    public bool Settled => Verdict != RecipeVerdict.Pending;

    /// <summary>What left the pack, as one log fragment (<c>nothing</c> when empty).</summary>
    public string ConsumedText => RecipeOutcomes.Text(Consumed);

    /// <summary>What arrived, as one log fragment (<c>nothing</c> when empty).</summary>
    public string CreatedText => RecipeOutcomes.Text(Created);
}

/// <summary>
/// M6 item 8: the recipe outcome reader (the <c>combine</c> and <c>apply</c> steps' one reader), pure. It reads one <c>UseWithTarget</c> from what the pack did first and the chat second, because the chat line cannot be the only signal: recipe 6068's success and fail
/// text are one text and its source is not consumed, and recipe 789's two lines differ only in "of high quality" (so a line is compared whole, never as a part).
/// <para>ACE's answers (@95ef426d <c>Managers/RecipeManager.cs</c>): a UseDone with a code for busy (:47, 0x001D), combat mode (:55, 0x043A, <c>allow_combat_mode_crafting</c> false) and the requirements (:79, 0x0437); the Craft lines of <c>same-item</c> (:61-63) and
/// <c>no-recipe</c> (:71-72) before a UseDone 0; the WeenieError of <c>untrained</c> (:187) before a UseDone 0, or a tinker's Broadcast line (:223); the requirements again as a WeenieError at the re-verify (:359); <c>CreateDestroyItems</c> (:1023-1077) destroys, creates, then sends the row's
/// line, then the UseDone (:137-143). <c>not-usable</c> is <c>Player_Use.cs:142</c>'s transient line before any recipe is looked up. The destroy lines (:1136-1140) are the row's own and read as nothing.</para>
/// </summary>
public static partial class RecipeOutcomes
{
    /// <summary>The expected result arrived.</summary>
    public const string Success = "success";

    /// <summary>The fail result or the fail line arrived.</summary>
    public const string FailedRoll = "failed-roll";

    /// <summary>Not settled yet.</summary>
    public const string Pending = "pending";

    /// <summary>ACE's "You are not trained in that trade skill" (0x043B) or a tinker's "You are not trained in ...".</summary>
    public const string Untrained = "untrained";

    /// <summary>ACE's "you must be in peace mode" (0x043A).</summary>
    public const string CombatMode = "combat-mode";

    /// <summary>ACE's "You're too busy" (0x001D), or a crafting dialog already open (0x048F).</summary>
    public const string Busy = "busy";

    /// <summary>The source was the target: "The X cannot be combined with itself."</summary>
    public const string SameItem = "same-item";

    /// <summary>No recipe joins the two: "The X cannot be used on the Y."</summary>
    public const string NoRecipe = "no-recipe";

    /// <summary>The recipe's requirements are not met (0x0437).</summary>
    public const string Requirements = "requirements";

    /// <summary>The target's type does not take the source: "Cannot use the X with the Y".</summary>
    public const string NotUsable = "not-usable";

    /// <summary>The recipe row names a result the world does not have (0x0435).</summary>
    public const string CraftError = "craft-error";

    /// <summary>The use ended, and neither the items nor the chat say a recipe ran; or it left the pack with nothing to show for it.</summary>
    public const string NoAnswer = "no-answer";

    /// <summary>A recipe's line arrived, and the result the file expects did not.</summary>
    public const string NoResult = "no-result";

    /// <summary>The confirmation type of the crafting chance dialog (ACE's <c>ConfirmationType.CraftInteraction</c>, @95ef426d <c>ACE.Entity/Enum/ConfirmationType.cs:10</c>).</summary>
    public const uint CraftDialogType = 5;

    private const uint BusyCode = 0x001D;
    private const uint CombatModeCode = 0x043A;
    private const uint UntrainedCode = 0x043B;
    private const uint RequirementsCode = 0x0437;
    private const uint CraftErrorCode = 0x0435;
    private const uint DialogInProgressCode = 0x048F;

    /// <summary>The verdict for the evidence so far.</summary>
    /// <param name="expect">What the file says.</param>
    /// <param name="seen">What the client saw.</param>
    public static RecipeOutcome Read(RecipeExpectation expect, RecipeEvidence seen)
    {
        ArgumentNullException.ThrowIfNull(expect);
        ArgumentNullException.ThrowIfNull(seen);
        var consumed = Left(seen.Before, seen.After);
        var created = Left(seen.After, seen.Before);
        var successSaid = LineOf(seen.Lines, expect.SuccessLine);
        var failSaid = expect.FailLine is { } failText ? LineOf(seen.Lines, failText) : null;
        var oneText = expect.FailLine is { } text && Whole(text, expect.SuccessLine);
        var line = failSaid ?? successSaid;
        RecipeOutcome Answer(RecipeVerdict verdict, string name, string why) => new(verdict, name, why, consumed, created, line);

        var oneResult = expect.FailWcid == expect.ResultWcid;
        if (!oneResult && expect.FailWcid is { } failWcid && created.Any(c => c.Wcid == failWcid))
        {
            return Answer(RecipeVerdict.FailedRoll, FailedRoll, string.Create(CultureInfo.InvariantCulture, $"the fail result {failWcid} arrived, not {expect.ResultWcid}"));
        }

        if (created.Any(c => c.Wcid == expect.ResultWcid))
        {
            // One result on both branches (recipes 6027 and 6068): only a fail line of its own tells them apart; one text (6068) reads as the success, the item being what the step wanted.
            return oneResult && failSaid is not null && !oneText
                ? Answer(RecipeVerdict.FailedRoll, FailedRoll, string.Create(CultureInfo.InvariantCulture, $"the result {expect.ResultWcid} arrived on the fail line (the recipe makes it on both branches)"))
                : Answer(RecipeVerdict.Success, Success, string.Create(CultureInfo.InvariantCulture, $"the result {expect.ResultWcid} arrived"));
        }

        if (Refusal(seen.ServerCode, seen.Lines) is { } refused)
        {
            return Answer(RecipeVerdict.Refused, refused.Name, refused.Why);
        }

        if (!seen.Ended)
        {
            return Answer(RecipeVerdict.Pending, Pending, "the use has not ended");
        }

        if (failSaid is not null && !oneResult)
        {
            return Answer(RecipeVerdict.FailedRoll, FailedRoll, $"the fail line \"{failSaid}\" arrived and no result did");
        }

        return line is not null
            ? Answer(RecipeVerdict.Refused, NoResult, string.Create(CultureInfo.InvariantCulture, $"the line \"{line}\" arrived but the result {expect.ResultWcid} did not"))
            : Answer(RecipeVerdict.Refused, NoAnswer, consumed.Count == 0 ? "the use ended with nothing moved and no line" : "the use ended with items gone, no result and no line");
    }

    /// <summary>The refusal the code or the lines name, or null.</summary>
    /// <param name="code">The code the use ended with, or null.</param>
    /// <param name="lines">The chat lines since the send.</param>
    public static (string Name, string Why)? Refusal(uint? code, IReadOnlyList<ChatLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (code is { } c && c != 0)
        {
            return (OfCode(c), string.Create(CultureInfo.InvariantCulture, $"the server ended the use with 0x{c:X4}"));
        }

        foreach (var line in lines)
        {
            if (line.Kind == ChatKind.WeenieError && line.ChatType != 0)
            {
                return (OfCode(line.ChatType), string.Create(CultureInfo.InvariantCulture, $"the server's error 0x{line.ChatType:X4}{(line.Text.Length == 0 ? string.Empty : ": " + line.Text)}"));
            }

            if (OfLine(line) is { } name)
            {
                return (name, $"\"{line.Text}\"");
            }
        }

        return null;
    }

    /// <summary>The refusal's name for a line the server words, or null: a system line (the Craft channel's, or a tinker's Broadcast) or a transient one.</summary>
    /// <param name="line">The line.</param>
    public static string? OfLine(in ChatLine line)
    {
        if (line.Kind is not (ChatKind.System or ChatKind.Transient))
        {
            return null;
        }

        var text = line.Text.Trim();
        return SameItemLine().IsMatch(text) ? SameItem
            : NoRecipeLine().IsMatch(text) ? NoRecipe
            : NotUsableLine().IsMatch(text) ? NotUsable
            : UntrainedLine().IsMatch(text) ? Untrained
            : null;
    }

    /// <summary>The percent the crafting dialog states (ACE's "You determine that you have a 38 percent chance to succeed.", @95ef426d <c>RecipeManager.cs:332</c>), or null.</summary>
    /// <param name="question">The dialog's text.</param>
    public static double? ChancePercent(string question)
    {
        ArgumentNullException.ThrowIfNull(question);
        var m = DialogPercent().Match(question);
        return m.Success && double.TryParse(m.Groups["p"].ValueSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) ? percent : null;
    }

    /// <summary>The changes as one log fragment.</summary>
    /// <param name="changes">The changes.</param>
    public static string Text(IReadOnlyList<RecipeChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        return changes.Count == 0 ? "nothing" : string.Join(", ", changes);
    }

    private static string OfCode(uint code) => code switch
    {
        BusyCode or DialogInProgressCode => Busy,
        CombatModeCode => CombatMode,
        UntrainedCode => Untrained,
        RequirementsCode => Requirements,
        CraftErrorCode => CraftError,
        _ => string.Create(CultureInfo.InvariantCulture, $"refused-0x{code:X4}"),
    };

    /// <summary>What <paramref name="from"/> holds and <paramref name="to"/> does not: whole objects, and the part of a stack that fell.</summary>
    private static List<RecipeChange> Left(IReadOnlyList<RecipeHeldItem> from, IReadOnlyList<RecipeHeldItem> to)
    {
        var changes = new List<RecipeChange>();
        foreach (var held in from)
        {
            var other = to.FirstOrDefault(o => o.Guid == held.Guid);
            var amount = other is null ? held.Stack : held.Stack - other.Stack;
            if (amount > 0)
            {
                changes.Add(new RecipeChange(held.Guid, held.Wcid, held.Name, amount));
            }
        }

        return changes;
    }

    private static string? LineOf(IReadOnlyList<ChatLine> lines, string wanted)
    {
        foreach (var line in lines)
        {
            if (line.Kind == ChatKind.System && Whole(line.Text, wanted))
            {
                return wanted;
            }
        }

        return null;
    }

    private static bool Whole(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.Ordinal);

    [GeneratedRegex(@"^(The .+ cannot be combined with itself\.|You can't use the .+ on itself\.)$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex SameItemLine();

    [GeneratedRegex(@"^The .+ cannot be used on the .+\.$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex NoRecipeLine();

    [GeneratedRegex(@"^Cannot use the .+ with the .+$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex NotUsableLine();

    [GeneratedRegex(@"^You are not trained in .+\.$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex UntrainedLine();

    [GeneratedRegex(@"(?<p>\d+(?:\.\d+)?) percent chance", RegexOptions.CultureInvariant)]
    private static partial Regex DialogPercent();
}
