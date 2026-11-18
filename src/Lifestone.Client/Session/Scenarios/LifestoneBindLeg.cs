using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.World.Data;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M5 7 F30 (soak round 6: a mage re-created by the build and never bound to a lifestone logged off in the den after two recalls home were refused 0x049E): the mage build's last step, the bind a player makes at the town's lifestone. It is the journey's
/// <see cref="BindStation"/> (M5 7 F14 runs the same station when a recall home is refused) as a leg of the build: <c>bind-place</c> (a place to the ground's home lifestone point, <see cref="GroundHome.LifestonePoint"/>, waited out as every place is),
/// <c>bind-find</c> (the lifestone, by its description flag), <c>bind-use</c> (peace, then <c>Use</c> of the lifestone: ACE's "You have attuned your spirit to this Lifestone ...", the death bind, <c>Lifestone.cs</c> 60-66 at 95ef426d), <c>bind-tie</c> (the magic mode, then Lifestone Tie
/// <see cref="BindStation.LifestoneTieSpell"/> at it: ACE's "You have successfully linked with the life stone.", the link Lifestone Recall reads, <c>WorldObject_Magic.cs</c> 966-976) and the peace mode again for the log off. These are a player's own actions, not grants; the place before them is the build's own (the build already places the mage in Holtburg). Every
/// fact is the ground profile's (D46).
/// </summary>
public sealed class LifestoneBindLeg : ScriptLeg
{
    /// <summary>The leg's name.</summary>
    public const string LegName = "lifestone-bind";

    /// <summary>The text ACE sends when the Use bound the spirit to the lifestone.</summary>
    public const string AttunedText = "attuned your spirit";

    /// <summary>The text ACE sends when Lifestone Tie linked.</summary>
    public const string LinkedText = "linked with the life stone";

    /// <summary>Seconds the lifestone is looked for after the place.</summary>
    public const double FindSeconds = 12;

    /// <summary>How far from the player the lifestone is looked for, in metres (the bind station's).</summary>
    public const float FindMetres = 150f;

    /// <summary>Seconds ACE's text is waited for after the Use or the cast.</summary>
    public const double TextSeconds = 5;

    /// <summary>Seconds a combat mode change may take.</summary>
    public const double ModeSeconds = 10;

    /// <summary>The same-level fizzle retries of the tie cast.</summary>
    public const int MaxFizzles = 3;

    private readonly HuntingGround _ground;
    private readonly ILogSink _sink;
    private readonly IReadOnlyDictionary<uint, Lifestone.Data.Magic.SpellInfo>? _spells;
    private readonly List<(ChatKind Kind, uint Type, string Text)> _chat = [];
    private string? _tieFailure;

    /// <summary>A bind at <paramref name="ground"/>'s home lifestone.</summary>
    /// <param name="ground">The ground (its <see cref="HuntingGround.Home"/>).</param>
    /// <param name="log">Where the leg logs (the build's <c>[mage]</c> category).</param>
    /// <param name="spells">The SpellTable's spells (the names in the cast lines), or null.</param>
    public LifestoneBindLeg(HuntingGround ground, ILogSink log, IReadOnlyDictionary<uint, Lifestone.Data.Magic.SpellInfo>? spells = null)
        : base(LegName, text => (log ?? throw new ArgumentNullException(nameof(log))).Info(MageBuildScenario.LogCategory, text))
    {
        _ground = ground ?? throw new ArgumentNullException(nameof(ground));
        _sink = log;
        _spells = spells;
    }

    /// <summary>True once ACE's attune text was seen.</summary>
    public bool Bound { get; private set; }

    /// <summary>True once ACE's link text was seen.</summary>
    public bool Linked { get; private set; }

    /// <inheritdoc />
    protected override IEnumerable<ActionHandle?> Script()
    {
        var home = Destination(_ground.Home.Town + " home lifestone", _ground.Home.LifestoneCell, _ground.Home.LifestonePoint);
        Stage = "bind-place";
        foreach (var h in PlaceAndSettle(home, "to the " + _ground.Home.Town + " lifestone"))
        {
            yield return h;
        }

        Require(Problems.Count == 0, "bind: the place to the lifestone failed: " + string.Join("; ", Problems));

        string? failure = null;
        using (S.Events.Subscribe<ChatReceived>((in ChatReceived e) => _chat.Add((e.Line.Kind, e.Line.ChatType, e.Line.Text))))
        {
            Stage = "bind-find";
            var lifestone = 0u;
            foreach (var h in Until(() => (lifestone = Find()) != 0, FindSeconds))
            {
                yield return h;
            }

            if (lifestone == 0)
            {
                failure = $"no lifestone (the BF_LIFESTONE description flag) within {FindMetres:0} m of the player within {FindSeconds:0} s of the place";
            }
            else
            {
                Stage = "bind-use";
                if (S.State.Self.Qualities.CombatMode != CombatModes.NonCombat)
                {
                    var peace = S.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
                    foreach (var h in Until(() => peace.IsCompleted, ModeSeconds))
                    {
                        yield return h;
                    }
                }

                var useMark = _chat.Count;
                var use = S.Actions.Use(lifestone, ActionSource.Host);
                foreach (var h in Until(() => use.IsCompleted, 20))
                {
                    yield return h;
                }

                foreach (var h in Until(() => TextSince(useMark, AttunedText), TextSeconds))
                {
                    yield return h;
                }

                Bound = TextSince(useMark, AttunedText);
                if (!Bound && !use.IsCompleted)
                {
                    failure = $"the Use of the lifestone did not complete ({Describe(use)})";
                }
                else
                {
                    // M5 7 lead (the live build, 2026-10-29): the Use completed but this leg's chat buffer saw no attune text; the journey's
                    // bind station sees it live, so the leg goes on to the Tie and the run's first recall home proves the bind (TODO(m5-build-bind-text)).
                    Log(Bound
                        ? $"bind: the Use of the lifestone 0x{lifestone:X8} attuned the spirit"
                        : $"bind: the Use of the lifestone 0x{lifestone:X8} completed but no attune text was seen; the Tie goes on (TODO(m5-build-bind-text))");
                    foreach (var h in Tie(lifestone))
                    {
                        yield return h;
                    }

                    failure = _tieFailure;
                }
            }
        }

        if (S.State.Self.Qualities.CombatMode != CombatModes.NonCombat)
        {
            var peaceAgain = S.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
            foreach (var h in Until(() => peaceAgain.IsCompleted, ModeSeconds))
            {
                yield return h;
            }
        }

        Require(failure is null, "bind: " + failure);
    }

    private IEnumerable<ActionHandle?> Tie(uint lifestone)
    {
        Stage = "bind-tie";
        if (CombatModeGuard.Read(S.State).Caster is null)
        {
            var caster = GearInventory.PackCaster(S.State, new List<uint>());
            if (caster == 0)
            {
                _tieFailure = "no caster (a wand, a staff or an orb) in hand or in the pack for the magic mode";
                yield break;
            }

            Log($"bind: no caster in hand; wielding the pack's caster 0x{caster:X8} for the magic mode");
            var sent = new Quartermaster.WieldResult();
            var wield = Quartermaster.Wardrobe.Send(S, new Quartermaster.WieldRequest(caster) { Name = "the pack's caster", Location = (uint)EquipMask.Held, Source = ActionSource.Host }, sent); // M6 16 E5e1: the one wield routine's send; this leg waits for the answer and the caster in hand by one deadline, below
            if (wield is null)
            {
                _tieFailure = $"the pack's caster 0x{caster:X8} was not wielded: {sent.Why}";
                yield break;
            }

            foreach (var h in Until(() => wield.IsCompleted && CombatModeGuard.Read(S.State).Caster is not null, ModeSeconds))
            {
                yield return h;
            }

            if (CombatModeGuard.Read(S.State).Caster is null)
            {
                _tieFailure = $"the pack's caster 0x{caster:X8} was not wielded within {ModeSeconds:0} s ({Describe(wield)})";
                yield break;
            }
        }

        if (S.State.Self.Qualities.CombatMode != CombatModes.Magic)
        {
            var mode = S.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Host);
            foreach (var h in Until(() => mode.IsCompleted && S.State.Self.Qualities.CombatMode == CombatModes.Magic, ModeSeconds))
            {
                yield return h;
            }
        }

        if (S.State.Self.Qualities.CombatMode != CombatModes.Magic)
        {
            _tieFailure = $"the magic mode was not reached within {ModeSeconds:0} s";
            yield break;
        }

        var mark = _chat.Count;
        var attempt = new CastAttempt(S.Actions, S.State, _sink, _spells);
        var end = attempt.Start(new CastRequest { Levels = new[] { BindStation.LifestoneTieSpell }, Rules = CastRules.Travel(MaxFizzles), Purpose = "lifestone tie", Target = lifestone, Source = ActionSource.Automation }, S.Clock.Now); // M6 15: a scripted cast is the automation's, so the player's rule holds for it (the spell is on a spell bar)
        while (end == CastEnd.Running)
        {
            yield return null;
            end = attempt.Step(S.Clock.Now);
        }

        var handle = attempt.Handle;
        if (handle is null)
        {
            _tieFailure = $"the tie cast {BindStation.LifestoneTieSpell} was not sent: {end.Name()} ({attempt.Report.Why})";
            yield break;
        }

        foreach (var h in Until(() => TextSince(mark, LinkedText) || RefusalSince(mark) != 0 || handle.Outcome is { Kind: ActionOutcomeKind.ServerError }, TextSeconds))
        {
            yield return h;
        }

        if (TextSince(mark, LinkedText))
        {
            Linked = true;
            Log("bind: ACE's text: You have successfully " + LinkedText + ".");
            yield break;
        }

        var refusal = RefusalSince(mark) is not 0 and var code ? code : handle.Outcome is { Kind: ActionOutcomeKind.ServerError, Code: var server } ? server : 0u;
        _tieFailure = refusal != 0
            ? $"Lifestone Tie {BindStation.LifestoneTieSpell} was refused: {RecallHome.NameOfRefusal(refusal) ?? string.Create(CultureInfo.InvariantCulture, $"WeenieError 0x{refusal:X4}")}"
            : $"Lifestone Tie {BindStation.LifestoneTieSpell} did not link: no \"{LinkedText}\" text within {TextSeconds:0} s (cast ended {end.Name()}, {Describe(handle)})";
    }

    private uint Find() => S.State.Self.Position is { } here && S.State.Objects.NearestWithFlags(WorldObjectTable.LifestoneFlag, in here, FindMetres, out var guid) ? guid : 0;

    private bool TextSince(int mark, string needle)
    {
        for (var i = mark; i < _chat.Count; i++)
        {
            if (_chat[i].Text.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private uint RefusalSince(int mark)
    {
        for (var i = mark; i < _chat.Count; i++)
        {
            if (_chat[i].Kind == ChatKind.WeenieError)
            {
                return _chat[i].Type;
            }
        }

        return 0;
    }
}
