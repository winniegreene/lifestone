using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Screens;

/// <summary>
/// The disconnected screen (retail's <c>gmDisconnectedUI</c>): layout 0x10000003 (<c>disconnected</c>), root 0x10000416, the reason in a
/// text (0x10000417) and one button (0x10000418). The flow queues it with the reason: a character error that ends the session, or the
/// lost connection (<see cref="GmUiFlow.RecvNoticeServerDied"/>).
/// </summary>
/// <remarks>
/// <para>
/// Re-expressed from <c>gmDisconnectedUI::gmDisconnectedUI</c> (11.4186 0x004E9A10; 11.6096 not in the symbol table, predicted
/// 0x004EA6A0, TODO(retailui-data)), <c>SetErrorMsg</c> (0x004E99F0, 0x004EA680) and <c>ListenToElementMessage</c> (0x004E99C0,
/// 0x004EA650). The reason is shown by <see cref="DisplayError"/> (retail's <c>SetErrorMsg</c> sets the text's string).
/// </para>
/// <para>
/// <b>The button.</b> Retail's queues the epilogue, which ends the application. Owner decision D43: it reconnects instead, through the
/// application (<see cref="IUiLoginHost.Reconnect"/>) and the data check starts again; <see cref="UiLoginContext.RetailDisconnect"/>
/// (<c>--retail-disconnect</c>) restores retail's exit. When the application cannot reconnect, the screen exits too, loudly.
/// </para>
/// </remarks>
public sealed class DisconnectedScreen : UiLoginScreen
{
    /// <summary>The reason text.</summary>
    public const uint ErrorTextId = 0x10000417;

    /// <summary>The button.</summary>
    public const uint ButtonId = 0x10000418;

    /// <summary>
    /// What the button says when it reconnects (D43; a Lifestone literal): retail's own label, "Quit Game", would be wrong for it.
    /// <c>--retail-disconnect</c> keeps retail's label and retail's exit.
    /// </summary>
    public const string ReconnectLabel = "Reconnect";

    private readonly UiTextElement? _errorText;

    /// <summary>The disconnected screen over <paramref name="context"/>.</summary>
    public DisconnectedScreen(UiLoginContext context)
        : base(context, UiLayoutEnums.DisconnectedRoot, "gmDisconnectedUI")
    {
        _errorText = Find<UiTextElement>(ErrorTextId);
        if (Find<UiButton>(ButtonId) is { } button && !context.RetailDisconnect)
        {
            button.SetText(ReconnectLabel);
        }
    }

    /// <summary>The reason now shown.</summary>
    public string ReasonText => TextOf(_errorText);

    /// <summary><c>gmDisconnectedUI::SetErrorMsg</c>: the text shows the reason.</summary>
    public override void DisplayError(in UiStringRef text) => _errorText?.SetStringInfo(text);

    /// <inheritdoc/>
    protected override UiListenResult OnElementMessage(in UiElementMessage message)
    {
        if (message.MessageId == UiMessageIds.ButtonClick && message.ElementId == ButtonId)
        {
            OnButton();
        }

        return UiListenResult.Default;
    }

    private void OnButton()
    {
        if (Context.RetailDisconnect)
        {
            Context.Flow.QueueUIMode(UiModes.Epilogue);
            return;
        }

        // D43: reconnect. The host resets the session for a new login; the flow then starts at the data check, as at launch.
        if (Context.Host.Reconnect())
        {
            Log.Info(UiElementManager.Category, "disconnected: reconnecting (D43)");
            Context.Flow.QueueUIMode(UiModes.DataPatch);
            return;
        }

        Log.Warn(UiElementManager.Category, "TODO(retailui-reconnect): the host cannot reconnect, so the disconnected screen exits as retail's does");
        Context.Flow.QueueUIMode(UiModes.Epilogue);
    }
}
