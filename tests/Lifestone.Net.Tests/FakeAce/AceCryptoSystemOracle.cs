// Ported from ACEmulator/ACE@95ef426d Source/ACE.Common/Cryptography/CryptoSystem.cs (AGPL-3.0)
// with changes: test-only, in namespace Lifestone.Net.Tests.FakeAce; renamed AceCryptoSystemOracle; it wraps
// Lifestone.Net's IsaacRandom (checked against ACE's ISAAC by the docs/protocol.md §10 vectors) instead of deriving from
// ACE's ISAAC; the byte[] constructor and ReleaseResources are not taken; the skipped-key set is private with a count
// for tests (owner decision D20).

using Lifestone.Net.Crypto;

namespace Lifestone.Net.Tests.FakeAce;

/// <summary>
/// ACE's client-to-server key window (docs/protocol.md §2.5, "Server receive tolerance"), exactly: the fake server
/// recovers each encrypted client packet's ISAAC key and accepts it when it is the current key or one it skipped,
/// searching up to 256 keys ahead (fewer by the number already skipped). The search adds every key it passes to the
/// skipped set, even when it fails, which is how a duplicate packet (whose key is already spent) shifts the window.
/// </summary>
internal sealed class AceCryptoSystemOracle
{
    /// <summary>ACE's <c>MaximumEffortLevel</c>: the window, in keys.</summary>
    public const int MaximumEffortLevel = 256;

    private readonly IsaacRandom _isaac;
    private readonly HashSet<uint> _skipped = [];

    /// <summary>A window over the keystream seeded with <paramref name="seed"/> (the client's seed from ConnectRequest).</summary>
    public AceCryptoSystemOracle(uint seed)
    {
        _isaac = new IsaacRandom(BitConverter.GetBytes(seed));
        CurrentKey = _isaac.Next();
    }

    /// <summary>The key the next in-order packet should carry.</summary>
    public uint CurrentKey { get; private set; }

    /// <summary>The keys passed over and still acceptable.</summary>
    public int SkippedKeyCount => _skipped.Count;

    /// <summary>Spends <paramref name="key"/>: advances past the current key, or removes a skipped one.</summary>
    public void ConsumeKey(uint key)
    {
        if (CurrentKey == key)
        {
            CurrentKey = _isaac.Next();
        }
        else
        {
            _skipped.Remove(key);
        }
    }

    /// <summary>True when <paramref name="key"/> is acceptable; searching ahead moves passed keys into the skipped set.</summary>
    public bool Search(uint key)
    {
        if (CurrentKey == key || _skipped.Contains(key))
        {
            return true;
        }

        var already = _skipped.Count;
        for (var i = 0; i < MaximumEffortLevel - already; i++)
        {
            _skipped.Add(CurrentKey);
            ConsumeKey(CurrentKey);
            if (CurrentKey == key)
            {
                return true;
            }
        }

        return false;
    }
}
