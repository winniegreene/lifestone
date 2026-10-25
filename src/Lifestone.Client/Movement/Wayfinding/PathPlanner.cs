using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>
/// Lifestone's outdoor route planner (P5a, design 3.1, docs/wayfinding.md): plans over a <see cref="NavMap"/> in two
/// levels, then pulls the node path taut.
/// </summary>
/// <remarks>
/// <para><b>Coarse.</b> A* over the map's region graph (24 m land cells split by a flood fill) gives a corridor of
/// land cells from the start's region to the goal's.</para>
/// <para><b>Fine.</b> 8-neighbour A* with the octile heuristic over the 1 m nodes, bounded to the corridor plus
/// <see cref="CorridorRing"/> land cell; if that fails, once more at <see cref="WidenedRing"/>; then unbounded over every
/// attached tile (<see cref="NavPlanResult.Widened"/> says which). A step costs its length (1 or the square root of 2)
/// times (1 + the slope ramp x <see cref="SlopeWeight"/> + (<see cref="ClearanceSoftNodes"/> - clearance) x
/// <see cref="ClearanceWeight"/> below it + <see cref="WaterWeight"/> on water), plus <see cref="DropWeight"/> per metre a
/// drop exceeds the step-up; <see cref="NavMap"/>'s step rules block the rest. Every addend is non-negative, so the
/// octile distance stays admissible and open flat ground costs exactly the octile distance.</para>
/// <para><b>Pulled.</b> The node path is pulled to vertices by the lattice line of sight (<see cref="NavMap.LineOfSight"/>
/// at clearance <see cref="PullMinClearance"/>, never crossing water, slope or a scar the node path kept off, nor more of the creature layer
/// than the node path between), each vertex with its node's z; then straightened: a vertex goes when the vertex kept before it sees the one
/// after it by the same rule (nav-natural-steering: long straight runs over open ground, a vertex only at a real corner).</para>
/// <para><b>Creatures.</b> <see cref="Creatures"/>, the run's creature layer, is stamped per plan (never into the tile): a step into a disc
/// costs <see cref="NavCreatures.CostPerMetre"/> more per metre, the goal's own creature left out (nav-creature-avoid).</para>
/// <para><b>Far goals.</b> A goal beyond the attached tiles is planned to a horizon goal: the open node on the attached
/// area's outer edge, reachable on the coarse graph, nearest the straight line from the start to the goal
/// (<see cref="NavStatus.Horizon"/>). The runner's contract: run the horizon route, and once the map's
/// <see cref="NavMap.Version"/> has changed (the next tiles attached) and the body is within
/// <see cref="HorizonReplanMetres"/> of the route's end, plan again to the real goal.</para>
/// <para><b>Pooled.</b> The open heap, g, parent and the closed and seen stamps (generation counters) are kept between
/// plans and grow only with the map, so a warm replan allocates only its result. One planner per runner; not
/// thread-safe. Not retail: Lifestone's own wayfinder; every constant is Lifestone's.</para>
/// </remarks>
public sealed class PathPlanner
{
    /// <summary>The slope ramp's weight: a node at the ramp's top (SlopeCost 255) costs this much extra per metre. Not retail: Lifestone's own.</summary>
    public const float SlopeWeight = 2f;

    /// <summary>Clearance (in nodes) below which a node costs extra. Not retail: Lifestone's own (design 3.1).</summary>
    public const int ClearanceSoftNodes = 5;

    /// <summary>The extra cost per metre per node of clearance under <see cref="ClearanceSoftNodes"/>. Not retail: Lifestone's own (design 3.1).</summary>
    public const float ClearanceWeight = 0.3f;

    /// <summary>The extra cost per metre on water. Not retail: Lifestone's own (a run keeps to dry land when it can).</summary>
    public const float WaterWeight = 3f;

    /// <summary>The extra cost per metre a drop exceeds <see cref="NavMap.StepUpMetres"/>. Not retail: Lifestone's own.</summary>
    public const float DropWeight = 1f;

    /// <summary>The bounded fine search's margin round the coarse corridor, in land cells. Not retail: Lifestone's own (design 3.1).</summary>
    public const int CorridorRing = 1;

    /// <summary>The widened search's margin: the corridor plus one plus two land cells. Not retail: Lifestone's own.</summary>
    public const int WidenedRing = CorridorRing + 2;

    /// <summary>The clearance in nodes the pulled route's samples keep (1: not blocked). Not retail: Lifestone's own (design 3.1).</summary>
    public const int PullMinClearance = 1;

    /// <summary>The runner replans a horizon route within this many metres of its end once the next tiles are attached. Not retail: Lifestone's own (design 3.1).</summary>
    public const float HorizonReplanMetres = 60f;

    /// <summary>The runner replans when the body is this far off the route (<see cref="NavRoute.DistanceFrom"/>). Not retail: Lifestone's own (design 3.1).</summary>
    public const float OffRouteMetres = 3f;

    /// <summary>The most passes of the straightening (each drops the vertices the kept one before sees past). Not retail: Lifestone's own.</summary>
    public const int StraightenPasses = 4;

    /// <summary>
    /// The clearance in nodes a straightened shortcut keeps (one more than the pull's): a small static's collision can reach past its stamp, and a long
    /// line beside one is what the physics refuses (the forest run's 0x02000246 in 0xA3B4). Not retail: Lifestone's own.
    /// </summary>
    public const int StraightenMinClearance = PullMinClearance + 1;

    /// <summary>
    /// How much more of the creature layer's cost (in metres of edge cost) a pulled segment may cross than the node path between its ends did:
    /// the lattice's rounding, so a segment never cuts deeper into a creature's disc than the search went. Not retail: Lifestone's own.
    /// </summary>
    public const float CreaturePullSlack = 0.5f;

    /// <summary>
    /// How far, in nodes, a blocked start may move to an open node (a body brushing a static stands on its stamp, which
    /// the builder inflates by the body's radius and a margin). Not retail: Lifestone's own.
    /// </summary>
    public const int StartSnapNodes = 1;

    /// <summary>An object goal's smallest arrival radius in metres: the node holding the target counts. Not retail: Lifestone's own.</summary>
    public const float MinObjectRange = 0.75f;

    /// <summary>
    /// The heuristic's tie-break: f = g + h x this, so among equal f the deeper node goes first (a plan may cost up to
    /// 0.1% over the optimum; open flat ground still costs exactly the octile distance). Not retail: Lifestone's own.
    /// </summary>
    public const float TieBreak = 1.001f;

    private const float Sqrt2 = 1.41421356f;

    // Node index offsets of the 8 directions inside a tile.
    private static readonly int[] Offset = [1, NavTile.Size, -1, -NavTile.Size, NavTile.Size + 1, NavTile.Size - 1, -NavTile.Size - 1, -NavTile.Size + 1];

    private readonly NavMap _map;
    private readonly FineHeap _heap = new();
    private readonly NodeHeap _coarseHeap = new();

    private NodeState[] _state = [];
    private int _gen;

    private int[] _cellStamp = [];
    private int _cellGen;

    private float[] _cg = [];
    private int[] _cparent = [];
    private int[] _cseen = [];
    private int[] _cclosed = [];
    private int _cgen;

    private int[] _coarsePath = new int[256];
    private int _coarseCount;
    private int[] _path = new int[1024];
    private int[] _waterPrefix = new int[1024];
    private int[] _slopePrefix = new int[1024];
    private int _pathCount;
    private Vector2[] _vxy = new Vector2[64];
    private float[] _vz = new float[64];
    private int _vCount;

    private NavScar[] _scars = new NavScar[8];
    private bool[] _scarStart = new bool[8];
    private int _scarCount;

    private NavCreature[] _creatures = new NavCreature[8];
    private int _creatureCount;
    private float[] _creatureCost = [];
    private int[] _creatureStamp = [];
    private int _creatureGen;
    private float[] _creaturePrefix = new float[1024];
    private int[] _vIdx = new int[64];

    // The current plan's goal.
    private int _goalNode;
    private int _goalGx;
    private int _goalGy;
    private bool _objectGoal;
    private Vector2 _target;
    private float _range;
    private int _reached;

    /// <summary>A planner over <paramref name="map"/>.</summary>
    public PathPlanner(NavMap map)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
    }

    /// <summary>The last found plan's string pulling time in milliseconds (a diagnostic for the timing test).</summary>
    internal double LastPullMilliseconds { get; private set; }

    /// <summary>The fine open heap's largest size so far (a diagnostic for the timing test).</summary>
    internal int HeapHighWater => _heap.HighWater;

    /// <summary>The map planned over.</summary>
    public NavMap Map => _map;

    /// <summary>The run's creature layer (nav-creature-avoid), read at each plan; null or empty plans over terrain and statics alone.</summary>
    public NavCreatures? Creatures { get; set; }

    /// <summary>The creatures the last plan stamped (the goal's own left out).</summary>
    public int CreaturesStamped => _creatureCount;

    /// <summary>
    /// Plans a route from <paramref name="from"/> to <paramref name="goal"/> over the attached tiles, avoiding the live
    /// <paramref name="scars"/>. Never throws for an unplannable route: the result names it.
    /// </summary>
    public NavPlanResult Plan(NavPoint from, NavGoal goal, NavScars? scars = null)
    {
        ArgumentNullException.ThrowIfNull(goal);
        long t0 = Stopwatch.GetTimestamp();
        EnsureCapacity();
        var startG = from.Global;
        SnapshotScars(scars, startG);
        StampCreatures(goal);

        if (!_map.TryNode(startG, out int start))
        {
            return Fail(NavStatus.StartBlocked, "start-not-attached", goal, t0);
        }

        bool snapped = false;
        if (!_map.Walkable(start))
        {
            // A body brushing a static stands on its conservative stamp: start from the nearest open node next to it.
            if (!TrySnapStart(ref start, ref startG))
            {
                return Fail(NavStatus.StartBlocked, "start-blocked", goal, t0);
            }

            snapped = true;
        }

        var goalG = goal.Point.Global;
        bool horizon = false;
        _objectGoal = false;
        if (!_map.TryNode(goalG, out _goalNode))
        {
            if (!TryHorizon(start, startG, goalG, out _goalNode))
            {
                return Fail(NavStatus.NoPath, "no-horizon", goal, t0);
            }

            horizon = true;
        }
        else if (goal.IsObject)
        {
            _target = goalG;
            _range = MathF.Max(goal.Range, MinObjectRange);
            if (!TryObjectGoal(out _goalNode))
            {
                return Fail(NavStatus.GoalBlocked, "no-open-node-in-range", goal, t0);
            }

            _objectGoal = true;
        }
        else
        {
            if (!_map.Walkable(_goalNode))
            {
                return Fail(NavStatus.GoalBlocked, "goal-blocked", goal, t0);
            }

            if (ScarredNode(_goalNode))
            {
                return Fail(NavStatus.GoalBlocked, "goal-scarred", goal, t0);
            }
        }

        _goalGx = _map.NodeGx(_goalNode);
        _goalGy = _map.NodeGy(_goalNode);

        int expanded = 0;
        int touched = 0;
        int corridor = Coarse(start, _goalNode);
        bool found = false;
        var widening = NavWidening.Bounded;
        if (corridor > 0)
        {
            StampCorridor(CorridorRing);
            found = Fine(start, bounded: true, ref expanded, ref touched);
            if (!found)
            {
                widening = NavWidening.Widened;
                StampCorridor(WidenedRing);
                found = Fine(start, bounded: true, ref expanded, ref touched);
            }
        }

        if (!found)
        {
            widening = NavWidening.Unbounded;
            found = Fine(start, bounded: false, ref expanded, ref touched);
        }

        if (!found)
        {
            return new NavPlanResult
            {
                Status = NavStatus.NoPath,
                Goal = goal,
                Reason = "no-path",
                NodesExpanded = expanded,
                NodesTouched = touched,
                CorridorRegions = corridor,
                Widened = widening,
                Milliseconds = Stopwatch.GetElapsedTime(t0).TotalMilliseconds,
            };
        }

        float cost = _state[_reached].G;
        long t1 = Stopwatch.GetTimestamp();
        Reconstruct();
        bool exactGoal = !horizon && !_objectGoal;
        Pull(startG, goalG, exactGoal);
        Straighten(startG, goalG, exactGoal);
        LastPullMilliseconds = Stopwatch.GetElapsedTime(t1).TotalMilliseconds;
        var xy = new Vector2[_vCount];
        var z = new float[_vCount];
        Array.Copy(_vxy, xy, _vCount);
        Array.Copy(_vz, z, _vCount);
        var route = new NavRoute(xy, z, _pathCount);
        return new NavPlanResult
        {
            Status = horizon ? NavStatus.Horizon : NavStatus.Found,
            Route = route,
            StartSnapped = snapped,
            Goal = goal,
            HorizonPoint = horizon ? route.End : null,
            NodesExpanded = expanded,
            NodesTouched = touched,
            CorridorRegions = corridor,
            Widened = widening,
            Cost = cost,
            Milliseconds = Stopwatch.GetElapsedTime(t0).TotalMilliseconds,
        };
    }

    private static NavPlanResult Fail(NavStatus status, string reason, NavGoal goal, long t0) => new()
    {
        Status = status,
        Goal = goal,
        Reason = reason,
        Milliseconds = Stopwatch.GetElapsedTime(t0).TotalMilliseconds,
    };

    private void EnsureCapacity()
    {
        int nodes = _map.SlotCapacity * NavMap.NodesPerTile;
        if (_state.Length < nodes)
        {
            _state = new NodeState[nodes];
            _gen = 0;
        }

        int cells = _map.SlotCapacity * NavMap.CellsPerTile;
        if (_cellStamp.Length < cells)
        {
            _cellStamp = new int[cells];
            _cellGen = 0;
        }

        int regions = _map.RegionCount;
        if (_cg.Length < regions)
        {
            int size = Math.Max(regions, _cg.Length * 2);
            _cg = new float[size];
            _cparent = new int[size];
            _cseen = new int[size];
            _cclosed = new int[size];
            _cgen = 0;
        }
    }

    private int NextGen(ref int gen)
    {
        if (gen == int.MaxValue)
        {
            Array.Clear(_state);
            gen = 0;
        }

        return ++gen;
    }

    private static int NextGen(ref int gen, int[] a, int[]? b = null)
    {
        if (gen == int.MaxValue)
        {
            Array.Clear(a);
            if (b is not null)
            {
                Array.Clear(b);
            }

            gen = 0;
        }

        return ++gen;
    }

    // ---- scars ----

    private void SnapshotScars(NavScars? scars, Vector2 startG)
    {
        _scarCount = scars?.CopyLive(ref _scars) ?? 0;
        if (_scarStart.Length < _scars.Length)
        {
            _scarStart = new bool[_scars.Length];
        }

        for (int k = 0; k < _scarCount; k++)
        {
            var s = _scars[k];
            _scarStart[k] = Vector2.DistanceSquared(startG, s.Centre) <= s.Radius * s.Radius;
        }
    }

    /// <summary>Whether a scar the start is not in covers the node (a goal there is blocked).</summary>
    private bool ScarredNode(int node)
    {
        var c = _map.NodeCentre(node);
        for (int k = 0; k < _scarCount; k++)
        {
            if (!_scarStart[k] && Vector2.DistanceSquared(c, _scars[k].Centre) <= _scars[k].Radius * _scars[k].Radius)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A step into a scar is blocked, except outward inside the scar the start stands in.</summary>
    private bool ScarBlocks(int from, int to)
    {
        var ct = _map.NodeCentre(to);
        for (int k = 0; k < _scarCount; k++)
        {
            var s = _scars[k];
            float r2 = s.Radius * s.Radius;
            float dt = Vector2.DistanceSquared(ct, s.Centre);
            if (dt > r2)
            {
                continue;
            }

            if (_scarStart[k])
            {
                float df = Vector2.DistanceSquared(_map.NodeCentre(from), s.Centre);
                if (df <= r2 && dt >= df - 1e-4f)
                {
                    continue;
                }
            }

            return true;
        }

        return false;
    }

    // ---- creatures ----

    /// <summary>Stamps the creature layer's cost on the nodes of every disc (the goal's own creature left out); per plan, never into a tile.</summary>
    private void StampCreatures(NavGoal goal)
    {
        _creatureCount = 0;
        if (Creatures is not { Count: > 0 } layer)
        {
            return;
        }

        if (_creatureCost.Length < _state.Length)
        {
            _creatureCost = new float[_state.Length];
            _creatureStamp = new int[_state.Length];
            _creatureGen = 0;
        }

        int gen = NextGen(ref _creatureGen, _creatureStamp);
        int n = layer.CopyTo(ref _creatures);
        uint ignore = goal.Guid ?? 0;
        for (int k = 0; k < n; k++)
        {
            var c = _creatures[k];
            if ((ignore != 0 && c.Guid == ignore) || c.Radius <= 0f)
            {
                continue;
            }

            _creatures[_creatureCount++] = c;
            int x0 = (int)MathF.Floor(c.Centre.X - c.Radius);
            int x1 = (int)MathF.Floor(c.Centre.X + c.Radius);
            int y0 = (int)MathF.Floor(c.Centre.Y - c.Radius);
            int y1 = (int)MathF.Floor(c.Centre.Y + c.Radius);
            for (int gy = y0; gy <= y1; gy++)
            {
                for (int gx = x0; gx <= x1; gx++)
                {
                    float cost = NavCreatures.CostPerMetre(Vector2.Distance(new Vector2(gx + 0.5f, gy + 0.5f), c.Centre), c.Radius);
                    if (cost <= 0f || !_map.TryNodeG(gx, gy, out int node))
                    {
                        continue;
                    }

                    if (_creatureStamp[node] != gen)
                    {
                        _creatureStamp[node] = gen;
                        _creatureCost[node] = 0f;
                    }

                    _creatureCost[node] += cost;
                }
            }
        }
    }

    /// <summary>The creature layer's extra cost per metre on <paramref name="node"/> this plan (0 off every disc).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float CreatureCost(int node) => _creatureCount > 0 && _creatureStamp[node] == _creatureGen ? _creatureCost[node] : 0f;

    // ---- goals ----

    /// <summary>The open node within <see cref="StartSnapNodes"/> of a blocked start nearest the start point; the start point becomes its centre.</summary>
    private bool TrySnapStart(ref int start, ref Vector2 startG)
    {
        int gx = _map.NodeGx(start);
        int gy = _map.NodeGy(start);
        float best = float.MaxValue;
        int found = -1;
        for (int dy = -StartSnapNodes; dy <= StartSnapNodes; dy++)
        {
            for (int dx = -StartSnapNodes; dx <= StartSnapNodes; dx++)
            {
                if (!_map.TryNodeG(gx + dx, gy + dy, out int node) || !_map.Walkable(node))
                {
                    continue;
                }

                float d2 = Vector2.DistanceSquared(_map.NodeCentre(node), startG);
                if (d2 < best)
                {
                    best = d2;
                    found = node;
                }
            }
        }

        if (found < 0)
        {
            return false;
        }

        start = found;
        startG = _map.NodeCentre(found);
        return true;
    }

    private bool TryObjectGoal(out int goalNode)
    {
        goalNode = -1;
        int r = (int)MathF.Ceiling(_range) + 1;
        int cx = (int)MathF.Floor(_target.X);
        int cy = (int)MathF.Floor(_target.Y);
        float best = float.MaxValue;
        float range2 = _range * _range;
        for (int gy = cy - r; gy <= cy + r; gy++)
        {
            for (int gx = cx - r; gx <= cx + r; gx++)
            {
                if (!_map.TryNodeG(gx, gy, out int node) || !_map.Walkable(node) || ScarredNode(node))
                {
                    continue;
                }

                float d2 = Vector2.DistanceSquared(new Vector2(gx + 0.5f, gy + 0.5f), _target);
                if (d2 <= range2 && d2 < best)
                {
                    best = d2;
                    goalNode = node;
                }
            }
        }

        return goalNode >= 0;
    }

    /// <summary>
    /// The horizon goal: of the open nodes on the attached area's outer edge whose region the start reaches on the coarse
    /// graph, the one nearest the segment from the start to the goal (ties to the one nearer the goal).
    /// </summary>
    private bool TryHorizon(int start, Vector2 startG, Vector2 goalG, out int horizon)
    {
        horizon = -1;
        CoarseReach(_map.RegionOfNode(start));
        float best = float.MaxValue;
        for (int slot = 0; slot < _map.SlotHighWater; slot++)
        {
            if (!_map.SlotAttached(slot))
            {
                continue;
            }

            var (ox, oy) = _map.SlotOrigin(slot);
            int bx = ox / NavTile.Size;
            int by = oy / NavTile.Size;
            for (int side = 0; side < 4; side++)
            {
                if (_map.SlotOfBlock(bx + NavMap.Dx[side], by + NavMap.Dy[side]) >= 0)
                {
                    continue;
                }

                for (int k = 0; k < NavTile.Size; k++)
                {
                    int x = side switch { 0 => NavTile.Size - 1, 2 => 0, _ => k };
                    int y = side switch { 1 => NavTile.Size - 1, 3 => 0, _ => k };
                    int node = (slot * NavMap.NodesPerTile) + NavTile.Index(x, y);
                    if (!_map.Walkable(node) || _map.Clearance(node) < PullMinClearance || ScarredNode(node))
                    {
                        continue;
                    }

                    int region = _map.RegionOfNode(node);
                    if (region < 0 || _cseen[region] != _cgen)
                    {
                        continue;
                    }

                    var c = new Vector2(ox + x + 0.5f, oy + y + 0.5f);
                    float score = MathF.Sqrt(SegmentDistance2(c, startG, goalG)) + (0.01f * Vector2.Distance(c, goalG));
                    if (score < best)
                    {
                        best = score;
                        horizon = node;
                    }
                }
            }
        }

        return horizon >= 0;
    }

    private static float SegmentDistance2(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float len2 = ab.LengthSquared();
        float t = len2 <= 0f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
        return Vector2.DistanceSquared(p, a + (ab * t));
    }

    // ---- coarse ----

    /// <summary>Marks (<c>_cseen == _cgen</c>) every region reachable from <paramref name="from"/>, breadth first.</summary>
    private void CoarseReach(int from)
    {
        int gen = NextGen(ref _cgen, _cseen, _cclosed);
        if (from < 0)
        {
            return;
        }

        // The coarse parent array doubles as the queue: the region count bounds it.
        int head = 0;
        int tail = 0;
        _cseen[from] = gen;
        _cparent[tail++] = from;
        while (head < tail)
        {
            int r = _cparent[head++];
            foreach (int packed in _map.RegionEdges(r))
            {
                int t = _map.EdgeTarget(packed);
                if (_cseen[t] != gen)
                {
                    _cseen[t] = gen;
                    _cparent[tail++] = t;
                }
            }
        }
    }

    /// <summary>The coarse A* from the start's region to the goal's; fills the corridor and returns its region count (0: none).</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private int Coarse(int start, int goalNode)
    {
        _coarseCount = 0;
        int rs = _map.RegionOfNode(start);
        int rg = _map.RegionOfNode(goalNode);
        if (rs < 0 || rg < 0)
        {
            return 0;
        }

        int gen = NextGen(ref _cgen, _cseen, _cclosed);
        var goalC = _map.Region(rg).Centroid;
        _coarseHeap.Clear();
        _cg[rs] = 0f;
        _cseen[rs] = gen;
        _cparent[rs] = -1;
        _coarseHeap.Push(rs, Vector2.Distance(_map.Region(rs).Centroid, goalC));
        while (_coarseHeap.Count > 0)
        {
            int r = _coarseHeap.Pop();
            if (_cclosed[r] == gen)
            {
                continue;
            }

            _cclosed[r] = gen;
            if (r == rg)
            {
                for (int c = r; c >= 0; c = _cparent[c])
                {
                    if (_coarseCount == _coarsePath.Length)
                    {
                        Array.Resize(ref _coarsePath, _coarsePath.Length * 2);
                    }

                    _coarsePath[_coarseCount++] = c;
                }

                return _coarseCount;
            }

            var rc = _map.Region(r).Centroid;
            float gr = _cg[r];
            foreach (int packed in _map.RegionEdges(r))
            {
                int t = _map.EdgeTarget(packed);
                if (_cclosed[t] == gen)
                {
                    continue;
                }

                var tc = _map.Region(t).Centroid;
                float ng = gr + Vector2.Distance(rc, tc);
                if (_cseen[t] == gen && ng >= _cg[t])
                {
                    continue;
                }

                _cseen[t] = gen;
                _cg[t] = ng;
                _cparent[t] = r;
                _coarseHeap.Push(t, ng + Vector2.Distance(tc, goalC));
            }
        }

        return 0;
    }

    /// <summary>Stamps the corridor's land cells and every land cell within <paramref name="ring"/> of them.</summary>
    private void StampCorridor(int ring)
    {
        int gen = NextGen(ref _cellGen, _cellStamp);
        for (int k = 0; k < _coarseCount; k++)
        {
            int region = _coarsePath[k];
            int slot = _map.SlotOfRegion(region);
            int cell = _map.RegionCellIndex(region) - (slot * NavMap.CellsPerTile);
            var (ox, oy) = _map.SlotOrigin(slot);
            int lcx = (ox / NavMap.LandCellNodes) + (cell % NavMap.LandCellsPerSide);
            int lcy = (oy / NavMap.LandCellNodes) + (cell / NavMap.LandCellsPerSide);
            for (int dy = -ring; dy <= ring; dy++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                {
                    int gx = lcx + dx;
                    int gy = lcy + dy;
                    if (gx < 0 || gy < 0)
                    {
                        continue;
                    }

                    int s = _map.SlotOfBlock(gx / NavMap.LandCellsPerSide, gy / NavMap.LandCellsPerSide);
                    if (s >= 0)
                    {
                        _cellStamp[(s * NavMap.CellsPerTile) + ((gy % NavMap.LandCellsPerSide) * NavMap.LandCellsPerSide) + (gx % NavMap.LandCellsPerSide)] = gen;
                    }
                }
            }
        }
    }

    // ---- fine ----

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private bool Fine(int start, bool bounded, ref int expanded, ref int touched)
    {
        int gen = NextGen(ref _gen);
        int cellGen = _cellGen;
        var state = _state;
        var stamps = _cellStamp;
        _heap.Reset(state);
        ref var s0 = ref state[start];
        s0.G = 0f;
        s0.Seen = gen;
        s0.Parent = -1;
        _heap.Push(start, Heuristic(start) * TieBreak, inHeap: false);
        while (_heap.Count > 0)
        {
            int node = _heap.Pop();
            ref var ns = ref state[node];
            if (ns.Closed == gen)
            {
                continue;
            }

            ns.Closed = gen;
            expanded++;
            if (IsGoal(node))
            {
                _reached = node;
                return true;
            }

            float gn = ns.G;
            int slot = node / NavMap.NodesPerTile;
            int i = node - (slot * NavMap.NodesPerTile);
            int x = i % NavTile.Size;
            int y = i / NavTile.Size;
            if (x == 0 || y == 0 || x == NavTile.Size - 1 || y == NavTile.Size - 1)
            {
                ExpandBorder(node, gn, gen, bounded, cellGen, ref touched);
                continue;
            }

            // The interior fast path: every neighbour is in this tile.
            var tile = _map.TileOfSlot(slot);
            var flags = tile.Flags;
            var z = tile.Z;
            float zn = z[i];
            int slotBase = slot * NavMap.NodesPerTile;
            int cellBase = slot * NavMap.CellsPerTile;
            var (ox, oy) = _map.SlotOrigin(slot);
            for (int d = 0; d < 8; d++)
            {
                int j = i + Offset[d];
                int nb = slotBase + j;
                ref var ms = ref state[nb];
                if (ms.Closed == gen || (flags[j] & NavFlags.Blocked) != 0)
                {
                    continue;
                }

                int nx = x + NavMap.Dx[d];
                int ny = y + NavMap.Dy[d];
                if (bounded && stamps[cellBase + ((ny / NavMap.LandCellNodes) * NavMap.LandCellsPerSide) + (nx / NavMap.LandCellNodes)] != cellGen)
                {
                    continue;
                }

                if (d >= 4 && ((flags[i + NavMap.Dx[d]] & NavFlags.Blocked) != 0 || (flags[i + (NavMap.Dy[d] * NavTile.Size)] & NavFlags.Blocked) != 0))
                {
                    continue;
                }

                float zt = z[j];
                if (!NavMap.StepRule(zn, zt) || (_scarCount > 0 && ScarBlocks(node, nb)))
                {
                    continue;
                }

                float ng = gn + StepCost(tile, j, d, zn - zt);
                if (_creatureCount > 0)
                {
                    ng += (d < 4 ? 1f : Sqrt2) * CreatureCost(nb);
                }

                if (ms.Seen == gen && ng >= ms.G)
                {
                    continue;
                }

                bool inHeap = ms.Seen == gen;
                ms.Seen = gen;
                ms.G = ng;
                ms.Parent = node;
                touched++;
                _heap.Push(nb, ng + (HeuristicAt(ox + nx, oy + ny) * TieBreak), inHeap);
            }
        }

        return false;
    }

    /// <summary>The general expansion of a node on its tile's edge (neighbours may lie in other tiles).</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void ExpandBorder(int node, float gn, int gen, bool bounded, int cellGen, ref int touched)
    {
        var state = _state;
        for (int d = 0; d < 8; d++)
        {
            int nb = _map.Neighbour(node, d);
            if (nb < 0)
            {
                continue;
            }

            ref var ms = ref state[nb];
            if (ms.Closed == gen || (bounded && _cellStamp[NavMap.CellOf(nb)] != cellGen))
            {
                continue;
            }

            if (!_map.CanStep(node, nb, d) || (_scarCount > 0 && ScarBlocks(node, nb)))
            {
                continue;
            }

            int ts = nb / NavMap.NodesPerTile;
            float ng = gn + StepCost(_map.TileOfSlot(ts), nb - (ts * NavMap.NodesPerTile), d, _map.Z(node) - _map.Z(nb));
            if (_creatureCount > 0)
            {
                ng += (d < 4 ? 1f : Sqrt2) * CreatureCost(nb);
            }

            if (ms.Seen == gen && ng >= ms.G)
            {
                continue;
            }

            bool inHeap = ms.Seen == gen;
            ms.Seen = gen;
            ms.G = ng;
            ms.Parent = node;
            touched++;
            _heap.Push(nb, ng + (Heuristic(nb) * TieBreak), inHeap);
        }
    }

    private bool IsGoal(int node)
    {
        if (!_objectGoal)
        {
            return node == _goalNode;
        }

        return Vector2.DistanceSquared(_map.NodeCentre(node), _target) <= _range * _range;
    }

    private float Heuristic(int node)
    {
        if (_objectGoal)
        {
            return MathF.Max(0f, Vector2.Distance(_map.NodeCentre(node), _target) - _range);
        }

        return HeuristicAt(_map.NodeGx(node), _map.NodeGy(node));
    }

    /// <summary>The heuristic at global node (gx, gy): the octile distance to a point goal, the distance to an object goal's range.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float HeuristicAt(int gx, int gy)
    {
        if (_objectGoal)
        {
            return MathF.Max(0f, Vector2.Distance(new Vector2(gx + 0.5f, gy + 0.5f), _target) - _range);
        }

        int dx = Math.Abs(gx - _goalGx);
        int dy = Math.Abs(gy - _goalGy);
        int lo = Math.Min(dx, dy);
        return Math.Max(dx, dy) - lo + (Sqrt2 * lo);
    }

    /// <summary>
    /// The cost of a step in direction <paramref name="dir"/> into node <paramref name="i"/> of <paramref name="tile"/>,
    /// dropping <paramref name="drop"/> metres (negative for a rise; see the remarks).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float StepCost(NavTile tile, int i, int dir, float drop)
    {
        var flags = tile.Flags[i];
        float c = 1f;
        if ((flags & NavFlags.Slope) != 0)
        {
            c += SlopeWeight * tile.SlopeCost[i] / 255f;
        }

        int clearance = tile.Clearance[i];
        if (clearance < ClearanceSoftNodes)
        {
            c += (ClearanceSoftNodes - clearance) * ClearanceWeight;
        }

        if ((flags & NavFlags.Water) != 0)
        {
            c += WaterWeight;
        }

        float cost = (dir < 4 ? 1f : Sqrt2) * c;
        if (drop > NavMap.StepUpMetres)
        {
            cost += DropWeight * (drop - NavMap.StepUpMetres);
        }

        return cost;
    }

    /// <summary>One node's search state, packed so a visit touches one cache line.</summary>
    private struct NodeState
    {
        public float G;
        public int Parent;
        public int Seen;
        public int Closed;
        public int HeapPos;
    }

    // ---- the route ----

    private void Reconstruct()
    {
        _pathCount = 0;
        for (int n = _reached; n >= 0; n = _state[n].Parent)
        {
            if (_pathCount == _path.Length)
            {
                Array.Resize(ref _path, _path.Length * 2);
            }

            _path[_pathCount++] = n;
        }

        Array.Reverse(_path, 0, _pathCount);
        if (_waterPrefix.Length < _pathCount)
        {
            _waterPrefix = new int[_path.Length];
            _slopePrefix = new int[_path.Length];
        }

        if (_creaturePrefix.Length < _pathCount)
        {
            _creaturePrefix = new float[_path.Length];
        }

        int water = 0;
        int slope = 0;
        float exposure = 0f;
        for (int k = 0; k < _pathCount; k++)
        {
            var flags = _map.Flags(_path[k]);
            water += (flags & NavFlags.Water) != 0 ? 1 : 0;
            slope += (flags & NavFlags.Slope) != 0 ? 1 : 0;
            if (k > 0 && _creatureCount > 0)
            {
                bool diagonal = _map.NodeGx(_path[k]) != _map.NodeGx(_path[k - 1]) && _map.NodeGy(_path[k]) != _map.NodeGy(_path[k - 1]);
                exposure += (diagonal ? Sqrt2 : 1f) * CreatureCost(_path[k]);
            }

            _waterPrefix[k] = water;
            _slopePrefix[k] = slope;
            _creaturePrefix[k] = exposure;
        }
    }

    /// <summary>String pulling: from each vertex, the farthest later path point the lattice sees (galloping, then a binary search).</summary>
    private void Pull(Vector2 startG, Vector2 goalG, bool exactGoal)
    {
        _vCount = 0;
        int last = _pathCount - 1;
        AddVertex(startG, _map.Z(_path[0]), 0);
        if (last == 0)
        {
            if (exactGoal && goalG != startG)
            {
                AddVertex(goalG, _map.Z(_path[0]), 0);
            }

            return;
        }

        int i = 0;
        while (i < last)
        {
            int best = i + 1;
            int stride = 1;
            int failAt = -1;
            while (best < last)
            {
                int cand = Math.Min(best + stride, last);
                if (Visible(i, cand, startG, goalG, exactGoal))
                {
                    best = cand;
                    stride *= 2;
                }
                else
                {
                    failAt = cand;
                    break;
                }
            }

            if (failAt > 0)
            {
                int lo = best;
                int hi = failAt;
                while (hi - lo > 1)
                {
                    int mid = (lo + hi) >> 1;
                    if (Visible(i, mid, startG, goalG, exactGoal))
                    {
                        lo = mid;
                    }
                    else
                    {
                        hi = mid;
                    }
                }

                best = lo;
            }

            AddVertex(PathPoint(best, startG, goalG, exactGoal), _map.Z(_path[best]), best);
            i = best;
        }
    }

    /// <summary>
    /// Straightening (nav-natural-steering): a pulled vertex goes when the vertex kept before it sees the vertex after it (<see cref="Visible"/>, the
    /// pull's own rule at <see cref="StraightenMinClearance"/>), repeated up to <see cref="StraightenPasses"/> times. The pull's gallop stops at the first point it cannot see, and the
    /// lattice's sight is not monotone along a winding node path, so the pull leaves vertices a straight run does not need.
    /// </summary>
    private void Straighten(Vector2 startG, Vector2 goalG, bool exactGoal)
    {
        for (int pass = 0; pass < StraightenPasses && _vCount > 2; pass++)
        {
            int kept = 1;
            for (int v = 1; v < _vCount - 1; v++)
            {
                if (Visible(_vIdx[kept - 1], _vIdx[v + 1], startG, goalG, exactGoal, StraightenMinClearance))
                {
                    continue;
                }

                _vxy[kept] = _vxy[v];
                _vz[kept] = _vz[v];
                _vIdx[kept] = _vIdx[v];
                kept++;
            }

            _vxy[kept] = _vxy[_vCount - 1];
            _vz[kept] = _vz[_vCount - 1];
            _vIdx[kept] = _vIdx[_vCount - 1];
            kept++;
            bool changed = kept != _vCount;
            _vCount = kept;
            if (!changed)
            {
                break;
            }
        }
    }

    private Vector2 PathPoint(int k, Vector2 startG, Vector2 goalG, bool exactGoal)
    {
        if (k == 0)
        {
            return startG;
        }

        return exactGoal && k == _pathCount - 1 ? goalG : _map.NodeCentre(_path[k]);
    }

    private bool Visible(int i, int j, Vector2 startG, Vector2 goalG, bool exactGoal, int minClearance = PullMinClearance)
    {
        // The pull may cross water or slope only where the node path between the two points did.
        var avoid = NavFlags.None;
        if (_waterPrefix[j] - (i > 0 ? _waterPrefix[i - 1] : 0) == 0)
        {
            avoid |= NavFlags.Water;
        }

        if (_slopePrefix[j] - (i > 0 ? _slopePrefix[i - 1] : 0) == 0)
        {
            avoid |= NavFlags.Slope;
        }

        var a = PathPoint(i, startG, goalG, exactGoal);
        var b = PathPoint(j, startG, goalG, exactGoal);
        if (!_map.Sight(a, b, minClearance, avoid, _scars, _scarStart, _scarCount))
        {
            return false;
        }

        // Nor more of the creature layer than the node path between (nav-creature-avoid): a segment never cuts deeper into a disc than the search went.
        return _creatureCount == 0 || LineExposure(a, b) <= _creaturePrefix[j] - _creaturePrefix[i] + CreaturePullSlack;
    }

    /// <summary>The creature layer's stamped cost along the straight line from <paramref name="a"/> to <paramref name="b"/>, sampled as the lattice's sight is.</summary>
    private float LineExposure(Vector2 a, Vector2 b)
    {
        float len = Vector2.Distance(a, b);
        int n = Math.Max(1, (int)MathF.Ceiling(len / NavMap.SightSampleMetres));
        float step = len / n;
        float sum = 0f;
        for (int s = 1; s <= n; s++)
        {
            if (_map.TryNode(Vector2.Lerp(a, b, (float)s / n), out int node))
            {
                sum += CreatureCost(node) * step;
            }
        }

        return sum;
    }

    private void AddVertex(Vector2 xy, float z, int pathIndex)
    {
        if (_vCount == _vxy.Length)
        {
            Array.Resize(ref _vxy, _vxy.Length * 2);
            Array.Resize(ref _vz, _vz.Length * 2);
            Array.Resize(ref _vIdx, _vIdx.Length * 2);
        }

        _vxy[_vCount] = xy;
        _vz[_vCount] = z;
        _vIdx[_vCount] = pathIndex;
        _vCount++;
    }

    /// <summary>
    /// The fine search's binary min-heap of (node, key) with decrease-key: each open node is in it once, its position
    /// kept in its <see cref="NodeState"/>; the arrays grow and are kept.
    /// </summary>
    private sealed class FineHeap
    {
        private int[] _nodes = new int[1024];
        private float[] _keys = new float[1024];
        private NodeState[] _state = [];

        public int Count { get; private set; }

        public int HighWater { get; private set; }

        public void Reset(NodeState[] state)
        {
            _state = state;
            Count = 0;
        }

        /// <summary>Adds <paramref name="node"/>, or lowers its key when it is already in the heap.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Push(int node, float key, bool inHeap)
        {
            int i;
            if (inHeap)
            {
                i = _state[node].HeapPos;
            }
            else
            {
                if (Count == _nodes.Length)
                {
                    Array.Resize(ref _nodes, _nodes.Length * 2);
                    Array.Resize(ref _keys, _keys.Length * 2);
                }

                i = Count++;
                if (Count > HighWater)
                {
                    HighWater = Count;
                }
            }

            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (_keys[p] <= key)
                {
                    break;
                }

                int moved = _nodes[p];
                _nodes[i] = moved;
                _keys[i] = _keys[p];
                _state[moved].HeapPos = i;
                i = p;
            }

            _nodes[i] = node;
            _keys[i] = key;
            _state[node].HeapPos = i;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Pop()
        {
            int top = _nodes[0];
            int n = --Count;
            if (n > 0)
            {
                int node = _nodes[n];
                float key = _keys[n];
                int i = 0;
                while (true)
                {
                    int c = (2 * i) + 1;
                    if (c >= n)
                    {
                        break;
                    }

                    if (c + 1 < n && _keys[c + 1] < _keys[c])
                    {
                        c++;
                    }

                    if (_keys[c] >= key)
                    {
                        break;
                    }

                    int moved = _nodes[c];
                    _nodes[i] = moved;
                    _keys[i] = _keys[c];
                    _state[moved].HeapPos = i;
                    i = c;
                }

                _nodes[i] = node;
                _keys[i] = key;
                _state[node].HeapPos = i;
            }

            return top;
        }
    }

    /// <summary>A binary min-heap of (node, key) with lazy deletion (the coarse search); its arrays grow and are kept.</summary>
    private sealed class NodeHeap
    {
        private int[] _nodes = new int[1024];
        private float[] _keys = new float[1024];

        public int Count { get; private set; }

        public int HighWater { get; private set; }

        public void Clear() => Count = 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Push(int node, float key)
        {
            if (Count == _nodes.Length)
            {
                Array.Resize(ref _nodes, _nodes.Length * 2);
                Array.Resize(ref _keys, _keys.Length * 2);
            }

            int i = Count++;
            if (Count > HighWater)
            {
                HighWater = Count;
            }

            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (_keys[p] <= key)
                {
                    break;
                }

                _nodes[i] = _nodes[p];
                _keys[i] = _keys[p];
                i = p;
            }

            _nodes[i] = node;
            _keys[i] = key;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Pop()
        {
            int top = _nodes[0];
            int n = --Count;
            if (n > 0)
            {
                int node = _nodes[n];
                float key = _keys[n];
                int i = 0;
                while (true)
                {
                    int c = (2 * i) + 1;
                    if (c >= n)
                    {
                        break;
                    }

                    if (c + 1 < n && _keys[c + 1] < _keys[c])
                    {
                        c++;
                    }

                    if (_keys[c] >= key)
                    {
                        break;
                    }

                    _nodes[i] = _nodes[c];
                    _keys[i] = _keys[c];
                    i = c;
                }

                _nodes[i] = node;
                _keys[i] = key;
            }

            return top;
        }
    }
}
