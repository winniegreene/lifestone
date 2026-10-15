using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Core;

/// <summary>
/// The root made over frames (<see cref="UiStagedRoot"/>, the shell warm-up's way to spread a build; G4 live-gate fix 3): the same tree as
/// <c>CreateRootElement</c> makes, joined to the manager only when whole, one unit of work per step when the deadline has passed.
/// </summary>
public sealed class UiStagedRootTests
{
    private const uint LayoutId = 0x21000001;
    private const uint F = UiClassIds.Field;

    private static UiElementManager Manager() => new(new UiServices
    {
        Descs = new FakeDescSource([UiDescs.Layout(LayoutId, 800, 600, Tree())]),
    });

    private static UiElementDesc Tree() => UiDescs.Element(1, F, 0, 0, 800, 600, children:
    [
        UiDescs.Element(2, F, 10, 10, 100, 100, children: [UiDescs.Element(5, F, 1, 1, 10, 10), UiDescs.Element(6, F, 2, 2, 10, 10)]),
        UiDescs.Element(3, F, 15, 15, 100, 100),
        UiDescs.Element(4, F, 20, 20, 100, 100, children: [UiDescs.Element(7, F, 1, 1, 10, 10)]),
    ]);

    private static string Describe(UiElement element) =>
        $"{element.ElementId}[{string.Join(",", element.Children.Select(Describe))}]";

    [Fact]
    public void The_staged_root_is_the_tree_the_one_call_root_is()
    {
        var whole = Manager().CreateRootElementByDataId(LayoutId, 1)!;

        var manager = Manager();
        var staged = manager.BeginRootElementByDataId(LayoutId, 1)!;
        Assert.True(staged.Step(long.MaxValue));

        Assert.True(staged.Done);
        Assert.NotNull(staged.Root);
        Assert.Equal(Describe(whole), Describe(staged.Root!));
        Assert.Same(staged.Root, Assert.Single(manager.Roots));
        Assert.True(staged.Root!.IsShown);
        Assert.Equal(Manager().CreateRootElementByDataId(LayoutId, 1)!.Box, staged.Root.Box);
    }

    [Fact]
    public void A_passed_deadline_does_one_unit_a_step_and_the_root_joins_the_manager_only_when_whole()
    {
        var manager = Manager();
        var staged = manager.BeginRootElementByDataId(LayoutId, 1)!;

        var steps = 0;
        while (!staged.Step(0))
        {
            steps++;
            Assert.Equal(steps, staged.Units);
            Assert.Null(staged.Root);
            Assert.Empty(manager.Roots);
            Assert.True(steps < 100, "the build must end");
        }

        Assert.True(steps >= 7, "at least one unit per element: " + steps);
        Assert.NotNull(staged.Root);
        Assert.Single(manager.Roots);
        Assert.True(staged.Step(0), "a done root stays done");
    }

    [Fact]
    public void A_layout_or_element_that_does_not_exist_begins_nothing()
    {
        var manager = Manager();

        Assert.Null(manager.BeginRootElementByDataId(0x21000099, 1));
        Assert.Null(manager.BeginRootElementByDataId(LayoutId, 99));
        Assert.Null(manager.BeginRootElement(0x10000001, 1)); // the fake source has no enum map
    }

    [Fact]
    public void The_shell_registers_its_controller_classes_so_they_are_not_reported_unbuilt()
    {
        var registry = UiClassRegistry.CreateDefault();
        Assert.All(GamePlayScreen.ControllerClasses, id => Assert.False(registry.IsImplemented(id)));

        GamePlayScreen.RegisterClasses(registry);

        Assert.All(GamePlayScreen.ControllerClasses, id => Assert.True(registry.IsImplemented(id), UiClassRegistry.NameOf(id)));
        Assert.Contains(0x10000016u, GamePlayScreen.ControllerClasses); // gmSpewBoxUI
        Assert.DoesNotContain(0x1000000Fu, GamePlayScreen.ControllerClasses); // gmPowerbarUI has no controller: it stays loud
        Assert.False(registry.IsImplemented(0x1000000F));
    }
}
