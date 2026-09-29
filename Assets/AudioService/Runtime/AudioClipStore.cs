using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Controller.Audio
{
    internal sealed class AudioClipStore
    {
        private sealed class Waiter { public Action<AudioClipLease> Callback; public string Group; }
        private sealed class Entry
        {
            public (AudioCategory Category, string Id) Key;
            public AudioClipLease Lease;
            public bool Loading = true, Retained;
            public int Users;
            public readonly HashSet<string> Groups = new();
            public readonly List<Waiter> Waiters = new();
        }
        private static readonly Action NoCancellation = () => { };
        private readonly Dictionary<(AudioCategory, string), Entry> entries = new();
        private readonly MonoBehaviour host;
        private readonly IAudioClipProvider provider;
        private bool disposed;
        public int CachedCount { get { int n = 0; foreach (var e in entries.Values) if (e.Lease?.Clip != null) n++; return n; } }
        public int UserCount { get { int n = 0; foreach (var e in entries.Values) n += e.Users; return n; } }
        public int LoadingCount { get { int n = 0; foreach (var e in entries.Values) if (e.Loading) n++; return n; } }
        public AudioClipStore(MonoBehaviour host, IAudioClipProvider provider) { this.host = host; this.provider = provider; }
        private static (AudioCategory, string) Key(AudioClipAddress address) => (address.Category, address.Id);
        public bool TryGetCached(AudioClipAddress address, out AudioClip clip)
        {
            clip = null;
            if (entries.TryGetValue(Key(address), out var entry)) clip = entry.Lease?.Clip;
            return clip != null;
        }
        public Action Request(AudioClipAddress address, bool allowAsync, Action<AudioClipLease> callback, string group = null)
        {
            if (disposed || !AudioValues.Alive(provider)) { AudioCallbacks.Deliver(callback, null); return NoCancellation; }
            var key = Key(address);
            bool created = !entries.TryGetValue(key, out var entry);
            // A failed delivery can synchronously retry before its old entry is cleaned up.
            if (!created && !entry.Loading && entry.Lease?.Clip == null) created = true;
            if (!created && entry.Loading && !allowAsync) { AudioCallbacks.Deliver(callback, null); return NoCancellation; }
            if (created) { entry = new Entry { Key = key }; entries[key] = entry; }
            if (group != null) entry.Groups.Add(group);
            else entry.Retained = true;
            if (!entry.Loading)
            {
                // Resident requests have no pending work and need no waiter/list/cancel closure.
                entry.Users++;
                AudioCallbacks.Deliver(callback, new AudioClipLease(entry.Lease.Clip, () => { entry.Users--; Cleanup(entry); }));
                return NoCancellation;
            }
            var waiter = new Waiter { Callback = callback, Group = group };
            entry.Waiters.Add(waiter);
            if (created)
            {
                try
                {
                    if (!allowAsync)
                    {
                        if (provider is IAudioSynchronousClipProvider synchronous)
                        { synchronous.TryAcquireClip(address, out var lease); Complete(entry, lease); }
                        else
                        {
                            AudioClip clip = null;
                            if (provider is IAudioClipCache cache) cache.TryGetCachedClip(address.Category, address.Id, out clip);
                            else provider.TryGetClip(address.Category, address.Id, out clip);
                            Complete(entry, clip != null ? new AudioClipLease(clip) : null);
                        }
                    }
                    else if (provider is IAudioClipLeaseProvider leases)
                        leases.AcquireClip(address, result => Complete(entry, result));
                    else host.StartCoroutine(LoadLegacy(entry, address));
                }
                catch (Exception) { Complete(entry, null); }
            }
            return () => { waiter.Callback = null; Cleanup(entry); };
        }
        private IEnumerator LoadLegacy(Entry entry, AudioClipAddress address)
        {
            yield return AudioAsync.AcquireLegacy(provider, address, lease => Complete(entry, lease));
        }
        private void Complete(Entry entry, AudioClipLease result)
        {
            if (!entry.Loading) { result?.Dispose(); return; }
            entry.Loading = false; entry.Lease = result;
            if (disposed) { result?.Dispose(); entry.Lease = null; return; }
            Deliver(entry); Cleanup(entry);
        }
        private void Deliver(Entry entry)
        {
            var waiting = entry.Waiters.ToArray(); entry.Waiters.Clear();
            // Reserve all delivery leases before invoking callbacks. A reentrant
            // ReleaseUnused/Stop from the first waiter cannot invalidate the rest.
            var deliveries = new List<(Action<AudioClipLease> callback, AudioClipLease lease)>();
            foreach (var waiter in waiting)
            {
                var callback = waiter.Callback; waiter.Callback = null;
                if (callback == null) continue;
                AudioClipLease lease = null;
                if (!disposed && entry.Lease?.Clip != null)
                {
                    entry.Users++;
                    lease = new AudioClipLease(entry.Lease.Clip, () => { entry.Users--; Cleanup(entry); });
                }
                deliveries.Add((callback, lease));
            }
            foreach (var delivery in deliveries)
            {
                AudioCallbacks.Deliver(delivery.callback, delivery.lease);
            }
        }

        private void Cleanup(Entry entry)
        {
            if (entry.Loading || entry.Users > 0) return;
            if (!disposed && entry.Lease?.Clip != null && (entry.Retained || entry.Groups.Count > 0)) return;
            if (entries.TryGetValue(entry.Key, out var current) && ReferenceEquals(entry, current)) entries.Remove(entry.Key);
            entry.Lease?.Dispose(); entry.Lease = null;
        }
        public void ReleaseGroup(string group)
        {
            var cancelled = new List<Action<AudioClipLease>>();
            foreach (var entry in new List<Entry>(entries.Values))
            {
                entry.Groups.Remove(group);
                foreach (var waiter in entry.Waiters)
                    if (waiter.Group == group && waiter.Callback != null)
                    { cancelled.Add(waiter.Callback); waiter.Callback = null; }
                Cleanup(entry);
            }
            // Snapshot all cancelled requests before callbacks can start new ones.
            foreach (var callback in cancelled) AudioCallbacks.Deliver(callback, null);
        }
        public void ReleaseUnused()
        {
            foreach (var entry in new List<Entry>(entries.Values)) { entry.Retained = false; Cleanup(entry); }
        }
        public void Dispose()
        {
            disposed = true;
            foreach (var entry in new List<Entry>(entries.Values))
            {
                foreach (var waiter in entry.Waiters.ToArray()) { var callback = waiter.Callback; waiter.Callback = null; AudioCallbacks.Deliver(callback, null); }
                entry.Waiters.Clear(); entry.Groups.Clear(); entry.Retained = false; Cleanup(entry);
            }
            entries.Clear();
        }
    }
}
