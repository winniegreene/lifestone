using DatReaderWriter.Enums;
using Lifestone.Client.RetailUi.Chat;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Tests.Session.Fighter;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6's hero video (the owner, 2026-11-18): a Major-or-better cantrip of an ideal family (a basic attribute, or a skill she holds) that she loots is looked at in the examine window with the paper doll and packs up for
/// three seconds (<see cref="CantripLook"/>), and the main chat of the video is 400 tall at 1080 and stops short of her (<see cref="VideoChatPlace"/>).
/// </summary>
public sealed class CantripLookTests
{
    private const uint Armor = 0x2, Jewelry = 0x8;
    private const uint MajorFocus = 2574, MinorFocus = 2581, MajorHeavy = 2531; // the real spell ids (ACE's spell table), as CantripRoomTests
    private const uint OldRing = 0x8C0D0001, NewRing = 0x8C0D0002, Girth = 0x8C0D0003, MinorRing = 0x8C0D0004;

    private static readonly Dictionary<uint, SpellInfo> Spells = new()
    {
        [MajorFocus] = WorldActionRig.Spell(MajorFocus, SpellType.Enchantment) with { Name = "Major Focus" },
        [MinorFocus] = WorldActionRig.Spell(MinorFocus, SpellType.Enchantment) with { Name = "Minor Focus" },
        [MajorHeavy] = WorldActionRig.Spell(MajorHeavy, SpellType.Enchantment) with { Name = "Major Heavy Weapon Aptitude" },
    };

    private static string? Name(uint id) => Spells.TryGetValue(id, out var s) ? s.Name : null;

    private sealed class Surface : ICantripLookSurface
    {
        public uint Shown { get; set; }

        public uint? Examined { get; private set; }

        public bool Filled { get; set; } = true;

        public List<string> Calls { get; } = [];

        public bool Open(uint panelId)
        {
            Calls.Add("open " + panelId);
            Shown = panelId;
            return true;
        }

        public void Close(uint panelId)
        {
            Calls.Add("close " + panelId);
            Shown = 0;
        }

        public bool Examine(uint guid)
        {
            Calls.Add($"examine 0x{guid:X8}");
            Examined = guid;
            return true;
        }

        public bool ExamineShows(uint guid) => Filled && Examined == guid;

        public void CloseExamine()
        {
            Calls.Add("close examine");
            Examined = null;
        }
    }

    private static void Hold(WorldActionRig r, params (uint Guid, uint Type, string Name)[] items)
    {
        foreach (var i in items)
        {
            var o = LootRulesTests.Item(i.Type, 500, 10, i.Guid, i.Name);
            r.H.R.Reducer.OnCreateObject(r.H.R.Context, o.Created with { Weenie = o.Created.Weenie with { Header = o.Created.Weenie.Header | WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player } }, isUpdate: false);
        }

        r.H.Describe(new PlayerDescriptionMsg { Inventory = [.. items.Select(i => new ContentDesc(i.Guid, 0))] });
    }

    [Fact]
    public void A_new_Major_attribute_piece_is_looked_at_for_three_seconds_with_the_paper_doll_up_and_the_page_given_back()
    {
        var r = new WorldActionRig();
        Hold(r, (OldRing, Jewelry, "Ring of Focus"));
        var state = r.H.State;
        state.Appraisals.Add(KeepAsideRuleTests.Answer(OldRing, null, MajorFocus), 0);
        var surface = new Surface { Shown = AutopilotPanelDirector.AttributesPanel };
        var lines = new List<(string Line, bool Warn)>();
        var look = new CantripLook(surface, (l, w) => lines.Add((l, w)));

        look.Tick(running: true, state, Name, 0.0); // the baseline: the ring she held when the run began is not a find
        Assert.False(look.Busy);

        Hold(r, (OldRing, Jewelry, "Ring of Focus"), (NewRing, Jewelry, "Gold Ring"), (Girth, Armor, "Studded Leather Girth"), (MinorRing, Jewelry, "Silver Ring"));
        state.Appraisals.Add(KeepAsideRuleTests.Answer(NewRing, null, MajorFocus), 0);
        state.Appraisals.Add(KeepAsideRuleTests.Answer(Girth, null, MajorHeavy), 0); // a skill she neither holds nor plans: no look
        state.Appraisals.Add(KeepAsideRuleTests.Answer(MinorRing, null, MinorFocus), 0); // a Minor: no look
        look.Tick(running: true, state, Name, 1.0);

        Assert.True(look.Busy);
        Assert.Equal(["open 7", "examine 0x8C0D0002"], surface.Calls);
        Assert.Contains(lines, l => l.Line.Contains("Major Focus", StringComparison.Ordinal) && l.Line.Contains("paper doll and packs open", StringComparison.Ordinal) && !l.Warn);

        look.Tick(running: true, state, Name, 1.1); // the window shows the piece: the hold begins
        look.Tick(running: true, state, Name, 4.0);
        Assert.True(look.Busy); // 2.9 s of 3
        look.Tick(running: true, state, Name, 4.2);
        Assert.False(look.Busy);
        Assert.Equal(1, look.Shown);
        Assert.Equal(["open 7", "examine 0x8C0D0002", "close examine", "close 7"], surface.Calls);

        look.Tick(running: true, state, Name, 10.0); // each piece once
        Assert.False(look.Busy);
        Assert.Equal(4, surface.Calls.Count);
    }

    [Fact]
    public void A_page_already_up_stays_and_a_window_that_never_fills_is_a_warning()
    {
        var r = new WorldActionRig();
        Hold(r);
        var state = r.H.State;
        var surface = new Surface { Shown = AutopilotPanelDirector.InventoryPanel, Filled = false };
        var lines = new List<(string Line, bool Warn)>();
        var look = new CantripLook(surface, (l, w) => lines.Add((l, w)));
        look.Tick(running: true, state, Name, 0.0);

        Hold(r, (NewRing, Jewelry, "Gold Ring"));
        state.Appraisals.Add(KeepAsideRuleTests.Answer(NewRing, null, MajorFocus), 0);
        look.Tick(running: true, state, Name, 1.0);
        look.Tick(running: true, state, Name, 1.0 + CantripLook.FillSeconds + 0.1);

        Assert.False(look.Busy);
        Assert.Equal(0, look.Shown);
        Assert.Equal(["examine 0x8C0D0002", "close examine"], surface.Calls); // the page was up already: never opened or closed by the look
        Assert.Contains(lines, l => l.Warn && l.Line.Contains("did not show", StringComparison.Ordinal));
    }

    [Fact]
    public void Held_mode_looks_at_the_pieces_she_already_holds_without_the_autopilot()
    {
        var r = new WorldActionRig();
        Hold(r, (OldRing, Jewelry, "Ring of Focus"));
        var state = r.H.State;
        state.Appraisals.Add(KeepAsideRuleTests.Answer(OldRing, null, MajorFocus), 0);
        var surface = new Surface();
        var look = new CantripLook(surface, (_, _) => { }, includeHeld: true);

        look.Tick(running: false, state, Name, 0.0);

        Assert.True(look.Busy);
        Assert.Equal(["open 7", "examine 0x8C0D0001"], surface.Calls);
    }

    [Fact]
    public void A_skill_cantrip_is_ideal_only_for_a_skill_her_experience_raises()
    {
        const uint loyalty = 35, run = 24; // ACE's Skill enum: a trained skill she does not raise
        CantripStatMod? Row(uint spell) => new CantripStatMod(spell, "row", CantripStatMod.SkillFlag, spell);

        Assert.True(CantripLook.IsIdeal(new CantripVerdict(MageTemplate.WarMagic, "Major War Magic Aptitude", CantripUse.Specialised, "w"), Row));
        Assert.True(CantripLook.IsIdeal(new CantripVerdict(MageTemplate.ManaConversion, "Major Mana Conversion Prowess", CantripUse.Trained, "m"), Row));
        Assert.False(CantripLook.IsIdeal(new CantripVerdict(loyalty, "Major Fealty", CantripUse.Trained, "l"), Row));
        Assert.False(CantripLook.IsIdeal(new CantripVerdict(run, "Major Sprint", CantripUse.Trained, "r"), Row));
        Assert.True(CantripLook.IsIdeal(new CantripVerdict(2574, "Major Focus", CantripUse.Attribute, "f")));
        Assert.False(CantripLook.IsIdeal(new CantripVerdict(1, "Planned", CantripUse.Planned, "p"), Row));
    }

    [Theory]
    [InlineData(1920, 1080, 4, 984, 410, 94, 4, 678, 751, 400)] // the live frames' chat (2026-11-18): right edge 755, short of her shoulder pads near 790
    [InlineData(800, 600, 4, 500, 410, 94, 4, 372, 282, 222)] // the test size: narrower than retail's, so it stays short of her
    [InlineData(800, 600, 4, 40, 410, 94, 4, 0, 282, 134)] // never above the window's top
    [InlineData(400, 300, 4, 200, 300, 94, 4, 183, 200, 111)] // never narrower than 200
    public void The_video_chat_is_0_37_of_the_window_tall_with_its_bottom_kept_and_stops_short_of_the_middle(int rootW, int rootH, int x, int y, int w, int h, int ex, int ey, int ew, int eh)
    {
        Assert.Equal((ex, ey, ew, eh), VideoChatPlace.Target(rootW, rootH, x, y, w, h));
    }
}
