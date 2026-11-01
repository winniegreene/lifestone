using System.Globalization;

namespace Lifestone.Client.Session.Contracts;

/// <summary>The server mod's verbs that change the world for the character (the cheat ledger of the M5 spec, section 5.6); a status, where, quest or purge request is none of them.</summary>
public enum ModVerb
{
    /// <summary><c>item</c>: <see cref="PlayerTools.SpawnItem"/> and, one per grant, <see cref="PlayerTools.SpawnGear"/>.</summary>
    Item,

    /// <summary><c>xp</c>: <see cref="PlayerTools.AddXp"/>.</summary>
    Xp,

    /// <summary><c>place</c>: <see cref="PlayerTools.Place"/>.</summary>
    Place,

    /// <summary><c>corpses rot</c>: <see cref="PlayerTools.RotCorpses"/>.</summary>
    CorpsesRot,

    /// <summary><c>spell learn</c>: <see cref="PlayerTools.LearnSpells"/>.</summary>
    SpellLearn,
}

/// <summary>
/// What the mod's verbs have been asked for, by verb (M5 item 3 c1, DoD 6): a value, so a mark taken at the end of the build and the counts now give the play's delta (<see cref="Since"/>).
/// A place sent inside <see cref="ModVerbCounter.RecoveryPlaces"/> is <see cref="RecoveryPlace"/> and never <see cref="Place"/> (a recovery place is outside the budget).
/// </summary>
/// <param name="Item">The <c>item</c> requests.</param>
/// <param name="Xp">The <c>xp</c> requests.</param>
/// <param name="Place">The <c>place</c> requests that are not recovery places.</param>
/// <param name="CorpsesRot">The <c>corpses rot</c> requests.</param>
/// <param name="SpellLearn">The <c>spell learn</c> requests.</param>
/// <param name="RecoveryPlace">The <c>place</c> requests made as a named recovery.</param>
public readonly record struct ModVerbCounts(int Item = 0, int Xp = 0, int Place = 0, int CorpsesRot = 0, int SpellLearn = 0, int RecoveryPlace = 0)
{
    /// <summary>
    /// The grants, the journey's <c>granted=</c> (M5 3 F2, closes 3-R8): every mod verb but <c>place</c>, that is the <c>item</c> and <c>xp</c> requests and also <c>corpses rot</c> and <c>spell learn</c>, which DoD 6 names beside them. A <c>place</c> is the
    /// journey's <c>places=</c> (<see cref="Place"/>), and a recovery place is counted apart (<see cref="RecoveryPlace"/>).
    /// </summary>
    public int Granted => Item + Xp + CorpsesRot + SpellLearn;

    /// <summary>Every verb counted, the recovery places included.</summary>
    public int Total => Item + Xp + Place + CorpsesRot + SpellLearn + RecoveryPlace;

    /// <summary>The counts after <paramref name="mark"/>: this minus the mark, verb by verb.</summary>
    /// <param name="mark">Counts taken earlier from the same counter.</param>
    public ModVerbCounts Since(ModVerbCounts mark) =>
        new(Item - mark.Item, Xp - mark.Xp, Place - mark.Place, CorpsesRot - mark.CorpsesRot, SpellLearn - mark.SpellLearn, RecoveryPlace - mark.RecoveryPlace);

    /// <summary>The counts as one log token run: <c>item=0 xp=0 place=0 corpses_rot=0 spell_learn=0 recovery_place=0</c>.</summary>
    public string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"item={Item} xp={Xp} place={Place} corpses_rot={CorpsesRot} spell_learn={SpellLearn} recovery_place={RecoveryPlace}");
}

/// <summary>
/// The one counter of the server mod's verbs a session sends (M5 item 3 c1; <see cref="PlayerTools.Verbs"/>): <see cref="PlayerTools"/> counts a request at its one send path when it is accepted (a request refused at the gate, a non-loopback server or the
/// kill switch is not a send), so every caller, scenario, station and panel is counted the same way and none counts for itself. The play's rule is that nothing is granted or placed after the build (spec 5.6); the journey marks the counts at the end of its
/// build-verify station and reads the delta. Game thread only.
/// </summary>
public sealed class ModVerbCounter
{
    private int _item;
    private int _xp;
    private int _place;
    private int _corpsesRot;
    private int _spellLearn;
    private int _recoveryPlace;
    private int _recoveryDepth;

    /// <summary>The counts now.</summary>
    public ModVerbCounts Counts => new(_item, _xp, _place, _corpsesRot, _spellLearn, _recoveryPlace);

    /// <summary>The count of one verb now (<see cref="ModVerb.Place"/> without the recovery places).</summary>
    /// <param name="verb">The verb.</param>
    public int Of(ModVerb verb) => verb switch
    {
        ModVerb.Item => _item,
        ModVerb.Xp => _xp,
        ModVerb.Place => _place,
        ModVerb.CorpsesRot => _corpsesRot,
        ModVerb.SpellLearn => _spellLearn,
        _ => throw new ArgumentOutOfRangeException(nameof(verb)),
    };

    /// <summary>The counts since <paramref name="mark"/>.</summary>
    /// <param name="mark">Counts taken earlier (<see cref="Counts"/>).</param>
    public ModVerbCounts Since(ModVerbCounts mark) => Counts.Since(mark);

    /// <summary>
    /// Opens a scope in which a <c>place</c> is a recovery place (the journey's pre-journey and failure recoveries, outside its budget): counted in <see cref="ModVerbCounts.RecoveryPlace"/>, never in <see cref="ModVerbCounts.Place"/>.
    /// Dispose it right after the send; scopes nest.
    /// </summary>
    public RecoveryScope RecoveryPlaces()
    {
        _recoveryDepth++;
        return new RecoveryScope(this);
    }

    /// <summary>The verb an action kind sends, or false for a kind that is not one of the counted verbs.</summary>
    /// <param name="kind">The request's kind.</param>
    /// <param name="verb">The verb.</param>
    public static bool TryVerbOf(ActionKind kind, out ModVerb verb)
    {
        switch (kind)
        {
            case ActionKind.ToolItem:
                verb = ModVerb.Item;
                return true;
            case ActionKind.ToolAddXp:
                verb = ModVerb.Xp;
                return true;
            case ActionKind.ToolPlace:
                verb = ModVerb.Place;
                return true;
            case ActionKind.ToolCorpsesRot:
                verb = ModVerb.CorpsesRot;
                return true;
            case ActionKind.ToolSpell:
                verb = ModVerb.SpellLearn;
                return true;
            default:
                verb = default;
                return false;
        }
    }

    /// <summary>Counts one request of <paramref name="kind"/>; a kind that is not a counted verb is ignored. Only <see cref="PlayerTools"/> calls it.</summary>
    /// <param name="kind">The request's kind.</param>
    internal void Record(ActionKind kind)
    {
        if (!TryVerbOf(kind, out var verb))
        {
            return;
        }

        switch (verb)
        {
            case ModVerb.Item:
                _item++;
                break;
            case ModVerb.Xp:
                _xp++;
                break;
            case ModVerb.Place when _recoveryDepth > 0:
                _recoveryPlace++;
                break;
            case ModVerb.Place:
                _place++;
                break;
            case ModVerb.CorpsesRot:
                _corpsesRot++;
                break;
            default:
                _spellLearn++;
                break;
        }
    }

    private void EndRecovery() => _recoveryDepth = Math.Max(0, _recoveryDepth - 1);

    /// <summary>The scope <see cref="RecoveryPlaces"/> opens; disposing it closes it.</summary>
    public readonly struct RecoveryScope : IDisposable
    {
        private readonly ModVerbCounter _counter;

        internal RecoveryScope(ModVerbCounter counter) => _counter = counter;

        /// <inheritdoc />
        public void Dispose() => _counter?.EndRecovery();
    }
}
