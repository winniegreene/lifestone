using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;

namespace Lifestone.Client.Audio;

/// <summary>Where the ambient player reads the terrain words of the landblocks around the listener.</summary>
public interface IAmbientTerrain
{
    /// <summary>
    /// The terrain (81 words and 81 height indices) of a landblock, or null when the dat has none or the read is not finished (see <see cref="Revision"/>).
    /// Called on the frame thread, when the listener's cell changes (nine landblocks): a source reads nothing from the dats inside it.
    /// </summary>
    LandblockTerrain? Get(LandblockCoord coord);

    /// <summary>A number that changes when <see cref="Get"/> has a landblock it did not have at the last scan: the player scans again when it moves. A source that never defers keeps 0.</summary>
    int Revision => 0;
}

/// <summary>
/// Retail's outdoor ambient sound: for every terrain vertex near the listener, the region's ambient table for that terrain adds
/// weight to the table's sounds; each sound then plays now and again by its own chance and rate. Engine-agnostic, run once per
/// frame (the scan only when the listener's cell changes), allocation-free after construction.
/// </summary>
/// <remarks>
/// <para><b>Re-expressed by function name</b> (11.6096 addresses; the 11.4186 decompile was read, never copied):</para>
/// <list type="bullet">
/// <item><see cref="Update"/> re-expresses the new-cell branch of <c>CellManager::ChangePosition</c> (11.6096 0x00455AD0; 11.4186
/// 0x004559B0; not <c>SmartBox::set_viewer</c>, which has no ambient code): when the viewer's cell id is not the last one, it runs
/// <c>Ambient::InitSounds</c> (0x005521F0), <c>LScape::add_ambient_sounds</c> (0x00506270) only when the viewer's cell is
/// outdoors (or sees outside), <c>Ambient::UpdatePlayQueue</c> (0x00552670) and <c>Ambient::ReleaseSoundTables</c>. This player does
/// the same only when the listener's cell id (or the outdoors flag, or <see cref="IAmbientTerrain.Revision"/>, a landblock that
/// arrived late) changes; a cell is 24 m, so the weights stay those of the cell's entry point while the listener crosses it, as
/// retail's do. <c>Ambient::UseTime</c> (0x005524A0), which <c>SmartBox::UseTime</c> runs, is per frame. A 0 cell (no viewer) is
/// <see cref="Reset"/>.</item>
/// <item>The scan is <c>CLandBlock::add_ambient_sounds</c> (0x00531050): each of the 64 cells' south-west vertex, its terrain
/// type (bits 2 to 6 of the terrain word) and scene slot (bits 11 to 15), looked up as <c>CRegionDesc::GetSTBDesc</c>
/// (0x004FF530; <see cref="RegionSoundData.TableIndexOf"/>) and added by <c>Ambient::AddSound</c> (0x00552230).</item>
/// <item><c>Ambient::CalcWeight</c> (0x005519F0): 1 inside 20 m, 400 over the squared distance out to 120 m, 0 beyond.
/// <c>Ambient::CalcDir</c> (0x00551A60): 0 inside about 14 m, else one of eight compass sectors. <c>IntermitSound::AddTo</c>
/// (0x00552070) and <c>ConstantSound::AddTo</c> (0x00551C20) sum the weights; <c>IntermitSound::AddDir</c> (0x00551910) keeps a
/// distance range per sector.</item>
/// <item><c>IntermitSound::UpdateSound</c> (0x00551F30): chance = base chance / total weight × the sound's weight.
/// <c>ConstantSound::UpdateSound</c> (0x00552160): volume = volume / total weight × the sound's weight.</item>
/// <item><c>Ambient::Play</c> (0x005523C0): <c>CanHear</c> (<c>IntermitSound</c> 0x00551BA0: chance above 0;
/// <c>ConstantSound</c> 0x00551BF0: volume at least 0.03 and a sound table), <c>PlayNow</c> (<c>IntermitSound</c> 0x00551BC0:
/// <c>RollDice(0, 1)</c> not above the chance), then <c>GetSoundPos</c> (<c>IntermitSound</c> 0x00551F70), then the sound, then
/// the next play at now plus <c>GetPlayInterval</c> (<c>IntermitSound</c> 0x00551CA0: <c>RollDice(min rate, max rate)</c>;
/// <c>ConstantSound</c> 0x00551CC0: the min rate). A sound that cannot be heard leaves the queue until the next update finds it
/// audible.</item>
/// <item>The sound itself is <c>Ambient::PlaySoundA</c> (11.6096 0x005519B0; 0x00550D90 is its 11.4186 address) into <c>SoundManager::PlayAmbientSound</c> (0x00551430) or
/// <c>PlayAmbientSoundFromCenter</c> (0x005514C0): the sink's table entry for the type, its probability roll, the attenuation.</item>
/// </list>
/// <para><b>Intermittent sounds</b> are placed: <c>GetSoundPos</c> draws a sector among those the sound was added from, a heading
/// within a sixteenth of a turn of the sector's, and a distance that is the sector's near distance plus the square of a uniform
/// draw times the range (retail's quadratic bias). <b>Constant sounds</b> (chance 0 in the dat) are played from the centre at the
/// weighted volume every min-rate seconds.</para>
/// <para><b>Lifestone's differences, each named.</b> Retail's scan covers the loaded full-detail landblocks; the radius is 120 m, so
/// this scans the 3 x 3 landblocks around the listener's, which cover it whatever the load radius is. Retail's listener position
/// is the camera; the host passes it. A cell the listener is in is "outdoors" only if it is an outdoor land cell:
/// <c>TODO(ambient-seen-outside)</c> — retail also counts an EnvCell that <c>seen_outside</c> (a doorway), which Lifestone does not
/// know. <c>ConstantSound</c> has no <c>PlayNow</c> or <c>GetSoundPos</c> of its own in the symbol table; the base class's are
/// taken as "always" and "none" (<c>TODO(ambient-constant-defaults)</c>). The sink plays an ambient sound on the effects slider
/// and bus, as retail's ambient slider and enable flag are not in <see cref="AudioLevels"/> (<c>TODO(ambient-slider)</c>), and it
/// uses its own probability roll. The dice are this player's own (seeded by the host), as <see cref="RetailSoundPlayer"/>'s are.</para>
/// <para>Frame thread only.</para>
/// </remarks>
public sealed class AmbientSoundPlayer
{
    /// <summary><c>Ambient::ambient_sound_min_dist</c> (metres): sounds nearer than twice this get no direction.</summary>
    public const float MinDistance = 20f;

    /// <summary><c>Ambient::ambient_sound_min_dist_sq</c>.</summary>
    public const float MinDistanceSquared = 400f;

    /// <summary><c>Ambient::ambient_sound_max_dist_sq</c> (120 m).</summary>
    public const float MaxDistanceSquared = 14400f;

    /// <summary><c>Ambient::ambient_sound_min_vol</c>: a constant sound weaker than this is not heard.</summary>
    public const float MinVolume = 0.029999999f;

    /// <summary>Retail's <c>upper_bound</c> constant (a sixteenth of a turn times two): the heading spread of a placed sound.</summary>
    public const float HeadingSpread = 0.39269909f;

    private const int SectorCount = 8;

    // LandDefs::Direction: 0 in the viewer's own square, then the eight compass sectors (retail's NORTH_OF_VIEWER ... SOUTHEAST_OF_VIEWER).
    private const int InViewer = 0;
    private const int North = 1;
    private const int South = 2;
    private const int East = 3;
    private const int West = 4;
    private const int NorthWest = 5;
    private const int SouthWest = 6;
    private const int NorthEast = 7;
    private const int SouthEast = 8;

    private readonly RegionSoundData _region;
    private readonly float[] _heights;
    private readonly IAmbientTerrain _terrain;
    private readonly ISoundSink _sink;
    private readonly IEffectRandom _dice;
    private readonly AmbientEntry[] _entries;
    private readonly int[] _tableStart;
    private readonly uint[] _tableIds;
    private readonly bool[] _playable;
    private readonly int[] _tableLength;
    private readonly AmbientSound[] _sounds;
    private readonly AmbientSound[] _active;
    private int _activeCount;
    private float _totalWeight;
    private CellFrame _listener;
    private uint _scanCell;
    private bool _scanOutdoors;
    private int _scanRevision;
    private bool _scanned;

    /// <summary>Creates a player.</summary>
    /// <param name="region">The region's ambient tables (<see cref="RegionSoundData.FromRegion"/>).</param>
    /// <param name="landHeightTable">The region's height table (<c>Dats.TerrainRegion.LandHeightTable</c>): a vertex's height from its index.</param>
    /// <param name="terrain">The landblock terrain source.</param>
    /// <param name="sink">Where the sounds go (normally <c>AudioService.Sound</c>).</param>
    /// <param name="dice">Retail's <c>Random::RollDice(float, float)</c>; <see cref="RetailRandom"/>.</param>
    /// <param name="uiSoundTableId">The UI sound table the environ sounds play from (0 when the host has none: they are counted and dropped).</param>
    public AmbientSoundPlayer(RegionSoundData region, IReadOnlyList<float> landHeightTable, IAmbientTerrain terrain, ISoundSink sink, IEffectRandom dice, uint uiSoundTableId = 0)
    {
        _region = region ?? throw new ArgumentNullException(nameof(region));
        ArgumentNullException.ThrowIfNull(landHeightTable);
        _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _dice = dice ?? throw new ArgumentNullException(nameof(dice));
        UiSoundTableId = uiSoundTableId;
        _heights = [.. landHeightTable];

        var tables = region.Tables;
        _tableStart = new int[tables.Count];
        _tableIds = new uint[tables.Count];
        _playable = new bool[tables.Count];
        _tableLength = new int[tables.Count];
        var total = 0;
        for (var t = 0; t < tables.Count; t++)
        {
            _tableStart[t] = total;
            _tableIds[t] = tables[t].SoundTableId;
            _playable[t] = tables[t].IsPlayable;
            _tableLength[t] = tables[t].Sounds.Count;
            total += _tableLength[t];
        }

        _entries = new AmbientEntry[total];
        _sounds = new AmbientSound[total];
        _active = new AmbientSound[total];
        for (var t = 0; t < tables.Count; t++)
        {
            for (var i = 0; i < _tableLength[t]; i++)
            {
                var e = tables[t].Sounds[i];
                var flat = _tableStart[t] + i;
                _entries[flat] = new AmbientEntry(e.SoundType, e.Volume, e.BaseChance, e.MinRate, e.MaxRate);
                _sounds[flat] = new AmbientSound(t, flat, e.IsConstant);
            }
        }
    }

    /// <summary>The UI sound table the environ sounds play from (retail <c>ClientUISystem::GetUISoundTable</c>).</summary>
    public uint UiSoundTableId { get; set; }

    /// <summary>Retail's <c>SoundManager::ambient_sounds_enabled</c>: when false, <see cref="Update"/> does nothing.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Updates run.</summary>
    public int Frames { get; private set; }

    /// <summary>Re-weighting passes run (a change of the listener's cell, of the outdoors flag, or a landblock that arrived late); at most <see cref="Frames"/>.</summary>
    public int Scans { get; private set; }

    /// <summary>Updates run with the listener outdoors (the scan ran).</summary>
    public int OutdoorFrames { get; private set; }

    /// <summary>
    /// Chance draws taken: every time a sound that could be heard came due and was asked whether to play (retail
    /// <c>IntermitSound::PlayNow</c>; a constant sound always plays and counts here too). <c>Draws == Played + Skipped</c>.
    /// </summary>
    public int Draws { get; private set; }

    /// <summary>Draws that played: handed to the sink (the sink's own probability roll and attenuation may still keep one silent).</summary>
    public int Played { get; private set; }

    /// <summary>Draws the chance roll refused.</summary>
    public int Skipped { get; private set; }

    /// <summary>Environ sounds (AdminEnvirons 101 to 123) played through <see cref="ApplyEnviron"/>.</summary>
    public int EnvironSounds { get; private set; }

    /// <summary>Environ options applied that this player cannot show (the fog options and the clear: <c>TODO(m3-environs-fog)</c>), counted.</summary>
    public int EnvironFogIgnored { get; private set; }

    /// <summary>Sounds the scan has met so far (retail's <c>Ambient::num_sounds</c>).</summary>
    public int ActiveSounds => _activeCount;

    /// <summary>The sum of the weights the last scan found (retail <c>total_sound_count</c>); 0 indoors.</summary>
    public float TotalWeight => _totalWeight;

    /// <summary>
    /// One frame: when the listener's cell changed (<c>CellManager::ChangePosition</c>'s new-cell branch) re-weights the sounds around
    /// <paramref name="listener"/> if it is outdoors and starts or stops queued sounds; every frame, plays the ones that came due
    /// (<c>Ambient::UseTime</c>). Allocation-free.
    /// </summary>
    /// <param name="listener">The listener (retail <c>SoundManager::player_position_</c>: the camera).</param>
    /// <param name="outdoors">The listener's cell is an outdoor cell.</param>
    /// <param name="now">Seconds on a monotonic clock (retail <c>Timer::cur_time</c>).</param>
    public void Update(in CellFrame listener, bool outdoors, double now)
    {
        if (!Enabled)
        {
            return;
        }

        Frames++;
        _listener = listener;
        if (outdoors)
        {
            OutdoorFrames++;
        }

        var revision = _terrain.Revision;
        if (!_scanned || listener.CellId != _scanCell || outdoors != _scanOutdoors || revision != _scanRevision)
        {
            _scanned = true;
            _scanCell = listener.CellId;
            _scanOutdoors = outdoors;
            _scanRevision = revision;
            Scans++;
            InitSounds();
            if (outdoors)
            {
                AddAmbientSounds(listener);
            }

            UpdatePlayQueue(now);
        }

        UseTime(now);
    }

    /// <summary>
    /// The viewer is gone (retail's <c>CellManager::ChangePosition</c> with a 0 cell: the world is not shown, or the player is not in it): the weights are zeroed
    /// (<c>Ambient::InitSounds</c>) so a queued sound that comes due is dropped unplayed, and the next <see cref="Update"/> scans again. Draws are not made while reset.
    /// </summary>
    public void Reset()
    {
        if (!_scanned)
        {
            return;
        }

        _scanned = false;
        InitSounds();
    }

    /// <summary>
    /// Applies an AdminEnvirons option's sound half (<c>CPlayerSystem::Handle_Admin__Environs</c>, 11.6096 0x0055EB40): a global
    /// sound from the centre of the UI sound table (<c>SoundManager::PlaySoundFromCenter</c>, 0x00551560). The fog options are not
    /// drawn in M3; they are counted in <see cref="EnvironFogIgnored"/>. Returns true when a sound was handed to the sink.
    /// </summary>
    public bool ApplyEnviron(in EnvironEffect effect)
    {
        if (effect.Kind is EnvironKind.Fog or EnvironKind.Clear)
        {
            EnvironFogIgnored++;
            return false;
        }

        if (effect.Kind != EnvironKind.Sound || UiSoundTableId == 0)
        {
            return false;
        }

        EnvironSounds++;
        _sink.PlayFromCenter(UiSoundTableId, effect.SoundType);
        return true;
    }

    /// <summary><c>Ambient::InitSounds</c>: zero the weights.</summary>
    private void InitSounds()
    {
        _totalWeight = 0f;
        for (var i = 0; i < _activeCount; i++)
        {
            _active[i].ResetCount();
        }
    }

    /// <summary><c>LScape::add_ambient_sounds</c> over the 3 x 3 landblocks around the listener's.</summary>
    private void AddAmbientSounds(in CellFrame listener)
    {
        var home = LandblockCoord.FromId(listener.CellId);
        for (var bx = -1; bx <= 1; bx++)
        {
            for (var by = -1; by <= 1; by++)
            {
                if (home.Offset(bx, by) is { } coord && _terrain.Get(coord) is { } terrain)
                {
                    AddLandblock(terrain, bx, by, listener.Origin);
                }
            }
        }
    }

    /// <summary><c>CLandBlock::add_ambient_sounds</c>.</summary>
    private void AddLandblock(LandblockTerrain terrain, int blockDx, int blockDy, in System.Numerics.Vector3 listener)
    {
        var words = terrain.TerrainWords;
        var heights = terrain.HeightIndices;
        var offsetX = (blockDx * TerrainConstants.LandblockSize) - listener.X;
        var offsetY = (blockDy * TerrainConstants.LandblockSize) - listener.Y;
        for (var x = 0; x < TerrainConstants.CellsPerSide; x++)
        {
            for (var y = 0; y < TerrainConstants.CellsPerSide; y++)
            {
                var index = TerrainConstants.VertexIndex(x, y);
                int word = words[index];
                var terrainType = (word >> 2) & 0x1F;
                var slot = word >> 11;
                if (slot >= _region.SceneSlotCount(terrainType))
                {
                    continue;
                }

                var table = _region.TableIndexOf(terrainType, slot);
                if (table < 0 || !_playable[table])
                {
                    continue;
                }

                var dx = offsetX + (x * TerrainConstants.CellSize);
                var dy = offsetY + (y * TerrainConstants.CellSize);
                var heightIndex = heights[index];
                var dz = (heightIndex < _heights.Length ? _heights[heightIndex] : 0f) - listener.Z;
                AddSound(table, dx, dy, dz);
            }
        }
    }

    /// <summary><c>Ambient::AddSound</c>.</summary>
    private void AddSound(int table, float dx, float dy, float dz)
    {
        if ((dz * dz) + (dy * dy) + (dx * dx) > MaxDistanceSquared)
        {
            return;
        }

        var weight = CalcWeight(dx, dy, dz);
        var direction = CalcDirection(dx, dy);
        if (weight == 0f)
        {
            return;
        }

        _totalWeight += weight;
        var start = _tableStart[table];
        for (var i = 0; i < _tableLength[table]; i++)
        {
            var sound = GetSound(start + i);
            if (sound.Constant)
            {
                sound.Count += weight; // ConstantSound::AddTo
            }
            else
            {
                AddToIntermit(sound, weight, dx, dy, dz, direction);
            }
        }
    }

    /// <summary><c>Ambient::GetSound</c>: the sound object of a table's sound, made (and listed) the first time it is met.</summary>
    private AmbientSound GetSound(int flat)
    {
        var sound = _sounds[flat];
        if (!sound.Listed)
        {
            sound.Listed = true;
            _active[_activeCount++] = sound;
        }

        return sound;
    }

    /// <summary><c>Ambient::CalcWeight</c>.</summary>
    internal static float CalcWeight(float dx, float dy, float dz)
    {
        var d2 = (dx * dx) + (dy * dy) + (dz * dz);
        if (d2 > MaxDistanceSquared)
        {
            return 0f;
        }

        return d2 >= MinDistanceSquared ? MinDistanceSquared / d2 : 1f;
    }

    /// <summary><c>Ambient::CalcDir</c>: 0 (in the viewer's square) or the compass sector of the offset from the listener.</summary>
    internal static int CalcDirection(float dx, float dy)
    {
        var ax = MathF.Abs(dx);
        var ay = MathF.Abs(dy);
        if (MinDistanceSquared * 0.5f > (dy * dy) + (dx * dx))
        {
            return InViewer;
        }

        if (ax < 0.0002f || ay / ax > 2f)
        {
            return dy < 0f ? South : North;
        }

        if (ay < 0.0002f || ax / ay > 2f)
        {
            return dx < 0f ? West : East;
        }

        if (dx < 0f)
        {
            return dy < 0f ? SouthWest : NorthWest;
        }

        return dy < 0f ? SouthEast : NorthEast;
    }

    /// <summary><c>LandDefs::heading(Direction)</c> (11.6096 0x005AAAE0): the sector's compass heading in radians, 0 for any other.</summary>
    internal static float SectorHeading(int direction) => direction switch
    {
        South => 3.1415927f,
        East => 1.5707964f,
        West => 4.712389f,
        NorthEast => 0.78539819f,
        SouthEast => 2.3561945f,
        SouthWest => 3.9269907f,
        NorthWest => 5.497787f,
        _ => 0f,
    };

    /// <summary><c>IntermitSound::AddTo</c>.</summary>
    private static void AddToIntermit(AmbientSound sound, float weight, float dx, float dy, float dz, int direction)
    {
        var offset = MinDistance * 0.5f;
        var distance = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        sound.Count += weight;
        if (direction != InViewer)
        {
            sound.AddDirection(direction, distance - offset, offset + distance);
            return;
        }

        // Inside the viewer's square: every sector, near distance 4, far distance 10.
        const float near = 5f - 1f;
        sound.AddDirection(North, near, offset);
        sound.AddDirection(South, near, offset);
        sound.AddDirection(East, near, offset);
        sound.AddDirection(West, near, offset);
        sound.AddDirection(NorthWest, near, offset);
        sound.AddDirection(NorthEast, near, offset);
        sound.AddDirection(SouthWest, near, offset);
        sound.AddDirection(SouthEast, near, offset);
    }

    /// <summary><c>Ambient::UpdatePlayQueue</c>: re-weight every sound; one that is not queued gets its turn now.</summary>
    private void UpdatePlayQueue(double now)
    {
        for (var i = 0; i < _activeCount; i++)
        {
            var sound = _active[i];
            ref readonly var entry = ref _entries[sound.Flat];
            if (sound.Constant)
            {
                // ConstantSound::UpdateSound
                sound.CurrentVolume = sound.Count == 0f ? 0f : entry.Volume / _totalWeight * sound.Count;
            }
            else if (sound.Count > 0f)
            {
                // IntermitSound::UpdateSound
                sound.PlayChance = entry.BaseChance / _totalWeight * sound.Count;
            }

            if (!sound.OnQueue)
            {
                Play(sound, now);
            }
        }
    }

    /// <summary><c>Ambient::UseTime</c>: play what came due, earliest first.</summary>
    private void UseTime(double now)
    {
        while (true)
        {
            AmbientSound? first = null;
            for (var i = 0; i < _activeCount; i++)
            {
                var sound = _active[i];
                if (sound.OnQueue && (first is null || sound.Due < first.Due))
                {
                    first = sound;
                }
            }

            if (first is null || first.Due >= now)
            {
                return;
            }

            first.OnQueue = false;
            Play(first, now);
        }
    }

    /// <summary><c>Ambient::Play</c>.</summary>
    private void Play(AmbientSound sound, double now)
    {
        if (!CanHear(sound))
        {
            sound.OnQueue = false;
            return;
        }

        ref readonly var entry = ref _entries[sound.Flat];
        Draws++;
        if (sound.Constant)
        {
            // ConstantSound: PlayNow is the base's (always), GetSoundPos the base's (none): played from the centre.
            Played++;
            _sink.PlayFromTable(_tableIds[sound.Table], entry.SoundType, SoundOrigin.Center, sound.CurrentVolume);
        }
        else if (_dice.RollDice(0f, 1f) <= sound.PlayChance)
        {
            Played++;
            var origin = SoundPosition(sound);
            _sink.PlayFromTable(_tableIds[sound.Table], entry.SoundType, origin, entry.Volume);
        }
        else
        {
            Skipped++;
        }

        var interval = sound.Constant ? entry.MinRate : (float)_dice.RollDice(entry.MinRate, entry.MaxRate);
        sound.Due = now + (interval > 0f ? interval : 0f);
        sound.OnQueue = true;
    }

    /// <summary><c>IntermitSound::CanHear</c> and <c>ConstantSound::CanHear</c>.</summary>
    private bool CanHear(AmbientSound sound) =>
        sound.Constant
            ? sound.CurrentVolume >= MinVolume && _playable[sound.Table]
            : sound.PlayChance > 0f;

    /// <summary><c>IntermitSound::GetSoundPos</c>: where the sound is, as the listener hears it.</summary>
    private SoundOrigin SoundPosition(AmbientSound sound)
    {
        var count = Math.Max(sound.DirectionCount, 1);
        var pick = (int)Math.Floor(_dice.RollDice(0f, count));
        if (pick >= count)
        {
            pick = count - 1;
        }

        var direction = sound.DirectionCount == 0 ? North : sound.Directions[pick];
        var spread = (float)_dice.RollDice(0f, HeadingSpread);
        var heading = SectorHeading(direction) + spread - (HeadingSpread * 0.5f);
        var near = sound.DirectionCount == 0 ? 0f : sound.Near[pick];
        var far = sound.DirectionCount == 0 ? 0f : sound.Far[pick];
        var t = (float)_dice.RollDice(0f, 1f);
        var distance = (t * ((far - near) * t)) + near;
        var origin = _listener.Origin;
        var moved = new System.Numerics.Vector3(
            (MathF.Sin(heading) * distance) + origin.X,
            (distance * MathF.Cos(heading)) + origin.Y,
            origin.Z);
        return SoundOrigin.Between(_listener, new CellFrame(_listener.CellId, new AcFrame(moved, _listener.Frame.Orientation)));
    }

    private readonly record struct AmbientEntry(uint SoundType, float Volume, float BaseChance, float MinRate, float MaxRate);

    /// <summary>Retail's <c>AmbientSound</c> (an <c>IntermitSound</c> or a <c>ConstantSound</c>).</summary>
    private sealed class AmbientSound(int table, int flat, bool constant)
    {
        public readonly int Table = table;
        public readonly int Flat = flat;
        public readonly bool Constant = constant;
        public readonly int[] Directions = new int[SectorCount];
        public readonly float[] Near = new float[SectorCount];
        public readonly float[] Far = new float[SectorCount];

        public bool Listed;
        public bool OnQueue;
        public double Due;
        public float Count;
        public float PlayChance;
        public float CurrentVolume;
        public int DirectionCount;

        /// <summary><c>IntermitSound::ResetCount</c> and <c>ConstantSound::ResetCount</c>.</summary>
        public void ResetCount()
        {
            Count = 0f;
            DirectionCount = 0;
            PlayChance = 0f;
            if (Constant)
            {
                CurrentVolume = 0f;
            }
        }

        /// <summary><c>IntermitSound::AddDir</c>: a new sector, or the range of an existing one widened.</summary>
        public void AddDirection(int direction, float near, float far)
        {
            var i = 0;
            while (i < DirectionCount && Directions[i] != direction)
            {
                i++;
            }

            if (i == DirectionCount)
            {
                Directions[i] = direction;
                Far[i] = far;
                Near[i] = near;
                DirectionCount++;
                return;
            }

            if (near < Near[i])
            {
                Near[i] = near;
            }

            if (far > Far[i])
            {
                Far[i] = far;
            }
        }
    }
}
