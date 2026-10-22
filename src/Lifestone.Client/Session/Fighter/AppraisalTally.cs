using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Fighter;

/// <summary>
/// The hunt's appraisal counters (M4 3.2 s2): plain numbers a driver feeds with <see cref="Record"/>, one line a finished Identify handle, so the hunt's verdict reads <c>appraised=</c> (<see cref="Appraised"/>). Only a handle that
/// ended <see cref="ActionOutcomeKind.Confirmed"/> with a <see cref="IdentifyHandle.Result"/> counts; a refused or timed-out Identify is not counted here (the driver sees its outcome). Game thread only; nothing allocates.
/// </summary>
public sealed class AppraisalTally
{
    /// <summary>Identifies the server appraised (success 1): what the hunt's <c>appraised=</c> reports.</summary>
    public int Appraised { get; private set; }

    /// <summary>Identifies whose roll failed (<see cref="IdentifyResultKind.Failed"/>): the answer came with tables and no profiles.</summary>
    public int Failed { get; private set; }

    /// <summary>Of <see cref="Failed"/>, the ones that were the 5 s cooldown's answer, not a new roll (<see cref="IdentifyResult.Cooldown"/>).</summary>
    public int Cooldowns { get; private set; }

    /// <summary>Identifies for a guid the server could not find (<see cref="IdentifyResultKind.NotFound"/>).</summary>
    public int NotFound { get; private set; }

    /// <summary>Every Identify counted.</summary>
    public int Answered => Appraised + Failed + NotFound;

    /// <summary>Counts one finished Identify. A handle that is not completed, did not end Confirmed or carries no result adds nothing.</summary>
    /// <param name="handle">The handle of <see cref="IActionApi.Identify"/>.</param>
    public void Record(IdentifyHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (handle.Outcome is not { Kind: ActionOutcomeKind.Confirmed } || handle.Result is not { } result)
        {
            return;
        }

        switch (result.Kind)
        {
            case IdentifyResultKind.Appraised:
                Appraised++;
                break;
            case IdentifyResultKind.Failed:
                Failed++;
                if (result.Cooldown)
                {
                    Cooldowns++;
                }

                break;
            case IdentifyResultKind.NotFound:
                NotFound++;
                break;
        }
    }
}
