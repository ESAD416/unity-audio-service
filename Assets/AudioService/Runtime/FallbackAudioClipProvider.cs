using System;
using System.Collections;
using UnityEngine;

namespace Controller.Audio
{
    public class FallbackAudioClipProvider : MonoBehaviour, IResultAudioClipProvider, IAudioClipLeaseProvider, IAudioSynchronousClipProvider, IAudioClipProviderChanges, IAudioClipProviderRefresh
    {
        [SerializeField] private MonoBehaviour mainProvider;
        [SerializeField] private MonoBehaviour backupProvider;
        [SerializeField] private AudioFallbackPolicy policy = AudioFallbackPolicy.PreferAvailable;
        private IAudioClipProvider main, backup;
        private int generation;
        private readonly System.Collections.Generic.List<Action> cancellations = new();
        private readonly AudioCallbackQueue callbacks = new();
        public event Action Changed;
        public AudioFallbackPolicy Policy
        { get => policy; set { if (policy == value) return; policy = value; generation++; Changed?.Invoke(); } }
        private void Awake()
        {
            if (mainProvider == null) mainProvider = Ensure<AddressablesAudioClipProvider>("AddressablesProvider");
            if (backupProvider == null) backupProvider = Ensure<ResourcesAudioClipProvider>("ResourcesProvider");
            main = mainProvider as IAudioClipProvider; backup = backupProvider as IAudioClipProvider;
        }
        public void Configure(IAudioClipProvider primary, IAudioClipProvider secondary, AudioFallbackPolicy selection)
        { generation++; main = primary; backup = secondary; policy = selection; Changed?.Invoke(); }
        internal string DescribeLookup(AudioClipAddress address, int depth)
            => $"Fallback {policy}; primary [{AudioFailureLog.DescribeProvider(main, address, depth + 1)}]; backup [{AudioFailureLog.DescribeProvider(backup, address, depth + 1)}]";
        private T Ensure<T>(string childName) where T : MonoBehaviour
        {
            var child = transform.Find(childName);
            if (child == null) { var go = new GameObject(childName); go.transform.SetParent(transform, false); child = go.transform; }
            var component = child.GetComponent<T>(); return component != null ? component : child.gameObject.AddComponent<T>();
        }
        public AudioClip GetClip(AudioCategory category, string key) { TryGetClip(category, key, out var clip); return clip; }
        private static bool Cached(IAudioClipProvider provider, AudioCategory category, string key, out AudioClip clip)
        { clip = null; return AudioValues.Alive(provider) && provider is IAudioClipCache cache && cache.TryGetCachedClip(category, key, out clip); }
        public bool TryGetCachedClip(AudioCategory category, string key, out AudioClip clip)
        {
            if (Cached(main, category, key, out clip)) return true;
            return policy == AudioFallbackPolicy.PreferAvailable && Cached(backup, category, key, out clip);
        }
        public bool TryGetClip(AudioCategory category, string key, out AudioClip clip)
        {
            if (TryGetCachedClip(category, key, out clip)) return true;
            return policy == AudioFallbackPolicy.PreferAvailable && AudioValues.Alive(backup) && backup.TryGetClip(category, key, out clip);
        }
        public bool IsCached(AudioCategory category, string key) => TryGetCachedClip(category, key, out _);
        public bool IsLoading(AudioCategory category, string key) =>
            (AudioValues.Alive(main) && main is IAsyncAudioClipProvider a && a.IsLoading(category, key)) ||
            (AudioValues.Alive(backup) && backup is IAsyncAudioClipProvider b && b.IsLoading(category, key));
        public bool TryAcquireClip(AudioClipAddress address, out AudioClipLease lease)
        {
            lease = null;
            if (AudioValues.Alive(main) && main is IAudioSynchronousClipProvider primary && primary.TryAcquireClip(address, out lease)) return true;
            return policy == AudioFallbackPolicy.PreferAvailable && AudioValues.Alive(backup) && backup is IAudioSynchronousClipProvider secondary && secondary.TryAcquireClip(address, out lease);
        }
        public void AcquireClip(AudioClipAddress address, Action<AudioClipLease> completed)
        {
            using var mutation = callbacks.Begin();
            int version = generation;
            bool finished = false;
            Action cancel = null;
            bool Valid() => !finished && this != null && generation == version;
            void Finish(AudioClipLease lease)
            {
                using var delivery = callbacks.Begin();
                if (finished) { callbacks.Release(lease); return; }
                bool valid = Valid();
                finished = true; cancellations.Remove(cancel);
                if (!valid) { callbacks.Release(lease); lease = null; }
                callbacks.Enqueue(() => AudioCallbacks.Deliver(completed, lease));
            }
            cancel = () => Finish(null); cancellations.Add(cancel);
            void LoadMain()
            {
                var primary = main;
                Acquire(primary, address, lease =>
                {
                    if (!AudioValues.Alive(primary)) { AudioCallbacks.Dispose(lease); lease = null; }
                    if (!Valid()) Finish(lease);
                    else if (lease?.Clip != null) Finish(lease);
                    else { AudioCallbacks.Dispose(lease); Acquire(backup, address, Finish); }
                });
            }
            if (policy == AudioFallbackPolicy.PreferAvailable)
            {
                if (Cached(main, address.Category, address.AddressablesKey ?? address.Id, out _)) { Acquire(main, address, Finish); return; }
                // Resources.Load is an explicit load here, never disguised as a cache probe.
                if (AudioValues.Alive(backup) && (backup is ResourcesAudioClipProvider || Cached(backup, address.Category, address.ResourcesKey ?? address.Id, out _)))
                { Acquire(backup, address, lease => { if (lease?.Clip != null) Finish(lease); else { AudioCallbacks.Dispose(lease); if (Valid()) LoadMain(); else Finish(null); } }); return; }
            }
            LoadMain();
        }
        private void Acquire(IAudioClipProvider provider, AudioClipAddress address, Action<AudioClipLease> completed)
        {
            if (!AudioValues.Alive(provider)) { completed(null); return; }
            if (provider is IAudioClipLeaseProvider leases) leases.AcquireClip(address, completed);
            else StartCoroutine(AudioAsync.AcquireLegacy(provider, address, completed));
        }
        public IEnumerator LoadClipAsync(AudioCategory category, string key) { yield return LoadClipAsync(category, key, null); }
        public IEnumerator LoadClipAsync(AudioCategory category, string key, Action<AudioClip> completed)
        {
            bool done = false; AudioClipLease lease = null;
            AcquireClip(new AudioClipAddress(key, category), result => { lease = result; done = true; });
            while (!done) yield return null;
            // Legacy callers have no lease: retain through the provider's legacy load cache.
            var clip = lease?.Clip;
            if (clip != null && AudioValues.Alive(main) && main is IAsyncAudioClipProvider async && async.IsCached(category, key))
                yield return async.LoadClipAsync(category, key);
            try { AudioCallbacks.Invoke(completed, clip); }
            finally { AudioCallbacks.Dispose(lease); }
        }
        public void ReleaseClip(AudioCategory category, string key)
        {
            if (AudioValues.Alive(main) && main is IAsyncAudioClipProvider a) a.ReleaseClip(category, key);
            if (AudioValues.Alive(backup) && backup is IAsyncAudioClipProvider b) b.ReleaseClip(category, key);
        }
        public void RefreshClipLookup()
        {
            using var mutation = callbacks.Begin();
            CancelPending();
            if (AudioValues.Alive(main) && main is IAudioClipProviderRefresh primary) AudioCallbacks.Invoke(primary.RefreshClipLookup);
            if (!ReferenceEquals(main, backup) && AudioValues.Alive(backup) && backup is IAudioClipProviderRefresh secondary) AudioCallbacks.Invoke(secondary.RefreshClipLookup);
        }
        private void CancelPending()
        {
            generation++;
            var pending = cancellations.ToArray(); cancellations.Clear();
            foreach (var cancel in pending) AudioCallbacks.Invoke(cancel);
        }
        private void OnDisable() { using var mutation = callbacks.Begin(); CancelPending(); }
        private void OnDestroy() => OnDisable();
    }
}
