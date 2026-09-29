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
            public string Key;
            public AudioClipLease Lease;
            public bool Loading = true, Retained;
            public int Users;
            public readonly HashSet<string> Groups = new();
            public readonly List<Waiter> Waiters = new();
        }
        private readonly Dictionary<string, Entry> entries = new();
        private readonly MonoBehaviour host;
        private readonly IAudioClipProvider provider;
        private bool disposed;
        public int CachedCount { get { int n = 0; foreach (var e in entries.Values) if (e.Lease?.Clip != null) n++; return n; } }
        public int UserCount { get { int n = 0; foreach (var e in entries.Values) n += e.Users; return n; } }
        public int LoadingCount { get { int n = 0; foreach (var e in entries.Values) if (e.Loading) n++; return n; } }
        public AudioClipStore(MonoBehaviour host, IAudioClipProvider provider) { this.host = host; this.provider = provider; }
        private static string Key(AudioClipAddress address) => (int)address.Category + ":" + address.Id;
        public bool TryGetCached(AudioClipAddress address, out AudioClip clip)
        {
            clip = null;
            if (entries.TryGetValue(Key(address), out var entry)) clip = entry.Lease?.Clip;
            return clip != null;
        }
        public Action Request(AudioClipAddress address, bool allowAsync, Action<AudioClipLease> callback, string group = null)
        {
            if (disposed || !AudioValues.Alive(provider)) { callback(null); return () => { }; }
            var key = Key(address);
            bool created = !entries.TryGetValue(key, out var entry);
            if (!created && entry.Loading && !allowAsync) { callback(null); return () => { }; }
            if (created) { entry = new Entry { Key = key }; entries.Add(key, entry); }
            if (group != null) entry.Groups.Add(group);
            else entry.Retained = true;
            var waiter = new Waiter { Callback = callback, Group = group };
            entry.Waiters.Add(waiter);
            if (!entry.Loading) Deliver(entry);
            else if (created)
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
                try { delivery.callback(delivery.lease); }
                catch (Exception error) { delivery.lease?.Dispose(); Debug.LogException(error); }
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
            foreach (var entry in new List<Entry>(entries.Values))
            {
                entry.Groups.Remove(group);
                foreach (var waiter in entry.Waiters.ToArray()) if (waiter.Group == group) { var callback = waiter.Callback; waiter.Callback = null; callback?.Invoke(null); }
                Cleanup(entry);
            }
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
                foreach (var waiter in entry.Waiters.ToArray()) { var callback = waiter.Callback; waiter.Callback = null; callback?.Invoke(null); }
                entry.Waiters.Clear(); entry.Groups.Clear(); entry.Retained = false; Cleanup(entry);
            }
            entries.Clear();
        }
    }
}
