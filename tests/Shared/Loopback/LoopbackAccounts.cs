using Lifestone.Client.Session.Credentials;
using Xunit.Abstractions;

namespace Lifestone.Tests.Shared.Loopback;

/// <summary>
/// Which test account a loopback test logs in on (M4 D31): one account per character, from the one list in code
/// (<see cref="TestAccounts"/>). A character that is on no test account, or none named, uses <c>test</c> as before.
/// </summary>
public static class LoopbackAccounts
{
    /// <summary>The account key (an entry of secrets.json's accounts) that holds <paramref name="characterName"/>; <c>test</c> when none is named or listed.</summary>
    public static string For(string? characterName) =>
        characterName is null ? BackendProbe.TestAccountKey : TestAccounts.AccountOf(characterName) ?? BackendProbe.TestAccountKey;
}

/// <summary>
/// Names the character a loopback test (or its class) logs in with, so the gate also skips it, with a named reason, when that character's account
/// (<c>test-melee</c>, <c>test-archer</c>, <c>test-tools</c>) has no entry in secrets.json. Tests of the characters on <c>test</c> need none: the probe gates on it already.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class LoopbackCharacterAttribute(string name) : Attribute
{
    /// <summary>The character's name.</summary>
    public string Name { get; } = name;

    /// <summary>The character named on the method or else its class, or null.</summary>
    public static string? Of(ITestMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var type = typeof(LoopbackCharacterAttribute).AssemblyQualifiedName!;
        foreach (var attribute in method.Method.GetCustomAttributes(type).Concat(method.TestClass.Class.GetCustomAttributes(type)))
        {
            return attribute.GetConstructorArguments().FirstOrDefault() as string;
        }

        return null;
    }
}
