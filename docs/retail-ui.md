# Retail UI (M2, retail UI track)

Lifestone draws the retail user interface the way the retail client did: the layouts of `client_local_English.dat`
become element trees, the elements paint into software surfaces with retail's blitter, and the host shows the
surfaces' dirty tiles. Everything here is engine-agnostic C# with no I/O beyond `DatSet`; the Godot side (G2) only
uploads tiles and forwards input.

| Layer | Where | Steps | Doc |
|---|---|---|---|
| Desc model, inheritance, anchoring, master properties, fonts, strings, cursors, CharGen data | `src/Lifestone.Data/Ui/`, `src/Lifestone.Data/CharGen/` | R1-R9, R18a (UI-a) | [retail-ui-data.md](retail-ui-data.md) |
| Element runtime, compositor, text | `src/Lifestone.Client/RetailUi/{Core,Rendering,Text}/` | R10-R12 (UI-b, checkpoint C-ui) | this document |
| Widgets, input, dialogs, flow, architecture tests | `src/Lifestone.Client/RetailUi/{Widgets,Input,Dialogs,Flow}/` | R13-R16 (UI-b) | this document (§8-§12) |
| The Godot layer: tile presentation, input feed, cursor, viewport host, worker-decoded images, options, engine checks | `src/Lifestone.Game/scripts/RetailUi/`, `src/Lifestone.Client/RetailUi/RetailUiOptions.cs` | W2-pre(b), G2 U2 | this document (§17) |

The plan of record is [m2-plan.md](milestones/m2-plan.md) (§3.3 "retail UI (core)", contract §4.11); the
specification is [m2-log-in.md](milestones/m2-log-in.md) §5. Nothing here is ported: every algorithm is re-expressed
from the retail decompile and cited by function name with its 11.4186 address and, where known, its 11.6096 address
(§16); the sources are in [third-party/retail-ui.md](third-party/retail-ui.md) and, for character creation, [third-party/retail-ui-chargen.md](third-party/retail-ui-chargen.md). Tests are in `tests/Lifestone.Client.Tests/RetailUi/`; those that need the user's dats skip without them.

## 1. Seams

The runtime reaches everything outside it through narrow interfaces, so it runs the same under tests (fakes) and in
the game (the host's adapters).

| Seam | What it gives the runtime | Implemented now by | To be implemented by |
|---|---|---|---|
| `IUiDescSource` | UILAYOUT enums, layouts, `InqFullDesc` | `UiDescSource.From(UiLayouts)` | (done) |
| `IUiImageSource` | UI images as RGBA8 (`Ready`, `Pending`, `Missing`) | `DatUiImageSource` (synchronous); in the game `GodotUiImageSource` (worker decode over it, §17) | (done) |
| `IUiFontSource`, `IUiFont` | fonts by DID and FONT enum, glyph descs (R5's lookup: glyph 0 inside a font's range, '?' outside it), atlases | `UiFontsSource` over R5's `UiFonts` (one `UiFontAdapter` per font) | (done) |
| `IUiStringSource` | table strings for `UiStringRef`, with variables (`UiStringVariables`) and the language's number format | `UiStringsSource` over R6's `UiStrings` | (done) |
| `IUiSoundSink` | sounds of media entries | `NullUiSoundSink`; in the game `UiSoundAdapter` over the portal `ISoundSink` (the audio service's `RetailSoundPlayer`) | (done) |
| `IUiClipboard` | copy, cut, paste (D39) | test fake; in the game `DisplayServerClipboard` | (done) |
| `IUiRandom` | media dice | `UiSeededRandom` | (done) |
| `ILogSink` | loud TODOs and errors (category `retailui`) | the client's log sinks | (done) |
| `IUiDirtySink` | dirty rectangles of a surface | `UiCompositor` | (done) |
| `IUiTileSink` | changed 128-pixel tiles, root placement | test sinks; in the game `RetailUiTiles` (§17) | (done) |
| `IUiViewportHost` | where each viewport's hole is, and whether it shows | test recorder; in the game `RetailViewportHost` (§17) | the sources: G1 (tunnel), G3 (CharGen), G4 (SmartBox) register through `RetailUiLayer.RegisterViewportSource` |
| `IWorldPointerSink` | presses and releases the UI does not take (and their releases on a focus loss), mouse look, whether a text field has the keyboard | test recorder; in the game `WorldPointerRelay` (§17) | G5's player controller sets its `Target` |
| `IInputActionSink` | the actions of an input map a component registers | elements, `GmUiFlow`, test recorders | G5 (movement map 4), the screens |
| `IUiScreen` | a UI mode's screen (made, shown its error, updated, shown, hidden, disposed) | test recorder | G2-G4's screens |
| `IUiNoticeSink` | text notices by type (`DisplayStringInfo`) | `UiNoticeRouter`, `GameSpewBox`, `GameChatWindow` (§21.2) | (done) |
| `IEventBus` (state contract) | the session's character errors for `GmUiFlow.Attach` | the session | (done) |

`UiServices` carries the seams into `UiElementManager`, and `RetailGlitches` (`UiRetailGlitches`), the retail glitches
the host restores (none by default; §14). The runtime holds no static state, takes time from its caller
(`Update(now)`), and runs on one thread. Besides the input itself, the host reports the window's focus
(`UiInputRouter.SetClientActive`, §10).

**Coordinates.** Every place the host passes in or gets back is in logical window pixels: window pixels divided by the
UI scale, with the forced display's `DisplayOrigin` included. That covers the mouse (`UiInputRouter.MouseMove`,
`MousePosition`, `UiElementMessage.WindowPoint`, `IWorldPointerSink`), `UiElement.ScreenBox`, `UiRootFrame.ScreenBox`
and `IUiViewportHost.Bind`. With the centred 800×600 display of the login screens and CharGen in a 1920×1080 window,
the display starts at (560, 240) in them (`UiLifecycleTests`).

## 2. Element runtime (R10)

### 2.1 Elements and the manager

`UiElement` is retail's `UIRegion` (a box, z level, visibility, image, children) and `UIElement` (desc, states,
properties, media machine) in one class; every class derives from it. `UiElementManager` is `UIElementManager` with the
root bookkeeping of `UIMainFramework`:

- **Creating.** `CreateRootElement(layoutEnum, elementId)` resolves a UILAYOUT enum and creates a mode root
  (`UIMainFramework::CreateAndAddRootElement`). Each element is created as
  `UIElementManager::CreateElementRecursiveFromPartialDesc` does: resolve the desc, create the class, create and attach
  each child of the full desc the same way, then initialize. A desc that does not resolve, an enum that does not
  resolve, or a missing layout or element creates nothing and logs an error, as retail creates nothing.
- **The root element** (`UIElementManager::CreateHollowElement`, element id 8) is a Field the size of the display with
  all four edges following it; mode roots are its children.
- **Surfaces.** An element with a non-zero object mode (property `ElementObjectMode`; no value counts as mode 3) owns a
  surface; the mode roots do. Elements without one draw into their nearest ancestor's.
- **Ticking.** Only what waits on time registers for ticks (a blocked media machine, a blinking caret); `Update(now)`
  ticks them and allocates nothing.
- **Messages, activation and focus** are §8; the input router (`Input`), the cursor (`Cursor`) and the dialogs
  (`Dialogs`) hang off the manager. Deleting an element (`DeleteElement`) runs each element's `OnDeleting` first, as
  retail's destructors do (a menu deletes its popup root, `UIElement_Menu::~UIElement_Menu`; a viewport unbinds), then
  forgets it everywhere: its ticks, listeners, input maps, the mouse state, the cursor (which falls back to the next
  one), the focus and the active window. A deleted element says so (`IsDeleted`); deleting it again does nothing.

### 2.2 Classes

`UiClassRegistry.RetailClassNames` pins the 105 classes retail registers (`UIElementManager::Init`,
`UIElement::RegisterElementClass`, `gmUIHelper::RegisterElements`); the retail layouts use 100 of them
(`UiClassRegistryDatTests`). `UiClassRegistry.CreateDefault()` implements all 21 engine classes (§9, §11): the widgets,
the Browser's loud stub and the seven dialogs. The 84 game classes (0x10000001 and up) are the screen tracks' to
register on the same registry; until then each gets a plain element as a stand-in (`IsStandIn`), drawn with its retail
art, with one loud TODO per class. A class retail does not register is not created and logs an error.

## 3. Order: children, z levels and hit tests

Read from `UIRegion::AddChild`, `UIRegion::DrawChildren`, `MouseHitTester<MouseOverCriteria>::Recurse` and
`UIElementManager::DrawDirtyRegions` before any code was written:

- **Child order.** A child list is kept in descending z level, then ascending read order (`UIRegion::AddChild` with
  `UIElement::CompareZLevel`). `DrawChildren` paints the list from first to last, so **a lower z level is in front**.
- **Hit tests** start at the manager's root. A hidden element hides its subtree; otherwise its children are tried
  from last to first (front to back), each only where the point is over its box and hit-test mask
  (`SurfaceWindow::PointTestOverlay`), and the first that answers wins; when none does, the element answers itself if
  it is mouse-visible or blocks clicks. An element that is not mouse-visible lets the point through.
- **Bring to front** (`UIRegion::BringToFront`) moves an element to the front of its z group only.
- **Initialization** (`UIElement::Initialize`): the media machine starts on the desc's media, the element enters its
  default state, then every property of the desc is applied. Where an element's desc and its default state both set a
  property with different values, the desc's value is what retail shows; the data has 27 such shared properties and 3
  that differ (§13).

## 4. States and properties

- **`SetState`** (`UIElement::SetState`) compares the old effective properties (desc + old state + instance) with the
  new (desc + new state + instance): a dropped property is reset, a changed or new one applied. The state's media
  restart the media machine only when the new state has media. A state with pass-to-children is set on the children
  first. An unknown state id means state 0.
- **`SetProperty` and `InqProperty`** keep per-instance values above the desc and the state, as retail does.
- **Resets.** Retail resets a property by calling its handler with no value, and each handler then reads whatever its
  local held: a fixed value for some, an uninitialised stack slot for others. Lifestone resets to the class's
  constructor value:

| Property | Reset to |
|---|---|
| `ElementHide` | shown |
| `ElementBlocksClicks`, `ElementTooltipsOn`, `ElementDragable`, `ElementActivatable`, `ElementActivateOnShow`, `ElementContextMenu`, `ElementShouldEraseBackground` | false |
| `ElementAlphaBlendMod` | 1 (opaque) |
| `ElementClampGameViewEdgeToObject` | 0 |
| `ElementObjectMode` | mode 3 (owns a surface), as retail treats a missing value |
| `ElementDrawAfterChildren` | set, whatever the value (retail's handler; every retail value is true, §13) |
| `ElementImageTileOffset`, `ElementInputMapId`, `ElementVisibilityToggleInputAction` | unchanged (retail ignores a reset) |
| Text properties | the `UIElement_Text` constructor's values: horizontal justification 2, vertical 4, no margins, a black outline, at most 0xFFFF characters, flags off; no text for a cleared entry |

## 5. Media, images and the compositor (R11)

### 5.1 Media machine

`UiMediaMachine` re-expresses `MediaMachine::Reset`, `MediaMachine::Update` and the `Update_*` handlers. Images, alpha
masks, cursors, sounds and jumps act at once and move on; a pause, an animation or a fade holds the machine until it
ends; a message waits until the element is initialized; a state change rolls its dice every frame until it happens. An
animation's frame is `(int)((n − 1) × t + 0.5)` with `t` the clamped fraction of its duration; a fade sets the opacity
of the surface its element draws into. Movies (two entries in the data) are skipped with a loud TODO.

### 5.2 Images

`DatUiImageSource` decodes each RenderSurface once through `TextureLoader` (high-res first) and answers again without
allocating, since the compositor and the hit test ask on every repaint. The layouts reference 624
image DIDs: 325 A8R8G8B8, 292 R8G8B8, 6 JPEG and DID 0, which clears the image; the 30 animation frames are R8G8B8 (§13).
Retail's `SurfaceWindow::LegacyBlit` path for 16-bit, paletted and DXT images is therefore not re-expressed; such an
image is blitted as the nearest supported format with a loud TODO.

The six JPEG images (the login screens' backgrounds among them) go through `UiJpegDecoder`, Lifestone's own baseline
decoder written from ITU-T T.81 (sequential Huffman, 8-bit, one or three components, sampling up to 2×2, restart
intervals, the Adobe transform flag, an integer inverse DCT, JFIF YCbCr conversion in fixed point). It refuses
progressive, lossless, arithmetic and multi-scan files with a loud TODO; none of the retail UI images is one. Retail
decoded these files with its own decoder, so a pixel can differ by the rounding of the inverse DCT; a host keeps
`DatUiImageSource`'s default decoder so what it shows is what the tests check.

An image that does not load is drawn as retail's default graphic (`Graphic::SetToDefaultGraphic`): 32×32, filled
(76, 76, 102, 255), with the right three columns and bottom three rows in a colour retail keeps in a global the
decompile does not show; Lifestone uses opaque magenta, a visible placeholder.

### 5.3 Blitting

`SoftwareUiPainter` is the only `IUiPainter`; `UiBlitter` is retail's software blitter (`SoftwareBlitAndColor` and its
loops) as a machine with SSE runs it:

- **Opacity modifier** `m = (int)(opacity × 255.0)`; 0 draws nothing; a source alpha `a` counts as `(m + 1) × a / 256`
  unless `m` is 255.
- **Blit mode 0, SrcCopy**: the source replaces the destination.
- **Blit mode 1, ThreeChannelAlpha**: `d − (d·k >> 8) + (s·k >> 8)` per colour channel with `k` the source alpha plus
  one; the destination keeps its alpha.
- **Blit mode 2, FourChannelAlpha** (the SSE variants): a copy over a transparent destination or at `k` = 256, the
  three-channel formula over an opaque one, otherwise `A' = k − k·(da + 1)/256 + da + 1`, colour
  `d − (d − s)·k / A'` in single precision rounded to nearest even, alpha `A' − 1`.
- **Colouring** through an A8 mask (text): the colour with the mask as alpha, the colour's own alpha as the modifier.

The mode comes from the image media's draw mode as `UIRegion::SetImageByDID` maps it (`UiBlitModes.FromDrawMode`).
**Tiling** is `Graphic::Draw`: an image is never stretched; it repeats from the region's top-left corner shifted by the
tiling offset, and each dirty piece draws only the tiles it touches.

### 5.4 Compositing

`UiCompositor` keeps one RGBA8 surface per element that owns one (retail's `UIObject`):

- **Dirty rectangles** arrive from the elements (`UIRegion::AddDirtyRect`, clipped up the parent chain) and are kept
  disjoint per surface as `UIObject::AddDirtyRect` keeps them, cutting overlaps with `Box2D::Subtract` (left, right,
  top, bottom pieces).
- **Painting** re-expresses `UIElementManager::DrawRegionWithObject`, `UIRegion::DrawHere` and
  `UIRegion::DrawChildren`: per visible region, its dirty pieces and their bounding window; erase when asked (0x51);
  children first when it draws after them (0x53); itself; then the children without a surface of their own.
- **Time slicing.** `Composite(deadline)` paints whole surfaces round-robin until the deadline (at least one per call),
  so a surface is never presented half-painted; a surface whose images are still pending waits. A paint that meets a
  pending image the pre-check could not see (one a `Draw` override resolves, or one that went pending since) sets
  `SoftwareUiPainter.HadPendingImage`; its area stays dirty, none of it is handed over, and a later call paints it again.
- **Tiles.** `CopyDirtyTiles(sink)` hands the host each finished surface's placement (`UiRootFrame`: order, screen box,
  visibility, alpha) and the 128-pixel tiles that changed (`UiTileGrid`), then any removed surface.
- `Attach(manager)` follows a manager's surfaces (created, deleted, reordered). Steady-state compositing, an idle UI
  and a blinking caret allocate nothing (`UiAllocationTests`).

### 5.5 Frames for review

`RetailFramesTests` composites the data-check, disconnected and character-management screens at 800×600 and
1920×1080, with the production fonts and strings (`UiFontsSource`, `UiStringsSource`), and checks they are complete
and deterministic. With `LIFESTONE_UI_FRAMES` set (a folder, or `1` for `cache/ui-frames` in the main checkout) it
writes them as PNG files for review; they are derived from the dats and are never committed. They are stand-in frames
until the screens land (no screen logic fills or ghosts anything yet).

## 6. Layout, display and scale

- **Anchoring** (`UiRuntimeLayout.UpdateForParentSizeChange`, `UIElement::UpdateForParentSizeChange`) re-anchors
  children from their desc boxes with `UiAnchor` whenever a parent's size changes; a root anchors from its layout's
  design size to the display.
- **Scale** (`UiScalePolicy.Choose`): the largest integer `s` with at least 800×600 UI pixels left,
  `s = max(1, min(W / 800, H / 600))`, drawn nearest-neighbour by the host; an explicit override (the host's
  `--ui-scale`) is honoured as given, and `--ui-scale=1` restores retail's pixel-for-pixel UI. A window below 800×600
  gets scale 1 with a placeholder warning.
- **Forced display** (`ForceDisplayResolution`, `Device::ForceDisplayResolution`): retail switched the screen to
  800×600 for the login screens. Lifestone keeps the window and centres an 800×600 display in it (`DisplayOrigin`), so
  those screens stay pixel-exact at any window size.

## 7. Text (R12)

- **Fonts** (`IUiFont`, `UiFontsSource` over R5's `UiFonts`): metrics, glyph descs and the foreground and background
  (outline) atlases, each decoded once as an A8 image. The lookup is R5's ([retail-ui-data.md](retail-ui-data.md) §5):
  `Font::GenerateCharacterMap` maps the font's whole character range, so `Font::ContainsChar` is the range test and
  `Font::GetCharDesc` gives glyph 0 (a space in most fonts) for a character inside the range the font lacks and '?'
  only outside it. `Glyph::SetFont` keeps the font for any character in its range and takes the FONT enum 22, else 20,
  font (both Gulim 18, 0x40000018, from U+0000) for one outside it: in the default font 0x40000002 (U+0020-U+FFFC) a
  control character draws in Gulim and '一' as a space (`RealFontTests`). Advances are `Font::GetCharWidthA`'s byte
  sum. The horizontal offsets are signed (§13).
- **Line breaking** (`GlyphLayout`, `GlyphList::Recalculate` with `LinebreakUtils`): breaks after spaces and at
  East Asian characters subject to the no-begin and no-end classes, hard breaks at new lines, width limits, the
  maximum length (trimming from the top when asked), and the caret and selection queries of `GlyphList::Find*`.
- **Rasterizing** (`TextRasterizer`, `SurfaceWindow::DrawCharacter` and `CreateCharRectPair`): an A8 atlas tinted with
  the text colour; the outline from the background atlas in the outline colour, or from eight one-pixel offsets when
  the font has none; selection inverts the colour bits; the caret is a one-pixel line in the font colour, blinking
  every 0.53 s (Windows' default caret time; a host may pass the user's).
- **`UiTextElement`** (`UIElement_Text`): justification, margins, colour and font runs, fit-to-text, truncation, and
  editing: caret moves by character, word, line and text, selection with Shift, delete forward and back, select all,
  copy, cut and paste through `IUiClipboard`, the Name and Number input filters, and the maximum length (D39, no IME).
  Text tags are not parsed yet (loud TODO); a font change re-sets the text as `UIElement_Text::DoFontReset` does.

## 8. Element messages, activation and focus

Read from `UIElement::BroadcastElementMessage`, `ListenToElementMessage`, `ForwardElementMessage`,
`UIElementManager::BroadcastElementMessage` and `UIElement::DefElementMessageHandler`:

- **Delivery.** A message about an element (`UiElementMessage`: the element, the id, two parameters, and for mouse
  messages the window and element points) goes first to the element itself (`ListenToElementMessage`), then to its
  listeners, each once per message, then up to its parent, and at the top to the manager's listeners for that element
  id and message (`RegisterForElementMessage`). A listener that answers `Consumed` stops it. A message sent while one
  is being delivered waits in a queue, as retail's broadcast does. The "heard" marks of listeners that are not
  elements last one delivery, so nothing keeps a deleted listener alive. `UiElementManager.ElementMessage` taps every
  message for hosts and tests.
- **Global messages** go to their listeners in registration order; a listener may broadcast a global message itself
  (a frame listener's callback starting an action), and the outer broadcast still reaches every one of its listeners.
- **Ids** are `UiMessageIds` (button click 1, hot click 2, list selection 4, menu selection 7, scroll requests
  0x0D-0x10, visibility 0x18, click 0x19, double click 0x1A, mouse 0x1B-0x20, activation 0x29 and 0x2A, focus 0x2F,
  keys 0x41 and 0x42, and the rest); the global messages are action start 1, action end 2 and frame 3
  (`UiGlobalMessageIds`).
- **Mouse visibility.** Registering for a mouse message (0x19, 0x1C, 0x1D, 0x40) makes an element mouse-visible, as
  retail; registering for double clicks (0x1A) makes it want them. Retail keeps that in element flag bit 23, whose
  setter is not in the decompile read (`SetWantsDoubleClicks`, a named TODO).
- **Activation** (`UIElement::Activate`, `Deactivate`, `UIElementManager::ActivationAlert`, `ActivateNext`): a press
  activates the window root under it; the active window registers its input maps at 0 and 2000, sends 0x29 and gives
  the focus to its pending focus element; a window that hides passes the activation on. Activate-on-show (0x34) and
  focus-on-show (0x35) act when an element shows.
- **Focus** (`UIElementManager::SetFocusElement`): the old element hears 0x2F with 0 and drops its maps; the new one
  hears 0x2F with 1 and registers its maps at 3000. `TakeFocus` only marks the pending focus while the window is
  inactive.
- **Visibility-toggle actions** show, hide or toggle an element when their action starts unclaimed.

## 9. Widgets (R13)

| Class | Id | Lifestone | What it does |
|---|---|---|---|
| Button | 1 | `UiButton` (a Text) | states from its properties and the mouse (Normal 1, rollover 2, pressed 3, Highlight 6-8, Ghosted 13); click 1 on a release over it; toggle (0x0B); hot repeat (0x0F, 0x10, 0x11); its input action (0x12) fired on click |
| Dragbar | 2 | `UiDragbar` | moves its parent window with the mouse, Shift snapping to 10, kept inside the parent |
| Field | 3 | `UiField` | the plain container; drag-rollover states for dropped items (drag and drop is not built in M2) |
| ListBox | 5 | `UiListBox` (a Scrollable) | rows from templates (0x64) in a grid on the paper, click and drag selection, item states |
| Menu | 6 | `UiMenu` (a Button) | a popup root with a list box; choosing a row closes it and shows the row in the selection display |
| Meter | 7 | `UiMeter` | a level from 0 to 1 as a frame of an array or a clipped or moved fill child; smooth movement to a goal |
| Panel | 8 | `UiPanel` | tabbed pages: the open page shows and its tab is Open (12), the others Closed (11) |
| Resizebar | 9 | `UiResizeBar` | resizes its parent from the border its properties name |
| Scrollbar | 0x0B | `UiScrollbar` (a Button) | widget, buttons and page areas; line, page and position requests; drag and wheel |
| Text | 0x0C | `UiTextElement` (a Scrollable) | §7; scrolls by lines and pages through its scroll bar |
| Viewport | 0x0D | `UiViewport` | reports its hole to `IUiViewportHost`; presses over it go to the world |
| Browser | 0x0E | `UiBrowser` | a loud stub: its frame only (Lifestone opens no web pages) |
| ColorPicker | 0x10 | `UiColorPicker` | picks the colour under the mouse from its image and outlines the choice |
| GroupBox | 0x11 | `UiGroupBox` | radio buttons: the selected one is Highlight |

Details that matter on screen:

- **Scrollable** (`UIElement_Scrollable`, no class id of its own) keeps the scroll offset and the paper's size, sizes
  its scroll bars (the widget is the visible share; a bar is disabled when the paper fits) and turns their requests and
  the wheel into scrolling.
- **Buttons are mouse-visible unless ghosted**, the rule of `UIElement_ListBox::GetShouldBeMouseVisible`, which list
  boxes and colour pickers share (a named TODO for the button's own slot). Drag and resize bars are mouse-visible.
- **Ghosting at creation.** `UIElement::Initialize` enters the default state through the button's own `SetState`,
  which clears the ghosting for any state but 13 before the desc's properties apply. The 23 buttons the layouts ghost
  all default to Normal, so retail creates them unghosted and the screens ghost what they need (§13).
- **The scroll bar's track.** The increment button sits at the start, the decrement button at the end; the widget
  moves over the track less its own size, at `start + (int)(active × position)`; a proportional widget is
  `(int)(track × share + 0.5)` pixels, at least property 0x89.
- **List boxes** select twice on a press on the list itself (its own MouseDown message, then its MouseDown; the second
  is a reselect, 0x43), as retail.
- **Menus** open their popup at the menu's last row (retail's inclusive bottom): one pixel of overlap. The popup is a
  root of its own, so the menu deletes it when it is deleted (`UIElement_Menu::~UIElement_Menu`); a menu dialog or a
  screen leaves no popup surface behind. A menu counts as closed before its popup hides, so the popup's deactivation
  does not close it twice.
- **Meters** clip their fill child to the level (`ClipChild`, from `UIElement_Meter::DrawChildren`), which the
  compositor applies to that child alone.

## 10. Input (R14)

The host feeds `UiInputRouter` (`manager.Input`) keys as DIK scan codes (`GodotKeyCodes.ToDik` maps Godot physical
key codes, as plain ints), typed characters, and the mouse in logical window pixels; each call answers whether the UI
took the input.

- **Bindings** (`RetailKeyBindings.Load`): the engine key map (KEYMAP enum 1, 0x14000002: the UI's mouse map 3, the
  text maps 7 and 8, the dialog keys of map 9, the wheel map 0xA) merged with the game map (0x14000000: movement map 4,
  chat, panels), and the action map's toggle types. Right Shift counts as left Shift.
- **Maps and priorities** (`CInputManager::RegisterInputMap`; higher first, a new map before older ones of the same
  priority):

| Who | Maps | Priority |
|---|---|---|
| The element manager | 13 | 4000 |
| The UI flow (`GmUiFlow`) | 14 | 4000 |
| The focused element: the map its property 0x4E names (its ancestors' one lower each); an editable text also 7, 8 and the keyboard stop 1 ten below; a scrollable also 0xA | its own | 3000 |
| The active window | its own | 2000 (and 0) |
| The player controller (G5) | 4, 0x10000006-0x10000008 | 1000 |
| The element manager's mouse | 3 | 0 |

- **Precedence** (`CInputManager_WIN32::FireInputEvent`, `CInputManager::SendActionToListeners`): a key is looked up in
  every map from the highest priority down until the keyboard stop map 1 (for a key) or the stop map 2 (for anything);
  the best binding wins (the higher activation, then more meta keys); its action goes to the map's registrant, then,
  when that declines, to the handlers: the element manager (the focused element, else the active window, hears 0x41;
  unclaimed, global message 1 and the visibility-toggle actions), then the host's. So a modal dialog eats the clicks
  behind it, the capture keeps the mouse, a focused text field keeps the keyboard (movement stops until it loses the
  focus), a dialog's map 9 takes Return and Escape before the game's maps, and a key nothing binds stays the host's.
- **Toggle types** (`CInputManager::FireActionEvent`): momentary actions end on release, toggles flip on each press,
  impulses fire once, auto-repeat actions repeat after 0.5 s every 1/30 s (Windows' defaults; the host may pass the
  user's). Moving forward or backward ends autorun (`TurnOffRunLock`); stop is an impulse in the retail maps, so it
  does not.
- **Mouse.** Map 3 turns the buttons into mouse actions (7-9, double clicks 0xA-0xC, the wheel 5 and 6); the element
  under the mouse (`SwitchMouseOver`) takes the capture on a press and keeps it until the last button that made it is
  up. A press on no element or on a viewport goes to `IWorldPointerSink`, and so does its release, after the maps end
  the button's action. Tooltips open after the mouse rests 0.25 s (property 0x50 overrides), 32 pixels below and right
  of it, and close after 10 s or when the mouse leaves.
- **Text mode** (`CInputManager::SetTextMode`): a focused editable text receives the typed characters; the character
  of the key that focused it is swallowed; `IWorldPointerSink.TextEntryActive` follows.
- **Focus** (`CInputManager_WIN32::Activate`, `ReleasePressedKeys`): neither Windows nor Godot sends the releases a
  window misses while it has no focus, so the host calls `SetClientActive(false)` on Godot's
  `NOTIFICATION_APPLICATION_FOCUS_OUT` and `SetClientActive(true)` on `NOTIFICATION_APPLICATION_FOCUS_IN`. A loss lets go
  of every pressed control: its meta keys leave the meta mode (Shift is up for the text fields too), the action it
  started ends (momentary, auto-repeating and continuous ones; a toggle such as autorun stays), a button the world took
  is released to the world, and a button held on an element is let go as if the mouse had left the window, so it does
  not click. The next press of each control starts afresh. Then `ClientActiveChanged` reports the change
  (`CInputManager::CallFocusSwitchHandler`); G5's player controller clears its keyboard movement on false, as retail's
  release tells the command interpreter (most likely `CommandInterpreter::LoseKeyboardFocus`). Mouse look sends nothing
  while the window has no focus.
- **The cursor** (`UiCursorController`, `UIElementManager::CheckCursor`): the capture's cursor, else the entered
  element's, else the default pointer; the host shows `CursorDid` whenever `Version` changes. When the element whose
  cursor shows is deleted (an OK button under the mouse), the cursor is checked again at once.

## 11. Dialogs, flow and notices (R15)

**Dialogs** (`DialogFactory`, `manager.Dialogs`) are roots of the dialog layout (UILAYOUT 2, 0x2100003C):

| Type | Element | Class | Buttons | Response |
|---|---|---|---|---|
| 1 Confirmation | 0x15 | `UiConfirmationDialog` (0x13) | yes 0x17, no 0x19 | 0x92, a bool |
| 2 Wait | 0x31 | `UiWaitDialog` (0x19) | none; it closes when its key action (0x9E) starts unclaimed | none |
| 3 Message | 0x24 | `UiMessageDialog` (0x17) | OK 0x26 | none |
| 4 Text input | 0x28 | `UiTextInputDialog` (0x18) | Done 0x2A | 0x98, the text; the end-of-retail layout has no element 0x28, so none opens |
| 5 Confirmation text input | 0x2C | `UiConfirmationTextInputDialog` (0x15) | Done 0x2E, Cancel 0x2F | 0x9C, the text |
| 6 Menu | 0x1B | `UiMenuDialog` (0x16) | menu 0x1D, OK 0x1E | 0xA4, the index |
| 7 Confirmation menu | 0x1F | `UiConfirmationMenuDialog` (0x14) | menu 0x21, OK 0x22, Cancel 0x23 | 0xAB, the index |

A dialog shows its text (0xC5) in element 0x3E of its popup 0x3D, which grows to fit and is centred; a modal one (0xAC)
blocks the clicks behind it; a duration (0xC6) counts down in the text's variable `time` and cancels the dialog when it
runs out. Queue 1 shows dialogs at once; every other queue (2 by default) shows one at a time in order, the pending
display 0x33 saying whether more wait; a topmost dialog (0x8D) displaces the open one. `MakeCallbackDialog` hears the
response; `DialogOpened` and `DialogClosed` report every dialog. A close callback may open the next dialog (a
"Deleting..." after a confirmation): in the same queue it becomes the queue's dialog and the waiting ones wait behind
it; a dialog a callback opens during `Reset` survives the reset. A dialog that is not created (an unknown type, text
input on the end-of-retail layout, an element that does not resolve) opens nothing, loudly, and holds no queue, as
retail tests the queue's dialog pointer (`MakeDialog`, `CreateDialog_`, `IsDialogOpen`): it never shows, waits,
closes or answers, and in its turn the dialog waiting behind it opens. The countdown renders the text again only when
the whole seconds change (retail renders it every frame; nothing on screen differs). `RetailQueueing` restores retail's
queue glitches (§14). `NotInM2.Show` answers a feature M2 does not build with a modal message dialog whose Lifestone
literal says so, logged as a placeholder. `NotInM3.Show` is its sibling for M3's unbuilt features (the panel pages with no controller and the combat and examine buttons, §23.1); M2's features (the chat focus, the options page's unbuilt buttons, the login screens) keep "Not in Lifestone M2".

**Flow.** `UiFlow` is retail's `UIFlow`: modes registered with a screen factory, and a queued mode (with its error)
starting at the next frame (`UseNewMode`): the old screen is hidden and disposed, every dialog closes (each callback
hears its data and `DialogClosed` reports it: retail's mode screens are `UIMainFramework`s, whose destructor calls
`DialogFactory::Reset`), and the new screen is made, shown its error when that is a valid string, and shown. A dialog a
close callback opens during that reset shows over the new screen. The first mode, with no old screen, closes nothing;
disposing the flow closes the dialogs after its last screen. `GmUiFlow` is `gmUIFlow::gmUIFlow`: the eight modes in retail's order (data patch
0x10000003, intro 0x10000001, character management 0x1000000A, game 0x10000008, epilogue 0x10000009, disconnected
0x10000002, CharGen 0x1000000B, credits 0x10000005), the data patch queued first, the default pointer (UICURSOR enum 1
at hot spot (0, 0)) and input map 14 at 4000. Two notices change the mode by themselves:

| Character error (F659) | Mode | String (`ID_CHAR_ERROR_...` in table 0x23000002) |
|---|---|---|
| 1, 3, 4, 8, 9, 10, 14, 21, 24 | disconnected | `LOGON`, `ACCOUNT_LOGON`, `SERVER_CRASH` (4 and 8), `ACCOUNT_INVALID`, `ACCOUNT_DOESNT_EXIST`, `ENTER_GAME_PLAYER_ACCOUNT_MISSING`, `LOGON_SERVER_FULL`, `SUBSCRIPTION_EXPIRED` |
| 5, 6, 11-13, 15-20, 23 | character management | `LOGOFF`, `DELETE`, then `ENTER_GAME_` with `GENERIC`, `STRESS_ACCOUNT`, `CHARACTER_IN_WORLD`, `CHARACTER_NOT_OWNED`, `CHARACTER_IN_WORLD_SERVER`, `OLD_CHARACTER`, `CORRUPT_CHARACTER`, `START_SERVER_DOWN`, `COULDNT_PLACE_CHARACTER`, `CHARACTER_LOCKED` |
| any other | unchanged | none |

A lost connection (`RecvNoticeServerDied`) queues the disconnected screen with `ID_NetErr_ConnectionLost` unless that
screen shows or is queued. `GmUiFlow.Attach(IEventBus)` hears the session's `CharacterErrorReceived`; the host reports a
lost connection, because the contract reports it in network terms.

**Notices.** `UiNoticeRouter` is the `IUiNoticeSink` the portal presenter (W4) sends to: text type 26 goes to the spew
box (`gmSpewBoxUI::RecvNotice_DisplayFinalStringInfo` keeps that type only), every other type to the chat windows,
each a sink G4 registers. Before they exist a notice is logged as a placeholder and raised as `Unheard`, for the host's
labelled placeholder line (D32).

## 12. Architecture (R16)

`RetailUiArchitectureTests` checks every source under `src/Lifestone.Client/RetailUi/` (comments and literals
blanked) and the built assembly's member uses from `Lifestone.Client.RetailUi.*`: no Godot; no file, process or stream
I/O (`System.IO` only for `InvalidDataException`); no `Lifestone.Net` and no `System.Net`; of the session layer only
the state contract (`Lifestone.Client.Session.Contracts`: `IGameSessionView`, `IActionApi` and the events and records
they expose), never the session itself, its bootstrap, options or host, nor anything named for passwords, secrets or
credentials.

## 13. Census of the retail data

Where the runtime relies on the data as it is rather than on everything the format allows, `UiRuntimeCensusTests`
pins it (with `UiClassRegistryDatTests`, `UiRuntimeLayoutDatTests`, `WidgetCensusTests`, `RealKeyMapTests`,
`RetailDialogTests` and `UiFlowDatTests`):

| Fact | Used by |
|---|---|
| Every `ElementDrawAfterChildren` value (8) and every `ElementShouldEraseBackground` value (28) is true | the reset table (§4) |
| 624 image DIDs: 325 A8R8G8B8, 292 R8G8B8, 6 JPEG, DID 0; 30 animation frames, all R8G8B8 | no LegacyBlit (§5.2) |
| The six JPEG images, and that they decode at their sizes | `UiJpegDecoder` |
| 49 fonts, 37 with an outline atlas; every font's horizontal and vertical borders are equal, 0 without an outline atlas | `TextRasterizer` keeps retail's border slip |
| Of 153,749 glyphs, 8,738 have a negative before-offset and 7,048 a negative after-offset; no vertical offset is negative | signed glyph offsets |
| 27 properties set by both an element's desc and its default state, 3 with different values | `Initialize` order (§3) |
| 100 of the 105 registered classes are used | the class registry (§2.2) |
| The engine classes in the layouts: 766 Field, 447 Text, 369 Button, 71 Resizebar, 63 Scrollbar, 35 ListBox, 30 Dragbar, 25 Meter, 24 Panel, 13 Menu, 7 Viewport, 2 GroupBox, one of each of the six dialogs; no Browser and no ColorPicker | the widgets (§9) |
| No element has both scroll bars | `UiScrollable` keeps the other axis (§14) |
| Of 445 buttons (menus and scroll bars included), 23 are ghosted by their desc, all with default state Normal | creation unghosts them, as retail (§9) |
| Every keyboard binding fires on down and up (activation 3); no binding names map 1, 2, 13 or 14; map 3 binds the three buttons to 7-9 (down and up) and 0xA-0xC (double click) | the router (§10), `TestKeyBindings` |
| The dialog layout 0x2100003C holds the elements of types 1-3 and 5-7 and no 0x28 (text input) | `DialogFactory` (§11) |
| STRINGTABLE enum 0x10000002 is table 0x23000002, which holds every string the flow shows but `ID_CHAR_ERROR_ENTER_GAME_OLD_CHARACTER` (code 17) | `GmUiFlow` (§11, §14) |

## 14. Decisions and deviations

- **Resets** go to constructor values, not to whatever a retail handler's local held (§4).
- **Signed glyph offsets.** The 11.4186 decompile reads the offsets as unsigned bytes in `DrawCharacter`, which would
  move thousands of glyphs 255 pixels; Lifestone reads them signed, as DatReaderWriter does.
- **Forced display** is centred in the window instead of changing the screen mode (§6).
- **Integer UI scale** by default; retail's scale 1 is an option (§6).
- **JPEG** images are decoded by Lifestone's own decoder (§5.2).
- **The default graphic's border** is magenta, a placeholder for an unread global (§5.2).
- **Text tags** are not parsed, and **movies** are skipped, each with a loud TODO.
- **Stand-in classes**: the game classes draw their retail art without their behaviour until the screen tracks
  register them (§2.2).
- **Glitch fixes, each with a switch** (the fidelity rule: the fix is the documented default, and the host restores
  retail through `UiServices.RetailGlitches`; the lead registers the command-line flag, §15):
  - **Dialog queues** (`UiRetailGlitches.DialogQueueing`, `DialogFactory.RetailQueueing`). Retail drops a dialog a
    topmost one displaced (when its turn comes nothing opens: `OpenNextDialog` pops an entry that still points at the
    deleted dialog, and `CreateDialog_` makes nothing for it), and closing a dialog that still waits removes it from a
    copy of the queue, so it opens later anyway. Lifestone reopens the displaced dialog from its data in its turn and
    removes a closed waiting dialog for good. When a queue's next dialog is not created, retail leaves the dialogs
    behind it waiting until another dialog opens and closes in that queue; Lifestone opens the next one at once.
  - **Held actions on a focus loss** (`UiRetailGlitches.FocusLossActions`, `UiInputRouter.RetailFocusLoss`). Retail's
    `ReleasePressedKeys` fires each held action's end with input map 0, which the action map does not have (its 25
    input maps with actions start at 3), so `FireActionEvent` takes it as an impulse and nothing ends: a held
    auto-repeating or continuous action keeps repeating, and every held action stays in progress, until its key is
    pressed and released again. Lifestone ends them with the map of their press.
  - **A8 hit-test masks** (`UiRetailGlitches.A8PointTest`, `UiImage.PointTestOverlay`). Retail reads a whole dword at
    the pixel's byte and masks off the format's alpha mask (`PixelFormatDesc::SetFormat`: 0xFF for A8), so the alpha
    of the next three pixels decides; Lifestone's A8 mask never tests true. No retail layout has an A8 mask: its three
    masks (alpha media of layout 0x21000040) are A8R8G8B8, which both tests treat alike.
  - **Character creation's heritage tabs** (`UiRetailGlitches.CharGenHeritageTabs`, `CharGenMainScreen`). Retail's handler shows the profession, skills
    and town tabs for ten heritage buttons and hides them for the two Olthoi ones, and lists no Lugian button, so the tabs stay hidden after an Olthoi
    until the page changes. Lifestone shows them by the character's heritage (§19).
  - **Character creation's summary heritage line** (`UiRetailGlitches.CharGenSummaryHeritage`): retail names only heritages 1 to 4; Lifestone names all (§20).
  - **Character creation's appearance camera** (`UiRetailGlitches.CharGenAppearanceCamera`): retail's `Update` snaps the camera to the face whatever the half and zoom state; Lifestone follows the zoom flag (§20).
  - **Character creation's empty name** (`UiRetailGlitches.CharGenEmptyName`): retail keeps the last non-empty name in the state when the field is emptied, so Finish creates under it; Lifestone clears it (§20).
  - **Character creation's held headgear** (`UiRetailGlitches.CharGenHeldHeadgear`): retail loses (or replaces with a stale one) the hat the face half holds when the page is left; Lifestone gives it back (§20).
- **Re-entrant dialogs.** A dialog a close callback opens in the same queue stays the queue's dialog, and the entry
  `OpenNextDialog` would have popped keeps waiting (retail's `CreateDialog_` refuses the second dialog and drops the
  popped entry).
- **Scrollables** keep the other axis where retail's scroll-bar handling resets it to 0; no retail element has both
  bars (§13), so nothing on screen differs.
- **The wheel** goes through map 0xA (a focused scrollable); one no map takes goes to the element under the mouse as
  mouse action 5 or 6, which scrollables turn into scrolling. Retail's route from the wheel to a scrollable without the
  focus is not in the decompile read, so this route is Lifestone's (a named TODO, logged the first time it runs).
- **Tooltips** are placed in display coordinates (the root element's), so they sit where retail put them inside a
  centred forced display (§6).
- **Viewports** report where their hole is (`IUiViewportHost`) instead of rendering a scene object of their own.
- **A press that went to the world** releases to the world after the input maps end the button's action, so a held
  action never sticks.
- **A focus loss** releases the world's buttons to it, lets go of a button held on an element as if the mouse had left
  the window (so the release cancels the press instead of clicking), drops a mouse capture no button holds any more,
  and pauses mouse look until the focus returns (§10). Retail's input manager knows nothing of the world's buttons, and
  its own release of a held mouse button ends nothing (§14, the glitch above).
- **Dialogs that are not created** are dropped rather than kept as an empty current entry: a callback given for one
  never hears, and closing its context does nothing. Retail keeps the empty entry, which counts in
  `IsDialogOpen(0)` and `IsDialogOpen(1)` and which `Reset` or `CloseDialog` would report with no dialog behind it.
- **The flow** takes the queued mode before the old screen goes, so a mode queued while it goes (by its `Dispose` or a
  dialog's close callback during the reset) stays queued for the next frame. Retail reads `_nextMode` again after
  deleting the old screen, so such a mode would become `_curMode` while the screen made is the one queued first; no
  retail flow queues a mode there.
- **The state contract.** `GmUiFlow` hears character errors through `IEventBus` (reached as `IGameSessionView.Events`)
  besides the R16 rule's `IGameSessionView` and `IActionApi`; the architecture test allows the whole contract namespace.
- **Code 17** (an old character) has no string in the retail table; retail shows its string-table error text there,
  and Lifestone shows what the string source gives (with `UiStringsSource`, nothing, logged). A friendlier fallback
  would be a glitch fix behind an option (`docs/milestones/m1-explore-dereth.md`, "Fidelity rules").
- **Character creation** (§19). Back and Next are never ghosted (retail's neither); the Lugian button shows the optional tabs after an Olthoi unless
  `--retail-ui-glitches` restores retail's glitch; Help is a "Not in Lifestone M2" dialog.
- **The login screens** (§18). The data check goes straight to character management (the intro is skipped, D38); the Credits button
  opens a "Not in Lifestone M2" dialog (D38); the disconnected button reconnects and says "Reconnect" (D43; `--retail-disconnect`
  restores "Quit Game" and the exit); a character pending deletion shows in red with Restore and no countdown (D52).
- **Character management keeps its selection in the session** (`Characters.SelectedGuid`, local `SelectCharacter`) where retail keeps it
  in persistent UI data and in `CharGenState`'s slot; Lifestone's character creation takes the next free slot itself.
- **`RecvNotice_BeginEnterWorld` is `PlayerCreated`** (F746), the first event of entering the world; the contract has no earlier one.
- **The "connected" test of the data check** (retail reads a flag of its network object) is the session's data check having started, and
  its "characters received" flag (`UIFramework::GetPersistantData()+72`) is `Characters.Version > 0`.
- **A session that failed before the host started** is reported as a lost connection on the host's first frame (polled from state).
- **The effects pages' info box** (§23.3; D32). Retail's `gmEffectsUI::RebuildList` (11.6096 0x004B8F70) returns before `UpdateSelection` (0x004B8BB0) when the registry has nothing in effect at all, so when the last effect ends the info box keeps the ended spell's name and description and the selection stays on it. Lifestone clears the selection and the info box when the list empties; `--retail-effects-stale-info` (`UiRetailGlitches.EffectsStaleInfo`, value 256, also in `--retail-ui-glitches` / `UiRetailGlitches.All`) restores retail's. A character with no effects at all sees a blank box either way.
- **The paperdoll's slots view** (§23.5). Retail's `gmPaperDollUI::PostInit` (0x004A56D0) opens on the figure with the nine armour slots hidden. A host that serves no 3D figure (no `CharGenSetup.View3D` / `ViewHost`) opens the page on the slots view instead (`TODO(m3-paperdoll-figure)`); a served figure opens as retail does. A deviation with no flag: there is no retail behaviour to restore without a figure.
- **Inferred and held values** (§23): the ammunition slot's mask 0x800000 is inferred from a string address in the decompile; the list sort ties of the spellbook are broken by spell id; the effects list and the skills list sort ordinally (`strcmp`, `wcscmp`).

## 15. Loud TODOs

Each logs at runtime (category `retailui`) or is named in code as `TODO(retailui-runtime)`:

- A registered class without a runtime yet (the game classes): a plain element stands in (one warning per class).
- `gmUIFlow`'s callback for input map 14 is not in the decompile read: an action there is logged once and passed on
  (the retail maps bind nothing to map 14, §13).
- No default pointer given to `GmUiFlow`: the host's cursor shows (placeholder).
- Drag and drop of UI items (`UIElementManager::StartDragandDrop`) is not built: a dragable element is clicked, drop
  targets never see a dragged item (`UiInputRouter.DragElement` is null).
- Scroll-bar stop locations (`UIElement_Scrollbar::SetScrollbarStop`) are not built: such a bar scrolls freely.
- A scrollable class with no line size scrolls only by pages (`InqScrollDelta`).
- The Browser shows its frame only, and a page it asks for is not opened.
- No viewport host: a 3D view's hole stays empty (placeholder).
- To confirm in the 11.6096 vtables: the slot `UIElement::Initialize` calls for the default state (taken as
  `SetState`), the button's mouse-visibility rule, the drag and resize bars' mouse visibility, the slot `UIElement_Menu::Open`
  calls to show its popup, and the setter of element flag bit 23 (double clicks).
- `UIElement::MouseResizeElement`'s top and right borders (garbled in the 11.4186 decompile) follow the left and bottom
  ones; logged the first time a window is resized from one.
- The wheel's route to the element under the mouse (§14); logged the first time it runs.
- On a focus loss in mouse look, retail's `ReleasePressedKeys` fires a mouse event of its own whose effect is not
  known; Lifestone pauses mouse look until the focus returns, logged the first time it matters. Retail's release also
  calls a virtual of the command interpreter, most likely `CommandInterpreter::LoseKeyboardFocus`; the slot is to
  confirm in the 11.6096 vtable (G5 handles `ClientActiveChanged`).
- The command-line flag for `UiServices.RetailGlitches` (§14) is `--retail-ui-glitches` (§17), which restores every
  glitch at once; a single glitch is restored in code only.
- Retail's topmost displacement as the 11.4186 decompile reads it: `DialogFactory::MakeDialog` re-queues the open
  dialog's entry and calls `CreateDialog_`, whose current entry for the queue may still point at the removed dialog, in
  which case the topmost dialog itself would not open. `RetailQueueing` opens it and drops the displaced one; to confirm
  in 11.6096.
- Retail's A8 hit test (`UiRetailGlitches.A8PointTest`) reads past the last pixel of a row or of the image; what is there
  (row padding, the next allocation) is unknown, so it counts as 0. Its dword read of a 24-bit pixel (the next pixel's
  blue byte) and of an X8R8G8B8 pixel (the X byte) is not reproduced; no retail mask has those formats.
- A notice before the spew box or chat exists is a placeholder line (D32).
- Features M2 does not build answer with a "Not in Lifestone M2" dialog (`NotInM2`), logged as a placeholder.
- Movies in media are skipped.
- Images in 16-bit, paletted or DXT formats are blitted as the nearest supported format (LegacyBlit).
- The default graphic's border colour (a global to read from 11.6096).
- `TODO(retailui-tooltip-wrap)`: CharGen's Next tooltip ("Advance to next screen.") wraps every few letters
  in a narrow box (seen at 2560x1440, UI scale 2, while filming the M2 hero video); the tooltip's width rule is to
  check against the decompile. M3.
- JPEG processes other than baseline and extended sequential with Huffman coding, other sample sizes, component
  counts, sampling factors and multi-scan files.
- `SurfaceWindow::PointTestOverlay` and `NameInputFilter` have no row in the 11.6096 symbol table; their neighbours
  predict 0x00441840 and 0x004664E0, to confirm from the bytes.
- The sign extension of glyph offsets, to confirm in the 11.6096 machine code.
- No font source, a missing font, an unresolved string or no string source: the text shows nothing or keeps its font
  (placeholder).
- Text tags (`TextTagFactory::MakeTag`) are not parsed.
- The constructors of the data check, the disconnected screen, character management and the epilogue are not in the 11.6096 symbol table
  (predicted 0x004EEF40, 0x004EA6A0, 0x004ED580 and 0x004EA790; TODO(retailui-data)).
- `UpdateButtons` sets the Delete button's state through a call whose argument the 11.4186 decompile lost; it is taken as Normal
  (TODO(retailui-runtime): confirm in the 11.6096 bytes). A row of the list gets one more virtual call with 1 and the wish for double
  clicks; the call is taken as `SetMouseVisible(true)`.
- `TODO(retailui-datapatch)`: the data check's queue place, download size and progress events are built but no session raises them
  (Lifestone does not patch).
- `TODO(retailui-reconnect)`: a host that cannot reconnect makes the disconnected button exit, loudly. The live session can (G6 W9: `SessionHost.Reconnect`), so only a run with no session host is left.
- The epilogue's closing movie (layout 0x10000037) is skipped, so the epilogue ends the application at once.
- A window below 800×600 cuts the screens off (placeholder warning).
- Character creation: `TODO(retailui-chargen-pages)`, `-finish`, `-help` and `-data` (§19).
- `TODO(ui-sound-sink)` is closed for every run that has an audio service: the retail UI's sounds reach `RetailSoundPlayer` (UI bus) through `UiSoundAdapter`, in play mode, explore, `--retail-ui-only` and `--retail-ui-frames` (these two install the audio service themselves, `AudioService.Install`); a run with no audio service (the check suites) still drops a sound, counts it and logs the TODO once (M3 1.3 s2a).
- The counter is `RetailUiLayer.SoundStats` (`RetailUiSoundStats`): `Requested`, `Played` (handed to the sound player, whatever the master volume; the login check's `sounds_ui=`), `Dropped` (no player), `Silenced` (played, but the player started no voice) and `Muted` (played while Master is silent, D8).
- M3 2.2 (§23; each logs under category `retailui` or is named in code). Closed: `g4-quality-filter`, `g4-quality-filter-install`, `g4-enchantment-affects-skills`, `m3-stat-header`, `m3-portal-tables-install`, `g4-indicators-gameplay` for the effects, vitae and burden indicators (the mini-game indicator keeps it), and `m3-paperdoll-figure` for a host that serves the figure. Open, by page: the skill page `m3-attribute-heritage`, `m3-pk-status`, `m3-character-titles`, `m3-skill-tooltips`, `m3-skill-aug-bonus`, `m3-stat-tables`, `m3-skill-strings`, `m3-attributes-strings`, `m3-attributes-layout`, `m3-skills-tab-missing`; effects and vitae `m3-effects-spell-table`, `m3-effects-strings`, `m3-vitae-strings`, `m3-effects-page-missing`, `m3-vitae-page-missing`; items and inventory `m3-itemlist-use`, `m3-itemlist-examine`, `m3-itemlist-empty-fill`, `m3-itemlist-template`, `m3-selected-object`, `m3-quantity-source`, `m3-uiitem-extras`, `m3-use-from-pack`, `m3-wield-slot-choice`, `m3-inventory-sidepack-contents`, `m3-inventory-page`, `m3-burden-capacity`, `m3-pickup`; the paperdoll `m3-paperdoll`, `m3-paperdoll-part-lighting`, `m3-aetheria`, `m3-paperdoll-clickmap`, `m3-paperdoll-tooltips`, `m3-paperdoll-checkbox-targets`, `m3-paperdoll-sharp`, `m3-paperdoll-scale`, `m3-paperdoll-figure-data`; the spell page `m3-spellbook-delete`, `m3-spellbook-filter-send`, `m3-spellbook-shortcut`, `m3-spellbook-unknown-spell`, `m3-spellbook-page`, `m3-spell-removal`, `m3-component-buy-rate`, `m3-component-icon`, `m3-component-category`, `m3-component-zero-rows`, `m3-component-strings`, `m3-components-template`, `m3-components-page`; the stand-in `m3-standin-probe-spellbook` (the probe spellbook and enchantments, a placeholder). A page with no controller answers "Not in Lifestone M3" (`NotInM3`), logged as a placeholder.

## 16. Retail functions

Every function the runtime re-expresses or relies on, with its 11.4186 and 11.6096 addresses, checked against the
pinned 11.6096 symbol table of step L10 (`docs/research/retail-addresses-m2.md`); "predicted" marks an address
inferred from its neighbours and not yet confirmed from the bytes.

| Function | 11.4186 | 11.6096 | Lifestone |
|---|---|---|---|
| `UIMainFramework::CreateAndAddRootElement` | 0x006A0C00 | 0x006A1A20 | `UiElementManager.CreateRootElement` |
| `UIElementManager::Init` | 0x0045EE10 | 0x0045EEF0 | `UiClassRegistry` |
| `UIElement::RegisterElementClass` | 0x00460190 | 0x00460270 | `UiClassRegistry.Register` |
| `gmUIHelper::RegisterElements` | 0x0047A460 | 0x0047A860 | `UiClassRegistry.RetailClassNames` |
| `UIElementManager::CreateElement` | 0x0045CB70 | 0x0045CCF0 | `UiElementManager` |
| `UIElementManager::CreateElementRecursiveFromPartialDesc` | 0x0045BFB0 | 0x0045C090 | `UiElementManager` |
| `UIElementManager::CreateElementRecursiveFromFullDesc` | 0x0045B0C0 | 0x0045B1A0 | `UiElementManager` |
| `UIElementManager::CreateHollowElement` | 0x0045D0E0 | 0x0045D1C0 | `UiElementManager.RootElement` |
| `UIElementManager::CreateRootElement` | 0x0045D020 | 0x0045D100 | `UiElementManager.CreateRootElement` |
| `UIElementManager::CreateRootElementByDataID` | 0x0045E3D0 | 0x0045E4B0 | `UiElementManager.CreateRootElementByDataId` |
| `UIElementManager::CreateChildElement` | 0x0045D080, 0x0045D7D0 | 0x0045D160, 0x0045D8B0 | `UiElementManager.CreateChildElement` |
| `UIElementManager::SetFocusElement` | 0x0045B890 | 0x0045B970 | `UiElementManager.SetFocusElement` |
| `UIElementManager::DrawDirtyRegions` | 0x0045B820 | 0x0045B900 | `UiCompositor.Composite` |
| `UIElementManager::DrawRegionWithObject` | 0x0045AC70 | 0x0045AD80 | `UiCompositor` |
| `UIElementManager::UpdateMouseOver` | 0x0045C2B0 | 0x0045C390 | `UiHitTester` |
| `MouseHitTester<MouseOverCriteria>::Recurse` | 0x00459370 | 0x00459490 | `UiHitTester.HitTest` |
| `LayoutDesc::InqFullDesc` | 0x0069A520 | 0x0069B400 | `IUiDescSource.InqFullDesc` |
| `UIElement::UIElement` | 0x004647D0 | 0x00464900 | `UiElement` |
| `UIElement::Initialize` | 0x00462C90 | 0x00462D70 | `UiElement.Initialize` |
| `UIElement::PostInit` | 0x00463870 | 0x004639A0 | `UiElement.PostInit` |
| `UIElement::SetState` | 0x00464E70 | 0x00464FA0 | `UiElement.SetState` |
| `UIElement::SetProperty` | 0x00464B60 | 0x00464C90 | `UiElement.SetProperty` |
| `UIElement::InqProperty` | 0x004638D0 | 0x00463A00 | `UiElement.InqProperty` |
| `UIElement::OnSetAttribute` | 0x00462D80 | 0x00462E60 | `UiElement.OnSetAttribute` |
| `UIElement::SetParent` | 0x00462A50 | 0x00462B30 | `UiElement.SetParent` |
| `UIElement::GetChild`, `GetChildRecursive` | 0x00463A70, 0x00463AD0 | 0x00463BA0, 0x00463C00 | `UiElement.GetChild`, `GetChildRecursive` |
| `UIElement::CompareZLevel` | 0x0045FC60 | 0x0045FD40 | `UiElement.CompareZLevel` |
| `UIElement::MoveTo`, `ResizeTo` | 0x004633E0, 0x00463C30 | 0x004634C0, 0x00463D60 | `UiElement.MoveTo`, `ResizeTo` |
| `UIElement::UpdateForParentSizeChange` | 0x00462640 | 0x00462720 | `UiRuntimeLayout` |
| `UIElement::SetVisible`, `IsVisible` | 0x004622B0, 0x004602C0 | 0x00462390, 0x004603A0 | `UiElement.SetVisible`, `IsShown` |
| `UIElement::SetShouldOwnObject`, `SetIsRootElement` | 0x00461BD0, 0x00460100 | 0x00461CB0, 0x004601E0 | `UiElement` |
| `UIElement::SetMouseVisible`, `UpdateMouseVisibility`, `GetShouldBeMouseVisible` | 0x00461E30, 0x00460260, 0x004601B0 | 0x00461F10, 0x00460340, 0x00460290 | `UiElement` |
| `UIElement::SetCursor`, `UnSetCursor` | 0x0045FF50, 0x0045FF80 | 0x00460030, 0x00460060 | `UiElement` |
| `UIRegion::AddChild`, `RemoveChild`, `BringToFront` | 0x006A0760, 0x006A0330, 0x006A07F0 | 0x006A1580, 0x006A1150, 0x006A1610 | `UiElement` |
| `UIRegion::CompareZLevel` | 0x0069F000 | 0x0069FE80 | `UiElement.CompareZLevel` |
| `UIRegion::MoveTo`, `ResizeTo`, `SetVisible` | 0x0069F830, 0x0069F8C0, 0x0069FED0 | 0x006A06B0, 0x006A0740, 0x006A0D50 | `UiElement` |
| `UIRegion::SetImageByDID`, `ClearImage`, `SetBlitMode` | 0x0069F960, 0x0069F7E0, 0x0069F6F0 | 0x006A07E0, 0x006A0660, 0x006A0570 | `UiElement` |
| `UIRegion::SetAlphaBlendMod`, `SetTilingOffset`, `SetAlphaImageByDID` | 0x0069F5E0, 0x0069F730, 0x0069F030 | 0x006A0460, 0x006A05B0, 0x0069FEB0 | `UiElement` |
| `UIRegion::PointIsOverRegion` | 0x0069EE40 | 0x0069FCC0 | `UiElement.PointIsOverRegion` |
| `UIRegion::AddDirtyRect`, `MakeRootDirtyHere` | 0x0069F080, 0x0069F5B0 | 0x0069FF00, 0x006A0430 | `UiElement` |
| `UIRegion::DrawHere`, `DrawChildren`, `DrawSelf`, `EraseSelf` | 0x0069FA30, 0x0069FCA0, 0x0069F1A0, 0x0069F160 | 0x006A08B0, 0x006A0B20, 0x006A0020, 0x0069FFE0 | `UiCompositor`, `UiElement.Draw` |
| `UIObject::AddDirtyRect` | 0x00699670 | 0x0069A530 | `UiCompositor.AddDirtyRect` |
| `Box2D::Subtract`, `Intersection` | 0x00699560, 0x00466550 | 0x0069A420, 0x00466680 | `UiCompositor`, `UiBoxes` |
| `MediaMachine::Reset`, `Update` | 0x00465D90, 0x00465BA0 | 0x00465EC0, 0x00465CD0 | `UiMediaMachine` |
| `MediaMachine::Update_*` | 0x00465520-0x00465A80 | 0x00465650-0x00465BB0 | `UiMediaMachine` |
| `Device::ForceDisplayResolution` | 0x0043A750 | 0x0043A990 | `UiElementManager.ForceDisplayResolution` |
| `Graphic::Graphic`, `Draw`, `PutImage` | 0x00693EE0, 0x00693B20, 0x00693A30 | 0x00694E00, 0x00694A40, 0x00694950 | `SoftwareUiPainter` |
| `Graphic::SetToDefaultGraphic` | 0x00693D70 | 0x00694C90 | `UiDefaultGraphic` |
| `SurfaceWindow::PointTestOverlay` | 0x004416A0 | 0x00441840 (predicted) | `UiImage.PointTestOverlay` |
| `SurfaceWindow::LegacyBlit` | 0x00441C50 | 0x00441DF0 | not re-expressed (§5.2) |
| `SurfaceWindow::DrawCharacter`, `CreateCharRectPair` | 0x00442BD0, 0x00441480 | 0x00442D30, 0x00441620 | `TextRasterizer` |
| `SoftwareBlitAndColor`, `IsSSEAvailable` | 0x00660300, 0x00660220 | 0x006612A0, 0x006611C0 | `UiBlitter` |
| `BlitLoopNoScale<&BlitOp_SrcCopy<BlitFormat_ARGB,BlitFormat_ARGB>,...>` | 0x00660420 | 0x006613C0 | `UiBlitter` (every loop is listed in its remarks) |
| `ColoringLoopNoScale_FromA8<BlitOp_FourChannelAlpha<AXXX,ARGB,1>>` | 0x0066D310 | 0x0066E2B0 | `UiBlitter.Colorize` |
| `RGBAColor::SetColor32`, `GetColor32` | 0x00430B30, 0x00423D00 | 0x00430CD0, 0x00424000 | `UiRgbaColor` |
| `Font::GetCharDesc`, `GetCharWidthA`, `ContainsChar` | 0x00443360, 0x004433F0, 0x00443420 | 0x004434C0, 0x00443550, 0x00443580 | `IUiFont`, `UiFontAdapter` (through R5's `UiFont`), `UiFonts` |
| `Font::GenerateCharacterMap` | 0x00443470 | 0x004435D0 | `UiFontAdapter` (through R5's `UiFont`) |
| `PixelFormatDesc::SetFormat` | 0x0043D0F0 | 0x0043D290 | `UiImage.PointTestOverlay` (the retail A8 test) |
| `StringInfo::GetString` | 0x0042E760 | 0x0042E980 | `IUiStringSource` |
| `NameInputFilter`, `NumberInputFilter` | 0x004663B0, 0x004663F0 | 0x004664E0 (predicted), 0x00466520 | `TextFilters` |
| `GlyphList::Recalculate`, `AddText`, `Delete` | 0x00473800, 0x00473190, 0x004730A0 | 0x00473BC0, 0x00473550, 0x00473460 | `GlyphLayout` |
| `GlyphList::Find*` (current, next and previous line, line from y, pixels, position, words) | 0x004726F0-0x004732D0 | 0x00472AB0-0x00473690 | `GlyphLayout` |
| `LinebreakUtils::IsWhiteSpace`, `IsNonBeginningChar`, `IsNonEndingChar`, `IsEastAsianChar`, `CanBreakLineAt` | 0x00472520-0x00472610 | 0x004728E0-0x004729D0 | `GlyphLayout` |
| `Glyph::IsWhiteSpace`, `IsNewLine`, `SetFont` | 0x004747D0, 0x004747F0, 0x004749A0 | 0x00474B90, 0x00474BB0, 0x00474D60 | `UiGlyph`, `UiTextElement` |
| `UIElement_Text::UIElement_Text`, `OnSetAttribute`, `DoFontReset` | 0x00468570, 0x0046A640, 0x0046A560 | 0x00468800, 0x0046A8D0, 0x0046A7F0 | `UiTextElement` |
| `UIElement_Text::SetText`, `AppendText`, `AppendTextWithFont`, `SetStringInfo`, `ClearAllText` | 0x0046A4B0, 0x00469CE0, 0x00469D70, 0x0046A0C0, 0x00467850 | 0x0046A740, 0x00469F70, 0x0046A000, 0x0046A350, 0x00467AE0 | `UiTextElement` |
| `UIElement_Text::InqGlyphs`, `RecalculateGlyphList`, `RecalculateTruncation`, `CalcJustification`, `ResizeToPaper`, `DetermineMarginValues`, `SetDirty` | 0x00468EA0, 0x00468420, 0x00466F80, 0x00467260, 0x00467550, 0x004668A0, 0x00466850 | 0x00469130, 0x004686B0, 0x00467210, 0x004674F0, 0x004677E0, 0x004669D0, 0x00466980 | `UiTextElement` |
| `UIElement_Text::DrawSelf` | 0x00467AA0 | 0x00467D30 | `UiTextElement.Draw` |
| `UIElement_Text::CharacterHandler`, `AddText_Internal`, `MoveCursor`, `MoveUpDown`, `SetCursorPosition`, `ScrollToPosition`, `IsPositionInView` | 0x00469B90, 0x00469A30, 0x00468D00, 0x004682E0, 0x00468B90, 0x004691B0, 0x00468AA0 | 0x00469E20, 0x00469CC0, 0x00468F90, 0x00468570, 0x00468E20, 0x00469440, 0x00468D30 | `UiTextElement` |
| `UIElement_Text::DeleteSection`, `DeleteSelection`, `SelectAll`, `Deselect`, `GetSelection` | 0x00469800, 0x004699B0, 0x004678D0, 0x00467930, 0x00466F20 | 0x00469A90, 0x00469C40, 0x00467B60, 0x00467BC0, 0x004671B0 | `UiTextElement` |
| `UIElement_Text::Copy`, `Cut`, `Paste` | 0x004688A0, 0x00469B60, 0x0046A110 | 0x00468B30, 0x00469DF0, 0x0046A3A0 | `UiTextElement` |
| `UIElement_Text::Global_Loop` | 0x00469FC0 | 0x0046A250 | `UiTextElement.OnTick` (caret blink) |
| `SetFontDIDHelper`, `SetFontColorHelper` | 0x00466960, 0x00466AC0 | 0x00466A90, 0x00466BF0 | `UiTextElement` |
| `DialogFactory::IsDialogOpen`, `DialogDone`, `UpdatePendingDialogDisplay_`, `Reset`, `CreateDialog_`, `OpenNextDialog` | 0x00477060, 0x004773C0, 0x00477590, 0x00477950, 0x00477AD0, 0x00477D30 | 0x00477460, 0x004777C0, 0x00477990, 0x00477D50, 0x00477ED0, 0x00478130 | `DialogFactory` |
| `DialogFactory::MakeDialog`, `CloseDialog`, `MakeCallbackDialogInCurrentUI` | 0x00477E90, 0x00478160, 0x00478430 | 0x00478290, 0x00478560, 0x00478830 | `DialogFactory` |
| `ECM_UI::SendNotice_CloseDialog`, `SendNotice_OpenDialog` | 0x00692500, 0x00692650 | 0x00693450, 0x006935A0 | `DialogFactory` |
| `CInputManager_WIN32::GenerateKeyboardEvent` | 0x00689340 | 0x0068A3D0 | `DikScanCodes` |
| `UIGlobals::Init` | 0x004EE470 | 0x004EF100 | `GmUiFlow` |
| `gmUIFlow::gmUIFlow`, `RecvNotice_CharacterError`, `RecvNotice_ServerDied` | 0x0047A610, 0x0047A7C0, 0x0047A9A0 | 0x0047AA10, 0x0047ABC0, 0x0047ADA0 | `GmUiFlow` |
| `gmDataPatchUI::SetConnectLevel`, `SetPatchLevel`, `UseTime`, `OnDDDEvent`, `ListenToElementMessage` | 0x004EDE60, 0x004EDEE0, 0x004EDF10, 0x004EDFF0, 0x004EDE30 | 0x004EEAF0, 0x004EEB70, 0x004EEBA0, 0x004EEC80, 0x004EEAC0 | `DataPatchScreen` |
| `gmDisconnectedUI::SetErrorMsg`, `ListenToElementMessage` | 0x004E99F0, 0x004E99C0 | 0x004EA680, 0x004EA650 | `DisconnectedScreen` |
| `gmEpilogueUI::ListenToGlobalMessage`, `ListenToElementMessage` | 0x004E9C30, 0x004E9C40 | 0x004EA8C0, 0x004EA8D0 | `EpilogueScreen` |
| `gmCharacterManagementUI::UpdateButtons`, `RebuildCharacterList`, `SelectCharacter`, `ResetPreviouslySelectedCharacterSlot`, `Update`, `UpdateWorldName` | 0x004EC240, 0x004EC3A0, 0x004EC160, 0x004EBFF0, 0x004ECAB0, 0x004EC120 | 0x004ECED0, 0x004ED030, 0x004ECDF0, 0x004ECC80, 0x004ED740, 0x004ECDB0 | `CharacterManagementScreen, CharacterManagementRules` |
| `gmCharacterManagementUI::MakeErrorMessageDialog`, `MakeDeleteCharacterConfirmationDialog`, `MakePleaseWaitDialog`, `MakeEnteringWorldDialog`, `MakeConfirmExitDialog`, `CloseDeleteCharacterDialog`, `RecvNotice_CloseDialog`, `RecvNotice_CharGenVerificationResponse` | 0x004ECB10, 0x004ECCA0, 0x004ECED0, 0x004ED090, 0x004ED250, 0x004ED4A0, 0x004ED760, 0x004EBF60 | 0x004ED7A0, 0x004ED930, 0x004EDB60, 0x004EDD20, 0x004EDEE0, 0x004EE130, 0x004EE3F0, 0x004ECBF0 | `CharacterManagementScreen` |
| `gmCharacterManagementUI::OnAction`, `EnterGame`, `ListenToElementMessage`, `RecvNotice_BeginEnterWorld` | 0x004ED410, 0x004ED440, 0x004ED5A0, 0x004E79F0 | 0x004EE0A0, 0x004EE0D0, 0x004EE230, 0x004E8680 | `CharacterManagementScreen` |
| `UIFlow::Update`, `UIFramework::QueueUIMode` | 0x00479430, 0x006A0920 | 0x00479830, 0x006A1740 | `UiFlow, RetailUiScreenHost` |
| `UIElementManager::SetCursor` | 0x0045A800 | 0x0045A910 | `GmUiFlow, UiCursorController` |
| `UIElement_Meter::DrawChildren` | 0x0046FBD0 | 0x0046FF90 | `IUiRegionView, UiMeter` |
| `CInputManager::FireActionEvent` | 0x00432720 | 0x004328C0 | `RetailKeyBindings` |
| `CInputManager_WIN32::AddKeyMap` | 0x006864C0 | 0x00687510 | `RetailKeyBindings` |
| `CInputMap::ActionBindingList::FindBestMatch` | 0x0068D460 | 0x0068E3B0 | `RetailKeyBindings` |
| `CMasterInputMap::MetaModeFromKey`, `Merge` | 0x0068D850, 0x0068F6F0 | 0x0068E7A0, 0x00690640 | `RetailKeyBindings` |
| `QualifiedControl::operator==`, `IsBetterMatch` | 0x0068A1D0, 0x0068A270 | 0x0068B260, 0x0068B300 | `RetailKeyBindings` |
| `UIElement_Button::UIElement_Button`, `ListenToGlobalMessage`, `UpdateState_`, `SetState`, `HandleButtonClick`, `PostInit` | 0x00471B50, 0x00471C60, 0x00471CF0, 0x00471DA0, 0x00471E50, 0x00471F30 | 0x00471F10, 0x00472020, 0x004720B0, 0x00472160, 0x00472210, 0x004722F0 | `UiButton` |
| `UIElement_Button::OnSetAttribute`, `MouseDown`, `MouseUp`, `MouseOverTop`, `ListenToElementMessage` | 0x00471F40, 0x00471FF0, 0x004720E0, 0x004721F0, 0x00472210 | 0x00472300, 0x004723B0, 0x004724A0, 0x004725B0, 0x004725D0 | `UiButton` |
| `UIElement_ListBox::GetShouldBeMouseVisible` | 0x0046D590 | 0x0046B300 | `UiButton, UiListBox` |
| `SurfaceWindow::DrawBox`, `SearchForColor` | 0x004413F0, 0x004417B0 | 0x00441590, 0x00441950 | `UiColorPicker` |
| `UIElement_ColorPicker::UIElement_ColorPicker`, `SetCurrentSelectedColor`, `IndexToRGBA`, `HandleMouseClick`, `DrawSelf`, `OnSetAttribute` | 0x0046AE30, 0x0046AEE0, 0x0046AF90, 0x0046AFF0, 0x0046B070, 0x0046B190 | 0x0046B0C0, 0x0046B170, 0x0046B220, 0x0046B280, 0x0046B320, 0x0046B440 | `UiColorPicker` |
| `UIElement_ColorPicker::ListenToElementMessage` | 0x0046B230 | 0x0046B4E0 | `UiColorPicker` |
| `UIElementManager::CheckCursor` | 0x0045ABF0 | 0x0045AD00 | `UiCursorController` |
| `Dialog::SetContext`, `Dialog`, `ListenToElementMessage`, `UpdatePendingDialogDisplay`, `UpdatePopupSizeAndPosition`, `UpdateDialogText` | 0x00474C60, 0x00476830, 0x004768C0, 0x00476930, 0x004769B0, 0x00476A50 | 0x00475020, 0x00476BF0, 0x00476C80, 0x00476CF0, 0x00476D70, 0x00476E10 | `UiDialog` |
| `Dialog::ListenToGlobalMessage`, `SetData` | 0x00476B90, 0x00476BE0 | 0x00476F50, 0x00476FA0 | `UiDialog` |
| `ConfirmationDialog::SetData`, `ListenToElementMessage`, `CancelDialog` | 0x004764D0, 0x00476670, 0x00476770 | 0x00476890, 0x00476A30, 0x00476B30 | `UiDialogClasses` |
| `ConfirmationMenuDialog::SetData`, `ListenToElementMessage`, `CancelDialog` | 0x00475EB0, 0x00476230, 0x00476340 | 0x00476270, 0x004765F0, 0x00476700 | `UiDialogClasses` |
| `ConfirmationTextInputDialog::SetData`, `ListenToElementMessage`, `CancelDialog` | 0x00475A40, 0x00475BE0, 0x00475D40 | 0x00475E00, 0x00475FA0, 0x00476100 | `UiDialogClasses` |
| `MenuDialog::SetData`, `ListenToElementMessage`, `CancelDialog` | 0x004754F0, 0x004757E0, 0x004758E0 | 0x004758B0, 0x00475BA0, 0x00475CA0 | `UiDialogClasses` |
| `MessageDialog::ListenToElementMessage`, `SetData` | 0x004752C0, 0x00475330 | 0x00475670, 0x004756F0 | `UiDialogClasses` |
| `TextInputDialog::SetData`, `ListenToElementMessage`, `CancelDialog` | 0x00474F00, 0x00475050, 0x00475190 | 0x004752B0, 0x00475400, 0x00475540 | `UiDialogClasses` |
| `WaitDialog::ListenToGlobalMessage` | 0x00474CD0 | 0x00475090 | `UiDialogClasses` |
| `UIElement_Dragbar::StartMouseMoving`, `StopMouseMoving`, `ListenToElementMessage` | 0x0046C760, 0x0046C7C0, 0x0046C850 | 0x0046CB40, 0x0046CBA0, 0x0046CC30 | `UiDragbar` |
| `UIElement_Resizebar::StartMouseResizing`, `StopMouseResizing`, `ListenToElementMessage` | 0x0046B7E0, 0x0046B8C0, 0x0046B930 | 0x0046BA90, 0x0046BB70, 0x0046BBE0 | `UiDragbar` |
| `UIElement::OnVisibilityChanged`, `BringChildToTop` | 0x00463700, 0x004654E0 | 0x00463830, 0x00465610 | `UiElement` |
| `UIElement::GetRootElement`, `RegisterInputMaps`, `UnregisterInputMaps`, `OnAction`, `OnChildAction`, `StartResizing` | 0x0045FA50, 0x0045FB00, 0x0045FB70, 0x0045FBC0, 0x0045FC00, 0x0045FCA0 | 0x0045FB30, 0x0045FBE0, 0x0045FC50, 0x0045FCA0, 0x0045FCE0, 0x0045FD80 | `UiElement.Input` |
| `UIElement::StopResizing`, `StartMovement`, `StopMovement`, `MouseMoveElement`, `CenterAt`, `GetHasFocus` | 0x0045FD60, 0x0045FDB0, 0x0045FE20, 0x0045FE30, 0x0045FFF0, 0x00460230 | 0x0045FE40, 0x0045FE90, 0x0045FF00, 0x0045FF10, 0x004600D0, 0x00460310 | `UiElement.Input` |
| `UIElement::BroadcastElementMessage`, `SetAttribute_Bool`, `MouseDown`, `MouseTap`, `KeyUp`, `KeyDown` | 0x00460330, 0x00460740, 0x00460F10, 0x00460FE0, 0x00461060, 0x004610C0 | 0x00460410, 0x00460820, 0x00460FF0, 0x004610C0, 0x00461140, 0x004611A0 | `UiElement.Input` |
| `UIElement::MouseResizeElement`, `MouseOver`, `MouseOverTop`, `Activate`, `RelinquishFocus`, `ListenToElementMessage` | 0x00461130, 0x004615A0, 0x004615D0, 0x00461C80, 0x00461DE0, 0x00462340 | 0x00461210, 0x00461680, 0x004616B0, 0x00461D60, 0x00461EC0, 0x00462420 | `UiElement.Input` |
| `UIElement::MouseHover`, `ForwardElementMessage`, `MouseMove`, `FindRelative`, `RegisterForElementMessages` | 0x00462520, 0x00462B20, 0x00463310, 0x00464560, 0x004649A0 | 0x00462600, 0x00462C00, 0x004633F0, 0x00464690, 0x00464AD0 | `UiElement.Input` |
| `UIRegion::NotifyMouseDown` | 0x0069FE20 | 0x006A0CA0 | `UiElement.Input` |
| `UIElement::SetTooltip` | 0x004617C0 | 0x004618A0 | `UiElement.State` |
| `UIElementManager::ActivateNext`, `RegisterActivatable`, `ActivationAlert`, `UseTime` | 0x00459910, 0x0045C3C0, 0x0045C460, 0x0045CFD0 | 0x00459980, 0x0045C4A0, 0x0045C540, 0x0045D0B0 | `UiElementManager` |
| `UIElementManager::BroadcastElementMessage` | 0x0045AB40 | 0x0045AC50 | `UiElementManager, UiElementManager.Messages` |
| `UIElement::DefElementMessageHandler` | 0x0045FC30 | 0x0045FD10 | `UiElementManager.Messages` |
| `UIElementManager::BroadcastElementMessage`, `BroadcastGlobalMessage`, `DoVisibilityToggleAction`, `RegisterElementForInputAction`, `RegisterForGlobalMessage`, `RegisterForElementMessage` | 0x0045B2C0, 0x0045B3E0, 0x0045B660, 0x0045D800, 0x0045E680, 0x0045F2A0 | 0x0045B3A0, 0x0045B4C0, 0x0045B740, 0x0045D8E0, 0x0045E760, 0x0045F380 | `UiElementManager.Messages` |
| `UIElement_Field::MouseOverTop`, `CatchDroppedItem` | 0x00472360, 0x00472450 | 0x00472720, 0x00472810 | `UiField` |
| `UIFlow::QueueUIMode`, `QueueUIModeWithError`, `Update`, `UIFlow`, `UseNewMode`, `ListenToGlobalMessage` | 0x004793C0, 0x004793F0, 0x00479430, 0x004795C0, 0x004796A0, 0x004797A0 | 0x004797C0, 0x004797F0, 0x00479830, 0x004799C0, 0x00479AA0, 0x00479BA0 | `UiFlow` |
| `UIFlow::RegisterFrameworkClass` | 0x00479850 | 0x00479C50 | `UiFlow` |
| `UIMainFramework::~UIMainFramework`, `scalar_deleting_destructor` | 0x006A0CC0, 0x006A0D00 | 0x006A1AE0, 0x006A1B20 | `UiFlow.UseNewMode`, `UiFlow.Dispose` (they reset the dialogs) |
| `UIElement_GroupBox::PostInit`, `OnSetAttribute`, `ListenToElementMessage` | 0x0046AC60, 0x0046ACA0, 0x0046AD20 | 0x0046AEF0, 0x0046AF30, 0x0046AFB0 | `UiGroupBox` |
| `ActionState::RemoveKeyPress`, `AddKeyPress` | 0x00431420, 0x00431E00 | 0x004315C0, 0x00431FA0 | `UiInputRouter` |
| `CInputManager::CallCharacterHandler`, `SendActionToListeners`, `SetTextMode`, `UnregisterInputMap`, `UnregisterCallback`, `IsActionInProgress` | 0x004311B0, 0x004312A0, 0x004313F0, 0x004316D0, 0x00431710, 0x00431950 | 0x00431350, 0x00431440, 0x00431590, 0x00431870, 0x004318B0, 0x00431AF0 | `UiInputRouter` |
| `CInputManager::RegisterInputMap`, `UseTime`, `ToggleActionKey`, `DeactivateActionKey`, `TurnOffRunLock`, `ActivateActionKey` | 0x00431B70, 0x00431BC0, 0x00432480, 0x00432530, 0x004325E0, 0x00432650 | 0x00431D10, 0x00431D60, 0x00432620, 0x004326D0, 0x00432780, 0x004327F0 | `UiInputRouter` |
| `CInputManager_WIN32::OnSwitchTextMode`, `FireInputEvent` | 0x006866B0, 0x00688800 | 0x00687700, 0x00689890 | `UiInputRouter` |
| `CInputManager_WIN32::ReleasePressedKeys`, `Activate` | 0x00689590, 0x00689AA0 | 0x0068A620, 0x0068AB30 | `UiInputRouter.ReleasePressedKeys`, `SetClientActive` |
| `CInputManager::CallFocusSwitchHandler` | 0x00431250 | 0x004313F0 | `UiInputRouter.ClientActiveChanged` |
| `CommandInterpreter::LoseKeyboardFocus` (G5's, most likely what `ReleasePressedKeys` calls) | 0x006B31F0 | 0x006B4130 | `UiInputRouter.ClientActiveChanged` |
| `List<CInputManager::InputMapEntry>::insert_sort` | 0x00431510 | 0x004316B0 | `UiInputRouter` |
| `UIElementManager::KeyPressEvent`, `ActionHandler` | 0x0045C300, 0x0045CC00 | 0x0045C3E0, 0x0045CD80 | `UiInputRouter` |
| `CInputManager::CallMouseLookHandler` | 0x00431220 | 0x004313C0 | `UiInputRouter.Mouse` |
| `HierarchyNotifier<MouseOverNotificationLogic>::CallFunctions` | 0x0045ADE0 | 0x0045AEC0 | `UiInputRouter.Mouse` |
| `UIElement::StartTooltipAtMouse` | 0x00460D70 | 0x00460E50 | `UiInputRouter.Mouse` |
| `UIElementManager::StopHover`, `SetDurationForCurrentTooltip`, `StartTooltip`, `SwitchMouseOver`, `CheckTooltip`, `MouseLeaveEvent` | 0x00459280, 0x004592C0, 0x00459700, 0x0045B560, 0x0045B6E0, 0x0045C2A0 | 0x004593A0, 0x004593E0, 0x00459770, 0x0045B640, 0x0045B7C0, 0x0045C380 | `UiInputRouter.Mouse` |
| `UIElementManager::ResetTooltip`, `DoMouseUpdate`, `SetMouseCapture`, `ReleaseMouseCapture`, `MouseDownEvent`, `MouseUpEvent` | 0x0045C360, 0x0045CC60, 0x0045D270, 0x0045D2B0, 0x0045DB60, 0x0045DD50 | 0x0045C440, 0x0045CDE0, 0x0045D350, 0x0045D390, 0x0045DC40, 0x0045DE30 | `UiInputRouter.Mouse` |
| `UIElementManager::StartTooltip`, `OnAction`, `MouseMoveHandler` | 0x0045DE90, 0x0045E6A0, 0x0045E710 | 0x0045DF70, 0x0045E780, 0x0045E7F0 | `UiInputRouter.Mouse` |
| `UIElement_ListBox::SetState`, `OnSetAttribute`, `ResizeTo`, `GetItem`, `SetSelectedItem`, `InqItemIndexAtPoint` | 0x0046D510, 0x0046D6A0, 0x0046D7B0, 0x0046D890, 0x0046D8B0, 0x0046D9A0 | 0x0046D8F0, 0x0046DA60, 0x0046DB70, 0x0046DC50, 0x0046DC70, 0x0046DD60 | `UiListBox` |
| `UIElement_ListBox::RemoveItem`, `StartDragSelect`, `AdjustToScrollableXYChange`, `InqScrollDelta`, `UIElement_ListBox`, `DeleteItem` | 0x0046DAC0, 0x0046DB50, 0x0046DC00, 0x0046DC10, 0x0046DCB0, 0x0046E020 | 0x0046DE80, 0x0046DF10, 0x0046DFC0, 0x0046DFD0, 0x0046E070, 0x0046E3E0 | `UiListBox` |
| `UIElement_ListBox::DeleteItem`, `Flush`, `GetItemUnderMouse`, `ListenToElementMessage`, `MouseDown`, `UpdateLayout` | 0x0046E070, 0x0046E0A0, 0x0046E220, 0x0046E350, 0x0046E3A0, 0x0046E460 | 0x0046E430, 0x0046E460, 0x0046E5E0, 0x0046E710, 0x0046E760, 0x0046E820 | `UiListBox` |
| `UIElement_ListBox::InsertItem`, `ScrollToShow`, `SetSelectedIndex`, `InsertItem`, `SetSelectedItemByID`, `AddItem` | 0x0046E760, 0x0046E940, 0x0046EBA0, 0x0046EC00, 0x0046ECE0, 0x0046ED70 | 0x0046EB20, 0x0046ED00, 0x0046EF60, 0x0046EFC0, 0x0046F0A0, 0x0046F130 | `UiListBox` |
| `UIElement_ListBox::AddItem`, `AddItemFromTemplateListByID`, `AddItemFromTemplateList` | 0x0046EDC0, 0x0046EF00, 0x0046F2B0 | 0x0046F180, 0x0046F2C0, 0x0046F670 | `UiListBox` |
| `UIElement_Menu::GetItem`, `InsertItem`, `RecalculatePopupSize`, `GetNumItems`, `GetSelectedIndex`, `Open` | 0x0046CA50, 0x0046CA90, 0x0046CAF0, 0x0046CBD0, 0x0046CBF0, 0x0046CC30 | 0x0046CE30, 0x0046CE70, 0x0046CED0, 0x0046CFB0, 0x0046CFD0, 0x0046D010 | `UiMenu` |
| `UIElement_Menu::Close`, `NewSelection`, `InsertTextItem`, `SetSelectedItem`, `SetVisible`, `Deactivate` | 0x0046CD20, 0x0046CD60, 0x0046CF10, 0x0046D0D0, 0x0046D100, 0x0046D130 | 0x0046D100, 0x0046D140, 0x0046D2F0, 0x0046D4B0, 0x0046D4E0, 0x0046D510 | `UiMenu` |
| `UIElement_Menu::ListenToElementMessage`, `MakePopup`, `Initialize` | 0x0046D180, 0x0046D310, 0x0046D440 | 0x0046D560, 0x0046D6F0, 0x0046D820 | `UiMenu` |
| `UIElement_Menu::~UIElement_Menu` | 0x0046CE40 | 0x0046D220 | `UiMenu.OnDeleting` |
| `UIElement_Meter::UIElement_Meter`, `StartAnimation`, `UpdateChild`, `Initialize`, `ListenToGlobalMessage`, `UpdateFrame` | 0x0046F4C0, 0x0046F5C0, 0x0046F660, 0x0046F7B0, 0x0046F870, 0x0046F940 | 0x0046F880, 0x0046F980, 0x0046FA20, 0x0046FB70, 0x0046FC30, 0x0046FD00 | `UiMeter` |
| `UIElement_Meter::OnSetAttribute`, `ResizeTo` | 0x0046FE70, 0x0046FFB0 | 0x00470230, 0x00470370 | `UiMeter` |
| `ClientCommunicationSystem::RecvNotice_DisplayStringInfo` | 0x0056E890 | 0x0056F640 | `UiNotices` |
| `ECM_UI::SendNotice_DisplayStringInfo` | 0x006925B0 | 0x00693500 | `UiNotices` |
| `gmSpewBoxUI::RecvNotice_DisplayFinalStringInfo` | 0x004D60A0 | 0x004D6D00 | `UiNotices` |
| `UIElement_Panel::Update`, `OpenTab`, `InqTabFromPage`, `ListenToElementMessage`, `SetupTabPageHash`, `OnSetAttribute` | 0x0046BD00, 0x0046BE20, 0x0046BEB0, 0x0046BF90, 0x0046C2E0, 0x0046C6C0 | 0x0046BFF0, 0x0046C110, 0x0046C1A0, 0x0046C370, 0x0046C6C0, 0x0046CAA0 | `UiPanel` |
| `UIElement_Scrollable::UIElement_Scrollable`, `MouseDown`, `SetScrollableXY`, `PostInit`, `ListenToElementMessage`, `OnSetAttribute` | 0x00473D90, 0x00473E00, 0x004740C0, 0x004744B0, 0x00474560, 0x00474630 | 0x00474150, 0x004741C0, 0x00474480, 0x00474870, 0x00474920, 0x004749F0 | `UiScrollable` |
| `UIElement_Scrollable::ResizeTo`, `ResizeScrollableArea` | 0x004746C0, 0x00474730 | 0x00474A80, 0x00474AF0 | `UiScrollable` |
| `UIElement_Scrollbar::UIElement_Scrollbar`, `RegisterInputMaps`, `SetupDefaultHotClick`, `ComputeActiveSize`, `Initialize`, `StartAnimation` | 0x0046FFF0, 0x004700E0, 0x00470150, 0x004702A0, 0x00470520, 0x00470550 | 0x004703B0, 0x004704A0, 0x00470510, 0x00470660, 0x004708E0, 0x00470910 | `UiScrollbar` |
| `UIElement_Scrollbar::UpdateButtonID_`, `HandlePageClick`, `PointToPosition`, `PositionToWidgetX0Y0`, `UpdateScrollingArea`, `ValidatePosition` | 0x00470680, 0x004706D0, 0x00470890, 0x004709A0, 0x00470AA0, 0x00470B60 | 0x00470A40, 0x00470A90, 0x00470C50, 0x00470D60, 0x00470E60, 0x00470F20 | `UiScrollbar` |
| `UIElement_Scrollbar::Global_Loop`, `HandleMoveSteps`, `HandleButtonClick`, `SetScrollbarPosition`, `ScrollToPoint`, `StartWidgetDrag` | 0x00470CA0, 0x00470E30, 0x00470E90, 0x00470EC0, 0x00470F50, 0x00470FB0 | 0x00471060, 0x004711F0, 0x00471250, 0x00471280, 0x00471310, 0x00471370 | `UiScrollbar` |
| `UIElement_Scrollbar::ScrollNPixelsFromReset`, `UpdateLayout`, `ResizeTo`, `HandleMouseWheel`, `OnSetAttribute`, `ListenToElementMessage` | 0x00471020, 0x004710D0, 0x00471430, 0x00471450, 0x004714D0, 0x00471870 | 0x004713E0, 0x00471490, 0x004717F0, 0x00471810, 0x00471890, 0x00471C30 | `UiScrollbar` |
| `MediaMachine::Update_Sound` | 0x004658B0 | 0x004659E0 | `UiServices` |
| `UIElement_Text::ScrollPage`, `RegisterInputMaps`, `UnregisterInputMaps`, `AdjustToScrollableXYChange`, `ListenToElementMessage`, `DeterminePositionFromXY` | 0x00466440, 0x00467350, 0x004673D0, 0x00467440, 0x004687C0, 0x004688F0 | 0x00466570, 0x004675E0, 0x00467660, 0x004676D0, 0x00468A50, 0x00468B80 | `UiTextElement` |
| `UIElement_Text::InqScrollDelta`, `MouseDown`, `MouseUp`, `MouseMove`, `OnAction` | 0x004689B0, 0x00469370, 0x004694F0, 0x004695F0, 0x0046A260 | 0x00468C40, 0x00469600, 0x00469780, 0x00469880, 0x0046A4F0 | `UiTextElement` |
| `UIElement_Browser::Create` | 0x0046B3C0 | 0x0046B670 | `UiViewport` |
| `UIElement_Viewport::Create`, `PostInit` | 0x0046BA90, 0x0046BB40 | 0x0046BD40, 0x0046BDF0 | `UiViewport` |

## 17. The Godot layer (W2-pre(b), G2 U2)

`src/Lifestone.Game/scripts/RetailUi/` presents what the runtime composites and feeds it the keyboard and mouse; it holds
no UI logic. Retail layouts, fonts, strings, cursors and art only, with no Godot theme.

| Piece | What it does |
|---|---|
| `RetailUiSetup` | The two calls `Main` makes: `AttachExplore` right after `SkyTrack.Attach`, and `AttachOnly` for `--retail-ui-only` (no world, no sky). With `--retail-ui` off, nothing is installed and nothing about the explore scene changes (`--engine-checks` holds; no `retailui` line is logged). A failure to build is an error line and no UI, never a crash. A line on screen says the stand-in screens are placeholders |
| `RetailUiLayer` | A `CanvasLayer` in slot 10 (the portal notice line is 8, the placeholder note 11) that owns the manager, the compositor, the tiles, the input feed, the cursor, the viewport host, the offline stand-in session (`RetailUiOfflineSession`) and the screen host (`RetailUiScreenHost`, §18). Process priority 25 (docs/game-core.md §1.2): after the world, the portal presenter and the sky, it ticks the runtime, runs the flow, composites within its **own 1 ms slice** (round-robin, whole surfaces, not subtracted from the queue's budget) and uploads the tiles of the surfaces that finished. An idle frame allocates nothing (checked). `Flush()` paints and uploads everything at once, for scripted captures and the checks |
| `RetailUiTiles` | The `IUiTileSink`: one `Node2D` per surface at its screen box times the scale, `Scale(s)` with `s` an integer, `Nearest` filtering, repeat off, no mipmaps, one `Sprite2D` per 128-pixel tile. Only the tiles `CopyDirtyTiles` hands over are uploaded, straight-alpha RGBA8, through one reused `Image` and byte array per tile size into the tile's own `ImageTexture` (updated in place; re-made only when the tile's size changes). Back-to-front order is the nodes' `ZIndex`; a hidden surface is hidden; a fade is the node's modulate |
| `RetailUiInput` | `_Input` to `UiInputRouter`: Godot physical key codes through `GodotKeyCodes` (the Client's table; Shift, Ctrl, Alt and Windows keys by side) to DIK codes, then the key's typed character (`Unicode`) to the focused text field, as `WM_CHAR` follows `WM_KEYDOWN` (D39, no IME); the mouse in logical pixels (window position divided by the scale, rounded down), one wheel notch per press event, Godot's double click. `SetInputAsHandled` only when the router took the event: a key nothing binds stays the host's, so the explore camera and F3 still work. `NOTIFICATION_APPLICATION_FOCUS_OUT` and `FOCUS_IN` call `SetClientActive`; the mouse leaving the window calls `MouseLeave`. While the mouse is captured (mouse look) only the router's mouse-look path hears it |
| `RetailCursor` | `Input.SetCustomMouseCursor` with the controller's cursor enlarged by the scale with nearest neighbour (hot spot scaled with it), at most the engine's 256 pixels (the factor is capped at 256 / the image's longest side: 8 for the 32-pixel retail cursors); it acts only when `UiCursorController.Version` or the scale changes; an image still decoding is asked again next frame |
| `RetailViewportHost`, `IUiViewportSource` | Keeps each viewport's hole in logical and window pixels, tells the registered source (`Bound(rect, visible)`) and draws its texture in the hole: as a child of the **surface node the viewport element owns** (retail renders a viewport's scene into its own surface), above the page's surface, which may paint a panel under the hole (character creation's does), and below every later surface (the dialogs); without that node (the synthetic checks, the first frames of a screen) in a node **behind** the surfaces, where a transparent hole shows it. A viewport with no source leaves the world (or, behind a forced display, the black backdrop) showing |
| `GodotUiImageSource` | The first request for an image decodes it on a worker (`GameServices.Workers`) and answers `Pending`; the compositor keeps the area dirty and paints once it is ready. The decode is the runtime's `DatUiImageSource`, and the six JPEG images go through `UiJpegDecoder`, **not** Godot's loader, so the screen is the tests' pixels (checked: the worker-decoded screen equals a synchronous composite). The three hit-test masks are decoded ahead (`Prefetch`) because the compositor's pending check does not see them |
| Adapters | `UiSoundAdapter` (`IUiSoundSink` over the portal `ISoundSink`, found when a sound plays because the audio service is installed after the UI; silent without one (the `TODO(ui-sound-sink)` of M2 D62 is closed in M3 1.3 s2a, section 15): the first sound asked for with no sink logs it once and `UiSoundAdapter.Dropped` counts the rest; the retail-ui engine check clicks a button and asserts that a recording sink is asked for the sound-table entry the layout names, and the no-sink case), `DisplayServerClipboard` (`IUiClipboard`), `WorldPointerRelay` (`IWorldPointerSink`: G5 sets `Target`; it also reports text entry, which drives `KeyboardCapturedChanged` and, in `AttachExplore`, `FlyCamera.InputHeldByRetailUi`, the retail UI's own hold, which `InputEnabled` reads with the teleport panel's flag and `InputSuspended`) |
| Fonts | The production adapter is `UiFontsSource` (§7) over R5's `UiFonts`; nothing new was needed. The glyph-fallback questions between R5 and R12 are settled by the rules in §7 and pinned in the wired source by `[uicheck]`: offsets are signed; a character inside a font's range that the font lacks draws glyph 0 (kept: retail's `Font::GenerateCharacterMap`); one outside the range falls back to `'?'`; a control character is drawn from the Gulim fallback font (FONT enum 22, `Glyph::SetFont`). Still to confirm in the 11.6096 machine code: the sign extension of the offsets |

**Contract members** (m2-plan §4.13): `Scale` (an integer; it hides `CanvasLayer.Scale`, which stays 1), `LogicalSize`,
`GameViewRect` (the whole window: in retail's default shell the SmartBox fills the screen, D40 as amended), `KeyboardCaptured`
(and `KeyboardCapturedChanged`), `WantsWorldHidden` (a forced display with a screen showing: the layer holds one hide request
on `WorldVisibility` under the owner `retail-ui` and shows a black backdrop) and `RegisterViewportSource(id, IUiViewportSource)`.
`SetSelectionProjector(ISelectionProjector?)` is built (G5 W7, G4 slice g0, docs/avatars.md §18): play mode gives the layer the world's picking, and `SelectionProjector` reads it back; rectangles are in `LogicalSize` pixels.

**Scale.** `UiScalePolicy.Choose` on the layer's viewport size, or `--ui-scale`; a resize picks it again
(`UiElementManager.SetScale`, then the tiles, the viewport host and the cursor). 1920x1080 gives 1 (the 800x600 login screens
centred on black), 2560x1440 gives 2.

**Options** (`RetailUiOptions`, registered in `OptionCatalog.RetailUi`):

| Option | Effect |
|---|---|
| `--retail-ui=off` or `standin` | Install the retail UI (default off). `--retail-ui-start`, `--retail-ui-frames` and `--retail-ui-only` imply `standin`; with an explicit `off` they are a reported error and the UI stays off |
| `--ui-scale=N` | The integer scale, 1 to 16; 1 restores retail's pixel-for-pixel UI |
| `--retail-ui-start=SCREEN` | `datapatch` (default), `intro`, `charmanage`, `game`, `epilogue`, `disconnected`, `chargen`, `credits`: queues that mode after the flow's data check. The intro, credits and epilogue have no stand-in (D38): the mode stays queued and a TODO is logged |
| `--retail-ui-frames[=DIR]` | The review-frames runner (U9, see "Review frames" below): plays the login screens offline on stand-in data, saves 14 frames to `DIR` (default `docs/screenshots/m2/ui/<W>x<H>`, gitignored), prints `retail ui frames done size=<W>x<H> scale=<s> captured=<n> failures=<f> missing_images=<k>` and quits (exit 1 on any failure). Builds no world (it implies the `--retail-ui-only` behaviour) |
| `--retail-ui-only` | No world and no sky (`Main` returns before building them) |
| `--retail-ui-glitches` | `UiServices.RetailGlitches = UiRetailGlitches.All` (the fidelity rule's flag, §14) |
| `--retail-disconnect` | Retail's exit from the disconnected screen (D43): its button says "Quit Game" and ends the application; by default it says "Reconnect" and reconnects (§18) |
| `--retail-effects-stale-info` | Retail's stale info box on the effects pages (`UiRetailGlitches.EffectsStaleInfo`; also set by `--retail-ui-glitches`): by default the box and the selection clear when the last effect ends (D32, §14, §23.3) |

**Stand-in screens** (`UiStandInScreens`, Client): the data check, the disconnected screen and character management are real since
G2 U3-U4 (§18). Character creation (G3) without its data still shows its layout as a stand-in in the
forced, centred 800x600 display. The game screen is no stand-in since G4 g1: it is `GamePlayScreen` (section 21). A stand-in is a placeholder,
labelled on screen while it shows: widget classes behave, but no screen logic fills a list or ghosts a button.

**Engine checks** (`--retail-ui-checks`, `RetailUiChecks`; the smoke step `retail-ui` runs them after its frames; run off-screen): `[uicheck] PASS|FAIL
<name>` lines and `all retail-ui checks hold`. 67 checks (12 of them G4 g1's, section 21): tiles byte for byte at scales 1, 2 and 3 (random pixels, full and
edge tiles), nearest filtering (a one-pixel checkerboard), no seam at the tile edges, no mipmaps, straight alpha, back-to-front
order, only the dirty tile uploads (into its own texture); the viewport hole at scales 1 and 2 (the source's picture exactly in
the hole, the source told the hole in window pixels, a hidden hole shows nothing); the cursor (nearest enlargement, hot spot,
256-pixel cap, refusal above it); every keyboard control of the engine and game key maps has a Godot physical key; the fonts
(above); the real data check at 800x600 (scale 1) and 1600x1200 (scale 2) read back as the compositor's surface and equal to a
synchronous composite; the layer's contract members, the default pointer and the world hide request; input (W reaches map 4 as
Move Forward, an unbound key is not taken, a press outside the screens reaches the world at 5,6 for window 11,13 at scale 2, the
keyboard capture is reported once each way, the focus notifications); an idle frame allocates 0 bytes; the login flow (§18: the data
check, character management with its list and ghosting, restore, delete with the typed word, the disconnected screen and its
reconnect). The review frames are
written under `cache/retail-ui-checks/` (gitignored).

**Review frames** (`--retail-ui-frames[=DIR]`, `RetailUiFramesRunner` over `RetailUiFramesPlan`; smoke step `retail-ui`; run off-screen,
`-Resolution WxH` sets the window; when the desktop's work area is smaller than the request, which keeps a decorated window short (1920x1061 for a 1920x1080 request on a 1920x1080 display, 2560x1421 on a 2560x1440 one), the runner makes the window borderless and sizes it again, so the frames are the size requested on any desktop and the check that the window is the size asked for stays): the layer is stepped by the runner at a fixed 1/60 s, so a run is the same frame for frame, and the
screens, dialogs and session are the ones `--retail-ui=standin` shows; only the clicks and the typed words are the runner's, sent through
the input router the mouse and the keyboard feed. The 25 frames, in order (`NN-name.png`): the data check mid-progress (`datapatch-checking`: the real check under way;
`datapatch-queue` and `datapatch-patching-half`: synthetic, because no session raises the queue and download events, Lifestone does
not patch), character management with a character selected and with the character pending deletion selected, the typed-word delete
confirmation, Please Wait, the list after a delete (two red rows, Restore), the exit confirmation, the Credits "not in M2" message,
the Entering World dialog (synthetic: offline the screen closes it at once, so the runner opens it with the screen's own data), the
offline refusal of Enter, a retryable character error (code 13; synthetic: the runner calls `RecvNoticeCharacterError`), character creation's six pages (§19 and §20: `chargen-heritage`, `-profession`, `-skills`, `-appearance-face`, `-appearance-clothes`, `-town`, `-summary`),
the name-in-use message (`chargen-dialog-name-in-use`), character management after a successful create (`charmanage-after-create`), an Olthoi's appearance and
summary pages (`chargen-appearance-olthoi`, `chargen-summary-olthoi`) and the disconnected screen (synthetic: the runner calls `RecvNoticeServerDied`). The run fails (exit 1, `failures>=1`)
when the window is not the size `--resolution` asked for (checked against the process's own command line, since `OS.GetCmdlineArgs()`
holds no engine argument; Windows clamps a window larger than the display: 7680x4320 on a 2560x1440 display comes up as 2564x1421
and captures nothing), when a screen or dialog is not where the script expects it, a frame is missing, blank or not the window's size, the UI
did not settle, or an image other than a known one is drawn as the default graphic. The one known unresolved UI image is DID 0 (an
entry that clears an element's image; §7 of retail-ui-data.md), named in a log line of every run, so `missing_images=0` is expected.
At scale 1 the 800x600 screens sit centred in a 1920x1080 window over black (the integer-scale rule, D37); `--ui-scale=2` shows them
doubled. The smoke step runs 800x600 (retail's native size; the owner's rule of 2026-10-14 keeps every test, check and frame run there, several resolutions being a polish step at the end of the project), then the `[uicheck]` suite; `-AllSizes` adds the 1920x1080 frames run.

**Placeholders and TODOs** (logged, category `retailui`): the stand-in screens (above); `TODO(retailui-host)` is closed (`SetSelectionProjector`, G4 slice g0). `Lifestone.Data.csproj` embeds
`Ui\Layouts\*.json` (G7 lane A, with `tools.json`; `UiLayouts` registers the files on the first lookup of a DID in 0x7F000000-0x7FFFFFFF, section 22.1).

## 18. The login screens (G2 U3-U4)

`src/Lifestone.Client/RetailUi/Screens/` holds the screens of the first three modes of `gmUIFlow`, retail-exact, plus the host
that runs them. Screens read `IGameSessionView.State` and its events and act only through `IActionApi`; they cannot tell a live
session from the offline stand-in. They are engine-agnostic and tested against the user's dats with the retail fonts and strings.

| Mode | Screen | Layout enum / root | What it does |
|---|---|---|---|
| 0x10000003 data check | `DataPatchScreen` (`gmDataPatchUI`) | 0x10000001 / 0x1000041A | Connect meter 0x1000041E (label 0x10000420, states 1, 0x1000003B, 0x1000003C), patch meter 0x1000041F (status text 0x10000421), quit button 0x1000041C |
| 0x10000002 disconnected | `DisconnectedScreen` (`gmDisconnectedUI`) | 0x10000003 / 0x10000416 | The reason in text 0x10000417, one button 0x10000418 |
| 0x1000000A character management | `CharacterManagementScreen` (`gmCharacterManagementUI`) | 0x10000005 / 0x1000039A | List 0x1000039D (row template 0x100003A5), world name 0x1000039B, Enter 0x100003A2, Delete 0x1000039F, Restore 0x1000039E, Create 0x100003A0, Credits 0x100003A3, Exit 0x100003A4 |
| 0x10000001 intro | `IntroSkippedScreen` | none | Skipped (D38): its first frame queues character management |
| 0x10000009 epilogue | `EpilogueScreen` (`gmEpilogueUI`) | none (retail: 0x10000037 / 0x10000399, one movie) | Ends a live session (`Actions.Quit`), then closes the application (`IUiLoginHost.Exit`); the movie is skipped |

`RetailUiScreenHost` (U3) is `gmUIFlow` with these screens: `LoginScreens.Create` is the screen factory (character creation, the
game screen and the credits keep `UiStandInScreens`), `GmUiFlow.Attach(session.Events)` routes character errors, and a session
that ended in failure (`GameState.ConnectionLost`, read every frame so a session that failed before the host started is reported
too; a reconnect arms it again) becomes `RecvNoticeServerDied`, once. It owns the `UiNoticeRouter` the game's text notices go to.
`--retail-ui-start=disconnected` starts the screen with the lost-connection reason.

**Data check.** The connect level is 0 until the session's data check has started (`DataCheckState` is not `NotStarted`), then 1;
the patch level is 0 until `Complete`, when "Patching Done!" shows and the patch meter fills. "Looking for data to patch..." shows
during `Checking`. When both are full and the character list has arrived (`Characters.Version > 0`) the screen queues character
management (retail queues the intro, whose movies end there: D38). The queue place, the bytes to download and their progress
(`OnDataEvent`, retail's `OnDDDEvent` cases 2, 3 and 5, with their strings "Too many people patching. We're #N in line...",
"Patching data..." and "P% of NK complete...") are implemented and tested but no session raises them: Lifestone does not patch, the
dats are the user's own and must match the server's (`TODO(retailui-datapatch)`; a mismatch ends the login, which the disconnected
screen reports). The quit button queues the epilogue.

**Disconnected.** `DisplayError` sets the reason's string (`SetErrorMsg`). The button, retail's "Quit Game", reconnects (D43): the
host resets the session for a new login (`IUiLoginHost.Reconnect`), the flow queues the data check again, and the button says
"Reconnect" (a Lifestone literal that survives the button's states). With `--retail-disconnect` it says "Quit Game" and queues the
epilogue as retail's does, and a host that cannot reconnect falls back to it, loudly (`TODO(retailui-reconnect)`).

**Character management.** The list is rebuilt when the session's character list changes (the delete acknowledgement alone does not
change it), as retail's `Update` runs when its persistent data changes: a row from the template per character, the character's id as
instance property 0x10000009, its name as text, font colour 0 red for a character pending deletion (no countdown, D52), a height of
`min(listHeight / max(characters, slots), listHeight / 20)` (16 pixels in the retail list), the wish for double clicks; created in
server order, the default selection taken there (the first character that is not pending deletion, else the first), then sorted by name
(ordinal) with the pending ones last. The selection is kept by character through a rebuild and, as the session's `SelectedGuid`, through
a mode change. The buttons follow `UpdateButtons` (`CharacterManagementRules.Buttons`, the ghosting matrix):

| Selected | Enter | Delete / Restore | Create |
|---|---|---|---|
| nothing | ghosted | Delete | normal when `characters < slots`, else ghosted (an unknown slot count ghosts it) |
| a ready character | normal | Delete | as above |
| a character pending deletion | ghosted | Restore replaces Delete | as above |

Delete (a click with a selected row) opens the confirmation text-input dialog naming the character (the string's one variable,
0x05506DA2) and asks for the word of `ID_CharacterManagement_DeleteCharacterResponse` ("DELETE", compared ignoring case); only then
"Please Wait" opens and `Actions.DeleteCharacter(guid)` runs (the Action API finds the list index). Restore opens "Please Wait" and
runs `Actions.RestoreCharacter(guid)`. Enter (the button, or a double click on a row) opens "Entering World" and runs
`Actions.EnterWorld(guid)`; the flow starts the game screen when `PlayerCreated` arrives (`RecvNotice_BeginEnterWorld`). Exit and the
Escape key (input map 9, action 39, priority 3000) ask "Are you sure you want to leave?", and yes queues the epilogue. Create queues
character creation. Credits opens a "Not in Lifestone M2" dialog (D38). "Please Wait" closes when the list updates. A refused restore
(F643 name in use, corrupt or database down) closes it and says why in retail's strings; any other result only closes it. An error the
flow queued the screen with (`DisplayError`) is a message dialog. One error dialog at a time, as retail's context: a second error
while one shows is dropped. An action that fails for a reason retail has no text for (a timeout, a refusal, a server error the flow
does not route) closes the wait dialogs and says so in a message dialog in Lifestone's words, logged as a placeholder; with the offline
stand-in, entering the world says "Not in Lifestone M2: entering the world without a server".

**Error routing** (`GmUiFlow.Routes`, §11): codes 1, 3, 4, 8, 9, 10, 14, 21 and 24 queue the disconnected screen with the error's
string; 5, 6, 11-13, 15-20 and 23 queue character management again with the string in a message dialog (code 17 has no string in the
retail table, so the dialog shows the string source's error text, loudly); any other code changes nothing. The tests run every code
through the real reducer and screens.

**The live session (G6 W9, `--play`).** `RetailUiLayer.Create` takes the `SessionHost` of a play run: the screens then read and act on the live session
(which the host ticks, before everything else in the frame), `RetailUiMode.Live` makes the layer exist without `--retail-ui`, the disconnected screen's button reconnects through the host
(`SessionHost.Reconnect`: a new network session with the same credentials, then the login again), and `--retail-ui-start` is ignored. The game mode is `GamePlayScreen`, the in-game shell (section 21; it was
`PlayGameScreen` until G4 g1): the forced 800x600 display is released, the gameplay root is drawn, the screen's note names the windows still without content, and the screen returns to character
management when the server sends the character list again (a logoff). `tools\play.ps1` and docs/session.md section 21 describe the rest.

**The offline stand-in session** (`RetailUiOfflineSession`, Game; made by `SessionHost.CreateStandIn`, the only place a session is created): without `--play`,
`--retail-ui=standin` creates `GameSession.Offline` fed by `StandInSessions` (a timed 2 s data check, then three Lifestone-authored
characters, one pending deletion; a screen further on starts with the list already in) with the stand-in responder on, and ticks it
each frame before the UI. In a scripted run (`--retail-ui-checks`) its clock follows the frame time (`FrameTimeProvider`), so the checks
are deterministic. `RetailUiLayer` is the `IUiLoginHost`: exit closes the tree, reconnect resets the offline session.

**The stand-in responder** (`StandInSessions.AnswerLocally`, opt-in, offline only): the Action API consults it only when it has no
network session and only for delete, restore and create. It feeds the same reducer the messages ACE would send, 0.6 s later on the
session clock: for a delete a synthetic F655 acknowledgement and the F658 with the character pending deletion (ACE reports 1, D52); for
a restore F643 `Ok`; for a create F643 `Ok` (the reducer appends the character) or `NameInUse` for a name the list holds
(case-insensitively). Everything else stays refused. A live session cannot have one (`AnswerLocally` needs an offline reducer),
`GameSession.Reconnect` keeps it, and tests pin all of this.

**Checks.** `LoginScreenDataTests` (every element a screen uses exists with the class it casts to, the connect label's states, the row
template, every string in the pregame table, the variables' ids), `DataPatchAndDisconnectedTests`, `CharacterManagementScreenTests`
(the ghosting matrix, the order, the dialogs, every error code) and `StandInResponderTests` run in the unit suite; `--retail-ui-checks`
runs the flow through the whole layer and writes review frames under `cache/retail-ui-checks/` (`login-*.png`, gitignored).

## 19. Character creation: the main screen and the first three pages (G3 U5, first stage)

`src/Lifestone.Client/RetailUi/Screens/` holds `gmCharGenMainUI` (`CharGenMainScreen`) with the heritage, profession and skills pages
(`CharGenHeritagePage`, `CharGenProfessionPage`, `CharGenSkillsPage`); the appearance, town and summary pages, the 3D character, Finish and the create
are §20 (the second stage of step U5; the first stage had `CharGenPlaceholderPage`s there). Everything runs over one
`CharGenState` (§10 of retail-ui-data.md) that the screen makes when it opens and fills with `RandomizeCharacter`: the pages show it and
call its setters, and none of its rules is repeated here. The pure rules (page order, button tables, readouts, skill rows, formula text) are
`CharGenRules`.

**Reaching it.** Create on character management queues `UiModes.CharGen` (0x1000000B). `LoginScreens.Create` makes the real screen when the
context carries a `CharGenSetup` (the CharGen data of the dats, a source of dice, the Throne of Destiny flag; the Godot layer reads the data
at start, with a fixed seed in a scripted run), else the stand-in. `--retail-ui-start=chargen` starts there. Back on the first page, Exit and
the Escape action (a global message, action 39) ask `ID_CharGen_ExitWarning`; yes queues character management.

| Part | Layout enum / ids | What it does |
|---|---|---|
| Main screen | 0x10000039 / root 0x100003CC | Master page 0x100003D0 holds the six pages 0x100003D1-0x100003D6 (classes 0x10000039-0x1000003E); tabs 0x100003EF-0x100003F4 in the progress bar 0x100003CE; Back 0x100003C6, Next 0x100003C7, Finish 0x100003C8, Help 0x100003C9, Exit 0x100003CA, Random 0x100003CB in the bar 0x100003CF |
| Heritage | 0x100003D1 | Thirteen buttons (the heritage table in `CharGenRules`), picture 0x100003BE (state per heritage), text 0x100003C4 |
| Profession | 0x100003D2 | Seven profession buttons 0x100003D9-0x100003DF (not in template order: 0x100003DF is the third template), picture 0x100003D8, text 0x100003E0, six attribute rows 0x100003E6-0x100003EB (lock 0x100002EC, name 0x100002ED, slider 0x100002EE, number 0x100002EF), credits 0x100003E2, health, stamina and mana 0x100003E3-0x100003E5 |
| Skills | 0x100003D3 | List 0x100003F7 (template 0 a group header with text 0x100002F6, template 1 a skill row: name 0x10000301, level 0x10000302, up cost 0x10000303, down cost 0x10000306, arrows 0x10000304 and 0x10000305), credits 0x100002F3, skill box 0x100003FB and 0x100003FC |

**Navigation** (`SetProgressState`, 11.4186 0x004E7A10, 11.6096 0x004E86A0). Every page hides, the tabs go to Normal, the master page takes the
page's state (0x10000025-0x1000002A), the page shows with its tab in Highlight and draws itself again from the state. Next does nothing on the last page,
where Finish replaces it. **Retail never ghosts Back or Next**: the gates are in the pages (credits, locks, the Throne of Destiny), not in the
buttons. The Olthoi (heritages 12 and 13) have no profession, skills or town page: their tabs hide and the order skips the pages
(`CharGenRules.Resolve`). Random (`DoRandom`) draws the current page again (a heritage, a profession with its default attributes, the skills, the start
town, or on the summary page the whole character, after a warning).

**Heritage page.** A click calls `SetHeritageGroup`; Viamontian first asks the account for the Throne of Destiny and says
`ID_CharGen_ToDRequiredWarning` without it (every end-of-retail account has it, so the flag defaults to true). The text is a header in font colour 1
(`SetStringInfoWithFont`), the shared starting skills, the bonus-skills header, the heritage's bonus skills, a line feed and its description.

**Profession page.** A profession button calls `SetTemplate(n, true)` then clears the locks and shows the default attributes. A slider's message carries
thousandths; a tenth of it (at least 10) is the attribute, and a raise above `GetAbsRemainingCredits` is cut before the state's setter (with balancing) hears it. The number field
takes digits only and means 10-100 when it loses the focus. A lock freezes an attribute out of the balancing (slider and lock change state). Health is
endurance halved and rounded half up, stamina endurance, mana self. The template is fitted to the numbers after every change, so a hand-made
character shows as the custom profession.

**Skills page.** Four groups (specialised, trained, untrained that can be used, untrained that need training: `MinLevel` above 1), each skill in name
order inside its group (ordinal), priced by the SkillTable and shifted for the heritage (`CharGenRules.SkillCosts`: a skill the heritage trains for nothing costs 0 and
cannot be untrained). The arrows ask the state (`SetSkillLevel` refuses what the credits cannot pay) and the row moves to its new group; a step of 999 credits or more shows no
cost. In the user's dats the page's figures equal the state's charges for every heritage and skill (tests).

**Deviations and decisions** (documented defaults; a flag restores retail where noted):
- The heritage tabs: retail's handler lists no Lugian button, so after an Olthoi the tabs stay hidden until the page changes. Lifestone shows them by the
  character's heritage; `--retail-ui-glitches` (`UiRetailGlitches.CharGenHeritageTabs`) restores retail's.
- Retail's `NumberInputFilter` is assigned beside the attribute's name field in the decompile; Lifestone filters the number, the field it can mean.
- Typing in a number moves the slider's goal (property 0x85); the slider's smooth movement has no duration in the layout, so it catches up when the field loses the focus.
- The Penumbraen show the Shadowbound strings (retail's `Update` shares the case).
- The Help button and key open a "Not in Lifestone M2" dialog (retail opens its help).
- A template outside 0-6 (the state's -1) shows no profession and logs an error (retail's code would fault).

**Loud TODOs** (logged once, category `retailui`): `TODO(retailui-chargen-help)`, `TODO(retailui-chargen-data)` (the text retail shows for a step of 999 or more is a wide string at 11.4186 0x00794320, not read; taken as empty).

**Placeholders on screen.** `IUiPlaceholderScreen` lets a half-built screen say what is missing: the Godot layer shows its note at the top left (as it does for the
stand-ins), and each 3D character view's hole (appearance 0x100003BB, summary 0x10000406) wears a label while no source draws there (§20: a host that gives no view data).

**Checks.** `CharGenRulesTests` (no dats), `CharGenScreenTests` (the layout's elements and classes, every string, navigation, the Olthoi, the glitch flag, each heritage
and profession button, attribute credits, locks, typed numbers, skill credits over 160 random arrow clicks for every heritage with a skills page, the skill box, Random,
the missing view data) and the frames below. The frames are `chargen-heritage`, `chargen-profession` and `chargen-skills` (and the later pages, §20), taken after real clicks
on Create and Next (nothing synthetic), on a random character drawn with the scripted run's seed.

## 20. Character creation: the appearance, town and summary pages, the 3D character, Finish (G3 U5, second stage)

The last three pages replace the first stage's placeholders, over the same `CharGenState`; the 3D character is the avatars track's `CreaturePreview` in the portal
track's `CreatureModeView`, and Finish sends the create through the action API. Everything below is re-expressed from the 11.4186 decompile by function, with the
11.6096 address next to it (the rows are in docs/research/retail-addresses-m2.md §2; sources in docs/third-party/retail-ui-chargen.md).

| Part | Layout ids | What it does |
|---|---|---|
| Appearance (`CharGenAppearancePage`, `gmCGAppearancePage`) | 0x100003D4, class 0x1000003C | Female 0x100003A7, Male 0x100003A8, Face tab 0x100003A9, Clothes tab 0x100003AA; the face group 0x100003AE (spinners hair 0x100003AF, eyes B0, nose B1, mouth B2, skin B3) and the clothes group 0x100003B4 (headgear B5, shirt B6, trousers B7, footwear B8), each spinner holding the arrows 0x1000030A (back) and 0x1000030B (on); the wheel: nine spots 0x1000030F to 0x10000317, nine rings 0x10000318 to 0x10000320, the disk 0x1000030E and the shade slider 0x10000321; turn 0x10000323 and 0x10000324, zoom in 0x10000325 and out 0x10000326; the viewport 0x100003BB |
| Town (`CharGenTownPage`, `gmCGTownPage`) | 0x100003D5, class 0x1000003D | Sanamar 0x1000040B, Holtburg 0x1000040D, Yaraq 0x1000040E, Shoushi 0x1000040F; the text 0x10000409; the page's own states 0x10000034 to 0x10000037 show the town's name and map |
| Summary (`CharGenSummaryPage`, `gmCGSummaryPage`) | 0x100003D6, class 0x1000003E | The list 0x10000400 (templates: 0 a line with text 0x100002F9, 1 a heading with text 0x100000FE, 2 a pair with 0x100002FC and 0x100002FD), the name field 0x10000402, the how-to text 0x10000404, the viewport 0x10000406 |

**Appearance page.** The Face tab holds the state's headgear style and sets none (the head shows), shows the face spinners and moves the camera to the face; the
Clothes tab gives the held headgear back (only through this tab: a character left in the face half is created without headgear, as in retail), shows the clothes
spinners and moves the camera to the whole figure. The camera moves linearly over 0.6 s (the zoom sets a flag; each frame message moves the camera; the first frame
starts the clock). The cameras are in AC space and look along +Y: whole figure (0, -2.5, 0.95), the Olthoi's (0, -3.8, 1.15), the acid Olthoi's (0, -5.7, 1.65); face (0, -0.55, 1.65),
the Tumerok's (0, -0.85, 1.65), the Olthoi's (0, -1.85, 1.85), the acid Olthoi's (0, -3.05, 2.75) (`CharGenRules.BodyCamera` and `FaceCamera`). A turn button turns the
character a whole turn in 5 s (the same button again stops it; the other takes over; the heading wraps to 0-360). A spinner's arrows step the style
(round the list; the headgear also holds -1, none), write the state's field and select the part again; the spinner itself only keeps the style in range. Selecting a
part fills the wheel: the spot colours are the palette entry 208 (hair), 259 (eyes, one palette each), 176 (skin: the nose, mouth and skin share its one set) or 1312 (the
garments) averaged over the palettes of the part's PalSet (integer division); spots past the part's colours show the empty bullet and do nothing; the ring marks the chosen
spot. The slider's position is the shade (message 10: position x 0.001, clamped to 0-1) for the hair, the skin (nose, mouth, skin) or the garment; the eyes have no slider and
show the plug. The spot pictures and the disk are made as retail's surface calls make them (`CharGenSwatches`: the blank bullet with its opaque black replaced, the gradient ring multiplied
by the colour, the plug and the empty bullet copied) and given to the element as a generated image (`UiElement.SetGeneratedImage`); a bullet the host has not decoded yet is made on a
later frame. The Gear Knights and the Olthoi hide the clothes tab, the nose and mouth spinners, use their own texts and move the skin spinner up to y 90 (180 otherwise); entering the
Gear Knight heritage from another draws the appearance and clothing again at random, and so does leaving it.

**Kept from retail as observed:** opening the page sets the opposite gender first (the code does, on a random character).

**Glitches fixed by default** (each restored by `--retail-ui-glitches`; §14):
- *The camera* (`UiRetailGlitches.CharGenAppearanceCamera`). Retail's `gmCGAppearancePage::Update` (11.4186 0x0047E8F0, 11.6096 0x0047ECA0) puts the camera at the face and leaves the zoom flag alone, and it runs
  whenever the page is shown again (Back from the town page), on the page's Random and on a gender button. On the clothes half that shows the clothes spinners over a close-up of the head, and
  zoom-out does nothing (the flag says it is zoomed out) until zoom-in is pressed. Lifestone puts the camera by the zoom flag (face when zoomed in, whole figure otherwise); the Gear Knight and Olthoi
  branches still force the face half, as retail's.
- *The held headgear* (`UiRetailGlitches.CharGenHeldHeadgear`). The face half holds the headgear style and takes the hat off (`SetChoice`, 0x0047D4B0 / 0x0047D860); only the clothes half gives it back, so a
  hat left in the face half (Next, a tab, Finish), or rolled by the summary page's Random while the page is in the face half, is lost or replaced by the stale one. Lifestone gives the held hat back when the page
  is left (and before the create is sent), holds the state's hat again when the face half shows, and a hat cleared on the clothes half stays cleared.

**Town page.** A click picks the start area (town number less one: Holtburg 0, Shoushi 1, Yaraq 2, Sanamar 3, as the CharGen data lists them), highlights the town's button, puts the page
in the town's state and shows the description, a blank line and the how-to line; Sanamar needs the Throne of Destiny and says so without it. The page states of Sanamar (0x10000035) and
Shoushi (0x10000037) are retail's constants; Holtburg's (0x10000034, the heading reads "Holtburg" in the frame) and Yaraq's (0x10000036) are the two that remain (a test pins that each
state's heading is its town's name). The bundled ACE places a new character at the first location of start area 0 (the Holtburg Training Academy, interiors.md §6) whichever town is
chosen: the choice is retail's, kept and sent.

**Summary page.** Opening it fits the profession to the character (`FitTemplateToCharacter`), builds the list (profession, gender, heritage and start town as lines; the attributes with
health, stamina, mana and the skill credits left as pairs; then the specialised, trained, useable untrained and unuseable untrained skills, each in name order with its score), the how-to
text (the sample names of the heritage and gender, `CharGenRules.NamesToken`) and the character, gives the name field the keyboard (the vtable call at slot 21 is `TakeFocus`: the same slot
is `FocusOnShow`'s in `UIElement::OnVisibilityChanged`) and, until a name is typed, fills it with `ID_CharGen_NamePrompt` ("[ Name ]"), selected. The field takes ASCII letters, apostrophes,
spaces and hyphens (`TextFilters.Name`); each typed or deleted character sets the name entered and hands a non-empty text to `CharGenState.SetName` (formatted as `ACCharGenData::FormatName`
does); a text over 32 characters is taken back to the state's name and `ID_CharGen_NameTooLong` says so. **Glitch fixed by default:** retail hands the state a name only when the field is not empty
(`ListenToElementMessage`, 11.4186 0x0047BF40), so after "Bob" is deleted (select all and Delete, or backspacing) the state still holds "Bob" (or "B") and Finish creates a character under a name the empty field
does not show; Lifestone clears the state's name when an edit empties the field, so Finish says there is no name (`ID_CharGen_NoNameWarning`). `--retail-ui-glitches` (`UiRetailGlitches.CharGenEmptyName`) restores retail's. The names of the profession, gender, heritage and town come from four arrays of the
client that the decompile does not show (the data header gives only the first entries, "Custom" and "?"): Lifestone takes the CharGen data's names (and "Custom" for template 0, which the data calls
"Adventurer"), logged as `TODO(retailui-chargen-summary-data)`. **Glitch fixed by default:** retail names only heritages 1 to 4 and leaves "Heritage: " empty for the others;
`--retail-ui-glitches` (`UiRetailGlitches.CharGenSummaryHeritage`) restores it.

**Finish and the create** (`gmCharGenMainUI::DoFinish`, 11.4186 0x004E9170, 11.6096 0x004E9E00). The name is trimmed of "[", "]" and spaces and kept; with no name typed
(or an empty one) `ID_CharGen_NoNameWarning` says so; with attribute credits left (`remainingAtrbCredits`, offset 416 of the state, not the skill credits) `ID_CharGen_CreditWarning` asks first, and yes
finishes without the check; a create already waiting for its answer sends nothing more. Then `CharGenVerifier` checks the character (a character that fails, as one with a heritage the
account lacks, is logged as an error and said on screen in Lifestone's words, never sent) and `IActionApi.CreateCharacter` sends the intent (always exactly 55 skills, `CharacterCreateIntent.TryValidate`).
The answer (F643, `RecvNotice_CharGenVerificationResponse`, 0x004E9030 / 0x004E9CC0) is routed as retail routes it: Ok waits for the list; 3 says `ID_Character_Err_NameReserved`, 4
`ID_Character_Err_NameBanned`, 7 `ID_Character_Err_NameAdminDenied`, and every other result (2, 5 and 6, and any code retail does not name) `ID_Character_Err_NameDBDown`. A request that was not sent
(the offline session without a responder, a full list, no answer in time) is said in Lifestone's words, as a placeholder when the session is offline.
**Logging the new character in (`TODO(retailui-chargen-enter)`, closed).** On success retail finds the new character in its list the next time `Update` runs and logs it in
(`CPlayerSystem::LogOnCharacter`, 11.4186 0x0055F890). Lifestone selects the character, queues character management and sends the same Enter action as the Enter button
(`IActionApi.EnterWorld`, source Ui), so the player's arrival starts the game screen (`RecvNotice_BeginEnterWorld`). An Enter that cannot go (the offline stand-in, a refusal, a timeout)
is said on the character management screen in Lifestone's words (a placeholder when offline). The "Entering World" dialog is not shown for this path
(`TODO(retailui-chargen-enter-dialog)`). Owner confirmation of the behaviour is still pending, and a live create has not been run (it is set aside; the stand-in and the action log are the evidence).
`DoFinish`'s deletion of the window's dialogs and the focus
rules follow the runtime: a dialog's close now hides its window first, as retail's `DialogFactory::DialogDone` (11.4186 0x004773C0) does (taken to be the hide, the virtual at slot 26; `TODO(retailui-runtime)`, logged once when the first dialog closes, until the 11.6096 bytes confirm it), which activates the window behind it, so the
Finish button (it took the keyboard with the click) and the page are active again and a click on the name field gives it the keyboard.

**The 3D character** (`Cg3DViewModel`, `CharGenViewportBinder`; docs/avatars.md §13). Each of the appearance and summary pages makes its own `gmCG3DView`: `Cg3DViewModel` composes the state's
look (`CharGenState.ToLook`, `CharGenAppearance.Compose`) and poses it with a `CreaturePreview`, keeps the camera, the heading (180 degrees) and the heritage's backdrop (its environment Setup,
a single static part), and bumps a version whenever anything the host draws changed. The page hands it to the host (`ICg3DViewHost.Attach`, which `RetailUiLayer` implements): the
`CharGenViewportBinder` makes a `CreatureModeView` the size of the hole, one `MeshInstance3D` per part (meshes from an `ActorMeshLibrary` built for creature-mode lighting, per dress: a part's
texture swaps and modified palette are its `PartAppearance.VariantKey`; variants no part uses any more are released), a camera at the model's position looking along +Y (vertical field of view the game's, 60
by default, near 0.1 m) and creature mode's light: ambient 0.3 and one distant light of intensity 2 towards the CharGen vector (0.3, 1.9, 0.65). The view renders only while its hole shows. The animation (owner
decision D60, which amends D54) is retail's: `gmCG3DView::StartAnimation` (11.6096 0x004EF290) clears the object's sequence and loops the view's animation DID from frame 0 at 30 fps (the human
heritages' 33-frame CharGen animation 0x03000001, a breathing loop of about 3 cm; heritages 12 and 13, the Olthoi, their own, which is also their rest: UIASSET 0x10000011 and 0x10000013, set by
`gmCG3DView::Initialize` 0x004EF3F0), and `StopAnimation` (0x004EF2D0) clears it and holds frame 0 of the rest DID (the human paper-doll pose 0x030003C0) at 0 fps. The pages call them where retail does:
the summary page's `InitializePage` (0x0047BFB0) starts it once (the character is composed, and starts playing, when the page is first shown and updated); the appearance page's `ZoomOut` (0x0047D400) starts it
and `ZoomIn` (0x0047D2B0) stops it, both at once as the zoom begins (the page's zoomed-in flag changes at that moment, not when the 0.6 s camera move ends); the appearance page's `Update` (0x0047ECA0, run when
the page is shown and after a gender or heritage change) starts it when zoomed out and stops it when zoomed in. `SetSelection` (0x0047E610) does not touch the animation: a selection while zoomed out keeps the
loop going. The idle emotes are never played, as in retail. `CreaturePreview.Advance` steps the object each frame (`CPhysicsObj::update_position`: nothing under 1/30 s, one step of the whole elapsed time up to
2 s), so the pose changes with the clock and wraps at the animation's end; a held rest pose bumps no version and costs nothing to step. There is no seam or option: the animation is the only behaviour
(`TODO(retailui-chargen-anim)` is closed). The frames runner pins the loop for a review frame (`Cg3DViewModel.PinAnimation`, not in retail): a frame of a character that plays (the clothes half, the summary) shows the pose
**0.5 s into the loop, frame 15 of the 33**, whatever the script did before it; the face half, zoomed in, shows the rest pose; each frame's log line says which (`pose=`). `--retail-ui-checks` judges the poses: the
zoomed-in face view's is the same half a second later; the clothes view's and the summary view's differ between two times.
**`TODO(retailui-chargen-light)`:** retail's lights are D3D lights, whose direction is the way the light travels, so the CharGen vector (0.3, +1.9, 0.65) is taken to travel along +Y and light the figure's front
(the character faces -Y, to the camera); read as pointing towards the light (as portal space's `TODO(portal)` reads its vector) it would light the back. One constant in the binder
(`LightTravelsAlongVector`); unverified against retail.

**Loud TODOs** (logged once, category `retailui`): `TODO(retailui-chargen-view)` (a host without view data: no character, no colour swatches; also a shade disk drawn for no colour),
`TODO(retailui-chargen-summary-data)`, `TODO(retailui-chargen-enter-dialog)`, `TODO(retailui-chargen-11-6096)`, `TODO(retailui-chargen-light)`.
**An assumption about 11.6096** (`TODO(retailui-chargen-11-6096)`, logged once when character creation opens): `CharGenNames` assumes 11.6096 keeps 11.4186's `CharCase` values (`CASE_UPPER` 0, `CASE_LOWER` 1, `CASE_EITHER` 2; the 11.6096 PDB is not public) and `CharGenState` assumes the `startAtrb` global is initialised to 1 (Strength) as in the 11.4186 data. Both are re-expressed from 11.4186; confirm them in the 11.6096 bytes.

**Checks.** `CharGenViewTests` (the state's look, the composition and the preview for every kind of heritage, the heritages' animation and rest DIDs, the loop and its wrap, the held rest pose and the pinned frame, the new object for a new body, the backdrop, the
cameras), `CharGenAppearanceTests` (the halves and the headgear, the zoom's straight line and end, the turn, the arrows, the wheel's colours and pictures, the shade, gender, the Gear
Knights and the Olthoi, Random, the character the view shows, no view data), `CharGenSummaryTests` (every element and string, the towns and their texts and headings, the list, the sample names, the
name filter and the 32-character limit, Finish's warnings, the create and its answers, the intent's 55 skills and its verification for every heritage and gender, the return to character management), and the
frames: the appearance face and clothes halves, the towns, the summary, the name-in-use message, character management after a successful create, and an Olthoi's appearance and summary (25 frames in all).
The frames runner reads each 3D hole's own picture back (the creature-mode view's texture) and fails when it is flat. Not checked: the light's sign against retail.

## 21. The in-game shell (G4)

The game mode (`gmGamePlayUI`, mode 0x10000008) is the end-of-retail floating-window shell (D36). Each slice of m2-plan §6.6 adds its part below.

### 21.1 The GamePlay root (g1)

**The screen.** `GamePlayScreen` (`Client/RetailUi/Screens/GamePlayScreen.cs`) replaces G6's `PlayGameScreen` and the game-mode stand-in. It is made in every session, live or the offline
stand-in. Its constructor does what `gmGamePlayUI::gmGamePlayUI` (11.6096 0x004EACA0) does first: `ForceDisplayResolution(false)` (`Device::ForceDisplayResolution`, 11.6096 0x0043A990), so
the shell follows the window; then it builds the mode root 0x10000495 of `classic_gameplay` (layout enum 0x10000006, LayoutDesc 0x21000005) at the full window through
`UiElementManager.CreateRootElement` (retail-ui-data §2) and finds the children `gmGamePlayUI::SetupChildren` (11.6096 0x004EAB50) finds. Its destructor (`gmGamePlayUI::~gmGamePlayUI`,
11.6096 0x004EAF30) forces the display again; here the next screen (character management) does it. The screen returns to character management when the server sends the character list again
(a logoff), as `PlayGameScreen` did.

| Window | Id | Property | Place at 1920x1080 | Later slice |
|---|---|---|---|---|
| Root (a Field, z level 1) | 0x10000495 | `Root` | (0,0)-(1919,1079) | |
| SmartBox (`gmSmartBoxUI`) | 0x1000049A | `SmartBox` | (0,0)-(1919,1079), fills the screen | g5 (selection indicator) |
| SmartBox viewport | 0x10000436 | `SmartBoxViewport` | the whole window; the layout hides it, the screen shows it | |
| Spew box (`gmSpewBoxUI`, UI class 0x10000016; its list is 0x10000049) | 0x10000046 | `SpewBox` | (735,20)-(1184,91) | g2 (id confirmed, §21.2) |
| Main chat (`gmMainChatUI`, 11.6096 0x004CDD60) | 0x10000601 | `MainChat` | (0,980)-(409,1079) | g2 |
| Floating chats 1 to 4 | 0x10000505, 0x1000050E-0x10000510 | `FloatingChats` | stacked at the left, hidden until toggled | g2 |
| Stacked vitals (`gmVitalsUI`, 11.6096 0x004C0A60) | 0x100005FA | `StackedVitals` | (880,0)-(1039,57) | g3 |
| Side-by-side vitals | 0x100006D5 | `SideBySideVitals` | (730,0)-(1189,25); hidden for now (`TODO(g4-vitals-layout)`: the layout shows both, they overlap, retail shows one by a setting) | g3 |
| Radar (`gmRadarUI`, 11.6096 0x004D95D0) | 0x100006D2 | `Radar` | (1800,0)-(1919,139) | g3 |
| Toolbar (`gmToolbarUI`, 11.6096 0x004BE550) | 0x10000603 | `Toolbar` | (1610,980)-(1919,1079) | g4 |
| Panel host (the floating panel) | 0x100005FF | `Panel` | (1610,608)-(1919,979) | g4 |

The other windows of the root (the keyboard and admin windows, the indicators 0x10000611, the power bar, the combat panel, the environment panel, the examination window) keep the layout's state
and are not found by id yet.

**The SmartBox and its viewport.** In retail's SmartBox the 3D scene is drawn into the viewport's own surface, above the root's art. Lifestone draws the world behind the UI, so the world
is the SmartBox's picture: the viewport's hole is the whole window and the surfaces around it draw only what they hold. Two deliberate changes to the layout follow from it. The screen shows the
viewport (`TODO(g4-smartbox-viewport)`: which retail function does is not read; `gmSmartBoxUI::PostInit`, 11.6096 0x004D77C0, is the likely one). The root paints an opaque dirt backdrop over
the window that retail's 3D view covers; the screen clears the root's image (`UiElement.ClearImage`), or the shell would hide the world. A press on the hole goes to the world (G5 picking); a press
on a window does not (`GamePlayScreenTests`). **Portal space** draws in that viewport: `PortalTunnelViewportSource` (Game/PortalSpace; `Main` registers it through
`RetailUiLayer.RegisterViewportSource(0x10000436, ...)`) gives `RetailViewportHost` the tunnel's texture while the tunnel shows, and the host draws it in the hole, as a child of the viewport's
surface node when it has one, else behind the surfaces. While the hole shows, the tunnel layer's own full-window picture is hidden (`PortalTunnelView.DrawnByViewport`), so the tunnel is
drawn once; with no shell (explore mode, the login screens) the layer draws it as before. The view still follows the window, not the hole (`TODO(g4-tunnel-hole)`; the same thing while the hole is the
window).

**`GameViewRect`** is the SmartBox's window rectangle (`gmSmartBoxUI::RecvNotice_UpdateGameView`, 11.6096 0x004D6F20) in window pixels while the shell is up (`GamePlayScreen.GameViewBox`
times the scale; an edge on the UI's edge is the window's edge, since a window is not always a whole number of UI pixels), else the whole window. The SmartBox fills the screen, so it is the
whole window and `WorldLens` applies no offset (D40 as amended); a SmartBox that did not fill it would move the rectangle, and the hook is there.

**The shell is composited before the world shows.** Retail builds the game screen before it draws. `UIFlow::UseNewMode` closes every dialog with the old screen, so the "Entering World" dialog
is gone in the frame the shell is made, and the 1 ms composite slice would paint a 1920x1080 shell over several frames while the world (no longer hidden by a forced display) showed bare. Two things
prevent it. (1) **The hold:** while the current screen is a `GamePlayScreen` that is not yet painted (`ShellPainted`, set by `RetailUiLayer` when the root shows and nothing is left to paint or
copy; `ShellHoldsWorld`), the layer keeps the world hidden behind its black backdrop (`WantsWorldHidden` stays true) and paints with a 3 ms slice and uploads tiles in a 3 ms slice (the frame that builds the shell does neither; §21.10); it gives up after 800 frames, loudly
(`TODO(g4-shell-paint)`). No frame shows the world without the shell (checked per frame). (2) **The warm-up** (`ShellWarmUp`, Game/RetailUi): in play mode, from the first login screen on (§21.10; it began at character management before live-gate fix 3), a second element runtime builds and paints the gameplay root once on the side at the real scale through the layer's own image source, so the images are decoded
and cached and the glyphs made; nothing is drawn, no sound plays. It registers the same element classes as the real shell (`GamePlayScreen.RegisterClasses`, so it builds the same tree and logs no false "no runtime" TODO, R7) and stops when the mode leaves the login screens (R14). Measured by `--play-checks` at 1280x720: without it the first shell waits 435 ms (88 frames, the images decoding on workers) and
the arrival's portal space plays under the backdrop; with it the wait is about 30 ms (3 frames), and a second entry after a logoff about 20 ms. The offline stand-in session (no world to enter) does not warm.

**Placeholders.** Every window above is still without content. The game screen is an `IUiPlaceholderScreen`: its note (the host's line at the top left, `PlaceholderNote`) names the windows
without content ("the in-game shell's spew box, chat, vitals, radar, toolbar, panels, indicators, selection have no content yet"), and one line is logged once (`retailui`, a warning,
`TODO(g4-shell)`). A slice that fills a window calls `GamePlayScreen.MarkWindowBuilt("<name>")` (the names are `GamePlayScreen.InitialPlaceholders`), which takes it off the note. The windows
keep retail's art from the layout meanwhile. G6's status line (`PlayStatusLine`) stays until g2.

**Checks.** `[uicheck]` (12 new, `--retail-ui-checks`): the game mode is the shell with the display released and no frame shows the world without it; at 1920x1080 the root and the SmartBox fill the screen, the
radar, main chat, toolbar and stacked vitals sit at the places above; the root's backdrop is not painted; the viewport hole is the whole window and shows; `GameViewRect` is the whole window; a source for
the viewport is told the hole and drawn across it; the note names the placeholders (frame: `cache/retail-ui-checks/gameplay-1920x1080.png`). `--play-checks` (3 new): the game screen is the shell with its
windows, shown over the world; it was composited before the world showed (no bare-world frame); portal space draws in the SmartBox viewport. Client tests: `GamePlayScreenTests`. Review frames of a play-checks
run: `--play-checks --play-frames[=DIR]` (§21.9; the read-back is slow, so that run is not a timing run).

### 21.2 The spew box and the chat windows (g2)

Code: `Client/RetailUi/Chat/` (`GameSpewBox`, `GameChatWindow`, `GameChat`, `ChatTextTypes`); `GamePlayScreen.Chat` makes them and registers them on the router
(`UiLoginContext.Notices`, the host's `UiNoticeRouter`), so every notice goes through §11's router: text type 26 to the spew box, every other type to the chat windows.
**Each world entry starts the chat empty** (R1): retail's chat log is an element of `gmGamePlayUI` and dies with it, so the shell replays only the lines since the session entered `EnteringWorld` (`ChatLog.EntryMark`, set by the reducer; `GameChat.EntryLines`), not the whole session log (the log itself keeps its history for the scenarios). Test: `ReducerChatTests`.
Tests: `GameChatTests`. The exe reads behind the masks and colours are docs/research/retail-addresses-m2.md section 1.5 (D55).

**The spew box** (`gmSpewBoxUI`, 11.6096 0x004D6710 for `PostInit`, 0x004D6D00 for the notice). Window 0x10000046 is the element of UI class 0x10000016 (the class `GetUIElementType` returns), a child
of the SmartBox, and holds the list 0x10000049; `TODO(g4-spew-id)` is closed. It keeps text type 26 only and queues the string; each frame (global message 3) it makes one row per queued string from element
0x1000004A of layout enum 0x10000012, sets the trimmed text, sizes the row to the list's width and then to its text's height, **deletes the top row when its text is identical** (so a repeated notice replaces
rather than adds), inserts the new row at index 0 (newest on top), deletes rows past the **maximum** (master property 0x10000028 of the list; 4 in the data; 1 without it), and scrolls to the top. An element
message 0x10000003 from a row deletes it (the layout's media send it; not seen in the stand-in's runs yet). The window and its list are made mouse-invisible so the world below stays clickable (retail's two calls
in `PostInit` read as that; unverified). **The portal-space notice:** the portal director posts "In Portal Space - Please Wait..." at the start of the tunnel and at each roll segment; the D32 placeholder line
(`PortalNoticeLine`) now only shows when no shell is registered (explore mode): its `Shell` hook sends the notice through the router and hides the line while the spew box is registered. The first post of an
arrival comes before the shell exists (the tunnel starts about 100 ms before the game screen is painted), so the line shows it for that moment and the spew box takes the next roll segment's. `PlayStatusLine`
(the stage text) is hidden by play mode while the shell is up and still shows over the login screens.

**The chat windows** (`ChatInterface`, 11.6096 0x004F4A30; `gmMainChatUI`, `gmFloatyChatUI`). The main chat 0x10000601 and the four floating chats (0x10000505, 0x1000050E-0x10000510, hidden until toggled; how
they are toggled is not built) each have a log (the text 0x10000011 and its scrollbar), an entry (the text 0x10000016), the "new text" indicator (the button 0x1000048C), a Send button (0x10000019) and a
window id (property 0x1000007E: 1 and 8 the main chat, 2 to 5 the floating chats). A window shows a line when its **text-type mask** has the line's type: ids 1 and 8 every type but 26, id 2 speech, tells and
emotes (types 2, 3, 4, 12), id 3 types 10, 11 and 18, id 4 type 19, id 5 types 27 to 30. Lines are coloured by type from the exe's table (the log's property 0x1B, built in code; `ChatTextTypes.ColorOf`). A new
line starts with a line break (not the first); the log is cut back to 7,500 glyphs at a line break once it passes 10,000; the view follows new lines while it is at the end, else the indicator lights. "At the end" is `UIElement_Text::IsAtVerticalEnd` (11.6096 0x004695E0, 11.4186 0x00469350): the LAST GLYPH is in view (`IsPositionInView` at count - 1; an empty text is at its end), not "the paper's bottom is in view". The port first used the paper test, which is false right after the chat's own line break is appended (an empty line opens below the view), so a log stopped following after its first page and the indicator came up (M3 3.1 fix B, found in the fight's chat); `UiTextElement.IsAtVerticalEnd` is fixed. The order in `GameChatWindow.Append` already matched `ChatInterface::RecvNotice_DisplayFinalStringInfo` (break, `IsAtVerticalEnd`, text, truncate, `ScrollToPosition(count)` or the indicator). Tests: `GameChatTests.A_log_filled_past_one_page_while_at_the_end_keeps_following_new_lines` and `A_log_scrolled_away_from_the_end_keeps_its_place_and_shows_the_indicator`.
**Not built:** the idle/active opacity fade (defaults 0.5 and 1.0 are read; the runtime has no element opacity: `TODO(g4-chat-opacity)`), the history and reply keys, the chat font setting, the saved masks of the
options panel (`TODO(g4-chat-options)`), toggling the floating windows, resizing and the maximise button.

**The session's lines.** `GameChat` shows every `ChatReceived` line (and those already in `GameState.Chat` when the shell is made) through `ChatLineText`: the type is the server's chat type (0 to 33; anything else
is system text), speech and tells are composed as English literals ("Name says, ...", "You say, ...": retail composes them from its string table, `TODO(g4-chat-format)`), emotes use type 12. A line
of the mod's machine channel (`#lifestone/1`, never shown; the state track keeps its JSON out of the chat already) is dropped here as well. Type 26 goes to the spew box. A player's brackets stay (chat text is
not parsed as the string meta-language).

**The entry.** Enter in the entry (`UiTextElement.AcceptInput`, raised by the Accept action) or a click on Send runs `ProcessCommand` (`ChatInterface::ProcessCommand`, 11.4186 0x004F5100): an empty entry
does nothing; else the line joins a history of 100 and the entry clears. `GameChat.Submit` sends plain text and `@` server commands as `Actions.Talk(text, ActionSource.Ui)` (retail's `OnChatCommand` ends in
`DoSay` and `CM_Communication::Event_Talk`); a Talk the action layer refuses (offline, not in the world) is reported in the chat. A `/` command is not parsed: it is answered with a labelled line and one log
warning, `TODO(g4-chat-commands)` (logged once, without the command's text). The stand-in server (`StandInWorld`) hears plain Talk back as the player's own speech, as ACE echoes it, so the Talk completes; `StandInVisit.Speech` lets a visit script what others say.

**The talk-focus menu** (retail has no channel tabs). The popup menu 0x10000014 holds one row per focus in retail's order (`gmMainChatUI::InitTalkFocusMenu`, 11.6096 0x004CE8C0): a squelch row, then Monarch
(5), Selected (2), Patron (4), All (1), Vassals (6), Fellows (3), Allegiance (7), General (8), Trade (9), LFG (10), Roleplay (11), Society (12), Olthoi (13); the focus is in each row's property 0x1000000B and the
texts are `ID_Chat_*` of the game string table. M2 honours All only: another choice answers `NotInM2` ("Chat to <focus>") and the menu goes back to All (`TODO(g4-chat-focus)`; the selected-target tracking and the
button text swap of `HandleSelection`, 11.6096 0x004CE1B0, are not built).

**Overlap with the vitals.** The spew rows are drawn at the box's top (y 20 to 91 at 1920x1080), and the stacked vitals window (880,0)-(1039,57) draws over the first rows; with several rows the lower ones show below it. The layout's own anchors put them there (the window at (735,20) and the vitals at (880,0), both from the layout's data; the vitals' saved place, which retail applies over the layout, is not built: `TODO(g4-vitals-place)`), so the overlap is retail's and stays (g3a). At 800x600 (review R18) the spew box is (175,20)-(624,91) and the stacked vitals (320,0)-(479,57): the same overlap of the box's first rows, nothing else touches (the radar starts at x 680, the main chat ends at x 409 and the toolbar starts at x 490); it is the layout's own, checked by `At_800_by_600_the_chat_and_the_toolbar_leave_a_gap_and_the_vitals_cover_the_first_rows_of_the_spew_box_as_the_layout_has_it`. The ghost glyphs the g2 review frame showed where rows moved are fixed (21.3).

**Checks.** `--play-checks` (3 new, 61): the spew box shows the portal-space notice while the tunnel plays and no chat window does; a stand-in speech line reaches the main chat (`StandInVisit.Speech`); a typed line is
sent as Talk and answered ("You say, ..."). Review frame of a `--play-frames` run: `03-shell-spew-and-chat.png` (the check posts four spew notices for it). `--retail-ui-checks`: the placeholder note
no longer names the chat. Client tests: `GameChatTests` (13) and one stand-in test.

### 21.3 Spew ghost and the vitals (g3a)

**The ghost** (`TODO(g4-spew-ghost)`, closed). The spew box (0x10000046) is a window with a surface of its own and no art: its rows are bare glyphs over a transparent surface. `UIRegion::DrawHere` (11.6096 0x006A08B0) erases a region's
dirty pieces only when the element's property 0x51 (`ElementShouldEraseBackground`) is set (`UIRegion::EraseSelf` 0x0069FFE0), and the layout sets it on neither the window nor its list, so a repaint blended the new glyphs over the old
(rows that moved or were replaced). `GameSpewBox` now sets the flag on its window (`UiElement.SetShouldEraseBackground`, `UIRegion::SetShouldEraseBackground` 11.6096 0x00464A80), so each dirty piece is cleared to transparent first. The compositor stays
exactly retail's (existing tests pin that a non-erasing element's pixels stay). Retail's own mechanism is not found: `gmSpewBoxUI::PostInit` (11.6096 0x004D6710) makes two virtual calls after `UIElement::PostInit` (slot 2 with 1 is `SetVisible`, the
slot `gmFloatyVitalsUI::UpdateFromPlayerModule` calls with its window's visibility; slot 22 with 0 is probably the click-through the box already has), neither an erase, so why retail does not ghost is open (`TODO(g4-erase-exe)`). The test
(`GameSpewGhostTests`) plays four notices into the real shell and compares the incrementally painted surfaces with the same scene painted whole; it failed before the fix, at a pixel inside the spew box's surface.

**The shell root erases too** (G7 ghost fix). The same class of ghost showed on the root itself: the tools panel (0x7F000100, a child of the shell's root 0x10000495 with no surface of its own) stayed painted after F7 closed it, though `IsOpen` and `IsShown` were false. The root has no art (g1 cleared its backdrop, §21.1) and does not erase, so hiding a child dirtied its box (`UIRegion::SetVisible` calls `MakeRootDirtyHere`, which was right) and the compositor repainted that box over the old pixels: the hidden child draws nothing, and nothing cleared the piece. The inspector, a root of its own, never ghosted. `GamePlayScreen` now sets `SetShouldEraseBackground(true)` on the root, so each dirty piece is cleared to transparent and the visible children are drawn again over it; the compositor is untouched (the tests that pin a non-erasing opaque surface stay green). Floating windows parented to the root that paint into its surface (the tools panel; any later one) are covered; the panel host, the options and character pages (inside the host) and the floating chats did not ghost before the fix (the test showed no difference; why was not traced), and `ShellHideGhostTests` checks all of them against a whole paint. The retail cause stays open (`TODO(g4-erase-exe)`).

**The vitals** (D16; `GameVitals`, `VitalsWindow`; `gmVitalsUI`, 11.6096 0x004C0AB0). Each floaty window (stacked 0x100005FA, side by side 0x100006D5) holds the meters 0x100000E6 (health), 0x100000EC (stamina), 0x100000EE (mana) and the labels
0x100000EB, 0x100000ED, 0x100000EF. `Update` sets each meter's position (property 0x69 directly: retail's `Update` does not use the smooth goal 0x66, so there is no smoothing) to current over maximum and its label to the string
`ID_Vitals_VitalBarLabel` (table enum 0x10000001; it renders as "cur/max") with the variables `ID_Cur_0` and `ID_Max_0` (the hashes of "CUR" and "MAX"; the research's names `Cur_0` and `Max_0` are the globals' names, not the variables'). It does
nothing until the PlayerDescription has arrived, and runs for every `QualitiesChanged` (`OnQualityChanged` 0x004C0E70, `RecvNotice_PlayerDescReceived` 0x004C0E80); both windows update, the hidden one too. A vital's maximum is
`PlayerQualities.TryGetVitalLevels`, retail's `CACQualities::InqAttribute2nd` (11.6096 0x005927F0 / 0x00592D20): the vital's start and ranks plus the secondary-attribute formula (health half of Endurance, rounded up as char gen's `HealthOf` does; stamina Endurance; mana Self) over the attributes **with their enchantments applied** (`InqAttribute2ndBaseLevel` 0x00592A20 asks `InqAttribute` with `raw` false), plus PropertyInt 379 (`0x17B`) for health, and then the enchantment registry's modifiers for the vital itself. Checked against a live ACE: a fresh level-1 test character (Endurance and Self 55, vital ranks 0) has currents 28, 55, 55 at login, which is the formula with no enchantments; the live gate's buffed one (raw attributes 55, currents 50, 100, 100) is the formula over Endurance and Self enchanted to 100. The current value is not enchanted.

**Enchantments** (`EnchantmentRules`; `PlayerQualities.Enchantments` keeps the description's registry in wire order: multiplicative, additive, cooldown, vitae). The rule is `CEnchantmentRegistry::EnchantAttribute` (11.6096 0x005953E0) and `EnchantAttribute2nd` (0x005954E0): the multiplicative list, then the additive list, are culled for the stat (`CullEnchantmentsFromList` 0x005952E0: the type has the kind's bit, attribute 0x1 or secondary attribute 0x2, and names several stats (0x2000) or its key equals the stat's); the culled ones duel by spell category (`CEnchantmentRegistry::Duel` 0x00595120, `Enchantment::Duel` 0x005CBC10: the higher power level stays, at equal power the one that started later), so layered duplicates of one spell and two spells of one category count once; the survivors apply in order (`Enchantment::Enchant` 0x005CBC40: additive adds, else multiplicative multiplies); the vitae applies first to a secondary attribute; the result is kept at 1 or at the floor (attribute 10, secondary attribute 5; the base decides which) and rounded half up. Client tests (`EnchantmentRulesTests`): the category rule with layered duplicates and two categories, additive and multiplicative order, ties, the vitae, and a vital at 28 with a +22 buff = 50. The messages that change the registry after the description are decoded and applied (M3 1.2 s1; `TODO(g4-enchantment-updates)` is closed), so a buff cast or ended in the world shows at once. Closed in M3 2.2: `TODO(g4-quality-filter)` (a1: the portal's per-vital enchantable table, 0x1000000C, is read from the dat as the quality filter 0x0E010001 and applied in `EnchantAttribute2nd`, §23.2) and `TODO(g4-enchantment-affects-skills)` (a2: `EnchantmentRules.AffectsKey` matches the attack and defence skill groups by the type bits 0x10000 and 0x20000 and the skill lists of `Enchantment::AffectsAttackSkills` and `AffectsDefenseSkills`, §23.2). A zero maximum shows an empty meter where retail would divide by it. The registry's start time is the wire's start time plus the arrival time (`Enchantment::UnPack` 0x005CBF70 adds the clock); the duel compares those. A timed enchantment replaces the timed entry of the same spell at any layer (`AttemptToReplaceSpellInList` 0x00594B70). Retail prints the expiry line itself, in the client (`NotifyOfEnchantmentRemoval` 0x00569460: " penalty" for the vitae, spells below 0x8000, and 02C3 and 02C5 only); Lifestone carries `EnchantmentChanged.Notify` and leaves the line to a layer with the spell table.

**The window choice.** `UpdateFromPlayerModule` of each wrapper (stacked 0x004CFD50, side 0x004D15C0) shows its own window by `PlayerModule::SideBySideVitals` (0x005D4070: bit 21, 0x200000, of `PlayerModule::options_`): the stacked window when
the bit is clear, the side-by-side window when it is set. `options_` is the PlayerDescription's first options word (`PlayerDescriptionMsg.Options1`, now kept as `PlayerQualities.CharacterOptions1`). Until the description arrives the stacked window shows.
The choice is logged (info, once per window: "the character's options 0x... choose the stacked (0x100005FA) window"). **What ACE stores:** ACE's default `CharacterOptions1` for a new character is 0x50C4A54A (the value the net project's wire writers
already use), whose bit 21 is clear, so a character that never changed the option gets the stacked window; the stand-in uses that default (`StandInPlayerStats.AceDefaultOptions1`). What a test character has stored shows in the log line at login.
Not built (`TODO(g4-vitals-place)`): each window's saved place, size and lock state (options 0x10000080 to 0x10000089, `UpdateLockedStatus`).

**The reducer's vital key.** A PrivateUpdateAttribute2ndLevel names the vital by its current's secondary attribute (2 health, 4 stamina, 6 mana, the ids `gmVitalsUI::Update` reads the current value by); the state keeps a vital under its maximum's
id (1, 3, 5: the description's and PrivateUpdateVital's key). The reducer now maps 2, 4 and 6 to 1, 3 and 5, so a current update reaches the record the meters read (it had been stored under a key nothing read).

**The stand-in.** `StandInVisit.Stats` (`StandInPlayerStats`, on by default) sends a PlayerDescription as the player is created: Endurance 120, Self 100, health ranks 5, so health 52 of 65, stamina 120 of 120, mana 75 of 100 (Lifestone-authored
placeholders). `StandInVisit.VitalUpdates` sends current-value updates after the unhide; `--play-checks` sets health to 41.

**Checks.** Client tests (`GameVitalsTests` 5, `GameSpewGhostTests` 1): the option bit and ACE's default, the maximum's formula, the meters and labels from a scripted description and a vital update (and skipped until known), both values of the option
choosing the window. `--play-checks` (2 new, 63): the vitals at the stand-in's values in the stacked window with no update yet; the health update to 41 of 65 on the meter and the label. `--retail-ui-checks` (67): the placeholder note no longer names
the vitals. Review frame (gitignored, `--play-frames` run at 1920x1080): `cache/g4-frames/g3a/03-shell-spew-and-chat.png`.

### 21.4 The radar (g3b)

**The window.** 0x100006D2 is class 0x10000010 (`gmRadarUI`, registered by `gmRadarUI::Register`, 11.6096 0x004D97E0): a 120x140 window that owns its surface, with the ring art and the cone (field 0x1000003F, 120x120), the
four compass tokens (fields 0x10000040 north, 0x10000041 east, 0x10000042 south, 0x10000043 west), the lock button 0x10000619, the drag button 0x100006A3 (hidden by `PostInit` 0x004DA990) and a text 0x1000003E. Its properties:
radius 50 (0x1000002D) and the centre struct (0x1000002E: 0x1000002F = 60, 0x10000030 = 60). Blips are not images: retail draws single pixels into the surface. `UiRadarElement` is the class; the compositor gained one hook,
`IUiRegionView.DrawOverChildren`, called after a region's children (`gmRadarUI::DrawChildren` 0x004DA380 draws the children, then `DrawObjects`, then the marker). `GameRadar` (`Client/RetailUi/Radar/`, `GamePlayScreen.RadarUi`)
is the behaviour; `RadarRules`, `RadarPalette` and `RadarShapes` hold retail's rules with no UI in them.

**Update.** `UseTime` (0x004DA500): every **0.025 s** of the UI clock (D55 read, docs/research/retail-addresses-m2.md section 1.6; the next due time is the current time plus the interval), and only with the player present, it sets the
range (`CPlayerSystem::GetRadarRadius` 0x0055E5A0: **75 m** when the player's cell is a land cell, **25 m** otherwise), turns the compass tokens (`UpdateCompassTokens` 0x004D9CC0: when the heading changed, each token goes to
(sin a * orbit + centre x, cos a * orbit + centre y) less half its size, a = heading in radians plus 180, 90, 0 and 270 degrees for north, east, south, west; the orbit is the distance from the centre to the token's centre in the
layout; so the radar is player-up and the letters rotate), collects the blips and marks the radar dirty (retail marks it on every update; `GameRadar` only when a blip, the heading's tokens or the selection changed, which paints the same
pixels). The player's position is the session's last server position, or the live physics body's when the play mode gives it (`PlayMode`).

**Which objects.** `AddObject` (0x004DA660) tracks an object that is not the player, is not hidden from the UI (object description bit 0x80), has a radar behaviour of 2, 3 or 4 (`InqShowableOnRadar` 0x0058D080; a description
that sent none has 0 and is never shown) and has a shape. `DrawObjects` (0x004D9FE0) then drops one with no position, farther than the range less one metre (a squared test, "greater or equal" drops it) or landing outside the
radar's pixel square. The pixel: the object's position in the player's frame (`SmartBox::convert_to_player_space` 0x00452DE0, x right, y forward), x = (int)(x * radius / range + centre x), y = (int)(centre y - y * radius / range):
50 / 75 pixels a metre outdoors, 2 indoors. **Intensity:** 1.0 when the object is under 5 m above or below the player, else 0.65 (red, green and blue scaled as floats, alpha kept). The selected object's blip also gets `DrawSelected`
(0x004D8C40).

**Shapes** (offsets from the blip's pixel; `GetBlipShape` 0x004D87C0 gives the shape, the draw routines at 0x004D8890-0x004D90D0 the pixels): cross (4) = the point and its four edges, for everything but the cases below; X (3) = the point and the four
diagonals, when both the object and the player are player killers (or both PK-Lite); hollow (2) = the eight neighbours without the point, an allegiance member; triangle (5) = the point and the three pixels under it, a fellowship's
leader; inverted triangle (6) = the point and the three over it, a fellowship's member; point (1) and X-in-box (7, hollow plus four pixels at 2,2) are never returned. The selection mark is a square outline 7 pixels across (four bars,
20 pixels). The player's own marker is bright green: the centre, its four edges and the four pixels two out (a plus with a two-pixel arm), drawn last.

**Colours** (`GetBlipColor` 0x004D8350; the floats are 11.6096's, read with D55): a hidden object is white; a server blip-colour byte 1 to 10 picks blue, gold, white, purple, red, pink, green, yellow, cyan or bright green (any other
non-zero byte is the default white); else a portal is purple, a vendor yellow, an attackable creature that is not a player gold, any other non-player white; a player is white, cyan for an admin (unless a hidden admin), red for a player killer,
pink for PK-Lite, gold for a free-PK; a fellowship's leader and members are bright green over all that. So NPCs and life stones have no colour of their own in the client: the server sends their byte (ACE sets yellow for NPCs, blue for life
stones). The bits are the PublicWeenieDesc's (the names ACE gives them are the same); `WorldObject` offers them as plain values (`DescriptionFlags`, `ItemType`, `RadarBlipColor`, `RadarBehavior`) because the retail UI may not name the net layer.

**Not built (named TODOs):** `g4-radar-tooltip` (the name of the blip within 6 pixels of the mouse), `g4-radar-coords` (the coordinate fields, `UpdateCoordinates` 0x004D98E0, the option `CoordinatesOnRadar`), `g4-radar-lock` (the lock button's
state, `UpdateLockedStatus` 0x004D82E0), `g4-radar-creature-slot` (the creature test reads the ItemType where retail calls a virtual of the weenie object that was not read), fellowship and allegiance (`IRadarRelations.None`: the session keeps
neither). The "radar blank" client flag is `GameRadar.Blank` (nothing sets it).

**The stand-in.** Its objects now carry what the radar reads (`StandInObject.DescriptionFlags`, `BlipColor`; every one has behaviour 4, show always, and a portal the portal bit; placeholders, the stand-in's weenies carry no data). The
play checks' visit adds five far objects south of the player (a creature, a vendor, a life stone, a player killer, a player) so the radar shows every colour.

**Checks.** Client tests (32: `RadarRulesTests` 26, `GameRadarTests` 6 with dats, and the placeholder-note tests changed): the range outdoors and indoors, the cut at the range less one metre, the pixel of a blip for headings 0 and 90 and across
a landblock seam, the intensity, the colour and shape of an NPC, creature, portal, life stone, vendor, player, player killer, PK-Lite, admin and fellowship members, the shapes' pixels, the palette, the window, the compass tokens at heading 90, the
selection mark, the surface's pixels, and the 25 ms interval. `--play-checks` (1 new, 64): the stand-in portal's blip is a purple cross two pixels left of the centre (and the guard's gold one two pixels right). Review frame (gitignored, `LIFESTONE_PLAY_FRAMES` run at
1920x1080): `cache/g4-frames/g3b/03-shell-spew-and-chat.png`, and the radar at 4x: `04-radar-4x.png`.

### 21.5 The toolbar, the panel host and the indicators (g4)

**The panel host** (`GamePanelHost`, `Client/RetailUi/Panels/`; window 0x100005FF, `gmFloatyPanelUI`) has sixteen pages, children of the page frame 0x10000180. Each carries its panel id (property 0x10000029) and its toggle
action (0x57). `gmPanelUI::SetupChildren` (11.6096 0x004BD670) finds them and hides each; `gmPanelUI::PostInit` (0x004BDCA0) shows the window only when a panel is shown (`m_currentlyShownPanel`), so **the host is
hidden at login** (the layout shows every page stacked, which is why the earlier frames showed the last, a spellbook-like page). A page opens when a toolbar button (or a key) fires its action; retail's page then
toggles itself and tells the shell (`ListenToElementMessage` 0x004BD300, `RecvNotice_SetPanelVisibility` 0x004BD380, which `SetPanelVisibility` re-expresses: one page at a time, an overlay page remembering the page
below it, property 0x10000049). **Deviation:** Lifestone registers the host window for the pages' actions in the pages' place (`UnregisterElementForAllInputActions` on each page), because a tabbed page (a `UiPanel`)
shows itself again whenever it is hidden, so hiding a page that must not open looped without end. The host answers the action by the page's kind:

| Panel id: page | Element | Opened by | M2 (M3 2.2: the six Built pages are marked) |
|---|---|---|---|
| 7 inventory | 0x1000018B | toolbar 0x100001B1 (action 0x19) | **Built in M3 2.2** (§23.4, §23.5); in M2 NeedsGameplay: "Inventory, backpack and paperdoll" |
| 12 social | 0x1000018F | 0x10000197 (0x0D) | "Fellowship, allegiance and friends" (now "Not in Lifestone M3") |
| 11 skill | 0x1000018E | 0x10000199 (0x13) | **Built in M3 2.2** (§23.2); in M2 "Skills and attributes" |
| 25 quest | 0x10000559 | 0x1000055A (0x10000127) | "Contracts, journal and quests" (now "Not in Lifestone M3") |
| 16 map | 0x1000018C | 0x1000019A (0x16) | "Map" (now "Not in Lifestone M3") |
| 13 spell | 0x10000190 | 0x10000198 (0x10) | **Built in M3 2.2** (§23.6); in M2 "Spellbook and components" |
| 2 book, 4/5 effects, 1 abuse, 14 assistance, 15 vitae, 8 net, 9 game | 0x10000182, 0x10000184/5, 0x10000181, 0x10000189, 0x1000018A, 0x10000187, 0x10000188 | keys, the indicators' clicks | NeedsGameplay, named in `GamePanelHost.Pages`; **4, 5 and 15 Built in M3 2.2** (§23.3) |
| 10 options | 0x1000018D | 0x1000019B (0x1A) | hook for g6 (`SetPageHandler(10, ...)`); until then a logged `TODO(g6-options)`, no message |
| 3 character | 0x10000183 | key action 0x05, the burden indicator | hook for g6 (`SetPageHandler(3, ...)`, `TODO(g6-character-info)`) |

A NeedsGameplay page opens the retail message dialog ("Not in Lifestone M2: <panel>", `NotInM2`, §11), logged as a placeholder. The names are Lifestone's English; the pages' contents (spellbook, inventory, ...) are the
gameplay milestone's. The same action closes a page g6 has shown. g6 shows its page with `SetPanelVisibility(panelId, true)`.

**M3 2.2 (§23.1).** Opening a page now answers by what has a controller: a page with a `GamePanels` controller is Built (the host's handler shows it with `SetPanelVisibility`), and a page with none shows the retail message dialog "Not in Lifestone M3: <panel>" (`NotInM3`, §11), logged as a placeholder. Built by M3 2.2: pages 11, 4, 5, 15, 7 and 13; the options page (10) and character information (3) are Built by their g6 handlers; the rest (social, quest, map, book, abuse, assistance, net, game) answer "Not in Lifestone M3". The toolbar's Examine button answers it too; its four combat-mode buttons stopped answering it in M3 3.2 (§23.8).

**The toolbar** (`GameToolbar`, window 0x10000603, `gmFloatyToolbarUI`). The seven panel buttons (0x10000197 social, 0x10000198 spells, 0x10000199 skills, 0x1000055A quests, 0x1000019A map, 0x1000019B options, 0x100001B1
inventory) have no handler in retail: each is a button whose click action (property 0x12) is its page's toggle action, so they work through the host above. The action buttons are heard by `gmToolbarUI::ListenToElementMessage`
(0x004BFB20): the four combat-mode buttons 0x10000192-0x10000195 (`ToggleCombatMode`; the layout shows the one for the current mode), Examine 0x100001A5 (`ExamineObject` or a target mode) and Use 0x1000019D
(`UseObject` or a target mode): since M3 3.2 a press of a combat button is `ClientCombatSystem::ToggleCombatMode` (11.6096 0x0056D610: from non-combat it asks `IActionApi.SetCombatMode(ActionSource.Ui)` for `GetDefaultCombatMode`'s mode, 0x0056C050, from any other mode for non-combat; §23.8), and the layout shows the button of the current mode (`gmToolbarUI::RecvNotice_SetCombatMode` 0x004BE2A0, live on `CombatModeChanged`); Examine still opens NeedsGameplay ("Examination", now the retail message "Not in Lifestone M3"); Use uses the selected object through the action layer, as the double click does, and does nothing without a selection (R10). The selection display, the stack slider
and the 18 shortcut slots (class 0x10000031) wait for selection (g5) and for items; they keep the layout's art. The environment panel (0x100005FD) is hidden by the layout and stays so
(retail shows it for the environment; nothing in M2 does). The power bar this paragraph first called 0x10000613 is `gmPowerbarUI`, window 0x10000044, built in M3 3.2 (§23.8): hidden until an attack or a jump begins it, and nothing in M3 begins it.

**The indicators** (`GameIndicators`, window 0x10000611, `gmFloatyIndicatorsUI`) hold six buttons by class: 0x100000F8 link status (class 0x10000003, action 0x09 -> the net page), 0x100000F5/6 effects (0x10000002, ghosted),
0x100000F4 vitae (0x10000006), 0x100000F7 burden (0x10000001, action 0x05) and 0x100000F3 mini-game (0x10000004). Only the link indicator has content (`UiLinkStatusIndicator`, registered for class 0x10000003 as
`gmUIElement_LinkStatusIndicator::Register`, 11.6096 0x004E73A0; a `UiButton`): `PostInit` (0x004E7360) enters link state 1 (element state 17); `UseTime` (0x004E7450) reads the link when more than 4 seconds passed and,
in link state 3, flashes between states 18 and 19 every 0.75 s; `UpdateLinkState` (0x004E73C0) chooses the link state from the seconds since the last server packet: connected and at most 5 s is 1 (state 17), at most 20 s is
2 (18), at most 40 s is 3 (19), else, or not connected, 4 (20); `SetLinkState` (0x004E7270) shows element state 16 plus the link state, whose images are the layout's. The thresholds are doubles and the interval is
4 s (read from the exe, D55; docs/research/retail-addresses-m2.md §1.7). The reading comes from `IGameSessionView.SecondsSinceLastServerPacket` (a plain accessor over the network layer's `LinkStats.LastReceivedTimestamp`;
the retail UI may not name `Lifestone.Net`): the time since the last admitted datagram. An offline stand-in session has no link to measure and reads as connected and just heard (`TODO(g4-link-offline)`, placeholder). Not built (named
TODOs): the tooltip with packet loss and ping (`g4-link-units`, `ID_LinkStatus_*`), the 15 s no-drop-kick cap (`g4-link-nodrop`), the mini-game indicator (`g4-indicators-gameplay`; it keeps its ghosted layout art). The effects, vitae and burden indicators are built (M3 2.2 a3, §23.3): `UiStateIndicator` buttons lit from the enchantment registry and the load. A click on an indicator fires its action, and the host answers it.

**Placeholder note.** "toolbar", "panels" and "indicators" are off the note; "selection" (g5) was the one left (g5b built it: §21.7).

**Checks.** Client tests (`RetailUi/Shell/GameShellG4Tests`, 30 cases): the host hidden at login with every page hidden, each gameplay panel button and each combat/use/examine button opens the message naming its panel
(table-driven, through the mouse path), OK closes it and the button opens it again, a press after the mouse rested on a tabbed page's button ends (the loop regression), the options button's hook and the page it shows,
overlay pages, every panel button names a page and every page has its action, and the link indicator at 0, 6, 25 and 45 s (states 17, 18, 19, 20), the flash, and the thresholds. `--play-checks` (4 new): the host is
hidden and the shell has no toolbar/panels/indicators placeholder; the inventory button opens the message and OK closes it; a combat button opens it; the link indicator with the stand-in (17) and with 25 s and 45 s of
silence. The check "portal space draws in the SmartBox viewport" tolerates the hand-over frame (the hidden host made the shell paint in one frame, before the viewport's picture existed; the tunnel layer's own picture still
draws it). Review frame (gitignored): `cache/g4-frames/g4/04-shell-toolbar-and-message.png`. `GameShellG4Tests.Every_panel_key_of_the_real_key_map_opens_the_NeedsGameplay_message_naming_its_panel` feeds F12 (Inventory) and F10 (Map) of the real key map (map 0x10000009 registered with the log-off key, as `PlayMode` does) through `UiInputRouter` to the "Not in Lifestone M2" dialog; only those two NeedsGameplay pages have a plain key in the real map (Options, F11, is a hook page).

### 21.6 Selection (g5)

There is no separate section: the plan's g5 (the click that selects) is docs/avatars.md §18 and what the shell does with it is built by g5b, in §21.7.

### 21.7 Selection and the vivid target indicator (g5b)

Picking (the click that selects) is docs/avatars.md §18; this part is what the shell does with `PlayMode.Selection`. `GameSelection` (`Client/RetailUi/Selection/`, made by the screen; play mode gives it `SelectedProvider` = the selection and
`ProjectorProvider` = `RetailUiLayer.SelectionProjector`, as it does for the radar) follows the selected id each shell frame and drives the toolbar's selection display and the indicator. Engine-agnostic; the game side only feeds the two providers.

**The toolbar's selection display** (`gmToolbarUI::HandleSelectionChanged`, 11.6096 0x004C0010; field 0x1000019E, name text 0x1000019F, health meter 0x100001A1, mana meter 0x100001A2, stack slider 0x100001A3/0x100001A4). With nothing
selected the field is hidden (the layout shows it, empty). On a change: the name is cleared and the field hidden; if the health meter was showing it is hidden and `QueryHealth(0)` is sent (the Action API's "deselect" form: the server stops answering); then, for a
known object, the name (`WorldObject.Name`) is shown with the field, and **for an attackable object** `Actions.QueryHealth(guid, ActionSource.Ui)` is sent (`CM_Combat::Event_QueryHealth`, 0x006AAAA0; D16). The answer (`CM_Combat::DispatchUI_QueryHealthResponse`,
0x006AA900, which the session publishes as `HealthUpdated`) reaches `gmToolbarUI::RecvNotice_UpdateObjectHealth` (0x004BDDA0): when it is for the selected id the meter is shown with its position (property 0x69) at the fraction (0 to 1; retail shows no numbers). The
check "attackable" is `SelectionRules.QueriesHealth`: the object description's attackable bit (0x10, the radar's), not the player (`TODO(g5b-attackable)`: retail's own test is a few virtual calls whose names the decompile lost; ACE answers only for a creature, so another
object would time out). An object missing from the session's table shows nothing and logs `TODO(g5b-unknown)`. Not built (named): the stack slider (`TODO(g5b-stack)`, hidden), the mana meter and `CM_Item::Event_QueryItemMana` for an owned item
(`TODO(g5b-item-mana)`), the Use button's state, the name's formatting (title, PK colour).

**The vivid target indicator** (`VividTargetIndicator`, 11.6096 constructor 0x004F7720, `Initialized` 0x004F78A0, `OnDraw` 0x004F6EF0, `SetSelected` 0x004F6980, `SetOnScreenColor` 0x004F61B0, `SetOffScreenImage` 0x004F6140, `CopyImage` 0x004F6A70; an embedded member of
`gmSmartBoxUI`). Elements, found among the SmartBox's children: the frame 0x10000038 with its corners 0x10000039 to 0x1000003C (12 x 12; the layout anchors them to the frame's corners, so resizing the frame places them) and the arrow 0x10000045 (24 x 24); all plain fields, hidden at
`Initialized`. Pictures: the 13 entries of the enum group 0x10000009 (`EnumIdMaps`, `UiServices.EnumMaps`; indices 1 to 4 the corners, 5 to 12 the arrows), tinted per target colour, cached by colour, and set with `SetGeneratedImage`. **Tint:** `s_BlitMethod` is 4 in the exe (docs/research/retail-addresses-m2.md §1.8),
`SurfaceWindow::BlitAndColor` with the colorize mode: each pixel keeps its brightness (largest channel) and takes the colour's hue and saturation, alpha untouched (`VividColorize`). The colour is the target's radar colour (`gmRadarUI::GetBlipColor`, `RadarRules.ColorOf`: gold for an attackable creature, purple
for a portal, ...).

It draws only when it is enabled, the character option is on and the target is shown: **enabled** is `VividTargetIndicator::SetEnabled` (0x004F6CE0), which portal space drives (`PortalCues.SelectionIndicatorOff` when the tunnel comes up, `On` at `WorldFadeOut`/`Arriving`; `PortalSpacePresenter.SelectionIndicatorEnabled`, copied to
`Indicator.Enabled` by play mode each frame; closes `TODO(portal): G4's vivid target indicator`); the **option** is `PlayerModule::VividTargetingIndicator` (0x005D3EE0), bit 15 of the options word (`PlayerQualities.VividTargetingIndicator`; clear until the PlayerDescription arrives); the **target** is not the player and not an
object the player holds or wields (`SetSelected`; its one more ignored kind, "weenie type 2", is unread: `TODO(g5b-vti-ignore)`). A change of the option or the cue hides it at once.

*On screen* (`ObjectSelectStatus` 1; the projector's rectangle with `onScreen`): the frame is the rectangle grown outward by one corner (12 px), at least 8 pixels from the view's edge, spanning at least the corners (`VividTargetMath.OnScreenFrame`). *Off screen* (status 2): the arrow's picture
by the heading's sector (0 ahead, clockwise: [338,23) 6, [23,68) 7, [68,113) 9, [113,158) 12, [158,203) 11, [203,248) 10, [248,293) 8, [293,338) 5) and its position on the border from the heading's tangent against the half view, clamped 8 pixels inside (`OffScreenPosition`; `min_dist` is 8.0, §1.8).
**Approximation:** retail's heading is the sphere centre's in the viewer's frame (`SmartBox::GetObjectBoundingBox`, 0x00452E60); the shell has only the projected rectangle, so it is that rectangle's angle on the screen, and an object behind the camera (an empty rectangle) points straight back, 180 degrees (`TODO(g5b-heading)`).
The box is the Setup's selection sphere (picking's `TODO(g5-pick-bounds)`: a human's is large, so the corners sit well apart).

**Integrated with g6.** The indicator reads the option through `GameOptions.VividTargetingIndicator` (the session-local bit 15 the options panel flips in the session's override of `PlayerQualities.CharacterOptions1`, section 21.8), so toggling it in the panel hides and shows the indicator at once (`GameSelectionTests`, one test).

**Placeholder note.** "selection" was the last name on it: the note is gone and the screen logs no placeholder line.

**Checks.** Client tests (`RetailUi/Selection/`, 46): `VividTargetMathTests` (the sectors, the frame, the arrow position, the heading, the colorize) and `GameSelectionTests` over the real layout and the stand-in (select, QueryHealth, the answer as a fraction, deselect clears, a changed selection, an answer for
another object ignored, an unknown object, the corners round the projected box and following it, the arrow outside and behind, the portal cue and the option hiding it, a missing projector, the 12 source images resolve and the corners are 12 x 12, the rules). `--play-checks` (6 new, 75): the shell has the display, selecting the stand-in
drudge (a placeholder attackable creature ahead of the player) shows its name, the stand-in answers `QueryHealth` (0.6, a placeholder value) and the meter shows the fraction, the indicator is drawn for it, clearing clears all three, and the portal cue holds the indicator off while the tunnel shows. `--retail-ui-checks`: the placeholder check now expects no placeholders.
Review frame (gitignored): `cache/g4-frames/g5b/05-shell-selection-and-vivid-indicator.png` (1920x1080: the gold corners round the drudge, its name and the red health meter in the toolbar).

### 21.8 Character information, options and logging out (g6)

Sources: the sources research (§11 to §14, §16) and the 11.4186 decompile read for each function below (addresses are 11.6096); the exe reads are docs/research/retail-addresses-m2.md §1.9.

**Character information** (`GameCharacterInfo`, `Client/RetailUi/Panels/`; page 3 of the host, 0x10000183, `gmCharacterInfoUI`). Retail's page is a **text report** in the text element 0x1000011D, not a list of skills and vitals (those are the Skills page and the vitals
window). `gmCharacterInfoUI::Update` (0x004BB420) rebuilds it each time the page shows (`ListenToElementMessage` on a visibility message) and when the qualities change, from the player's description: `UpdatePlayerBirthAgeDeaths` (0x004B98D0: birth date from
PropertyInt 0x62 as C's `%c`, time played from 0x7D, deaths from 0x2B: none, one, two or "N times"), `UpdateEnduranceInfo` (0x004B9B40: "Natural" and "Drain" resistances from Strength + Endurance and the regeneration bonus from Strength + 2 * Endurance, each one
of None, Poor, Mediocre, Hardy, Resilient or Indomitable by the thresholds 200/260/320/380/440 and 200/346/470/580/690), `UpdateInnateAttributeInfo` (0x004B9400: the six starting attributes), `UpdateFakeSkills` (0x004B9550: chess rank 0xB5, default 1,400, and fishing skill 0xC0),
`UpdateAugmentations` (0x004B9C90) and `UpdateLoad` (0x004B9640: "not overburdened", or the burden over capacity and the skill penalty, `EncumbranceSystem::EncumbranceCapacity`, `Load` and `LoadMod`), with a blank line between them. The strings are `ID_CharacterInfo_*` of table 0x10000001; their
variables are the uppercase names without `ID_` (DATE, DURATION, RESIST, REGEN, STRENGTH and the other attributes, CHESS, FISHING, BURDEN, PENALTY, DEATHS: found by hashing candidate names against the renderer's missing-variable reports). A line whose quality the description lacks is left out; nothing is invented.
**Not built:** `UpdateAugmentations` and the carrying-augmentation line of `UpdateLoad` (`TODO(g6-character-info-augmentations)`, logged once); the played-time format (`TimeUtils::QueryDuration` is table driven: a labelled compact "1d 2h 3m 4s", `TODO(g6-duration-format)`).
The page opens from key action 0x05 and the burden indicator, through the host's hook.

**Options** (`GameOptionsPanel`, `GameOptions`, `PlayerOptionTable`, `UiOptionCheckbox`, `Client/RetailUi/Options/`; page 10, 0x1000018D, a four-tab `UiPanel`). The Character tab (class 0x10000027, `gmCharacterSettingsUI`, list 0x100001FA) is built as
`InitOptions` (0x004A05B0) builds it: for each section a header (list template 0), its toggles (template 2: a row holding the check box 0x10000219 of class 0x10000035, `UIOption_Checkbox`, registered as `UiOptionCheckbox` after `UIOption_Checkbox::Register` 0x00487000) and a separator
(template 1). All **49** `PlayerOption` toggles are listed in retail's order with their `ID_PlayerOption_*` labels (table 0x10000003); each option's word and bit are the PlayerDescription's two option words (ACE's `CharacterOptions1` and `CharacterOptions2`: bit 21 of word 1 is `SideBySideVitals`, as
`PlayerModule::SideBySideVitals` 0x005D4070 tests). A click changes the box only (`m_current`); Apply (0x100001FC) saves the changed honoured options, Reset (0x100001FD) restores the saved values, Defaults (0x100001FE) sets the defaults of the honoured options (retail's default words
0x50C4A54A and 0x00948700); Apply and Reset are ghosted while nothing differs (`PlayerOptionPage::OnOptionChanged` 0x004F3430). **D42: the options last for the session; nothing is sent to the server** (retail's `UIOption_Checkbox::Apply` 0x00487020 called
`PlayerModule::SetOption` and so `GameActionSetCharacterOptions`; `TODO(g6-options-save)`). `GameOptions` asks the session to apply a change (`IActionApi.SetSessionOption`, local notice 8): the reducer owns a session-local override and `PlayerQualities.CharacterOptions1` and `CharacterOptions2` return the server's word with the override applied, so every reader follows in any subscription order, a later PlayerDescription keeps the player's changes, and the override ends with the world entry (R8; `ReducerChatTests`).

**Honoured (exactly two):**

| Option | Word and bit | Effect in M2 |
|---|---|---|
| `SideBySideVitals` | 1, 0x200000 | the vitals window swaps at once (`GameVitals` refreshes on `GameOptions.Changed`): stacked 0x100005FA when clear, side by side 0x100006D5 when set |
| `VividTargetingIndicator` | 1, 0x8000 | the bit flips in the session's option word, which the vivid target indicator reads (nothing else changes) |

Every other option is shown with its value from the description and **ghosted**: a click leaves it as it was and `GameOptions.TrySet` refuses and logs (`TODO(g6-option-effects)`; for example `ToggleRun` is read by the player rig only when the rig is built (`PlayMode.RunByDefaultOf`), so a toggle here would not reach it (`TODO(g5-run-by-default)`), and `DisplayTimeStamps` has no chat timestamps yet).
The Gameplay Options tab (class 0x10000029, `gmGameplayOptionsUI::ListenToElementMessage` 0x0049E390) has seven buttons: **Exit to Character Selection** (0x10000203) is the logout below; **Exit Game** (0x10000617; retail broadcasts the quit-the-app action 0x10000027) asks the same confirmation as the logout (`ID_Client_EndCharacterSessionConfirm`), and Yes queues the epilogue (`UiModes.Epilogue`), which sends Quit and closes the application (`GameLogout.RequestExitGame`). Retail's own handler for the button (`gmGameplayOptionsUI::ListenToElementMessage`, 11.4186 0x0049E110) is not read for its confirmation: `TODO(g6-exit-game-confirm)`, logged once;
the other five (keyboard configuration, mouse turning, help files, urgent assistance and abuse report, which open web pages or windows) stay in the normal state and a click answers with the "not in M2" message under the button's own label (`TODO(g6-options-buttons)`, now only these five; the layout's ghosted art is its normal art, so ghosting would look live and say nothing: R6). The Chat tab (class 0x10000042) and the Config tab (0x10000028) are empty
(`TODO(g6-chat-options)`: per-window chat settings; `TODO(g6-config-options)`: sound, camera, graphics and input preferences are the client's own settings, not the character's).

**Logging out** (`GameLogout`, `Client/RetailUi/Panels/`). The Exit button sends `CM_UI::SendNotice_EndCharacterSession(1)`; `RecvNotice_EndCharacterSession` (0x004ECB30) calls `MakeLogoutConfirmationDialog` (0x004EC330) with `ID_Client_EndCharacterSessionConfirm` ("This will exit your character from the game world. Are you
sure?", table 0x10000001): a type 1 confirmation (property 0x8E = 1) in the immediate queue (0xC3 = 1: both read from the exe, §1.9) with the text in 0xC5, refused while one is open. Yes calls `Actions.LogOff` (retail's `UseTime` then ends the session); No closes the dialog and stays. **Retail's mid-air refusal "Cannot log off while in mid-air." is NOT built** (`TODO(g6-logoff-midair)`, logged once with the first log off by the key or by the panel's Yes): both log off whatever the body is doing, because the UI does not read the player body's airborne state. Retail's logoff sequence is otherwise met (DoD 11).
**Retail's Shift+Escape asks nothing** (`gmGamePlayUI::HandleKeyPress` 0x004EAA80 sets the request, the confirmation and the end together), so `TODO(g6-logoff-confirm)` is closed as retail-exact: `EndSessionKey` logs off at once and says so (docs/session.md section 26). The layout's toolbar has no logout button; the toolbar's options button and the Exit button are the way.

**The UI's busy count** (`UiBusyCount`, `UiElementManager.Busy`; `ClientUISystem::IncrementBusyCount` 0x00565610 and `DecrementBusyCount` 0x00565630). A counter; when it goes 0 to 1 or 1 to 0 retail re-evaluates the pointer (`UpdateCursorState` 0x005653D0), which tests the count **first**
(exe read, §1.9) and picks the busy pointer, cursor enum 14 or 15 over an object, ahead of every other pointer. Portal space's busy state raises the count for its whole sequence (`PortalSpacePresenter.BusyChanged`, wired in `Main`); `TODO(portal): G2 busy count` is closed. The host cannot yet draw the pointer
(`TODO(g6-busy-cursor-image)`: the cursor enum to the dat's pointer image is not mapped). Retail's other callers of the count (dat loads, the plugin API) are not built.

**Window positions** (D56). The floating windows' places, sizes and locks last for the session only; `TODO(g4-window-layout-save)` (logged at the shell's creation) records retail's two stores: the character's options on the server (the PlayerModule's per-window options 0x10000086 to 0x10000089 and the chat filters, restored by each floaty window's `UpdateFromPlayerModule`) and the client's layout file
`UI-<world>-<character>-<width>-<height>.txt` (`gmGamePlayUI::SaveScreenLayout` 0x004EB9E0, `LoadScreenLayout` 0x004EB580, `CreateScreenLayoutPath` 0x004EB320: one `<TAG> X:%d Y: %d W: %d H: %d` line per window).

**The vivid option and the indicator.** The vivid target indicator (section 21.7) reads this session-local bit through `GameOptions`, so the panel's checkbox hides and shows it live.

**Checks.** Client tests (`RetailUi/Shell/GameShellG6Tests`, 17): the option table (49 once each, two honoured, defaults), the busy count, the resist words, the report from a scripted description (birth, played, deaths, endurance words, innates, chess, fishing, load, and a burdened load), the panel lists every option and ghosts the rest, an honoured option takes effect on Apply
and swaps the vitals window (Reset, Defaults, a later description keeps it), a ghosted option cannot change and the words do not, the vivid option flips its bit, the logout asks and No stays, Yes logs off (`Confirmed`, `Last`), Exit Game asks and Yes queues the epilogue (`Exit_Game_*`), and the one-time `TODO(g6-logoff-midair)` line; `StandInWorldTests` now expects the key to log off without a TODO. `--play-checks` (5 new): character information opens with the report; the side-by-side option swaps the window; a ghosted option does not change;
the logout control asks first; No stays in the world (the visit's one log off is the key's, so Yes is covered by the client test). Review frames (gitignored): `cache/g4-frames/g6/06-character-information.png` and `07-side-by-side-vitals-and-logout-confirmation.png`.

### 21.9 Checks and review frames (g7)

**The frames option.** `--play-checks --play-frames[=DIR]` (registered in `OptionCatalog.Play`, parsed by `PlayOptions`; it replaces the unregistered environment variable of the earlier slices, which is gone) saves the shell's review frames to `DIR`, default `docs/screenshots/m2/shell/<W>x<H>` (gitignored with the other screenshot folders, derived from the dats, never committed). It needs `--play-checks` (an error otherwise). The names are `ShellFramesPlan.FrameNames` (Client; `PlayChecks` takes each when its state is reached):
`01-shell-portal-space` (the tunnel in the SmartBox's viewport, the shell around it), `02-shell-arrival` (vitals, chat, radar, the world), `03-shell-spew-and-chat` (the spew box and the main chat with lines, the vitals after a server update), `04-shell-toolbar-and-message` (a NotInM2 message over the shell),
`05-shell-selection-and-vivid-indicator` (the drudge selected: name, health, the corner marks), `06-character-information`, `07-options-side-by-side-vitals` (the options panel's Character tab after Apply, the side-by-side vitals window shown), `08-logout-confirmation`, `09-shell-automation-inspector` and `10-shell-tools-panel` (the two Lifestone panels, §22): ten in all.
**A frames run is not a timing run.** Each read-back stalls the frame it is taken in, so the 50 ms judgement is reported, not judged (the check's detail says "REPORTED, NOT JUDGED"), the tunnel's per-frame tallies skip the two frames after a read-back, and the run can pass. Before the arrival the run makes the window the size `--resolution` asked for (borderless when the desktop's work area is smaller, as the retail UI frames run does) and checks it; every frame is checked to be that size and not blank. A run ends with the check `every-shell-review-frame-is-taken-the-size-asked-for-and-not-blank` and the verdict line
`shell frames done size=<W>x<H> scale=<s> captured=<n> failures=<f> frame_time=reported` (`ShellFramesPlan.Verdict`; the lead's note: additive fields only). Smoke step `shell-frames` (`tools/smoke.ps1`; `slice-check.ps1 -Steps shell-frames`) runs it at 800x600 (scale 1; the default, the owner's rule of 2026-10-14) and, with `-AllSizes`, also at 1920x1080 and 2560x1440, and fails on a missing, blank or wrong-sized frame (it counts the ten files too); frames go to `<OutDir>\shell-frames\<W>x<H>`.

**UI scale 2 (2560x1440).** The UI scale is the largest integer that leaves 800x600 UI pixels (§17): 1 at 1920x1080 and 2 at 2560x1440, whose UI is 1280x720. The windows keep their anchors in UI pixels: `--retail-ui-checks` runs the shell check at 800x600 by default and, with `--all-sizes` (smoke `-AllSizes`), at 1920x1080 and 2560x1440 too (`CheckGameShellAsync(window, scale)`; the larger sizes are opt-in, never deleted, not run by default): the root and the SmartBox fill the UI, the radar is (1160,0)-(1279,139), the main chat (0,620)-(409,719), the toolbar (970,620)-(1279,719), the stacked vitals (560,0)-(719,57) (centred), the SmartBox hole, the viewport source's rectangle and `GameViewRect` are the whole 2560x1440 window, and nothing is drawn over the world. The frame at scale 2 shows the same shell with every window at twice the size at the same anchor; no layout problem was found (the M1 statistics text, drawn by the explore HUD over the top right, is a separate overlay that lies over the radar's neighbourhood at any size).

**The stand-in note (review R15).** In an offline stand-in session (`SessionHost.StandIn` set; never a live one) the vitals, the radar's objects and the chat lines are the scripted visit's placeholder values. The play status line is hidden while the shell is up, so `PlayMode` keeps `PlayStatusLine` visible with `PlayStatusLine.StandInNote` (two lines at the top left, clear of the spew box at 800 wide: "STAND-IN SERVER (offline): placeholder values"); `--play-checks` has `a-stand-in-note-labels-the-placeholder-values-in-the-shell`.

**The login check judges the shell.** `--login-check` ends its verdict line with `shell=ok|missing vitals=<h>/<s>/<m>|empty spew_portal=yes|no vitals_window=stacked|side` (docs/session.md section 27): `LoginCheckShell` (Client: the vitals from the PlayerDescription, the window the character's option chooses, the wording) and `ShellWatch` (Godot: the game screen up with no bare-world frame, the spew box having heard portal space's notice, the vitals window shown, the radar's blips), which `--play-checks` also runs on the stand-in session (`the-login-check-s-shell-fields-read-ok-on-the-stand-in`). The review frames of the login check gain `03-shell-after-the-settle` (the Academy, the shell, the radar's blips); the character list's frame is now `04-`.

**Checks.** Client tests (`LoginCheckScenarioTests`, 4 new: the vitals and the window from the scripted description, the side-by-side option, empty vitals fail at the settle, the option and the plan). `--play-checks` 2 new in a plain run (the shell fields; the window fit and the frame check only when `--play-frames` is given); `--retail-ui-checks` 9 new `[uicheck]`s at 2560x1440.

### 21.10 The warm-up spread over frames (G4 live-gate fix 3)

**The problem.** The warm-up built the whole gameplay root in one call (45 ms offline, 51 ms in the frame that also built the character list, 94 ms in all) and started only when character management showed. Live, character management lasts about 0.2 s before the login check presses Enter (a person takes longer), so the build, the paint (51 frames) and the stop all fell in or beside the judged window, and the real shell's frame also paid for the warm-up's delete (4 ms), a paint slice of 8 ms and the first upload of the whole screen's tiles (12 ms): 44 ms of retail UI work offline in the one frame (`a slow retail ui step`: update 20, warm-up 4, composite 7, copy 12), which live read as 52.8 and 59.6 ms.

**Now.** (1) `UiElementManager.BeginRootElement` / `UiStagedRoot` (Client/RetailUi/Core) make the same tree as `CreateRootElement` one element at a time, interruptible between two, joining the manager only when whole; `ShellWarmUp` builds for at most 4 ms a frame (`BuildBudgetMilliseconds`) and then paints in 2 ms slices (`PaintBudgetMilliseconds`), so a warm-up frame is about 4 ms; the longest single unit offline is 8 to 10 ms (the root's recursive initialize and post-init, which retail also does in one call). (2) It starts at the first login screen (`RetailUiLayer.StepShellWarmUp`: mode none, the data check or character management), not at the list. (3) If the login screens end while it runs (Enter comes first) it is dropped, not deleted: the images decoded, glyphs made and descriptions resolved so far stay cached, the real shell is built as always, and the half-built tree is left to the garbage collector (the delete was a frame's work). A warm-up that finishes deletes its tree in its own frame. (4) The frame that builds the shell neither composites nor uploads (`builtThisFrame`); the next frames paint in a 3 ms slice (`ShellPaintSliceMilliseconds`, was 8) and upload in a 3 ms slice (`ShellCopySliceMilliseconds`; `UiCompositor.CopyDirtyTiles(sink, deadline)` stops between two roots or tiles and leaves the rest dirty). The cost is a few frames more under the backdrop (cold: 43 frames, 234 ms offline, was 17 frames, 112 ms) before the world shows; with the warm-up done first it is 5 frames, 35 to 50 ms.

**Logs.** `a slow retail ui step` (the layer, over 8 ms: update, screens, warm_up, composite, copy), `the in-game shell was built: root .. ms, wiring .. ms`, and the warm-up's started / stopped / painted lines with `build_frames`, `longest_step_ms` and `longest_unit_ms`. `--play-checks` judges `enter-right-after-character-select` (the stand-in server answers Enter 0.1 s after the character list shows, as a live one does, `StandInVisit.HoldEntry`; every frame from three frames after the list to the arrival, and from the arrival to the first teleport, under 50 ms; the detail says whether the warm-up was still running at Enter).

**False "no runtime" TODOs (review R7, completed).** `GamePlayScreen.RegisterClasses` also registers the windows whose behaviour the `Game*` controllers drive over a plain window element (`ControllerClasses`: gmSmartBoxUI, gmSpewBoxUI, gmFloatyChatUI, gmFloatyMainChatUI, gmFloatyVitalsUI, gmFloatySideVitalsUI, gmFloatyIndicatorsUI, gmFloatyToolbarUI, gmToolbarUI, gmFloatyPanelUI, gmCharacterInfoUI, gmCharacterSettingsUI, gmGameplayOptionsUI), so a login no longer logs a stub for them. The classes with no controller (the vitae, effects, burden and mini-game indicators, the chat and config option pages, the map, inventory, spellbook and the rest) stay loud.

**The real shell is staged too (G7 live fix 2).** The warm-up only helps when it finishes before Enter, and the live login pressed Enter first: the warm-up was dropped and `GamePlayScreen` built the real shell in one frame (21 to 29 ms, 60.9 ms with a gen2 collection). Now `UiLoginContext.StageShell` (set by `RetailUiLayer` in play mode) makes the screen begin its root with `BeginRootElement`, finish it in `Update` at `GamePlayScreen.BuildBudgetMilliseconds` (3 ms) a frame, and then wire the windows a controller at a time (`Wire`, one `yield` between the spew box and chat, the vitals, the radar, the panel host, the options pages, the toolbar, the selection, the indicators and the Lifestone panels); the first chunk (windows found, backdrop cleared, viewport shown) runs in the frame the root joins the manager, so no paint sees the layout's dirt backdrop. The layer does not paint while the windows are being wired (`RetailUiLayer.Step`), counts the paint frames from the frame the screen is built (`TrackShellPaint`), and `ShellHoldsWorld` keeps the world hidden throughout. A screen disposed before it is made finishes its tree in that one call and deletes it. Notices with no window yet are held by `UiNoticeRouter.ExpectWindows` and shown when the box registers. Log line: `the in-game shell was built over N frames ... longest frame .. ms, longest unit .. ms`; `--play-checks` judges `no-ui-step-after-enter-is-over-12-ms` (`RetailUiLayer.WorstStepMilliseconds`). The warm-up's partial tree is not continued (another element runtime), only its caches.

**The hold lets the tunnel through (M2 W3 gate fix).** The hold's black backdrop covered portal space's tunnel (a layer below the UI's) from the first frame of the arrival until the shell's viewport existed, although retail shows the tunnel at once and the world behind it is hidden by portal space anyway. `RetailUiLayer.FollowWorld` now hides the backdrop while `ShellHoldsWorld` and the tunnel shows (`TunnelShowing`, set by `Main`); `WantsWorldHidden` and the layer's hide request on `WorldVisibility` do not change, so no frame shows the bare world (`bare_world_frames=0`) and the tunnel's own full-window picture draws until the shell's SmartBox viewport (`PortalTunnelViewportSource`) takes over. A logon screen or a log off still sits on the backdrop (the hold is the game screen's only). Judged by `--play-checks` (`tunnel_covered_frames <= 1`) and the login check (docs/session.md section 27).

## 22. The Lifestone panels (G7)

Lifestone's own panels are floating windows over the in-game shell (section 21), built from layout files in the R9 format (retail-ui-data.md section 9: `src/Lifestone.Data/Ui/Layouts/*.json`, retail frame and template ids, Lifestone literal text) with the
element runtime of sections 2-10; their logic is a model with no element in it, and everything they do goes through the Action API and the state (DoD 13), never the session directly. Code: `Client/RetailUi/Lifestone/` (namespace
`Lifestone.Client.RetailUi.LifestonePanels`: a nested namespace called `Lifestone` would capture every `Lifestone.Client...` name written in full in its sibling namespaces). The retail UI may reach the session only through
`Lifestone.Client.Session.Contracts` (`RetailUiArchitectureTests`, unchanged and with no allow-list), so what a panel needs from the rest of the session comes across as data in the contract: the tools model's position comparison is `WhereResult.Compare`, which returns a `PositionComparison` (distance, agreement, the chat line) built from `Session.Tools.PositionAgreement` inside the contract. `ToolsModel` and `ToolsGate` live beside `ToolsPanel`.

### 22.1 The tools panel (G7 lane A)

**What it is.** `ToolsPanel` (`tools.json`, layout 0x7F000010, window 0x7F000100, 300x356 at (250, 60), z level 20) is a child of the shell's root, hidden until F7. Its window is made the first time it is opened, not with the shell: the first build costs
about 35 ms (the fonts and glyphs of the dialog templates, which nothing else of the shell uses), and the frame that builds the shell is already the heaviest of the login (DoD 15, 50 ms; found by `--play-checks`). It uses the retail dialog frame (0x21000042/0x100002AF), the retail text and
button templates of 0x21000040 (0x100002CA text and fields, 0x100002C3 row and link buttons, 0x100002C4 and 0x100002C5 the two tool buttons), the dialog border 0x100002B7 for the two fields, and a drag bar (type 2) over the title.
The words are Lifestone literals: "Lifestone tools", "Places (click one to teleport)", "Where am I", "Add XP", "Up", "Dn", "Hide panel" (`LifestoneLayoutTests` refuses any literal that is retail text: "Close", "Done" and "Hide" all are, hence "Hide panel").
`UiLayouts` reads the embedded files with `LifestoneLayoutReader` on the first lookup of a DID in the Lifestone range, so no host registers anything. The load is loud in every way it can fail, and the failure is kept: a file that does not read, two embedded files with one DID (the error names both files; a layout a test registered under that DID still wins) or a resource that cannot be opened logs one error line (`UiLayouts.ErrorLog`, set by `RetailUiLayer`) and makes every later lookup in the range throw the same `TODO(g7-layouts)` message, never a quiet "absent".

| Part | Elements | Does |
|---|---|---|
| Places | search field 0x7F000106, rows 0x7F000110-0x7F000117, Up 0x7F000118, Dn 0x7F000119, counter 0x7F000120 | A search (`TeleportCatalog.Default().Search`: ACE's points of interest plus the curated dungeons and interiors; best match first, an empty search lists all) narrows the list as it is typed; eight rows at a time, Up and Dn move a page. A click on a row teleports: `PlayerTools.Place` with `ActionSource.Ui` (an ACE row by `place poi "<its first ACE name>"`, a curated row by `place loc`, `PlaceRequests`), and portal space follows the server's F751. |
| Where am I | button 0x7F000121, text 0x7F000122 | `PlayerTools.WhereAmI`; the server's position and its distance from the client's physics body (`WhereResult.Compare`: `PositionAgreement`, 0.5 m) in the panel ("client body 0.12 m away (agrees)"), and the full line (ACE's loc text, both positions, "agrees within 0.5 m") in the chat. The client's position is the player's walker body: `PlayMode` sets `ToolsModel.ClientPosition` from `PlayerController.Position` (null while the body is not placed: "the client body has no position yet"). The session state's own position is the server's last word, so it is never used as the client's. A host with no body (the offline shell without a play session) gets "no client body in this host (TODO(g7-where-client-body))" in the panel and the chat, and one error line in the log; it never says "agrees". |
| Add XP | field 0x7F000124, button 0x7F000125 | `PlayerTools.AddXp` of the amount typed (whole number, commas allowed, 1 to 999,999,999,999); a bad amount is told in the status line and sends nothing. The mod's reply line ("added N experience; total ...") is the chat confirmation; the status line says it too. |
| Status | 0x7F000126 | What the last use did, or why it did not (a refusal, a server error, a silence). |
| Hide panel | 0x7F000127 | Closes the panel (F7 does too). |

**Every use is in the chat and the action log.** The panel calls `PlayerTools`, so the request's own chat line ("Lifestone: teleporting to Holtburg...") and the mod's reply line are written by the session, and the action log holds a `ToolPlace`,
`ToolWhereAmI` or `ToolAddXp` entry from `ActionSource.Ui`, under the Action API's rate limit. **The kill switch and dry run gate automation (`ActionSource.Automation`) only** (`ActionApi.Accept`): a player's own click in this panel is not automation, so engaging the kill switch does not stop the tools (making it stop them would be a design change for the owner, not a fix). `ToolsModel` adds a chat line only where the session has none: Where am I's agreement, a refusal or silence, a `Cancelled` request and a reply the client could not read ("failed: ..."; the line reads "Lifestone: could not <what>: <why>"); an error reply of the
mod is already in the chat.

**Refusals.** Against a server that is not on loopback the panel does not open: a retail message dialog says "The Lifestone tools work only against the bundled server on this computer. This server is not on loopback, so the tools are switched off."
(and `PlayerTools` refuses every request anyway). When the mod is absent (the server answered "Unknown command: lifestone" to the status probe the session sends on entering the world, or to the one the panel sends when it opens without a status yet) another message
dialog says "The Lifestone server mod is not loaded on this server, so the tools cannot work. ...". Either learned while the panel shows closes it with the same dialog. Both texts are Lifestone literals on the retail message dialog (`UiDialogType.Message`). A refused F7 also puts the same text in the chat as a "Lifestone: <text>" line.

**Keys (D41): `LifestoneHotkeys`.** F7 (DirectInput 0x41) toggles the tools panel and Scroll Lock (0x46) opens the automation inspector. `LifestoneHotkeysTests` proves over the user's dats that neither key is bound, with any meta keys, in `gmDefaultMap`
(MasterInputMap 0x14000000) or the engine map (0x14000002), and that F1-F6 and F8-F12 all are (F7 was the one free function key; it was M1's interior debug key, dropped in play mode by D41). They are the bindings of an input map of Lifestone's own (0x7F000001),
added to retail's with `RetailKeyBindings.With` by `LifestoneHotkeys.Attach` and registered at a priority above a focused text's, so F7 closes the panel while its search field has the focus. The inspector key calls `LifestoneHotkeys.OpenInspector`, which `GamePlayScreen` sets to `AutomationInspector.Toggle` (section 22.2; a host that wires nothing gets a logged `TODO(g7-inspector-unwired)`; the key is always counted). A panel that opens with a key takes the focus for its search field one frame later: a text field that gains the focus while a key is being
handled makes the router drop the next character (`UiInputRouter.SetTextMode`), and F7 produces none, so the first letter typed would be lost.

**Placeholders.** The offline stand-in answers `place` (the teleport), `where` (the session's own position, so the agreement is trivial), `xp` (a counter that starts at 1,000,000 with level 1 and adds what is asked: no level table, no cap) and `status` (a Player
account on a mod that answers, with the stand-in's placeholder values), all labelled placeholders in the stand-in's code only: their reply lines in the chat read like the mod's (as the older `place` reply does), so the offline run is the placeholder; a live server answers all of them with the mod's real values. With no player position yet the stand-in refuses `where` (`TODO(g7-standin-where)`) instead of throwing.

**Paging and overlap.** The Places list moves a page at a time and clamps the last page to the end (10 results: 1-8, then 3-10): a design choice, so the last page is always full. The tools panel (250,60, 300x356) and the inspector (120,40, 560x400) are both at z level 20 and overlap; the checks show them one at a time, and with both open the one opened later is on top.

**Shutting down.** `ToolsPanel.Dispose` detaches the model (`ToolsModel.Detach`): a tool reply that arrives after the panel is gone (a request pending when the player logs off) no longer writes a status or chat line. The search field is followed with `UiTextElement.TextEquals`, which allocates nothing, so an open panel allocates no string per frame.

**Checks.** Client tests (`ToolsModelTests` 18, `ToolsPanelTests` 7, `LifestoneHotkeysTests` 4, one more `LifestoneLayoutTests`): the search and its pages, the request for an ACE row (`place poi`) and a curated row (`place loc`), Where am I's agreement and
disagreement, Add XP's request, amounts and the server's confirmation in the chat, the UI trail in the chat and the action log, both refusals (a non-loopback server through the Action API's own gate; the mod missing), F7 over the real shell, typing, clicking and
paging, the two dialogs, and the keys unbound in 0x14000000. `--play-checks` opens the panel with F7, types "holtburg", clicks the first row (portal space follows), presses Where am I and Add XP, finds the three uses in the action log, presses Scroll Lock and closes the
panel with F7. A `--play-frames` run saves the panel as `10-shell-tools-panel` (the tenth of `ShellFramesPlan`; `09-shell-automation-inspector` is the inspector's). Scroll Lock opens and closes the inspector in the same run.

### 22.2 The automation inspector (G7 lane B)

**What it is.** `AutomationInspector` (Client/RetailUi/Lifestone/, namespace `Lifestone.Client.RetailUi.LifestonePanels`: a namespace named `Lifestone` under `RetailUi` would capture every `Lifestone.` qualified name in the sibling namespaces) is a floating window over the shell that shows the Action API's audit log (ST9, DoD 13): the `ActionLog` rows newest first (each: when it completed, the source Ui/Host/Automation/Client/Test, the action's summary, how it ended with the detail or the milliseconds taken, and "[waited for the rate limit]"), four counter lines (logged and kept of the ring's 1,024; confirmed, local, sent, dry run; refusals, server error, timed out; cancelled, not implemented, rate-limited), and the kill switch and dry run **read-only** (`Kill switch: off | Dry run: off | read-only here`). It has no control that changes either; changing them is automation's, later. The title says "(read-only)"; the window borrows retail's dialog frame and the menu popup's list, so it is a labelled Lifestone window in retail's look, not a retail window.

**API.** `Open()`, `Close()`, `Toggle()`, `IsOpen` (Scroll Lock calls `Toggle()`); `Refresh()`; `RowCount`, `RowText(i)`, `CountersText`, `StateText`, `Refreshes`, `Rebuilds`, `IsStale` for tests and checks. `GamePlayScreen.Inspector` holds it (made with the shell, no elements until the first `Open`). `IsBackfilling`, `FirstBatch` and `FrameBatch` describe the row build (below). `FormatRow`, `FormatCounters` and `FormatState` are public and engine-free.

**Layout.** `src/Lifestone.Data/Ui/Layouts/inspector.json` (layout 0x7F000002, `lifestone-layout/1`): window 0x7F000200 (Field, 560x400 at 120,40), the dialog frame 0x21000042/0x100002AF, a drag bar (class 2, moves the window), text 0x21000040/0x100002CA for the title, counters and state line, a list box 0x7F000206 (class 5, with the properties of the menu popup's list 0x21000043/0x100004C0: a composite template cannot be resized, because its children keep their sizes) and its scrollbar 0x7F000208 (0x2100003E/0x10000367), the button 0x21000040/0x100002C8 ("Hide inspector"), and the row template 0x7F000210 (a top-level text element made detached per row). All text is Lifestone literals (no retail string; the layout hygiene test checks it). `UiLayouts` registers every embedded `Ui/Layouts/*.json` lazily (section 22.1), so no host calls anything. A host without the layout gets one `TODO(g7-inspector-layout)` error and an inspector that stays closed. `truncate_text_to_fit` is typed as a string by the master property table (not a Bool), so rows are one line and clipped at the list's width. TODO(g7-inspector-scroll): inserting a row at the top shifts a list the viewer has scrolled down.

**Change-driven.** `ActionLog` gained `Changed`, `Version`, lifetime `CountOf(outcome)` and `RateLimited` (the ring's drops do not reduce them) and `ActionLogEntry.RateLimited` (the tracker sets it when the lane's token bucket made it wait; nothing is dropped). `Changed` is raised on each completion and, through `ActionLog.Touch`, when the kill switch or dry run changes. The handler only sets a flag. While open, the inspector listens to the frame message and, if the flag is set, refreshes once: a new action adds a row on top (the oldest leaves past 1,024), a burst of more than 32 new actions in one frame (or more than the ring) or the first build rebuilds, the texts are replaced only if they differ. While closed nothing is rebuilt (`Open` catches up); idle, `Refresh` and the frame hook allocate 0 bytes (tested over 10,000 calls).

**Rows are built over frames.** Measured (Debug build, `AutomationInspectorTests`): making all 1,024 rows at once cost about 40 ms for a rebuild and about 31 ms for the first open of a full ring including the window, over the 10 ms a frame can spare and approaching DoD 15's 50 ms. A (re)build now makes the newest 32 rows (`FirstBatch`), and each following frame adds 64 older ones to the bottom (`FrameBatch`, `IsBackfilling`) until the list holds the ring: the first open of a full ring costs about 3 ms warm (18 ms cold, mostly the window), a burst of 1,100 while open about 8 ms for its frame, and 16 frames fill the list. Row k of the list is always ring entry Count-1-k, so new rows still go on top and the oldest leave from the bottom while the backlog is pending; the list scrolls as it fills. An idle inspector with nothing left to build does nothing.

**Checks.** Client tests (`AutomationInspectorTests`, 13): row format, lifetime counters and notices, the safety line, the session's notices for the switches, the shell's inspector and its layout resolving, rows newest first with outcomes, refresh on the next frame (two actions in a frame make one refresh), the cap at 1,024 (incremental trim, and a rebuild after a burst), the first open of a full ring building only the newest rows and the rest over frames, switches shown and not changeable, the Hide button and catching up on reopen, idle without allocation, a host without the layout. `--play-checks` (+8): the inspector is closed in the shell, opens and lists the log, shows the typed Talk, the portal's Use (`Use guid=0x80000A03`) and the tools teleport (`ToolPlace Holtburg`) with their outcomes, its newest row is the log's newest, counters and read-only switches, and hides again. Review frame `09-shell-automation-inspector` (`ShellFramesPlan`).

## 23. The M3 panels (M3 2.2)

Item 2.2 (Panels A and B) builds six of the host's sixteen pages (§21.5): the skill page (11: attributes and skills), the effects pages (4 and 5), the vitae page (15), the inventory page (7, with the paperdoll) and the spell page (13: spellbook and components). Code: `Client/RetailUi/Panels/` and `Client/RetailUi/Widgets/`; the panels read `IGameSessionView` and act through `IActionApi` only (§22, DoD 13). Plan, slice table and decisions: docs/milestones/m3-plan.md §4.6 and §7. Sources: the layout dumps of the six pages (`PanelLayoutDumpTests` writes `cache/m3/2-2/layout-<panelId>.txt`; derived from the dats, never committed) and the 11.6096 functions cited below.

### 23.1 The panel seam (s0)

**Pages and controllers.** Page 11 holds `UIElement_Panel` (the tabs) with `gmAttributeUI` 0x1000002A, `gmSkillUI` 0x1000002B and `gmCharacterTitleUI` 0x10000046 (lists, meters, no item list); pages 4 and 5 hold `gmEffectsUI` 0x1000001B (a list box and scrollbars); page 15 `gmVitaeUI` 0x10000020; page 7 `gmInventoryUI` 0x10000023, `gmBackpackUI` 0x10000022, `gm3DItemsUI` 0x10000021, `gmPaperDollUI` 0x10000024 and 27 `UIElement_ItemList` 0x10000031; page 13 `gmSpellbookUI` 0x1000002E, `gmSpellComponentUI` 0x1000002F and one item list. `UIElement_UIItem` 0x10000032 is in no page layout: the item list makes its cells at run time (§23.4). Pages 11, 4, 5 and 15 hold no item list, so panels A did not wait for the item widgets.

**The seam.** `GamePanels` (with `IGamePanel`: `PanelId`, `OnShown`, `OnHidden`, an optional allocation-free `Step(now)`) registers controllers per page; several controllers may share a page (the tabs; the inventory page and the paperdoll). The first controller of a page makes it **Built**: the host's handler shows it with `SetPanelVisibility`, and `GamePanels` calls the controllers' `OnShown` and `OnHidden` from `GamePanelHost.PageVisibilityChanged`, so they follow the page's visibility by any path. A page with no controller answers with the retail message dialog "Not in Lifestone M3: <panel>" (`NotInM3`, §11, a Lifestone literal, logged as a placeholder); the toolbar's Examine button answers it too (its combat buttons are built since 3.2, §23.8). `PanelPageKind` is `Built | NotInM3` (`GamePanelHost.KindOf`). The options page (10) and character information (3) stay Built by their g6 handlers, so a page-11 controller must not reuse them.

**Staged build.** After the shell's own controllers (the indicators and character information must exist), `GamePlayScreen.Wire` runs `GamePanels.Build()`: one staged unit per queued controller, each under 8 ms (§21.10), constructing nothing but element look-ups; a list is built the first time its page shows. `GamePanels.Step(now)` runs once per shell frame over the shown pages' controllers (a plain list, no allocation). `RegisterClasses` registers the classes of the six pages as plain containers (`UiClassRegistry` logs no stand-in warning; review R13). A page whose element is missing from the layout logs a named TODO and has no controller (`m3-inventory-page`, `m3-spellbook-page`, `m3-components-page`, `m3-effects-page-missing`, `m3-vitae-page-missing`, `m3-skills-tab-missing`, `m3-attributes-layout`, `m3-paperdoll`). Built lines at the first shell: 8 frames, wiring 9.3 ms, longest unit 3.9 ms (play-checks); the timings vary on the cold JIT.

**Lifetime.** The session and its context outlive a log off, so the panels end with their screen: `GamePlayScreen.Dispose` calls `GamePanels.Dispose` (unsubscribes the host's `PageVisibilityChanged` and disposes every `IDisposable` controller, each of which unsubscribes from the session; the paperdoll disposes its figure's attachment, which unbinds and frees the viewport binder) and `GameIndicators.Dispose`. `RetailViewportHost.RegisterSource` tells a source it replaces `Bound(default, visible: false)`. A test compares `EventBus.HandlerCount` before a screen and after it ends.

**Review frames.** `PanelFramesPlan` (§23.7) lists the frames; `--play-panel-shots=DIR` (play mode; with `--play-checks` it only saves; it ends like `--play-screenshot`) saves them from a plain run. The gameplay-staged-notice test no longer counts frames beyond "more than one": the staged build's budget is wall-clock, so whether the notice had become a row when `IsBuilt` turned true depended on the machine's speed.

### 23.2 The skill page: attributes and skills (a1, a2, fix A)

**The page.** Page 11 (element 0x1000018E) is a `UIElement_Panel` with three tabs (attributes 0x10000228 over page 0x1000022B, skills 0x10000229 over 0x1000022C, character titles 0x10000538 over 0x10000539); property 0x32 marks the attributes tab open, so the toolbar's skills button (0x10000199) opens the page on it. The attributes and skills tabs share one header, `StatHeader`: name, level (a placeholder while unknown), the player-kill line, total experience, experience to the next level (or the retail "infinity" text) and the experience meter (position 0x69), as `gmStatManagementUI::UpdateCharacterInfo` (0x004F1400) and `UpdateExperience` (0x004F1700) have them; each tab owns its copy of the header elements (same ids under its own tab element) and calls the helper on every redraw. The `ID_StatManagement_*` strings take the variables TOTAL_EXPERIENCE, EXPERIENCE_TO_LEVEL, SKILL_CREDITS, AVAILABLE_EXPERIENCE and XP_TO_RAISE.

**Attributes tab** (`GameAttributesTab`; `gmAttributeUI` over `gmStatManagementUI`). `PostInit` (0x0049DDE0) lists nine rows: Strength, Endurance, Coordination, Quickness, Focus, Self, then Health, Stamina and Mana; a row is the list's template 0 (icon 0x10000129 from enum groups 0x10000002 and 0x10000003 of the stat, label 0x1000012A, value 0x1000012B). A row shows the stat with the enchantment registry applied (`InqAttribute` with `raw` false; a vital as current over maximum), in font colour 1 raised above the base and 2 lowered; the vitae alone changes nothing (`GetVitaeModifier` 0x004F1D90). A click on a row (either button, single or double) selects it (`SetSelection` 0x0049CD30; the same row clears it); `UpdateSelection` (0x0049E150) sets the rows' states (selected 6, else 1) and the page state: 0x10000011 the default footer (0x10000240: a prompt, the skill credits, the unassigned experience) or 0x10000012 the selection footer (0x10000241: "name: value" and the difference in colour 1 or 2, a vital's vitae loss in colour 3, the experience to raise, the unassigned experience, the two buttons).

**Skills tab** (`GameSkillsTab`; `gmSkillUI`, tab element 0x1000022C, button 0x10000229). `gmSkillUI` and its base are one controller over generic elements. The list is built the first time the tab shows (the tab's visibility message, as `gmStatManagementUI::ListenToElementMessage` 0x004F0870), never in the constructor (look-ups only, 0.03 ms): one unit of about 6 ms cold for 42 rows (four headers and the SkillTable's 38 skills). A change of a skill's advancement class builds the list again (`RecvNotice_SkillAdvancementClassChanged`); any other change rewrites only the rows whose value or colour changed. Grouping is `RebuildSkillList` (0x0049C610): specialised, trained, untrained (class 1 with `_min_level` at most 1), unusable (everything else, a skill the description lacks included); each group sorted by name ordinally (`wcscmp`) under header templates 1 to 4; a row is template 0. A row shows the skill's current value (`SkillInfoRegion::Update` 0x004F2740: colour 1 above the base, 2 below; the vitae's loss taken out first). A click selects, a header or the same row clears (`SetSelection` 0x0049B2B0); the footer follows through the tab's state: 0x10000011 none, 0x10000012 untrained (credits to train and available; the train button, no "10" button), 0x10000013 trained or specialised (title with the vitae's and the enchantments' differences, the experience to raise or the cap text at the rank cap, a meter of the rank's progress, the unassigned experience; the raise and "10" buttons). The three footers hold the same child ids, so the controller looks each up inside its own footer.

**The raise buttons (D29).** Attributes: 0x10000246 is `RaiseSelection` (0x0049D290) and 0x100005EB `Raise10Selection` (0x0049D3C0). The cost (`GetCostToRaise` 0x0049CDF0, `GetCostToRaise10` 0x0049CEE0) is the XpTable's cumulative cost of rank `ranks + 1` (or `ranks + min(10, cap - ranks)`) less the experience already spent on the stat, none at the cap (attribute 190, vital 196); `StatValues.CostToRaise` and, for skills, `SkillRules.CostToRaise` / `CostToRaise10` hold it in the contracts layer. A button is ghosted (state 13) when the cost is none or the unassigned experience is short, and while an earlier raise awaits its answer (`m_bAwaitingRaise`); a click sends `RaiseAttribute`, `RaiseVital` or `RaiseSkill` (`ActionSource.Ui`) and waits for the stat's own change. The attributes tab clears the wait when the raise action ends (its tracker completes on the stat's own update), not on any qualities change: a vital's tick or ACE's AvailableExperience update, which arrives before the stat's own, leaves both buttons ghosted and `Raise` returns false; the skills tab works the same way (`GameSkillsTab.Await`). The skills tab's train button asks the retail confirmation (spend N credits to train X; the literal) and Yes calls `TrainSkill` with the SkillTable's trained cost. Tests and the play-checks never click against a server.

**The quality filter (closes `TODO(g4-quality-filter)`).** `CEnchantmentRegistry::EnchantAttribute2nd` asks `DBObj::GetByEnum(0x10000002, 3, 0x1000000C)`: enum 0x10000002 of enum group 3 resolves to portal file 0x0E010001, an `ACQualityFilter` (`QualityFilter::UnPack` 0x006B3490: the file id, eight counts and their id lists, then the attribute, secondary-attribute and skill counts and lists). `QueryAttribute2nd` (0x005CCBC0) is true for the listed ids; the file lists 1, 3 and 5 (the three maximums), and a secondary attribute not listed is left as it is (`QualityFilterData`, `EnchantmentRules.Attribute2ndFilter`; the filter loads with the tables in `GameSession.UseStatTablesFrom`, which closes `TODO(g4-quality-filter-install)`). **The skill groups (closes `TODO(g4-enchantment-affects-skills)`):** `EnchantmentRules.AffectsKey` is the cull's key match, adding the attack group (`Enchantment::AffectsAttackSkills` 0x005CBD10: type bit 0x10000 and skills 0x21, 0x22, 0x29, 0x2B to 0x2F, 0x31) and the defence group (`AffectsDefenseSkills` 0x005CBD60: type bit 0x20000 and skills 6, 7, 0xF, 0x30). The Defender aura (BuffStatMods 1599 to 1605, 4400) is a float stat (type 0x9008, key 169), not a skill, so it does not lift Melee Defense; "Aura of Defense" (2245) does, by key 6.

**Built, named TODOs and placeholders.** Built: the nine attribute rows and the skills with base and current values, both footers, the header (`TODO(m3-stat-header)` is closed), the raise and train buttons. Named: `TODO(m3-attribute-heritage)` (the header's gender and heritage line is the layout's art), `TODO(m3-pk-status)` (the kill-status line always reads non-player killer; a placeholder), `TODO(m3-character-titles)` (the titles tab is art only), `TODO(m3-skill-tooltips)` (`SkillInfoRegion::GetTooltip` 0x004F2C40), `TODO(m3-skill-aug-bonus)` (retail's `InqSkill` also adds the credit augmentation PropertyInt 365, the +10 specialisation augmentations 300 to 302, Jack of All Trades 326 and the luminance augmentation 344), `TODO(m3-stat-tables)` (logged when the XpTable or SkillTable has not loaded), `TODO(m3-skill-strings)` and `TODO(m3-attributes-strings)` (a footer string that does not render; the fallback shows). The luminance lines are empty below level 200, as retail.

### 23.3 The effects pages, the vitae page and the indicators (a3, fix A)

**Effects** (`GameEffectsPage`; `gmEffectsUI` 0x004B8180 to 0x004B9300; one controller per page, 4 beneficial and 5 harmful, told apart by the page's property 0x1000000C, 1 or 2, in `PostInit`). Elements: the list 0x10000123 (template 0: icon 0x10000129, name 0x1000012A, time 0x1000012B; row state 1 normal, 6 highlight), the info text 0x10000126, the help button 0x100000FC (the host answers it). The layout has no dispel or remove button. The list is `RebuildList` (0x004B8F70): `CACQualities::GetEnchantmentsInEffect` (0x0058FD80, over `CEnchantmentRegistry::GetEnchantmentsInEffect` 0x005953B0) takes the multiplicative list, then the additive list (not the cooldown list, not the vitae) and duels each entry into the result (`CEnchantmentRegistry::Duel` 0x00595120: the first entry of the same spell category; `Enchantment::Duel` 0x005CBC10 keeps it when its power is higher, or equal and it started earlier), so a stronger spell of the category and the lower layers of one spell are not listed. `SpellEffectMatchesUIType` (0x004B82E0) keeps the spells whose SpellTable bit 4 (beneficial) matches the page; a spell the table does not hold is skipped. Lines are inserted by name (`GetSortedInsertionPlace` 0x004B84E0, `strcmp`, ordinal here). `EffectRules.InEffect` (Session/Contracts) compares starts on the arrival-adjusted clock.

**The time** (`EffectInfoRegion::Update` 0x004F2860): start + duration - now; with a duration of 0 or more, `H:MM:SS` from an hour up, else `M:SS`, whole seconds truncated; a permanent or overdue entry shows no text; the whole column only when `PlayerModule::SpellDuration` (0x005D4110; character options bit 0x800000, set in ACE's default 0x50C4A54A) is on. Retail updates the durations once a second (`m_nextDurationUpdate`); Lifestone rewrites a line when its whole seconds change. `UpdateSelection` (0x004B8BB0): the selected row state 6, the others 1; the info box says `ID_Effects_Info_NoSpells`, `ID_Effects_Info_SelectASpell` (table 0x10000001) or the name, a blank line and the description. `RecvNotice_EnchantmentsChanged` and `RecvNotice_PlayerDescReceived` mark the list for a rebuild (`m_bNeedToRebuild` is `GameEffectsPage._dirty`); `Update` does nothing while the page is hidden, so the rebuild happens when it shows.

**The stale info box (D32; `--retail-effects-stale-info`).** `RebuildList` returns before `UpdateSelection` when nothing at all is in effect, so when the last effect ends retail's info box keeps the ended spell's name and description and the selection stays on it. Lifestone clears both when the list empties; the flag restores retail's (§14).

**Vitae** (`GameVitaePage`; `gmVitaeUI` 0x004A75C0 to 0x004A7B80, text box 0x100001C3). `Update` (0x004A7780): percentage = 100 - (u64)(vitae x 100 + 0.5); above 0 the text is `ID_Vitae_Text_Vitae` (variable PERCENTAGE) with `ID_Vitae_Text_Skills` (PERCENTAGE) and `ID_Vitae_Text_Experience` (variable EXPERIENCE, the experience still owed) appended; else `ID_Vitae_Text_Full`. Owed = `VitaeSystem::VitaeCPPoolThreshold` (0x005C9FB0: (level^2.5 x 2.5 + 20) x vitae^5 + 0.5, truncated) less PropertyInt 0x81 (the vitae CP pool); the level is PropertyInt 0x8B, else 0x19. The vitae is `CACQualities::GetVitaeValue` (0x0058FE80: the vitae enchantment's multiplier, 1 when none); `EffectRules` (InEffect, IsEnchanted, CountKinds, TryRemainingWholeSeconds, FormatDuration, the vitae maths) holds the rules. `RecvNotice_VitaeChanged` (0x004A7AA0): when the page is hidden and the new multiplier is lower than the one held (a worse penalty, a death) the page opens (`CM_UI::SendNotice_SetPanelVisibility(0xF, 1)`); otherwise the value is taken and the page updates when shown. The description arriving and PropertyInt 0x81 changing mark the page dirty (`OnQualityChanged`). The page reads the vitae from the qualities when it is built (fix B), because the staged wiring builds it after the login description.

**The indicators** (`GameIndicators`; closes `TODO(g4-indicators-gameplay)` for three of the four). The effects (0x100000F5 type 1, 0x100000F6 type 2; class 0x10000002), vitae (0x100000F4; 0x10000006) and burden (0x100000F7; 0x10000001) indicators are `UiStateIndicator` buttons (`gmUIElement_EffectsIndicator::Update` 0x004E76E0, `gmUIElement_VitaeIndicator::Update` 0x004E6C70, `gmUIElement_BurdenIndicator::Update` 0x004E7970). Effects: state 1 when the registry holds an entry of the kind, else 13 (ghosted); the counts are `CEnchantmentRegistry::UpdateSpellTotals` (0x00594AB0: every entry of the multiplicative and additive lists, surpassed ones and layers included; a spell id from 0x8000 up counts for neither; the spell's bit 4 says beneficial). Vitae: 1 when the multiplier is below 1, else 13. Burden: `CACQualities::InqLoad` (0x0058FF60: PropertyInt 5 over `EncumbranceCapacity` of the enchanted Strength and PropertyInt 0xE6): state 14 below 1, 15 from 1 to below 2, 16 from 2. The click is the button's own action (property 0x12: 0x10000006 page 4, 0x10000007 page 5, 0x1000000C page 15, 0x10000005 page 3); a ghosted button takes no mouse. Only the mini-game indicator (class 0x10000004) stays the layout's ghosted art (`TODO(g4-indicators-gameplay)`). Character information (§21.8) is rebuilt on every `QualitiesChanged` while page 3 is shown, and `UpdateLoad` uses the enchanted Strength.

**Named TODOs.** `TODO(m3-effects-spell-table)` (the SpellTable has not loaded: the pages list nothing for spells they cannot name and list them when the table answers), `TODO(m3-effects-strings)`, `TODO(m3-vitae-strings)` (a string that does not render), `TODO(m3-effects-page-missing)`, `TODO(m3-vitae-page-missing)`. Placeholders on screen: none.

### 23.4 The item widgets and the inventory page (b1)

**`UIElement_ItemList`** (0x10000031) is a `UiListBox` whose rows are `UiItem` cells made at run time from the template element in its property 0x1000000E, in the layout of enum 0x10000038. Property 0x10000015 is the fixed number of cells (the pack's 102, the side-pack column's 7, a paperdoll slot's 1; -1 grows), 0x10000011 marks a container list, 0x10000052 single selection. Lifestone makes the cells when the list is shown (24 at once, 12 a frame); `SetVisible` of a parent does not announce itself to its descendants, so a page calls `BeginFill()` from its `OnShown`. The list hears the mouse-down a cell sends: a left button selects, a double left button is `ItemActivated`. No drag and drop (D13; `UiInputRouter.DragElement` stays null).

**`UIElement_UIItem`** (0x10000032) composes an icon as `IconData::RenderIcons` (0x0058E200) does: the type background (enum group 0x10000004, the lowest set bit of the item type plus one, 33 for none), the custom underlay, then the icon with the custom overlay blended over it, whose pure white pixels are replaced by the UI-effects texture (enum group 0x10000005, the lowest set bit of the effects plus one, 33 for none: the magical glow); all 32 by 32, composed once per change. The count is the template's text 0x100004F5 (nested in the selected frame, which hides by blend 0 so the count stays); the capacity and structure bars are the meters 0x10000347 and 0x10000348, hidden unless a container holds something or a structure is below full. The retail UI may not name `Lifestone.Net` types (the architecture test): item fields reach it as plain values through `WorldObject.ItemFields()` (`Session/Contracts/WorldObjectIcon.cs`).

**The inventory page** (`GameInventoryPage`; `gmInventoryUI` 0x10000023, `gmBackpackUI` 0x10000022). The title (the retail inventory-of-name string), the contents titles (the main pack's and a side pack's, retail strings) and the burden text "<n>%" with n = floor(clamp(load / 3, 0, 1) x 300) (`gmBackpackUI::SetLoadLevel` 0x004A6EA0); the burden meter takes load / 3. The page redraws only for what it shows (fix B): the description, an enchantment change, PropertyInt 5 (burden), 0xE6 (the carrying augmentations, which `SetBurden` reads) and Strength (attribute 1); a vital or option change redraws nothing. The player-cell entry is a field rebuilt when the owner changes, and the title strings are rebuilt only when the name changes. A double click on a pack item with valid locations wields it through `IActionApi.Wield` at all its valid locations.

**Named TODOs.** `m3-use-from-pack` (retail uses the double-clicked item; M3 only wields), `m3-wield-slot-choice` (retail picks one free slot for a ring or bracelet), `m3-inventory-sidepack-contents` (a side pack's contents are known only from item events; no view-contents request is sent, D8), `m3-burden-capacity` (the server does not send the capacity; `PlayerInventory` derives it), `m3-itemlist-use`, `m3-itemlist-examine` (a right click examines in retail; the examination panel is not in M3), `m3-itemlist-empty-fill` (`UpdateEmptySlots` 0x004E3700; no M3 page grows), `m3-itemlist-template`, `m3-selected-object` (retail's selection is the player's one selected object; a list keeps its own), `m3-quantity-source` (how retail shows a pack stack's count with the frame hidden is not read), `m3-uiitem-extras` (the shortcut number, the sell and trade marks, the cooldown overlay), `m3-pickup` (a pickup from the ground is not built). Placeholders on screen: none beyond these.

### 23.5 The paperdoll and its figure (b2, b4)

**Slots** (`GamePaperDoll`, the second controller of page 7, beside `GameInventoryPage`; `gmPaperDollUI` 0x100001CD). It owns the 24 one-cell item lists and the slots checkbox 0x100005BE. The slot table is `gmPaperDollUI::GetLocationInfoFromElementID` (0x004A3B60): each list's element id to an equip-location mask and a side (1 left, 2 right); the weapon slot (0x100001DF) takes 0x3500000 (melee, missile, held, two-handed). The ammunition slot's mask (0x800000) is a string address in the decompile and is inferred. A slot shows the last `PlayerInventory.Equipped` entry whose location includes one of the slot's bits (`SetUIItemIntoLocation` 0x004A6300 over `RemakeCharacterInventory` 0x004A6930), so a multi-location item (a leather coat: chest, abdomen, upper arm, lower arm) shows in each of those slots. A worn item whose world object has not arrived is left out until it does; an empty slot keeps the layout's art. A double click on a worn item calls `IActionApi.MoveToPack(item, 0, 0, ActionSource.Ui)`: `ItemHolder::UseObject` (0x005898B0) with `DetermineUseResult` (0x00589290) = 2 for a wielded item leads `CPlayerSystem::UsingItem` (0x00563D10) to `PlaceInBackpack` (0x0055E5E0), which sends GameAction 0x0019 to the main pack; the slot empties and the pack list fills when GameEvent 0x0022 arrives (`InventoryChanged`). No drag and drop (D13). `PostInit` (0x004A56D0) hides the nine armour slots (head, chest, abdomen, upper arm, lower arm, hand, upper leg, lower leg, foot); the checkbox shows them and hides the figure and its click mask (`ListenToElementMessage` 0x004A5FA0).

**The figure** (`PaperDollFigure`, `ICreatureView`; `TODO(m3-paperdoll-figure)` is closed for a host that serves it). The viewport 0x100001D5 shows the player's own body. `PaperDollFigure` (Client, engine-agnostic) is a `CreaturePreview` of the player's Setup, motion and script tables and current appearance (`WorldObjectFigure.FigureFields` hands plain values and an `ObjDesc`: the architecture test forbids `PhysicsDesc` in the retail UI), at heading 191.3679 (`gmPaperDollUI::RedressCreature` 0x004A3F30), holding the paper-doll rest animation at frame 0 (0 fps). The camera is `PostInit`'s (0.12, -2.4, 0.88), direction 0, or `UpdateForRace`'s (0x004A4240) by the player's HeritageGroup (PropertyInt 0xBC): 6 and 7 at (0.12, -3.0, 0.88); 8 at (0.12, -3.4, 1.0); 9, 12 and 13 at (0.12, -3.4, 0.88); the Olthoi also take their own animation; the other heritages keep `PostInit`'s. The light is CharGen's (ambient 0.3, a distant light towards (0.3, 1.9, 0.65)). `ICreatureView` (version, preview, camera, backdrop) is what `CharGenViewportBinder` draws: `Cg3DViewModel` and `PaperDollFigure` implement it, so one binder serves both pages; `ICg3DViewHost.AttachFigure` (a default method that answers null) is `RetailUiLayer`'s `AttachView`.

**Lifecycle and z-order.** `GamePaperDoll` makes the figure at the page's first show (never at construction) and hands it to the host; it is made again only when the player's ObjDescEvent arrives or an item is worn or taken off (`RecvNotice_PlayerObjDescChanged` 0x004A46C0), in `Step`, so only while the page is shown; nothing steps the figure (a held pose). A served figure opens as retail does: the nine armour slots hidden, the viewport and its click mask shown; the checkbox swaps both ways without rebuilding. The viewport's picture sits on the viewport's own surface (§17), above the page's; the slots lie inside the viewport's box, so with a served figure each slot list and the checkbox own a surface (`UIElement::SetShouldOwnObject`), drawn after the viewport's in tree order: the slots show over the figure, which is drawn on creature mode's black.

**Deviation.** A host with no `CharGenSetup.View3D` / `ViewHost` serves no figure: the page then opens on the slots view (the armour slots shown, no figure) with `TODO(m3-paperdoll-figure)` logged, where retail always opens on the figure (§14). `GamePaperDoll.FigureBuilt` decides it.

**Named TODOs.** `m3-paperdoll-part-lighting` (`ApplyPartSelectionLighting` 0x004A4010), `m3-aetheria` (`UpdateAetheria` 0x004A41C0), `m3-paperdoll-clickmap` (`CreateClickMap` 0x004A4BC0), `m3-paperdoll-tooltips` (`UpdateItemSlotTooltip` 0x004A5200), `m3-paperdoll-checkbox-targets`, `m3-paperdoll-sharp` (`CreatureMode::UseSharpMode`), `m3-paperdoll-scale` (ObjScale), `m3-paperdoll-figure-data` (no player object or Setup yet).

### 23.6 The spell page: spellbook and components (b3)

**The page** (panel 13, 0x10000190) is a `UIElement_Panel` with two tabs: the spellbook (tab text 0x100002A9, element `gmSpellbookUI` 0x1000002E at 0x100002AC, the default) and the components (0x100002AA, `gmSpellComponentUI` 0x1000002F at 0x100002AD). The toolbar's spells button (0x10000198) toggles it. `GameSpellbookPage` and `GameComponentsPage` find their children and hear the tab element's messages; the tab's visibility message builds it, as the skills tab does.

**The spellbook.** The known spells (`Spellbook.Spells`) are planned when the tab shows or the book changes (filter `gmSpellbookUI::IsFilteredOut` 0x0048B190; order by display order, `GetSortedInsertionPlace` 0x0048B720, ties by spell id here) and written to the item list 0x10000295 eight a frame, in place (a cell already showing the right spell is left alone; surplus cells are deleted 24 a frame). Icons and names come from `IGameSessionView.Spell(id)`; while it answers null (the SpellTable decodes on a worker) the list stays empty and fills when the table arrives. The filter word: school bits creature 1, item 2, life 4, war 8, void 0x2000; level bits 0x10 (level 1) to 0x800 (level 8) and 0x1000 (level 9); a spell with no rough level (its first component is no scarab) is filtered out. A filter click is session-local: no `SpellbookFilterEvent` is sent (M2 D42 style; `TODO(m3-spellbook-filter-send)`). The layout has no selected-spell name or details (its two texts are captions), so a selection is the cell highlight; a learned spell (GameEvent 0x02C1) is selected and scrolled into view (`RecvNotice_SpellAdded` 0x0048BBB0). Delete spell (0x100002A5) answers NotInM3.

**The components.** Seven category titles (scarabs, herbs, gems, alchemical, talismans, tapers, peas; `gmSpellComponentUI::PostInit` 0x0048A4C0) over list templates 0 (title 0x10000466) and 1 (row 0x10000467: icon 0x10000468, name 0x10000469, count 0x1000046A, buy rate 0x1000046B). The tracker is the pack: items with ItemType 0x1000, summed by weenie class; the category comes from the pinned component map (`SpellPageData.CategoryOf`, pinned against the SpellComponentTable's types by a test; Chorizite and Moo Juice are potions); the desired amount is keyed by weenie class id (`SpellPageData.DesiredComponentAmount`; outside 0 to 5000 it shows 0). Typing a buy rate answers NotInM3. `Session/Contracts/SpellPageData.cs` hands the weenie class, the component flag and the desired amount over as plain values (the retail UI may not name `Lifestone.Net`).

**Named TODOs.** `m3-spellbook-delete` (deleting a spell has no action), `m3-spellbook-filter-send`, `m3-spellbook-shortcut` (the double-click shortcut; the spellcasting panel is item 3.2's, built: §23.8), `m3-spellbook-unknown-spell` (a spell in the book but not in the SpellTable is left out, as retail's `InqSpellBase` fails for it), `m3-spell-removal` (retail also takes a GameEvent that removes a spell), `m3-component-buy-rate` (no action sets the desired amount), `m3-component-icon` (a row draws the pack object's icon; retail draws the SpellComponentTable's), `m3-component-category` (a class outside the pinned map is listed under peas), `m3-component-zero-rows` (no row for a component the character wants but does not carry), `m3-component-strings`, `m3-components-template`. Placeholders on screen: the component icons (the pack object's own).

### 23.7 Checks and frames

**Frames.** `PanelFramesPlan.FramesFor` lists the pairs (page, tab button): 11 attributes (tab 0x10000228) and 11 skills (0x10000229), 13 spellbook (0x100002A9) and 13 components (0x100002AA), then 7, 4, 5 and 15 once each: eight frames for the six pages with controllers. The names say the tab (`11a-attributes`, `11b-skills`, `13a-spellbook`, `13b-components`; the others `<id>-<name>`), saved as `<frames dir>/<W>x<H>/panels/<name>.png` (gitignored, derived from the dats, never committed). `PanelFramesPlan.OpenTab(frame, pageElement)` opens the tab as a click does (`UIElement_Panel::OpenTab`); the play-checks' frame phase (`PlayChecks.Panels.cs`, with `--play-frames`) and `--play-panel-shots` (`PlayMode.PanelShots.cs`) both call it before the shot. `every-registered-panel-page-has-its-frame` counts the pairs (`captured=8/8 pages=6`).

**Play-checks** (the stand-in; each page's hook opens its page, judges, and closes it, leaving the host closed). `a-page-with-a-controller-is-built-and-the-others-say-not-in-m3` and `the-panel-controllers-are-wired-after-the-shell` (s0). Attributes: `the-attributes-tab-shows-the-stand-ins-strength-and-max-mana` also compares the value colours of the Strength and Max Mana rows with the rule of `AttributeInfoRegion::Update` and `Attribute2ndInfoRegion::Update` on `StatValues`' numbers (R18; `strength_colour=1 want=1`, `mana_colour=1 want=1` with vitae -3). Skills: `skills-tab-lists-the-skills-and-shows-war-magic-as-the-read-model-has-it`. Effects: `effects-page-4-...` (beneficial spells in name order with times) and `effects-page-5-...` (harmful: `lines > 0` and the line is spell 230, Vulnerability Other II, which the stand-in's probe enchantments add). Vitae: `vitae-page-states-the-penalty-percentage-and-the-experience-owed-and-the-vitae-indicator-is-lit` (multiplier 0.95, percentage 5, the page text holds "5%" and the experience `EffectRules.VitaeExperienceOwed` gives, the indicator in state Normal). Indicators: `the-effects-indicator-is-lit-by-the-registry-and-its-click-opens-page-4`, `the-burden-indicator-shows-the-load-state-the-read-model-gives`. Inventory: the pack's items in order, a stack's count, the player's cell, the side packs, the burden and the title, and `the-inventory-page-closes-and-draws-nothing-while-hidden`. Paperdoll: the stand-in's wielded items in their slots as the model has them (the coat in four), a slot emptying when its item is taken off, nothing drawn while hidden, and `the-paperdoll-opens-on-the-players-3d-figure-served-to-its-viewport-with-the-armour-slots-hidden`. Spell page: `the-spellbook-lists-the-known-spells-that-pass-the-filter-in-retails-order-as-the-book-has-them`, `the-components-tab-lists-the-packs-components-by-category-with-summed-counts`, `the-spell-page-closes-and-draws-nothing-while-hidden`.

**What the spellbook check proves (fix B, R5).** The stand-in's `StandInPlayerStats` carries `ProbeSpellbook` (40 real spell ids: the lowest id of each school and rough level 1 to 8, with the filter bits each needs, taken by hand from a dump of the retail SpellTable), `ProbeUnknownSpell` (60001, not in the table), `ProbeSpellbookFilters` (0x3FFF with the level 8 bit cleared: 0x37FF) and `ProbeEnchantments()` (spell 230 and the vitae, spell 666, multiplier 0.95). Both description paths (the synthetic `Description(stats)` and a replayed recording, `RecordedFor`) reach `StandInSessions.WithProbe`, which replaces a recording's spells and filter word with the probe's and appends the probe enchantments to its registry (144 recorded entries untouched: `registry=146`); logged once as `TODO(m3-standin-probe-spellbook)`, a placeholder. The retail table has no spell with rough level 0 or above 8 and none the default word hides (6266 spells), so "hidden by the filter" is the level 8 bit cleared in the word sent (5 of the 40) and "no level" is the id the table lacks. The check judges `listed > 0`; `listed == 35`, counted from the probe rows' bits and the word sent, not the page's `IsFilteredOut`; no hidden row and no unknown id listed; the first ids `[2092, 2120, 59]` and the last `234` fixed; the table's `DisplayOrder` rising along the list; cells equal listed. `PlayExpectations.For(recorded, stats)` derives the expected effect count and vital maxima from the description as sent (the vitae lowers them), on both paths.

**Tests.** Client tests per panel over the real layout and a scripted description: `GamePanelsTests`, `NotInM3Tests` and `GameShellG4Tests` (the seam), `GameAttributesTabTests`, `GameSkillsTabTests`, `EnchantmentRulesTests`, `QualityFilterDataTests`, `SkillRulesTests`, `GameEffectsPageTests`, `GameInventoryPageTests`, `GamePaperDollTests`, `GameSpellPageTests` (spellbook and components) and `PanelLayoutDumpTests` (the six layout dumps). The 247-spell fill test (`A_book_of_247_spells_is_written_over_frames_in_bounded_units_of_work`) judges work, not time: per unit at most `SpellsPerFrame` written and no more cells created, 31 units give or take one, a unit under 250,000 allocated bytes, 247 cells occupied at the end; the times are printed, report-only.

**M3 3.2 additions to the frames and checks (§23.8).** `PanelFramesPlan.WindowFrames` lists the combat windows' frames, `c1-combat` and `c1-powerbar`, taken in melee mode through the UI's own notice (never a session action) with the power bar begun by hand at a placeholder 0.6 (nothing in M3 begins it); `PanelFramesPlan.SpellcastingFrame` (`c2-spellcasting`, Magic mode) joins them in `AllWindowFrames`, which `--play-panel-shots` uses. The play-checks' frame phase saves `WindowFrames` under `<frames dir>/panels/` (log line `window frames done captured=2/2`, separate from `panel frames done captured=8`), and `--play-panel-shots` saves all three after the pages (they count in its `panel frames done captured=N`). The spellcasting shot opens the first tab that holds spells (`FirstTabWithSpells`; play keeps retail's first tab). Play-checks: `PlayChecks.PanelC1.cs` (`the-combat-button-puts-the-player-in-melee-mode-and-the-combat-window-shows-with-the-melee-button`, `magic-mode-hides-the-combat-window-and-shows-the-magic-button`, `the-mode-button-returns-to-peace-and-hides-the-windows`) and `PlayChecks.PanelC2.cs` (six); the shell check `a-combat-button-opens-the-not-in-m3-message` is gone. Tests: `GameCombatPanelTests` (10), `GameSpellcastingPanelTests` (9) and `PanelLayoutDumpTests` (the three window dumps).

### 23.8 The combat windows (M3 3.2)

Item 3.2 builds the three windows of the combat UI and the toolbar's combat buttons. They are **windows of the gameplay layout** (`classic_gameplay`, root 0x10000495), not host pages: gmCombatUI (class 0x1000000C) is element 0x1000005C and gmSpellcastingUI (0x10000015) is element 0x10000061, both children of gmFloatyCombatPanelUI (class 0x10000054) element 0x100006B5; gmPowerbarUI (0x1000000F) is element 0x10000044 inside the SmartBox frame 0x1000049A. Code: `Client/RetailUi/Panels/GameCombatPanel.cs`, `GamePowerBar.cs`, `GameSpellcastingPanel.cs`, the hooks `GamePanels.C1.cs` and `.C2.cs` and `Toolbar/GameToolbar.cs`; plan and slices: `docs/milestones/m3-plan.md` §4.8. The subtrees are dumped to `cache/m3/3-2/layout-combat.txt`, `layout-powerbar.txt` and `layout-spellcasting.txt` by `PanelLayoutDumpTests` (a dat test; gitignored). Classes held: combat 0x01 0x03 0x07 0x0B 0x0C 0x11 0x1000000C 0x10000035; power bar 0x03 0x07 0x0C 0x1000000F; spellcasting 0x01 0x03 0x08 0x0B 0x0C 0x10000015 0x10000031 (`UIElement_ItemList`).

**The seam.** A floaty window that is not a host page is not an `IGamePanel` and is registered under no panel id: its class is an element of its own, registered in `RegisterC1Classes` (`GameCombatPanel` 0x1000000C, `GamePowerBar` 0x1000000F, `UiFloatyCombatPanel` 0x10000054, shared with the spellcasting window) and in `RegisterC2Classes` (`GameSpellcastingPanel` 0x10000015), and its end of life is `UiElement.OnDeleting`, so `GamePanels.Dispose` has nothing to do and the `HandlerCount` test holds. The `QueueC1` and `QueueC2` factories only hand the elements the session and return null. Until 3.2's slice s0 the three classes were plain containers (`GamePlayScreen.RegisterClasses`, so a login logged no stand-in warning) and the power bar was the "stays loud" class; it is no longer. **Hidden until staged (review R9):** `PostInit` of `GameCombatPanel` and `GameSpellcastingPanel` hides the window (the layout shows them by default and they are shown by mode in `Attach`, inside the staged unit after the shell's other controllers), so the shell cannot draw a spellcasting window in non-combat while it builds; a test fails without it.

**The toolbar's combat buttons.** The four combat-mode buttons 0x10000192-0x10000195 answer `gmToolbarUI::ListenToElementMessage` (11.6096 0x004BFB20) with `ClientCombatSystem::ToggleCombatMode` (0x0056D610): from non-combat it asks `IActionApi.SetCombatMode(ActionSource.Ui)` for `GetDefaultCombatMode`'s mode (0x0056C050: a melee, missile or two-handed weapon gives melee or missile, where Lifestone reads the slot, MissileWeapon giving missile, `TODO(m3-combat-use)`; a held caster gives magic; a held non-caster says "You can't enter combat mode while wielding the <name>" and stays in peace; nothing wielded gives melee), from any other mode it asks for non-combat. `CombatModeRules.Toggle` is the one path and calls `Actions.SetCombatMode` with the Ui source only. The layout's button for the mode shows (`gmToolbarUI::RecvNotice_SetCombatMode` 0x004BE2A0), live on `CombatModeChanged`; the notice rides the combat window's subscription because `GameToolbar` has no end of life. Examine still answers NotInM3. `TODO(m3-toolbar-shortcut-ghost)`: retail also ghosts the spell shortcuts outside magic mode; the 18 slots wait for items.

**`gmCombatUI` (`GameCombatPanel`; 11 functions, 11.6096 0x004CCC20-0x004CD210).** `RecvNotice_SetCombatMode` (0x004CD210) shows the window in melee (state 0x10000003) and missile (state 0x10000004) and hides it in peace and magic; with the AdvancedCombatUI option (word 1, 0x1000) it hides in every mode; melee also shows the recklessness field 0x100005EF when skill 0x32 is trained (class 2 or more). Controls: the scrollbar 0x1000004F (power, message 10: thousandths clamped to 0..1, `m_rUIRequestedPower`), the meter 0x10000050 (`RecvNotice_SetPowerbarLevel`, only for power bar mode 1), the group box 0x10000056 with the buttons 0x10000057 high, 0x10000058 medium and 0x10000059 low (a mouse down, 0x1C, requests the height; the click is `EndAttackRequest`, which M3 answers with NotInM3 "Melee and missile attacks", `TODO(m3-melee-attack)`: the attack buttons answer NotInM3 by design, D8's neighbour), and three option check boxes 0x10000053-55 (AutoRepeatAttack, AutoTarget, ViewCombatTarget; labels `ID_CombatPanelOption_*` in table 0x10000003), ghosted in M3 like the options page (`TODO(m3-combat-options)`, `TODO(m3-combat-tooltips)`).

**`gmFloatyCombatPanelUI`.** Class 0x10000054, window 0x100006B5: shown while one of its children with a panel id (the combat window, the spellcasting window) is visible (retail: `gmCombatPanelUI::ListenToElementMessage` 11.6096 0x004F64F0 and `RecvNotice_SetPanelVisibility` 0x004F6560, without the remembered-panel stack). `gmFloatyPowerBarUI` (0x10000053) has no window in the gameplay layout: a plain container.

**`gmPowerbarUI` (`GamePowerBar`; 12 functions, 0x004DB000-0x004DB390).** Window 0x10000044 (760x15, the bottom of the world view), hidden until `Begin(mode)`: mode 2 (advanced) shows the melee state 0x10000043 with the recklessness field 0x100005EE by skill, else 0x10000044; mode 3 (jump) 0x10000042; mode 4 (download) 0x10000045, whose text is the `DddStatus` wording (`ID_Powerbar_DDDModeText`); mode 1 does nothing. `SetLevel` acts only for the mode that began; `Finish` hides. Nothing begins it in M3 (no melee attack, no jump): the API is the attack action's (`TODO(m3-melee-attack)`) and the review frame drives it by hand at 0.6, **a placeholder**.

**`gmSpellcastingUI` (`GameSpellcastingPanel`; the class spans 11.6096 0x004C6400-0x004C86A0).** Shown in Magic mode (8) and hidden in every other (`RecvNotice_SetCombatMode`, 0x004C7E70; the floaty follows), from `Attach`, `CombatModeChanged` and the description. It holds **eight tabs** (page 0x100000AA to 0x100000B0 and 0x100005C3 with tab 0x100000A3 to 0x100000A9 and 0x100005C2, `PostInit` 0x004C68F0), one `Spellbook.SpellBars` bar each, on the item list 0x100000B6 of the page. A spell cell shows the SpellTable's icon, its name and the tooltip "name, new line, Double-click to cast this spell" (`SpellCastSubMenu::UpdateFromPlayerModule` 0x004C7160). Only the shown tab is built, and again only when its bar (or how many of its spells are known) changed; a favourite the character does not know is left out (retail drops it from the player module: nothing is written, `TODO(m3-spellcast-unknown-favourite)`). `gmSpellcastingUI::Register` is 0x004C6C20. **The death scenario's bars are on tabs II and III** (the bar id is 0-based, `docs/session.md` §32); the first tab shows what the character had.
- *Select, double click, Cast.* A left click selects a spell (`ListenToElementMessage` 11.6096 0x004C86A0 over `SpellCastSubMenu::SetSelected` 0x004C66F0: message 7 selects): the cell highlights, the name text 0x1000048B and the cast button 0x100000B2 follow (`UpdateCastButtonTooltip` 0x004C7620). A double click selects and casts (message 10), and the cast button casts the selected spell (`gmSpellcastingUI::Cast` 0x004C6C40; with none selected the chat line "You must select a spell to cast"). `ClientMagicSystem::CastSpell` (0x00568DE0) decides: a spell with the SelfTargeted flag (bit 8) is a targeted cast at the player (`FreeHandsAndCastSpell`), any other at the selected object, and with none "You must select a suitable target before casting this spell" (nothing sent). Lifestone sends `IActionApi.Cast(spell, target, ActionSource.Ui)`. No automation clicks a window (D29): the death leg sets its mode through the Action API as Host, and `--play-panel-shots` changes only the UI notices and puts them back.
- *Named TODOs.* `m3-cast-untargeted` (retail's formula targeting type, `CSpellBase::IsUntargeted` 0x005974A0; here every spell that is not SelfTargeted needs a selection), `m3-cast-components`, `m3-cast-compat`, `m3-spellcast-selection-notice`, `m3-spellcast-endowment`, `m3-spellcast-hotkeys`, `m3-spellcast-unknown-favourite`, `m3-spellcast-unknown-spell`, `m3-cast-unknown-spell`, `m3-cast-selection-missing`, `m3-spellcast-list-missing`, `m3-spellcast-window-missing`, `m3-standin-bars-live` (the stand-in sends its bars only with its next description, so a play-check cannot list them live). Guards that fail loudly if the layout lacks a window: `m3-combat-window-missing`, `m3-powerbar-window-missing`. `m3-powerbar-strings`: the download text (`ID_Powerbar_DDDModeText`) does not render in the string tables, so it is a placeholder. Placeholders on screen: the power bar's 0.6 in its frame; the spellcasting window shows none (the endowment icon is hidden).

**The vitae page and a death.** Page 15 opens by itself at a worse vitae: `gmVitaeUI::RecvNotice_VitaeChanged` (11.6096 0x004A7AA0, §23.3) sends `CM_UI::SendNotice_SetPanelVisibility(0xF, 1)` when the page is hidden and the new multiplier is lower than the one held, so the death leg's dead and lifestone frames show the vitae page open: retail's behaviour, left as is (`GameVitaePage.OnEnchantmentChanged` calls `GamePanelHost.SetPanelVisibility(15, true)`). The vitals windows follow the 0x02E9 updates through the death (0.00 at the death, 0.75 after the arrival; `docs/session.md` §32).

**Live frames** (the lead, BSD Mage at Holtburg, `--play-panel-shots`): `c1-combat` in melee (Speed/Power, Repeat Attacks, Auto Target, Keep in View, High/Medium/Low), `c1-powerbar` (the Power meter under the chat) and `c2-spellcasting` in magic (tabs I-VIII, Cast). The first spellcasting frame showed tab I empty because the death scenario's bars are on tabs II and III; the shot now opens the first tab with spells. The windows are staged by the UI's own notices, not by clicks or session actions, which the owner should know when reading the frames.
