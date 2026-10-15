namespace Lifestone.Data.Ui;

/// <summary>
/// The kind of a StateDesc media entry: retail's media type numbers, as <c>MediaDesc::CreateMediaType</c> (11.4186
/// 0x0069D420, 11.6096 0x0069E230) switches on them.
/// </summary>
/// <remarks>
/// 12 is DatReaderWriter's <c>Stretch</c>: retail's switch has no case for it (it creates nothing) and DatReaderWriter has
/// no decoder for it, so a media entry of that type would leave the rest of its layout misread. The retail layouts hold
/// none (UiLayoutCensusTests), and <see cref="UiDescConverter"/> fails loudly on one.
/// </remarks>
public enum UiMediaType : uint
{
    /// <summary>1: a movie file (<c>MD_Data_Movie</c>).</summary>
    Movie = 1,

    /// <summary>2: an alpha mask image (<c>MD_Data_Alpha</c>).</summary>
    Alpha = 2,

    /// <summary>3: an image animation (<c>MD_Data_Anim</c>).</summary>
    Animation = 3,

    /// <summary>4: a cursor (<c>MD_Data_Cursor</c>).</summary>
    Cursor = 4,

    /// <summary>5: an image (<c>MD_Data_Image</c>).</summary>
    Image = 5,

    /// <summary>6: a jump to another media entry (<c>MD_Data_Jump</c>).</summary>
    Jump = 6,

    /// <summary>7: a UI message (<c>MD_Data_Message</c>).</summary>
    Message = 7,

    /// <summary>8: a pause (<c>MD_Data_Pause</c>).</summary>
    Pause = 8,

    /// <summary>9: a sound (<c>MD_Data_Sound</c>).</summary>
    Sound = 9,

    /// <summary>10: a change of UI state (<c>MD_Data_State</c>).</summary>
    State = 10,

    /// <summary>11: an alpha fade (<c>MD_Data_Fade</c>).</summary>
    Fade = 11,
}

/// <summary>
/// An image's draw mode (<c>m_drawMode</c> of <c>MD_Data_Image</c> and <c>MD_Data_Anim</c>). The names are
/// DatReaderWriter's labels; how each mode blits (<c>UIRegion::SetImageByDID</c>) is the compositor's (R11). The retail
/// layouts use Normal (842 images, and both animations) and AlphaBlend (468 images) only (UiLayoutCensusTests).
/// </summary>
public enum UiDrawMode : uint
{
    /// <summary>0.</summary>
    Undefined = 0,

    /// <summary>1.</summary>
    Normal = 1,

    /// <summary>2.</summary>
    Overlay = 2,

    /// <summary>3.</summary>
    AlphaBlend = 3,
}

/// <summary>
/// One media entry of a StateDesc, in the order the state lists them (<c>StateDesc::ConcatenateMedia</c> appends a
/// derived desc's media after its base's). Retail's <c>MediaDesc</c> with its <c>MD_Data_*</c> payloads, whose fields
/// are those each <c>MD_Data_*::Serialize</c> reads.
/// </summary>
public abstract record UiMedia
{
    /// <summary>The media type.</summary>
    public abstract UiMediaType Type { get; }
}

/// <summary>An image: a RenderSurface (0x06xxxxxx) and how to draw it.</summary>
/// <param name="File">The image's DID (<c>m_file</c>).</param>
/// <param name="DrawMode">How to draw it (<c>m_drawMode</c>).</param>
public sealed record UiImageMedia(uint File, UiDrawMode DrawMode) : UiMedia
{
    /// <inheritdoc />
    public override UiMediaType Type => UiMediaType.Image;
}

/// <summary>An alpha mask image.</summary>
/// <param name="File">The mask's DID (<c>m_file</c>).</param>
public sealed record UiAlphaMedia(uint File) : UiMedia
{
    /// <inheritdoc />
    public override UiMediaType Type => UiMediaType.Alpha;
}

/// <summary>A cursor image and its hot spot.</summary>
/// <param name="File">The cursor image's DID.</param>
/// <param name="HotspotX">The hot spot's column in the image.</param>
/// <param name="HotspotY">The hot spot's row in the image.</param>
public sealed record UiCursorMedia(uint File, uint HotspotX, uint HotspotY) : UiMedia
{
    /// <inheritdoc />
    public override UiMediaType Type => UiMediaType.Cursor;
}

/// <summary>A sound from a SoundTable.</summary>
/// <param name="File">The SoundTable's DID (<c>m_file</c>).</param>
/// <param name="Sound">The sound type in that table (<c>m_stype</c>).</param>
public sealed record UiSoundMedia(uint File, uint Sound) : UiMedia
{
    /// <inheritdoc />
    public override UiMediaType Type => UiMediaType.Sound;
}

/// <summary>A change to another UI state, with a probability.</summary>
/// <param name="StateId">The state to change to (<c>m_stateID</c>).</param>
/// <param name="Probability">The chance it happens (<c>m_probability</c>).</param>
public sealed record UiStateMedia(uint StateId, float Probability) : UiMedia
{
    /// <inheritdoc />
    public override UiMediaType Type => UiMediaType.State;
}

/// <summary>A jump to another media entry of the same state, with a probability.</summary>
/// <param name="JumpItemIndex">The media index to jump to (<c>m_jumpItemIndex</c>).</param>
/// <param name="Probability">The chance it happens (<c>m_probability</c>).</param>
public sealed record UiJumpMedia(uint JumpItemIndex, float Probability) : UiMedia
{
    /// <inheritdoc />
    public override UiMediaType Type => UiMediaType.Jump;
}

/// <summary>A UI message, with a probability.</summary>
/// <param name="MessageId">The message (<c>m_messageID</c>).</param>
/// <param name="Probability">The chance it is sent (<c>m_probability</c>).</param>
public sealed record UiMessageMedia(uint MessageId, float Probability) : UiMedia
{
    /// <inheritdoc />
    public override UiMediaType Type => UiMediaType.Message;
}

/// <summary>A pause of a random length.</summary>
/// <param name="MinDuration">The shortest pause in seconds (<c>m_minDuration</c>).</param>
/// <param name="MaxDuration">The longest pause in seconds (<c>m_maxDuration</c>).</param>
public sealed record UiPauseMedia(float MinDuration, float MaxDuration) : UiMedia
{
    /// <inheritdoc />
    public override UiMediaType Type => UiMediaType.Pause;
}

/// <summary>An alpha fade.</summary>
/// <param name="StartAlpha">The alpha at the start (<c>m_startAlpha</c>).</param>
/// <param name="EndAlpha">The alpha at the end (<c>m_endAlpha</c>).</param>
/// <param name="Duration">The fade's length in seconds (<c>m_duration</c>).</param>
public sealed record UiFadeMedia(float StartAlpha, float EndAlpha, float Duration) : UiMedia
{
    /// <inheritdoc />
    public override UiMediaType Type => UiMediaType.Fade;
}

/// <summary>An image animation: frames shown over a duration.</summary>
/// <param name="Duration">The animation's length in seconds (<c>m_duration</c>).</param>
/// <param name="DrawMode">How to draw the frames (<c>m_drawMode</c>).</param>
/// <param name="Frames">The frame images' DIDs (<c>m_frames</c>).</param>
public sealed record UiAnimationMedia(float Duration, UiDrawMode DrawMode, IReadOnlyList<uint> Frames) : UiMedia
{
    /// <inheritdoc />
    public override UiMediaType Type => UiMediaType.Animation;
}

/// <summary>A movie file (the intro and credits; not shown in M2).</summary>
/// <param name="FileName">The movie's file name (<c>m_strFileName</c>).</param>
/// <param name="StretchToFullScreen">Whether it fills the screen (<c>m_StretchToFullScreen</c>).</param>
public sealed record UiMovieMedia(string FileName, bool StretchToFullScreen) : UiMedia
{
    /// <inheritdoc />
    public override UiMediaType Type => UiMediaType.Movie;
}
