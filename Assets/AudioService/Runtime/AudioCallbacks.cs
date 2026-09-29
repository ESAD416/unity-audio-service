using System;
using System.Collections.Generic;
using UnityEngine;

namespace Controller.Audio
{
    internal static class AudioCallbacks
    {
        public static void Invoke<T>(Action<T> callback, T value)
        {
            try { callback?.Invoke(value); }
            catch (Exception error) { Debug.LogException(error); }
        }
        public static void Deliver(Action<AudioClipLease> callback, AudioClipLease lease)
        {
            try { if (callback != null) callback(lease); else lease?.Dispose(); }
            catch (Exception error) { lease?.Dispose(); Debug.LogException(error); }
        }
    }

    // Terminal state is immediate; user notifications wait until the enclosing
    // engine mutation finishes. Nested calls enqueue notifications without recursion.
    internal sealed class AudioCallbackQueue
    {
        private readonly Queue<Action> pending = new();
        private int depth;
        private bool dispatching;
        public Scope Begin() { depth++; return new Scope(this); }
        public void Enqueue(Action callback) { pending.Enqueue(callback); Drain(); }
        private void End() { depth--; Drain(); }
        private void Drain()
        {
            if (depth != 0 || dispatching) return;
            dispatching = true;
            try
            {
                while (pending.Count > 0)
                {
                    var callback = pending.Dequeue();
                    try { callback(); } catch (Exception error) { Debug.LogException(error); }
                }
            }
            finally { dispatching = false; }
        }
        public readonly struct Scope : IDisposable
        {
            private readonly AudioCallbackQueue queue;
            internal Scope(AudioCallbackQueue queue) { this.queue = queue; }
            public void Dispose() => queue.End();
        }
    }
}
