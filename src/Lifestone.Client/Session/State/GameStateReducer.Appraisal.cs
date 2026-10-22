using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.State;

/// <summary>
/// The appraisal model (M4 3.2 s2): GameEvent 0x00C9 IdentifyObjectResponse, the answer to GameAction 0x00C8 Identify, kept per guid in <see cref="GameState.Appraisals"/> (a bounded cache) and announced as
/// <see cref="AppraisalReceived"/>.
/// </summary>
/// <remarks>
/// <para>Nothing is unsolicited (spec 5.6): ACE answers each 0x00C8 with one 0x00C9, 1 to 10 ms later (capture.md C3), for a guid it cannot find too (flags 0, success 0), so the model keeps every answer and the
/// <see cref="Actions.Trackers.IdentifyTracker"/> reads it back by guid. A success of false is a result: the tables are kept and the profiles are simply not there (ACE withholds them, <c>AppraiseInfo.cs:334-345, 556-597</c>).
/// Retail's handler of the 0x00C9 is not read; the candidate is <c>CM_Item::DispatchUI_AppraiseDone</c> 11.6096 0x006A9400 (net.md: its unpacker is <c>AppraisalProfile::UnPack</c> 11.6096 0x005B4A30 and its consumer <c>gmExaminationUI::RecvNotice_SetAppraiseInfo</c> 11.6096 0x004B6E20), so the model
/// re-expresses the behaviour, not a function: <c>TODO(m4-retail-handler-00c9)</c>.</para>
/// </remarks>
internal sealed partial class GameStateReducer
{
    /// <summary>GE 0x00C9: the appraisal of an object. See the class remarks.</summary>
    public void OnIdentifyResponse(in InboundContext context, in GameEventHeader header, IdentifyResponseMsg message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var appraisal = _state.Appraisals.Add(message, _clock.Now);
        Changed();
        if (_log.IsEnabled(LogSeverity.Debug))
        {
            _log.Debug(
                SessionLog.Category,
                "appraisal received",
                new LogField("guid", appraisal.Guid),
                new LogField("success", appraisal.Success),
                new LogField("flags", (uint)appraisal.Flags),
                new LogField("sequence", appraisal.Sequence));
        }

        Publish(new AppraisalReceived(appraisal.Guid, appraisal.Success, appraisal.Flags, appraisal.Sequence));
    }
}
