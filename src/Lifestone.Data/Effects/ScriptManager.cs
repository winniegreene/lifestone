using Lifestone.Data.Animation;

namespace Lifestone.Data.Effects;

/// <summary>
/// Plays PhysicsScripts on one object (retail <c>ScriptManager</c>, acclient 11.4186 PORTAL/cphysobj/CScriptManager.c:
/// <c>AddScriptInternal</c>, <c>NextHook</c>, <c>UpdateScripts</c>).
/// </summary>
/// <remarks>
/// Scripts are <em>queued</em>, not overlapped: a script added while another is pending starts at
/// <c>previous.start + previous.length</c> (<c>AddScriptInternal</c>); a script added to an empty queue starts now.
/// A hook fires once the clock reaches <c>script.start + hook.start_time</c>; several due hooks fire in one update.
/// After the last hook the next-hook time becomes −1, so the following update moves on to the next script.
/// </remarks>
public sealed class ScriptManager
{
    private sealed class ScriptData(double startTime, PhysicsScriptData script)
    {
        public double StartTime { get; } = startTime;

        public PhysicsScriptData Script { get; } = script;
    }

    private readonly LinkedList<ScriptData> _queue = new();
    private int _hookIndex = -1;
    private double _nextHookTime;

    /// <summary>Scripts queued or playing, with their start times.</summary>
    public IEnumerable<(uint ScriptId, double StartTime)> Queue => _queue.Select(s => (s.Script.Id, s.StartTime));

    public bool IsIdle => _queue.First is null;

    /// <summary>Retail <c>ScriptManager::AddScriptInternal</c>.</summary>
    public void AddScript(PhysicsScriptData script, double now)
    {
        var last = _queue.Last?.Value;
        var data = new ScriptData(last is null ? now : last.Script.Length + last.StartTime, script);
        _queue.AddLast(data);
        if (last is null)
        {
            _hookIndex = -1;
            _nextHookTime = FirstHookTime(data);
        }
    }

    /// <summary>Retail <c>ScriptManager::UpdateScripts</c>: fires every hook that is due at <paramref name="now"/>.</summary>
    public void UpdateScripts(double now, Action<AnimHook> execute)
    {
        while (_queue.First is { } current && now >= _nextHookTime)
        {
            var hook = NextHook(current);
            if (hook is not null)
            {
                execute(hook);
                continue;
            }

            _queue.RemoveFirst();
            _hookIndex = -1;
            _nextHookTime = _queue.First is { } next ? FirstHookTime(next.Value) : -1.0;
        }
    }

    /// <summary>Retail <c>ScriptManager::NextHook</c>.</summary>
    private AnimHook? NextHook(LinkedListNode<ScriptData> current)
    {
        _hookIndex++;
        var hooks = current.Value.Script.Hooks;
        if (_hookIndex >= hooks.Count)
        {
            return null;
        }

        if (_hookIndex + 1 >= hooks.Count)
        {
            _nextHookTime = current.Next is { } next ? FirstHookTime(next.Value) : -1.0;
        }
        else
        {
            _nextHookTime = hooks[_hookIndex + 1].StartTime + current.Value.StartTime;
        }

        return hooks[_hookIndex].Hook;
    }

    /// <summary>
    /// <c>data[0]-&gt;start_time + start</c>. Retail dereferences the first entry unconditionally (it would crash on an
    /// empty script); an empty script here is due immediately and is skipped.
    /// </summary>
    private static double FirstHookTime(ScriptData data) =>
        data.Script.Hooks.Count > 0 ? data.Script.Hooks[0].StartTime + data.StartTime : data.StartTime;
}
