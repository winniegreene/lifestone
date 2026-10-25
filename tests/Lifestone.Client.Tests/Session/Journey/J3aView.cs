using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Diagnostics;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>A session view over <see cref="WorldActionRig"/> for the J3a tests (M4 P5b lane J3a): the read model, the Action API and the clock of the rig.</summary>
internal sealed class J3aView(WorldActionRig rig) : IGameSessionView
{
    public GameState State => rig.H.State;

    public IEventBus Events => rig.H.R.Bus;

    public IActionApi Actions => rig.Api;

    public PlayerTools Tools => rig.Api.Tools;

    public SessionClock Clock => rig.H.R.Clock;

    public ActionLog ActionLog => rig.H.ActionLog;

    public LinkStats Link => default;
}
