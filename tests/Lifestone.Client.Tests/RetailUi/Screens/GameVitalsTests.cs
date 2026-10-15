using System.Collections.Immutable;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Data.Tests;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.RetailUi.Screens;

/// <summary>
/// The in-game shell's vitals (docs/retail-ui.md section 21.3): the meters and labels from a scripted PlayerDescription and a vital update (<c>gmVitalsUI::Update</c>), the
/// maximum's formula, and the choice between the stacked and the side-by-side window by the character's option bit 21 (<c>PlayerModule::SideBySideVitals</c>).
/// </summary>
public sealed class GameVitalsTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static PlayerDescriptionMsg Description(uint options1, uint health = 52, uint mana = 75) => new()
    {
        Options1 = options1,
        Attributes = [new AttributeDesc(2, 0, 120, 0), new AttributeDesc(6, 0, 100, 0)],
        Vitals = [new VitalDesc(1, 5, 0, 0, health), new VitalDesc(3, 0, 0, 0, 120), new VitalDesc(5, 0, 0, 0, mana)],
    };

    private (LoginRig Rig, GamePlayScreen Screen, IInboundMessageHandler Handler) EnterGame(uint? options1, bool describeAfterEntry = true)
    {
        IInboundMessageHandler? handler = null;
        var rig = new LoginRig(fixture, h =>
        {
            handler = h;
            StandInSessions.DataCheckThenCharacterSelect(h, TimeSpan.FromSeconds(2));
            StandInSessions.AnswerLocally(h, TimeSpan.FromSeconds(0.6));
        }, liveGame: true);
        rig.Run(2.5);
        rig.Flow.QueueUIMode(UiModes.GamePlay);
        rig.Frame();
        rig.Frame();
        var screen = Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);
        if (options1 is { } options && describeAfterEntry)
        {
            handler!.OnPlayerDescription(new InboundContext(0, MessageQueue.SmartBox, 0), new GameEventHeader(1, 1, 0x0013), Description(options));
            rig.Frame();
        }

        return (rig, screen, handler!);
    }

    // ------------------------------------------------------------------ no dats

    [Fact]
    public void The_option_bit_is_bit_21_and_ACEs_default_options_leave_it_clear()
    {
        var q = new PlayerQualities();
        Assert.False(q.SideBySideVitals);
        q.SetServerOptions(StandInPlayerStats.AceDefaultOptions1, 0);
        Assert.False(q.SideBySideVitals); // 0x50C4A54A: the stacked window
        q.SetServerOptions(StandInPlayerStats.AceDefaultOptions1 | 0x200000, 0);
        Assert.True(q.SideBySideVitals);
        Assert.Equal(1u << 21, PlayerQualities.SideBySideVitalsOption);
    }

    [Fact]
    public void A_vitals_maximum_is_its_start_and_ranks_plus_the_secondary_attribute_formula()
    {
        var q = new PlayerQualities();
        Assert.False(q.TryGetVitalLevels(1, out _, out _)); // not known yet
        q.IsKnown = true;
        Assert.False(q.TryGetVitalLevels(1, out _, out _)); // no such vital
        q.Attributes[2] = new AttributeDesc(2, 20, 101, 0); // Endurance 121
        q.Attributes[6] = new AttributeDesc(6, 0, 90, 0);
        q.Vitals[1] = new VitalDesc(1, 5, 3, 0, 40);
        q.Vitals[3] = new VitalDesc(3, 0, 0, 0, 60);
        q.Vitals[5] = new VitalDesc(5, 2, 0, 0, 7);

        Assert.True(q.TryGetVitalLevels(1, out var current, out var max));
        Assert.Equal((40u, 5u + 3u + 61u), (current, max)); // half of 121 rounds up to 61
        Assert.True(q.TryGetVitalLevels(3, out current, out max));
        Assert.Equal((60u, 121u), (current, max));
        Assert.True(q.TryGetVitalLevels(5, out current, out max));
        Assert.Equal((7u, 92u), (current, max));
    }

    /// <summary>
    /// The numbers a live ACE sent a fresh Aluvian test character (nine recordings, Endurance 55 and Self 55, every vital's start and ranks 0): the server's current values at login are its
    /// maximums, 28 (half of 55, rounded up), 55 and 55. The older tests above use made-up records that a wrong formula could also satisfy; this one pins the server's own numbers.
    /// </summary>
    [Fact]
    public void A_fresh_live_characters_maximums_are_the_currents_the_server_sent()
    {
        var q = new PlayerQualities { IsKnown = true };
        for (uint id = 1; id <= 6; id++)
        {
            q.Attributes[id] = new AttributeDesc(id, 0, 55, 0);
        }

        q.Vitals[1] = new VitalDesc(1, 0, 0, 0, 28);
        q.Vitals[3] = new VitalDesc(3, 0, 0, 0, 55);
        q.Vitals[5] = new VitalDesc(5, 0, 0, 0, 55);

        foreach (var (id, expected) in new[] { (1u, 28u), (3u, 55u), (5u, 55u) })
        {
            Assert.True(q.TryGetVitalLevels(id, out var current, out var max));
            Assert.Equal((expected, expected), (current, max));
        }
    }

    // ------------------------------------------------------------------ with dats

    [RequiresLocalDatsFact]
    public void The_meters_and_labels_show_current_over_maximum_after_a_description_and_follow_a_vital_update()
    {
        var (rig, screen, handler) = EnterGame(StandInPlayerStats.AceDefaultOptions1, describeAfterEntry: false);
        using var _ = rig;
        var vitals = Assert.IsType<GameVitals>(screen.Vitals);
        Assert.DoesNotContain("vitals", screen.Placeholders);
        var stacked = Assert.IsType<VitalsWindow>(vitals.Stacked);
        Assert.NotNull(stacked.HealthMeter);
        Assert.NotNull(stacked.StaminaMeter);
        Assert.NotNull(stacked.ManaMeter);
        Assert.NotNull(stacked.HealthLabel);
        Assert.Equal(0, stacked.Updates); // skipped until the qualities are loaded
        Assert.Equal(0f, stacked.HealthMeter.Position);
        Assert.Null(stacked.ReadLevels()); // the login check's vitals= reads the meters and labels: nothing filled yet

        var context = new InboundContext(0, MessageQueue.SmartBox, 0);
        handler.OnPlayerDescription(context, new GameEventHeader(1, 1, 0x0013), Description(StandInPlayerStats.AceDefaultOptions1));
        rig.Frame();

        // Health: 5 ranks and half of Endurance 120 is 65; stamina 120; mana 100 (Self).
        Assert.Equal(52f / 65f, stacked.HealthMeter.Position, 4);
        Assert.Equal(1f, stacked.StaminaMeter.Position, 4);
        Assert.Equal(0.75f, stacked.ManaMeter.Position, 4);
        Assert.Contains("52", stacked.HealthLabel.Text, StringComparison.Ordinal);
        Assert.Contains("65", stacked.HealthLabel.Text, StringComparison.Ordinal);
        Assert.Contains("120", stacked.StaminaLabel!.Text, StringComparison.Ordinal);
        Assert.Contains("75", stacked.ManaLabel!.Text, StringComparison.Ordinal);
        Assert.Contains("100", stacked.ManaLabel.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("CUR", stacked.HealthLabel.Text, StringComparison.Ordinal); // the variables were filled
        Assert.True(vitals.SideBySide!.Updates > 0); // the hidden window follows too
        Assert.Equal("52/120/75", stacked.ReadLevels());
        Assert.Equal("52/120/75", vitals.SideBySide.ReadLevels());

        // A PrivateUpdateAttribute2ndLevel for the current health (id 2) moves the meter and the text.
        handler.OnPrivateUpdate(context, new PrivateUpdateMsg(PrivateUpdateKind.VitalCurrent, 2, 2, 13, default, default, default));
        rig.Frame();
        Assert.Equal(13f / 65f, stacked.HealthMeter.Position, 4);
        Assert.Contains("13", stacked.HealthLabel.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("52", stacked.HealthLabel.Text, StringComparison.Ordinal);
        Assert.Equal(1f, stacked.StaminaMeter.Position, 4);
        Assert.Equal("13/120/75", stacked.ReadLevels()); // the UI's values, as the login check's vitals= field reads them
    }

    [RequiresLocalDatsFact]
    public void The_stacked_window_shows_until_the_description_and_when_option_bit_21_is_clear()
    {
        var (rig, screen, _) = EnterGame(StandInPlayerStats.AceDefaultOptions1);
        using var _ = rig;
        var vitals = screen.Vitals!;
        Assert.True(vitals.Stacked!.Window.IsShown);
        Assert.False(vitals.SideBySide!.Window.IsShown);
        Assert.Same(vitals.Stacked, vitals.Chosen);
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("0x50C4A54A", StringComparison.Ordinal) && e.Message.Contains("stacked", StringComparison.Ordinal));
    }

    [RequiresLocalDatsFact]
    public void The_side_by_side_window_shows_instead_when_option_bit_21_is_set()
    {
        var (rig, screen, _) = EnterGame(StandInPlayerStats.AceDefaultOptions1 | 0x200000);
        using var _ = rig;
        var vitals = screen.Vitals!;
        Assert.False(vitals.Stacked!.Window.IsShown);
        Assert.True(vitals.SideBySide!.Window.IsShown);
        Assert.Same(vitals.SideBySide, vitals.Chosen);
        Assert.NotNull(vitals.SideBySide.HealthMeter);
        Assert.Equal(52f / 65f, vitals.SideBySide.HealthMeter.Position, 4);
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("side-by-side (0x100006D5)", StringComparison.Ordinal));
    }
}
