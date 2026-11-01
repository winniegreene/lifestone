using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Core;

/// <summary>The UI's target mode (M5 7 S6, D136): <c>ClientUISystem::ExamineObject</c>, <c>SetTargetMode</c>, <c>ExecuteTargetModeForItem</c>, <c>OnAction</c>'s press and escape, <c>UseTime</c>.</summary>
public sealed class UiTargetModeTests
{
    private static readonly UiCursor Pointer = new(1, "Default", 0x06001001, 0, 0);
    private static readonly UiCursor ExaminePointer = new(10, "Examine", 0x0600100A, 0, 0);

    private static (UiElementManager Manager, UiTargetMode Mode, List<uint> Examined) Rig()
    {
        var manager = new UiElementManager(new UiServices { Descs = new FakeDescSource([]) });
        manager.Cursor.SetCursor(Pointer.ImageId, 0, 0, isDefault: true);
        var examined = new List<uint>();
        var mode = manager.TargetMode;
        mode.Examiner = examined.Add;
        mode.Cursors = e => e switch { 1 => Pointer, 10 => ExaminePointer, _ => null };
        return (manager, mode, examined);
    }

    [Fact]
    public void Examining_an_object_opens_it_and_examining_nothing_enters_the_examine_mode_with_its_pointer()
    {
        var (manager, mode, examined) = Rig();
        Assert.True(mode.ExamineObject(0x80000A02));
        Assert.Equal([0x80000A02u], examined);
        Assert.False(mode.Active);

        Assert.True(mode.ExamineObject(0));
        Assert.Equal(UiTargetModeKind.Examine, mode.Mode);
        Assert.Equal(ExaminePointer.ImageId, manager.Cursor.DefaultCursorDid); // UpdateCursorState: UICURSOR 10 at (0, 0), set as the default pointer
        Assert.Equal(ExaminePointer.ImageId, manager.Cursor.CursorDid);
        Assert.Single(examined);
    }

    [Fact]
    public void A_mode_runs_on_the_object_clicked_and_ends_on_the_frame_after_the_press_with_the_pointer_back()
    {
        var (manager, mode, examined) = Rig();
        mode.Set(UiTargetModeKind.Examine);
        mode.MousePressed(); // OnAction: the press marks the mode to end; it is still on while the press is handled
        Assert.True(mode.Execute(0x80000A01));
        Assert.Equal([0x80000A01u], examined);
        Assert.Equal(UiTargetModeKind.Examine, mode.Mode);

        manager.Update(1.0); // UseTime
        Assert.False(mode.Active);
        Assert.Equal(Pointer.ImageId, manager.Cursor.DefaultCursorDid);
        Assert.Equal(1, mode.Executions);
        Assert.Equal(0x80000A01u, mode.LastTarget);
    }

    [Fact]
    public void With_no_mode_a_click_runs_nothing_and_a_press_marks_nothing()
    {
        var (manager, mode, examined) = Rig();
        Assert.False(mode.Execute(0x80000A01));
        mode.MousePressed();
        mode.Set(UiTargetModeKind.Examine);
        manager.Update(1.0); // the press came before the mode: it does not end it
        Assert.True(mode.Active);
        Assert.False(mode.Execute(0)); // a press over nothing runs nothing
        Assert.Empty(examined);
    }

    [Fact]
    public void Escape_ends_a_mode_and_is_taken_only_while_one_is_on()
    {
        var (manager, mode, _) = Rig();
        Assert.False(manager.Input.KeyDown(DikScanCodes.Escape) && mode.Active);
        manager.Input.KeyUp(DikScanCodes.Escape);
        mode.Set(UiTargetModeKind.Examine);
        Assert.True(manager.Input.KeyDown(DikScanCodes.Escape));
        manager.Input.KeyUp(DikScanCodes.Escape);
        Assert.False(mode.Active);
        Assert.Equal(Pointer.ImageId, manager.Cursor.DefaultCursorDid);
    }

    [Fact]
    public void A_left_or_right_press_through_the_router_ends_the_mode_on_the_next_frame()
    {
        var (manager, mode, _) = Rig();
        mode.Set(UiTargetModeKind.Examine);
        manager.Input.MouseMove(10, 10);
        manager.Input.MouseButton(UiMouseButton.Right, down: true);
        manager.Input.MouseButton(UiMouseButton.Right, down: false);
        Assert.True(mode.Active);
        manager.Update(1.0);
        Assert.False(mode.Active);
    }

    [Fact]
    public void The_use_target_mode_is_a_named_todo_and_no_cursor_table_logs_once()
    {
        var log = new RecordingLogSink();
        var manager = new UiElementManager(new UiServices { Descs = new FakeDescSource([]), Log = log });
        var mode = manager.TargetMode;
        mode.Set(UiTargetModeKind.UseTarget);
        Assert.True(mode.Execute(0x80000A01));
        Assert.Contains(log.Lines, static l => l.Contains("TODO(m5-target-mode-use-target)", StringComparison.Ordinal));
        Assert.Contains(log.Lines, static l => l.Contains("UICURSOR 39 has no image", StringComparison.Ordinal));
    }
}
