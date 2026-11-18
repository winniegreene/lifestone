using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>What <see cref="CantripLook"/> needs of the retail UI beyond the panel host: the examine window.</summary>
public interface ICantripLookSurface : IAutopilotPanelSurface
{
    /// <summary>Opens the examine window on <paramref name="guid"/> as a person's examine does (the window shows and an appraisal is asked). False when it could not.</summary>
    /// <param name="guid">The piece.</param>
    bool Examine(uint guid);

    /// <summary>True while the examine window is up with <paramref name="guid"/>'s appraisal filled in.</summary>
    /// <param name="guid">The piece.</param>
    bool ExamineShows(uint guid);

    /// <summary>Closes the examine window.</summary>
    void CloseExamine();
}

/// <summary>
/// <c>--video-cantrip-look</c> (M6's hero video, off by default): each piece she loots with a great cantrip (<see cref="KeepAsideRule.GreatTiers"/>: Major or better) that raises a basic attribute or a skill she uses
/// (<see cref="IsIdeal"/>; <see cref="CantripJudge"/>, her skills read live) is looked at as a person would: the paper doll and packs page opens (<see cref="AutopilotPanelDirector.InventoryPanel"/>), the examine
/// window opens on the piece, and both stay up <see cref="HoldSeconds"/> s once the window shows the piece's appraisal; then the examine window closes, and the page closes if the look opened it, so the panel
/// director takes the panels back. A planned skill she does not hold yet does not count, nor a trained skill she does not raise. Pieces she held when the run began are not looked at; a piece is looked at once.
/// <para><b>Check, act, check.</b> Each look logs its piece and cantrips, whether the page and the window opened, whether the appraisal showed (a window that does not fill in <see cref="FillSeconds"/> s is logged as a
/// warning and closed), and the close. The look only shows windows: the autopilot's own work goes on under it, and the director does nothing while a look runs.</para>
/// </summary>
public sealed class CantripLook
{
    /// <summary>How long the page and the examine window stay up once the window shows the piece (the owner, 2026-11-18: three seconds).</summary>
    public const double HoldSeconds = 3.0;

    /// <summary>How long the examine window may take to show the piece's appraisal before the look gives up on it.</summary>
    public const double FillSeconds = 3.0;

    /// <summary>How often the packs are read for new pieces.</summary>
    public const double ScanSeconds = 0.5;

    private readonly ICantripLookSurface _surface;
    private readonly Action<string, bool> _log;
    private readonly HashSet<uint> _seen = [];
    private readonly Queue<(uint Guid, string Name, string Why)> _queue = new();
    private bool _baselined;
    private double _scannedAt = double.NegativeInfinity;
    private (uint Guid, string Name, string Why) _current;
    private Stage _stage;
    private double _stageStart;
    private bool _openedPage;
    private readonly Action<uint>? _appraise;
    private readonly HashSet<uint> _asked = [];

    /// <summary>Makes a look over the retail UI.</summary>
    /// <param name="surface">The panel host and the examine window.</param>
    /// <param name="log">Where the lines go (the text, and true for a warning).</param>
    /// <param name="includeHeld">True (<c>--video-cantrip-look=held</c>, for checking the look on screen): the pieces she holds when the look begins are looked at too, and the look runs without the autopilot.</param>
    /// <param name="appraise">With <paramref name="includeHeld"/>: asks the server for a piece's appraisal (a login holds none for the pieces she carries); one piece per scan. Null asks none.</param>
    public CantripLook(ICantripLookSurface surface, Action<string, bool> log, bool includeHeld = false, Action<uint>? appraise = null)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _appraise = includeHeld ? appraise : null;
        _baselined = includeHeld;
        IncludesHeld = includeHeld;
    }

    /// <summary>True when the pieces held at the start are looked at too, without the autopilot (<c>--video-cantrip-look=held</c>).</summary>
    public bool IncludesHeld { get; }

    private enum Stage
    {
        Idle,
        Filling,
        Holding,
    }

    /// <summary>True while a look holds the panels (the panel director waits).</summary>
    public bool Busy => _stage != Stage.Idle;

    /// <summary>How many looks were shown in full.</summary>
    public int Shown { get; private set; }

    /// <summary>
    /// The verdicts that make a piece worth a look: a basic attribute, or a skill she holds trained or specialised that she uses, which is a skill her experience raises
    /// (<see cref="MageTemplate.ClimberSpendSkills"/>: War, Life, Creature and Item Magic and Mana Conversion). A trained skill she does not raise (Loyalty, Run, Magic Defense) is not looked at.
    /// </summary>
    /// <param name="verdict">The verdict.</param>
    /// <param name="statOf">A spell's row of the spell table; null: <see cref="AceCantripStatMods.Of"/>.</param>
    public static bool IsIdeal(CantripVerdict verdict, Func<uint, CantripStatMod?>? statOf = null) => verdict.Use switch
    {
        CantripUse.Attribute => true,
        CantripUse.Specialised or CantripUse.Trained => (statOf ?? AceCantripStatMods.Of)(verdict.SpellId) is { IsSkill: true } row && MageTemplate.ClimberSpendSkills.Contains(row.Key),
        _ => false,
    };

    /// <summary>The ideal great cantrips of an appraisal, as the log names them; empty for none.</summary>
    /// <param name="state">The session's read model (her skills).</param>
    /// <param name="appraisal">The piece's appraisal.</param>
    /// <param name="spellName">The SpellTable's names.</param>
    public static List<CantripVerdict> IdealOf(GameState state, Appraisal? appraisal, Func<uint, string?> spellName) =>
        CantripJudge.Of(state).GreatOf(appraisal, spellName).Where(v => IsIdeal(v)).ToList();

    /// <summary>One frame.</summary>
    /// <param name="running">True while the autopilot runs; otherwise nothing is read and a look under way is finished at once.</param>
    /// <param name="state">The session's read model.</param>
    /// <param name="spellName">The SpellTable's names.</param>
    /// <param name="now">The session clock, in seconds.</param>
    public void Tick(bool running, GameState state, Func<uint, string?> spellName, double now)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(spellName);
        running |= IncludesHeld;
        if (!running)
        {
            if (Busy)
            {
                End("the autopilot stopped");
            }

            return;
        }

        switch (_stage)
        {
            case Stage.Filling:
                if (_surface.ExamineShows(_current.Guid))
                {
                    _stage = Stage.Holding;
                    _stageStart = now;
                    _log(string.Create(CultureInfo.InvariantCulture, $"cantrip look: '{_current.Name}' 0x{_current.Guid:X8} shows in the examine window; held {HoldSeconds:0.#} s"), false);
                }
                else if (now - _stageStart > FillSeconds)
                {
                    _log(string.Create(CultureInfo.InvariantCulture, $"cantrip look: '{_current.Name}' 0x{_current.Guid:X8}: the examine window did not show its appraisal within {FillSeconds:0.#} s; closed"), true);
                    End("not shown");
                }

                return;
            case Stage.Holding:
                if (now - _stageStart >= HoldSeconds)
                {
                    Shown++;
                    End("held " + HoldSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " s");
                }

                return;
        }

        if (now - _scannedAt >= ScanSeconds)
        {
            _scannedAt = now;
            Scan(state, spellName);
        }

        if (_queue.Count > 0)
        {
            Begin(_queue.Dequeue(), state, now);
        }
    }

    private void Scan(GameState state, Func<uint, string?> spellName)
    {
        var inventory = state.Self.Inventory;
        if (!inventory.IsKnown)
        {
            return;
        }

        var askedThisScan = false;
        foreach (var guid in Held(inventory))
        {
            if (_seen.Contains(guid))
            {
                continue;
            }

            if (!_baselined)
            {
                _seen.Add(guid); // held when the run began: not a find
                continue;
            }

            if (!state.Objects.TryGet(guid, out var item))
            {
                continue; // described later
            }

            if (!KeepAsideRule.NeedsAppraisal(item.ItemType))
            {
                _seen.Add(guid); // no wearable, no caster: no cantrip to show
                continue;
            }

            if (!state.Appraisals.TryGet(guid, out var appraisal))
            {
                if (_appraise is not null && !askedThisScan && _asked.Add(guid))
                {
                    askedThisScan = true;
                    _appraise(guid); // the held mode: a login holds no appraisal of what she carries
                }

                continue; // read again once appraised
            }

            _seen.Add(guid);
            var ideal = IdealOf(state, appraisal, spellName);
            if (ideal.Count > 0)
            {
                _queue.Enqueue((guid, item.Name, string.Join("; ", ideal)));
            }
        }

        _baselined = true;
    }

    private static IEnumerable<uint> Held(PlayerInventory inventory)
    {
        foreach (var entry in inventory.Pack)
        {
            yield return entry.Guid;
        }

        foreach (var pack in inventory.SidePacks)
        {
            foreach (var entry in inventory.ItemsIn(pack.Guid))
            {
                yield return entry.Guid;
            }
        }
    }

    private void Begin((uint Guid, string Name, string Why) piece, GameState state, double now)
    {
        if (!state.Self.Inventory.Find(piece.Guid).Held)
        {
            _log(string.Create(CultureInfo.InvariantCulture, $"cantrip look: '{piece.Name}' 0x{piece.Guid:X8} is no longer held; no look"), false);
            return;
        }

        _current = piece;
        _openedPage = false;
        if (_surface.Shown != AutopilotPanelDirector.InventoryPanel)
        {
            _openedPage = _surface.Open(AutopilotPanelDirector.InventoryPanel);
        }

        var pageUp = _surface.Shown == AutopilotPanelDirector.InventoryPanel;
        var examined = _surface.Examine(piece.Guid);
        _log(
            string.Create(
                CultureInfo.InvariantCulture,
                $"cantrip look: '{piece.Name}' 0x{piece.Guid:X8}: {piece.Why}; paper doll and packs {(pageUp ? "open" : "NOT open")}{(_openedPage ? " (opened for the look)" : string.Empty)}, examine {(examined ? "opened" : "NOT opened")}"),
            !pageUp || !examined);
        if (!examined)
        {
            End("the examine window did not open");
            return;
        }

        _stage = Stage.Filling;
        _stageStart = now;
    }

    private void End(string why)
    {
        _surface.CloseExamine();
        if (_openedPage && _surface.Shown == AutopilotPanelDirector.InventoryPanel)
        {
            _surface.Close(AutopilotPanelDirector.InventoryPanel);
        }

        _log(string.Create(CultureInfo.InvariantCulture, $"cantrip look: '{_current.Name}' 0x{_current.Guid:X8} done ({why}); the examine window is closed{(_openedPage ? " and the page given back" : string.Empty)}"), false);
        _stage = Stage.Idle;
        _openedPage = false;
    }
}
