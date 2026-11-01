using System.Globalization;
using Lifestone.Client.Avatars;

namespace Lifestone.Client.Diagnostics;

/// <summary>
/// What a play-mode host does with the avatar set's notices once a frame (M5 item 7, <c>m5-avatar-notices-full</c>): it counts them by kind and logs
/// the problem kinds (<see cref="IsProblem"/>) at most once per kind per <see cref="LogIntervalSeconds"/> at Info with the running count, so a run of
/// hours neither fills the set's list (<see cref="AvatarSet.MaxNotices"/>) nor floods the log. Engine-free; the host drains the set into one reused list
/// and hands that list to <see cref="Add"/>, which allocates nothing unless it logs.
/// </summary>
public sealed class AvatarNoticeTally
{
    /// <summary>The fewest seconds between two log lines of one kind.</summary>
    public const double LogIntervalSeconds = 60.0;

    /// <summary>The most object lines (creates and deletes near the player, <c>--log-objects-m</c>) written in one window (M5 7 F17).</summary>
    public const int ObjectLinesPerWindow = 40;

    /// <summary>The length of that window in seconds: past the lines above, the rest of the window is only counted, and the next window's first notice logs one line naming how many.</summary>
    public const double ObjectWindowSeconds = 10.0;

    private const int KindCount = (int)AvatarNoticeKind.UnknownObject + 1;
    private const int MaxDetailChars = 120;

    private readonly ILogSink _log;
    private readonly string _category;
    private readonly long[] _counts = new long[KindCount];
    private readonly long[] _loggedCounts = new long[KindCount];
    private readonly double[] _lastLogged = new double[KindCount];
    private readonly bool[] _everLogged = new bool[KindCount];
    private readonly double _objectRadius;
    private bool _windowStarted;
    private double _windowStart;
    private int _windowLines;
    private int _windowSuppressed;

    /// <summary>Creates a tally that logs through <paramref name="log"/> under <paramref name="category"/>.</summary>
    /// <param name="log">Where the lines go.</param>
    /// <param name="category">The category of the problem lines.</param>
    /// <param name="objectRadiusMetres">M5 7 F17 (<c>--log-objects-m</c>): an object created, re-created or deleted within this many metres of the player is logged at Info in the category <c>objects</c>; 0 or less = none.</param>
    public AvatarNoticeTally(ILogSink log, string category = "avatars", double objectRadiusMetres = 0)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _category = category ?? throw new ArgumentNullException(nameof(category));
        _objectRadius = objectRadiusMetres;
    }

    /// <summary>Object lines written so far.</summary>
    public long ObjectLinesLogged { get; private set; }

    /// <summary>Object notices inside the radius that the per-window limit kept out of the log.</summary>
    public long ObjectLinesSuppressed { get; private set; }

    /// <summary>True when the notice is one the object lines cover: a create, re-create or delete with a known distance inside <paramref name="radiusMetres"/> (the radius is inclusive).</summary>
    public static bool IsNearObject(in AvatarNotice notice, double radiusMetres) =>
        radiusMetres > 0 && notice.Kind is AvatarNoticeKind.Created or AvatarNoticeKind.Recreated or AvatarNoticeKind.Deleted && notice.Distance >= 0 && notice.Distance <= radiusMetres;

    /// <summary>All notices counted so far.</summary>
    public long Total { get; private set; }

    /// <summary>True for the kinds that report something wrong: the ones the tally logs.</summary>
    public static bool IsProblem(AvatarNoticeKind kind) => kind is AvatarNoticeKind.AppearanceProblem or AvatarNoticeKind.CreateFailed
        or AvatarNoticeKind.ParentFailed or AvatarNoticeKind.MotionFailed or AvatarNoticeKind.UnknownObject;

    /// <summary>How many notices of <paramref name="kind"/> were counted.</summary>
    public long Count(AvatarNoticeKind kind) => _counts[(int)kind];

    /// <summary>Counts <paramref name="notices"/> (the frame's drain) and logs each problem kind that is due; <paramref name="nowSeconds"/> is any monotonic clock.</summary>
    public void Add(List<AvatarNotice> notices, double nowSeconds)
    {
        ArgumentNullException.ThrowIfNull(notices);
        for (var i = 0; i < notices.Count; i++)
        {
            var notice = notices[i];
            var kind = (int)notice.Kind;
            if ((uint)kind >= KindCount)
            {
                continue; // a kind added later is counted by whoever adds it; never an index fault in a long run
            }

            _counts[kind]++;
            Total++;
            if (_objectRadius > 0 && IsNearObject(in notice, _objectRadius))
            {
                LogObject(in notice, nowSeconds);
            }

            if (!IsProblem(notice.Kind) || (_everLogged[kind] && nowSeconds - _lastLogged[kind] < LogIntervalSeconds))
            {
                continue;
            }

            _everLogged[kind] = true;
            _lastLogged[kind] = nowSeconds;
            var sinceLast = _counts[kind] - _loggedCounts[kind];
            _loggedCounts[kind] = _counts[kind];
            var detail = notice.Detail ?? string.Empty;
            if (detail.Length > MaxDetailChars)
            {
                detail = detail[..MaxDetailChars];
            }

            _log.Info(
                _category,
                "avatar notices: problem",
                new LogField("kind", notice.Kind),
                new LogField("count", _counts[kind]),
                new LogField("since_last_line", sinceLast),
                new LogField("guid", string.Create(CultureInfo.InvariantCulture, $"0x{notice.Guid:X8}")),
                new LogField("detail", detail));
        }
    }

    // One object line, within the window's limit; the first notice of a new window names the lines the last one kept out.
    private void LogObject(in AvatarNotice notice, double nowSeconds)
    {
        if (!_windowStarted || nowSeconds - _windowStart >= ObjectWindowSeconds)
        {
            if (_windowSuppressed > 0)
            {
                _log.Info("objects", "object lines left out (over the limit of a window)", new LogField("count", _windowSuppressed), new LogField("window_s", ObjectWindowSeconds), new LogField("limit", ObjectLinesPerWindow));
            }

            _windowStarted = true;
            _windowStart = nowSeconds;
            _windowLines = 0;
            _windowSuppressed = 0;
        }

        if (_windowLines >= ObjectLinesPerWindow)
        {
            _windowSuppressed++;
            ObjectLinesSuppressed++;
            return;
        }

        _windowLines++;
        ObjectLinesLogged++;
        var name = string.IsNullOrEmpty(notice.Name) ? "?" : notice.Name;
        _log.Info(
            "objects",
            notice.Kind switch { AvatarNoticeKind.Created => "object created", AvatarNoticeKind.Recreated => "object created again", _ => "object deleted" },
            new LogField("name", name),
            new LogField("guid", string.Create(CultureInfo.InvariantCulture, $"0x{notice.Guid:X8}")),
            new LogField("dist_m", MathF.Round(notice.Distance, 1)));
    }
}
