using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Text;

namespace Lifestone.Client.Tests.RetailUi.Dialogs;

/// <summary>NotInM3 (M3 2.2 s0): the panel host's and the toolbar's answer for a feature M3 does not build yet; M2's features keep NotInM2.</summary>
public sealed class NotInM3Tests
{
    [Fact]
    public void Not_in_M3_opens_a_modal_message_dialog_with_a_lifestone_literal_and_logs_the_placeholder()
    {
        var scene = DialogFactoryTests.Scene();

        var context = NotInM3.Show(scene.Manager.Dialogs, scene.Log, "Map");

        var dialog = Assert.IsType<UiMessageDialog>(scene.Manager.Dialogs.Find(context));
        Assert.Equal("Not in Lifestone yet: Map", Assert.IsType<UiTextElement>(dialog.GetChildRecursive(UiDialog.TextElement)).Text);
        Assert.True(dialog.BlocksClicks);
        Assert.Contains(scene.Log.Lines, l => l.Contains("placeholder", StringComparison.Ordinal) && l.Contains("Map", StringComparison.Ordinal) && l.Contains("M3", StringComparison.Ordinal));
    }

    [Fact]
    public void The_prefixes_of_the_two_milestones_differ()
    {
        Assert.Equal("Not in Lifestone yet: ", NotInM3.Prefix);
        Assert.NotEqual(NotInM2.Prefix, NotInM3.Prefix);
    }
}
