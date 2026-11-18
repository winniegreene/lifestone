using System.Runtime.CompilerServices;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Quartermaster;

/// <summary>
/// M6 16 E5c2 (docs/automation-engine.md section 2.5, E5; <c>TODO(engine-sale-copies)</c>): the quartermaster's sale desk, one a session: what the job states for the rule book's verdict (<see cref="SaleVerdict"/>) outside a
/// trip, and the verdict's context for a ground as the session stands (<see cref="BookOn"/>).
/// <para><b>The statement</b> (<see cref="StateJob"/>). A place that asks the verdict outside a trip (the trip trigger's sale estimate, the start-up audit's sale gate, the field's make-room rule) knows the session's read model
/// and a ground; who she is, the SpellTable and the job's facts (the every-spell switch, the learn plan's order lines) are the job's. The outer loop states them once a session's read
/// model; a station that runs with no job states its own when none stands (<see cref="StateIfNone"/>).</para>
/// <para><b>Not stated.</b> The context is then made over an empty SpellTable and with no facts: no spell is named, so a cantrip is not read by name, her book's burns are not known (the components rule then keeps every
/// component: <c>components-book-unread</c>) and the every-spell rule keeps nothing. That is what a test's own counters read; the outer loop and the stations state themselves (<see cref="JobStated"/>).</para>
/// </summary>
public sealed class SaleDesk
{
    private static readonly ConditionalWeakTable<GameState, SaleDesk> PerSession = new();
    private static readonly IReadOnlyDictionary<uint, SpellInfo> NoSpells = new Dictionary<uint, SpellInfo>();
    private readonly GameState _state;

    private SaleDesk(GameState state) => _state = state;

    /// <summary>The sale desk of a session, made on first use.</summary>
    /// <param name="state">The session's read model.</param>
    public static SaleDesk For(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return PerSession.GetValue(state, static s => new SaleDesk(s));
    }

    /// <summary>The character's name the job stated (the keep-aside default), or null.</summary>
    public string? Character { get; private set; }

    /// <summary>The SpellTable's spells the job stated, or an empty table.</summary>
    public IReadOnlyDictionary<uint, SpellInfo> Spells { get; private set; } = NoSpells;

    /// <summary>The job's facts (the every-spell switch, the learn plan's order lines), or null.</summary>
    public JourneyFacts? Facts { get; private set; }

    /// <summary>True once a job or a station stated itself.</summary>
    public bool JobStated { get; private set; }

    /// <summary>The job states who she is, the SpellTable it reads and its facts, once a session's read model.</summary>
    /// <param name="character">The character's name, or null.</param>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="facts">The job's facts: read live at each ask, never copied.</param>
    public void StateJob(string? character, IReadOnlyDictionary<uint, SpellInfo> spells, JourneyFacts facts)
    {
        ArgumentNullException.ThrowIfNull(spells);
        ArgumentNullException.ThrowIfNull(facts);
        (Character, Spells, Facts, JobStated) = (character, spells, facts, true);
    }

    /// <summary>A station that runs with no job states its own context; a statement that stands is left as it is.</summary>
    /// <param name="character">The character's name, or null.</param>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="facts">The station's facts.</param>
    public void StateIfNone(string? character, IReadOnlyDictionary<uint, SpellInfo> spells, JourneyFacts facts)
    {
        if (!JobStated)
        {
            StateJob(character, spells, facts);
        }
    }

    /// <summary>The verdict's context over <paramref name="ground"/> as the session stands now (<see cref="SaleContext.For"/>): made afresh at each ask, so every part reads the pack of the ask.</summary>
    /// <param name="ground">The ground.</param>
    public SaleContext BookOn(HuntingGround ground) => SaleContext.For(_state, ground, Character ?? _state.Self.Name, Spells);
}
