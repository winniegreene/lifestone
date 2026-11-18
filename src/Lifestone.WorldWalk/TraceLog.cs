using System.Text;
using Lifestone.Client.Diagnostics;

namespace Lifestone.WorldWalk;

/// <summary>The <c>--trace</c> log: every entry of the walks, one line each, to a file under the run's control. For looking at one walk closely.</summary>
internal sealed class TraceLog(string path) : ILogSink, IDisposable
{
    private readonly StreamWriter _file = new(path);

    public bool IsEnabled(LogSeverity severity) => true;

    public void Write(LogSeverity severity, string category, string message, ReadOnlySpan<LogField> fields)
    {
        var line = new StringBuilder(severity.ToString()).Append(' ').Append(category).Append(": ").Append(message);
        foreach (var field in fields)
        {
            line.Append(' ').Append(field.ToString());
        }

        lock (_file)
        {
            _file.WriteLine(line.ToString());
        }
    }

    public void Dispose() => _file.Dispose();
}
