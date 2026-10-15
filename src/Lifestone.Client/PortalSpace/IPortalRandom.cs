namespace Lifestone.Client.PortalSpace;

/// <summary>
/// The random source of the tunnel roll: retail <c>RandDouble</c> (11.4186 0x006843D0, 11.6096 0x006853D0) over the C
/// runtime's <c>rand()</c>. <see cref="Audio.MsvcCrtRandom"/> is the retail implementation; tests may script values.
/// </summary>
public interface IPortalRandom
{
    /// <summary>
    /// A value in [<paramref name="min"/>, <paramref name="max"/>], both ends included (retail's <c>rand()</c> reaches
    /// 32767, which gives <paramref name="max"/>).
    /// </summary>
    double RandDouble(double min, double max);
}
