using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// Whether Lifestone Recall can be the way home now, read in one place (M6, the recall's components; live 2026-11-18 20:29:54 UTC, the sprint's log: a level 8 climber had read the scroll five minutes before, the way home from Dryreach
/// was chosen because the spellbook held the spell, and ACE refused the cast with 0x0400, YouDontHaveAllTheComponents: her account's formula needs Comfrey and Powdered Malachite and the pack held neither; the trip's station failed by name
/// and the autopilot's recovery took the portals). The rule before read the book and, after a refusal, <see cref="JourneyFacts.LifestoneRecallUnavailable"/> (M6 7 AV): the first cast of a run was not checked. Here the check comes before the
/// act: the book, that fact, and the pack's components for the cast, judged as the retreat has judged them since M6 6 FXD2 (<see cref="LevelFloor.HeldForAccount"/> and <see cref="LevelFloor.ShortOf"/>; <c>TravelLeg.Retreat</c> takes this read too), on the foci formula
/// when the pack holds the Item Enchantment focus and on the account's full formula when it does not and the account's name is known. A pack that cannot be judged (unread or unsettled, the spell's row missing, no focus and no account name)
/// reads as castable, as before: the cast's own refusal names the component and sets the fact.
/// </summary>
public static class RecallReadiness
{
    /// <summary>What was read.</summary>
    /// <param name="Known">The spellbook holds Lifestone Recall.</param>
    /// <param name="Unavailable">ACE refused it for a component earlier this run (<see cref="JourneyFacts.LifestoneRecallUnavailable"/>).</param>
    /// <param name="ShortOf">The first component the pack is short of for one cast (<see cref="LevelFloor.ShortOf"/>'s words), or null: none, or not judged.</param>
    /// <param name="Judged">The pack's components were judged (false: unread, unsettled, no row, or no focus and no account name).</param>
    public readonly record struct Read(bool Known, bool Unavailable, string? ShortOf, bool Judged)
    {
        /// <summary>
        /// M6 redoubt (live 2026-11-18 22:29:09 UTC: with the components held, the cast in Dryreach was refused 0x049E, YouMustLinkToLifestoneToRecall: she had read the Recall and the Tie scrolls on that trip and no tie was cast yet; the
        /// recall-home station then repeated the cast and the run stopped <c>failed:travel:no-lifestone-bind</c>): ACE refused a Lifestone Recall this run because no lifestone is tied (<see cref="JourneyFacts.LifestoneTied"/> is false). The
        /// recall is no way home until a tie or an arrived recall proves the link; an unknown tie (null) is tried, as before.
        /// </summary>
        public bool Untied { get; init; }

        /// <summary>The way home may be the recall: known, not refused this run for a component or a missing tie, and no component known short.</summary>
        public bool Castable => Known && !Unavailable && !Untied && ShortOf is null;

        /// <summary>Why the recall is not the way home, in words for a log line or a plan's reason; null when <see cref="Castable"/>.</summary>
        public string? WhyNot => !Known
            ? "the spellbook lacks Lifestone Recall"
            : Untied
                ? "Lifestone Recall is in the spellbook and no lifestone is tied (ACE refused it 0x049E this run; recall-untied)"
            : Unavailable
                ? "Lifestone Recall is in the spellbook and ACE refused it for a component earlier this run (recall-unavailable:components)"
                : ShortOf is { } need
                    ? string.Create(CultureInfo.InvariantCulture, $"Lifestone Recall is in the spellbook and the pack is short for one cast: {need} (recall-short:components)")
                    : null;

        /// <summary>The read as log fields: <c>lifestone_recall=yes|no</c> and, when known, the components' word.</summary>
        public string Fields => !Known
            ? "lifestone_recall=no"
            : "lifestone_recall=yes components=" + (Unavailable ? "refused-this-run" : ShortOf is not null ? "short" : Judged ? "held" : "not-judged") + (Untied ? " tie=no" : string.Empty);
    }

    /// <summary>Reads the book, the run's fact and the pack.</summary>
    /// <param name="state">The session's state (the spellbook and the pack).</param>
    /// <param name="facts">The journey's facts (the refusal of this run, the account's name for the formula).</param>
    /// <param name="spells">The spell table, or null: the components are not judged.</param>
    /// <param name="accountName">The login name the server hashes for the formula, when the caller holds its own (a station's option); null: <see cref="JourneyFacts.FormulaAccount"/>. Hashed, never logged.</param>
    public static Read Of(GameState state, JourneyFacts facts, IReadOnlyDictionary<uint, SpellInfo>? spells, string? accountName = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(facts);
        var known = state.Self.Spellbook.Knows(RecallHome.LifestoneRecallSpell);
        if (!known || spells is null || !spells.TryGetValue(RecallHome.LifestoneRecallSpell, out var info))
        {
            return new(known, facts.LifestoneRecallUnavailable, null, false) { Untied = facts.LifestoneTied == false };
        }

        var account = string.IsNullOrEmpty(accountName) ? facts.FormulaAccount : accountName;
        if (LevelFloor.HeldForAccount(state, info.School, account) is not { } held)
        {
            return new(true, facts.LifestoneRecallUnavailable, null, false) { Untied = facts.LifestoneTied == false };
        }

        return new(true, facts.LifestoneRecallUnavailable, LevelFloor.ShortOf(held, info, out var need, account) ? need : null, true) { Untied = facts.LifestoneTied == false };
    }
}
