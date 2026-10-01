using System;
using System.Collections.Generic;
using UnityEngine;

namespace Controller.Audio
{
    internal sealed class AudioClipStore
    {
        internal sealed class Waiter
        {
            public Action<AudioClipLease> Callback;
            public string Group;
        }

        internal sealed class Entry
        {
            public (AudioCategory Category, string Id) Key;
            public AudioClipLease Lease;
            public bool Loading = true, Retained;
            public int Users;
            public readonly HashSet<string> Groups = new();
            public readonly List<Waiter> Waiters = new();
        }

        // Internal playback owns this value in exactly one field. Clear that field
        // before returning the user so reentrant cleanup cannot release it twice.
        internal struct Usage : IDisposable
        {
            private AudioClipStore owner;
            private Entry entry;
            internal Usage(AudioClipStore owner, Entry entry)
            {
                this.owner = owner;
                this.entry = entry;
            }

            public AudioClip Clip => entry?.Lease?.Clip;

            public void Dispose()
            {
                var store = owner;
                var retained = entry;
                this = default;
                if (retained == null)
                    return;
                retained.Users--;
                store.Cleanup(retained);
            }
        }

        private static readonly Action NoCancellation = () =>
        {
        };
        private readonly Dictionary<(AudioCategory, string), Entry> entries = new();
        private readonly IAudioClipProvider provider;
        private readonly AudioCallbackQueue callbacks;
        private bool disposed;
        public void GetCounts(out int cached, out int users, out int loading)
        {
            cached = users = loading = 0;
            foreach (var entry in entries.Values)
            {
                if (entry.Lease?.Clip != null)
                    cached++;
                users += entry.Users;
                if (entry.Loading)
                    loading++;
            }
        }

        public AudioClipStore(IAudioClipProvider provider, AudioCallbackQueue callbacks)
        {
            this.provider = provider;
            this.callbacks = callbacks;
        }

        private static (AudioCategory, string) Key(ResolvedAudioClip address) => (address.Category, address.Id);
        public bool TryGetCached(ResolvedAudioClip address, out AudioClip clip)
        {
            clip = null;
            if (entries.TryGetValue(Key(address), out var entry))
                clip = entry.Lease?.Clip;
            return clip != null;
        }

        public bool TryAcquireResident(AudioCategory category, string id, out Usage usage)
        {
            usage = default;
            if (disposed || !AudioValues.Alive(provider) || !entries.TryGetValue((category, id), out var entry) || entry.Loading || entry.Lease?.Clip == null)
                return false;
            // Ordinary playback retains the entry exactly as Request(group: null).
            entry.Retained = true;
            entry.Users++;
            usage = new Usage(this, entry);
            return true;
        }

        private AudioClipLease AcquireUser(Entry entry)
        {
            entry.Users++;
            return new AudioClipLease(entry.Lease.Clip, () =>
            {
                entry.Users--;
                Cleanup(entry);
            });
        }

        public Action Request(ResolvedAudioClip address, bool allowAsync, Action<AudioClipLease> callback, string group = null)
        {
            using var mutation = callbacks.Begin();
            if (disposed || !AudioValues.Alive(provider))
            {
                AudioCallbacks.Deliver(callback, null);
                return NoCancellation;
            }

            var key = Key(address);
            bool created = !entries.TryGetValue(key, out var entry);
            // A failed delivery can synchronously retry before its old entry is cleaned up.
            if (!created && !entry.Loading && entry.Lease?.Clip == null)
                created = true;
            if (!created && entry.Loading && !allowAsync)
            {
                AudioCallbacks.Deliver(callback, null);
                return NoCancellation;
            }

            if (created)
            {
                entry = new Entry
                {
                    Key = key
                };
                entries[key] = entry;
            }

            if (group != null)
                entry.Groups.Add(group);
            else
                entry.Retained = true;
            if (!entry.Loading)
            {
                // Resident requests have no pending work and need no waiter/list/cancel closure.
                AudioCallbacks.Deliver(callback, AcquireUser(entry));
                return NoCancellation;
            }

            var waiter = new Waiter
            {
                Callback = callback,
                Group = group
            };
            entry.Waiters.Add(waiter);
            if (created)
            {
                try
                {
                    if (!allowAsync)
                    {
                        provider.TryAcquireClip(address, out var lease);
                        Complete(entry, lease);
                    }
                    else
                        provider.AcquireClip(address, result => Complete(entry, result));
                }
                catch (Exception)
                {
                    Complete(entry, null);
                }
            }

            return () =>
            {
                waiter.Callback = null;
                Cleanup(entry);
            };
        }

        private void Complete(Entry entry, AudioClipLease result)
        {
            using var mutation = callbacks.Begin();
            if (!entry.Loading)
            {
                callbacks.Release(result);
                return;
            }

            entry.Loading = false;
            entry.Lease = result;
            if (disposed)
            {
                entry.Lease = null;
                callbacks.Release(result);
                return;
            }

            Deliver(entry);
            Cleanup(entry);
        }

        private void Deliver(Entry entry)
        {
            // Reserve all delivery leases before invoking callbacks. A reentrant
            // ReleaseUnused/Stop from the first waiter cannot invalidate the rest.
            var deliveries = new (Action<AudioClipLease> callback, AudioClipLease lease)[entry.Waiters.Count];
            for (int i = 0; i < entry.Waiters.Count; i++)
            {
                var waiter = entry.Waiters[i];
                var callback = waiter.Callback;
                waiter.Callback = null;
                if (callback == null)
                    continue;
                AudioClipLease lease = null;
                if (!disposed && entry.Lease?.Clip != null)
                    lease = AcquireUser(entry);
                deliveries[i] = (callback, lease);
            }

            entry.Waiters.Clear();
            foreach (var delivery in deliveries)
                if (delivery.callback != null)
                    AudioCallbacks.Deliver(delivery.callback, delivery.lease);
        }

        private void Cleanup(Entry entry)
        {
            if (entry.Loading || entry.Users > 0)
                return;
            if (!disposed && entry.Lease?.Clip != null && (entry.Retained || entry.Groups.Count > 0))
                return;
            if (entries.TryGetValue(entry.Key, out var current) && ReferenceEquals(entry, current))
                entries.Remove(entry.Key);
            var lease = entry.Lease;
            entry.Lease = null;
            callbacks.Release(lease);
        }

        public void ReleaseGroup(string group)
        {
            using var mutation = callbacks.Begin();
            var cancelled = new List<Action<AudioClipLease>>();
            foreach (var entry in new List<Entry>(entries.Values))
            {
                entry.Groups.Remove(group);
                foreach (var waiter in entry.Waiters)
                    if (waiter.Group == group && waiter.Callback != null)
                    {
                        cancelled.Add(waiter.Callback);
                        waiter.Callback = null;
                    }

                Cleanup(entry);
            }

            // Snapshot all cancelled requests before callbacks can start new ones.
            foreach (var callback in cancelled)
                AudioCallbacks.Deliver(callback, null);
        }

        public void ReleaseUnused()
        {
            using var mutation = callbacks.Begin();
            foreach (var entry in new List<Entry>(entries.Values))
            {
                entry.Retained = false;
                Cleanup(entry);
            }
        }

        public void Dispose()
        {
            using var mutation = callbacks.Begin();
            disposed = true;
            foreach (var entry in new List<Entry>(entries.Values))
            {
                foreach (var waiter in entry.Waiters.ToArray())
                {
                    var callback = waiter.Callback;
                    waiter.Callback = null;
                    AudioCallbacks.Deliver(callback, null);
                }

                entry.Waiters.Clear();
                entry.Groups.Clear();
                entry.Retained = false;
                Cleanup(entry);
            }

            entries.Clear();
        }
    }
}
