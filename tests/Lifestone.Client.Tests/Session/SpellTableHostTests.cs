using System.Collections.Immutable;
using DatReaderWriter.Enums;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Tests.Session;

/// <summary>M3 1.2 fix R5: the SpellTable is decoded on a worker from the session being given its dats, never on the thread that asks for a spell, and a failed load is one logged WARNING.</summary>
public sealed class SpellTableHostTests
{
    private static IReadOnlyDictionary<uint, SpellInfo> Table() =>
        ImmutableDictionary<uint, SpellInfo>.Empty.Add(2, WorldActionRig.Spell(2, SpellType.Enchantment, SpellFlags.SelfTargeted));

    [Fact]
    public async Task The_table_is_decoded_on_a_worker_and_a_lookup_before_it_is_ready_is_null_and_never_decodes_on_the_calling_thread()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var loadedOn = 0;
        var log = new RecordingLogSink();
        var host = SpellTableHost.Start(
            () =>
            {
                loadedOn = Environment.CurrentManagedThreadId;
                started.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(30)));
                return Table();
            },
            () => ImmutableDictionary<uint, uint>.Empty.Add(2, 0x02000099),
            log,
            TimeProvider.System);

        Assert.True(started.Wait(TimeSpan.FromSeconds(30)));
        Assert.NotEqual(Environment.CurrentManagedThreadId, loadedOn); // the load runs on a worker, not on the caller
        Assert.False(host.Ready);
        Assert.Null(host.Find(2)); // the first cast sees "Unknown" until the table is ready: it does not wait for, or run, the decode
        Assert.Null(host.ProjectileSetup(2));

        release.Set();
        await host.Loaded.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(host.Ready);
        Assert.NotEqual(Environment.CurrentManagedThreadId, host.LoadThreadId);
        Assert.Equal(2u, host.Find(2)?.Id);
        Assert.Null(host.Find(3));
        Assert.Equal(0x02000099u, host.ProjectileSetup(2));
        Assert.Single(log.Entries, e => e.Message.Contains("the SpellTable loaded on a worker in ", StringComparison.Ordinal)); // the load time, once
        Assert.DoesNotContain(log.Entries, e => e.Severity >= LogSeverity.Warning);
    }

    [Theory]
    [InlineData(typeof(InvalidDataException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(KeyNotFoundException))]
    [InlineData(typeof(IndexOutOfRangeException))]
    public async Task A_table_that_cannot_be_read_is_one_warning_and_leaves_every_spell_unknown_whatever_the_failure(Type failure)
    {
        var log = new RecordingLogSink();
        var host = SpellTableHost.Start(() => throw (Exception)Activator.CreateInstance(failure, "unreadable")!, () => null, log, TimeProvider.System);

        await host.Loaded.WaitAsync(TimeSpan.FromSeconds(30)); // never faults: the worker catches the failure
        Assert.False(host.Ready);
        Assert.Null(host.Find(2));
        var warning = Assert.Single(log.Entries, e => e.Severity >= LogSeverity.Warning);
        Assert.Contains("TODO(m3-cast-spell-table-host)", warning.Message, StringComparison.Ordinal);
        Assert.Contains("unreadable", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Projectile_setups_that_cannot_be_read_do_not_cost_the_spells_and_say_so()
    {
        var log = new RecordingLogSink();
        var host = SpellTableHost.Start(Table, () => throw new FileNotFoundException("no extract"), log, TimeProvider.System);

        await host.Loaded.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(2u, host.Find(2)?.Id);
        Assert.Null(host.ProjectileSetup(2));
        Assert.Contains("TODO(m3-cast-projectile-attribution)", Assert.Single(log.Entries, e => e.Severity >= LogSeverity.Warning).Message, StringComparison.Ordinal);
    }
}
