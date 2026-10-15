using Lifestone.Client.Diagnostics;

namespace Lifestone.Client.Tests;

public sealed class LogSinkTests
{
    [Fact]
    public void Recording_sink_formats_like_the_game_log_and_filters()
    {
        var sink = new RecordingLogSink(LogSeverity.Info);
        sink.Debug("cat", "hidden", new LogField("a", 1));
        sink.Info("session", "connected", new LogField("ms", 12.3456), new LogField("port", 9000), new LogField("name", "two words"), new LogField("none", null));
        sink.Warn("portal", "slow");
        sink.Error("net", "lost", new LogField("f", 1.5f));
        Assert.Equal(
            new[] { "INFO  [session] connected ms=12.346 port=9000 name=\"two words\" none=null", "WARN  [portal] slow", "ERROR [net] lost f=1.5" },
            sink.Lines);
        Assert.Equal(3, sink.Entries.Count);
        Assert.True(sink.AnyLineContains("9000"));
        Assert.False(sink.AnyLineContains("hidden"));
        Assert.False(sink.IsEnabled(LogSeverity.Debug));
        sink.Clear();
        Assert.Empty(sink.Lines);
    }

    [Fact]
    public void Null_sink_is_disabled_and_ignores_writes()
    {
        ILogSink sink = NullLogSink.Instance;
        Assert.False(sink.IsEnabled(LogSeverity.Error));
        sink.Error("x", "y", new LogField("k", "v"));
        sink.Write(LogSeverity.Error, "x", "y", []);
    }

    [Fact]
    public void Extensions_skip_disabled_severities()
    {
        var sink = new RecordingLogSink(LogSeverity.Error);
        sink.Info("a", "b");
        Assert.Empty(sink.Entries);
    }
}
