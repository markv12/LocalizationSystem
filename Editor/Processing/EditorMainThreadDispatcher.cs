using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// EditorApplication.delayCall is not thread-safe, so background upload/status tasks queue here
// and EditorApplication.update drains on the main thread.
public static class EditorMainThreadDispatcher {
    private static readonly Queue<Action> _queue = new Queue<Action>();

    [InitializeOnLoadMethod]
    private static void Init() {
        EditorApplication.update -= Drain;
        EditorApplication.update += Drain;
    }

    public static void Enqueue(Action action) {
        if (action == null) return;
        lock (_queue) {
            _queue.Enqueue(action);
        }
    }

    private static void Drain() {
        while (true) {
            Action action;
            lock (_queue) {
                if (_queue.Count == 0) return;
                action = _queue.Dequeue();
            }
            try {
                action();
            } catch (Exception e) {
                Debug.LogException(e);
            }
        }
    }
}
