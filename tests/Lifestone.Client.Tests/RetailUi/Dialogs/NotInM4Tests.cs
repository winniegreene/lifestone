using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Text;

namespace Lifestone.Client.Tests.RetailUi.Dialogs;

/// <summary>NotInM4 (M4 2.2 s2): the answer for a retail feature a later item of M4 builds (it was first used for the examine panel, which item 3.2 built; an item list with no examine owner still falls back to it); M3's and M2's keep their own.</summary>
public sealed class NotInM4Tests
{
    [Fact]
    public void Not_in_M4_opens_a_modal_message_dialog_that_names_the_item_that_builds_the_feature_and_logs_the_placeholder()
    {
        var scene = DialogFactoryTests.Scene();

        var context = NotInM4.Show(scene.Manager.Dialogs, scene.Log, "The examine panel", "3.2");

        var dialog = Assert.IsType<UiMessageDialog>(scene.Manager.Dialogs.Find(context));
        Assert.Equal("The examine panel is built in M4 3.2", Assert.IsType<UiTextElement>(dialog.GetChildRecursive(UiDialog.TextElement)).Text);
        Assert.True(dialog.BlocksClicks);
        Assert.Contains(scene.Log.Lines, l => l.Contains("placeholder", StringComparison.Ordinal) && l.Contains("M4 3.2", StringComparison.Ordinal));
    }

    [Fact]
    public void The_text_is_the_feature_then_is_built_in_M4_then_the_item()
    {
        Assert.Equal("The examine panel is built in M4 3.2", NotInM4.TextFor(UiItemListNames.Feature, UiItemListNames.Item));
        Assert.Throws<ArgumentException>(() => NotInM4.TextFor(" ", "3.2"));
        Assert.Throws<ArgumentException>(() => NotInM4.TextFor("The examine panel", string.Empty));
    }

    private static class UiItemListNames
    {
        public const string Feature = Lifestone.Client.RetailUi.Widgets.UiItemList.ExaminePanelFeature;
        public const string Item = Lifestone.Client.RetailUi.Widgets.UiItemList.ExaminePanelItem;
    }
}
