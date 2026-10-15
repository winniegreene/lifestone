# Third-party sources: character creation screens (M2 G3 U5, both stages)

This page covers `src/Lifestone.Client/RetailUi/Screens/CharGen*.cs` and their tests ([retail-ui.md](../retail-ui.md) §19). The product code is
re-expressed from the retail decompile by function; nothing is copied, and no file carries a port header. Lifestone is AGPL-3.0. Nothing derived
from the user's dats is committed: the tests read the dats at run time and pin counts, ids and strings' first words only.

## Sources

| Source | Licence | Pin | Used for |
|---|---|---|---|
| Mag-nus/acclient-AI-RE, `2013-09 11.4186/acclient-src/src/GAME/game_ui_misc/` (`gmCharGenMainUI.c`, `gmCGHeritagePage.c`, `gmCGProfessionPage.c`, `gmCGSkillsPage.c`), `ENGINE/uielement_types/UIElement_Text.c` and `AC/accharacter/CharGenState.c` (Hex-Rays output with PDB names) | Turbine/WB IP; reference only | commit 01ccd5be2d8fff40e25151d804fa97b3e8f27ec6, read 2026-10-13 into session scratch | Behaviour of the functions listed in [retail-addresses-m2.md](../research/retail-addresses-m2.md) §2 for the retail-ui track (the 37 rows of G3 U5): the page flow, element ids, state ids, the strings' tokens and the skill-row rules. |
| Mag-nus/acclient-AI-RE, `2013-09 11.4186/acclient-src/src/GAME/game_ui_misc/` (`gmCGAppearancePage.c`, `gmCGTownPage.c`, `gmCGSummaryPage.c`, `gmCG3DView.c`, `gmCharGenMainUI.c` again for `DoFinish`, `RecvNotice_CharGenVerificationResponse`, `Update` and the dialogs), `ENGINE/render_base/SurfaceWindow.c` and `_other/misc/SoftwareBlit.c` (the colour spots' and the disk's surface calls), `ENGINE/uielement_system/UIElement.c` and `ENGINE/uielement_util_dialog/DialogFactory.c` (the visibility change and the dialog's close), `include/acclient_data.h` and `acclient.h` (the global name arrays' first entries; the state's field offsets) | Turbine/WB IP; reference only | the same commit 01ccd5be2d8fff40e25151d804fa97b3e8f27ec6, read 2026-10-13 into session scratch | Behaviour of the functions listed in [retail-addresses-m2.md](../research/retail-addresses-m2.md) §2 for the second stage: the appearance page's spinners, wheel, zoom and turn; the town page's states; the summary page's list, name field and how-to text; the 3D view's camera, light and update; Finish, the credit and name checks and the verification answers; the multiply and replace-colour loops. |
| Mag-nus/acclient-AI-RE, `symbol_transplant_11.6096.tsv` | Turbine/WB IP; reference only | the lead's pinned copy (owner decision D4) | The 11.6096 addresses cited in the sources and docs. |

The literals in the second stage's code that are retail's own (the summary list's "Profession: ", "Gender: ", "Heritage: ", "Starting Town: ", "Attributes", the ten attribute and credit
labels and the four skill headings, and the name "Custom" of template 0, from the first entry of the client's profession array) are taken from `gmCGSummaryPage::SetSummaryText` and the
data header. The literals in the first stage's pages that are retail's own (the skill formula's "Formula : ", " + ", " / ", "Training Bonus  +5" and "Specialization Bonus  +10")
are taken from the same functions (`gmCGSkillsPage::MakeSkillFormula` and `ShowSkillsText`); every other text shown comes from the user's dats (the pregame string
table and the layout), at run time.
