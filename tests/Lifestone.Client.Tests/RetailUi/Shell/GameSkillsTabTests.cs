using System.Collections.Immutable;
using System.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Options;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Tests;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.RetailUi.Shell;

/// <summary>
/// The skills tab of the skill page (<see cref="GameSkillsTab"/>, M3 2.2 a2): the list grouped as retail groups it, the base and buffed values, the credits and the experience, the raise, "10" and train
/// buttons (clicked here on the offline session, which refuses the send: never a live server), a button ghosted when the experience or the credits do not cover it, and the live redraw.
/// </summary>
public sealed class GameSkillsTabTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint Page = 11;
    private const uint SkillsTab = 0x10000229;
    private const uint MeleeDefense = 6;
    private const uint Run = 24;
    private const uint LifeMagic = 33;
    private const uint WarMagic = 34;

    // The character: War Magic specialised at 100 ranks, Melee Defense trained at 50, Run trained at 20, Life Magic untrained (its min level is 2: unusable), and a skill the SkillTable lets an untrained
    // character use (picked from the table in the test).
    private SkillDesc Specialized(uint id, ushort ranks, long extra = 0) => new(id, ranks, 1, SkillRules.SpecializedClass, (uint)(Xp.SpecializedSkillXpList[ranks] + extra), 10, 0, 0.0);

    private SkillDesc Trained(uint id, ushort ranks, long extra = 0) => new(id, ranks, 1, SkillRules.TrainedClass, (uint)(Xp.TrainedSkillXpList[ranks] + extra), 5, 0, 0.0);

    private XpTableData Xp => _xp ??= XpTableData.Load(fixture.Dats);

    private SkillTableData Skills => _skills ??= SkillTableData.Load(fixture.Dats);

    private XpTableData? _xp;
    private SkillTableData? _skills;

    private PlayerDescriptionMsg Description(int credits, long experience, ImmutableArray<SkillDesc> skills, ImmutableArray<EnchantmentDesc> enchantments = default) => new()
    {
        Options1 = StandInPlayerStats.AceDefaultOptions1,
        Options2 = PlayerOptionTable.DefaultWord2,
        IntProperties = ImmutableDictionary<uint, int>.Empty.Add(24, credits).Add(25, 30),
        Int64Properties = ImmutableDictionary<uint, long>.Empty.Add(2, experience).Add(1, 5_000_000),
        Attributes =
        [
            new AttributeDesc(1, 0, 90, 0), new AttributeDesc(2, 0, 80, 0), new AttributeDesc(3, 0, 80, 0),
            new AttributeDesc(4, 0, 90, 0), new AttributeDesc(5, 0, 70, 0), new AttributeDesc(6, 0, 100, 0),
        ],
        Vitals = [new VitalDesc(1, 5, 0, 0, 52), new VitalDesc(3, 0, 0, 0, 120), new VitalDesc(5, 0, 0, 0, 75)],
        Skills = skills,
        Enchantments = enchantments.IsDefault ? [] : enchantments,
    };

    private static InboundContext Context => new(0, MessageQueue.SmartBox, 0);

    private ImmutableArray<SkillDesc> TheCharacter() => [Specialized(WarMagic, 100), Trained(MeleeDefense, 50), Trained(Run, 20), new SkillDesc(LifeMagic, 0, 1, SkillRules.UntrainedClass, 0, 0, 0, 0.0), new SkillDesc(Untrained.Id, 0, 1, SkillRules.UntrainedClass, 0, 0, 0, 0.0)];

    /// <summary>A skill an untrained character can use at once (SkillTable <c>_min_level</c> 1) that the list shows in the untrained section.</summary>
    private SkillTableEntry Untrained => Skills.Skills.Where(static s => s.MinLevel <= 1 && s.TrainedCost > 0 && s.Id is not (MeleeDefense or Run or LifeMagic or WarMagic)).OrderBy(static s => s.Name, StringComparer.Ordinal).First();

    private sealed record Setup(LoginRig Rig, GamePlayScreen Screen, GameSkillsTab Tab, IInboundMessageHandler Handler) : IDisposable
    {
        public GamePanelHost Host => Screen.PanelHost!;

        public void Dispose() => Rig.Dispose();
    }

    private Setup Enter(int credits = 20, long experience = 1_000_000_000, ImmutableArray<SkillDesc> skills = default, bool tables = true, bool describe = true, ImmutableArray<EnchantmentDesc> enchantments = default)
    {
        IInboundMessageHandler? handler = null;
        var rig = new LoginRig(fixture, h =>
        {
            handler = h;
            StandInSessions.DataCheckThenCharacterSelect(h, TimeSpan.FromSeconds(2));
            StandInSessions.AnswerLocally(h, TimeSpan.FromSeconds(0.6));
        }, liveGame: true);
        if (tables)
        {
            rig.Session.UseStatTablesFrom(fixture.Dats);
        }

        rig.Run(2.5);
        rig.Flow.QueueUIMode(UiModes.GamePlay);
        rig.Frame();
        rig.Frame();
        var screen = Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);
        var tab = Assert.Single(screen.PanelControllers!.ControllersOf(Page).OfType<GameSkillsTab>());
        if (describe)
        {
            handler!.OnPlayerDescription(Context, new GameEventHeader(1, 1, 0x0013), Description(credits, experience, skills.IsDefault ? TheCharacter() : skills, enchantments));
            rig.Frame();
        }

        return new Setup(rig, screen, tab, handler!);
    }

    private static void OpenSkills(Setup s)
    {
        Assert.True(s.Host.Toggle(Page));
        s.Rig.Frame();
        Assert.Equal(Page, s.Host.CurrentPanel);
        var panel = Assert.IsType<UiPanel>(s.Host.ElementFor(Page));
        Assert.True(panel.OpenTab(SkillsTab));
        s.Rig.Frame();
        s.Rig.Frame();
    }

    private static void Select(Setup s, uint skill)
    {
        var list = Assert.IsType<UiListBox>(s.Tab.List);
        list.SetSelectedItem(Assert.IsAssignableFrom<UiElement>(s.Tab.RowOf(skill)), broadcast: true);
        s.Rig.Frame();
    }

    private static string Text(UiElement footer, uint id) => Assert.IsType<Lifestone.Client.RetailUi.Text.UiTextElement>(footer.GetChildRecursive(id)).Text;

    // ------------------------------------------------------------------ construction and the first show

    [RequiresLocalDatsFact]
    public void The_controller_is_built_in_a_staged_unit_and_lists_nothing_until_the_tab_first_shows()
    {
        using var s = Enter();
        Assert.Equal(Page, s.Tab.PanelId);
        Assert.False(s.Tab.IsBuilt);
        Assert.Equal(0, s.Tab.Builds);
        Assert.Empty(s.Tab.Lines); // not in the constructor, not when the page opens on the attributes tab
        Assert.True(s.Host.Toggle(Page));
        s.Rig.Frame();
        Assert.False(s.Tab.IsBuilt);

        var sw = Stopwatch.StartNew();
        var second = new GameSkillsTab(s.Rig.Screens.Context, s.Host); // the construction is the element lookups only
        sw.Stop();
        output.WriteLine($"construction {sw.Elapsed.TotalMilliseconds:F3} ms");
        Assert.True(sw.Elapsed.TotalMilliseconds < 8.0, $"construction took {sw.Elapsed.TotalMilliseconds:F2} ms");
        second.Dispose();

        sw.Restart();
        Assert.True(((UiPanel)s.Host.ElementFor(Page)!).OpenTab(SkillsTab)); // the tab's visibility message builds the list
        s.Rig.Frame();
        sw.Stop();
        output.WriteLine($"first show (the list of {s.Tab.Lines.Count()} rows built, with the frame) {sw.Elapsed.TotalMilliseconds:F3} ms");
        Assert.True(s.Tab.IsBuilt);
        Assert.Equal(1, s.Tab.Builds);
    }

    [RequiresLocalDatsFact]
    public void The_header_shows_the_name_the_level_the_total_experience_the_experience_to_the_next_level_and_the_meter()
    {
        // Review 2.2-R3: gmStatManagementUI::UpdateCharacterInfo (0x004F1400) and UpdateExperience (0x004F1700), the code the attributes tab shares (StatHeader); TODO(m3-stat-header) is closed.
        using var s = Enter(); // level 30, total experience 5,000,000 (the description's)
        OpenSkills(s);
        var tab = Assert.IsAssignableFrom<UiElement>(s.Host.ElementFor(Page)!.GetChildRecursive(0x1000022C));
        Assert.Equal(s.Rig.Session.State.Self.Name ?? string.Empty, Text(tab, 0x10000231));
        Assert.Equal("30", Text(tab, 0x1000023B));
        Assert.Contains("5,000,000", Text(tab, 0x10000235), StringComparison.Ordinal);
        var next = Xp.XpForLevel(31);
        var toNext = next > 5_000_000ul ? (long)(next - 5_000_000ul) : 0;
        var toText = Text(tab, 0x10000238);
        Assert.Contains(toNext != 0 ? toNext.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) : "Infinity", toText, StringComparison.Ordinal);
        Assert.DoesNotContain("string table error", Text(tab, 0x10000235) + toText, StringComparison.Ordinal);
        var from = Xp.XpForLevel(30);
        var expected = next > from ? Math.Clamp((5_000_000.0 - from) / (next - from), 0.0, 1.0) : 0.0;
        Assert.Equal(expected, Assert.IsType<UiMeter>(tab.GetChildRecursive(0x10000236)).Position, 3);
        Assert.DoesNotContain(s.Rig.Log.Entries, static e => e.Message.Contains("TODO(m3-stat-header)", StringComparison.Ordinal));

        // The header follows a change of the experience.
        s.Handler.OnPrivateUpdate(Context, new PrivateUpdateMsg(PrivateUpdateKind.PropertyInt64, 100, PlayerQualities.TotalExperienceProperty, 6_000_000, default, default, default));
        s.Rig.Frame();
        s.Rig.Frame();
        Assert.Contains("6,000,000", Text(tab, 0x10000235), StringComparison.Ordinal);
    }

    [RequiresLocalDatsFact]
    public void Without_the_tables_the_tab_waits_and_says_so()
    {
        using var s = Enter(tables: false);
        OpenSkills(s);
        Assert.False(s.Tab.IsBuilt);
        Assert.Contains(s.Rig.Log.Entries, static e => e.Message.Contains("TODO(m3-stat-tables)", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ the grouping and the order

    [RequiresLocalDatsFact]
    public void The_skills_are_grouped_by_advancement_class_and_sorted_by_name_inside_each_group()
    {
        using var s = Enter();
        OpenSkills(s);
        Assert.True(s.Tab.IsBuilt);
        var lines = s.Tab.Lines.ToArray();
        Assert.Equal(Skills.Skills.Count, lines.Length); // every skill of the SkillTable has a row
        Assert.Equal([WarMagic], lines.Where(static l => l.Section == 0).Select(static l => l.Skill).ToArray());
        Assert.Equal(["Melee Defense", "Run"], lines.Where(static l => l.Section == 1).Select(l => Name(l.Skill)).ToArray());
        Assert.Equal([Untrained.Name], lines.Where(static l => l.Section == 2).Select(l => Name(l.Skill)).ToArray());

        // Life Magic is untrained but its min level is 2 (it cannot be used untrained): unusable, with every skill the description lacks.
        var unusable = lines.Where(static l => l.Section == 3).Select(l => Name(l.Skill)).ToArray();
        Assert.Contains("Life Magic", unusable);
        Assert.Equal(unusable.OrderBy(static n => n, StringComparer.Ordinal).ToArray(), unusable);

        // The list box holds a header before each group, in retail's order: specialised, trained, untrained, unusable.
        var list = Assert.IsType<UiListBox>(s.Tab.List);
        Assert.Equal(lines.Length + 4, list.Items.Count);
        Assert.Equal(["Specialized Skills", "Trained Skills", "Untrained Skills", "Unusable Skills"], list.Items.OfType<Lifestone.Client.RetailUi.Text.UiTextElement>().Select(static t => t.Text).ToArray());
        Assert.IsType<Lifestone.Client.RetailUi.Text.UiTextElement>(list.Items[0]);
        Assert.Same(s.Tab.RowOf(WarMagic), list.Items[1]);
        Assert.IsType<Lifestone.Client.RetailUi.Text.UiTextElement>(list.Items[2]);
    }

    private string Name(uint skill) => Skills.TryGetSkill(skill, out var entry) ? entry.Name : $"skill {skill}";

    // ------------------------------------------------------------------ base and buffed values, live

    [RequiresLocalDatsFact]
    public void A_row_shows_the_skills_current_value_and_follows_the_buffs_and_the_ranks()
    {
        using var s = Enter();
        OpenSkills(s);
        Assert.True(Skills.TryGetSkill(WarMagic, out var warMagic));
        var q = s.Rig.Session.State.Self.Qualities;
        Assert.True(SkillRules.TryBase(q, WarMagic, warMagic.Formula, warMagic.MinLevel, out var baseValue));
        Assert.Equal(baseValue, Value(s, WarMagic));
        Output(s);

        // A +30 enchantment on War Magic (and a Focus one): the row shows the buffed number, SkillRules' number.
        const uint beneficialAdditive = EnchantmentRules.BeneficialKind | EnchantmentRules.Additive | 0x1000;
        var plus = new EnchantmentDesc(557, 1, 50, 0, 200, 0.0, 1800.0, 1, 0f, 0f, 0.0, beneficialAdditive | SkillRules.SkillKind, WarMagic, 30f, 0, EnchantmentBucket.Additive);
        var redraws = s.Tab.Redraws;
        s.Handler.OnPlayerDescription(Context, new GameEventHeader(1, 2, 0x0013), Description(20, 1_000_000_000, TheCharacter(), [plus]));
        s.Rig.Frame();
        Assert.True(s.Tab.Redraws > redraws, "the description marks the tab dirty and the next frame redraws it");
        q = s.Rig.Session.State.Self.Qualities;
        Assert.True(SkillRules.TryCurrent(q, WarMagic, warMagic.Formula, warMagic.MinLevel, out var buffed));
        Assert.Equal(baseValue + 30, buffed);
        Assert.Equal(buffed, Value(s, WarMagic));

        // With nothing changed a frame redraws nothing.
        redraws = s.Tab.Redraws;
        s.Rig.Frame();
        s.Rig.Frame();
        Assert.Equal(redraws, s.Tab.Redraws);
    }

    private static uint Value(Setup s, uint skill) => uint.Parse(s.Tab.Lines.Single(l => l.Skill == skill).Value, System.Globalization.CultureInfo.InvariantCulture);

    private void Output(Setup s)
    {
        foreach (var line in s.Tab.Lines.Take(8))
        {
            output.WriteLine($"{line.Section} {Name(line.Skill)} = {line.Value}");
        }
    }

    [RequiresLocalDatsFact]
    public void A_skill_that_changes_its_class_moves_to_its_new_group()
    {
        using var s = Enter();
        OpenSkills(s);
        Assert.Equal(1, s.Tab.Builds);
        var trainedNow = new SkillDesc(Untrained.Id, 0, 1, SkillRules.TrainedClass, 0, 5, 0, 0.0);
        s.Handler.OnPlayerDescription(Context, new GameEventHeader(1, 2, 0x0013), Description(20, 1_000_000_000, [Specialized(WarMagic, 100), Trained(MeleeDefense, 50), Trained(Run, 20), trainedNow]));
        s.Rig.Frame();
        Assert.Equal(2, s.Tab.Builds);
        Assert.Contains(Untrained.Id, s.Tab.Lines.Where(static l => l.Section == 1).Select(static l => l.Skill));
        Assert.DoesNotContain(s.Tab.Lines, static l => l.Section == 2);
    }

    // ------------------------------------------------------------------ the footers

    [RequiresLocalDatsFact]
    public void With_no_selection_the_footer_shows_the_credits_and_the_unassigned_experience()
    {
        using var s = Enter(credits: 37, experience: 1_234_567);
        OpenSkills(s);
        Assert.Equal(GameSkillsTab.DefaultState, s.Tab.FooterState);
        var footer = s.Tab.FooterElement!;
        Assert.True(footer.Visible);
        Assert.Equal("Select a Skill to Improve", Text(footer, GameSkillsTab.TitleId));
        Assert.Equal("Skill Credits Available:", Text(footer, GameSkillsTab.LineOneLabelId));
        Assert.Equal("37", Text(footer, GameSkillsTab.LineOneValueId));
        Assert.Equal("Unassigned Experience:", Text(footer, GameSkillsTab.LineTwoLabelId));
        Assert.Equal("1,234,567", Text(footer, GameSkillsTab.LineTwoValueId));
    }

    [RequiresLocalDatsFact]
    public void Selecting_a_trained_skill_shows_its_value_the_cost_of_a_rank_and_a_meter_and_the_same_row_again_clears_it()
    {
        using var s = Enter();
        OpenSkills(s);
        Select(s, MeleeDefense);
        Assert.Equal(MeleeDefense, s.Tab.SelectedSkill);
        Assert.Equal(GameSkillsTab.TrainedState, s.Tab.FooterState);
        var footer = s.Tab.FooterElement!;
        Assert.True(footer.Visible);
        Assert.True(Skills.TryGetSkill(MeleeDefense, out var melee));
        SkillRules.TryCurrent(s.Rig.Session.State.Self.Qualities, MeleeDefense, melee.Formula, melee.MinLevel, out var current);
        Assert.Equal($"Melee Defense: {current}", Text(footer, GameSkillsTab.TitleId));
        Assert.Equal("Experience To Raise:", Text(footer, GameSkillsTab.LineOneLabelId));
        var expected = Xp.TrainedSkillXpList[51] - Xp.TrainedSkillXpList[50];
        Assert.Equal(expected.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), Text(footer, GameSkillsTab.LineOneValueId));
        Assert.NotNull(s.Tab.Raise10Button);
        Assert.True(s.Tab.Raise10Button!.Visible);

        Select(s, MeleeDefense); // the same row again
        Assert.Equal(0u, s.Tab.SelectedSkill);
        Assert.Equal(GameSkillsTab.DefaultState, s.Tab.FooterState);
    }

    [RequiresLocalDatsFact]
    public void Selecting_an_untrained_skill_shows_the_credits_it_costs_and_the_credits_available()
    {
        using var s = Enter(credits: 9);
        OpenSkills(s);
        Select(s, Untrained.Id);
        Assert.Equal(GameSkillsTab.UntrainedState, s.Tab.FooterState);
        var footer = s.Tab.FooterElement!;
        Assert.Equal(Untrained.Name, Text(footer, GameSkillsTab.TitleId));
        Assert.Equal("Skill Credits To Train", Text(footer, GameSkillsTab.LineOneLabelId));
        Assert.Equal(Untrained.TrainedCost.ToString(System.Globalization.CultureInfo.InvariantCulture), Text(footer, GameSkillsTab.LineOneValueId));
        Assert.Equal("9", Text(footer, GameSkillsTab.LineTwoValueId));
    }

    // ------------------------------------------------------------------ the buttons

    private static bool Ghosted(UiElement? button) => button?.State == UiStateIds.Ghosted;

    [RequiresLocalDatsFact]
    public void The_raise_button_spends_one_ranks_experience_through_RaiseSkill()
    {
        using var s = Enter(experience: 100_000_000);
        OpenSkills(s);
        Select(s, MeleeDefense);
        Assert.False(Ghosted(s.Tab.RaiseButton));
        Assert.False(Ghosted(s.Tab.Raise10Button));

        Assert.True(s.Rig.Click(s.Tab.RaiseButton!));
        s.Rig.Frame();
        var action = Assert.IsAssignableFrom<ActionHandle>(s.Tab.LastAction);
        Assert.Equal(ActionKind.RaiseSkill, action.Kind);
        Assert.Equal(ActionSource.Ui, action.Source);
        Assert.Equal($"RaiseSkill skill={MeleeDefense} xp={Xp.TrainedSkillXpList[51] - Xp.TrainedSkillXpList[50]}", action.Summary);
        Assert.False(Ghosted(s.Tab.RaiseButton), "the offline session refuses the send at once, so the answer is in and the button is back");
    }

    [RequiresLocalDatsFact]
    public void The_10_button_spends_ten_ranks_and_a_ghosted_button_sends_nothing()
    {
        using var s = Enter(experience: 100_000_000);
        OpenSkills(s);
        Select(s, WarMagic);
        Assert.True(s.Rig.Click(s.Tab.Raise10Button!));
        s.Rig.Frame();
        var action = Assert.IsAssignableFrom<ActionHandle>(s.Tab.LastAction);
        Assert.Equal($"RaiseSkill skill={WarMagic} xp={Xp.SpecializedSkillXpList[110] - Xp.SpecializedSkillXpList[100]}", action.Summary);

        using var poor = Enter(experience: 10);
        OpenSkills(poor);
        Select(poor, MeleeDefense);
        Assert.True(Ghosted(poor.Tab.RaiseButton));
        Assert.True(Ghosted(poor.Tab.Raise10Button));
        poor.Rig.Click(poor.Tab.RaiseButton!);
        poor.Rig.Click(poor.Tab.Raise10Button!);
        poor.Rig.Frame();
        Assert.Null(poor.Tab.LastAction);
        Assert.False(poor.Tab.RaiseSelection());
        Assert.False(poor.Tab.Raise10Selection());
        Assert.Null(poor.Tab.LastAction);
    }

    [RequiresLocalDatsFact]
    public void The_train_button_asks_first_and_Yes_trains_for_the_SkillTables_cost()
    {
        using var s = Enter(credits: 40);
        OpenSkills(s);
        Select(s, Untrained.Id);
        Assert.False(Ghosted(s.Tab.RaiseButton));
        Assert.False(s.Tab.Raise10Button!.Visible); // an untrained skill has no "10" button

        Assert.True(s.Rig.Click(s.Tab.RaiseButton!));
        s.Rig.Frame();
        Assert.Null(s.Tab.LastAction); // the confirmation comes first
        var dialog = Assert.Single(s.Rig.Manager.Dialogs.OpenDialogs);
        Assert.Equal($"Are you sure you want to spend {Untrained.TrainedCost} credits to train {Untrained.Name}?", Assert.IsType<Lifestone.Client.RetailUi.Text.UiTextElement>(dialog.GetChildRecursive(UiDialog.TextElement)).Text);

        Assert.True(s.Rig.Click(dialog.GetChildRecursive(UiConfirmationDialog.YesButton)!));
        s.Rig.Frame();
        var action = Assert.IsAssignableFrom<ActionHandle>(s.Tab.LastAction);
        Assert.Equal(ActionKind.TrainSkill, action.Kind);
        Assert.Equal(ActionSource.Ui, action.Source);
        Assert.Equal($"TrainSkill skill={Untrained.Id} credits={Untrained.TrainedCost}", action.Summary);
    }

    [RequiresLocalDatsFact]
    public void No_answers_the_train_dialog_without_sending_and_too_few_credits_ghost_the_button()
    {
        using var s = Enter(credits: 40);
        OpenSkills(s);
        Select(s, Untrained.Id);
        Assert.True(s.Rig.Click(s.Tab.RaiseButton!));
        s.Rig.Frame();
        var dialog = Assert.Single(s.Rig.Manager.Dialogs.OpenDialogs);
        Assert.True(s.Rig.Click(dialog.GetChildRecursive(UiConfirmationDialog.NoButton)!));
        s.Rig.Frame();
        Assert.Null(s.Tab.LastAction);

        using var poor = Enter(credits: Untrained.TrainedCost - 1);
        OpenSkills(poor);
        Select(poor, Untrained.Id);
        Assert.True(Ghosted(poor.Tab.RaiseButton));
        Assert.False(poor.Tab.RaiseSelection());
        Assert.Empty(poor.Rig.Manager.Dialogs.OpenDialogs);
    }

    // ------------------------------------------------------------------ hidden pages cost nothing

    [RequiresLocalDatsFact]
    public void A_hidden_tab_does_not_redraw()
    {
        using var s = Enter();
        OpenSkills(s);
        Assert.True(s.Host.Toggle(Page)); // closes the page
        s.Rig.Frame();
        var redraws = s.Tab.Redraws;
        s.Handler.OnPlayerDescription(Context, new GameEventHeader(1, 2, 0x0013), Description(20, 1_000_000_000, [Specialized(WarMagic, 120), Trained(MeleeDefense, 50)]));
        s.Rig.Run(0.5);
        Assert.Equal(redraws, s.Tab.Redraws);

        Assert.True(s.Host.Toggle(Page)); // it shows again and brings itself up to date
        s.Rig.Run(0.2);
        Assert.True(s.Tab.Redraws > redraws);
    }
}
