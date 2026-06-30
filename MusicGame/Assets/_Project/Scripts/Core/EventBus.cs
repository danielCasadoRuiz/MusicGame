using System;
using System.Collections.Generic;

public static class EventBus
{
    private static readonly Dictionary<Type, Delegate> _handlers = new();

    public static void Subscribe<T>(Action<T> handler)
    {
        var type = typeof(T);
        _handlers[type] = _handlers.TryGetValue(type, out var existing)
            ? Delegate.Combine(existing, handler)
            : handler;
    }

    public static void Unsubscribe<T>(Action<T> handler)
    {
        var type = typeof(T);
        if (!_handlers.TryGetValue(type, out var existing)) return;
        var updated = Delegate.Remove(existing, handler);
        if (updated == null) _handlers.Remove(type);
        else _handlers[type] = updated;
    }

    public static void Publish<T>(T eventData)
    {
        if (_handlers.TryGetValue(typeof(T), out var handler))
            ((Action<T>)handler).Invoke(eventData);
    }

    public static void Clear() => _handlers.Clear();
}
