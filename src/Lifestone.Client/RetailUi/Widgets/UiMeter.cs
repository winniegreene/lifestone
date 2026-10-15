using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Widgets;

/// <summary>
/// The Meter (class 7, retail's <c>UIElement_Meter</c>; vitals, the data check's progress): a level from 0 to 1 (property
/// 0x69) shown either as a frame of an image array (a frame meter, 0x67) or as its fill child (element 2) clipped, or
/// moved, to the level along the child direction (0x6F).
/// </summary>
/// <remarks>
/// <para>
/// Re-expressed from the constructor (11.4186 0x0046F4C0, 11.6096 0x0046F880), <c>Initialize</c> (11.4186 0x0046F7B0,
/// 11.6096 0x0046FB70), <c>OnSetAttribute</c> (11.4186 0x0046FE70, 11.6096 0x00470230), <c>StartAnimation</c> (11.4186
/// 0x0046F5C0, 11.6096 0x0046F980), <c>ListenToGlobalMessage</c> (11.4186 0x0046F870, 11.6096 0x0046FC30),
/// <c>UpdateFrame</c> (11.4186 0x0046F940, 11.6096 0x0046FD00), <c>UpdateChild</c> (11.4186 0x0046F660, 11.6096
/// 0x0046FA20), <c>DrawChildren</c> (11.4186 0x0046FBD0, 11.6096 0x0046FF90) and <c>ResizeTo</c> (11.4186 0x0046FFB0,
/// 11.6096 0x00470370).
/// </para>
/// <para>
/// <b>Level.</b> Setting the goal (0x66) moves the level there: at once, or, with smooth movement (0x6A), linearly over
/// the duration (0x6B) from the current level, one step a frame. A level change of more than 0.0002 broadcasts
/// <see cref="UiMessageIds.MeterPosition"/>; reaching the goal (within 0.0002) <see cref="UiMessageIds.MeterAtGoal"/>.
/// </para>
/// <para>
/// <b>Fill.</b> Directions: 1 fills from the left, 2 from the top, 3 from the right, 4 from the bottom. The fill child is
/// drawn only over the filled part (truncated, as retail converts to integer), or, with move fill (0x68), it slides out
/// of view by the empty part instead. A frame meter shows frame <c>(int)((n − 1) × level + 0.5)</c> of its array (0x6D)
/// with the array's draw mode (0x6E), and no image when the array is missing or the frame is empty.
/// </para>
/// </remarks>
public class UiMeter : UiElement
{
    private const float Epsilon = 0.00019999999f;

    private bool _frameMeter;
    private bool _animating;
    private double _animStartTime;
    private double _animEndTime;
    private float _animStartPosition;
    private float _animEndPosition;
    private float _oldPosition;
    private int _currentFrame = -1;
    private uint _direction = 1;
    private UiElement? _childImage;

    /// <summary>A meter of <paramref name="init"/>: direction 1, no frame shown.</summary>
    public UiMeter(in UiElementInit init)
        : base(init)
    {
    }

    /// <summary>The level (property 0x69), 0 when unset.</summary>
    public float Position => TryGetFloatProperty(UiPropertyIds.MeterPosition, out var p) ? p : 0f;

    /// <summary>The fill direction (property 0x6F, read at initialization; <c>m_eDirection</c>).</summary>
    public uint Direction => _direction;

    /// <summary>True when the meter shows frames (property 0x67, <c>m_framemeter</c>).</summary>
    public bool IsFrameMeter => _frameMeter;

    /// <summary>True while a smooth movement runs.</summary>
    public bool IsAnimating => _animating;

    /// <summary>The fill child (element 2), or null for a frame meter or a meter without one.</summary>
    public UiElement? FillChild => _childImage;

    /// <summary>The frame shown (<c>m_currentFrame</c>), or -1.</summary>
    public int CurrentFrame => _currentFrame;

    /// <summary>Sets the goal (property 0x66): the level moves there, smoothly when the meter says so.</summary>
    public void SetGoal(float goal) => SetFloatProperty(UiPropertyIds.MeterGoalPosition, goal);

    /// <summary>
    /// <c>UIElement_Meter::Initialize</c>: after the base initialization (which applies the desc while the meter is not yet
    /// a frame meter and has no fill child, so the desc's level shows no frame yet, as retail), a meter that is not a frame
    /// meter finds its fill child (element 2) and its direction.
    /// </summary>
    protected internal override void Initialize()
    {
        base.Initialize();
        _frameMeter = TryGetBoolProperty(UiPropertyIds.MeterFrameMeter, out var frame) && frame;
        if (!_frameMeter)
        {
            _childImage = GetChildRecursive(2);
            if (TryGetEnumProperty(UiPropertyIds.MeterChildDirection, out var direction))
            {
                _direction = direction;
            }
        }
    }

    /// <inheritdoc/>
    protected internal override void OnSetAttribute(uint id, UiProperty? value)
    {
        base.OnSetAttribute(id, value);
        switch (id)
        {
            case UiPropertyIds.MeterGoalPosition:
                if (TryGetBoolProperty(UiPropertyIds.MeterSmoothMovement, out var smooth) && smooth)
                {
                    StopAnimation();
                    StartAnimation();
                }
                else
                {
                    SetFloatProperty(UiPropertyIds.MeterPosition, value is UiFloatProperty goal ? goal.Value : 0f);
                }

                break;
            case UiPropertyIds.MeterPosition:
                var position = value is UiFloatProperty p ? p.Value : 0f;
                if (_frameMeter)
                {
                    UpdateFrame();
                }
                else if (_childImage is not null)
                {
                    UpdateChild();
                }

                if (Math.Abs(position - _oldPosition) > Epsilon)
                {
                    _oldPosition = position;
                    BroadcastElementMessage(UiMessageIds.MeterPosition);
                }

                var goalNow = TryGetFloatProperty(UiPropertyIds.MeterGoalPosition, out var g) ? g : 0f;
                if (Math.Abs(position - goalNow) <= Epsilon)
                {
                    BroadcastElementMessage(UiMessageIds.MeterAtGoal);
                }

                break;
        }
    }

    /// <summary><c>UIElement_Meter::StartAnimation</c>: from the level to the goal over the duration (0x6B), when it is not 0.</summary>
    private void StartAnimation()
    {
        if (_animating || !TryGetFloatProperty(UiPropertyIds.MeterSmoothMovementDuration, out var duration) || duration == 0f)
        {
            return;
        }

        _animStartPosition = Position;
        _animEndPosition = TryGetFloatProperty(UiPropertyIds.MeterGoalPosition, out var goal) ? goal : 0f;
        _animStartTime = Manager.Now;
        _animEndTime = duration + _animStartTime;
        _animating = true;
        Manager.RegisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
    }

    private void StopAnimation()
    {
        if (_animating)
        {
            _animating = false;
            Manager.UnregisterForGlobalMessage(this, UiGlobalMessageIds.Frame);
        }
    }

    /// <summary><c>UIElement_Meter::ListenToGlobalMessage</c>: one step of a smooth movement.</summary>
    protected internal override void ListenToGlobalMessage(uint messageId, int data)
    {
        base.ListenToGlobalMessage(messageId, data);
        if (messageId != UiGlobalMessageIds.Frame || !_animating)
        {
            return;
        }

        var t = (float)((Manager.Now - _animStartTime) / (_animEndTime - _animStartTime));
        if (t >= 1f)
        {
            t = 1f;
            StopAnimation();
        }

        SetFloatProperty(UiPropertyIds.MeterPosition, ((_animEndPosition - _animStartPosition) * t) + _animStartPosition);
    }

    /// <summary><c>UIElement_Meter::UpdateFrame</c>: the frame for the level, from the frame array.</summary>
    private void UpdateFrame()
    {
        if (!InqProperty(UiPropertyIds.MeterFrameArray, out var property) || property is not UiArrayProperty frames || frames.Items.Count == 0)
        {
            ClearImage();
            return;
        }

        var drawMode = TryGetEnumProperty(UiPropertyIds.MeterFrameArrayDrawMode, out var mode) ? mode : 1u;
        var frame = (int)((((double)(frames.Items.Count - 1)) * Position) + 0.5);
        if (frame == _currentFrame)
        {
            return;
        }

        _currentFrame = frame;
        if (frame >= 0 && frame < frames.Items.Count && frames.Items[frame] is UiDataIdProperty did && did.Value != 0)
        {
            SetImageByDid(did.Value, (UiDrawMode)drawMode);
        }
        else
        {
            ClearImage();
        }
    }

    /// <summary>
    /// <c>UIElement_Meter::UpdateChild</c>: with move fill (0x68), the fill child slides out by the empty part; otherwise the
    /// meter is repainted (the clip does the rest).
    /// </summary>
    private void UpdateChild()
    {
        if (_childImage is null)
        {
            return;
        }

        if (!(TryGetBoolProperty(UiPropertyIds.MeterMoveFill, out var moveFill) && moveFill))
        {
            MakeRootDirtyHere();
            return;
        }

        var empty = 1.0 - Position;
        switch (_direction)
        {
            case 1:
                _childImage.MoveTo((int)(-Box.Width * empty), 0);
                break;
            case 2:
                _childImage.MoveTo(0, (int)(-Box.Height * empty));
                break;
            case 3:
                _childImage.MoveTo((int)(Box.Width * empty), 0);
                break;
            case 4:
                _childImage.MoveTo(0, (int)(Box.Height * empty));
                break;
            default:
                _childImage.MoveTo(0, 0);
                break;
        }
    }

    /// <summary><c>UIElement_Meter::DrawChildren</c>: the fill child is drawn over the filled part only (unless it moves instead).</summary>
    public override UiBox ClipChild(IUiRegionView child, UiBox childSelf, UiBox childClip)
    {
        if (!ReferenceEquals(child, _childImage) || (TryGetBoolProperty(UiPropertyIds.MeterMoveFill, out var moveFill) && moveFill))
        {
            return childClip;
        }

        var position = Position;
        var fill = childSelf;
        var width = childSelf.X1 - childSelf.X0 + 1;
        var height = childSelf.Y1 - childSelf.Y0 + 1;
        switch (_direction)
        {
            case 1:
                fill = fill with { X1 = (int)(width * (double)position) + childSelf.X0 - 1 };
                break;
            case 2:
                fill = fill with { Y1 = (int)(height * (double)position) + childSelf.Y0 - 1 };
                break;
            case 3:
                var x0 = childSelf.X0 + (int)((1.0 - position) * width);
                fill = fill with { X0 = x0, X1 = (int)(width * (double)position) + x0 - 1 };
                break;
            case 4:
                var y0 = childSelf.Y0 + (int)((1.0 - position) * height);
                fill = fill with { Y0 = y0, Y1 = (int)(height * (double)position) + y0 - 1 };
                break;
        }

        return UiBoxes.TryIntersect(fill, childClip, out var clipped) ? clipped : new UiBox(1, 1, 0, 0);
    }

    /// <summary><c>UIElement_Meter::ResizeTo</c>: the frame or the fill follows the new size.</summary>
    public override void ResizeTo(int width, int height)
    {
        base.ResizeTo(width, height);
        if (_frameMeter)
        {
            UpdateFrame();
        }
        else if (_childImage is not null)
        {
            UpdateChild();
        }
    }
}
