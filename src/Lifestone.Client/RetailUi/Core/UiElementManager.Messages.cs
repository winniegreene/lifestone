namespace Lifestone.Client.RetailUi.Core;

public sealed partial class UiElementManager
{
    private readonly Queue<UiElementMessage> _queuedMessages = new();
    private readonly Dictionary<IUiElementListener, uint> _heard = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(uint ElementId, uint MessageId), List<IUiElementListener>> _elementListeners = [];
    private readonly Dictionary<uint, List<IUiGlobalListener>> _globalListeners = [];
    private readonly Dictionary<uint, List<UiElement>> _visibilityToggleActions = [];
    private readonly List<IUiGlobalListener[]> _globalScratches = [];
    private IUiElementListener[] _idListenerScratch = new IUiElementListener[4];
    private int _globalDepth;
    private bool _broadcasting;
    private uint _messageSerial;

    /// <summary>The serial number of the message being delivered (retail's <c>s_tsMsgSerialNumber</c>); each element and listener hears a message once.</summary>
    internal uint CurrentMessageSerial { get; private set; }

    /// <summary>How many listeners carry a "heard" mark (0 between deliveries; tests).</summary>
    internal int HeardMarkCount => _heard.Count;

    /// <summary>
    /// <c>UIElementManager::BroadcastElementMessage</c> (11.4186 0x0045AB40, 11.6096 0x0045AC50): delivers
    /// <paramref name="message"/> to its element, which passes it on (<see cref="UiElement.ListenToElementMessage"/>). A
    /// message sent while one is being delivered waits and goes out after it, in order (and reports false). True when a
    /// listener took it.
    /// </summary>
    public bool BroadcastElementMessage(in UiElementMessage message)
    {
        if (_broadcasting)
        {
            _queuedMessages.Enqueue(message);
            return false;
        }

        _broadcasting = true;
        try
        {
            var taken = Deliver(message);
            while (_queuedMessages.TryDequeue(out var queued))
            {
                Deliver(queued);
            }

            return taken;
        }
        finally
        {
            _broadcasting = false;
        }
    }

    /// <summary>Sends message <paramref name="messageId"/> about <paramref name="element"/> (<see cref="BroadcastElementMessage(in UiElementMessage)"/>).</summary>
    public bool BroadcastElementMessage(UiElement element, uint messageId, uint param1, uint param2)
    {
        ArgumentNullException.ThrowIfNull(element);
        return BroadcastElementMessage(new UiElementMessage(element, messageId, param1, param2));
    }

    private bool Deliver(in UiElementMessage message)
    {
        CurrentMessageSerial = ++_messageSerial;
        try
        {
            ElementMessage?.Invoke(message);
            return message.Element.Receive(message) != UiListenResult.Default;
        }
        finally
        {
            // The marks only have to last one delivery; keeping them would keep every listener (and the element tree
            // behind it) alive for the manager's lifetime.
            if (_heard.Count != 0)
            {
                _heard.Clear();
            }
        }
    }

    /// <summary>
    /// True the first time <paramref name="listener"/> hears the message being delivered (<c>m_tsSerialNumberLastListenedTo</c>).
    /// The marks are dropped when the delivery ends.
    /// </summary>
    internal bool MarkHeard(IUiElementListener listener)
    {
        if (_heard.TryGetValue(listener, out var last) && last == CurrentMessageSerial)
        {
            return false;
        }

        _heard[listener] = CurrentMessageSerial;
        return true;
    }

    /// <summary>
    /// <c>UIElementManager::RegisterForElementMessage</c> (11.4186 0x0045F2A0, 11.6096 0x0045F380): <paramref name="listener"/>
    /// hears message <paramref name="messageId"/> of every element with id <paramref name="elementId"/> that reaches the
    /// top unconsumed. Registering for a mouse message (down, up, click or tap) makes the elements with that id take the
    /// mouse (<see cref="UiElement.SetMouseVisible"/>), as retail; registering for double clicks makes them hear double
    /// clicks as such (<see cref="UiElement.WantsDoubleClicks"/>, a Lifestone rule).
    /// </summary>
    public void RegisterForElementMessage(IUiElementListener listener, uint elementId, uint messageId)
    {
        ArgumentNullException.ThrowIfNull(listener);
        if (!_elementListeners.TryGetValue((elementId, messageId), out var list))
        {
            list = [];
            _elementListeners[(elementId, messageId)] = list;
        }

        if (!list.Contains(listener))
        {
            list.Add(listener);
        }

        if (messageId is UiMessageIds.MouseDown or UiMessageIds.MouseUp or UiMessageIds.Click or UiMessageIds.Tap or UiMessageIds.DoubleClick)
        {
            for (var i = _elements.Count - 1; i >= 0; i--)
            {
                var element = _elements[i];
                if (element.ElementId != elementId)
                {
                    continue;
                }

                if (messageId == UiMessageIds.DoubleClick)
                {
                    element.SetWantsDoubleClicks(true);
                }
                else
                {
                    element.SetMouseVisible(true);
                }
            }
        }
    }

    /// <summary><c>UIElementManager::UnRegisterForElementMessage</c>: the reverse of <see cref="RegisterForElementMessage"/>.</summary>
    public void UnregisterForElementMessage(IUiElementListener listener, uint elementId, uint messageId)
    {
        if (_elementListeners.TryGetValue((elementId, messageId), out var list))
        {
            list.Remove(listener);
        }
    }

    /// <summary>
    /// <c>UIElement::DefElementMessageHandler</c> (11.4186 0x0045FC30, 11.6096 0x0045FD10) over
    /// <c>UIElementManager::BroadcastElementMessage</c> by id (11.4186 0x0045B2C0, 11.6096 0x0045B3A0): the listeners
    /// registered for the message's element id and message hear it in turn, until one consumes it. True when one took it.
    /// </summary>
    internal bool DefElementMessageHandler(in UiElementMessage message)
    {
        if (!_elementListeners.TryGetValue((message.ElementId, message.MessageId), out var list) || list.Count == 0)
        {
            return false;
        }

        var count = list.Count;
        if (_idListenerScratch.Length < count)
        {
            _idListenerScratch = new IUiElementListener[count * 2];
        }

        list.CopyTo(_idListenerScratch);
        var taken = false;
        for (var i = 0; i < count; i++)
        {
            var listener = _idListenerScratch[i];
            _idListenerScratch[i] = null!;
            var result = listener.ListenToElementMessage(message);
            taken |= result != UiListenResult.Default;
            if (result == UiListenResult.Consumed)
            {
                Array.Clear(_idListenerScratch, i + 1, count - i - 1);
                break;
            }
        }

        return taken;
    }

    /// <summary>
    /// <c>UIElementManager::RegisterForGlobalMessage</c> (11.4186 0x0045E680, 11.6096 0x0045E760): <paramref name="listener"/>
    /// hears global message <paramref name="messageId"/> (<see cref="UiGlobalMessageIds"/>). Registering twice keeps one entry.
    /// </summary>
    public void RegisterForGlobalMessage(IUiGlobalListener listener, uint messageId)
    {
        ArgumentNullException.ThrowIfNull(listener);
        if (!_globalListeners.TryGetValue(messageId, out var list))
        {
            list = [];
            _globalListeners[messageId] = list;
        }

        if (!list.Contains(listener))
        {
            list.Add(listener);
        }
    }

    /// <summary><c>UIElementManager::UnRegisterForGlobalMessage</c>.</summary>
    public void UnregisterForGlobalMessage(IUiGlobalListener listener, uint messageId)
    {
        if (_globalListeners.TryGetValue(messageId, out var list))
        {
            list.Remove(listener);
        }
    }

    /// <summary>
    /// <c>UIElementManager::BroadcastGlobalMessage</c> (11.4186 0x0045B3E0, 11.6096 0x0045B4C0): every listener of
    /// <paramref name="messageId"/> hears it, in registration order; a listener that unregisters before its turn does not.
    /// A listener may broadcast a global message itself (a frame listener's callback starting an action): the inner
    /// broadcast has a scratch of its own, so the outer one goes on with its own listeners. Allocation-free once the
    /// scratches have grown.
    /// </summary>
    public void BroadcastGlobalMessage(uint messageId, int data)
    {
        if (!_globalListeners.TryGetValue(messageId, out var list) || list.Count == 0)
        {
            return;
        }

        var depth = _globalDepth;
        if (depth == _globalScratches.Count)
        {
            _globalScratches.Add(new IUiGlobalListener[8]);
        }

        var count = list.Count;
        var scratch = _globalScratches[depth];
        if (scratch.Length < count)
        {
            scratch = new IUiGlobalListener[count * 2];
            _globalScratches[depth] = scratch;
        }

        list.CopyTo(scratch);
        _globalDepth = depth + 1;
        try
        {
            for (var i = 0; i < count; i++)
            {
                var listener = scratch[i];
                scratch[i] = null!;
                if (list.Contains(listener))
                {
                    listener.ListenToGlobalMessage(messageId, data);
                }
            }
        }
        finally
        {
            Array.Clear(scratch, 0, count);
            _globalDepth = depth;
        }
    }

    /// <summary>
    /// <c>UIElementManager::RegisterElementForInputAction</c> (11.4186 0x0045D800, 11.6096 0x0045D8E0): the element hears
    /// <see cref="UiMessageIds.VisibilityToggleAction"/> when key action <paramref name="action"/> starts unclaimed (its
    /// property 0x57).
    /// </summary>
    public void RegisterElementForInputAction(UiElement element, uint action)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!_visibilityToggleActions.TryGetValue(action, out var list))
        {
            list = [];
            _visibilityToggleActions[action] = list;
        }

        if (!list.Contains(element))
        {
            list.Add(element);
        }
    }

    /// <summary><c>UIElementManager::UnRegisterElementForAllInputActions</c>.</summary>
    public void UnregisterElementForAllInputActions(UiElement element)
    {
        foreach (var list in _visibilityToggleActions.Values)
        {
            list.Remove(element);
        }
    }

    /// <summary>
    /// <c>UIElementManager::DoVisibilityToggleAction</c> (11.4186 0x0045B660, 11.6096 0x0045B740): every element registered
    /// for <paramref name="action"/> hears <see cref="UiMessageIds.VisibilityToggleAction"/>. True when there was one.
    /// </summary>
    public bool DoVisibilityToggleAction(uint action)
    {
        if (!_visibilityToggleActions.TryGetValue(action, out var list) || list.Count == 0)
        {
            return false;
        }

        foreach (var element in list.ToArray())
        {
            element.BroadcastElementMessage(UiMessageIds.VisibilityToggleAction, action);
        }

        return true;
    }

    private void UnregisterFromAllMessages(UiElement element)
    {
        foreach (var list in _globalListeners.Values)
        {
            list.Remove(element);
        }

        UnregisterElementForAllInputActions(element);
        Input.UnregisterCallback(element);
    }
}
