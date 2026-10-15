using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Screens;

/// <summary>The data check's events (retail's <c>DDDEvent</c> numbers, as <c>gmDataPatchUI::OnDDDEvent</c> switches on them).</summary>
public enum DataPatchEvent
{
    /// <summary>0: the server is interrogating the client's data (F7E5): "Looking for data to patch...".</summary>
    Interrogation = 0,

    /// <summary>2: the client waits in the patch queue; the byte count is its place in line.</summary>
    Waiting = 2,

    /// <summary>3: patching starts; the byte count is the total to download.</summary>
    Patching = 3,

    /// <summary>4: patching is done (or there was nothing to patch): the patch meter is full.</summary>
    Done = 4,

    /// <summary>5: more bytes arrived; the byte count is how many.</summary>
    Progress = 5,
}

/// <summary>
/// The data check (retail's <c>gmDataPatchUI</c>, the first mode of <c>gmUIFlow</c>): layout 0x10000001 (<c>patch</c>), root
/// 0x1000041A, a connect meter (0x1000041E) with its label (0x10000420), a patch meter (0x1000041F) with its status text
/// (0x10000421), and a button (0x1000041C) that quits. It fills as the session connects and checks the data, and starts character
/// management when both meters are full and the character list is in (retail starts the intro there; the intro is skipped in M2, D38).
/// </summary>
/// <remarks>
/// <para>
/// Re-expressed from <c>gmDataPatchUI::gmDataPatchUI</c> (11.4186 0x004EE2B0; 11.6096 not in the symbol table, predicted 0x004EEF40,
/// TODO(retailui-data)), <c>SetConnectLevel</c> (11.4186 0x004EDE60, 11.6096 0x004EEAF0), <c>SetPatchLevel</c> (0x004EDEE0, 0x004EEB70),
/// <c>UseTime</c> (0x004EDF10, 0x004EEBA0), <c>OnDDDEvent</c> (0x004EDFF0, 0x004EEC80) and <c>ListenToElementMessage</c> (0x004EDE30,
/// 0x004EEAC0).
/// </para>
/// <para>
/// <b>Meters.</b> The connect meter shows 0 until the connection is up (the data check has started) and then 1; its label takes
/// state 1 at 0, 0x1000003B between and 0x1000003C at 1, as <c>SetConnectLevel</c> does. The patch meter and its text follow the
/// data check: <see cref="DataCheckState.Checking"/> is the interrogation (<see cref="DataPatchEvent.Interrogation"/>),
/// <see cref="DataCheckState.Complete"/> is <see cref="DataPatchEvent.Done"/>. The queue position, the bytes to patch and the progress
/// of a download (<see cref="DataPatchEvent.Waiting"/>, <see cref="DataPatchEvent.Patching"/>, <see cref="DataPatchEvent.Progress"/>)
/// are implemented in <see cref="OnDataEvent"/> but no session raises them: Lifestone does not patch (the dats are the user's own and
/// must match the server's; a mismatch ends the login), TODO(retailui-datapatch).
/// </para>
/// <para>
/// <b>The button</b> (0x1000041C) queues the epilogue, which ends the application, as retail's does.
/// </para>
/// </remarks>
public sealed class DataPatchScreen : UiLoginScreen
{
    /// <summary>The connect meter.</summary>
    public const uint ConnectMeterId = 0x1000041E;

    /// <summary>The patch meter.</summary>
    public const uint PatchMeterId = 0x1000041F;

    /// <summary>The connect meter's label (states 1, 0x1000003B, 0x1000003C).</summary>
    public const uint ConnectTextId = 0x10000420;

    /// <summary>The patch meter's status text.</summary>
    public const uint PatchTextId = 0x10000421;

    /// <summary>The quit button.</summary>
    public const uint QuitButtonId = 0x1000041C;

    /// <summary>The connect label's state while connecting (0 &lt; level &lt; 1).</summary>
    public const uint ConnectingState = 0x1000003B;

    /// <summary>The connect label's state once connected (level 1).</summary>
    public const uint ConnectedState = 0x1000003C;

    private static readonly uint TotalVariable = StringHash.Tokenize("total");
    private static readonly uint PercentVariable = StringHash.Tokenize("percent");

    private readonly UiTextElement? _connectText;
    private readonly UiTextElement? _patchText;
    private readonly UiMeter? _connectMeter;
    private readonly UiMeter? _patchMeter;
    private readonly UiStringVariables _variables = new();
    private DataCheckState _shown = DataCheckState.NotStarted;
    private long _expected = -1;
    private long _received;
    private bool _starting;

    /// <summary>The data check over <paramref name="context"/>: both meters at 0 (<c>SetConnectLevel(0)</c> and the patch level).</summary>
    public DataPatchScreen(UiLoginContext context)
        : base(context, UiLayoutEnums.DataPatchRoot, "gmDataPatchUI")
    {
        _connectText = Find<UiTextElement>(ConnectTextId);
        _patchText = Find<UiTextElement>(PatchTextId);
        _connectMeter = Find<UiMeter>(ConnectMeterId);
        _patchMeter = Find<UiMeter>(PatchMeterId);
        SetConnectLevel(0f);
        SetPatchLevel(0f);
    }

    /// <summary>The connect level (<c>m_fConnectLevel</c>): -1 before the constructor sets it, then 0 or 1.</summary>
    public float ConnectLevel { get; private set; } = -1f;

    /// <summary>The patch level (<c>m_fPatchLevel</c>), 0 to 1.</summary>
    public float PatchLevel { get; private set; } = -1f;

    /// <summary>The patch status text now shown, resolved from the strings (empty before any).</summary>
    public string StatusText => TextOf(_patchText);

    /// <summary>
    /// <c>gmDataPatchUI::SetConnectLevel</c>: a level other than the current one moves the connect meter, and the label takes state 1 at
    /// 0, <see cref="ConnectingState"/> above 0 and <see cref="ConnectedState"/> from 1.
    /// </summary>
    public void SetConnectLevel(float level)
    {
        if (level == ConnectLevel)
        {
            return;
        }

        ConnectLevel = level;
        _connectMeter?.SetGoal(level);
        var state = UiStateIds.Normal;
        if (level > 0f)
        {
            state = level >= 1f ? ConnectedState : ConnectingState;
        }

        _connectText?.SetState(state);
    }

    /// <summary><c>gmDataPatchUI::SetPatchLevel</c>: a level other than the current one moves the patch meter.</summary>
    public void SetPatchLevel(float level)
    {
        if (level == PatchLevel)
        {
            return;
        }

        PatchLevel = level;
        _patchMeter?.SetGoal(level);
    }

    /// <summary>
    /// <c>gmDataPatchUI::OnDDDEvent</c>: one data-check event. The interrogation and the queue place show only while no download is
    /// expected; patching sets the expected total; progress adds the bytes and moves the patch meter (a total of 0 is full), with the
    /// text "percent % of total K complete..."; done fills the meter (an unset total becomes 0) and says so.
    /// </summary>
    /// <param name="kind">The event.</param>
    /// <param name="bytes">Its byte count (the place in line, the total, or the bytes that arrived).</param>
    public void OnDataEvent(DataPatchEvent kind, uint bytes = 0)
    {
        switch (kind)
        {
            case DataPatchEvent.Interrogation:
                if (_expected == -1)
                {
                    ShowStatus("ID_DataPatch_Interrogation");
                }

                break;
            case DataPatchEvent.Waiting:
                if (_expected == -1)
                {
                    ShowStatus("ID_DataPatch_Waiting", (TotalVariable, bytes));
                }

                break;
            case DataPatchEvent.Patching:
                _expected = bytes;
                ShowStatus("ID_DataPatch_Patching");
                break;
            case DataPatchEvent.Progress:
                _received += bytes;
                if (_expected != -1)
                {
                    var progress = _expected == 0 ? 1.0 : Math.Clamp((double)_received / _expected, 0.0, 1.0);
                    SetPatchLevel((float)progress);
                    ShowStatus("ID_DataPatch_PatchProgress", (PercentVariable, (uint)(PatchLevel * 100.0)), (TotalVariable, (uint)(_expected >> 10)));
                }

                break;
            case DataPatchEvent.Done:
                if (_expected == -1)
                {
                    _expected = 0;
                }

                _received = _expected;
                SetPatchLevel(1f);
                ShowStatus("ID_DataPatch_PatchingDone");
                break;
        }
    }

    /// <summary>
    /// <c>gmDataPatchUI::UseTime</c> for this frame: follows the session's data check, fills the connect meter once connected, and
    /// starts character management when both meters are full and the character list has arrived (D38: the intro is skipped).
    /// </summary>
    public override void Update()
    {
        var state = Session.State;
        var check = state.DataCheck;
        if (check != _shown)
        {
            _shown = check;
            switch (check)
            {
                case DataCheckState.Checking:
                    OnDataEvent(DataPatchEvent.Interrogation);
                    break;
                case DataCheckState.Complete:
                    OnDataEvent(DataPatchEvent.Done);
                    break;
                case DataCheckState.Mismatch:
                    Manager.LogOnce(
                        "datapatch:mismatch",
                        LogSeverity.Error,
                        "TODO(retailui-datapatch): the server says the dats do not match its own and Lifestone does not patch; the login ends in a failure");
                    break;
            }
        }

        if (ConnectLevel < 1f && check != DataCheckState.NotStarted)
        {
            SetConnectLevel(1f);
        }

        if (!_starting && ConnectLevel >= 1f && PatchLevel >= 1f && state.Characters.Version > 0)
        {
            // Retail queues the intro (0x10000001), whose movies end in character management; the intro is skipped (D38).
            _starting = true;
            Context.Flow.QueueUIMode(UiModes.CharacterManagement);
        }
    }

    /// <inheritdoc/>
    protected override UiListenResult OnElementMessage(in UiElementMessage message)
    {
        if (message.MessageId == UiMessageIds.ButtonClick && message.ElementId == QuitButtonId)
        {
            Context.Flow.QueueUIMode(UiModes.Epilogue);
        }

        return UiListenResult.Default;
    }

    private void ShowStatus(string token, params (uint Id, uint Value)[] variables)
    {
        if (_patchText is null)
        {
            return;
        }

        var language = Manager.Services.Strings?.Language;
        foreach (var (id, value) in variables)
        {
            if (language is not null)
            {
                _variables.SetInteger(id, value, language);
            }
            else
            {
                _variables.SetText(id, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        _patchText.SetStringInfo(Pregame(token), _variables);
    }
}
