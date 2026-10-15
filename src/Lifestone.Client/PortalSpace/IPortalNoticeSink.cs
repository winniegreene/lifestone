namespace Lifestone.Client.PortalSpace;

/// <summary>
/// Shows a portal-space notice: retail <c>ECM_UI::SendNotice_DisplayStringInfo</c> (11.4186 0x006925B0, 11.6096
/// 0x00693500), which the spew box (<c>gmSpewBoxUI</c>, element class 0x10000016) shows for text type 26 (0x1A). Until
/// the spew box exists, a host shows it as a labelled placeholder line (D32).
/// </summary>
public interface IPortalNoticeSink
{
    /// <summary>Posts <paramref name="literal"/> (retail <c>StringInfo::SetLiteralValue</c>) as text type <paramref name="textType"/>.</summary>
    void PostNotice(string literal, uint textType);
}
