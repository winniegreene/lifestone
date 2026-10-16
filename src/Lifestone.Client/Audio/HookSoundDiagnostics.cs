using System.Globalization;
using System.Text;
using Lifestone.Data.Animation;

namespace Lifestone.Client.Audio;

/// <summary>
/// Why the animation hooks handed to the sound player did or did not start a voice (M3 1.3 fix B, round 2; report-only): counts by hook kind (Sound, SoundTable,
/// SoundTweaked) and outcome (sounded, probability roll refused, attenuation below the floor, other: no variant or wave, a disabled category), a distance histogram of
/// the sounded and the refused, and the first few refused sounds with their distance and the figure they were refused at. Fixed arrays: no allocation per hook.
/// </summary>
public sealed class HookSoundDiagnostics
{
    /// <summary>Hook kinds in the counts.</summary>
    public static readonly string[] Kinds = ["SoundHook", "SoundTableHook", "SoundTweakedHook"];

    /// <summary>Outcomes in the counts.</summary>
    public static readonly string[] Outcomes = ["sounded", "roll", "attenuation", "other"];

    /// <summary>The upper edges (m) of the distance histogram's buckets; the last bucket is beyond the last edge.</summary>
    public static readonly float[] DistanceEdges = [5f, 10f, 20f, 50f, 100f];

    private const int MaxSamples = 16;
    private readonly int[] _counts = new int[3 * 4];
    private readonly int[] _sounded = new int[6];
    private readonly int[] _refused = new int[6];
    private readonly Sample[] _samples = new Sample[MaxSamples];
    private int _sampleCount;

    private readonly record struct Sample(int Kind, uint Type, float Distance, string Reason, float Figure);

    /// <summary>Notes one hook handed to the sound player and what it did (deltas of the player's counters around the hand-off).</summary>
    /// <param name="hook">The hook.</param>
    /// <param name="distance">Its distance from the listener, metres.</param>
    /// <param name="sounded">Voices it started.</param>
    /// <param name="rolls">Probability rolls it failed.</param>
    /// <param name="attenuations">Attenuations it failed.</param>
    /// <param name="player">The sound player (for the figures of the last refusal).</param>
    public void Note(AnimHook hook, float distance, int sounded, int rolls, int attenuations, RetailSoundPlayer player)
    {
        var kind = hook switch
        {
            SoundHook => 0,
            SoundTableHook => 1,
            _ => 2,
        };
        var outcome = sounded > 0 ? 0 : rolls > 0 ? 1 : attenuations > 0 ? 2 : 3;
        _counts[(kind * 4) + outcome]++;
        var bucket = 0;
        while (bucket < DistanceEdges.Length && distance >= DistanceEdges[bucket])
        {
            bucket++;
        }

        if (outcome == 0)
        {
            _sounded[bucket]++;
        }
        else
        {
            _refused[bucket]++;
        }

        if (outcome is 1 or 2 && _sampleCount < MaxSamples)
        {
            var type = hook is SoundTableHook table ? table.SoundType : 0u;
            _samples[_sampleCount++] = new Sample(kind, type, distance, outcome == 1 ? "roll" : "attenuation", outcome == 1 ? player.LastRollRefused : player.LastAttenuationRefused.Volume);
        }
    }

    /// <summary>The count of a kind and outcome.</summary>
    /// <param name="kind">0 SoundHook, 1 SoundTableHook, 2 SoundTweakedHook.</param>
    /// <param name="outcome">0 sounded, 1 roll, 2 attenuation, 3 other.</param>
    public int Count(int kind, int outcome) => _counts[(kind * 4) + outcome];

    /// <summary>The counts as one line: <c>SoundHook[sounded=n roll=n attenuation=n other=n] ...</c>.</summary>
    public string Summary()
    {
        var sb = new StringBuilder();
        for (var k = 0; k < 3; k++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{Kinds[k]}[");
            for (var o = 0; o < 4; o++)
            {
                sb.Append(CultureInfo.InvariantCulture, $"{(o > 0 ? " " : string.Empty)}{Outcomes[o]}={Count(k, o)}");
            }

            sb.Append("] ");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>The distance histogram: <c>&lt;5m:sounded/refused ... &gt;=100m:sounded/refused</c>.</summary>
    public string Distances()
    {
        var sb = new StringBuilder();
        for (var b = 0; b < _sounded.Length; b++)
        {
            var label = b < DistanceEdges.Length ? string.Create(CultureInfo.InvariantCulture, $"<{DistanceEdges[b]:0}m") : string.Create(CultureInfo.InvariantCulture, $">={DistanceEdges[^1]:0}m");
            sb.Append(CultureInfo.InvariantCulture, $"{(b > 0 ? " " : string.Empty)}{label}:{_sounded[b]}/{_refused[b]}");
        }

        return sb.ToString();
    }

    /// <summary>The first refused sounds: <c>kind type=0x.. d=..m reason figure</c> (the figure is the probability for a roll, the volume for an attenuation).</summary>
    public string Samples()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < _sampleCount; i++)
        {
            var s = _samples[i];
            sb.Append(CultureInfo.InvariantCulture, $"{(i > 0 ? "; " : string.Empty)}{Kinds[s.Kind]} type=0x{s.Type:X2} d={s.Distance:0.0}m {s.Reason}{(s.Reason == "roll" ? " p=" : " vol=")}{s.Figure:0.###}");
        }

        return sb.ToString();
    }
}
