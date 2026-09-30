using System;
using System.Collections.Generic;
using UnityEngine;

namespace Controller.Audio
{
    internal static class AudioCallbacks
    {
        public static void Invoke(Action callback)
        {
            try { callback?.Invoke(); }
            catch (Exception error) { Debug.LogException(error); }
        }
        public static void Dispose(AudioClipLease lease)
        {
            try { lease?.Dispose(); }
            catch (Exception error) { Debug.LogException(error); }
        }
        public static void Invoke<T>(Action<T> callback, T value)
        {
            try { callback?.Invoke(value); }
            catch (Exception error) { Debug.LogException(error); }
        }
        public static void Deliver(Action<AudioClipLease> callback, AudioClipLease lease)
        {
            try { if (callback != null) callback(lease); else Dispose(lease); }
            catch (Exception error) { Dispose(lease); Debug.LogException(error); }
        }
    }

    // Terminal state and store bookkeeping are immediate. Provider-owned leases
    // and user notifications drain only after the enclosing mutation has committed.
    internal sealed class AudioCallbackQueue
    {
        private readonly Queue<Action> pending = new();
        private readonly Queue<AudioClipLease> releases = new();
        private int depth;
        private bool dispatching;
        public Scope Begin() { depth++; return new Scope(this); }
        public void Enqueue(Action callback) { pending.Enqueue(callback); Drain(); }
        public void Release(AudioClipLease lease)
        { if (lease == null) return; releases.Enqueue(lease); Drain(); }
        private void End() { depth--; Drain(); }
        private void Drain()
        {
            if (depth != 0 || dispatching) return;
            dispatching = true;
            try
            {
                while (releases.Count > 0 || pending.Count > 0)
                {
                    // Releasing a provider lease can reenter Play or Shutdown.
                    // Each nested operation sees committed state and drains here.
                    if (releases.Count > 0) { AudioCallbacks.Dispose(releases.Dequeue()); continue; }
                    var callback = pending.Dequeue();
                    AudioCallbacks.Invoke(callback);
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
