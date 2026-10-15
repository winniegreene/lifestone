using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Core;

/// <summary>
/// The media machine (MediaMachine::Reset/Update and the Update_* handlers) with a fixed clock and fixed dice: what
/// finishes at once, what holds, and what rolls.
/// </summary>
public sealed class UiMediaMachineTests
{
    private const uint LayoutId = 0x21000001;

    [Fact]
    public void Images_alpha_masks_cursors_and_sounds_act_at_once_and_the_machine_goes_idle()
    {
        var sound = new RecordingSoundSink();
        var (manager, element) = Create(
            [
                new UiImageMedia(0x06000010, UiDrawMode.Overlay),
                new UiAlphaMedia(0x06000011),
                new UiCursorMedia(0x06000012, 3, 4),
                new UiSoundMedia(0x20000001, 0x5),
                new UiSoundMedia(0x0A000007, 0),
            ],
            sound: sound);

        Assert.Equal(new UiImageRef(0x06000010, UiBlitMode.ThreeChannelAlpha), element.Image);
        Assert.Equal(0x06000011u, element.AlphaImageDid);
        Assert.Equal(0x06000012u, element.CursorDid);
        Assert.Equal((3, 4), element.CursorHotspot);
        Assert.Equal(["table 0x20000001 type 0x5", "wave 0x0A000007 volume 1"], sound.Played);
        Assert.False(element.Media.IsBlocked);
        Assert.Equal(5, element.Media.CurrentIndex);
        Assert.True(manager.CursorVersion > 0);
    }

    [Fact]
    public void An_image_entry_with_DID_0_clears_the_image()
    {
        var (_, element) = Create([new UiImageMedia(0x06000010, UiDrawMode.Normal), new UiImageMedia(0, UiDrawMode.Normal)]);

        Assert.False(element.Image.HasImage);
    }

    [Fact]
    public void A_pause_rolls_its_length_once_and_holds_until_it_ends()
    {
        var random = new QueuedRandom(1.5);
        var (manager, element) = Create([new UiPauseMedia(1f, 2f), new UiImageMedia(0x06000020, UiDrawMode.Normal)], random: random);

        Assert.Equal([(1.0, 2.0)], random.Calls);
        Assert.True(element.Media.IsBlocked);
        Assert.False(element.Image.HasImage);

        manager.Update(1.4);
        Assert.False(element.Image.HasImage);

        manager.Update(1.5);
        Assert.Equal(0x06000020u, element.Image.Did);
        Assert.False(element.Media.IsBlocked);
        Assert.Single(random.Calls);
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.24, 1)]
    [InlineData(0.375, 2)]
    [InlineData(0.62, 2)]
    [InlineData(0.63, 3)]
    [InlineData(0.99, 4)]
    public void An_animation_shows_frame_round_n_minus_1_times_t(double now, int frame)
    {
        uint[] frames = [0x06000100, 0x06000101, 0x06000102, 0x06000103, 0x06000104];
        var (manager, element) = Create([new UiAnimationMedia(1f, UiDrawMode.Normal, frames)]);

        manager.Update(now);

        Assert.Equal(frames[frame], element.Image.Did);
        Assert.True(element.Media.IsBlocked);
    }

    [Fact]
    public void An_animation_finishes_on_its_last_frame_at_its_duration()
    {
        uint[] frames = [0x06000100, 0x06000101, 0x06000102];
        var (manager, element) = Create([new UiAnimationMedia(2f, UiDrawMode.AlphaBlend, frames), new UiImageMedia(0x06000200, UiDrawMode.Normal)]);

        manager.Update(1.99);
        Assert.Equal(0x06000102u, element.Image.Did);
        Assert.True(element.Media.IsBlocked);

        manager.Update(2.5);
        Assert.Equal(0x06000200u, element.Image.Did);
        Assert.False(element.Media.IsBlocked);
    }

    [Fact]
    public void A_fade_drives_the_surface_alpha_and_finishes_at_its_end()
    {
        var (manager, element) = Create([new UiFadeMedia(0f, 1f, 2f)]);

        // The machine starts during UIElement::Initialize, before the root has its surface, so the first step finds no
        // object to fade, as in retail.
        Assert.Equal(1f, element.SurfaceAlpha);

        manager.Update(1.0);
        Assert.Equal(0.5f, element.SurfaceAlpha);

        manager.Update(3.0);
        Assert.Equal(1f, element.SurfaceAlpha);
        Assert.False(element.Media.IsBlocked);
    }

    [Fact]
    public void A_jump_goes_back_when_the_dice_allow_it()
    {
        // [image, pause 1 s, jump to 0 at p 0.5]: the first jump roll passes (0.3), the second fails (0.9).
        var random = new QueuedRandom(1.0, 0.3, 1.0, 0.9);
        var (manager, element) = Create(
            [new UiImageMedia(0x06000300, UiDrawMode.Normal), new UiPauseMedia(1f, 1f), new UiJumpMedia(0, 0.5f)],
            random: random);

        manager.Update(1.0);
        Assert.Equal(1, element.Media.CurrentIndex);
        Assert.True(element.Media.IsBlocked);

        manager.Update(2.0);
        Assert.Equal(3, element.Media.CurrentIndex);
        Assert.False(element.Media.IsBlocked);
        Assert.Equal(4, random.Calls.Count);
    }

    [Fact]
    public void A_state_change_rolls_every_frame_until_it_happens_and_the_new_states_media_restart_the_machine()
    {
        var random = new QueuedRandom(0.9, 0.9, 0.2);
        var desc = UiDescs.Element(1, UiClassIds.Field, 0, 0, 100, 100,
            media: [new UiStateMedia(5, 0.5f)],
            states: [UiDescs.State(5, media: [new UiImageMedia(0x06000400, UiDrawMode.Normal)])]);
        var (manager, element) = Create(desc, random: random);
        Assert.Equal(0u, element.State);
        Assert.True(element.Media.IsBlocked);

        manager.Update(0.1);
        Assert.Equal(0u, element.State);

        manager.Update(0.2);
        Assert.Equal(5u, element.State);
        Assert.Equal(0x06000400u, element.Image.Did);
        Assert.False(element.Media.IsBlocked);
    }

    [Fact]
    public void A_message_waits_for_initialization_then_is_sent_by_probability()
    {
        var random = new QueuedRandom(0.5);
        var desc = UiDescs.Element(1, UiClassIds.Field, 0, 0, 100, 100, media: [new UiMessageMedia(0x77, 0.75f)]);
        var manager = new UiElementManager(new UiServices { Descs = new FakeDescSource([UiDescs.Layout(LayoutId, 800, 600, desc)]), Random = random });
        var messages = new List<UiElementMessage>();
        manager.ElementMessage += messages.Add;

        var element = manager.CreateRootElementByDataId(LayoutId, 1)!;

        // MediaMachine::Reset runs before UIElement::Initialize marks the element initialized.
        Assert.Empty(messages);
        Assert.True(element.Media.IsBlocked);

        manager.Update(0.016);

        Assert.Equal([new UiElementMessage(element, 0x77, 0, 0)], messages);
        Assert.False(element.Media.IsBlocked);
    }

    [Fact]
    public void An_idle_machine_is_not_ticked()
    {
        var random = new QueuedRandom();
        var (manager, element) = Create([new UiImageMedia(0x06000010, UiDrawMode.Normal)], random: random);

        manager.Update(1.0);
        manager.Update(2.0);

        Assert.Empty(random.Calls);
        Assert.False(element.Media.IsBlocked);
    }

    private static (UiElementManager Manager, UiElement Element) Create(UiMedia[] media, QueuedRandom? random = null, RecordingSoundSink? sound = null) =>
        Create(UiDescs.Element(1, UiClassIds.Field, 0, 0, 100, 100, media: media), random, sound);

    private static (UiElementManager Manager, UiElement Element) Create(UiElementDesc desc, QueuedRandom? random = null, RecordingSoundSink? sound = null)
    {
        var manager = new UiElementManager(new UiServices
        {
            Descs = new FakeDescSource([UiDescs.Layout(LayoutId, 800, 600, desc)]),
            Random = random ?? new QueuedRandom(),
            Sound = (IUiSoundSink?)sound ?? NullUiSoundSink.Instance,
        });
        return (manager, manager.CreateRootElementByDataId(LayoutId, desc.ElementId)!);
    }
}
