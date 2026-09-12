using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generic object pool. Accepts a factory function so it can create both prefab
/// instances and procedurally-built GameObjects (fallback cubes, etc.).
/// Pool.Return() is the only path that calls SetActive(false) — callers must NOT
/// deactivate a pooled object themselves.
/// </summary>
public class ObjectPool
{
    private readonly Func<GameObject>  _factory;
    private readonly Queue<GameObject> _free = new();
    private readonly List<GameObject>  _all  = new();
    private readonly int               _maxSize;

    public string Label     { get; }
    public int    FreeCount => _free.Count;
    public int    UsedCount => _all.Count - _free.Count;
    public int    TotalCount => _all.Count;

    public ObjectPool(string label, Func<GameObject> factory, int initialSize, int maxSize)
    {
        Label    = label;
        _factory = factory;
        _maxSize = maxSize;

        for (int i = 0; i < initialSize; i++) Grow();
    }

    /// <summary>Returns an inactive instance, activates it, then returns it to the caller.</summary>
    public GameObject Get()
    {
        if (_free.Count == 0)
        {
            if (_all.Count >= _maxSize)
            {
                Debug.LogWarning($"[Pool:{Label}] exhausted (max {_maxSize}). Expanding by 1.");
                // Allow one extra grow rather than returning null — avoids dropped events
                if (_all.Count >= _maxSize * 2) return null; // hard cap
            }
            Grow();
        }
        var go = _free.Dequeue();
        // Caller is responsible for setting position and calling SetActive(true)
        return go;
    }

    /// <summary>Returns a GO to the pool. Safe to call if already inactive.</summary>
    public void Return(GameObject go)
    {
        if (go == null) return;
        if (!go.activeSelf) { _free.Enqueue(go); return; } // already inactive, just re-queue
        go.SetActive(false);
        _free.Enqueue(go);
    }

    private void Grow()
    {
        var go = _factory();
        go.SetActive(false);
        _free.Enqueue(go);
        _all.Add(go);
    }
}
