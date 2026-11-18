using System.Globalization;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M6 7 AO (live, stretch 4: five scrolls learned twice by 15:12 UTC, and no <c>buff: bootstrap:</c> or <c>buff: pass:</c> line): on the outskirts a trip ends in the ground, the known state's Home arrow never runs, and one enchantment still up read as buffed (the audit cannot judge lines while the armour memory is unseeded, and only a pass seeds it). So a climber's run owes a whole pass, before the fight: on its first run, and whenever the book changed since the last pass. Pure.
/// M6 14 (<c>m6-caster</c>): and after a caster swap that left one of her caster auras down or under the margin (<see cref="CasterAuraCheck"/>): a re-buff pass, not a first one.
/// </summary>
public static class LearnedPassRule
{
    /// <summary>The start of the reason a caster swap gives (<see cref="Why"/>): its pass casts the due lines only.</summary>
    public const string CasterSwapPrefix = "the caster changed";

    /// <summary>Why a whole pass is due, or null when the last pass stands on this book.</summary>
    /// <param name="bookAtPass">The book's size when the last whole pass of this run ended; null when none has.</param>
    /// <param name="book">The book's size now.</param>
    /// <param name="casterOwed">M6 14: a caster swap's owed aura lines since the last pass (<see cref="CasterAuraMemory.Owed"/>), or null.</param>
    public static string? Why(int? bookAtPass, int book, string? casterOwed = null) =>
        bookAtPass is not { } before ? "the run's first pass (the bootstrap first; no pass has stood on this book)"
        : before != book ? string.Create(CultureInfo.InvariantCulture, $"the book changed from {before} to {book} spells since the last pass")
        : casterOwed is not null ? CasterSwapPrefix + " since the last pass: " + casterOwed
        : null;

    /// <summary>True when <paramref name="why"/> asks a first pass (every line cast from the start), false for one that casts the due lines (the book changed, the caster changed).</summary>
    /// <param name="why">The reason <see cref="Why"/> gave.</param>
    public static bool IsStartPass(string why) =>
        !(why ?? throw new ArgumentNullException(nameof(why))).StartsWith("the book changed", StringComparison.Ordinal) && !why.StartsWith(CasterSwapPrefix, StringComparison.Ordinal);
}
