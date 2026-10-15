using Lifestone.Net.Packets;

namespace Lifestone.Net.Tests.Support;

/// <summary>
/// LoginRequest bodies for tests, written by the product's <see cref="LoginRequest.Write"/> into a fresh array (the
/// vendored tests used OpenAC's removed <c>LoginRequest.Build</c>). Test credentials only: the array is not zeroed.
/// </summary>
internal static class LoginRequestBodies
{
    public static byte[] Build(string account, string password, uint timestamp, string clientVersion = LoginRequest.RetailClientVersion)
    {
        var body = new byte[LoginRequest.GetSize(clientVersion, account, password)];
        var written = LoginRequest.Write(body, clientVersion, account, password, timestamp);
        Assert.Equal(body.Length, written);
        return body;
    }
}
