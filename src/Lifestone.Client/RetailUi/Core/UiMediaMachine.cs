using Lifestone.Client.Diagnostics;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Core;

/// <summary>Something the element manager ticks once per frame (retail's listeners for global message 3).</summary>
internal interface IUiTickTarget
{
    /// <summary>One frame at time <paramref name="now"/> (seconds).</summary>
    void Tick(double now);
}

/// <summary>
/// An element's media machine: plays a state's media entries in order (retail's <c>MediaMachine</c>). Images, alpha
/// masks, cursors, sounds and jumps act at once and move on; a pause, an animation or a fade holds the machine until
/// it ends; a message waits until the element is initialized; a state change is rolled every frame until it happens
/// (the new state's media then restart the machine).
/// </summary>
/// <remarks>
/// <para>
/// Re-expressed from <c>MediaMachine::Reset</c> (11.4186 0x00465D90, 11.6096 0x00465EC0), <c>MediaMachine::Update</c>
/// (11.4186 0x00465BA0, 11.6096 0x00465CD0) and the <c>Update_*</c> handlers (11.4186 0x00465520-0x00465A80, 11.6096
/// 0x00465650-0x00465BB0). Time comes from the manager (<see cref="UiElementManager.Now"/>, retail's
/// <c>Timer::compute_time</c>), dice from <see cref="IUiRandom"/>, sounds go to <see cref="IUiSoundSink"/>.
/// </para>
/// <para>
/// A blocked machine registers with the manager for a tick each frame (retail's global message 3) and unregisters
/// when it runs to the end; an idle machine costs nothing.
/// </para>
/// </remarks>
public sealed class UiMediaMachine : IUiTickTarget
{
    private const double InvalidTime = -1.0;

    private readonly UiElement _owner;
    private UiMedia[] _media = [];
    private double[] _times = [];
    private int[] _frames = [];
    private int _count;
    private int _generation;

    internal UiMediaMachine(UiElement owner) => _owner = owner;

    /// <summary>The entry the machine is on (<c>m_curIndex</c>); <see cref="Count"/> when it ran to the end.</summary>
    public int CurrentIndex { get; private set; }

    /// <summary>The number of entries (<c>m_array.m_num</c>).</summary>
    public int Count => _count;

    /// <summary>True while the machine waits on an entry and ticks every frame.</summary>
    public bool IsBlocked => CurrentIndex < _count;

    /// <summary>
    /// <c>MediaMachine::Reset</c>: drops the old entries, takes fresh copies of <paramref name="media"/> (each entry's
    /// timers start over), goes to the first entry and runs (<see cref="Update"/>).
    /// </summary>
    public void Reset(IReadOnlyList<UiMedia> media)
    {
        ArgumentNullException.ThrowIfNull(media);
        _generation++;
        if (_media.Length < media.Count)
        {
            _media = new UiMedia[media.Count];
            _times = new double[media.Count];
            _frames = new int[media.Count];
        }

        _count = 0;
        for (var i = 0; i < media.Count; i++)
        {
            if (media[i] is { } entry)
            {
                _media[_count] = entry;
                _times[_count] = InvalidTime;
                _frames[_count] = -1;
                _count++;
            }
        }

        Array.Clear(_media, _count, _media.Length - _count);
        CurrentIndex = 0;
        Update();
    }

    /// <summary>
    /// <c>MediaMachine::Update</c>: runs entries from the current one until one is not finished (the machine then ticks
    /// each frame) or the list ends.
    /// </summary>
    public void Update()
    {
        _owner.Manager.UnregisterTick(this);
        var generation = _generation;
        while (CurrentIndex < _count)
        {
            var finished = UpdateEntry(CurrentIndex);
            if (generation != _generation)
            {
                // A state change restarted the machine; the inner run decided what happens next.
                return;
            }

            if (!finished)
            {
                _owner.Manager.RegisterTick(this);
                return;
            }

            CurrentIndex++;
        }
    }

    /// <inheritdoc/>
    void IUiTickTarget.Tick(double now) => Update();

    private bool UpdateEntry(int index)
    {
        var services = _owner.Manager.Services;
        switch (_media[index])
        {
            case UiImageMedia image:
                // MediaMachine::Update_Image: INVALID_DID (0) clears the image.
                _owner.SetImageByDid(image.File, image.DrawMode);
                return true;

            case UiAlphaMedia alpha:
                if (alpha.File == 0)
                {
                    _owner.ClearAlphaImage();
                }
                else
                {
                    _owner.SetAlphaImageByDid(alpha.File);
                }

                return true;

            case UiCursorMedia cursor:
                if (cursor.File == 0)
                {
                    _owner.UnSetCursor();
                }
                else
                {
                    _owner.SetCursor(cursor.File, unchecked((int)cursor.HotspotX), unchecked((int)cursor.HotspotY));
                }

                return true;

            case UiSoundMedia sound:
                // MediaMachine::Update_Sound: a sound type names an entry of the SoundTable; none plays the file as a Wave.
                if (sound.Sound != 0)
                {
                    services.Sound.PlayFromCenter(sound.File, sound.Sound);
                }
                else if (sound.File != 0)
                {
                    services.Sound.PlayWaveFromCenter(sound.File, 1f);
                }

                return true;

            case UiJumpMedia jump:
                if (services.Random.RollDice(0.0, 1.0) <= jump.Probability)
                {
                    // The loop's increment lands on the jump target.
                    CurrentIndex = unchecked((int)jump.JumpItemIndex) - 1;
                }

                return true;

            case UiMessageMedia message:
                if (!_owner.IsInitialized)
                {
                    return false;
                }

                if (services.Random.RollDice(0.0, 1.0) <= message.Probability)
                {
                    _owner.Manager.BroadcastElementMessage(_owner, message.MessageId, 0, 0);
                }

                return true;

            case UiStateMedia state:
                // MediaMachine::Update_State never finishes: it rolls every frame until the state changes, and the new
                // state's media restart the machine.
                if (services.Random.RollDice(0.0, 1.0) <= state.Probability)
                {
                    _owner.SetState(state.StateId);
                }

                return false;

            case UiPauseMedia pause:
                return UpdatePause(index, pause, services);

            case UiAnimationMedia animation:
                return UpdateAnimation(index, animation);

            case UiFadeMedia fade:
                return UpdateFade(index, fade);

            case UiMovieMedia movie:
                if (!_owner.IsInitialized)
                {
                    return false;
                }

                // Movies are the intro and the credits, which M2 skips (D38).
                services.Log.Warn("retailui", $"TODO(retailui-runtime): {_owner.Desc.Describe()} plays movie '{movie.FileName}'; movies are not in M2, the entry is skipped (placeholder)");
                return true;

            default:
                return true;
        }
    }

    /// <summary><c>MediaMachine::Update_Pause</c>: the first call rolls the pause's end; the entry finishes once it is reached.</summary>
    private bool UpdatePause(int index, UiPauseMedia pause, UiServices services)
    {
        var now = _owner.Manager.Now;
        if (_times[index] == InvalidTime)
        {
            _times[index] = services.Random.RollDice(pause.MinDuration, pause.MaxDuration) + now;
        }

        if (now >= _times[index])
        {
            _times[index] = InvalidTime;
            return true;
        }

        return false;
    }

    /// <summary>
    /// <c>MediaMachine::Update_Anim</c>: the first call starts the clock; the progress t (stored as a float, clamped to
    /// 0-1; 0 when the duration is nearly 0, which never finishes, as retail) picks frame <c>round((n - 1) × t)</c>, set
    /// only when it changes; the entry finishes when t reaches 1.
    /// </summary>
    private bool UpdateAnimation(int index, UiAnimationMedia animation)
    {
        var now = _owner.Manager.Now;
        if (_times[index] == InvalidTime)
        {
            _times[index] = now;
            _frames[index] = -1;
        }

        var duration = (double)animation.Duration;
        var t = Math.Abs(duration) > 0.00019999999 ? (float)((now - _times[index]) / duration) : 0f;
        t = t >= 0f ? t : 0f;
        t = t <= 1f ? t : 1f;
        var count = animation.Frames.Count;
        if (count != 0)
        {
            var frame = unchecked((int)(long)(((double)(count - 1) * t) + 0.5));
            if (frame != _frames[index])
            {
                _owner.SetImageByDid(animation.Frames[frame], animation.DrawMode);
                _frames[index] = frame;
            }
        }

        if (t == 1f)
        {
            _times[index] = InvalidTime;
            _frames[index] = -1;
            return true;
        }

        return false;
    }

    /// <summary>
    /// <c>MediaMachine::Update_Fade</c>: the alpha of the element's surface (its own, or the one it draws into) goes from
    /// the start to the end alpha over the duration (at once when the duration is nearly 0); the entry finishes at the end.
    /// </summary>
    private bool UpdateFade(int index, UiFadeMedia fade)
    {
        var now = _owner.Manager.Now;
        if (_times[index] == InvalidTime)
        {
            _times[index] = now;
        }

        var duration = (double)fade.Duration;
        var t = Math.Abs(duration) > 0.00019999999 ? (float)((now - _times[index]) / duration) : 1f;
        t = t >= 0f ? t : 0f;
        t = t <= 1f ? t : 1f;
        var surface = _owner.SurfaceOwner;
        if (surface is not null)
        {
            var start = (double)fade.StartAlpha;
            surface.SetSurfaceAlpha((float)(((fade.EndAlpha - start) * t) + start));
        }

        if (t == 1f)
        {
            _times[index] = InvalidTime;
            return true;
        }

        return false;
    }
}
