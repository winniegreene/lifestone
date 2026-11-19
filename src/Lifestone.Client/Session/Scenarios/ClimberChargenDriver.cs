using System.Globalization;
using Lifestone.Client.RetailUi.CharacterCreation;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 onscreen start: the clicks that make the climber through retail's character creation (<see cref="IClimberScreens.OpenCharGenAs"/>), engine-free so the tests run it over the real screens of the dats. From the character list it presses
/// Create, then sets each page to the <see cref="ClimberChargenPlan"/> as a person would, through the input router (the pointer moves, two frames pass, the button goes down and up; a number is typed into its field), and checks the
/// character state after every act: the heritage button; each attribute's number field (the ones that go down first, so the state's balancing never moves a value already set); the skills' down arrows to the heritage's free classes,
/// then the up arrows in the plan's order; the gender button; the start town; and Next between them. It ends on the summary page with the name field focused, after a last check that the screens hold the template
/// (<see cref="ClimberChargenPlan.Mismatches"/>). A page that does not take the plan fails the operation by name; nothing is guessed or forced into the state.
/// </summary>
/// <remarks>
/// <para><b>Pace.</b> With <c>--video-pace=S</c> each page is held S seconds when it opens and again when it is set (the pointer out of the window, so no tooltip covers it), and each attribute and skill change is followed by a quarter
/// of S, so a viewer can follow the clicks; with 0 nothing is held (the tests).</para>
/// </remarks>
public static class ClimberChargenDriver
{
    /// <summary>The most one wait for the screen may take, in session-clock seconds (as the throwaway's driver).</summary>
    public const double WaitSeconds = 20.0;

    /// <summary>The most arrow clicks one skill gets in one direction (untrained, trained, specialised are three classes apart by two steps).</summary>
    public const int MaxStepsPerSkill = 2;

    /// <summary>The share of the pace each attribute or skill change is held for.</summary>
    public const double ChangeShareOfPace = 0.25;

    /// <summary>The operation: Create, the pages set to <paramref name="plan"/>, and the summary page's name field focused.</summary>
    /// <param name="manager">The retail UI's element runtime (its input router is the pointer and the keyboard).</param>
    /// <param name="current">The screen the flow shows now.</param>
    /// <param name="plan">What the pages are set to.</param>
    /// <param name="clock">The session clock, in seconds (the holds).</param>
    /// <param name="paceSeconds">The video pace (0: no hold).</param>
    /// <param name="log">Where each step is said.</param>
    public static ScreenFrameOp OpenTo(UiElementManager manager, Func<IUiScreen?> current, ClimberChargenPlan plan, Func<double> clock, double paceSeconds, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(log);
        var steps = new Steps(manager, current, plan, clock, Math.Max(0, paceSeconds), log);
        return new ScreenFrameOp(steps.Run);
    }

    private sealed class Steps(UiElementManager manager, Func<IUiScreen?> current, ClimberChargenPlan plan, Func<double> clock, double pace, Action<string> log)
    {
        private static ScreenFrameOp.Until Wait(Func<bool> condition, string what, double seconds = WaitSeconds) => new(condition, seconds, what);

        private IEnumerable<object?> Click(UiElement element)
        {
            var box = element.ScreenBox;
            var x = (box.X0 + box.X1) / 2;
            var y = (box.Y0 + box.Y1) / 2;
            manager.Input.MouseMove(x, y);
            yield return 2;
            manager.Input.MouseMove(x, y);
            manager.Input.MouseButton(UiMouseButton.Left, true);
            manager.Input.MouseButton(UiMouseButton.Left, false);
            yield return 2;
        }

        private IEnumerable<object?> Hold(double seconds)
        {
            if (seconds <= 0)
            {
                yield break;
            }

            manager.Input.MouseLeave();
            var start = clock();
            yield return Wait(() => clock() - start >= seconds, "the --video-pace hold", seconds + WaitSeconds);
        }

        private IEnumerable<object?> PageHold() => Hold(pace);

        private IEnumerable<object?> ChangeHold() => Hold(pace * ChangeShareOfPace);

        private IEnumerable<object?> Next(ScreenFrameOp op, CharGenMainScreen chargen, CharGenPageId page, string what)
        {
            if (chargen.RightButton is not { } next)
            {
                op.Abort("character creation has no Next button");
                yield break;
            }

            foreach (var step in Click(next))
            {
                yield return step;
            }

            yield return Wait(() => chargen.Progress == page, "Next to open " + what);
        }

        public IEnumerable<object?> Run(ScreenFrameOp op)
        {
            log("chargen: the plan: " + plan.Describe() + "; " + ClimberChargenPlan.ScreensOwn);
            yield return Wait(() => current() is CharacterManagementScreen { CreateButton: { State: var s } } && s == UiStateIds.Normal, "the character list with Create enabled");
            foreach (var step in Click(((CharacterManagementScreen)current()!).CreateButton!))
            {
                yield return step;
            }

            yield return Wait(() => current() is CharGenMainScreen { Progress: CharGenPageId.Heritage, Heritage: not null }, "character creation on its heritage page");
            var chargen = (CharGenMainScreen)current()!;
            var state = chargen.State;
            foreach (var step in PageHold())
            {
                yield return step;
            }

            // The heritage: its button, checked in the state.
            if (CharGenRules.HeritageEntry(plan.Heritage) is not { } heritage || chargen.Root?.GetChildRecursive(heritage.ButtonId) is not { } heritageButton)
            {
                op.Abort(string.Create(CultureInfo.InvariantCulture, $"the heritage page has no button for heritage {plan.Heritage}"));
                yield break;
            }

            foreach (var step in Click(heritageButton))
            {
                yield return step;
            }

            if (state.Heritage != plan.Heritage)
            {
                op.Abort(string.Create(CultureInfo.InvariantCulture, $"the heritage button did not take: the character's heritage is {state.Heritage}, the plan's {plan.Heritage}"));
                yield break;
            }

            log(string.Create(CultureInfo.InvariantCulture, $"chargen: heritage {plan.Heritage} chosen"));
            foreach (var step in PageHold())
            {
                yield return step;
            }

            foreach (var step in Next(op, chargen, CharGenPageId.Profession, "the profession page"))
            {
                yield return step;
            }

            foreach (var step in SetAttributes(op, chargen))
            {
                yield return step;
            }

            if (op.Done)
            {
                yield break;
            }

            foreach (var step in Next(op, chargen, CharGenPageId.Skills, "the skills page"))
            {
                yield return step;
            }

            foreach (var step in SetSkills(op, chargen))
            {
                yield return step;
            }

            if (op.Done)
            {
                yield break;
            }

            foreach (var step in Next(op, chargen, CharGenPageId.Appearance, "the appearance page"))
            {
                yield return step;
            }

            foreach (var step in SetGender(op, chargen))
            {
                yield return step;
            }

            if (op.Done)
            {
                yield break;
            }

            // The appearance page zooms its camera in over a moment; Next works once the zoom is over (as the throwaway's driver waits).
            yield return Wait(() => chargen.Appearance is { IsZooming: false }, "the appearance page's zoom to end");
            foreach (var step in Next(op, chargen, CharGenPageId.Town, "the start town page"))
            {
                yield return step;
            }

            foreach (var step in SetTown(op, chargen))
            {
                yield return step;
            }

            if (op.Done)
            {
                yield break;
            }

            foreach (var step in Next(op, chargen, CharGenPageId.Summary, "the summary page"))
            {
                yield return step;
            }

            yield return Wait(() => chargen.Summary is { NameField.HasFocus: true }, "the summary page's name field to take the keyboard");
            var mismatches = plan.Mismatches(state.GetResult());
            if (mismatches.Count > 0)
            {
                op.Abort("the summary page's character is not the template: " + string.Join("; ", mismatches));
                yield break;
            }

            log(string.Create(CultureInfo.InvariantCulture, $"chargen: the summary page holds the template (heritage, gender, start area, six attributes, {plan.Skills.Count} skill classes); the screens' own: profession template {state.Template}, appearance as rolled"));
            foreach (var step in PageHold())
            {
                yield return step;
            }

            op.Succeed();
        }

        // ------------------------------------------------------------------------------------------------ the profession page

        private IEnumerable<object?> SetAttributes(ScreenFrameOp op, CharGenMainScreen chargen)
        {
            if (chargen.Profession is not { } page)
            {
                op.Abort("character creation has no profession page");
                yield break;
            }

            var state = chargen.State;
            foreach (var step in PageHold())
            {
                yield return step;
            }

            // The ones that go down first: the raises then never pass the credits, so the state's balancing (CharGenState::BalanceAttributes) never lowers a value already set.
            var lowers = ClimberChargenPlan.AttributeIds.Where(id => state.GetAttribute(id) > plan.AttributeTarget(id)).ToList();
            var raises = ClimberChargenPlan.AttributeIds.Where(id => state.GetAttribute(id) < plan.AttributeTarget(id)).ToList();
            foreach (var id in lowers.Concat(raises))
            {
                var target = plan.AttributeTarget(id);
                var before = state.GetAttribute(id);
                if (page.NumberFieldOf(id) is not { } field)
                {
                    op.Abort($"the profession page has no number field for {ClimberChargenPlan.AttributeName(id)}");
                    yield break;
                }

                foreach (var step in Click(field))
                {
                    yield return step;
                }

                if (!field.HasFocus)
                {
                    field.TakeFocus(); // the field's own click focuses it; this only covers a click the router gave elsewhere, and is checked below by the value
                }

                field.SelectAll();
                foreach (var c in target.ToString(CultureInfo.InvariantCulture))
                {
                    manager.Input.Character(c);
                }

                yield return 2;
                manager.SetFocusElement(null); // the number means its value when the field loses the focus (gmCGProfessionPage::ListenToElementMessage)
                yield return 2;
                var after = state.GetAttribute(id);
                if (after != target)
                {
                    op.Abort(string.Create(CultureInfo.InvariantCulture, $"{ClimberChargenPlan.AttributeName(id)} reads {after} after {target} was typed (it was {before}; {state.RemainingAttributeCredits} attribute credits left)"));
                    yield break;
                }

                log(string.Create(CultureInfo.InvariantCulture, $"chargen: {ClimberChargenPlan.AttributeName(id)} {before} -> {after} ({state.RemainingAttributeCredits} attribute credits left)"));
                foreach (var step in ChangeHold())
                {
                    yield return step;
                }
            }

            var wrong = ClimberChargenPlan.AttributeIds.Where(id => state.GetAttribute(id) != plan.AttributeTarget(id)).ToList();
            if (wrong.Count > 0)
            {
                op.Abort("the attributes are not the template's after the typing: " + string.Join(", ", wrong.Select(id => string.Create(CultureInfo.InvariantCulture, $"{ClimberChargenPlan.AttributeName(id)} {state.GetAttribute(id)} (the template {plan.AttributeTarget(id)})"))));
                yield break;
            }

            log(string.Create(CultureInfo.InvariantCulture, $"chargen: the attributes are the template's ({state.RemainingAttributeCredits} attribute credits left)"));
            foreach (var step in PageHold())
            {
                yield return step;
            }
        }

        // ------------------------------------------------------------------------------------------------ the skills page

        private IEnumerable<object?> SetSkills(ScreenFrameOp op, CharGenMainScreen chargen)
        {
            if (chargen.Skills is not { List: not null } page)
            {
                op.Abort("character creation has no skills page with its list");
                yield break;
            }

            var state = chargen.State;
            foreach (var step in PageHold())
            {
                yield return step;
            }

            // Down first, to the template's class: the profession the heritage click applied trained and specialised its own skills (CharGenState::ApplyTemplate), and their credits come back.
            for (uint skill = 0; skill < plan.Skills.Count; skill++)
            {
                foreach (var step in StepSkill(op, page, state, skill, plan.Skills[(int)skill], up: false))
                {
                    yield return step;
                }

                if (op.Done)
                {
                    yield break;
                }
            }

            // Then up, in the plan's order: War Magic specialised first, then the order of the buff bot's option (MageTemplate.ClimberOrder).
            foreach (var pick in plan.RaiseOrder)
            {
                foreach (var step in StepSkill(op, page, state, pick.Skill, pick.Class, up: true))
                {
                    yield return step;
                }

                if (op.Done)
                {
                    yield break;
                }
            }

            var wrong = new List<string>();
            for (uint skill = 0; skill < plan.Skills.Count; skill++)
            {
                var have = (uint)state.GetSkillLevel(skill);
                if (have != plan.Skills[(int)skill])
                {
                    wrong.Add($"{Named(state, skill)} {ClimberChargenPlan.ClassWord(have)} (the template {ClimberChargenPlan.ClassWord(plan.Skills[(int)skill])})");
                }
            }

            if (wrong.Count > 0)
            {
                op.Abort("the skills are not the template's after the arrows: " + string.Join(", ", wrong));
                yield break;
            }

            log(string.Create(CultureInfo.InvariantCulture, $"chargen: the skills are the template's ({state.RemainingSkillCredits} skill credits left)"));
            foreach (var step in PageHold())
            {
                yield return step;
            }
        }

        /// <summary>Clicks <paramref name="skill"/>'s up or down arrow until its class is <paramref name="target"/> (nothing when it is already, or lies the other way), checking the class after each click.</summary>
        private IEnumerable<object?> StepSkill(ScreenFrameOp op, CharGenSkillsPage page, CharGenState state, uint skill, uint target, bool up)
        {
            for (var steps = 0; steps <= MaxStepsPerSkill; steps++)
            {
                var have = (uint)state.GetSkillLevel(skill);
                if (up ? have >= target : have <= target)
                {
                    yield break;
                }

                if (steps == MaxStepsPerSkill)
                {
                    break;
                }

                var row = page.RowOf(skill);
                var arrow = up ? page.UpArrowOf(skill) : page.DownArrowOf(skill);
                if (row is null || arrow is null)
                {
                    op.Abort($"the skills page lists no row for {Named(state, skill)}");
                    yield break;
                }

                var index = page.List!.Items.ToList().IndexOf(row);
                page.List.ScrollToShow(index); // the row into the list's view, as the wheel would bring it
                yield return 2;
                foreach (var step in Click(arrow))
                {
                    yield return step;
                }

                var now = (uint)state.GetSkillLevel(skill);
                var wanted = up ? have + 1 : have - 1;
                if (now != wanted)
                {
                    op.Abort(string.Create(CultureInfo.InvariantCulture, $"{Named(state, skill)}'s {(up ? "up" : "down")} arrow did not take: it is {ClimberChargenPlan.ClassWord(now)}, not {ClimberChargenPlan.ClassWord(wanted)} ({state.RemainingSkillCredits} skill credits left)"));
                    yield break;
                }

                log(string.Create(CultureInfo.InvariantCulture, $"chargen: {Named(state, skill)} {(up ? "up" : "down")}: {ClimberChargenPlan.ClassWord(have)} -> {ClimberChargenPlan.ClassWord(now)} ({state.RemainingSkillCredits} skill credits left)"));
                foreach (var step in ChangeHold())
                {
                    yield return step;
                }
            }

            op.Abort(string.Create(CultureInfo.InvariantCulture, $"{Named(state, skill)} did not reach {ClimberChargenPlan.ClassWord(target)} in {MaxStepsPerSkill} clicks"));
        }

        /// <summary>The skill's name as the skills page lists it (the dats' SkillTable), else the plan's word for it.</summary>
        private static string Named(CharGenState state, uint skill)
        {
            foreach (var entry in state.SkillsByName)
            {
                if (entry.Id == skill)
                {
                    return entry.Name;
                }
            }

            return ClimberChargenPlan.SkillWord(skill);
        }

        // ------------------------------------------------------------------------------------------------ the appearance and town pages

        private IEnumerable<object?> SetGender(ScreenFrameOp op, CharGenMainScreen chargen)
        {
            var state = chargen.State;
            foreach (var step in PageHold())
            {
                yield return step;
            }

            if (state.Gender != plan.Gender)
            {
                var button = plan.Gender == 1 ? chargen.Appearance?.MaleButton : chargen.Appearance?.FemaleButton;
                if (button is null)
                {
                    op.Abort(string.Create(CultureInfo.InvariantCulture, $"the appearance page has no button for gender {plan.Gender}"));
                    yield break;
                }

                foreach (var step in Click(button))
                {
                    yield return step;
                }
            }

            if (state.Gender != plan.Gender)
            {
                op.Abort(string.Create(CultureInfo.InvariantCulture, $"the gender button did not take: the character's gender is {state.Gender}, the plan's {plan.Gender}"));
                yield break;
            }

            log(string.Create(CultureInfo.InvariantCulture, $"chargen: gender {plan.Gender}"));
            foreach (var step in PageHold())
            {
                yield return step;
            }
        }

        private IEnumerable<object?> SetTown(ScreenFrameOp op, CharGenMainScreen chargen)
        {
            var state = chargen.State;
            foreach (var step in PageHold())
            {
                yield return step;
            }

            if (state.StartArea != plan.StartArea)
            {
                if (CharGenRules.TownOfStartArea(plan.StartArea) is not { } town || chargen.Root?.GetChildRecursive(town.ButtonId) is not { } button)
                {
                    op.Abort(string.Create(CultureInfo.InvariantCulture, $"the start town page has no town for start area {plan.StartArea}"));
                    yield break;
                }

                foreach (var step in Click(button))
                {
                    yield return step;
                }
            }

            if (state.StartArea != plan.StartArea)
            {
                op.Abort(string.Create(CultureInfo.InvariantCulture, $"the town button did not take: the start area is {state.StartArea}, the plan's {plan.StartArea}"));
                yield break;
            }

            log(string.Create(CultureInfo.InvariantCulture, $"chargen: start area {plan.StartArea}"));
            foreach (var step in PageHold())
            {
                yield return step;
            }
        }
    }
}
