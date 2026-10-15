namespace Lifestone.Client.PortalSpace;

/// <summary>
/// Sends LoginComplete (GameAction 0x00A1) when portal space ends: retail
/// <c>CPlayerSystem::SendLoginCompleteNotification</c> (11.4186 0x00562E90, 11.6096 0x00563C30). The session bridge
/// implements it with the Action API's <c>LoginComplete</c>; offline there is none, and the director counts the sends it
/// suppressed. Retail holds the send until the player's inventory exists (<c>sendLoginCompletePending</c>); Lifestone
/// sends at once (a TODO on <see cref="PortalSessionBridge.SendLoginComplete"/>).
/// </summary>
public interface ILoginCompleteSink
{
    /// <summary>
    /// Sends LoginComplete. The director calls it once per completed teleport: when state 6 ends, or at once when a
    /// teleport completed while the machine was off (retail <c>SmartBox::teleport_occured</c> in state 0).
    /// </summary>
    void SendLoginComplete();
}
