using System.Reflection;
using Lifestone.ServerTools;

namespace Lifestone.ServerMod;

/// <summary>
/// What this build of the mod is: its version, and the ACE build it was compiled against, embedded by the project's
/// <c>LifestoneCheckAce</c> target as assembly metadata.
/// </summary>
internal static class ModInfo
{
    /// <summary>The mod's version (the project's Version, equal to Meta.json's).</summary>
    public static string Version { get; } =
        typeof(ModInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    /// <summary>SHA-256 (lower-case hex) of the ACE.Server.dll the mod was compiled against; the only one it loads into.</summary>
    public static string AceServerDllSha256 { get; } = Metadata("AceServerDllSha256");

    /// <summary>The ACE commit the mod was compiled against (40 hex digits).</summary>
    public static string AceCommit { get; } = Metadata("AceCommit");

    /// <summary>The build key setup recorded (m2-plan SM4), or <c>none</c> for a compile check.</summary>
    public static string BuildKey { get; } = Metadata("LifestoneModBuildKey");

    /// <summary>The ACE commit as log lines show it (8 characters).</summary>
    public static string AceShort => ToolsWire.ShortCommit(AceCommit);

    private static string Metadata(string key) =>
        typeof(ModInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value ?? string.Empty;
}

/// <summary>
/// The mod's lines in ACE's log (ACE_Log.txt, through ModManager.Log, logger ACE.Server.Mods.ModManager). They are a
/// contract (m2-plan §4.12): server\start-server.ps1 waits for <see cref="Ready"/> and fails fast on
/// <see cref="Refused"/>, and the smoke steps count them. Change them only with their readers.
/// </summary>
internal static class ModLog
{
    /// <summary>Starts the line logged when Initialize accepts the host: <c>Lifestone mod loaded: version 0.1.0, wire 1, ACE 95ef426d</c>.</summary>
    public const string Loaded = "Lifestone mod loaded: ";

    /// <summary>Starts the line logged (as an error) before Initialize throws: <c>Lifestone mod refused to load: &lt;reason&gt;</c>.</summary>
    public const string Refused = "Lifestone mod refused to load: ";

    /// <summary>The line logged once ACE's AutoPromoteNextAccountToAdmin has been cleared.</summary>
    public const string AutoPromotionOff = "Lifestone: ACE's first-account Admin auto-promotion is off; new accounts get DefaultAccessLevel 0 (Player).";

    /// <summary>Starts the line logged when the mod is ready: <c>Lifestone mod ready: @lifestone on 127.0.0.1:9000; wire 1; mod 0.1.0; ACE 95ef426d; auto-promotion off</c>.</summary>
    public const string Ready = "Lifestone mod ready: ";

    /// <summary>Starts the one line logged per request: <c>Lifestone tool: account=… character=… verb=… id=… args=… -&gt; ok|&lt;code&gt;</c>.</summary>
    public const string Tool = "Lifestone tool: ";

    /// <summary>Starts the arrival guard's lines (<see cref="ArrivalGuard"/>).</summary>
    public const string ArrivalGuard = "Lifestone arrival guard: ";

    /// <summary>Starts the coin rule's line (<see cref="CorpseCoin"/>).</summary>
    public const string CorpseCoin = "Lifestone corpse coin: ";

    /// <summary>Starts the house account-age rule's line (<see cref="HouseAccountAge"/>).</summary>
    public const string HouseAccountAge = "Lifestone house account age: ";

    /// <summary>Starts the buff bot's lines (<see cref="BuffBot"/>).</summary>
    public const string BuffBot = "Lifestone buff bot: ";

    /// <summary>Starts a reply to a command typed at ACE's console, which has no session to send it to.</summary>
    public const string ConsoleReply = "Lifestone console: ";
}
