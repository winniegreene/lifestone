using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Actions.Trackers;

/// <summary>
/// <see cref="IActionApi.OpenContainer"/> (M4 2.1 s2): GameAction 0x0036 Use, as retail's <c>CM_Inventory::Event_UseEvent</c> sends it (11.6096 0x006AD310). The order ACE answers in was captured live
/// (<c>cache/m4/2-1/capture.md</c> C2, C3; spec §5.4 corrected): a corpse sends GameEvent 0x0196 ViewContents, then UseDone 0, then an F745 per item; a chest sends the 0196, the UseDone 0 and the contents after its
/// F74C On; a locked chest sends F750 {chest, 0x94} and the UseDone 0, no 0196 and no 02EB text. So the tracker confirms from the model: the 0196 is the container's view (<see cref="ContainerViews.Viewed"/>), the open
/// completes when every entry's object has arrived (<see cref="ContainerView.IsResolved"/>; past <see cref="ActionPolicy.LoginCompleteContentsCapSeconds"/> it completes with the missing guids logged), and the
/// UseDone between the two is consumed but is not the end. A UseDone with no view for the container since the send is the refusal: its reason the last 02EB text, or "locked" for the sound 0x94.
/// <para>A container that is already open is Confirmed at once and nothing is sent (a second Use would close it). After the 0196 the tracker holds its lane until its UseDone has arrived (<see cref="HoldsLane"/>), so the UseDone
/// is taken by this open and not by a later action. Retail: the senders of 0x0195 and the handlers of 0x0196 and 0x0052 are not located (<c>TODO(m4-retail-handler-0196)</c>).</para>
/// </summary>
internal sealed class OpenContainerTracker(ActionHandle handle, uint container) : ActionTracker(handle, ActionLane.Use, ActionPolicy.OpenContainerSeconds)
{
    /// <summary>ACE's sound id a locked chest plays when it refuses the use (capture C3: F750 0x94).</summary>
    internal const uint LockedSound = 0x94;

    private ActionApi? _api;
    private bool _useDoneSeen;
    private bool _opened;
    private bool _locked;
    private string? _text;
    private double _viewAt = double.NaN;
    private bool _viewSeen;
    private bool _seen;

    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x0036 Use (open) " + ActionText.Hex(container);

    /// <inheritdoc/>
    internal override bool ConsumesUseDone => true;

    /// <inheritdoc/>
    /// <remarks>
    /// M6 14 recall unblock (m6-container-hold): the hold ends when the UseDone came, when the container's view no longer shows it (a CloseContainer: ACE sent the open's UseDone right behind the 0196, before any close
    /// could reach it, so nothing is waited for), or at <see cref="ActionPolicy.OpenContainerUseDoneSeconds"/> after the view arrived (<see cref="HoldBoundPassed"/>).
    /// </remarks>
    internal override bool HoldsLane => _opened && !_useDoneSeen && _api is { } api && api.State.Containers.ViewedGuid == container;

    /// <inheritdoc/>
    internal override bool HoldBoundPassed(double now) => _opened && !double.IsNaN(_viewAt) && now - _viewAt >= ActionPolicy.OpenContainerUseDoneSeconds;

    /// <inheritdoc/>
    /// <remarks>The container's view is open, and it was not when the open was sent (<see cref="Send"/> sends nothing for a view already open): the 0196 answered this open, and ACE's UseDone follows it (capture C2).</remarks>
    internal override bool ClaimsUseDone(ActionApi api) => api.State.Containers.ViewedGuid == container;

    /// <inheritdoc/>
    internal override void Accepted(ActionApi api) => _seen = api.State.Objects.TryGet(container, out _);

    /// <inheritdoc/>
    /// <remarks>
    /// M5 7 D141's before-check: in the world, and the container, seen when the open was asked, still in the object table (a corpse that rotted while the open waited its turn: ACE's NoObject). The range is not a
    /// precondition (ACE walks the player to it) and a busy lane is a wait, not a refusal.
    /// </remarks>
    internal override string? Refusal(ActionApi api) => WorldTrackers.NotInWorld(api) ?? TargetGone(api, container, _seen, "container");

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        _api = api;
        if (api.State.Containers.ViewedGuid == container)
        {
            api.Log.Info(SessionLog.Category, "the container " + ActionText.Hex(container) + " is already open: nothing sent");
            return new ActionOutcome(ActionOutcomeKind.Confirmed, 0, "already open");
        }

        session!.SendUse(container);
        return null;
    }

    /// <inheritdoc/>
    internal override void Poll(ActionApi api)
    {
        if (Resolution is null && (_viewSeen || !double.IsNaN(_viewAt)) && api.State.Containers.ViewedGuid != container)
        {
            // M6 14 recall unblock (live 2026-11-14 17:24:49 UTC: the loot closed a corpse whose 0196 had come but not its contents; the open stayed in flight 20 s, every cast waited behind it, and its timeout owed a
            // UseDone it had already taken): the view came and is closed now, so the open is over; nothing more will answer it.
            Resolve(new ActionOutcome(
                ActionOutcomeKind.Cancelled,
                0,
                "the container's view (0196) came and was closed before its contents arrived (a CloseContainer): the open ended" + (UseDoneTaken ? " (its UseDone came)" : " (its UseDone was not seen)")));
            return;
        }

        if (Resolution is not null || api.State.Containers.Viewed is not { } view || view.Guid != container)
        {
            return;
        }

        var now = api.Clock.Now;
        if (double.IsNaN(_viewAt))
        {
            _viewAt = now;
        }

        if (view.IsResolved)
        {
            Open(view.Entries.Count + " entries", null);
        }
        else if (now - _viewAt >= ActionPolicy.LoginCompleteContentsCapSeconds)
        {
            var missing = new List<string>();
            foreach (var entry in view.Entries)
            {
                if (!entry.Resolved)
                {
                    missing.Add(ActionText.Hex(entry.Guid));
                }
            }

            var list = string.Join(", ", missing);
            api.Log.Warn(SessionLog.Category, "the container " + ActionText.Hex(container) + " opened but " + ActionText.Count(missing.Count) + " of its entries have no object after " + ActionText.Count((long)ActionPolicy.LoginCompleteContentsCapSeconds) + " s: " + list);
            Open(view.Entries.Count + " entries, " + missing.Count + " without an object yet (" + list + ")", null);
        }
    }

    private void Open(string what, string? note)
    {
        _opened = true;
        Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed, 0, "opened with " + what + note));
    }

    /// <inheritdoc/>
    internal override bool OnUseDone(in UseDone e)
    {
        _useDoneSeen = true;
        if (_api is { } api && api.State.Containers.ViewedGuid == container)
        {
            _viewSeen = true;
            return true; // the 0196 came: this UseDone sits between it and the contents' F745s and is not the end (capture C2)
        }

        var why = _locked ? "the container is locked (it played its sound 0x94; ACE sends no text)" : _text is not null ? _text : "no reason given";
        var reason = _locked ? ActionReason.Locked : e.WeenieError != 0 ? ActionReasons.FromCode(e.WeenieError) : ActionReason.NoEffect; // M5 7 D141: the contrary result, typed
        Resolve(new ActionOutcome(
            ActionOutcomeKind.ServerError,
            e.WeenieError,
            "the server did not open " + ActionText.Hex(container) + ": UseDone " + ActionText.Hex(e.WeenieError) + " with no 0196 ViewContents; " + why,
            reason));
        return true;
    }

    /// <inheritdoc/>
    internal override bool OnChat(in ChatReceived e, ActionApi api)
    {
        if (e.Line.Kind == ChatKind.Transient && e.Line.Text.Length > 0)
        {
            _text = e.Line.Text;
        }
        else if (e.Line.Kind == ChatKind.WeenieError)
        {
            _text = WeenieErrorText.TryFormat(e.Line.ChatType, e.Line.Text, out _, out var line) ? line : e.Line.Text;
        }

        return false; // an observer: other trackers read the same line
    }

    /// <inheritdoc/>
    internal override void OnSound(in ObjectSoundPlayed e, ActionApi api)
    {
        if (e.Guid == container && e.SoundId == LockedSound)
        {
            _locked = true;
        }
    }
}

/// <summary>
/// <see cref="IActionApi.CloseContainer"/> (M4 2.1 s2): GameAction 0x0195 NoLongerViewingContents. ACE answers with GameEvent 0x0052 CloseGroundContainer and no UseDone: at once for a corpse (16 ms), after half the Off
/// animation for a chest (0.42 s; capture C2, C3). Confirmed when the model no longer has the container open (<see cref="ContainerClosed"/> is the reducer's report of the 0052). A container that is not open:
/// Confirmed at once, nothing sent. Retail's sender of 0x0195 is not located (<c>TODO(m4-retail-handler-0196)</c>).
/// </summary>
internal sealed class CloseContainerTracker(ActionHandle handle, uint container) : ActionTracker(handle, ActionLane.Use, ActionPolicy.CloseContainerSeconds)
{
    /// <inheritdoc/>
    internal override string WouldSend => "GameAction 0x0195 NoLongerViewingContents " + ActionText.Hex(container);

    /// <inheritdoc/>
    internal override string? Refusal(ActionApi api) => WorldTrackers.NotInWorld(api);

    /// <inheritdoc/>
    internal override ActionOutcome? Send(IAcSession? session, ActionApi api)
    {
        if (api.State.Containers.ViewedGuid != container)
        {
            api.Log.Info(SessionLog.Category, "the container " + ActionText.Hex(container) + " is not open: nothing sent");
            return new ActionOutcome(ActionOutcomeKind.Confirmed, 0, "not open");
        }

        session!.SendNoLongerViewingContents(container);
        return null;
    }

    /// <inheritdoc/>
    internal override void Poll(ActionApi api)
    {
        if (Resolution is null && api.State.Containers.ViewedGuid != container)
        {
            Resolve(new ActionOutcome(ActionOutcomeKind.Confirmed, 0, "closed"));
        }
    }
}
