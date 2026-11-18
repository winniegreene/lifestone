using System.Globalization;
using System.Text;

namespace Lifestone.Client.Session.Threat;

/// <summary>A read of a target's health by one of today's two models, each a lane of the <see cref="HealthShadow"/>.</summary>
public enum HealthReader : byte
{
    /// <summary>The fight's picker snapshot (<c>FightScenario.Picker.cs</c>): every bolt's decision.</summary>
    FightPicker,

    /// <summary>The fight's opener (<c>FightScenario.Opener.cs</c>: no opener at a creature under half its health).</summary>
    FightOpener,

    /// <summary>The fight's drain rule (<c>FightScenario.Drain.cs</c>).</summary>
    FightDrain,

    /// <summary>The fight's life casts through a wall (<c>FightScenario.LifeThrough.cs</c>).</summary>
    FightLifeThrough,

    /// <summary>The fight's mana loop (<c>FightScenario.ManaLoop.cs</c>: the target dying to its next bolt).</summary>
    FightManaLoop,

    /// <summary>The fight's vitals policy reads (<c>FightScenario.VitalsPolicy.cs</c>).</summary>
    FightPolicy,

    /// <summary>The fight's drain yield (<c>FightScenario.Health.cs</c>: what a drain is expected to give).</summary>
    FightYield,

    /// <summary>The fight's emergency candidates (<c>FightScenario.Emergency.cs</c>: the fullest creature to drain).</summary>
    FightEmergency,

    /// <summary>The fight's war reads (<c>FightScenario.WarPick.cs</c>, <c>.InFlight.cs</c>: the ring's anchor, the bolts in flight).</summary>
    FightWar,

    // M6 16 Q5: the vitals guard's lanes (guard-fullest, guard-race, guard-kill) and the travel legs' (travel-fight-back, travel-life-strike) are gone: those readers read the one picture itself (Vitals/PictureHealth.cs)
}

/// <summary>Why a model and the one picture read two healths of one creature (<see cref="HealthShadow.Compare"/>); several can hold at once.</summary>
[Flags]
public enum HealthDifference : byte
{
    /// <summary>They agree.</summary>
    None = 0,

    /// <summary>The two maxima differ.</summary>
    Maximum = 1,

    /// <summary>The picture counts what the creature regenerated; no model of today does.</summary>
    Regeneration = 2,

    /// <summary>One took a word of the server (a health update, an appraisal) the other did not.</summary>
    ServerUpdate = 4,

    /// <summary>One heard damage of hers the other did not.</summary>
    Damage = 8,

    /// <summary>The model does not know the creature's health and the picture holds it under its maximum.</summary>
    ModelUnknown = 16,

    /// <summary>The picture holds no maximum for the creature and the model does.</summary>
    PictureUnknown = 32,
}

/// <summary>The shadow's memory of one creature, a lane each: whether its pair differs, why, since when, and whether its line was written. Made the first time the creature differs.</summary>
internal sealed class HealthShadowState
{
    public readonly bool[] Differing = new bool[HealthShadow.ReaderCount];
    public readonly bool[] Written = new bool[HealthShadow.ReaderCount];
    public readonly HealthDifference[] Why = new HealthDifference[HealthShadow.ReaderCount];
    public readonly double[] Since = new double[HealthShadow.ReaderCount];
    public readonly long[] Asks = new long[HealthShadow.ReaderCount];
}

/// <summary>
/// M6 16 E2d (docs/automation-engine.md section 3, "shadow first"): today's two models of a target's health beside the one picture (<see cref="HealthPicture"/>). A model decides as it always did; where it is read, the
/// picture is asked for the same creature, and a difference beyond the tolerance is one line when it appears, when its cause changes and when it ends:
/// <c>health-shadow: &lt;reader&gt;: &lt;name&gt; 0x&lt;guid&gt;: its model says H1 of M1, the one picture says H2 of M2 (&lt;why&gt;)</c>. Each reader is a lane with its own counts, the session's, so they outlive the
/// fight or the leg that asks; an owner reports its share on a line of its own (<see cref="HealthShadowShare"/>). Report-only: nothing here is read by any decision. Allocation-free while a pair stands; a creature's
/// first difference allocates its memory once, and a line allocates.
/// </summary>
/// <remarks>
/// <para><b>The tolerance</b> (<see cref="Tolerance"/>): the largest of one point, <see cref="ToleranceShare"/> of the larger maximum, and one heartbeat's regeneration of the kind (the picture adds the rate a second where
/// ACE adds it a heartbeat, so one step is no difference). A model that does not know a creature agrees with a picture that holds it at its maximum: both say nothing was heard against it.</para>
/// <para><b>Why they differ</b> is worked out from what both sides state: the two maxima; the picture's regeneration, when the gap is its size; the age of each side's last word of the server, when the reader states its
/// model's; what each counted of her damage since. A gap none of these explains is named as damage or a server update not told apart.</para>
/// </remarks>
public sealed class HealthShadow
{
    /// <summary>The token every line of the shadow starts with.</summary>
    public const string Token = "health-shadow: ";

    /// <summary>The summary field of the asks that agreed.</summary>
    public const string AgreeField = "health_shadow_agree";

    /// <summary>The summary field of the asks that differed.</summary>
    public const string DifferField = "health_shadow_differ";

    /// <summary>The share of the larger maximum two healths may lie apart and agree (Lifestone's own: a model that keeps a share as a float and one that keeps points round apart).</summary>
    public const double ToleranceShare = 0.01;

    /// <summary>The least seconds between two lines of one lane (Lifestone's own; the changes not written are counted and named in the next line).</summary>
    public const double LineGapSeconds = 1.0;

    /// <summary>How far apart the ages of the two sides' last word of the server may lie and still be the same word (the two hear it from different handlers of the bus).</summary>
    public const double ServerWordSlackSeconds = 0.5;

    /// <summary>How many lanes there are.</summary>
    public const int ReaderCount = (int)HealthReader.FightWar + 1;

    private static readonly string[] Words =
    [
        "fight-picker", "fight-opener", "fight-drain", "fight-life-through", "fight-mana-loop", "fight-policy", "fight-yield", "fight-emergency", "fight-war",
    ];

    private readonly HealthPicture _picture;
    private readonly long[] _agree = new long[ReaderCount];
    private readonly long[] _differ = new long[ReaderCount];
    private readonly double[] _lineAt = new double[ReaderCount];
    private readonly long[] _unwritten = new long[ReaderCount];

    internal HealthShadow(HealthPicture picture)
    {
        _picture = picture;
        Array.Fill(_lineAt, double.NegativeInfinity);
    }

    /// <summary>True when the shadow failed and turned itself off: nothing is asked or counted any more.</summary>
    public bool Off { get; private set; }

    /// <summary>The lines written this session.</summary>
    public long Lines { get; private set; }

    /// <summary>The lane's word in the lines (<c>fight-picker</c>, <c>fight-war</c>, ...).</summary>
    /// <param name="reader">The lane.</param>
    public static string Word(HealthReader reader) => Words[(int)reader];

    /// <summary>The asks of <paramref name="reader"/> that agreed.</summary>
    /// <param name="reader">The lane.</param>
    public long Agree(HealthReader reader) => _agree[(int)reader];

    /// <summary>The asks of <paramref name="reader"/> that differed.</summary>
    /// <param name="reader">The lane.</param>
    public long Differ(HealthReader reader) => _differ[(int)reader];

    /// <summary>The two summary fields for the counts given: <c>health_shadow_agree=&lt;n&gt; health_shadow_differ=&lt;n&gt;</c>.</summary>
    /// <param name="agree">The asks that agreed.</param>
    /// <param name="differ">The asks that differed.</param>
    public static string FieldsOf(long agree, long differ) => string.Create(CultureInfo.InvariantCulture, $"{AgreeField}={agree} {DifferField}={differ}");

    /// <summary>How far two healths of one creature may lie apart and agree.</summary>
    /// <param name="modelMax">The model's maximum.</param>
    /// <param name="picture">The picture's read.</param>
    public static double Tolerance(int modelMax, in HealthRead picture) => Math.Max(1.0, Math.Max(ToleranceShare * Math.Max(modelMax, picture.Max), picture.Tick));

    /// <summary>
    /// Why a model's read and the picture's differ, or <see cref="HealthDifference.None"/> when they agree. Pure.
    /// </summary>
    /// <param name="known">True when the model knows the creature's health.</param>
    /// <param name="health">The model's health, in points.</param>
    /// <param name="max">The model's maximum (0: it holds none).</param>
    /// <param name="picture">The picture's read.</param>
    /// <param name="modelDealt">What the model counts of her damage since its base, or NaN when the reader does not state it.</param>
    /// <param name="modelWordAge">The age of the model's last word of the server (infinity: it has none), or NaN when the reader does not state it.</param>
    public static HealthDifference Judge(bool known, double health, int max, in HealthRead picture, double modelDealt = double.NaN, double modelWordAge = double.NaN)
    {
        var tolerance = Tolerance(max, in picture);
        if (!known || max <= 0)
        {
            // a model that does not know it says nothing against its maximum: so does a picture that holds it there (or holds nothing)
            return !picture.Known || picture.Health >= picture.Max - tolerance ? HealthDifference.None : HealthDifference.ModelUnknown;
        }

        if (!picture.Known)
        {
            return HealthDifference.PictureUnknown;
        }

        var why = Math.Abs(max - picture.Max) > tolerance ? HealthDifference.Maximum : HealthDifference.None;
        var gap = why == HealthDifference.None ? picture.Health - health : ((picture.Health / picture.Max) - (health / max)) * picture.Max; // with two maxima, the gap of the shares
        if (Math.Abs(gap) <= tolerance)
        {
            return why;
        }

        if (picture.Regen > 0 && Math.Abs(gap - picture.Regen) <= tolerance)
        {
            return why | HealthDifference.Regeneration; // the picture stands higher by what the creature regenerated
        }

        if (picture.Regen > tolerance)
        {
            why |= HealthDifference.Regeneration;
        }

        var mark = why;
        if (!double.IsNaN(modelWordAge) && (picture.Told != !double.IsInfinity(modelWordAge) || (picture.Told && Math.Abs(picture.BaseAge - modelWordAge) > ServerWordSlackSeconds)))
        {
            why |= HealthDifference.ServerUpdate;
        }

        if (!double.IsNaN(modelDealt) && Math.Abs(modelDealt - picture.Dealt) > tolerance)
        {
            why |= HealthDifference.Damage;
        }

        return why == mark ? why | HealthDifference.Damage | HealthDifference.ServerUpdate : why; // a gap nothing stated explains: one of the two, not told apart
    }

    /// <summary>
    /// A model read <paramref name="guid"/> at <paramref name="now"/>: the picture is asked for the same creature and the pair compared. Counts the ask; writes one line through <paramref name="line"/> when a difference
    /// appears, when its cause changes and when it ends (at most one line a lane in <see cref="LineGapSeconds"/>). A failure turns the shadow off for the session and says so once.
    /// </summary>
    /// <param name="reader">The lane.</param>
    /// <param name="guid">The creature.</param>
    /// <param name="known">True when the model knows its health.</param>
    /// <param name="health">The model's health, in points.</param>
    /// <param name="max">The model's maximum (0: it holds none).</param>
    /// <param name="now">The session clock.</param>
    /// <param name="line">The owner's line writer.</param>
    /// <param name="modelDealt">What the model counts of her damage since its base, or NaN.</param>
    /// <param name="modelWordAge">The age of the model's last word of the server (infinity: none), or NaN.</param>
    public void Compare(HealthReader reader, uint guid, bool known, double health, int max, double now, Action<string> line, double modelDealt = double.NaN, double modelWordAge = double.NaN)
    {
        if (Off || guid == 0)
        {
            return;
        }

        try
        {
            var lane = (int)reader;
            var picture = guid == _picture.LastForgotten.Guid ? Last(now) : _picture.Read(guid, now);
            var why = Judge(known, health, max, in picture, modelDealt, modelWordAge);
            if (why == HealthDifference.None)
            {
                _agree[lane]++;
                if (_picture.ShadowStateOf(guid, make: false, now) is not { } quiet || !quiet.Differing[lane])
                {
                    return;
                }

                quiet.Differing[lane] = false;
                if (!quiet.Written[lane])
                {
                    _unwritten[lane]++; // the difference ended inside the gap, before its line
                    return;
                }

                (quiet.Written[lane], _lineAt[lane]) = (false, now);
                Lines++;
                line(string.Create(CultureInfo.InvariantCulture, $"{Token}{Word(reader)}: {_picture.NameOf(guid)} 0x{guid:X8}: agrees again after {now - quiet.Since[lane]:0.#} s ({quiet.Asks[lane]} ask(s) differed){TakeUnwritten(lane)}"));
                return;
            }

            _differ[lane]++;
            var state = _picture.ShadowStateOf(guid, make: true, now);
            if (state is null)
            {
                return; // no creature of that guid stands in the object table: counted, nothing to remember it by
            }

            if (!state.Differing[lane])
            {
                (state.Differing[lane], state.Written[lane], state.Since[lane], state.Asks[lane], state.Why[lane]) = (true, false, now, 0, why);
            }
            else if (state.Why[lane] != why)
            {
                if (!state.Written[lane])
                {
                    _unwritten[lane]++; // the cause before this one changed inside the gap, before its line
                }

                (state.Why[lane], state.Written[lane]) = (why, false);
            }

            state.Asks[lane]++;
            if (state.Written[lane] || now - _lineAt[lane] < LineGapSeconds)
            {
                return; // the difference stands as written, or the lane wrote inside the gap: written at its end if it still stands
            }

            (state.Written[lane], _lineAt[lane]) = (true, now);
            Lines++;
            line(DiffersLine(reader, guid, known, health, max, in picture, why, modelDealt, modelWordAge) + TakeUnwritten(lane));
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Off = true;
            line($"{Token}{Word(reader)}: TODO(health-shadow-failed): the shadow threw {error.GetType().Name}: {error.Message}; it is off for this session; the model decides as ever");
        }
    }

    /// <summary>The line of a difference.</summary>
    internal string DiffersLine(HealthReader reader, uint guid, bool known, double health, int max, in HealthRead picture, HealthDifference why, double modelDealt, double modelWordAge)
    {
        var c = CultureInfo.InvariantCulture;
        var model = !known ? "its health is not known" : max <= 0 ? "it holds no maximum for it" : string.Create(c, $"{health:0} of {max}");
        var one = picture.Known ? string.Create(c, $"{picture.Health:0} of {picture.Max}") : "its health is not known";
        var words = new StringBuilder();
        void Add(string text) => words.Append(words.Length == 0 ? string.Empty : "; ").Append(text);
        if ((why & HealthDifference.Maximum) != 0)
        {
            Add(string.Create(c, $"the maximum: its model's {max}, the picture's {picture.Max} from {(picture.MaxSource == HealthMaxSource.Appraisal ? "the creature's own appraisal" : "the bestiary")}"));
        }

        if ((why & HealthDifference.Regeneration) != 0)
        {
            Add(string.Create(c, $"regeneration: the picture counts {picture.Regen:0.#} regenerated at {picture.PerSecond:0.###} a second, its model none"));
        }

        if ((why & HealthDifference.ServerUpdate) != 0)
        {
            var theirs = double.IsNaN(modelWordAge) ? "its model's is not stated" : double.IsInfinity(modelWordAge) ? "its model holds none" : string.Create(c, $"its model's is {modelWordAge:0.#} s old");
            var ours = picture.Told ? string.Create(c, $"the picture's is {picture.Base:0} from {(picture.Basis == HealthBasis.Appraised ? "an appraisal" : "a health update")} {picture.BaseAge:0.#} s ago") : "the picture holds none";
            Add($"a server update one did not take: {ours}, {theirs}");
        }

        if ((why & HealthDifference.Damage) != 0)
        {
            var theirs = double.IsNaN(modelDealt) ? "its model's count is not stated" : string.Create(c, $"its model counts {modelDealt:0}");
            Add(string.Create(c, $"damage one did not hear: the picture counts {picture.Dealt:0} of hers since its base, {theirs}"));
        }

        if ((why & HealthDifference.ModelUnknown) != 0)
        {
            Add(string.Create(c, $"its model was told nothing of it: the picture's base is {picture.Base:0}{(picture.Told ? " by the server" : string.Empty)}, less {picture.Dealt:0} of hers, plus {picture.Regen:0.#} regenerated"));
        }

        if ((why & HealthDifference.PictureUnknown) != 0)
        {
            Add(string.Create(c, $"the maximum: the bestiary does not hold weenie {_picture.WcidOf(guid)} with one and no appraisal told one"));
        }

        return string.Create(c, $"{Token}{Word(reader)}: {_picture.NameOf(guid)} 0x{guid:X8}: its model says {model}, the one picture says {one} ({words})");
    }

    /// <summary>The picture's read of the creature it forgot last (a model read after its death): its health just before.</summary>
    private HealthRead Last(double now)
    {
        var last = _picture.LastForgotten;
        return last.Max <= 0 ? default : new HealthRead(HealthBasis.Counted, last.Health, last.Max, HealthMaxSource.Bestiary, last.Health, now - last.At, 0, 0, 0, 0);
    }

    private string TakeUnwritten(int lane)
    {
        if (_unwritten[lane] == 0)
        {
            return string.Empty;
        }

        var word = string.Create(CultureInfo.InvariantCulture, $" (+{_unwritten[lane]} change(s) inside {LineGapSeconds:0.#} s not written)");
        _unwritten[lane] = 0;
        return word;
    }
}

/// <summary>
/// M6 16 E2d: the share of the session's health-shadow lanes one owner reports on a line of its own (a fight): its lanes and a mark of each, so the line states the asks since the owner
/// began (or since its last line: <see cref="Remark"/>) though the lanes are the session's. Allocates when it is made and for a line.
/// </summary>
public sealed class HealthShadowShare
{
    private readonly HealthShadow _shadow;
    private readonly HealthReader[] _readers;
    private readonly long[] _agree;
    private readonly long[] _differ;

    /// <summary>The share of <paramref name="readers"/>, marked at their counts now.</summary>
    /// <param name="shadow">The session's shadow.</param>
    /// <param name="readers">The owner's lanes.</param>
    public HealthShadowShare(HealthShadow shadow, params HealthReader[] readers)
    {
        ArgumentNullException.ThrowIfNull(shadow);
        ArgumentNullException.ThrowIfNull(readers);
        (_shadow, _readers, _agree, _differ) = (shadow, readers, new long[readers.Length], new long[readers.Length]);
        Remark();
    }

    /// <summary>The asks of the owner's lanes since the marks: those that agreed and those that differed.</summary>
    public (long Agree, long Differ) Counts
    {
        get
        {
            var (agree, differ) = (0L, 0L);
            for (var i = 0; i < _readers.Length; i++)
            {
                agree += _shadow.Agree(_readers[i]) - _agree[i];
                differ += _shadow.Differ(_readers[i]) - _differ[i];
            }

            return (agree, differ);
        }
    }

    /// <summary>The marks move to the lanes' counts now.</summary>
    public void Remark()
    {
        for (var i = 0; i < _readers.Length; i++)
        {
            (_agree[i], _differ[i]) = (_shadow.Agree(_readers[i]), _shadow.Differ(_readers[i]));
        }
    }

    /// <summary>
    /// The owner's line: <c>health-shadow: &lt;owner&gt;: its asks: health_shadow_agree=&lt;n&gt; health_shadow_differ=&lt;n&gt; (agreed/differed by reader: &lt;reader&gt; &lt;a&gt;/&lt;d&gt;, ...)</c>, or null when no lane
    /// was asked since the marks.
    /// </summary>
    /// <param name="owner">The owner's word (<c>fight</c>).</param>
    public string? Line(string owner)
    {
        var (agree, differ) = Counts;
        if (agree + differ == 0)
        {
            return null;
        }

        var by = new StringBuilder();
        for (var i = 0; i < _readers.Length; i++)
        {
            var (a, d) = (_shadow.Agree(_readers[i]) - _agree[i], _shadow.Differ(_readers[i]) - _differ[i]);
            if (a + d > 0)
            {
                by.Append(by.Length == 0 ? string.Empty : ", ").Append(CultureInfo.InvariantCulture, $"{HealthShadow.Word(_readers[i])} {a}/{d}");
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"{HealthShadow.Token}{owner}: its asks: {HealthShadow.FieldsOf(agree, differ)} (agreed/differed by reader: {by})");
    }
}
