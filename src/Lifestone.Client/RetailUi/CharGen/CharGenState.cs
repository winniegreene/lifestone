using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Appearance;
using Lifestone.Data.CharacterCreation;

namespace Lifestone.Client.RetailUi.CharacterCreation;

/// <summary>
/// The character being built in CharGen: heritage, gender, attributes, skills, profession template, appearance, start
/// area and name, with retail's rules for each. The six CharGen pages (G3) read and change it; <see cref="GetResult"/>
/// gives the session's create intent.
/// </summary>
/// <remarks>
/// <para>
/// Re-expresses retail's <c>CharGenState</c> (AC/accharacter/CharGenState.c; constructor 11.4186 0x005C6A50, 11.6096
/// 0x005C7A30) member by member; every method names its retail function. No decompiled code is copied. Attribute ids are
/// retail's: 1 Strength, 2 Endurance, 3 Quickness, 4 Coordination, 5 Focus, 6 Self.
/// </para>
/// <para>
/// <b>The page flow</b> (gmCharGenMainUI, 11.6096 0x004E9070): opening CharGen randomises a whole character
/// (<see cref="RandomizeCharacter"/>), then the heritage page's buttons call <see cref="SetHeritageGroup"/>
/// (<c>gmCGHeritagePage::ListenToElementMessage</c>, 11.4186 0x00483860, 11.6096 0x00483C20), which keeps the appearance
/// it can and fits the rest to the new heritage.
/// </para>
/// <para>
/// <b>Left to other code.</b> <c>ClientCharGenState::UpdateTrueFacePal</c> (the face palette of the 3D view) belongs to
/// CharGen's view (avatars AV12, G3); the verification state belongs to the main page (G3); administrator and envoy
/// characters are never created.
/// </para>
/// <para>
/// <b>Deviations.</b> Retail keeps <see cref="BalanceAttributes"/>' round-robin start in the global <c>startAtrb</c>,
/// shared by every CharGen session of the process; here it is per state (it differs only when CharGen opens twice in one
/// run). Its first value is retail's: <c>startAtrb</c> is initialised to 1 (Strength) in the 11.4186 data
/// (<c>acclient_data.h</c> of Mag-nus/acclient-AI-RE), and 11.6096 is assumed unchanged (logged once by the character creation screen: <c>TODO(retailui-chargen-11-6096)</c>). Retail's balance loop never ends
/// when no attribute can give a point; here a pass that lowers nothing ends it. A style index outside its list, which retail would read past the end of, gives no
/// colours.
/// </para>
/// </remarks>
public sealed class CharGenState
{
    /// <summary>The lowest attribute value (<c>atrbMin</c>).</summary>
    public const int AttributeMin = 10;

    /// <summary>The highest attribute value (<c>atrbMax</c>).</summary>
    public const int AttributeMax = 100;

    /// <summary>Retail's "not chosen" shade (-1.0).</summary>
    public const double UnsetShade = -1.0;

    /// <summary>The Setup CharGen's view starts from before a heritage is chosen (<c>Reset</c>: 0x02000054).</summary>
    public const uint DefaultSetupId = 0x02000054u;

    private static readonly CharGenHeritage EmptyHeritage = new(0, string.Empty, 0, 0, 0, 0, 0, [], [], [], [], new Dictionary<uint, CharGenGender>());

    private readonly CharGenCatalog _catalog;
    private readonly ICharGenRandom _random;
    private readonly SkillAdvancementClass[] _skillLevels;
    private readonly bool[] _attributeLocked = new bool[7];
    private readonly ClothingSlot _headgear = new();
    private readonly ClothingSlot _shirt = new();
    private readonly ClothingSlot _trousers = new();
    private readonly ClothingSlot _footwear = new();
    private uint _balanceStart = 1;

    /// <summary>
    /// <c>CharGenState::CharGenState</c>: an empty character (<see cref="Reset"/>), with one skill slot per id up to the
    /// SkillTable's highest (55) and the skills listed by name for the skills page.
    /// </summary>
    public CharGenState(CharGenCatalog catalog, ICharGenRandom random)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(random);
        _catalog = catalog;
        _random = random;
        _skillLevels = new SkillAdvancementClass[catalog.Skills.TotalNumSkills];
        SkillsByName = catalog.Skills.Skills.OrderBy(s => s.Name.Length > 30 ? s.Name[..30] : s.Name, StringComparer.Ordinal).ToArray();
        Reset();
    }

    /// <summary>The heritage (0 = none chosen).</summary>
    public uint Heritage { get; private set; }

    /// <summary>The gender (0 = none chosen; 1 male, 2 female).</summary>
    public uint Gender { get; private set; }

    /// <summary>Strength.</summary>
    public int Strength { get; private set; }

    /// <summary>Endurance.</summary>
    public int Endurance { get; private set; }

    /// <summary>Coordination.</summary>
    public int Coordination { get; private set; }

    /// <summary>Quickness.</summary>
    public int Quickness { get; private set; }

    /// <summary>Focus.</summary>
    public int Focus { get; private set; }

    /// <summary>Self.</summary>
    public int Self { get; private set; }

    /// <summary>The heritage's attribute points (<c>totalAtrbCredits</c>).</summary>
    public int TotalAttributeCredits { get; private set; }

    /// <summary>Attribute points not spent (<c>remainingAtrbCredits</c>; negative when overspent).</summary>
    public int RemainingAttributeCredits { get; private set; }

    /// <summary>The heritage's skill credits (<c>totalSkillCredits</c>).</summary>
    public int TotalSkillCredits { get; private set; }

    /// <summary>Skill credits not spent (<c>remainingSkillCredits</c>).</summary>
    public int RemainingSkillCredits { get; private set; }

    /// <summary>The profession template (-1 = none; 0 is the custom one).</summary>
    public int Template { get; private set; }

    /// <summary>The eye strip index.</summary>
    public int EyesStrip { get; set; }

    /// <summary>The nose strip index.</summary>
    public int NoseStrip { get; set; }

    /// <summary>The mouth strip index.</summary>
    public int MouthStrip { get; set; }

    /// <summary>The hair style index.</summary>
    public int HairStyle { get; set; }

    /// <summary>The hair colour index (<see cref="SetHairColor"/>).</summary>
    public int HairColor { get; private set; }

    /// <summary>The eye colour index (<see cref="SetEyeColor"/>).</summary>
    public int EyeColor { get; private set; }

    /// <summary>The skin shade, 0-1 (-1 before one is chosen).</summary>
    public double SkinShade { get; private set; }

    /// <summary>The hair shade, 0-1 (-1 before one is chosen).</summary>
    public double HairShade { get; private set; }

    /// <summary>The headgear style (-1 = none) and its colour index and shade.</summary>
    public int HeadgearStyle => _headgear.Style;

    /// <summary>The headgear colour index into <see cref="HeadgearColors"/>.</summary>
    public int HeadgearColor { get => _headgear.Color; set => _headgear.Color = value; }

    /// <summary>The headgear shade.</summary>
    public double HeadgearShade { get => _headgear.Shade; set => _headgear.Shade = value; }

    /// <summary>The shirt style.</summary>
    public int ShirtStyle => _shirt.Style;

    /// <summary>The shirt colour index into <see cref="ShirtColors"/>.</summary>
    public int ShirtColor { get => _shirt.Color; set => _shirt.Color = value; }

    /// <summary>The shirt shade.</summary>
    public double ShirtShade { get => _shirt.Shade; set => _shirt.Shade = value; }

    /// <summary>The trousers style.</summary>
    public int TrousersStyle => _trousers.Style;

    /// <summary>The trousers colour index into <see cref="TrousersColors"/>.</summary>
    public int TrousersColor { get => _trousers.Color; set => _trousers.Color = value; }

    /// <summary>The trousers shade.</summary>
    public double TrousersShade { get => _trousers.Shade; set => _trousers.Shade = value; }

    /// <summary>The footwear style.</summary>
    public int FootwearStyle => _footwear.Style;

    /// <summary>The footwear colour index into <see cref="FootwearColors"/>.</summary>
    public int FootwearColor { get => _footwear.Color; set => _footwear.Color = value; }

    /// <summary>The footwear shade.</summary>
    public double FootwearShade { get => _footwear.Shade; set => _footwear.Shade = value; }

    /// <summary>The headgear's colours (<c>headgearPaletteTemplateIDs</c> and <c>headgearPalSetIDs</c>).</summary>
    public IReadOnlyList<CharGenClothingColor> HeadgearColors => _headgear.Colors;

    /// <summary>The shirt's colours.</summary>
    public IReadOnlyList<CharGenClothingColor> ShirtColors => _shirt.Colors;

    /// <summary>The trousers' colours.</summary>
    public IReadOnlyList<CharGenClothingColor> TrousersColors => _trousers.Colors;

    /// <summary>The footwear's colours.</summary>
    public IReadOnlyList<CharGenClothingColor> FootwearColors => _footwear.Colors;

    /// <summary>The start area index (-1 = none).</summary>
    public int StartArea { get; private set; }

    /// <summary>The name, as formatted (<see cref="CharGenNames.Format"/>).</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The character slot (<c>SetSlot</c>; the session chooses the slot it sends).</summary>
    public int Slot { get; set; }

    /// <summary>The Setup the heritage's view starts from (<c>setupID</c>).</summary>
    public uint SetupId { get; private set; }

    /// <summary>Whether <see cref="SetupId"/> changed since the view last cleared it (<c>setupChanged</c>).</summary>
    public bool SetupChanged { get; set; }

    /// <summary>Set by <see cref="RandomizeCharacter"/>: the pages do not re-randomise these parts.</summary>
    public bool HeritageGroupFrozen { get; set; }

    /// <summary>Set by <see cref="RandomizeCharacter"/>.</summary>
    public bool SexFrozen { get; set; }

    /// <summary>Set by <see cref="RandomizeCharacter"/>.</summary>
    public bool AppearanceFrozen { get; set; }

    /// <summary>Cleared by <see cref="Reset"/>.</summary>
    public bool ClothingFrozen { get; set; }

    /// <summary>The number of skill slots (the SkillTable's highest id plus one, 55).</summary>
    public int TotalNumSkills => _skillLevels.Length;

    /// <summary>The skills in the order the skills page lists them: by name (<c>skillRecordList</c>, first 30 characters).</summary>
    public IReadOnlyList<SkillTableEntry> SkillsByName { get; }

    /// <summary>The data the state reads.</summary>
    public CharGenCatalog Catalog => _catalog;

    /// <summary><c>CharGenState::Reset</c> (11.4186 0x005C68A0, 11.6096 0x005C7880): an empty character.</summary>
    public void Reset()
    {
        HeritageGroupFrozen = false;
        SexFrozen = false;
        AppearanceFrozen = false;
        ClothingFrozen = false;
        Array.Clear(_attributeLocked);
        SetHeritageGroup(0);
        SetGender(0);
        ClearAppearance();
        Template = -1;
        TotalAttributeCredits = 0;
        RemainingAttributeCredits = 0;
        TotalSkillCredits = 0;
        RemainingSkillCredits = 0;
        SetName(string.Empty);
        StartArea = -1;
        ApplyDefaultTemplate();
        SetupChanged = false;
        SetupId = DefaultSetupId;
    }

    // ---- Heritage and gender -------------------------------------------------------------------------------------

    /// <summary>
    /// <c>CharGenState::SetHeritageGroup</c> (11.4186 0x005C67A0, 11.6096 0x005C7780): takes the heritage's credits and
    /// Setup, applies the current template, picks a start area, and fits everything else to the heritage.
    /// </summary>
    public void SetHeritageGroup(uint heritage)
    {
        Heritage = heritage;
        if (heritage != 0)
        {
            var hg = HeritageOrEmpty(heritage);
            if (hg.SetupId != SetupId)
            {
                SetupChanged = true;
                SetupId = hg.SetupId;
            }

            TotalAttributeCredits = (int)hg.AttributeCredits;
            RemainingAttributeCredits = TotalAttributeCredits - AttributeSum();
            TotalSkillCredits = (int)hg.SkillCredits;
            RemainingSkillCredits = TotalSkillCredits;
            ApplyTemplate();
            RandomizeStartArea();
        }

        ConstrainAllByHeritage();
        RefreshClothing();
    }

    /// <summary><c>CharGenState::SetGender</c> (11.4186 0x005C64A0, 11.6096 0x005C7480).</summary>
    public void SetGender(uint gender)
    {
        Gender = gender;
        ConstrainAllByGender();
        RefreshClothing();
    }

    /// <summary>
    /// <c>CharGenState::ConstrainAllByHeritage</c> (11.4186 0x005C6590, 11.6096 0x005C7570): fits the gender's choices,
    /// the template and the credits to the heritage; with no heritage, clears them.
    /// </summary>
    public void ConstrainAllByHeritage()
    {
        if (Heritage != 0)
        {
            var hg = HeritageOrEmpty(Heritage);
            ConstrainAllByGender();
            if (Template >= hg.Templates.Count)
            {
                Template = -1;
            }

            if (TotalAttributeCredits > hg.AttributeCredits)
            {
                RemainingAttributeCredits = -AttributeSum();
                TotalAttributeCredits = 0;
                SetStrength(0, balance: false);
                SetEndurance(TotalAttributeCredits / 6, balance: false);
                SetCoordination(TotalAttributeCredits / 6, balance: false);
                SetQuickness(TotalAttributeCredits / 6, balance: false);
                SetFocus(TotalAttributeCredits / 6, balance: false);
                SetSelf(TotalAttributeCredits / 6, balance: false);
            }

            if (TotalSkillCredits > hg.SkillCredits)
            {
                TotalSkillCredits = (int)hg.SkillCredits;
            }

            UpdateRemainingSkillCredits();
            if (RemainingSkillCredits < 0)
            {
                ResetSkillLevels();
            }
        }
        else
        {
            RemainingAttributeCredits = -AttributeSum();
            Template = -1;
            TotalAttributeCredits = 0;
            TotalSkillCredits = 0;
            RemainingSkillCredits = 0;
            SetName(string.Empty);
            StartArea = -1;
            ApplyDefaultTemplate();
        }
    }

    /// <summary>
    /// <c>CharGenState::ConstrainAllByGender</c> (11.4186 0x005C5B80, 11.6096 0x005C6B60): every appearance index above
    /// its list comes down to the last entry; with no heritage or gender, the appearance is cleared.
    /// </summary>
    public void ConstrainAllByGender()
    {
        if (Heritage == 0 || Gender == 0)
        {
            ClearAppearance();
            return;
        }

        var sx = GenderOrEmpty();
        EyesStrip = Clamp(EyesStrip, sx.EyeStrips.Count);
        NoseStrip = Clamp(NoseStrip, sx.NoseStrips.Count);
        MouthStrip = Clamp(MouthStrip, sx.MouthStrips.Count);
        HairStyle = Clamp(HairStyle, sx.HairStyles.Count);
        EyeColor = Clamp(EyeColor, sx.EyeColors.Count);
        HairColor = Clamp(HairColor, sx.HairColors.Count);
        if (_headgear.Style >= sx.Headgears.Count)
        {
            SetHeadgearStyle(sx.Headgears.Count - 1);
        }

        _headgear.Color = Clamp(_headgear.Color, _headgear.Colors.Count);
        if (_shirt.Style >= sx.Shirts.Count)
        {
            SetShirtStyle(sx.Shirts.Count - 1);
        }

        _shirt.Color = Clamp(_shirt.Color, _shirt.Colors.Count);
        if (_trousers.Style >= sx.Pants.Count)
        {
            SetTrousersStyle(sx.Pants.Count - 1);
        }

        _trousers.Color = Clamp(_trousers.Color, _trousers.Colors.Count);
        if (_footwear.Style >= sx.Footwear.Count)
        {
            SetFootwearStyle(sx.Footwear.Count - 1);
        }

        _footwear.Color = Clamp(_footwear.Color, _footwear.Colors.Count);
    }

    // ---- Attributes ----------------------------------------------------------------------------------------------

    /// <summary><c>CharGenState::GetAttribute</c> (11.4186 0x005C39B0, 11.6096 0x005C4990): attribute 1-6, else 0.</summary>
    public int GetAttribute(uint attribute) => attribute switch
    {
        1 => Strength,
        2 => Endurance,
        3 => Quickness,
        4 => Coordination,
        5 => Focus,
        6 => Self,
        _ => 0,
    };

    /// <summary>
    /// <c>CharGenState::GetAttributeName</c> (11.4186 0x005C3A20, 11.6096 0x005C4A00): attribute 1-6's English name as the profession and
    /// skills pages print it (the skill formulas name their attributes with it); the empty string for any other.
    /// </summary>
    public static string GetAttributeName(uint attribute) => attribute switch
    {
        1 => "Strength",
        2 => "Endurance",
        3 => "Quickness",
        4 => "Coordination",
        5 => "Focus",
        6 => "Self",
        _ => string.Empty,
    };

    /// <summary>
    /// <c>CharGenState::GetAbsRemainingCredits</c> (11.4186 0x005C3B20, 11.6096 0x005C4B00): the credits left if every
    /// attribute that is neither locked nor <paramref name="current"/> stood at 10.
    /// </summary>
    public int GetAbsRemainingCredits(uint current)
    {
        var result = TotalAttributeCredits;
        for (uint attribute = 1; attribute < 7; attribute++)
        {
            result -= _attributeLocked[attribute] || attribute == current ? GetAttribute(attribute) : AttributeMin;
        }

        return result;
    }

    /// <summary><c>CharGenState::LockAttribute</c> (11.4186 0x005C3BE0, 11.6096 0x005C4BC0): attribute 1-6 keeps its value while others balance.</summary>
    public void LockAttribute(uint attribute, bool locked)
    {
        if (attribute is >= 1 and < 7)
        {
            _attributeLocked[attribute] = locked;
        }
    }

    /// <summary>Whether attribute <paramref name="attribute"/> is locked.</summary>
    public bool IsAttributeLocked(uint attribute) => attribute is >= 1 and < 7 && _attributeLocked[attribute];

    /// <summary><c>CharGenState::ResetAttributeLock</c> (11.4186 0x005C3BC0, 11.6096 0x005C4BA0).</summary>
    public void ResetAttributeLock() => Array.Clear(_attributeLocked);

    /// <summary><c>CharGenState::SetStrength</c> (11.4186 0x005C4660, 11.6096 0x005C5640); see <see cref="SetAttribute"/>.</summary>
    public int SetStrength(int value, bool balance) => SetAttribute(1, value, balance);

    /// <summary><c>CharGenState::SetEndurance</c> (11.4186 0x005C46E0, 11.6096 0x005C56C0); see <see cref="SetAttribute"/>.</summary>
    public int SetEndurance(int value, bool balance) => SetAttribute(2, value, balance);

    /// <summary><c>CharGenState::SetCoordination</c> (11.4186 0x005C4760, 11.6096 0x005C5740); see <see cref="SetAttribute"/>.</summary>
    public int SetCoordination(int value, bool balance) => SetAttribute(4, value, balance);

    /// <summary><c>CharGenState::SetQuickness</c> (11.4186 0x005C47E0, 11.6096 0x005C57C0); see <see cref="SetAttribute"/>.</summary>
    public int SetQuickness(int value, bool balance) => SetAttribute(3, value, balance);

    /// <summary><c>CharGenState::SetFocus</c> (11.4186 0x005C4860, 11.6096 0x005C5840); see <see cref="SetAttribute"/>.</summary>
    public int SetFocus(int value, bool balance) => SetAttribute(5, value, balance);

    /// <summary><c>CharGenState::SetSelf</c> (11.4186 0x005C48E0, 11.6096 0x005C58C0); see <see cref="SetAttribute"/>.</summary>
    public int SetSelf(int value, bool balance) => SetAttribute(6, value, balance);

    /// <summary>
    /// The six <c>CharGenState::Set*</c> attribute setters: a lower value is always taken; a higher one only while
    /// <see cref="GetAbsRemainingCredits"/> for the attribute is not 0. With <paramref name="balance"/>, the others then
    /// give back any overspend (<see cref="BalanceAttributes"/>). Returns the attribute's value. The setters do not clamp
    /// to 10-100; the profession page does.
    /// </summary>
    public int SetAttribute(uint attribute, int value, bool balance)
    {
        var current = GetAttribute(attribute);
        if (attribute is < 1 or > 6 || (value > current && GetAbsRemainingCredits(attribute) == 0))
        {
            return current;
        }

        Assign(attribute, value);
        if (balance)
        {
            BalanceAttributes(attribute);
        }

        RemainingAttributeCredits = TotalAttributeCredits - AttributeSum();
        return GetAttribute(attribute);
    }

    /// <summary>
    /// <c>CharGenState::BalanceAttributes</c> (11.4186 0x005C3DF0, 11.6096 0x005C4DD0): while the attributes overspend,
    /// lower the others one point at a time, round-robin in the order Strength, Endurance, Coordination, Quickness, Focus,
    /// Self, starting where the last balance stopped, never below 10, skipping <paramref name="fixedAttribute"/> and the
    /// locked ones (see the class remarks for the start and the loop guard).
    /// </summary>
    public void BalanceAttributes(uint fixedAttribute)
    {
        ReadOnlySpan<uint> order = [1, 2, 4, 3, 5, 6];
        var excess = AttributeSum() - TotalAttributeCredits;
        if (excess <= 0)
        {
            return;
        }

        var position = Math.Max(0, order.IndexOf(_balanceStart));
        var idleVisits = 0;
        while (idleVisits < order.Length)
        {
            var attribute = order[position];
            position = (position + 1) % order.Length;
            if (attribute == fixedAttribute || _attributeLocked[attribute] || GetAttribute(attribute) <= AttributeMin)
            {
                idleVisits++;
                continue;
            }

            idleVisits = 0;
            Assign(attribute, GetAttribute(attribute) - 1);
            if (--excess <= 0)
            {
                _balanceStart = order[position];
                return;
            }
        }

        // Retail loops forever here (a whole round lowered nothing); a deviation ends the loop (class remarks).
    }

    // ---- Skills --------------------------------------------------------------------------------------------------

    /// <summary><c>CharGenState::GetSkillLevel</c> (11.4186 0x005C3C00, 11.6096 0x005C4BE0).</summary>
    public SkillAdvancementClass GetSkillLevel(uint skill) => skill < _skillLevels.Length ? _skillLevels[skill] : SkillAdvancementClass.Inactive;

    /// <summary>
    /// <c>CharGenState::SetSkillLevel</c> (11.4186 0x005C3C20, 11.6096 0x005C4C00): moves a skill to untrained, trained or
    /// specialised when the credits allow it, refunding what its current level cost; false (and no change) otherwise.
    /// </summary>
    public bool SetSkillLevel(uint skill, SkillAdvancementClass level)
    {
        if (skill >= _skillLevels.Length || level is < SkillAdvancementClass.Untrained or > SkillAdvancementClass.Specialized)
        {
            return false;
        }

        var remaining = RemainingSkillCredits + Cost(skill, _skillLevels[skill]) - Cost(skill, level);
        if (remaining < 0)
        {
            return false;
        }

        RemainingSkillCredits = remaining;
        _skillLevels[skill] = level;
        return true;
    }

    /// <summary>
    /// <c>CharGenState::ResetSkillLevels</c> (11.4186 0x005C43B0, 11.6096 0x005C5390): every SkillTable skill goes back to
    /// what is free: specialised when both costs are 0 or less, trained when training is free, untrained otherwise.
    /// </summary>
    public void ResetSkillLevels()
    {
        RemainingSkillCredits = TotalSkillCredits;
        if (Heritage == 0)
        {
            return;
        }

        for (uint skill = 1; skill < _skillLevels.Length; skill++)
        {
            if (!_catalog.Skills.TryGetSkill(skill, out _))
            {
                continue;
            }

            var trained = TrainedCost(skill);
            var specialized = SpecializedCost(skill);
            if (trained >= 0 && specialized >= 0)
            {
                _skillLevels[skill] = trained > 0 ? SkillAdvancementClass.Untrained
                    : specialized <= 0 ? SkillAdvancementClass.Specialized : SkillAdvancementClass.Trained;
            }
        }
    }

    /// <summary><c>CharGenState::UpdateRemainingSkillCredits</c> (11.4186 0x005C37C0, 11.6096 0x005C47A0): skill 1 upwards.</summary>
    public void UpdateRemainingSkillCredits()
    {
        var spent = 0;
        for (uint skill = 1; skill < _skillLevels.Length; skill++)
        {
            spent += Cost(skill, _skillLevels[skill]);
        }

        RemainingSkillCredits = TotalSkillCredits - spent;
    }

    /// <summary><c>ACCharGenData::GetSkillTrainedCost</c> for the current heritage.</summary>
    public int TrainedCost(uint skill) => _catalog.Data.GetSkillTrainedCost(skill, Heritage, _catalog.Skills);

    /// <summary><c>ACCharGenData::GetSkillSpecializedCost</c> (the total) for the current heritage.</summary>
    public int SpecializedCost(uint skill) => _catalog.Data.GetSkillSpecializedCost(skill, Heritage, _catalog.Skills);

    /// <summary>
    /// <c>CharGenState::GetSkillScore</c> (11.4186 0x005C4B50, 11.6096 0x005C5B30): the skill's formula over the current
    /// attributes, plus 5 trained or 10 specialised; 0 when the skill is below its minimum level or has no formula.
    /// </summary>
    public uint GetSkillScore(uint skill)
    {
        if (!_catalog.Skills.TryGetSkill(skill, out var entry))
        {
            return 0;
        }

        var level = GetSkillLevel(skill);
        if ((uint)level < entry.MinLevel || !entry.Formula.TryCalculate(
                (uint)GetAttribute(entry.Formula.Attribute1), (uint)GetAttribute(entry.Formula.Attribute2), out var score))
        {
            return 0;
        }

        return level switch
        {
            SkillAdvancementClass.Trained => score + 5,
            SkillAdvancementClass.Specialized => score + 10,
            _ => score,
        };
    }

    // ---- Templates -----------------------------------------------------------------------------------------------

    /// <summary><c>CharGenState::SetTemplate</c> (11.4186 0x005C5A60, 11.6096 0x005C6A40).</summary>
    public int SetTemplate(int template, bool apply)
    {
        Template = template;
        if (apply && template != -1)
        {
            ApplyTemplate();
        }

        return Template;
    }

    /// <summary>
    /// <c>CharGenState::ApplyTemplate</c> (11.4186 0x005C5080, 11.6096 0x005C6060): unlocks the attributes, sets them to the
    /// template's, resets the skills, trains its normal skills and specialises its primary ones as far as the credits go.
    /// The Olthoi heritages always use template 0.
    /// </summary>
    public void ApplyTemplate()
    {
        Array.Clear(_attributeLocked);
        if (Heritage is CharGenCatalog.OlthoiHeritage or CharGenCatalog.OlthoiAcidHeritage)
        {
            Template = 0;
        }

        if (Heritage == 0 || Gender == 0 || Template == -1)
        {
            return;
        }

        var hg = HeritageOrEmpty(Heritage);
        if (Template >= hg.Templates.Count)
        {
            return;
        }

        var template = hg.Templates[Template];
        SetStrength(template.Strength, balance: false);
        SetEndurance(template.Endurance, balance: false);
        SetCoordination(template.Coordination, balance: false);
        SetQuickness(template.Quickness, balance: false);
        SetFocus(template.Focus, balance: false);
        SetSelf(template.Self, balance: false);
        ResetSkillLevels();
        foreach (var skill in template.NormalSkills)
        {
            SetSkillLevel(skill, SkillAdvancementClass.Trained);
        }

        foreach (var skill in template.PrimarySkills)
        {
            if (skill >= _skillLevels.Length)
            {
                continue;
            }

            var remaining = RemainingSkillCredits + Cost(skill, _skillLevels[skill]) - SpecializedCost(skill);
            if (remaining >= 0)
            {
                RemainingSkillCredits = remaining;
                _skillLevels[skill] = SkillAdvancementClass.Specialized;
            }
        }
    }

    /// <summary>
    /// <c>CharGenState::ApplyDefaultTemplate</c> (11.4186 0x005C4990, 11.6096 0x005C5970): every attribute to 50 where the
    /// credits allow (retail's test is that <see cref="GetAbsRemainingCredits"/> is not 0, or the attribute is already
    /// 50 or more), then the skills reset.
    /// </summary>
    public void ApplyDefaultTemplate()
    {
        ReadOnlySpan<uint> order = [1, 2, 4, 3, 5, 6];
        foreach (var attribute in order)
        {
            if (GetAttribute(attribute) >= 50 || GetAbsRemainingCredits(attribute) != 0)
            {
                Assign(attribute, 50);
                RemainingAttributeCredits = TotalAttributeCredits - AttributeSum();
            }
        }

        ResetSkillLevels();
    }

    /// <summary>
    /// <c>CharGenState::FitTemplateToCharacter</c> (11.4186 0x005C6130, 11.6096 0x005C7110): picks the profession template
    /// (1 upwards) the character resembles most (attributes weigh (matched/total)^2.5, trained normal skills
    /// (n/total)^3, primary skills (points/2·total)^3.5, each a third; an empty list scores 0.3 or 0.45), or template 0
    /// when none scores 0.75. The summary page calls it.
    /// </summary>
    public void FitTemplateToCharacter()
    {
        if (Heritage == 0 || Gender == 0)
        {
            return;
        }

        var hg = HeritageOrEmpty(Heritage);
        var best = -1;
        var bestScore = 0.0;
        for (var i = 1; i < hg.Templates.Count; i++)
        {
            var t = hg.Templates[i];
            var total = t.Strength + t.Endurance + t.Coordination + t.Quickness + t.Focus + t.Self;
            var matched = Math.Min(Strength, t.Strength) + Math.Min(Endurance, t.Endurance) + Math.Min(Coordination, t.Coordination)
                + Math.Min(Quickness, t.Quickness) + Math.Min(Focus, t.Focus) + Math.Min(Self, t.Self);
            var score = total <= 0 ? 0.25 : Math.Pow(matched / (double)total, 2.5) * 0.3333333333333333;

            if (t.NormalSkills.Count > 0)
            {
                var trained = t.NormalSkills.Count(s => s < _skillLevels.Length && _skillLevels[s] >= SkillAdvancementClass.Trained);
                score += Math.Pow(trained / (double)t.NormalSkills.Count, 3.0) * 0.3333333333333333;
            }
            else
            {
                score += 0.3;
            }

            if (t.PrimarySkills.Count > 0)
            {
                var points = t.PrimarySkills.Where(s => s < _skillLevels.Length).Sum(s => _skillLevels[s] switch
                {
                    SkillAdvancementClass.Trained => 1,
                    SkillAdvancementClass.Specialized => 2,
                    _ => 0,
                });
                score += Math.Pow(points / (double)(2 * t.PrimarySkills.Count), 3.5) * 0.3333333333333333;
            }
            else
            {
                score += 0.45;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        Template = bestScore < 0.75 ? 0 : best;
    }

    // ---- Appearance ----------------------------------------------------------------------------------------------

    /// <summary><c>CharGenState::SetSkinShade</c> (11.4186 0x005C3850, 11.6096 0x005C4830).</summary>
    public double SetSkinShade(double shade) => SkinShade = shade;

    /// <summary><c>CharGenState::SetHairShade</c> (11.4186 0x005C3880, 11.6096 0x005C4860).</summary>
    public double SetHairShade(double shade) => HairShade = shade;

    /// <summary><c>CharGenState::SetHairColor</c> (11.4186 0x005C38B0, 11.6096 0x005C4890).</summary>
    public int SetHairColor(int color) => HairColor = color;

    /// <summary><c>CharGenState::SetEyeColor</c> (11.4186 0x005C38D0, 11.6096 0x005C48B0).</summary>
    public int SetEyeColor(int color) => EyeColor = color;

    /// <summary>
    /// <c>CharGenState::SetHeadgearStyle</c> (11.4186 0x005C5350, 11.6096 0x005C6330): the style (-1 = none) and its colours
    /// (<c>StoreColorInformation</c>), with the colour index kept inside them.
    /// </summary>
    public int SetHeadgearStyle(int style) => SetStyle(_headgear, style, sx => sx.Headgears);

    /// <summary><c>CharGenState::SetShirtStyle</c> (11.4186 0x005C5480, 11.6096 0x005C6460).</summary>
    public int SetShirtStyle(int style) => SetStyle(_shirt, style, sx => sx.Shirts);

    /// <summary><c>CharGenState::SetTrousersStyle</c> (11.4186 0x005C55A0, 11.6096 0x005C6580).</summary>
    public int SetTrousersStyle(int style) => SetStyle(_trousers, style, sx => sx.Pants);

    /// <summary><c>CharGenState::SetFootwearStyle</c> (11.4186 0x005C56C0, 11.6096 0x005C66A0).</summary>
    public int SetFootwearStyle(int style) => SetStyle(_footwear, style, sx => sx.Footwear);

    /// <summary><c>CharGenState::GetBaldState</c> (11.4186 0x005C52A0, 11.6096 0x005C6280): whether the hair style is bald.</summary>
    public bool GetBaldState()
    {
        if (Heritage == 0 || Gender == 0 || HairStyle == -1)
        {
            return false;
        }

        var styles = GenderOrEmpty().HairStyles;
        return HairStyle < styles.Count && styles[HairStyle].Bald;
    }

    /// <summary>
    /// <c>CharGenState::GetSetupID</c> (11.4186 0x005C5A90, 11.6096 0x005C6A70): the Setup CharGen's view shows: the hair
    /// style's alternate Setup, else the gender's; <see cref="SetupId"/> before a heritage and gender are chosen.
    /// </summary>
    public uint GetSetupId()
    {
        if (Heritage == 0 || Gender == 0)
        {
            return SetupId;
        }

        var sx = GenderOrEmpty();
        return HairStyle == -1 || HairStyle >= sx.HairStyles.Count || sx.HairStyles[HairStyle].AlternateSetup == 0
            ? sx.SetupId
            : sx.HairStyles[HairStyle].AlternateSetup;
    }

    /// <summary>
    /// The appearance choices as the 3D view reads them (<c>gmCG3DView::Update</c>, 11.4186 0x004EE9D0, 11.6096 0x004EF660, reads the same fields): each
    /// garment's colour index turned into its palette template (<c>CharGenState::Get*PaletteTemplateID</c>, 11.4186 0x005C38F0 to 0x005C3980; 0 outside
    /// the list); the shades go as they are (a shade outside 0-1, <see cref="UnsetShade"/> included, gives the view's palette set no palette, and the view says so).
    /// </summary>
    public CharGenLook ToLook() => new(
        (int)Heritage,
        (int)Gender,
        HairStyle,
        HairColor,
        HairShade,
        EyesStrip,
        EyeColor,
        NoseStrip,
        MouthStrip,
        SkinShade,
        new GearChoice(_headgear.Style, _headgear.Template, _headgear.Shade),
        new GearChoice(_shirt.Style, _shirt.Template, _shirt.Shade),
        new GearChoice(_trousers.Style, _trousers.Template, _trousers.Shade),
        new GearChoice(_footwear.Style, _footwear.Template, _footwear.Shade));

    // ---- Randomisation -------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>CharGenState::RandomizeAppearance</c> (11.4186 0x005C4F10, 11.6096 0x005C5EF0): new strips, shades, colours and
    /// hair style, each different from the current one where the list allows; <paramref name="restrictHair"/> picks among
    /// the first seven styles.
    /// </summary>
    public void RandomizeAppearance(bool restrictHair)
    {
        if (Heritage == 0 || Gender == 0)
        {
            return;
        }

        var sx = GenderOrEmpty();
        if (sx.EyeStrips.Count > 0)
        {
            EyesStrip = CharGenDice.RandInt(_random, sx.EyeStrips.Count, EyesStrip);
        }

        if (sx.NoseStrips.Count > 0)
        {
            NoseStrip = CharGenDice.RandInt(_random, sx.NoseStrips.Count, NoseStrip);
        }

        if (sx.MouthStrips.Count > 0)
        {
            MouthStrip = CharGenDice.RandInt(_random, sx.MouthStrips.Count, MouthStrip);
        }

        SkinShade = CharGenDice.RandomReal(_random);
        HairShade = CharGenDice.RandomReal(_random);
        if (sx.HairColors.Count > 0)
        {
            HairColor = CharGenDice.RandInt(_random, sx.HairColors.Count, HairColor);
        }

        if (sx.EyeColors.Count > 0)
        {
            EyeColor = CharGenDice.RandInt(_random, sx.EyeColors.Count, EyeColor);
        }

        if (sx.HairStyles.Count > 0)
        {
            HairStyle = CharGenDice.RandInt(_random, restrictHair ? 7 : sx.HairStyles.Count, HairStyle);
        }
    }

    /// <summary>
    /// <c>CharGenState::RandomizeHeadgear</c> (11.4186 0x005C5E10, 11.6096 0x005C6DF0): a style or none (-1), optionally
    /// not the current one, a colour and a shade.
    /// </summary>
    public void RandomizeHeadgear(bool excludeCurrent)
    {
        if (Heritage == 0 || Gender == 0)
        {
            return;
        }

        var count = GenderOrEmpty().Headgears.Count;
        if (count > 0)
        {
            var pick = excludeCurrent
                ? CharGenDice.RandInt(_random, count + 1, _headgear.Style + 1)
                : CharGenDice.RandInt(_random, count + 1);
            SetHeadgearStyle(pick - 1);
        }

        RandomizeColorAndShade(_headgear);
    }

    /// <summary><c>CharGenState::RandomizeShirt</c> (11.4186 0x005C5EF0, 11.6096 0x005C6ED0).</summary>
    public void RandomizeShirt() => RandomizeGarment(_shirt, sx => sx.Shirts, SetShirtStyle);

    /// <summary><c>CharGenState::RandomizeTrousers</c> (11.4186 0x005C5FB0, 11.6096 0x005C6F90).</summary>
    public void RandomizeTrousers() => RandomizeGarment(_trousers, sx => sx.Pants, SetTrousersStyle);

    /// <summary><c>CharGenState::RandomizeFootwear</c> (11.4186 0x005C6070, 11.6096 0x005C7050).</summary>
    public void RandomizeFootwear() => RandomizeGarment(_footwear, sx => sx.Footwear, SetFootwearStyle);

    /// <summary><c>CharGenState::RandomizeClothing</c> (11.4186 0x005C6770, 11.6096 0x005C7750).</summary>
    public void RandomizeClothing(bool excludeCurrent)
    {
        RandomizeHeadgear(excludeCurrent);
        RandomizeShirt();
        RandomizeTrousers();
        RandomizeFootwear();
    }

    /// <summary>
    /// <c>CharGenState::RandomizeTemplate</c> (11.4186 0x005C6500, 11.6096 0x005C74E0): a profession template other than
    /// the custom one and the current one, applied.
    /// </summary>
    public void RandomizeTemplate()
    {
        if (Heritage == 0 || Gender == 0)
        {
            return;
        }

        if (Heritage is CharGenCatalog.OlthoiHeritage or CharGenCatalog.OlthoiAcidHeritage)
        {
            Template = 1;
            ApplyTemplate();
            return;
        }

        var count = HeritageOrEmpty(Heritage).Templates.Count;
        if (count > 1)
        {
            Template = CharGenDice.RandInt(_random, count - 1, Template - 1) + 1;
            ApplyTemplate();
        }
    }

    /// <summary>
    /// <c>CharGenState::RandomizeStartArea</c> (11.4186 0x005C59E0, 11.6096 0x005C69C0): one of the heritage's primary start
    /// areas (-1 when it names one that does not exist).
    /// </summary>
    public void RandomizeStartArea()
    {
        if (Heritage == 0)
        {
            return;
        }

        var primary = HeritageOrEmpty(Heritage).PrimaryStartAreas;
        if (primary.Count == 0)
        {
            return;
        }

        var area = primary[CharGenDice.RandInt(_random, primary.Count, -1)];
        StartArea = area >= 0 && area < _catalog.Data.StartAreas.Count ? area : -1;
    }

    /// <summary>
    /// <c>CharGenState::RandomizeSkills</c> (11.4186 0x005C57E0, 11.6096 0x005C67C0): resets the skills, then up to 100
    /// random picks (training an untrained skill on odd picks, specialising a trained one), then spends what is left in id
    /// order.
    /// </summary>
    public void RandomizeSkills()
    {
        if (Heritage == 0 || Gender == 0)
        {
            return;
        }

        ResetSkillLevels();
        for (var pick = 0; pick < 100; pick++)
        {
            var skill = (uint)CharGenDice.RandInt(_random, _skillLevels.Length);
            if (skill < _skillLevels.Length)
            {
                if (_skillLevels[skill] == SkillAdvancementClass.Untrained)
                {
                    if ((pick & 1) != 0)
                    {
                        SetSkillLevel(skill, SkillAdvancementClass.Trained);
                    }
                }
                else if (_skillLevels[skill] == SkillAdvancementClass.Trained)
                {
                    SetSkillLevel(skill, SkillAdvancementClass.Specialized);
                }
            }

            if (RemainingSkillCredits == 0)
            {
                break;
            }
        }

        for (uint skill = 0; skill < _skillLevels.Length && RemainingSkillCredits > 0; skill++)
        {
            if (_skillLevels[skill] == SkillAdvancementClass.Untrained)
            {
                var remaining = RemainingSkillCredits - TrainedCost(skill);
                if (remaining >= 0)
                {
                    RemainingSkillCredits = remaining;
                    _skillLevels[skill] = SkillAdvancementClass.Trained;
                }
            }
            else if (_skillLevels[skill] == SkillAdvancementClass.Trained)
            {
                var remaining = RemainingSkillCredits + TrainedCost(skill) - SpecializedCost(skill);
                if (remaining >= 0)
                {
                    RemainingSkillCredits = remaining;
                    _skillLevels[skill] = SkillAdvancementClass.Specialized;
                }
            }
        }
    }

    /// <summary>
    /// <c>CharGenState::RandomizeHeritageGroup</c> (11.4186 0x005C6A20, 11.6096 0x005C7A00): heritage 1-4 with Throne of
    /// Destiny, 1-3 without (<c>Random::RollDice</c>); the later heritages are chosen only on the heritage page.
    /// </summary>
    public void RandomizeHeritageGroup(bool hasThroneOfDestiny) =>
        SetHeritageGroup((uint)CharGenDice.RollDice(_random, 1, hasThroneOfDestiny ? 4 : 3));

    /// <summary>
    /// <c>CharGenState::RandomizeCharacter</c> (11.4186 0x005C6D80, 11.6096 0x005C7D60): what opening CharGen does: reset,
    /// then a random heritage, gender, appearance, clothing, template and start area, with the frozen flags set.
    /// </summary>
    public void RandomizeCharacter(bool hasThroneOfDestiny)
    {
        Reset();
        SetHeritageGroup((uint)CharGenDice.RollDice(_random, 1, hasThroneOfDestiny ? 4 : 3));
        SetGender((uint)CharGenDice.RollDice(_random, 1, 2));
        RandomizeAppearance(restrictHair: false);
        RandomizeHeadgear(excludeCurrent: false);
        RandomizeShirt();
        RandomizeTrousers();
        RandomizeFootwear();
        RandomizeTemplate();
        RandomizeStartArea();
        HeritageGroupFrozen = true;
        SexFrozen = true;
        AppearanceFrozen = true;
    }

    // ---- Start area, name, result --------------------------------------------------------------------------------

    /// <summary><c>CharGenState::SetStartArea</c> (11.4186 0x005C4000, 11.6096 0x005C4FE0): an index outside the list is -1.</summary>
    public void SetStartArea(int startArea) =>
        StartArea = startArea >= 0 && startArea < _catalog.Data.StartAreas.Count ? startArea : -1;

    /// <summary>
    /// <c>CharGenState::SetName</c> (11.4186 0x005C3D10, 11.6096 0x005C4CF0): a name of at most 32 characters, formatted
    /// (<see cref="CharGenNames.Format"/>); a longer one is ignored.
    /// </summary>
    public void SetName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length <= CharGenNames.MaxLength)
        {
            Name = CharGenNames.Format(name);
        }
    }

    /// <summary>
    /// <c>CharGenState::GetCharGenResult</c> (11.4186 0x005C4030, 11.6096 0x005C5010): the character as a result record,
    /// clothing colours turned into their palette templates (0 for an index outside the list) and one advancement class
    /// per skill slot.
    /// </summary>
    public CharGenResult GetCharGenResult() => new(
        Heritage,
        Gender,
        EyesStrip,
        NoseStrip,
        MouthStrip,
        HairColor,
        EyeColor,
        HairStyle,
        _headgear.Style,
        _headgear.Template,
        _shirt.Style,
        _shirt.Template,
        _trousers.Style,
        _trousers.Template,
        _footwear.Style,
        _footwear.Template,
        SkinShade,
        HairShade,
        _headgear.Shade,
        _shirt.Shade,
        _trousers.Shade,
        _footwear.Shade,
        Template,
        Strength,
        Endurance,
        Coordination,
        Quickness,
        Focus,
        Self,
        Slot,
        _catalog.ClassFor(Heritage),
        Array.AsReadOnly((SkillAdvancementClass[])_skillLevels.Clone()),
        Name,
        StartArea);

    /// <summary>The session's create intent for the character (m2-plan §4.11: <c>CharGenState.GetResult</c>).</summary>
    /// <remarks>
    /// Send it only with a <see cref="Template"/> of 0 or more. Retail's verifier (<see cref="CharGenVerifier"/>)
    /// allows -1, and <see cref="SetTemplate"/> can set it, but ACE indexes the heritage's templates with the template
    /// option (ACEmulator/ACE@95ef426d PlayerFactory.cs:135, for every heritage but the Olthoi ones) and throws for -1.
    /// The normal flow, <see cref="RandomizeCharacter"/> first, always leaves a template of 0 or more: every heritage
    /// but the Olthoi has 7 templates, so changing heritage keeps the index, and the Olthoi always use template 0.
    /// </remarks>
    public CharacterCreateIntent GetResult() => GetCharGenResult().ToIntent();

    // ---- Internals -----------------------------------------------------------------------------------------------

    private CharGenHeritage HeritageOrEmpty(uint heritage) => _catalog.Data.TryGetHeritage(heritage, out var hg) ? hg : EmptyHeritage;

    private CharGenGender GenderOrEmpty() =>
        HeritageOrEmpty(Heritage).TryGetGender(Gender, out var sx) ? sx : EmptyGender;

    private static readonly CharGenGender EmptyGender = new(
        0, string.Empty, 0, 0, 0, 0, 0, 0, 0, 0, 0, CharGenObjDesc.Empty, [], [], [], [], [], [], [], [], [], [], []);

    private int AttributeSum() => Strength + Endurance + Coordination + Quickness + Focus + Self;

    private void Assign(uint attribute, int value)
    {
        switch (attribute)
        {
            case 1:
                Strength = value;
                break;
            case 2:
                Endurance = value;
                break;
            case 3:
                Quickness = value;
                break;
            case 4:
                Coordination = value;
                break;
            case 5:
                Focus = value;
                break;
            case 6:
                Self = value;
                break;
        }
    }

    private int Cost(uint skill, SkillAdvancementClass level) => level switch
    {
        SkillAdvancementClass.Trained => TrainedCost(skill),
        SkillAdvancementClass.Specialized => SpecializedCost(skill),
        _ => 0,
    };

    private static int Clamp(int index, int count) => index >= count ? count - 1 : index;

    private void ClearAppearance()
    {
        EyesStrip = -1;
        NoseStrip = -1;
        MouthStrip = -1;
        SkinShade = UnsetShade;
        HairShade = UnsetShade;
        HairColor = -1;
        EyeColor = -1;
        HairStyle = -1;
        foreach (var slot in (ReadOnlySpan<ClothingSlot>)[_headgear, _shirt, _trousers, _footwear])
        {
            slot.Style = -1;
            slot.Colors = [];
            slot.Color = -1;
            slot.Shade = UnsetShade;
        }
    }

    /// <summary>The tail of <c>SetHeritageGroup</c> and <c>SetGender</c>: each style set again, so its colours follow the gender.</summary>
    private void RefreshClothing()
    {
        SetHeadgearStyle(_headgear.Style);
        SetShirtStyle(_shirt.Style);
        SetTrousersStyle(_trousers.Style);
        SetFootwearStyle(_footwear.Style);
    }

    private int SetStyle(ClothingSlot slot, int style, Func<CharGenGender, IReadOnlyList<CharGenGear>> list)
    {
        slot.Colors = [];
        if (Heritage != 0 && Gender != 0 && style != -1)
        {
            var sx = GenderOrEmpty();
            var gear = list(sx);
            if (style >= 0 && style < gear.Count)
            {
                // CharGenState::StoreColorInformation (11.4186 0x005C44D0, 11.6096 0x005C54B0).
                slot.Colors = _catalog.Data.GetClothingColors(gear[style].ClothingTableId, sx);
            }

            if (slot.Color < 0)
            {
                slot.Color = 0;
            }

            if (slot.Color >= slot.Colors.Count)
            {
                slot.Color = slot.Colors.Count - 1;
            }
        }

        slot.Style = style;
        return style;
    }

    private void RandomizeGarment(ClothingSlot slot, Func<CharGenGender, IReadOnlyList<CharGenGear>> list, Func<int, int> setStyle)
    {
        if (Heritage == 0 || Gender == 0)
        {
            return;
        }

        var count = list(GenderOrEmpty()).Count;
        if (count > 0)
        {
            setStyle(CharGenDice.RandInt(_random, count, slot.Style));
        }

        RandomizeColorAndShade(slot);
    }

    private void RandomizeColorAndShade(ClothingSlot slot)
    {
        if (slot.Colors.Count > 0)
        {
            slot.Color = CharGenDice.RandInt(_random, slot.Colors.Count, slot.Color);
        }

        slot.Shade = CharGenDice.RandomReal(_random);
    }

    /// <summary>One clothing slot: its style, the colours that style offers, the chosen colour and the shade.</summary>
    private sealed class ClothingSlot
    {
        public int Style { get; set; } = -1;

        public IReadOnlyList<CharGenClothingColor> Colors { get; set; } = [];

        public int Color { get; set; } = -1;

        public double Shade { get; set; } = UnsetShade;

        /// <summary>The palette template of the chosen colour, or 0 when the index is outside the list.</summary>
        public uint Template => Color >= 0 && Color < Colors.Count ? Colors[Color].PaletteTemplate : 0;
    }
}
