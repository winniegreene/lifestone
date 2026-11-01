using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Panels;

// M5 7 D137: the inscription of the item window, re-expressed from ItemExamineUI::SetInscription (11.6096 0x004AE670), SetInscriptionEditableState (0x004ACAA0), HandleInscriptionMousePresses (0x004AC5B0),
// HandleInscriptionGainingFocus (0x004AC460) and HandleInscriptionLosingFocus (0x004AE9A0), which gmExaminationUI::ListenToElementMessage (0x004B6CE0) calls: a left press on the inscription background 0x10000137, and the
// focus message of the inscription text 0x1000013E. The write is CM_Writing::Event_SetInscription (0x006AA810) through IActionApi.SetInscription.
public sealed partial class GameExaminePanel
{
    /// <summary>The item window's inscription signature text ("--" and the scribe's name; <c>m_itemInscriptionSignatureText</c>).</summary>
    public const uint SignatureId = 0x1000013F;

    /// <summary>The item window's inscription background (<c>m_itemInscriptionBackground</c>): a left press on it asks whether the inscription may be written.</summary>
    public const uint InscriptionBackgroundId = 0x10000137;

    /// <summary>What an inscribable item with no inscription shows in the box (<c>SetInscription</c>'s literal).</summary>
    public const string InscribeHere = "<Inscribe here>";

    /// <summary>ObjectDescriptionFlags bit 0x2, Inscribable (ACE <c>ObjectDescriptionFlag.Inscribable</c>; retail's <c>pwd._bitfield</c> bit 1).</summary>
    public const uint InscribableFlag = 0x2;

    /// <summary>The hook profile's Inscribable flag (ACE <c>HookFlags.Inscribable</c> 0x1; <c>AppraisalProfile::IsHookedItemInscribable</c> 0x005B4040).</summary>
    public const uint HookInscribableFlag = 0x1;

    private UiTextElement? _inscription;
    private UiTextElement? _signature;
    private bool _inscriptionLooked;
    private bool _inscribable;
    private string _scribe = string.Empty;
    private string _oldInscription = string.Empty;

    /// <summary>The inscription text the box shows ("&lt;Inscribe here&gt;" for an empty inscribable item; empty when the box is hidden).</summary>
    public string InscriptionText => _inscription?.Text ?? string.Empty;

    /// <summary>The signature line under it ("--" and the scribe's name), or empty.</summary>
    public string SignatureText => _signature?.Text ?? string.Empty;

    /// <summary>True while the inscription box shows (the item is inscribable).</summary>
    public bool InscriptionShown => _inscription is { Visible: true };

    /// <summary>True while the player may write the inscription (<c>SetInscriptionEditableState</c>: an item the player holds, with no scribe or this character as its scribe).</summary>
    public bool InscriptionEditable => _inscription is { Editable: true };

    /// <summary>The scribe the window holds for the shown item (the appraisal's string 8, or this character's name once it wrote one), or empty.</summary>
    public string Scribe => _scribe;

    /// <summary>The last inscription write the window sent, or null.</summary>
    public ActionHandle? LastInscription { get; private set; }

    private void FindInscription()
    {
        if (_inscriptionLooked)
        {
            return;
        }

        _inscriptionLooked = true;
        _inscription = _window.GetChildRecursive(InscriptionId) as UiTextElement;
        _signature = _window.GetChildRecursive(SignatureId) as UiTextElement;
        if (_inscription is null || _signature is null)
        {
            _context.Manager.LogOnce("examine:inscription-missing", LogSeverity.Error, $"gmExaminationUI: the layout lacks the inscription text 0x{InscriptionId:X8} or its signature 0x{SignatureId:X8}; no inscription is shown or written (TODO(m5-examine-inscription-missing))");
        }
    }

    /// <summary>The window opened on a new object: the box is empty and hidden until the answer says whether the item is inscribable.</summary>
    private void ClearInscription()
    {
        FindInscription();
        _inscribable = false;
        _scribe = string.Empty;
        _oldInscription = string.Empty;
        _inscription?.SetText(string.Empty);
        _signature?.SetText(string.Empty);
        _inscription?.SetVisible(false);
        _signature?.SetVisible(false);
        SetEditable(false);
    }

    /// <summary>
    /// <c>ItemExamineUI::SetInscription</c> (0x004AE670): an item that is not inscribable (its ObjectDescriptionFlags, or for a hook its hook profile) hides the box; one with no scribe shows "&lt;Inscribe here&gt;"; one with a scribe
    /// and an inscription shows the text and "--" with the scribe's name (retail adds the scribe's account for an admin, string 23; a player sees none). Then the editable state.
    /// </summary>
    private void SetInscription(Appraisal appraisal, WorldObject? obj)
    {
        ClearInscription();
        _inscribable = appraisal.HookFlags is { } hook ? (hook & HookInscribableFlag) != 0 : obj is not null && (obj.DescriptionFlags & InscribableFlag) != 0;
        if (!_inscribable || _inscription is null || _signature is null)
        {
            return;
        }

        _scribe = appraisal.String(8) ?? string.Empty;
        if (_scribe.Length == 0)
        {
            _inscription.SetText(InscribeHere);
        }
        else if (appraisal.String(7) is { Length: > 0 } text)
        {
            _oldInscription = text;
            _inscription.SetText(text);
            _signature.SetText("--" + _scribe);
        }

        _inscription.SetVisible(true);
        _signature.SetVisible(true);
        SetEditable(MayInscribe());
    }

    /// <summary><c>SetInscriptionEditableState</c> (0x004ACAA0): the player may write an inscribable item it holds when the item has no scribe or this character is its scribe (retail's admin exception is not a player's).</summary>
    private bool MayInscribe() => _inscribable && Holds(ShownGuid) && (_scribe.Length == 0 || string.Equals(_scribe, PlayerName, StringComparison.OrdinalIgnoreCase));

    private string PlayerName => _context.Session.State.Self.Name ?? string.Empty;

    private bool Holds(uint guid)
    {
        var state = _context.Session.State;
        if (guid == 0)
        {
            return false;
        }

        if (state.Self.Inventory.Contains(guid))
        {
            return true;
        }

        // ACCWeenieObject::IsOwnedByPlayer: worn, in the main pack, or in a pack the player holds.
        return state.Objects.TryGet(guid, out var obj) && (obj.WielderId == state.Self.Guid || obj.ContainerId == state.Self.Guid || (obj.ContainerId is { } pack && state.Self.Inventory.Contains(pack)));
    }

    private void SetEditable(bool editable)
    {
        _inscription?.SetBoolProperty(UiPropertyIds.TextEditable, editable);
        _inscription?.SetBoolProperty(UiPropertyIds.TextSelectable, editable);
    }

    /// <summary>
    /// <c>HandleInscriptionMousePresses</c> (0x004AC5B0): a left press on the box while it cannot be written says why in the spew box (retail's text type 0x1A): another character's inscription, an item the player does not hold, or an item
    /// that is not inscribable. A writable box says nothing (the press gives it the focus).
    /// </summary>
    private void InscriptionPressed()
    {
        if (ShownGuid == 0 || Kind != ExamineKind.Item || InscriptionEditable)
        {
            return;
        }

        string text;
        if (_scribe.Length > 0 && !string.Equals(_scribe, PlayerName, StringComparison.Ordinal))
        {
            text = "Only " + _scribe + " can change the inscription";
        }
        else if (!_inscribable)
        {
            text = "This item is not inscribable.";
        }
        else if (!Holds(ShownGuid))
        {
            text = "Item must be in your inventory to inscribe.";
        }
        else
        {
            return;
        }

        LastInscriptionNotice = text;
        _context.Manager.Services.Log.Info("retailui", $"examine inscription refused item=0x{ShownGuid:X8} why=\"{text}\"");
        _context.Notices?.DisplayStringInfo(UiNoticeRouter.SpewBoxTextType, UiStringRef.FromLiteral(text));
    }

    /// <summary>The last reason a press on the box gave for not writing it (tests), or null.</summary>
    public string? LastInscriptionNotice { get; private set; }

    /// <summary><c>HandleInscriptionGainingFocus</c> (0x004AC460): with no scribe the "&lt;Inscribe here&gt;" goes; the signature reads "--" and this character's name while it writes.</summary>
    private void InscriptionFocused()
    {
        if (_inscription is null || _signature is null || !InscriptionEditable)
        {
            return;
        }

        if (_scribe.Length == 0)
        {
            _inscription.SetText(string.Empty);
        }

        _signature.SetText("--" + PlayerName);
    }

    /// <summary>
    /// <c>HandleInscriptionLosingFocus</c> (0x004AE9A0): the text the box holds is sent (<see cref="IActionApi.SetInscription"/>) when it differs from the old one and it is not an empty box that never had a scribe; an empty text
    /// puts "&lt;Inscribe here&gt;" back and clears the signature and the scribe, any other makes this character the scribe.
    /// </summary>
    private void InscriptionUnfocused()
    {
        if (_inscription is null || _signature is null || ShownGuid == 0 || !_inscribable)
        {
            return;
        }

        var text = _inscription.Text;
        if ((text.Length > 0 || _scribe.Length > 0) && !string.Equals(text, _oldInscription, StringComparison.Ordinal))
        {
            LastInscription = _context.Session.Actions.SetInscription(ShownGuid, text, ActionSource.Ui);
            _context.Manager.Services.Log.Info("retailui", $"examine inscription sent item=0x{ShownGuid:X8} chars={text.Length}");
        }

        if (text.Length == 0)
        {
            _inscription.SetText(InscribeHere);
            _signature.SetText(string.Empty);
            _scribe = string.Empty;
        }
        else
        {
            _scribe = PlayerName;
        }

        _oldInscription = text;
    }

    /// <summary>The inscription's two messages (<c>gmExaminationUI::ListenToElementMessage</c> 0x004B6CE0); true when one was taken.</summary>
    private bool InscriptionMessage(in UiElementMessage message)
    {
        if (message.ElementId == InscriptionBackgroundId && message.MessageId == UiMessageIds.MouseDown && message.Param1 == UiMouseActions.Left)
        {
            InscriptionPressed();
            return false; // the press goes on (the background is not a button)
        }

        if (message.ElementId == InscriptionId && message.MessageId == UiMessageIds.Focus)
        {
            if (message.Param1 != 0)
            {
                InscriptionFocused();
            }
            else
            {
                InscriptionUnfocused();
            }
        }

        return false;
    }
}
