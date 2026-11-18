using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M5 7 P7b (D139 rows g, i, j): the fight's walks and loot. (i) The splash kills: every corpse of a creature the mage killed is queued, not only the target's. (j) A put-off corpse that leaves the object table before it can rot was lost
/// from view; the fight remembers where it lay and walks back to it. (g) A walk that sticks is given up after <see cref="WalkStuckRungsGiveUp"/> stuck rungs, and the fight picks again.
/// </summary>
/// <remarks>
/// <para><b>(i) Who killed it.</b> ACE sends the 0x01AD KillerNotification only to the player who dealt the killing blow (<c>Creature_Death.cs:58-77</c> @95ef426d: <c>GetDeathMessage</c> enqueues it on the last damager's session when
/// that damager is a player), and its text names the dead creature (<c>Entity/Strings.cs</c>: every killer form holds <c>{0}</c>, the victim's name, once). So each 0x01AD is one kill of the mage's, named. The corpse comes a moment
/// later (ACE creates it after the death animation, <c>Creature_Death.cs:446-518</c>: <c>Corpse of &lt;name&gt;</c> where the creature lay, the creature then removed). The fight keeps each notice as a claim; the target path
/// (<see cref="SettlePendingKill"/>) takes the target's own; any other claim is paired with a fresh <c>Corpse of &lt;name&gt;</c> the notice names, created at or after it, that no queued kill already holds or would find. A claim that
/// meets no corpse within <see cref="SplashClaimSeconds"/> is named and counted as left. Corpses of others' kills never pair: no 0x01AD reaches this player for them.</para>
/// <para><b>(j) Lost from view.</b> A monster corpse rots 300 s after the kill (<c>WorldObject_Decay.cs:18</c>, <see cref="FightOptions.LootRetryWindowSeconds"/>); before that it leaves the client's table only when it was out of view
/// for 25 s (the client's cull, <see cref="State.ObjectCull"/>; ACE sends no delete, <c>Physics/Common/ObjectMaint.cs:21,41</c> @95ef426d). The loot step keeps such a corpse queued with the spot it lay at; when the fight has no
/// target in reach and the aggro rule passes at that spot, the fight walks back (purpose <c>loot</c>), and the corpse is looted as soon as the server sends it again. It is given up only for a named reason: its window passed, the
/// spot is beyond <see cref="LootWalkBackMaxMetres"/>, the walk back stuck or ended short, or the server did not send it within <see cref="LootWalkBackSendSeconds"/> of the arrival.</para>
/// <para><b>(g) Stuck walks.</b> The runner's own ladder gives a stuck run up only after three plans of four rungs each (about 75 s, <see cref="Movement.Wayfinding.NavRunner.MaxStuckPlans"/>), within the profile's 90 s leg; round 13's
/// walk stalled 64 s with no way past until the owner stopped it. The fight gives the leg up at <see cref="WalkStuckRungsGiveUp"/> rungs of one stuck episode, rests the leg's cell and the creature it walked toward for the tour's
/// <see cref="GroundTour.FailedCellRestSeconds"/>, and picks again (another target, another purpose, or a hold: D47).</para>
/// </remarks>
public sealed partial class FightScenario
{
    /// <summary>
    /// M5 7 P7b (D139 g): the stuck rungs of one episode after which the fight gives a walk up. Four: by then the back-off, the sidestep and the jump have each failed at that spot and the runner's fourth rung only replans round a
    /// 1.5 m scar there (<see cref="Movement.Wayfinding.NavRunner.StuckScarMetres"/>); round 13's two replans never got past. About 25 s at about 6 s a rung, against the 64 s the owner waited. Lifestone's own.
    /// </summary>
    public const int WalkStuckRungsGiveUp = 4;

    /// <summary>
    /// M5 7 P7b (D139 i): the seconds a kill notice that was not the target's waits for its corpse before it is given up. ACE creates the corpse after the death animation (round 13: about 1.3 s after the notice), the target path holds a
    /// same-name notice up to <see cref="KillNoticeSeconds"/>; ten seconds covers both with room. Lifestone's own.
    /// </summary>
    public const double SplashClaimSeconds = 10;

    /// <summary>M5 7 P7b (D139 j): the farthest, straight, a corpse lost from view may lie from the mage and still be walked back to (round 13's lost three lay 50 to 58 m off; about 8 s each way at the den's 10.4 m/s). Lifestone's own.</summary>
    public const double LootWalkBackMaxMetres = 80;

    /// <summary>M5 7 P7b (D139 j): a walk back that ends within this many metres of the spot has arrived (the spot's room: the server sends what that room sees). Lifestone's own.</summary>
    public const double LootWalkBackArriveMetres = 5;

    /// <summary>M5 7 P7b (D139 j): the seconds after the walk back's arrival the server may take to send the corpse again before it is given up (ACE adds an object to the player's view at the next visibility update). Lifestone's own.</summary>
    public const double LootWalkBackSendSeconds = 5;

    /// <summary>The slack, in seconds, for a corpse created in the same tick as the kill notice that names it (the packets of one tick are applied in their order).</summary>
    private const double SplashCreateSlackSeconds = 1.0;

    private const string CorpsePrefix = "Corpse of ";

    private readonly List<KillClaim> _claims = [];
    private readonly List<(uint Guid, double At)> _freshCorpses = [];
    private string? _killPendingName; // the name of the creature _killPendingGuid named
    private int _walkGiveUps;
    private TourPurpose _legPurpose;
    private string _legNote = string.Empty;
    private uint _legGuid;
    private uint _walkBackCorpse; // the corpse the walk under way goes back to (0: the walk is the tour's)
    private uint _lootWalkCorpse; // M5 7 P7s (D148): the corpse the fight's own walk goes toward, round a no-go cell ACE's straight approach would cross (0: none)
    private CellFrame _walkBackSpot;
    private readonly List<(double It, double Me, int Index)> _losSpots = [];

    /// <summary>A kill notice of the mage's not yet matched to a corpse: its text and the session clock it came.</summary>
    private sealed record KillClaim(string Text, double At);

    /// <summary>M5 7 P7b (D139 i): kill notices waiting for their corpse (the fight's end and a trip's stop wait for them, at most <see cref="SplashClaimSeconds"/>).</summary>
    private bool SplashPending => _loot is not null && _claims.Count > 0;

    /// <summary>
    /// True when <paramref name="text"/>, a 0x01AD's text, names <paramref name="name"/> as its victim: the name stands in it as whole words, followed by the end, punctuation or a lower-case word (every killer form of ACE's
    /// <c>Entity/Strings.cs</c> follows the name so), so "Tusker" is not named by "You bring Tusker Slave to a fiery end!".
    /// </summary>
    /// <param name="text">The notice's text.</param>
    /// <param name="name">A creature's name.</param>
    public static bool NoticeNames(string text, string name)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        for (var at = text.IndexOf(name, StringComparison.Ordinal); at >= 0; at = text.IndexOf(name, at + 1, StringComparison.Ordinal))
        {
            var end = at + name.Length;
            var before = at == 0 || text[at - 1] == ' ';
            var after = end == text.Length || text[end] is '\'' or '!' or ',' or '.' || (text[end] == ' ' && end + 1 < text.Length && char.IsLower(text[end + 1]));
            if (before && after)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>M5 7 P7b (D139 i): the tally heard a 0x01AD: one kill of the mage's, kept as a claim until its corpse is queued (looting on only).</summary>
    private void NoteKillClaim(string text)
    {
        if (_loot is null || _view is null)
        {
            return;
        }

        _claims.Add(new KillClaim(text, _view.Clock.Now));
    }

    /// <summary>M5 7 P7b (D139 i): the target path queued the target's corpse: the oldest claim naming the target is its own.</summary>
    private void ConsumeClaim(string name)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < _claims.Count; i++)
            {
                if (pass == 0 ? NoticeNames(_claims[i].Text, name) : _claims[i].Text.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    _claims.RemoveAt(i);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// M5 7 P7b (D139 i): once a frame, after the target path. Each claim that is not the pending target's is paired with a fresh corpse it names, which no queued kill holds or would find; then queued for the loot step with that corpse.
    /// A claim past <see cref="SplashClaimSeconds"/> with no corpse is given up, named and counted. Nothing runs with no claims (and nothing allocates).
    /// </summary>
    private void PairSplashKills(IGameSessionView session, double clock)
    {
        if (_loot is null)
        {
            return;
        }

        for (var i = 0; i < _claims.Count; i++)
        {
            var claim = _claims[i];
            if (_killPendingAt >= 0 && _killPendingName is { Length: > 0 } pending && claim.Text.Contains(pending, StringComparison.OrdinalIgnoreCase))
            {
                continue; // the target path settles a notice naming the target first (the table says whether the target died)
            }

            if (PairedCorpse(session, claim) is { } corpse && corpse.Position is { } lies)
            {
                var name = corpse.Name![CorpsePrefix.Length..];
                var created = CreatedAt(corpse.Guid);
                var away = session.State.Self.Position is { } own ? own.ToGlobal().DistanceTo(lies.ToGlobal()) : double.NaN;
                _log.Info(
                    LogCategory,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"loot: splash kill: the kill notice \"{claim.Text}\" ({clock - claim.At:0.#} s ago) was not the target's; paired with '{corpse.Name}' 0x{corpse.Guid:X8} created {created - claim.At:0.#} s after it, {away:0.#} m from the mage; queued (ACE sends the 0x01AD to the killer alone: Creature_Death.cs:58-77 @95ef426d)"));
                _loot.NoteKill(name, lies, 0, claim.At, corpse.Guid);
                _claims.RemoveAt(i--);
                continue;
            }

            if (clock - claim.At > SplashClaimSeconds)
            {
                _loot.NoteNoCorpse(string.Create(CultureInfo.InvariantCulture, $"loot: splash kill: the kill notice \"{claim.Text}\" ({clock - claim.At:0.#} s ago) met no fresh corpse it names that no other kill holds within {SplashClaimSeconds:0} s; counted, the fight goes on"));
                _claims.RemoveAt(i--);
            }
        }

        var cutoff = clock - SplashClaimSeconds - SplashCreateSlackSeconds;
        while (_freshCorpses.Count > 0 && _freshCorpses[0].At < cutoff)
        {
            _freshCorpses.RemoveAt(0);
        }
    }

    /// <summary>The fresh corpse <paramref name="claim"/> names (the longest name first, then the first created), created at or after the notice, that no queued kill holds or would find; null for none.</summary>
    private WorldObject? PairedCorpse(IGameSessionView session, KillClaim claim)
    {
        WorldObject? best = null;
        var bestLength = 0;
        foreach (var (guid, at) in _freshCorpses)
        {
            if (at < claim.At - SplashCreateSlackSeconds || !session.State.Objects.TryGet(guid, out var o) || o.Position is null || o.Name is not { } corpseName
                || !corpseName.StartsWith(CorpsePrefix, StringComparison.Ordinal) || corpseName.Length - CorpsePrefix.Length <= bestLength
                || !NoticeNames(claim.Text, corpseName[CorpsePrefix.Length..]) || MageLootRules.IsOwnCorpse(corpseName, session.State.Self.Name) || _loot!.Reserved(session, o))
            {
                continue;
            }

            (best, bestLength) = (o, corpseName.Length - CorpsePrefix.Length);
        }

        return best;
    }

    private double CreatedAt(uint guid)
    {
        foreach (var (g, at) in _freshCorpses)
        {
            if (g == guid)
            {
                return at;
            }
        }

        return double.NaN;
    }

    /// <summary>M5 7 P7b (D139 i): a corpse created during the fight is fresh for the splash pairing (looting on only).</summary>
    private void NoteFreshCorpse(WorldObject o, double clock)
    {
        if (_loot is not null && (o.DescriptionFlags & WorldObjectTable.CorpseFlag) != 0)
        {
            _freshCorpses.Add((o.Guid, clock));
        }
    }

    /// <summary>The leg <see cref="StartWalk"/> began: its purpose, note and the creature it walks toward (row g's give-up names them and rests them).</summary>
    private void NoteLeg(TourPurpose purpose)
    {
        _legPurpose = purpose;
        _legNote = _walk?.Tour?.PurposeNote ?? string.Empty;
        _legGuid = purpose == TourPurpose.Seen ? _walk?.Tour?.PurposeGuid ?? 0 : 0;
        _walkBackCorpse = 0;
        _lootWalkCorpse = 0;
    }

    /// <summary>
    /// M5 7 P7b (D139 g): in the Walk phase, each frame. When the leg under way has climbed <see cref="WalkStuckRungsGiveUp"/> rungs of one stuck episode, it is given up: the walk stops, the tour rests the leg's cell and the creature it
    /// walked toward (a walk back gives its corpse up instead), one line names it all, and the fight picks again. True when it gave up.
    /// </summary>
    private bool WalkStuckGivesUp(IGameSessionView session, double clock)
    {
        var rungs = _walk?.LegStuckRungs ?? 0;
        if (rungs < WalkStuckRungsGiveUp)
        {
            return false;
        }

        var goal = _walk!.LegGoalCell;
        _walk.Abandon();
        _walkGiveUps++;
        _missesInARow = 0;
        _phase = Phase.Choose;
        var where = session.State.Self.Position is { } here
            ? string.Create(CultureInfo.InvariantCulture, $"0x{here.CellId:X8} [{here.Origin.X:0.#} {here.Origin.Y:0.#} {here.Origin.Z:0.#}]")
            : "an unknown position";
        if (_escaping)
        {
            _log.Warn(LogCategory, $"walk: given up: the walk out of a stuck spot: {rungs} stuck rungs in a row near {where} (P7i); give-up {_walkGiveUps} this fight");
            StuckSpotAfterGiveUp(session, clock);
            return true;
        }

        if (_lootWalkCorpse != 0)
        {
            _log.Warn(LogCategory, $"walk: given up: the fight's own walk toward the corpse 0x{_lootWalkCorpse:X8} stuck: {rungs} stuck rungs in a row near {where} with no way past (D139 g); the loot step looks at it again from here (give-up {_walkGiveUps} this fight)");
            _lootWalkCorpse = 0;
            return true;
        }

        if (_walkBackCorpse != 0)
        {
            var why = string.Create(CultureInfo.InvariantCulture, $"the walk back stuck: {rungs} stuck rungs in a row near {where} with no way past (D139 g)");
            _log.Warn(LogCategory, $"walk: given up: {why}; the fight picks again (give-up {_walkGiveUps} this fight)");
            _loot?.WalkBackGivenUp(_walkBackCorpse, clock, why);
            _walkBackCorpse = 0;
            return true;
        }

        var tour = _walk.Tour;
        if (goal != 0)
        {
            tour?.LegFailed(goal, clock);
        }

        if (_legGuid != 0)
        {
            tour?.Avoid(_legGuid, clock);
        }

        var rest = tour is null
            ? "no tour to rest anything"
            : string.Create(CultureInfo.InvariantCulture, $"the cell 0x{goal:X8} rests {GroundTour.FailedCellRestSeconds:0} s{(_legGuid != 0 ? $" and the creature 0x{_legGuid:X8} is not walked toward for {GroundTour.FailedCellRestSeconds:0} s" : string.Empty)}");
        _log.Warn(
            LogCategory,
            string.Create(
                CultureInfo.InvariantCulture,
                $"walk: given up: {rungs} stuck rungs in a row on the leg to 0x{goal:X8} (purpose {_legPurpose.ToString().ToLowerInvariant()}{(_legNote.Length > 0 ? ": " + _legNote : string.Empty)}) with no way past near {where} (D139 g, at {WalkStuckRungsGiveUp} rungs); {rest}; the fight picks again: another target, another purpose or a hold (give-up {_walkGiveUps} this fight)"));
        StuckSpotAfterGiveUp(session, clock); // M5 7 P7i: twice in a row from about the same spot: out of the spot first
        return true;
    }

    /// <summary>
    /// M5 7 P7b (D139 j): the fight has no target in reach and no put-off corpse in the table could be retried: the oldest corpse lost from view whose spot the aggro rule passes is walked back to (purpose <c>loot</c>). True when a leg began.
    /// </summary>
    private bool StartWalkBack(IGameSessionView session, double clock)
    {
        if (_loot is null || _walk is null || !_loot.TryWalkBack(session, clock, out var corpse, out var spot, out var note))
        {
            return false;
        }

        if (!_walk.BeginLegTo(session, clock, in spot, out var leg))
        {
            _loot.WalkBackGivenUp(corpse, clock, "no walk back could be planned: " + leg);
            return false;
        }

        _walks++;
        NoteLegFrom(session); // M5 7 P7i
        _missesInARow = 0;
        _target = 0;
        _holdingLogged = false;
        if (double.IsNaN(_idleSince))
        {
            _idleSince = clock; // no target to pick: the burst rate leaves this time out
        }

        (_legPurpose, _legNote, _legGuid) = (TourPurpose.None, note, 0);
        (_walkBackCorpse, _walkBackSpot) = (corpse, spot);
        _lootWalkCorpse = 0;
        _log.Info(LogCategory, $"walking: purpose=loot ({note}); no target in reach and nothing in the way of it (D139 j); {leg}");
        _phase = Phase.Walk;
        return true;
    }

    /// <summary>
    /// M6 6 B2 (<c>m5-loot-walk-for-los</c>): the most floor spots whose line to a corpse one look sweeps, nearest the corpse first (Lifestone's own bound on the sweeps a look costs, as the self-defence step's
    /// <see cref="DefendStepLineTests"/>).
    /// </summary>
    public const int LootLosLineTests = DefendStepLineTests;

    /// <summary>
    /// M6 6 B2 (<c>m5-loot-walk-for-los</c>; the self-defence step's sweep, <see cref="DefenceSpot"/>): the spot the loot step's walk for a line goes to, for a corpse at <paramref name="lies"/> with no clear line from
    /// <paramref name="own"/>: of the floor's room middles (<see cref="IFightWalk.FloorSpots"/>) on a walkable cell off the no-go cells within the loot radius (<c>--fight-loot-radius-m</c>) of both the mage and the corpse, the
    /// nearest the corpse whose line to it is clear (at most <see cref="LootLosLineTests"/> lines swept). True with the spot and its note; false with why none.
    /// </summary>
    private bool LootLosSpot(in CellFrame own, in CellFrame lies, out CellFrame spot, out string note)
    {
        spot = default;
        if (_walk is null)
        {
            note = "the fight has no walk (TODO(m3-fight-no-walker))";
            return false;
        }

        var spots = _walk.FloorSpots;
        if (spots.Count == 0)
        {
            note = "this walk offers no floor spots (FloorSpots: none)";
            return false;
        }

        var radius = LootRadius;
        var origin = own.ToGlobal();
        var corpse = lies.ToGlobal();
        _losSpots.Clear();
        for (var i = 0; i < spots.Count; i++)
        {
            var s = spots[i];
            var at = s.ToGlobal();
            var me = at.DistanceTo(origin);
            var it = at.DistanceTo(corpse);
            if (me <= radius && it <= radius && _ground.IsWalkable(s.CellId) && (_options.ArenaHere || !_ground.IsNoGo(s.CellId)))
            {
                _losSpots.Add((it, me, i));
            }
        }

        _losSpots.Sort(static (a, b) => a.It != b.It ? a.It.CompareTo(b.It) : a.Me.CompareTo(b.Me));
        var swept = 0;
        foreach (var (it, me, i) in _losSpots)
        {
            if (swept >= LootLosLineTests)
            {
                break;
            }

            swept++;
            var s = spots[i];
            if (LineIsClearTo(in s, in lies))
            {
                spot = s;
                note = string.Create(CultureInfo.InvariantCulture, $"0x{s.CellId:X8} [{s.Origin.X:0.#} {s.Origin.Y:0.#} {s.Origin.Z:0.#}], {me:0.#} m off, {it:0.#} m from the corpse with a clear line to it ({swept} spot(s) swept of {_losSpots.Count} within the loot radius {radius:0.#} m)");
                return true;
            }
        }

        note = string.Create(CultureInfo.InvariantCulture, $"no floor spot within the loot radius {radius:0.#} m of the mage and the corpse has a clear line to it ({swept} swept of {_losSpots.Count})");
        return false;
    }

    /// <summary>
    /// M5 7 P7s (D148, D141; <c>m5-8-loot-no-go-corpse</c>): the loot step's own walk toward a corpse in view whose straight approach would cross a no-go cell (purpose <c>loot</c>): a leg to the corpse's spot planned over the
    /// walkable cells only (<see cref="IFightWalk.BeginLegTo"/>). The Walk phase runs it; its end, its give-up or its drop for an attacker or a heal hands the frame back to the loot step, which looks at the corpse again. True when the
    /// leg began; false with why not.
    /// </summary>
    private bool BeginLootWalk(IGameSessionView session, double clock, uint corpse, in CellFrame spot, string note, string purposeWhy, out string why)
    {
        if (_walk is null)
        {
            why = "the fight has no walk (TODO(m3-fight-no-walker))";
            return false;
        }

        if (!_walk.BeginLegTo(session, clock, in spot, out var leg))
        {
            why = leg;
            return false;
        }

        _walks++;
        NoteLegFrom(session); // M5 7 P7i
        _missesInARow = 0;
        _target = 0;
        _holdingLogged = false;
        (_legPurpose, _legNote, _legGuid) = (TourPurpose.None, note, 0);
        _walkBackCorpse = 0;
        _lootWalkCorpse = corpse;
        _log.Info(LogCategory, $"walking: purpose=loot ({note}); {purposeWhy}; {leg}");
        _phase = Phase.Walk;
        why = string.Empty;
        return true;
    }

    /// <summary>
    /// M5 7 P7b (D139 j): in the Walk phase of a walk back, each frame: the corpse sent again ends the walk (the loot step retries it now); the leg's end tells the loot step how near the spot the mage stands. True when it ended the walk.
    /// </summary>
    private bool WalkBackStep(IGameSessionView session, double clock, FightWalkStatus status)
    {
        if (_walkBackCorpse == 0 || _loot is null)
        {
            return false;
        }

        var away = session.State.Self.Position is { } here ? here.ToGlobal().DistanceTo(_walkBackSpot.ToGlobal()) : double.NaN;
        if (session.State.Objects.TryGet(_walkBackCorpse, out _))
        {
            _walk?.Abandon();
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: the walk back: the corpse 0x{_walkBackCorpse:X8} is sent again, {away:0.#} m from where it lay; the walk ends and the loot step retries it"));
            _walkBackCorpse = 0;
            _phase = Phase.Choose;
            return true;
        }

        if (status != FightWalkStatus.Arrived)
        {
            return false;
        }

        _loot.WalkBackEnded(_walkBackCorpse, clock, away);
        _walkBackCorpse = 0;
        _missesInARow = 0;
        _phase = Phase.Choose;
        return true;
    }
}
