using Lifestone.Client.RetailUi.CharacterCreation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Dat;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.RetailUi.CharacterCreation;

/// <summary>
/// The CharGen state and verifier (m2-plan.md R18a): every heritage and gender verifies with 55 advancement classes and
/// valid credits, ACE's create rules accept the result, templates, skill limits and seeded randomisation.
/// </summary>
public sealed class CharGenStateTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const string TestName = "Lifestone Tester";

    private CharGenCatalog? _catalog;

    private CharGenCatalog Catalog => _catalog ??= CharGenCatalog.Load(fixture.Dats);

    public static TheoryData<uint, uint> HeritagesAndGenders()
    {
        var data = new TheoryData<uint, uint>();
        for (uint heritage = 1; heritage <= 13; heritage++)
        {
            data.Add(heritage, 1);
            data.Add(heritage, 2);
        }

        return data;
    }

    [RequiresDatsFact]
    public void The_catalog_names_the_WCLASSID_classes()
    {
        Assert.Equal((0x00000001u, 0x0000A9D8u, 0x0000A9D9u), (Catalog.HumanClassId, Catalog.OlthoiClassId, Catalog.OlthoiAcidClassId));
        Assert.Equal("HUMAN", fixture.Dats.EnumMaps.NameOf(CharGenCatalog.WeenieClassGroup, CharGenCatalog.HumanEnum));
        Assert.Equal(Catalog.OlthoiClassId, Catalog.ClassFor(12));
        Assert.Equal(Catalog.HumanClassId, Catalog.ClassFor(6));
    }

    [RequiresDatsTheory]
    [MemberData(nameof(HeritagesAndGenders))]
    public void Every_heritage_and_gender_verifies_with_55_classes_and_valid_credits(uint heritage, uint gender)
    {
        foreach (var seed in new[] { 1, 7, 42, 1234 })
        {
            // What the player sees: CharGen opens on a random character, then the heritage page picks the heritage.
            var state = new CharGenState(Catalog, new CharGenRandom(seed));
            state.RandomizeCharacter(hasThroneOfDestiny: true);
            state.SetHeritageGroup(heritage);
            state.SetGender(gender);
            state.SetName(TestName);

            var result = state.GetCharGenResult();
            Assert.True(CharGenVerifier.Verify(result, Catalog, out var problem), $"heritage {heritage} gender {gender} seed {seed}: {problem}");
            Assert.Equal(55, result.SkillAdvancementClasses.Count);
            int[] attributes = [result.Strength, result.Endurance, result.Coordination, result.Quickness, result.Focus, result.Self];
            Assert.All(attributes, a => Assert.InRange(a, 10, 100));
            Assert.True(attributes.Sum() <= Catalog.Data.GetHeritage(heritage).AttributeCredits);
            Assert.True(state.RemainingSkillCredits >= 0, $"skill credits {state.RemainingSkillCredits}");
            Assert.Equal(TestName, result.Name);

            var intent = state.GetResult();
            Assert.True(intent.TryValidate(out var invalid), invalid);
            Assert.Null(AceCreateRules.Check(intent, Catalog.Data, Catalog.Skills));

            // The summary page fits the profession to the character (folded in from the CharGenSummary create-intent fact, P1): the intent the create sends still has the required skills and verifies.
            state.FitTemplateToCharacter();
            var fitted = state.GetCharGenResult();
            var fittedIntent = fitted.ToIntent();
            Assert.True(CharGenVerifier.Verify(fitted, Catalog, out problem), $"fitted: heritage {heritage} gender {gender} seed {seed}: {problem}");
            Assert.Equal(CharacterCreateIntent.RequiredSkillCount, fittedIntent.SkillAdvancementClasses.Count);
            Assert.True(fittedIntent.TryValidate(out invalid), invalid);
            Assert.Equal(TestName, fittedIntent.Name);
            Assert.True(state.Template >= 0, "the summary page fits the profession, so a character always has a template of 0 or more");
        }
    }

    [RequiresDatsFact]
    public void Skill_purchases_stop_at_the_credits_and_refund_on_the_way_down()
    {
        var state = Human();

        // Aluvian: 52 credits; the free skills (Arcane Lore through the heritage, Magic Defense, Jump, Run, Loyalty, Salvaging) start trained.
        state.SetTemplate(0, apply: true);
        Assert.Equal(52, state.RemainingSkillCredits);
        Assert.Equal(SkillAdvancementClass.Trained, state.GetSkillLevel(14));
        Assert.Equal(SkillAdvancementClass.Trained, state.GetSkillLevel(24));
        Assert.Equal(SkillAdvancementClass.Untrained, state.GetSkillLevel(6));
        Assert.Equal(SkillAdvancementClass.Inactive, state.GetSkillLevel(1)); // not in the SkillTable

        Assert.True(state.SetSkillLevel(34, SkillAdvancementClass.Specialized)); // War Magic: 28
        Assert.Equal(24, state.RemainingSkillCredits);
        Assert.True(state.SetSkillLevel(6, SkillAdvancementClass.Specialized)); // Melee Defense: 20
        Assert.Equal(4, state.RemainingSkillCredits);
        Assert.False(state.SetSkillLevel(33, SkillAdvancementClass.Trained)); // Life Magic: 12 > 4
        Assert.Equal(SkillAdvancementClass.Untrained, state.GetSkillLevel(33));
        Assert.True(state.SetSkillLevel(34, SkillAdvancementClass.Trained)); // back to trained: refunds 28, costs 16
        Assert.Equal(16, state.RemainingSkillCredits);
        Assert.False(state.SetSkillLevel(18, SkillAdvancementClass.Specialized)); // Item Tinkering cannot be specialised at creation
        Assert.False(state.SetSkillLevel(6, SkillAdvancementClass.Inactive)); // only 1-3
        Assert.False(state.SetSkillLevel(55, SkillAdvancementClass.Trained));

        state.UpdateRemainingSkillCredits();
        Assert.Equal(16, state.RemainingSkillCredits);
    }

    [RequiresDatsFact]
    public void Templates_apply_their_attributes_and_skills_and_FitTemplate_finds_them_again()
    {
        var state = Human();
        var bowHunter = Catalog.Data.GetHeritage(1).Templates[1];
        Assert.Equal("Bow Hunter", bowHunter.Name);

        state.SetTemplate(1, apply: true);
        Assert.Equal((40, 30, 100, 100, 50, 10), (state.Strength, state.Endurance, state.Coordination, state.Quickness, state.Focus, state.Self));
        Assert.Equal(0, state.RemainingAttributeCredits);
        Assert.All(bowHunter.NormalSkills, s => Assert.Equal(SkillAdvancementClass.Trained, state.GetSkillLevel(s)));
        Assert.All(bowHunter.PrimarySkills, s => Assert.Equal(SkillAdvancementClass.Specialized, state.GetSkillLevel(s)));
        Assert.Equal(0, state.RemainingSkillCredits); // 8 + 2 trained, 12 + 8 + 2 + 20 specialised: all 52

        state.SetTemplate(0, apply: false);
        state.FitTemplateToCharacter();
        Assert.Equal(1, state.Template);

        // A character that resembles no template is the custom one.
        state.SetTemplate(0, apply: true);
        state.FitTemplateToCharacter();
        Assert.Equal(0, state.Template);
    }

    [RequiresDatsFact]
    public void Attributes_balance_round_robin_and_respect_locks()
    {
        var state = Human();
        state.SetTemplate(0, apply: true); // all 10: 270 credits left
        Assert.Equal(270, state.RemainingAttributeCredits);
        foreach (var attribute in new uint[] { 1, 2, 3, 4, 5, 6 })
        {
            state.SetAttribute(attribute, 55, balance: false); // 330 spent
        }

        Assert.Equal(0, state.RemainingAttributeCredits);

        // Endurance locked: raising Strength by 10 takes a point from Coordination, Quickness, Focus and Self in turn.
        state.LockAttribute(2, true);
        Assert.Equal(65, state.SetStrength(65, balance: true));
        Assert.Equal((65, 55, 52, 52, 53, 53), (state.Strength, state.Endurance, state.Coordination, state.Quickness, state.Focus, state.Self));
        Assert.Equal(0, state.RemainingAttributeCredits);

        // The next balance starts where the last one stopped (Focus), skipping the attribute being raised.
        Assert.Equal(56, state.SetSelf(56, balance: true));
        Assert.Equal((64, 55, 51, 52, 52, 56), (state.Strength, state.Endurance, state.Coordination, state.Quickness, state.Focus, state.Self));

        // With every other attribute locked there is no absolute room: a raise is refused, a cut is not.
        foreach (var attribute in new uint[] { 3, 4, 5, 6 })
        {
            state.LockAttribute(attribute, true);
        }

        Assert.Equal(0, state.GetAbsRemainingCredits(1));
        Assert.Equal(64, state.SetStrength(70, balance: true));
        Assert.Equal(60, state.SetStrength(60, balance: true));
        Assert.Equal(4, state.RemainingAttributeCredits);
        state.ResetAttributeLock();
        Assert.False(state.IsAttributeLocked(2));
    }

    [RequiresDatsFact]
    public void Randomisation_is_deterministic_with_a_seed()
    {
        CharGenResult Make(int seed)
        {
            var state = new CharGenState(Catalog, new CharGenRandom(seed));
            state.RandomizeCharacter(hasThroneOfDestiny: true);
            state.RandomizeSkills();
            state.SetName(TestName);
            return state.GetCharGenResult();
        }

        var a = Make(99);
        var b = Make(99);
        Assert.Equal(a with { SkillAdvancementClasses = [] }, b with { SkillAdvancementClasses = [] });
        Assert.Equal(a.SkillAdvancementClasses, b.SkillAdvancementClasses);
        Assert.InRange(a.Heritage, 1u, 4u); // RandomizeHeritageGroup rolls 1-4 with Throne of Destiny
        Assert.True(CharGenVerifier.Verify(a, Catalog, out var problem), problem);

        var differ = Enumerable.Range(0, 8).Select(Make).Select(r => (r.Heritage, r.Gender, r.EyesStrip, r.HairStyle)).Distinct().Count();
        Assert.True(differ > 1);
    }

    [RequiresDatsFact]
    public void Clothing_colours_are_palette_templates_and_reset_clears_everything()
    {
        var state = Human();
        state.SetShirtStyle(0);
        Assert.NotEmpty(state.ShirtColors);
        Assert.InRange(state.ShirtColor, 0, state.ShirtColors.Count - 1);
        Assert.Equal(state.ShirtColors[state.ShirtColor].PaletteTemplate, state.GetCharGenResult().ShirtColor);
        state.SetHeadgearStyle(-1);
        Assert.Empty(state.HeadgearColors);
        Assert.Equal((-1, 0u), (state.GetCharGenResult().HeadgearStyle, state.GetCharGenResult().HeadgearColor));

        state.Reset();
        Assert.Equal((0u, 0u, -1, -1, CharGenState.UnsetShade), (state.Heritage, state.Gender, state.ShirtStyle, state.Template, state.SkinShade));
        Assert.Equal((50, -300), (state.Strength, state.RemainingAttributeCredits)); // ApplyDefaultTemplate with no heritage
        Assert.Equal(CharGenState.DefaultSetupId, state.SetupId);
        Assert.False(CharGenVerifier.Verify(state.GetCharGenResult(), Catalog, out var problem));
        Assert.Equal("heritage 0 does not exist", problem);
    }

    [RequiresDatsFact]
    public void The_verifier_names_what_fails()
    {
        var state = Human();
        state.SetName(TestName);
        var good = state.GetCharGenResult();
        Assert.True(CharGenVerifier.Verify(good, Catalog, out _));

        string Problem(CharGenResult r) => CharGenVerifier.Verify(r, Catalog, out var p) ? "passes" : p;

        Assert.Equal("the Viamontian heritage needs Throne of Destiny", CharGenVerifier.Verify(good with { Heritage = 4 }, Catalog, out var tod, hasThroneOfDestiny: false) ? null : tod);
        Assert.Equal("gender 3 is not 1 or 2", Problem(good with { Gender = 3 }));
        Assert.StartsWith("eye strip ", Problem(good with { EyesStrip = 99 }), StringComparison.Ordinal);
        Assert.StartsWith("shirt colour", Problem(good with { ShirtColor = 0xDEAD }), StringComparison.Ordinal);
        Assert.StartsWith("a shade", Problem(good with { HairShade = 1.5 }), StringComparison.Ordinal);
        Assert.StartsWith("an attribute (9)", Problem(good with { Strength = 9 }), StringComparison.Ordinal);
        Assert.StartsWith("the attributes spend", Problem(good with { Strength = 100, Endurance = 100, Coordination = 100, Quickness = 100 }), StringComparison.Ordinal);
        Assert.StartsWith("37 skills have an advancement class", Problem(good with { SkillAdvancementClasses = [.. good.SkillAdvancementClasses.Select((s, i) => i == 6 ? SkillAdvancementClass.Inactive : s)] }), StringComparison.Ordinal);
        Assert.StartsWith("the skills spend", Problem(good with { SkillAdvancementClasses = [.. good.SkillAdvancementClasses.Select(s => s == SkillAdvancementClass.Untrained ? SkillAdvancementClass.Specialized : s)] }), StringComparison.Ordinal);
        Assert.StartsWith("the name", Problem(good with { Name = "lifestone" }), StringComparison.Ordinal);
        Assert.StartsWith("template 7", Problem(good with { TemplateNum = 7 }), StringComparison.Ordinal);
        Assert.StartsWith("start area -1", Problem(good with { StartArea = -1 }), StringComparison.Ordinal);
        Assert.Equal("Sanamar needs Throne of Destiny", CharGenVerifier.Verify(good with { StartArea = 3 }, Catalog, out var sanamar, hasThroneOfDestiny: false) ? null : sanamar);
    }

    [RequiresDatsFact]
    public void The_intent_carries_the_result_field_for_field()
    {
        // Distinct values everywhere, so a swap (retail's attribute ids put Quickness 3 before Coordination 4; the wire
        // puts Coordination first) cannot pass. Through the state first: each named setter reaches its own field.
        var state = Human();
        state.SetName(TestName);
        state.SetStrength(11, balance: false);
        state.SetEndurance(12, balance: false);
        state.SetCoordination(13, balance: false);
        state.SetQuickness(14, balance: false);
        state.SetFocus(15, balance: false);
        state.SetSelf(16, balance: false);
        (state.EyesStrip, state.NoseStrip, state.MouthStrip, state.HairStyle) = (1, 2, 3, 4);
        state.SetHairColor(5);
        state.SetEyeColor(6);
        state.SetSkinShade(0.125);
        state.SetHairShade(0.25);
        state.SetHeadgearStyle(-1);
        (state.ShirtShade, state.TrousersShade, state.FootwearShade) = (0.375, 0.5, 0.625);
        var result = state.GetCharGenResult();
        Assert.Equal((11, 12, 13, 14, 15, 16), (result.Strength, result.Endurance, result.Coordination, result.Quickness, result.Focus, result.Self));
        Assert.Equal((1, 2, 3, 4, 5, 6), (result.EyesStrip, result.NoseStrip, result.MouthStrip, result.HairStyle, result.HairColor, result.EyeColor));
        Assert.Equal((0.125, 0.25, 0.375, 0.5, 0.625), (result.SkinShade, result.HairShade, result.ShirtShade, result.TrousersShade, result.FootwearShade));

        var intent = state.GetResult();
        Assert.Equal((result.Heritage, result.Gender, result.TemplateNum, result.ClassId, result.Name), (intent.Heritage, intent.Gender, intent.TemplateOption, intent.ClassId, intent.Name));
        Assert.Equal((11u, 12u, 13u, 14u, 15u, 16u), (intent.Strength, intent.Endurance, intent.Coordination, intent.Quickness, intent.Focus, intent.Self));
        Assert.Equal(0xFFFFFFFFu, intent.Appearance.HeadgearStyle); // -1 travels as its bit pattern, as CG_Pack writes it
        Assert.Equal((uint)result.StartArea, intent.StartArea);
        Assert.Equal(result.SkillAdvancementClasses.Select(s => (uint)s), intent.SkillAdvancementClasses);

        // Then every field of a result whose values all differ, against the intent's.
        var distinct = new CharGenResult(
            Heritage: 1, Gender: 2, EyesStrip: 3, NoseStrip: 4, MouthStrip: 5, HairColor: 6, EyeColor: 7, HairStyle: 8,
            HeadgearStyle: 9, HeadgearColor: 10, ShirtStyle: 11, ShirtColor: 12, TrousersStyle: 13, TrousersColor: 14,
            FootwearStyle: 15, FootwearColor: 16, SkinShade: 0.1, HairShade: 0.2, HeadgearShade: 0.3, ShirtShade: 0.4,
            TrousersShade: 0.5, FootwearShade: 0.6, TemplateNum: 17, Strength: 18, Endurance: 19, Coordination: 20,
            Quickness: 21, Focus: 22, Self: 23, Slot: 24, ClassId: 25, SkillAdvancementClasses: result.SkillAdvancementClasses,
            Name: TestName, StartArea: 26).ToIntent();
        Assert.Equal(new CharacterAppearance(3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6), distinct.Appearance);
        Assert.Equal((1u, 2u, 17, 18u, 19u, 20u, 21u, 22u, 23u, 25u, TestName, 26u), (
            distinct.Heritage, distinct.Gender, distinct.TemplateOption, distinct.Strength, distinct.Endurance, distinct.Coordination,
            distinct.Quickness, distinct.Focus, distinct.Self, distinct.ClassId, distinct.Name, distinct.StartArea));
    }

    [RequiresDatsFact]
    public void A_template_of_minus_one_verifies_as_in_retail_but_ACE_cannot_create_it()
    {
        // Retail's verifier allows -1; ACE indexes the heritage's templates with it (PlayerFactory.cs:135) and throws, so
        // G3 must send a template >= 0. The normal flow (RandomizeCharacter first) always has one.
        var state = Human();
        state.SetName(TestName);
        Assert.InRange(state.Template, 0, Catalog.Data.GetHeritage(1).Templates.Count - 1);
        Assert.Null(AceCreateRules.Check(state.GetResult(), Catalog.Data, Catalog.Skills));

        state.SetTemplate(-1, apply: false);
        Assert.True(CharGenVerifier.Verify(state.GetCharGenResult(), Catalog, out var problem), problem);
        Assert.StartsWith("TemplateOutOfRange", AceCreateRules.Check(state.GetResult(), Catalog.Data, Catalog.Skills), StringComparison.Ordinal);

        var past = state.GetCharGenResult() with { TemplateNum = Catalog.Data.GetHeritage(1).Templates.Count };
        Assert.False(CharGenVerifier.Verify(past, Catalog, out _));
        Assert.StartsWith("TemplateOutOfRange", AceCreateRules.Check(past.ToIntent(), Catalog.Data, Catalog.Skills), StringComparison.Ordinal);
        Assert.StartsWith("StartAreaOutOfRange", AceCreateRules.Check((past with { TemplateNum = 0, StartArea = -1 }).ToIntent(), Catalog.Data, Catalog.Skills), StringComparison.Ordinal);
    }

    /// <summary>An Aluvian male picked on the heritage page after CharGen opened on seed 3.</summary>
    private CharGenState Human()
    {
        var state = new CharGenState(Catalog, new CharGenRandom(3));
        state.RandomizeCharacter(hasThroneOfDestiny: true);
        state.SetHeritageGroup(1);
        state.SetGender(1);
        return state;
    }
}
