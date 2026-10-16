using System.Text.RegularExpressions;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Indicators;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;

namespace Lifestone.Game.Sessions;

// Slice a3 (Effects pages 4 and 5, vitae page 15, indicators), docs/milestones/m3-plan.md section 4.6 row "a3". This file is the slice's alone (M3 2.2 s0's seam): write the play-check asserts here and edit nothing else of PlayChecks.
// Open the page with OpenPanelAsync(shell, panelId), assert with Check(name, ok, detail), close it with ClosePanelAsync(shell, panelId); the page's frame is taken afterwards (PlayChecks.Panels.cs).
public sealed partial class PlayChecks
{
    private const uint BeneficialEffectsPage = 4;
    private const uint HarmfulEffectsPage = 5;
    private const uint VitaeEffectsPage = 15;

    // The text of a remaining time: M:SS or H:MM:SS, or nothing for a permanent enchantment (EffectInfoRegion::Update).
    private static readonly Regex EffectTime = new(@"^(\d+:\d{2}(:\d{2})?)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The effects pages, the vitae page and the indicators on the stand-in's description (M3 2.2 a3): its registry (the live-shaped one, or the recorded one) split by the SpellTable into the beneficial page (4) and the
    /// harmful page (5), each line a spell of the registry in effect (<see cref="EffectRules.InEffect"/>) with its name and a remaining time in retail's format, in name order; the effects indicator lit when the
    /// registry holds a beneficial spell, and a click on it opens page 4 (and the harmful indicator page 5); the vitae page states the penalty percentage and the experience owed the model gives when the registry holds a vitae, which the stand-in's description does since M3 2.2 fix B (and the vitae indicator is lit); the
    /// burden indicator shows the state of the load. Each page is closed again. Nothing is clicked on a ghosted indicator.
    /// </summary>
    private async partial Task PanelA3ChecksAsync(GamePlayScreen shell)
    {
        const string beneficialName = "effects-page-4-lists-the-registry-s-beneficial-spells-in-name-order-with-times";
        const string harmfulName = "effects-page-5-lists-the-registry-s-harmful-spells-in-name-order-with-times";
        const string indicatorName = "the-effects-indicator-is-lit-by-the-registry-and-its-click-opens-page-4";
        const string vitaeName = "vitae-page-states-the-penalty-percentage-and-the-experience-owed-and-the-vitae-indicator-is-lit";
        const string burdenName = "the-burden-indicator-shows-the-load-state-the-read-model-gives";
        var names = new[] { beneficialName, harmfulName, indicatorName, vitaeName, burdenName };
        var beneficialPage = shell.PanelControllers?.ControllersOf(BeneficialEffectsPage).OfType<GameEffectsPage>().FirstOrDefault();
        var harmfulPage = shell.PanelControllers?.ControllersOf(HarmfulEffectsPage).OfType<GameEffectsPage>().FirstOrDefault();
        var vitaePage = shell.PanelControllers?.ControllersOf(VitaeEffectsPage).OfType<GameVitaePage>().FirstOrDefault();
        if (_host.Session is not { } session || beneficialPage is null || harmfulPage is null || vitaePage is null || shell.IndicatorsUi is not { } indicators || _ui?.Manager is not { } manager)
        {
            foreach (var name in names)
            {
                Check(name, false, "no session, effects or vitae controller, indicators or UI manager");
            }

            return;
        }

        // The SpellTable decodes on a worker from the start of the run; the pages list nothing for a spell it does not hold yet.
        var loading = session.SpellTableLoaded;
        var tableReady = await Task.WhenAny(loading, Task.Delay(TimeSpan.FromSeconds(30))) == loading && session.SpellTableReady;
        var q = session.State.Self.Qualities;
        var registry = q.Enchantments.Length;

        // --- the two effects pages ---
        foreach (var (name, panel, page, beneficial) in new[] { (beneficialName, BeneficialEffectsPage, beneficialPage, true), (harmfulName, HarmfulEffectsPage, harmfulPage, false) })
        {
            var opened = await OpenPanelAsync(shell, panel);
            await Frames(2);
            var inEffect = new List<ActiveEnchantment>();
            EffectRules.InEffect(q, inEffect);
            var expected = new List<(uint Id, string Name)>();
            foreach (var entry in inEffect)
            {
                if (session.Spell(entry.SpellId) is { } spell && ((spell.Flags & Lifestone.Data.Magic.SpellFlags.Beneficial) != 0) == beneficial)
                {
                    expected.Add((entry.SpellId, spell.Name));
                }
            }

            var lines = page.Lines.ToList();
            var expectedNames = expected.Select(static e => e.Name).Order(StringComparer.Ordinal).ToList();
            var timesOk = lines.All(static l => EffectTime.IsMatch(l.Time));
            var timed = lines.Count(static l => l.Time.Length > 0);
            var ok = opened && tableReady && registry > 0 && lines.Count == expected.Count && lines.Select(static l => l.Name).SequenceEqual(expectedNames)
                && lines.Select(static l => l.SpellId).Order().SequenceEqual(expected.Select(static e => e.Id).Order()) && timesOk && lines.All(l => page.ItemOf(l.SpellId)?.Visible == true);
            if (beneficial)
            {
                ok = ok && lines.Count > 0 && timed > 0; // the registry of the stand-in has beneficial spells with durations; a page that lists none would be an empty pass
            }
            else
            {
                // M3 2.2 fix B (2.2-R6): the stand-in's registry holds one harmful enchantment (StandInPlayerStats.ProbeEnchantments), so the page must list it: lines > 0, and the line is that spell, not only a line the page's own rule produced.
                ok = ok && lines.Count > 0 && lines.Any(static l => l.SpellId == StandInPlayerStats.ProbeHarmfulSpell);
            }

            Check(
                name,
                ok,
                $"opened={opened} table_ready={tableReady} registry={registry} in_effect={inEffect.Count} lines={lines.Count}/{expected.Count} timed={timed} times_ok={timesOk} first='{lines.FirstOrDefault().Name} {lines.FirstOrDefault().Time}'");
            await ClosePanelAsync(shell, panel);
        }

        // --- the effects indicator ---
        EffectRules.CountKinds(session, out var beneficialCount, out var harmfulCount, out var unresolved);
        var lit = indicators.Beneficial;
        var litHarmful = indicators.Harmful;
        var litOk = lit is not null && litHarmful is not null
            && lit.State == (beneficialCount > 0 ? UiStateIds.Normal : UiStateIds.Ghosted) && litHarmful.State == (harmfulCount > 0 ? UiStateIds.Normal : UiStateIds.Ghosted);
        var clicked = false;
        var opensPage = false;
        if (litOk && lit is not null && beneficialCount > 0)
        {
            await ClickAsync(this, manager, lit);
            clicked = true;
            opensPage = shell.PanelHost?.CurrentPanel == BeneficialEffectsPage && shell.PanelHost.ElementFor(BeneficialEffectsPage)?.Visible == true;
            await ClosePanelAsync(shell, BeneficialEffectsPage);
        }

        Check(
            indicatorName,
            litOk && beneficialCount > 0 && clicked && opensPage,
            $"beneficial={beneficialCount} harmful={harmfulCount} unresolved={unresolved} state_beneficial={lit?.State} state_harmful={litHarmful?.State} clicked={clicked} opened_page_4={opensPage}");

        // --- the vitae page and indicator ---
        // M3 2.2 fix B (2.2-R6): the stand-in's registry holds a vitae of 0.95 (StandInPlayerStats.ProbeEnchantments), so the page must state the 5 percent penalty and the experience the model says is owed, and the indicator is lit.
        var hasVitae = q.TryGetVitae(out _);
        var openedVitae = await OpenPanelAsync(shell, VitaeEffectsPage);
        await Frames(2);
        var report = vitaePage.Report;
        var percentage = EffectRules.VitaePercentage(StandInPlayerStats.ProbeVitaeValue);
        var owed = EffectRules.VitaeExperienceOwed(q, vitaePage.Vitae);
        var digits = report.Replace(",", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
        var showsPercentage = report.Contains(percentage.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%", StringComparison.Ordinal);
        var showsOwed = digits.Contains(owed.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var vitaeIndicator = indicators.Vitae;
        Check(
            vitaeName,
            openedVitae && hasVitae && percentage == 5 && vitaePage.Vitae == StandInPlayerStats.ProbeVitaeValue && showsPercentage && showsOwed && vitaeIndicator is not null && vitaeIndicator.State == UiStateIds.Normal,
            $"opened={openedVitae} has_vitae={hasVitae} multiplier={vitaePage.Vitae} percentage={percentage} shows_percentage={showsPercentage} owed={owed} shows_owed={showsOwed} text_chars={report.Length} indicator_state={vitaeIndicator?.State}");
        await ClosePanelAsync(shell, VitaeEffectsPage);

        // --- the burden indicator (report of the state against the load the read model gives) ---
        var load = GameCharacterInfo.LoadOf(q, out var capacity, out var burden);
        var burdenState = indicators.Burden?.State;
        var expectedState = load < 1f ? GameIndicators.BurdenLightState : load >= 2f ? GameIndicators.BurdenOverState : GameIndicators.BurdenHeavyState;
        Check(burdenName, burdenState == expectedState, $"burden={burden} capacity={capacity} load={load:0.###} state={burdenState} expected={expectedState}");
    }
}
