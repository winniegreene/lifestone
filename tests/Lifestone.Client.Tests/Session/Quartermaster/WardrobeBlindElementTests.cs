using System.Collections.Immutable;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;
using static Lifestone.Client.Tests.Session.Quartermaster.QuartersState;

namespace Lifestone.Client.Tests.Session.Quartermaster;

/// <summary>
/// M6 item 16, slice E5e3, the loud stub (<see cref="Wardrobe.NoteBlindElement"/>): a caster pick that reads no element on a ground where a fight of the session has a line in hand is an error, once a session. It is the
/// line that was missing on 2026-11-18: five runs wrote the unknown element as a TODO inside an Info line while the fight cast the fire line.
/// </summary>
public sealed class WardrobeBlindElementTests
{
    private sealed class Sink(List<(LogSeverity Severity, string Line)> lines) : ILogSink
    {
        public bool IsEnabled(LogSeverity severity) => true;

        public void Write(LogSeverity severity, string category, string message, ReadOnlySpan<LogField> fields) => lines.Add((severity, message));
    }

    [Fact]
    public void A_pick_that_reads_no_element_while_the_fight_there_has_a_line_is_an_error_once_a_session()
    {
        // A state in which the judge reads no element: level 82 on the Quarters (no rung, no plan row) and a book that holds no bolt, so the fight's own choice is not known either.
        var quarters = GroundCatalog.Get("tusker-quarters");
        var spells = Spells();
        var rig = MageOf(82, 379, ImmutableDictionary<uint, float>.Empty.Add(HermeticLinkVI, 2f));
        var wardrobe = Wardrobe.For(rig.State);
        wardrobe.JudgeOn(quarters, spells, out var read);
        Assert.Equal(0u, read.Element);
        var lines = new List<(LogSeverity Severity, string Line)>();
        var log = new Sink(lines);

        // No fight on this ground this session: the unknown element is the quiet TODO of the judge's own line, as before.
        Assert.False(wardrobe.NoteBlindElement(log, "test", quarters, read));
        Assert.Empty(lines);

        // A fight there has a line in hand: an error, once.
        CastElement.For(rig.State).StateFight(quarters.Key, 0.95, 7);
        ElementsUsed.For(rig.State).Note("fire");
        Assert.True(wardrobe.NoteBlindElement(log, "test", quarters, read));
        Assert.False(wardrobe.NoteBlindElement(log, "test", quarters, read));
        var (severity, line) = Assert.Single(lines);
        Assert.Equal(LogSeverity.Error, severity);
        Assert.StartsWith("gear: TODO(m6-caster-element-blind): the caster pick on tusker-quarters reads no element while the fight there has a line in hand (fire)", line, StringComparison.Ordinal);
        Assert.Contains("TODO(m5-self-equip-element)", line, StringComparison.Ordinal); // the judge's own source, whole
    }

    [Fact]
    public void A_pick_that_reads_its_element_writes_nothing()
    {
        var quarters = GroundCatalog.Get("tusker-quarters");
        var spells = Spells();
        var rig = MageOf(82, 379, Book(6, fireSeven: true));
        var wardrobe = Wardrobe.For(rig.State);
        CastElement.For(rig.State).StateFight(quarters.Key, 0.95, 7);
        wardrobe.JudgeOn(quarters, spells, out var read);
        var lines = new List<(LogSeverity Severity, string Line)>();
        Assert.False(wardrobe.NoteBlindElement(new Sink(lines), "test", quarters, read));
        Assert.Empty(lines);
    }
}
