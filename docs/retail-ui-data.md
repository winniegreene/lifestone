# Retail UI data layer (M2, retail UI track, UI-a)

The engine-agnostic data layer of the retail user interface: the layouts of `client_local_English.dat` turned into an
immutable desc model, template inheritance, anchoring, the master property table, fonts, strings, cursors, the key map,
Lifestone's own layout files, the CharGen data and the character-creation state. The UI data lives in
`src/Lifestone.Data/Ui/` (namespace `Lifestone.Data.Ui`), the CharGen data in `src/Lifestone.Data/CharGen/` (namespace
`Lifestone.Data.CharacterCreation`, §8), and the character-creation state in `src/Lifestone.Client/RetailUi/CharGen/`
(namespace `Lifestone.Client.RetailUi.CharacterCreation`, §10). None of it has an engine or I/O beyond `DatSet`; the
tests are in `tests/Lifestone.Data.Tests/{Ui,CharGen}/` and `tests/Lifestone.Client.Tests/RetailUi/CharGen/`. The element
runtime, the compositor and the widgets (UI-b) and the screens (wave 2) build on it.

The plan of record is [m2-plan.md](milestones/m2-plan.md) (steps R1-R9 and R18a); the specification is
[m2-log-in.md](milestones/m2-log-in.md) §5. Nothing here is ported: every algorithm is re-expressed from the retail
decompile and cited by function name with its 11.4186 and 11.6096 addresses (§11). OpenAC's `UiLayoutPolicy` and
`RetailStringHash` were read as cross-checks only, and ACE's character-create checks are restated as test rules (§10),
not copied.

## 1. The desc model (R1)

| Type | Retail | What it holds |
|---|---|---|
| `UiLayout` | `LayoutDesc` | DID, design size (800×600; 800×500 for three), top-level elements by id |
| `UiElementDesc` | `ElementDesc` | id, class (`Type`), base element and layout, default state, read order, `UiEdges`, its own `BaseState`, `States` by id, `Children` by id, and the Lifestone-only `SourceLayout` |
| `UiStateDesc` | `StateDesc` | state id, pass-to-children, `UiIncorporation` flags, position, size and z level, properties by id, media in order |
| `UiProperty` (13 sealed records) | `BaseProperty` | a property id and a typed value; `UiPropertyLookup` reads them by type and throws on a type mismatch |
| `UiMedia` (11 sealed records) | `MediaDesc` + `MD_Data_*` | image, alpha, cursor, sound, state, jump, message, pause, fade, animation, movie |
| `UiStringRef` | `StringInfo` | a string id in a StringTable, or (for Lifestone's panels only) a literal |
| `UiBox` | `Box2D` | an inclusive rectangle, `Width = X1 - X0 + 1` |

- **Immutable.** Every collection keeps the dat's order (an ordered dictionary behind a read-only view). The dat order is
  the order Turbine's tool iterated its hash tables in; retail's own in-memory order differs (it re-inserts each entry at
  the head of its bucket's chain), so nothing should rely on either order for meaning. Draw order is the runtime's (R10).
- **Positions.** `ElementDesc::Serialize` stores X, Y, Width, Height and ZLevel only when the element's incorporation
  flag for each is set; the others are 0, as retail's `StateDesc` constructor leaves them. The states of an element's
  state table never store a position (the census below).
- **Reading.** Layouts come from `DatSet.TryGetLocal<LayoutDesc>`, which unpacks the language dat's bytes with a reader
  bound to the portal dat, where the master property table that types every property lives (m2-plan.md L2).
  `UiDescConverter.Convert` turns DatReaderWriter's record into the model.

### 1.1 Decoder traps, checked loudly

DatReaderWriter reads LayoutDesc, ElementDesc and StateDesc field for field as retail's serializers do, except in two
places that would misread the rest of a layout without an error (m2-plan.md §9 risk 10). `UiDescConverter` checks both,
and every failure is an `InvalidDataException` starting `TODO(retailui-data):` that names the layout, the element path,
the state and what it found.

- **StringInfo.** Retail's `StringInfo::Serialize` writes an override byte (0 none, 1 literal, 2 auto-generated); a
  UTF-16 literal, or the string id and table id; a has-strings byte, then three byte strings when it is 1; then the
  variables table. DatReaderWriter's `StringInfo` labels the override `Token`, the has-strings byte `Override`, and the
  variables table's bucket byte and count `English` and `Comment`, and reads only the no-literal, no-strings,
  no-variables shape. Override 1 or 2, has-strings 1 and a non-zero variable count all throw.
- **Media type 12** (`Stretch`). DatReaderWriter has no decoder and returns null; retail's `MediaDesc::CreateMediaType`
  creates nothing for it either. A null entry throws.
- It also throws on what retail would treat as corrupt: a hash key that differs from the id inside its value, a media
  entry whose two type fields differ, an unknown draw mode, and an edge mode above 4.

### 1.2 Census (UiLayoutCensusTests, against the user's dats)

| What | Count |
|---|---|
| Layouts | 101: 98 at 800×600, 3 at 800×500 (`classic_journal` 0x21000066, `classic_pagelist` 0x21000067, `classic_contracts` 0x21000069) |
| Elements | 2,162 (372 top-level); 1,088 partial descs (type 0), each naming a base layout and element; 695 Fields (type 3); 100 distinct classes |
| States | 3,304 StateDescs: 2,162 element base states and 1,142 in state tables, none with a position flag |
| Element flags | 0x0: 62, 0x18: 10, 0x1E: 1,656, 0x1F: 139, 0x38: 5, 0x3E: 256, 0x3F: 34 |
| Edge modes (left / top / right / bottom) | 0-4 on the left and right; 0-3 on the top and bottom (no Proportional) |
| Media | 1,310 images (842 Normal, 468 AlphaBlend; 624 distinct), 101 cursors, 13 pauses, 6 fades, 6 messages, 3 alpha, 3 sounds, 3 state changes, 2 jumps, 2 animations (15 frames each), 2 movies; no Stretch |
| Properties | 4,161 on states, of 155 ids: Bool 1,191, Enum 1,158, Integer 647, StringInfo 465, Array 424, DataId 223, Float 37, Color 9, Struct 7 |
| Strings | 465 StringInfo values, all table references (303 in 0x23000001, 162 in 0x23000002), all of which resolve; none nested in an array or struct |

`RawLayoutWalker` (a test helper) reads every layout's raw bytes again from retail's serializers, not DatReaderWriter's,
and the two agree on every element id, class, base, read order, edge, position field, state id, flag, property id and
type, string id and media type, with the bytes consumed exactly.

## 2. The layout catalogue (R2)

`UiLayouts` reads and converts each layout once, on first use, and is safe from any thread. It finds layouts by DID
(`GetLayout`, `TryGetLayout`) or by UILAYOUT enum (`ResolveLayoutEnum`, as `UIMainFramework::CreateAndAddRootElement`
does through group 5), top-level elements with `AccessElementDesc` (retail's `LayoutDesc::AccessElementDesc` looks at
no other level), and a mode's root desc with `GetRootDesc(UiRootRef)`. Without the language dat its constructor throws
`DatSet.RequireLocal`'s error, which names the file, the folder and the remedy.

- **UILAYOUT** (map 0x2500000E) names 81 layouts, each a different one, plus its `Undef` enum 0 (82 names, as m2-log-in
  §5.1 counted). The other 20 layouts are reached only as base layouts.
- **Mode roots** (`UiLayoutEnums`), from each mode's `CreateAndAddRootElement` call; each is a full-screen Field
  (class 3) at (0,0)-(799,599):

  | Mode | Root | Layout |
  |---|---|---|
  | data check (`gmDataPatchUI`) | 0x10000001 / 0x1000041A | 0x21000000 `patch` |
  | disconnected (`gmDisconnectedUI`) | 0x10000003 / 0x10000416 | 0x21000002 `disconnected` |
  | character management | 0x10000005 / 0x1000039A (eight children) | 0x21000004 `charactermanagement` |
  | character creation (`gmCharGenMainUI`) | 0x10000039 / 0x100003CC | 0x21000038 `chargen_master` |
  | game (`gmGamePlayUI::SetupChildren`) | 0x10000006 / 0x10000495 | 0x21000005 `classic_gameplay` |

- **Dialogs.** UILAYOUT enum 2 (`Dialog`) is 0x2100003C. It holds six full-screen elements, one per dialog class
  (element:class 0x15:0x13, 0x1B:0x16, 0x1F:0x14, 0x24:0x17, 0x2C:0x15, 0x31:0x19), each with one frame child 0x3D
  inheriting from 0x21000042/0x100002AF. (The raw plan also listed an element 0x28; the layout has none.) Which dialog
  type uses which element is DialogFactory's (R15).
- **The gameplay root (input to D36).** `classic_gameplay` is the end-of-retail floating-window shell, not the
  `classic_*` set m2-log-in §5.3 lists. Root 0x10000495 (z level 1, states 0x10000063 and 0x10000064) holds 18 windows,
  each a partial desc of another layout's template: the SmartBox (`classic_smartbox`), the keyboard and admin windows,
  four floating chats (unnamed layout 0x2100005B; hidden until toggled), and eleven `classic_floaty*` windows (vitals,
  side vitals, radar, main chat, toolbar, panel, environment panel, indicators, power bar, combat panel, examination).
  `classic_chat`, `classic_radar`, `classic_vitals`, `classic_toolbar` and `classic_panel` are not among them. Through
  inheritance, though, the windows' contents reuse templates of 75 layouts, `classic_chat`, `classic_toolbar`,
  `classic_characterinfo`, `classic_powerbar` and `classic_spewbox` among them; `classic_radar`, `classic_vitals`,
  `classic_panel`, `classic_indicators`, `classic_envpanel` and `classic_statusbar` are never reached
  (`ResolveAllElementsTests`).
  `gmGamePlayUI::SetupChildren` finds the SmartBox (0x1000049A), the radar (0x100006D2), the stacked and side-by-side
  vitals (0x100005FA, 0x100006D5) and the floating chats (0x10000505, 0x1000050E-0x10000510) by id
  (`UiLayoutEnums.Gameplay`).

## 3. Template inheritance and anchoring (R3)

### 3.1 Inheritance (`UiDescResolver`, `UiLayouts.InqFullDesc`)

Retail resolves a desc before it creates an element (`LayoutDesc::InqFullDesc`); the runtime calls
`UiLayouts.InqFullDesc(desc)` (or `RequireFullDesc`, which throws) for each desc it creates, then for each child of the
result.

- **The type gate.** A desc whose class (`Type`) is not 0 is used as authored (the same instance comes back).
- **The base.** A desc of class 0 names `BaseLayout`/`BaseElement`, which must be a **top-level** element of that layout.
  The base is resolved the same way, then the desc is merged onto it. Resolved bases are cached per (layout, element),
  successes only, as retail's `s_DescCache`.
- **Failures** (`UiResolveResult.Failure`, retail returns false and creates nothing): a missing base layout, a base
  element that is not top-level, and a base reached twice in one chain (retail's `add_unique`). The message names the
  element and every link, e.g. `element 0x1000039E of layout 0x21000004 does not resolve: base 0x21000040/0x100002D1:
  layout 0x21000040 has no top-level element 0x100002D1`.
- **`ElementDesc::Incorporate`** (`UiDescResolver.Incorporate`): the base state merges by `StateDesc::Incorporate`; the
  element id, default state, edges and read order are the derived desc's, the class and base links the base's;
  derived states merge onto the base's of the same id or join; derived children merge onto the base's of the same id
  (recursively, without resolving their own bases) or join with their read order raised by the number of base children
  the derived desc does not mention; and when the merge changed the width or height, the states and children only the
  base has are re-anchored from the old box to the new one (states by the element's edges, children by their own).
- **`StateDesc::Incorporate`**: flags are ORed; pass-to-children, X, Y, width, height and z level copy only when the
  derived flag for each is set; media append after the base's; properties replace or join; the state id stays.

On the user's dats every authored desc resolves except one: template 0x100002C2 of the shared layout 0x21000040 names
base element 0x10000480, which no layout holds. Nothing inherits from it and no property names it, so retail never
creates it either. Creating every layout's trees the way the runtime will (resolve, then recurse into the full desc's
children) makes 9,525 elements of 100 classes. The 1,087 resolvable partial descs name 196 distinct bases, through
chains of one to four bases (768, 183, 119 and 17 descs).

### 3.2 Anchoring (`UiAnchor`)

`UiAnchor.UpdateSizeAndPosition` is `StateDesc::UpdateSizeAndPosition` for a box or a state, and
`ElementDesc::UpdateSizeAndPosition` for an element (its base state, then every child by the child's own edges, from
the element's old box to its new one; its state table is left alone, and children are re-anchored even when the size
did not change). With `dW`/`dH` the parent's change in size and `W`/`H` its new size:

| Mode | Left / top | Right / bottom |
|---|---|---|
| 0 Keep | keeps | keeps |
| 1 OwnSide | keeps | `+ dW` / `+ dH` |
| 2 OppositeSide | `+ dW` / `+ dH` | keeps |
| 3 Centre | `W/2 - w/2` | `W/2 + w/2 - 1` |
| 4 Proportional | `x0 × W/oldW`, truncated | `x1 × W/oldW`, truncated |

- C integer division throughout, so a centred odd size in an odd parent loses a pixel; nothing is clamped.
- **Precision.** Retail stores the width ratio in a float and keeps the height ratio as a double. From 800 to 1024
  wide the ratio 1.28 becomes 1.27999997f, and a Proportional left edge at 100 lands on 127 (a double ratio gives
  128). An empty old parent gives ratio 0. Products are truncated toward zero; the 11 elements that use Proportional
  have no negative coordinate, so that reading of the 64-bit conversion is never exercised by the dats.
- **Hand-worked tests** (`UiAnchorTests`): every mode on every side, from 800×600 to 1280×720, 1920×1080 and
  2560×1440, and from an 800×500 layout to 1920×1080; mixed edges; the float pin; the empty parent; odd centring.
- **The gameplay shell** anchored to wide screens (`ResolveAllElementsTests`, hand-worked): the root and the SmartBox
  fill the screen; the radar keeps to the top-right corner (1920×1080: (1800,0)-(1919,139)); the main chat to the
  bottom-left ((0,980)-(409,1079)); the toolbar to the bottom-right ((1610,980)-(1919,1079)); the stacked vitals centre
  at the top ((880,0)-(1039,57)); the admin window centres ((752,340)-(1167,739)).
- **Roots.** The mode roots' own edges are 0000 (Keep), except the gameplay root's 1111. That matches retail's display
  handling: the login screens run in a forced 800×600 display mode, which `gmGamePlayUI`'s constructor releases
  (`Device::ForceDisplayResolution(0, 800, 600)`) and its destructor forces again (`(1, 800, 600)`), so only the game
  follows the user's resolution. How a root follows the screen and the UI scale in Lifestone is the runtime's policy
  (R10, `UiScalePolicy`; m2-log-in §5.4).

## 4. Master properties (R4)

`MasterPropertyTable.Load(dats)` converts MasterProperty 0x39000001 (portal dat): for each of its 383 properties the
id, the name, the value type (`UiPropertyType`, retail's numbering), the group, and the default, minimum and maximum
as `UiProperty` values. Its enum mapper names 384 ids: the extra one is id 0, which has no description. Twelve core
widget properties have a default (for example a button's hot-click intervals 0.2 s and 0.05 s, its click action 1, a
list box's selected-item state 6, a scrollable's keep-in-bounds true).

`UiPropertyIds` holds 192 constants: every `UICore_*` property of the table (the core widget classes: Menu, Button,
Text, Resizebar, Panel, Element, ListBox, Meter, Field, Scrollable, Scrollbar, ContextMenu, Dialog, Browser,
ColorPicker, GroupBox) and the chat, spew-box and radar properties of the M2 shell. Each is pinned by
`MasterPropertyTests`:

- its name is the table's name without the `UICore_` or `UI_` prefix and the underscores, ignoring case
  (`TextHorizontalJustification` is 0x14, `ElementHide` 0x3B, `DialogType` 0x8E);
- its `[PropertyType]` attribute is the table's type.

The layouts hold 171 property ids (155 on states, 16 more only inside arrays and structs), every occurrence typed as
the table says. Array items carry their own ids: a Text's font list (`TextFonts`) holds `TextFont` DIDs and its colour
list (`TextFontColors`) holds `TextFontColor` colours. `UiPropertyLookup` reads a collection by type and throws when a
property has another type than the caller expects; an absent property gets the caller's fallback, and retail's
defaults are the table's (the runtime decides when to apply them).

## 5. Fonts (R5)

`UiFonts` reads each Font (0x40xxxxxx, portal dat) once and finds it by DID or by FONT enum (group 9, map 0x25000012);
`UiFont` holds the glyphs (`UiGlyph`, retail's 11-byte `FontCharDesc`), the line metrics and the two atlas DIDs.

| What | Census (FontTests) |
|---|---|
| Fonts | 49 in the portal dat, none in the language or high-res dat |
| FONT enums | 48 names (plus `Undef`) for 39 DIDs; 11 fonts are reached by no enum (layouts name them by DID) |
| The gap | `FixedSys_15` (enum 0x12, only in the map's internal table) resolves to Font 0x40000033, which the portal dat does not hold: `GetFontByEnum` fails loudly, `TryGetFontByEnum` returns false |
| Font 0x40000000 (`Palatino_Linotype_16`) | 1,050 glyphs, U+0020-U+FFFC, line height 16, widest 16, baseline 12, 4 border pixels each way |
| Atlases | every one A8 in the portal dat; 37 fonts have a background (outline) atlas the size of the foreground one, 12 have none (DID 0); every glyph rectangle lies inside its atlas |

- **Glyph lookup** is `Font::GenerateCharacterMap` and `Font::GetCharDesc`: a map over the font's lowest to highest
  character starts zeroed, so a character inside that range that the font lacks draws as **glyph 0** (a space in 45
  fonts, U+0000 in the four Gulim fonts); only a character outside the range falls back to '?', and there is no glyph
  when '?' is outside it too. `Font::ContainsChar` is therefore true for the whole range. This is retail's behaviour,
  kept.
- **Advance**: `Font::GetCharWidthA` adds the width and both horizontal offsets as unsigned bytes and keeps the low
  byte (`UiGlyph.Advance`), so an offset of -1 subtracts one and a sum above 255 wraps; `Glyph::SetFont` stores it as an
  unsigned byte. A line is `MaxCharHeight` tall.
- **Fallback fonts**: `Glyph::SetFont` keeps the requested font when it contains the character or the character is a
  newline, else takes the FONT enum 22 (`Default_Japanese`) font, else enum 20 (`Default_Korean`), else none
  (`UiFonts.ResolveGlyphFont`). Both are Gulim 18 (0x40000018), whose range starts at U+0000, so in practice only
  control characters are drawn from it.
- **Atlases** decode with `TextureLoader.DecodeRenderSurface` in Native mode as `Alpha8` (`UiFont.DecodeForeground`,
  `DecodeBackground`), so text can take any colour.

## 6. Strings (R6)

`UiStringTables` reads each StringTable once and resolves STRINGTABLE enums (group 4) as `StringInfo::SetTableEnum` does,
keeping only DIDs in the StringTable range (enum 1, `Language`, names the LanguageInfo 0x41000000 and so no table).
`UiStrings` renders a `UiStringRef` as `StringInfo::GetString`; `StringHash` is `compute_str_hash` and `Tokenize`;
`StringMeta` is the meta-language subset; `UiLanguageInfo` formats integer variables.

| What | Census (StringTests) |
|---|---|
| Tables | 15: 0x23000001-0x23000010 without 0x2300000F; language 1 each |
| Strings | 6,899; 109 with variables (variable `i` goes after part `i`); no redirects; no variable-name table |
| Meta-language | 6,875 render with the subset; 24 use choice blocks, numbered labels (`#1:`) or `{{keepspaces}}` and show their raw text, each reported once |
| Layout strings | all 465 StringInfos of the 101 layouts render |
| M2 tokens | every `ID_CHAR_ERROR_*` token `gmUIFlow::RecvNotice_CharacterError` uses renders, except `ID_CHAR_ERROR_ENTER_GAME_OLD_CHARACTER` (error 17), which no table holds; `ID_DataPatch_PatchRate` is missing too. Retail shows its error text for both, as Lifestone does |

- **The hash** is the PJW/ELF hash over a C string with each byte added as a **signed** `char` (OpenAC adds it unsigned;
  ASCII tokens hash the same either way).
- **Rendering** follows `StringInfo::InqString` and `InqStringInternal`: a literal (override 1) is its text with its
  meta letters stripped; a table reference starts as `<string table error; token: 0x%08X tableDID: 0x%08X>`, which is
  what shows when the table or string is missing, a redirect fails, or a variable is missing (statuses 1-6); otherwise
  the parts and variables go through the meta-language, `StripMetaLetters` (unless retained; no UI caller retains), and
  `UnescapeString`.
- **The subset** (`StringMeta`, from `LoadTokenizer`, `_stringmeta_lex`, `r_Text`, `r_Var`, `RenderNodes` and
  `RenderString`): text with its escapes (a backslash before one of the escape characters keeps the pair together),
  plain variables (a value's meta letters after an unescaped `[` are dropped, and text after the `]` is retail's syntax
  error, status 7), then `TrimExcessSpaces` (leading whitespace dropped, every run of spaces and tabs collapsed to its
  first character, so retail shows the tables' double spaces as single ones). Escapes are undone last: backslash-n is a
  newline, backslash-q a quote. A string variable is escaped when it is set (`AddVariable_String`), so a name with
  brackets shows as typed.
- **Not built** (`UiStringStatus.Unsupported`, one `TODO(retailui-data)` line per string through the `todo` callback):
  choice blocks, numbered labels, `^`, `{{...}}` outside-information blocks. None of the M2 screens' strings uses one.
- **Whitespace set.** `PStringBase<unsigned short>::whitespace_string` is static data the decompile does not show; the
  trims use space, tab, CR and LF. The tables hold no literal tab, CR or LF (pinned), so only a variable's text could
  tell. TODO(retailui-data): read `s_WPString_Whitespace` from the 11.6096 binary.
- **Integers** format as `StringInfoDataHelper::NumToString` with precision 0 and grouping
  (`LInt_StringInfoData::ToString`): LanguageInfo 0x41000000 gives base 10, groups of 3 with ",", and "-%s" for
  negatives (1234567 is "1,234,567").

## 7. Cursors, key maps and the image census (R7)

- **Cursors** (`UiCursors`): UICURSOR (group 6, map 0x2500000F) names 41 cursors, 37 distinct A8R8G8B8 images of at most
  32×32 in the portal dat. `gmUIFlow::gmUIFlow` sets enum 1 (`Default`, 0x06004D68) with hot spot (0, 0) when the
  interface starts (`UIElementManager::SetCursor`). The enum names carry no hot spots; game code passes them case by case,
  and an element's own cursor carries its media entry's hot spot (`UiCursor.FromMedia`).
- **Key map** (`RetailKeyMap`): KEYMAP enum 0x10000001 (`gmDefault`, group 10) resolves to MasterInputMap 0x14000000
  named `gmDefaultMap`: keyboard (device 0) and mouse (device 1), 9 meta keys (left Shift 0x80000000, both Ctrl
  0x40000000, both Alt 0x20000000, both Windows keys 0x10000000, two mouse buttons; right Shift is none), 14 input maps.
  Each binding is a control (`m_dwKey`: device index, sub-control, DIK scan code or mouse control in the high word), a
  meta mode, an activation (1 Down, 2 Up, 3 Click, ...) and an action; DatReaderWriter reads these four dwords as
  `Key.Key`, `Key.Modifier`, `Activation` and `Unknown`. Pinned: Move Forward (map 4, action 0x29) on W and Up, Toggle
  Run/Walk (0x32) on left Shift, Jump (0x31) on Space, Begin Chat Mode (map 0x1000000A, 0x10000023) on Enter,
  Enter/Exit Chat Mode (0x1000000D) on Tab, Escape (map 0x10000009, action 0x27, not user-bindable) on Esc and End
  Character Session (0x10000026) on Shift+Esc.
- **Action map** (`RetailActionMap`): ACTIONMAP enum 1 is ActionMap 0x26000000: 27 input maps (two empty), each action's
  toggle type and binding-screen class, name and description, the names in StringTable 0x23000005; 16 input maps list the
  maps their bindings conflict with. Every binding of `gmDefaultMap` fires an action its map defines.
- **Image census** (UiImageCensusTests), by the kind of media entry that names the image:

  | Kind | Named | Distinct | Resolve | Formats |
  |---|---|---|---|---|
  | Image (`UiImageMedia.File`) | 1,310 | 624 | 623 | 325 A8R8G8B8, 292 R8G8B8, 6 JPEG |
  | Alpha mask (`UiAlphaMedia.File`) | 3 | 3 | 3 | A8R8G8B8 (170x99, 254x113, 211x211) |
  | Animation frame (`UiAnimationMedia.Frames`) | 30 (2 animations of 15) | 30 | 30 | R8G8B8, 325x52 |
  | Layout cursor (`UiCursorMedia.File`) | 101 | 5 | 5 | A8R8G8B8, 32x32 |

  No DID is named by two kinds (662 distinct in all), and none of the 5 layout cursors is one of the 41 UICURSOR
  images. Every alpha mask, frame and layout cursor decodes to RGBA8 at its own size. The one image that does not
  resolve is **DID 0**, retail's INVALID_DID, named 43 times in 17 states of 11 layouts (the shared dialog frame's
  corner pieces in 0x21000042, the chat windows' buttons, the vitals' and the toolbar's empty states): an image entry
  that clears the element's image, which the compositor (R11) must draw as nothing. No UI image of any of the four
  kinds is in `client_highres.dat`, so the loader's high-res-first rule never changes one.

## 8. CharGen data (R8)

`CharGenData` (CharGen 0x0E000002, UNIQUEDB enum 0xE `CharGen_CharacterData`) and `SkillTableData` (SkillTable
0x0E000004, UNIQUEDB enum 4 `Weenie_SkillTable`) are immutable projections in `Data/CharGen/`. The namespace is
`Lifestone.Data.CharacterCreation`, not `...CharGen`: a namespace named `CharGen` under `Lifestone.Data` would hide
DatReaderWriter's `CharGen` record from every `Lifestone.Data.*` file that names it unqualified (existing tests and the
avatars track do).

| What | Census (CharGenDataTests) |
|---|---|
| Heritages | 13: Aluvian, Gharu'ndim, Sho, Viamontian, Umbraen, Gear, Tumerok, Lugian, Empyrean, Penumbraen, Undead, Olthoi, OlthoiAcid; genders 1 and 2 each |
| Credits | 330 attribute and 52 skill credits, except the Olthoi (12): 60 and 68 |
| Templates | 7 each (index 0 the custom "Adventurer"), one for each Olthoi heritage |
| Heritage skills | every heritage lists only Arcane Lore (14): 0 to train, 2 to specialise |
| Start areas | Holtburg, Shoushi, Yaraq, Sanamar (5 Academy cells each, all at (12.32, -28.482, 0.005); Holtburg's first is 0x860201AD, as protocol.md §10 says) and OlthoiLair (1); the Olthoi start only in the lair |
| SkillTable | 38 skills, highest id 54, so 55 advancement classes; every CharGen-usable skill is below 55 |

- **Records**: `CharGenHeritage` (name, icon, Setups, credits, start areas, skills, templates, genders),
  `CharGenGender` (Setup, sound, physics, motion and combat tables, palettes, `BaseObjDesc` and every appearance list),
  `CharGenTemplate`, `CharGenStartArea` (locations as `CellFrame`s), `CharGenGear`, `CharGenObjDesc` (the dat's ObjDesc
  as values; the avatars track applies it), `SkillTableEntry` with `SkillTableFormula` (`SkillFormula::Calculate`).
- **Skill costs** (`GetSkillTrainedCost`, `GetSkillSpecializedCost`): the heritage's cost when it lists the skill, else
  the SkillTable's, else -1. The specialised cost is the total, not the step from trained.
- **Clothing colours** (`GetClothingColors`): `CharGenState::StoreColorInformation` walks a gear's ClothingTable palette
  templates in retail's `PackableHashTable` order (bucket by bucket, key modulo the stored table size, each bucket
  newest first) and keeps those the gender lists with at least one sub-palette effect; the PalSet is the first
  effect's. Every gear of every gender offers at least one colour. `HasPaletteTemplate` is
  `ClothingTable::GetCloPaletteTemplate` for the verifier.

## 9. Lifestone layouts (R9)

`LifestoneLayoutReader` reads Lifestone's own panels (the tools panel and the inspector, whose files G7 adds in wave 2
under `src/Lifestone.Data/Ui/Layouts/`) from JSON, format `lifestone-layout/1` (the class remarks give the grammar), into
`UiLayout`s; `UiLayouts.Register` adds them in DIDs 0x7F000000-0x7FFFFFFF, after which they are found and resolved
exactly like retail layouts.

- **Retail ids only.** An element gives a UI class (`type`) or a retail template (`base`: a top-level element of a
  retail layout, which the resolver merges as `LayoutDesc::InqFullDesc`); properties are named by their master-property
  name and typed by the table; images, sounds and cursors are retail DIDs; strings are a table and token (or enum) or a
  Lifestone literal (override 1). A literal is at most 256 characters, and anything that looks like embedded data (a
  `data:` URI or a base64 run of 48 characters or more) is refused. A position or size field sets its incorporation
  flag, so a partial desc overrides only what it gives.
- **Embedded files (G7).** The files under `Ui/Layouts/` are embedded resources that `UiLayouts` reads and registers on the first lookup of a DID in the Lifestone range (`tools.json` 0x7F000010, `inspector.json` 0x7F000002). Each needs its own DID: a second file with a DID an earlier embedded file holds is an error naming both files. Every failure to load (a file that does not read, a duplicate DID, an unopenable resource, any exception) is logged once through `UiLayouts.ErrorLog` and kept: every later lookup in the range throws the same `TODO(g7-layouts)` message instead of answering "absent". A layout registered by hand (a test's) under the same DID wins over an embedded one.
- **Never throws**: every problem is reported with its JSON path (the `ShotList` pattern), and the result is null when
  there is any. A literal renders like retail's: its meta letters are stripped and its escapes undone, so a literal that
  means a `[` must escape it.
- **Literals in code.** `UiStringRef.FromLiteral(text)` takes meta-language text as it is (so
  `"0xA9B4001A [84.0 7.1 94.0]"` shows as `0xA9B4001A`); `UiStringRef.FromPlainText(text)` escapes the text with
  `StringMeta.EscapeString` first (retail's `AddVariable_String` rule), so it shows exactly as given, brackets,
  backslashes, quotes, line breaks and runs of spaces included (StringTests). Text built at run time, such as the tools
  panel's coordinates, takes `FromPlainText`.
- **Numbers are checked, not coerced.** A cursor hot spot is two whole numbers, none negative (absent: 0, 0); a state
  change's probability is a number in 0-1 (absent: 1); a pause is 0 <= min <= max seconds; a fade's alphas are 0-1
  and its seconds not negative; every float (these, Float and Vector properties) must be finite as a 4-byte float.
  The retail layouts' own media keep to these ranges (alphas 0 and 1, probabilities 1, pauses 0.1-5 s).
- **Tests** (LifestoneLayoutTests): a tools panel in the shape G7 will author (frame 0x21000042/0x100002AF, text and
  button templates of 0x21000040) reads, registers and resolves; malformed values are reported per field, one case per
  media rule; every file in `Layouts/` (`tools.json` and `inspector.json` from G7, embedded by `Lifestone.Data.csproj` and registered by `UiLayouts` on the first lookup in the Lifestone DID range; `LifestoneLayoutFiles` lists them) must read and hold no retail string text, with literals collected
  inside arrays and structs too (a synthetic layout with retail text nested in an array shows the walk finds it).

## 10. Character creation state (R18a)

`CharGenState` (Client) re-expresses retail's `CharGenState` member by member; `CharGenVerifier` re-expresses
`ACCharGenResult::VerifyCharacterGenerationResult`; `CharGenNames.Format` is `ACCharGenData::FormatName`;
`CharGenCatalog` bundles the CharGen data, the SkillTable and the WCLASSID classes (`HUMAN` 1, `OLTHOIPLAYER` 0xA9D8,
`OLTHOIACIDPLAYER` 0xA9D9); `CharGenResult.ToIntent` gives the state track's `CharacterCreateIntent`
(`CharGenState.GetResult`, m2-plan §4.11).

- **Flow.** Opening CharGen randomises a whole character (`RandomizeCharacter`: heritage 1-4 with Throne of Destiny,
  gender, appearance, clothing, template, start area); the heritage page then calls `SetHeritageGroup`, which takes the
  new heritage's credits, applies the current template, picks a start area and clamps every appearance index to the new
  lists (`ConstrainAllByHeritage`, `ConstrainAllByGender`). Setting a style recomputes its colours.
- **The 3D view's look.** `CharGenState.ToLook()` gives the appearance fields as `CharGenLook` (each garment's colour index as its palette template, the
  shades as they are); `Cg3DViewModel` composes it with `CharGenAppearance.Compose` and poses it (retail-ui.md §20).
- **Attributes.** A setter always lowers and raises only while `GetAbsRemainingCredits` (credits left with every
  unlocked other attribute at 10) is not 0; with balancing, the other attributes give back the overspend one point at a
  time, round-robin (Strength, Endurance, Coordination, Quickness, Focus, Self) from where the last balance stopped,
  never below 10, skipping locked ones. The setters do not clamp to 10-100; the profession page does.
- **Skills.** `ResetSkillLevels` makes every free skill trained (Arcane Lore, Magic Defense, Jump, Run, Loyalty,
  Salvaging) and the rest untrained; `SetSkillLevel` refunds the current level and charges the new one, refusing what
  the credits cannot pay. Templates train their normal skills and specialise their primary ones as far as the credits
  go; Bow Hunter spends exactly 52.
- **Names.** At most 32 characters; letters stay, spaces, apostrophes and hyphens only between the right neighbours;
  the first letter is upper-cased and the rest lower-cased, except a letter after a space, apostrophe or hyphen, or
  after a `Mac`, `Mc`, `Fitz`, `Von`, `Van`, `De`, `Di`, `Du`, `Le` or `La` prefix, which keeps its typed case, and a
  word of capital I, V and X. The case classes are retail's `CharCase` enum, a PDB type of 11.4186 (`acclient.h` of
  Mag-nus/acclient-AI-RE): `CASE_UPPER` 0, `CASE_LOWER` 1, `CASE_EITHER` 2 (as typed); 11.6096 is assumed unchanged.
- **Verification** (retail's order): heritage (Viamontian needs Throne of Destiny), gender 1-2, every index inside its
  list and every clothing colour a palette template of its gear's table, shades 0-1, attributes 10-100 within the
  credits, exactly the SkillTable's 38 skills with a class 1-3 and their costs within the credits, a name formatting
  leaves unchanged, a template of the heritage or -1, an existing start area (Sanamar needs Throne of Destiny). Every
  end-of-retail account has Throne of Destiny, so it defaults to true.
- **Tests** (CharGenStateTests): for each of the 13 heritages and both genders, over four seeds, the state CharGen
  shows after picking that heritage verifies, has exactly 55 advancement classes, attributes of 10-100 within the
  credits and no negative skill credits, its intent validates, and `AceCreateRules` (ACE's create checks restated from
  ACEmulator/ACE@95ef426d PlayerFactory.cs 135-209, 358 and 606-631, not copied) accepts it. Template application, skill
  limits, balancing, seeded randomisation, the verifier's messages and the intent mapping (every attribute and
  appearance field, with distinct values) are covered too.
- **Template -1.** Retail's verifier allows a template of -1, and `SetTemplate` can set it, but ACE indexes the
  heritage's templates with it (PlayerFactory.cs:135, for every heritage but the Olthoi) and throws, so the session
  must be sent a template of 0 or more. The normal flow always has one: every heritage but the Olthoi has 7 templates,
  and the Olthoi always use template 0.
- **Random numbers** come from an injected `ICharGenRandom` (`CharGenRandom(seed)`): retail's two generators (the C
  runtime's `rand()` and `Random::rand`) are not reproduced, only how their values become choices (`CharGenDice`:
  `RandInt`, `RandomReal`, `RollDice`).
- **Deviations.** The balance round-robin's start is per state (retail: the global `startAtrb`, shared by the whole
  process); its first value is retail's, 1 (Strength), as the 11.4186 data initialises `startAtrb` (`acclient_data.h` of
  Mag-nus/acclient-AI-RE; 11.6096 assumed unchanged); a balance that can lower nothing ends instead of hanging; a style
  index outside its list gives no colours instead of reading past the list; the 3D view's face palette
  (`ClientCharGenState::UpdateTrueFacePal`), the verification state and administrator or envoy characters are left out.

## 11. Retail functions

| Function | 11.4186 | 11.6096 | Used for |
|---|---|---|---|
| `LayoutDesc::Serialize` | 0x0069A490 | 0x0069B370 | R1 walker |
| `ElementDesc::Serialize` | 0x0069BBA0 | 0x0069CA30 | R1 walker, position fields |
| `StateDesc::Serialize` | 0x0069C5F0 | 0x0069D480 | R1 walker |
| `StringInfo::Serialize` | 0x0042DEB0 | 0x0042E0D0 | `UiStringRef`, the StringInfo check |
| `MediaDesc::CreateMediaType` | 0x0069D420 | 0x0069E230 | `UiMediaType`, the Stretch check |
| `MediaDesc::Serialize` | 0x0069D700 | 0x0069E510 (folded: the table names it `FileNodeName_UInt32::Serialize`) | the media type check |
| `PropertyCollection::Serialize` | 0x00681420 | 0x00682420 | R1 walker |
| `BaseProperty::Serialize` | 0x0042A090 | 0x0042A330 | R1 walker |
| `MasterProperty::AllocatePropertyValue` | 0x00429120 | 0x004293C0 | `UiPropertyType` |
| `ArrayPropertyValue::Serialize` | 0x00427FD0 | 0x004282C0 | R1 walker |
| `StructPropertyValue::Serialize` | 0x00429490 | 0x00429730 | R1 walker |
| `UIMainFramework::CreateAndAddRootElement` | 0x006A0C00 | 0x006A1A20 | `ResolveLayoutEnum`, `GetRootDesc` |
| `LayoutDesc::AccessElementDesc` | 0x00699990 | 0x0069A850 | `AccessElementDesc` |
| `DBObj::GetDIDByEnum` | 0x004153A0 | 0x00415640 | (through `EnumIdMaps`) |
| `LayoutDesc::InqFullDesc` | 0x0069A520 | 0x0069B400 | `UiDescResolver.InqFullDesc` |
| `SmartArray<BaseInfo,1>::add_unique` | 0x00699B40 | 0x0069AA00 | the cycle check |
| `ElementDesc::Incorporate` | 0x0069B5A0 | 0x0069C430 | `UiDescResolver.Incorporate` (elements) |
| `StateDesc::Incorporate` | 0x0069CCA0 | 0x0069DB30 | `UiDescResolver.Incorporate` (states) |
| `StateDesc::ConcatenateMedia` | 0x0069C9B0 | 0x0069D840 | media order |
| `PropertyCollection::UpdateCollection` | 0x006807E0 | 0x006817E0 | property override |
| `StateDesc::UpdateSizeAndPosition` | 0x0069BF20 | 0x0069CDB0 | `UiAnchor` (boxes, states) |
| `ElementDesc::UpdateSizeAndPosition` | 0x0069A830 | 0x0069B6A0 | `UiAnchor` (elements) |
| `gmDataPatchUI::gmDataPatchUI` | 0x004EE2B0 | TODO(retailui-data): 11.6096 | data-check root |
| `gmDisconnectedUI::gmDisconnectedUI` | 0x004E9A10 | TODO(retailui-data): 11.6096 | disconnected root |
| `gmCharacterManagementUI::gmCharacterManagementUI` | 0x004EC8F0 | TODO(retailui-data): 11.6096 | character-management root |
| `gmCharGenMainUI::gmCharGenMainUI` | 0x004E7EB0 | 0x004E8B40 | CharGen root |
| `gmGamePlayUI::SetupChildren` | 0x004E9EC0 | 0x004EAB50 | gameplay root and its named children |
| `gmGamePlayUI::gmGamePlayUI` | 0x004EA010 | 0x004EACA0 | releases the forced 800×600 display (§3.2) |
| `gmGamePlayUI::~gmGamePlayUI` | 0x004EA2A0 | 0x004EAF30 | forces it again |
| `Device::ForceDisplayResolution` | 0x0043A750 | 0x0043A990 | (the display note) |
| `MasterProperty::Serialize` | 0x00428BF0 | 0x00428E90 | (through DatReaderWriter) the table |
| `MasterProperty::AccessPropertyDesc` | 0x004281C0 | 0x004284B0 | `MasterPropertyTable.Get` |
| `MasterProperty::InqPropertyNameStringStatic` | 0x004265B0 | 0x004268D0 | `MasterPropertyTable.NameOf` |
| `UIElement::OnSetAttribute` | 0x00462D80 | 0x00462E60 | (the runtime) the Element properties |
| `UIElement_Button::OnSetAttribute` | 0x00471F40 | 0x00472300 | (the runtime) the Button properties |
| `UIElement_Text::OnSetAttribute` | 0x0046A640 | 0x0046A8D0 | (the runtime) the Text properties |
| `Font::GetCharDesc` | 0x00443360 | 0x004434C0 | `UiFont.GetCharDescIndex` (R5) |
| `Font::GetCharWidthA` | 0x004433F0 | 0x00443550 | `UiGlyph.Advance`, `UiFont.GetCharWidth` |
| `Font::ContainsChar` | 0x00443420 | 0x00443580 | `UiFont.ContainsChar` |
| `Font::GenerateCharacterMap` | 0x00443470 | 0x004435D0 | `UiFont`'s character map |
| `Font::Serialize` | 0x00443650 | 0x004437B0 | (through DatReaderWriter) the glyph layout |
| `Glyph::SetFont` | 0x004749A0 | 0x00474D60 | `UiFonts.ResolveGlyphFont` |
| `DBObj::GetByEnum` | 0x00415490 | 0x00415730 | the FONT, CharGen and SkillTable enums |
| `compute_str_hash` | 0x00413110 | 0x004016B0 | `StringHash.Compute` (R6) |
| `Tokenize` | 0x00401A10 | 0x00401A20 | `StringHash.Tokenize` |
| `StringInfo::SetTableEnum` | 0x0042C720 | 0x0042C980 | `UiStringTables.TryResolveTableEnum` |
| `StringInfo::SetStringIDandTableEnum` | 0x0042C760 | 0x0042C9C0 | `UiStringTables.Reference` |
| `MasterDBMap::DivineType` | 0x0041BE40 | 0x0041C260 | the StringTable DID range |
| `StringInfo::InqStringInternal` | 0x0042E020 | 0x0042E240 | `UiStrings.Render` |
| `StringInfo::InqString` | 0x0042E490 | 0x0042E6B0 | `UiStrings.Render` (the unescape) |
| `StringInfo::GetString` | 0x0042E760 | 0x0042E980 | `UiStrings.GetString` |
| `StringInfo::AddVariable_String` | 0x0042E6C0 | 0x0042E8E0 | `UiStringVariables.SetText` |
| `StringInfo_StringInfoData::ToString` | 0x0042F700 | 0x0042F920 | `UiStringVariables.SetMetaText` |
| `LInt_StringInfoData::ToString` | 0x0042F7E0 | 0x0042FA00 | `UiStringVariables.SetInteger` |
| `StringInfoDataHelper::NumToString` | 0x0042F280 | 0x0042F4A0 | `UiLanguageInfo.FormatInteger` |
| `StringTable::GetString` | 0x004300D0 | 0x00430270 | `UiStrings.Render` (statuses 2, 4-7) |
| `StringTable::GetStringTableString` | 0x00430000 | 0x004301A0 | `UiStringTable.TryGetEntry` |
| `StringTable::Serialize` | 0x004309C0 | 0x00430B60 | (through DatReaderWriter) the tables |
| `StringTableString::Serialize` | 0x00430480 | 0x00430620 | the variable-name trap |
| `StringTableMetaLanguage::LoadTokenizer` | 0x0067F020 | 0x00680020 | `StringMeta.Render` |
| `_stringmeta_lex` | 0x0067EAC0 | 0x0067FAC0 | `StringMeta.Render` (the tokens) |
| `IsNumberLabel` | 0x0067E630 | 0x0067F630 | the unsupported numbered labels |
| `stringmeta_parse` | 0x0067DFE0 | 0x0067EFE0 | (the grammar's node actions) |
| `StringTableMetaLanguage::r_Text` | 0x0067DA40 | 0x0067EA40 | text nodes |
| `StringTableMetaLanguage::r_Var` | 0x0067DAB0 | 0x0067EAB0 | variable values and their meta letters |
| `StringTableMetaLanguage::RenderNodes` | 0x0067C3C0 | 0x0067D3C0 | the node order |
| `StringTableMetaLanguage::RenderString` | 0x0067D150 | 0x0067E150 | `keepspaces` and the trim |
| `StringTableMetaLanguage::TrimExcessSpaces` | 0x0067B9B0 | 0x0067C9B0 | `StringMeta.TrimExcessSpaces` |
| `PSUtils::TrimExcessSpaces` | 0x0067B7F0 | 0x0067C7F0 | `StringMeta.TrimExcessSpaces` |
| `PStringBase<unsigned short>::trim` | 0x004D5C60 | 0x004D68C0 | the leading and trailing trims |
| `StringTableMetaLanguage::StripMetaLetters` | 0x0067BAF0 | 0x0067CAF0 | `StringMeta.StripMetaLetters` |
| `PSUtils::wcschr_skipescapes` | 0x0040AFE0 | 0x0040B340 | `StringMeta.IndexOfUnescaped` |
| `StringTableMetaLanguage::IsEscapeChar` | 0x0067B690 | 0x0067C690 | `StringMeta.IsEscapeChar` |
| `StringTableMetaLanguage::GetEscapedChar` | 0x0067B6C0 | 0x0067C6C0 | `StringMeta.GetEscapedChar` |
| `StringTableMetaLanguage::GetUnEscapedChar` | 0x0067B750 | 0x0067C750 | `StringMeta.GetUnescapedChar` |
| `StringTableMetaLanguage::EscapeString` | 0x0067BBC0 | 0x0067CBC0 | `StringMeta.EscapeString` |
| `StringTableMetaLanguage::UnescapeString` | 0x0067BDC0 | 0x0067CDC0 | `StringMeta.UnescapeString` |
| `gmUIFlow::gmUIFlow` | 0x0047A610 | 0x0047AA10 | the default cursor (R7) |
| `gmUIFlow::RecvNotice_CharacterError` | 0x0047A7C0 | 0x0047ABC0 | the character-error tokens |
| `UIElementManager::SetCursor` | 0x0045A800 | 0x0045A910 | `UiCursors.Default` |
| `CMasterInputMap::Serialize` | 0x0068FCE0 | 0x00690C30 | `RetailKeyMap` |
| `CInputMap::Serialize` | 0x0068EE60 | 0x0068FDB0 | `RetailBinding` |
| `CMasterInputMap::MetaModeFromKey` | 0x0068D850 | 0x0068E7A0 | `RetailKeyMap.MetaModeOf` |
| `ControlSpecification::ToFileNode` | 0x0068AB80 | 0x0068BB80 | `RetailControl`'s fields |
| `QualifiedControl::operator==` | 0x0068A1D0 | 0x0068B260 | the activation's top bit |
| `ControlActivationTypeToString` | 0x0068A940 | 0x0068B940 | `RetailBinding.ActivationType` |
| `DeviceTypeToString` | 0x0068A8D0 | 0x0068B8D0 | `RetailDeviceType` |
| `SubControlIndexToString` | 0x0068AAD0 | 0x0068BAD0 | `RetailControl.SubControl` |
| `ActionMap::Serialize` | 0x00685F10 | 0x00686F60 | `RetailActionMap` |
| `ActionMapValue::Serialize` | 0x00684A30 | 0x00685A80 | `RetailAction` |
| `ActionMap::IsUserBindable` | 0x006854E0 | 0x00686530 | `RetailAction.IsUserBindable` |
| `ActionMap::GetConflictingInputMaps` | 0x00684EE0 | 0x00685F30 | `RetailActionMap.ConflictingInputMaps` |
| `ACCharGenData::GetSkillTrainedCost` | 0x005C26D0 | 0x005C36B0 | `CharGenData.GetSkillTrainedCost` (R8) |
| `ACCharGenData::GetSkillSpecializedCost` | 0x005C27D0 | 0x005C37B0 | `CharGenData.GetSkillSpecializedCost` |
| `ACCharGenData::GetHG` | 0x005C23A0 | 0x005C3380 | `CharGenData.TryGetHeritage` |
| `HeritageGroup_CG::GetSX` | 0x005C1BC0 | 0x005C2B30 | `CharGenHeritage.TryGetGender` |
| `PackableHashTable<unsigned long,CloPaletteTemplate>::add` | 0x005A7E30 | 0x005A8EE0 | the clothing colours' order |
| `PackableHashTable<unsigned long,CloPaletteTemplate>::begin` | 0x005A73F0 | 0x005A8530 | the clothing colours' order |
| `ClothingTable::GetCloPaletteTemplate` | 0x005A74B0 | 0x005A85F0 | `CharGenData.HasPaletteTemplate` |
| `SkillFormula::Calculate` | 0x00591960 | 0x00592660 | `SkillTableFormula.TryCalculate` |
| `CharGenState::CharGenState` | 0x005C6A50 | 0x005C7A30 | `CharGenState` (R18a) |
| `CharGenState::Reset` | 0x005C68A0 | 0x005C7880 | `CharGenState.Reset` |
| `CharGenState::SetHeritageGroup` | 0x005C67A0 | 0x005C7780 | `CharGenState.SetHeritageGroup` |
| `CharGenState::SetGender` | 0x005C64A0 | 0x005C7480 | `CharGenState.SetGender` |
| `CharGenState::ConstrainAllByHeritage` | 0x005C6590 | 0x005C7570 | `CharGenState.ConstrainAllByHeritage` |
| `CharGenState::ConstrainAllByGender` | 0x005C5B80 | 0x005C6B60 | `CharGenState.ConstrainAllByGender` |
| `CharGenState::GetAttribute` | 0x005C39B0 | 0x005C4990 | `CharGenState.GetAttribute` |
| `CharGenState::GetAbsRemainingCredits` | 0x005C3B20 | 0x005C4B00 | `CharGenState.GetAbsRemainingCredits` |
| `CharGenState::LockAttribute` | 0x005C3BE0 | 0x005C4BC0 | `CharGenState.LockAttribute` |
| `CharGenState::ResetAttributeLock` | 0x005C3BC0 | 0x005C4BA0 | `CharGenState.ResetAttributeLock` |
| `CharGenState::SetStrength` .. `SetSelf` | 0x005C4660-0x005C48E0 | 0x005C5640-0x005C58C0 | `CharGenState.SetAttribute` |
| `CharGenState::BalanceAttributes` | 0x005C3DF0 | 0x005C4DD0 | `CharGenState.BalanceAttributes` |
| `CharGenState::GetSkillLevel` | 0x005C3C00 | 0x005C4BE0 | `CharGenState.GetSkillLevel` |
| `CharGenState::SetSkillLevel` | 0x005C3C20 | 0x005C4C00 | `CharGenState.SetSkillLevel` |
| `CharGenState::ResetSkillLevels` | 0x005C43B0 | 0x005C5390 | `CharGenState.ResetSkillLevels` |
| `CharGenState::UpdateRemainingSkillCredits` | 0x005C37C0 | 0x005C47A0 | `CharGenState.UpdateRemainingSkillCredits` |
| `CharGenState::GetSkillScore` | 0x005C4B50 | 0x005C5B30 | `CharGenState.GetSkillScore` |
| `CharGenState::SetTemplate` | 0x005C5A60 | 0x005C6A40 | `CharGenState.SetTemplate` |
| `CharGenState::ApplyTemplate` | 0x005C5080 | 0x005C6060 | `CharGenState.ApplyTemplate` |
| `CharGenState::ApplyDefaultTemplate` | 0x005C4990 | 0x005C5970 | `CharGenState.ApplyDefaultTemplate` |
| `CharGenState::FitTemplateToCharacter` | 0x005C6130 | 0x005C7110 | `CharGenState.FitTemplateToCharacter` |
| `CharGenState::SetSkinShade`, `SetHairShade`, `SetHairColor`, `SetEyeColor` | 0x005C3850-0x005C38D0 | 0x005C4830-0x005C48B0 | the appearance setters |
| `CharGenState::SetHeadgearStyle` .. `SetFootwearStyle` | 0x005C5350-0x005C56C0 | 0x005C6330-0x005C66A0 | the style setters |
| `CharGenState::StoreColorInformation` | 0x005C44D0 | 0x005C54B0 | `CharGenData.GetClothingColors` |
| `CharGenState::GetBaldState` | 0x005C52A0 | 0x005C6280 | `CharGenState.GetBaldState` |
| `CharGenState::GetSetupID` | 0x005C5A90 | 0x005C6A70 | `CharGenState.GetSetupId` |
| `CharGenState::RandomizeAppearance` | 0x005C4F10 | 0x005C5EF0 | `CharGenState.RandomizeAppearance` |
| `CharGenState::RandomizeHeadgear` .. `RandomizeFootwear` | 0x005C5E10-0x005C6070 | 0x005C6DF0-0x005C7050 | the clothing randomisers |
| `CharGenState::RandomizeClothing` | 0x005C6770 | 0x005C7750 | `CharGenState.RandomizeClothing` |
| `CharGenState::RandomizeTemplate` | 0x005C6500 | 0x005C74E0 | `CharGenState.RandomizeTemplate` |
| `CharGenState::RandomizeStartArea` | 0x005C59E0 | 0x005C69C0 | `CharGenState.RandomizeStartArea` |
| `CharGenState::RandomizeSkills` | 0x005C57E0 | 0x005C67C0 | `CharGenState.RandomizeSkills` |
| `CharGenState::RandomizeHeritageGroup` | 0x005C6A20 | 0x005C7A00 | `CharGenState.RandomizeHeritageGroup` |
| `CharGenState::RandomizeCharacter` | 0x005C6D80 | 0x005C7D60 | `CharGenState.RandomizeCharacter` |
| `CharGenState::SetStartArea` | 0x005C4000 | 0x005C4FE0 | `CharGenState.SetStartArea` |
| `CharGenState::SetName` | 0x005C3D10 | 0x005C4CF0 | `CharGenState.SetName` |
| `CharGenState::GetCharGenResult` | 0x005C4030 | 0x005C5010 | `CharGenState.GetCharGenResult` |
| `CharGenState::GetRandomReal` | 0x00563940 | 0x005646E0 | `CharGenDice.RandomReal` |
| `RandInt` (both) | 0x00684400, 0x00684420 | 0x00685400, 0x00685420 | `CharGenDice.RandInt` |
| `Random::RollDice` | 0x0042C5C0 | 0x0042C820 | `CharGenDice.RollDice` |
| `Random::rand` | 0x0042C4C0 | 0x0042C720 | (not reproduced; `ICharGenRandom.NextUnit`) |
| `ACCharGenData::FormatName` | 0x005BD5D0 | 0x005BE6A0 | `CharGenNames.Format` |
| `ACCharGenResult::VerifyCharacterGenerationResult` | 0x005C79F0, 0x005C80E0 | 0x005C89D0, 0x005C90C0 | `CharGenVerifier.Verify` |
| `ACCharGenResult::CG_Pack` | 0x005C7200 | 0x005C81E0 | `CharGenResult` field order |
| `gmCharGenMainUI::DoRandom` | 0x004E7D70 | 0x004E8A00 | the page flow (§10) |
| `gmCharGenMainUI::Create` | 0x004E83E0 | 0x004E9070 | the page flow (§10) |
| `gmCGHeritagePage::ListenToElementMessage` | 0x00483860 | 0x00483C20 | the page flow (§10) |
| `ClientCharGenState::UpdateTrueFacePal` | 0x00563A20 | 0x005647C0 | (left to CharGen's view) |
